using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Cars
{
	public static class CarLocks
	{
		public const float CarLockSeconds = 30f;

		public sealed class Lock
		{
			public int Id;
			public int LinkedId;
			public int Owner;
			public int Loader;
			public int SpawnSeq;
			public CarLockKind Kind;
			public byte Phase;
			public List<string> X = new List<string>();
			public List<string> S = new List<string>();
			public List<long> Items = new List<long>();
			public Dictionary<string, bool> StartUnmounted = new Dictionary<string, bool>();
			public float Since;
			public float RenewedAt;
		}

		public sealed class Refused
		{
			public CarLockRefusal Refusal;
			public int Holder = -1;
			public string Key;
		}

		private sealed class Holders
		{
			public int X;
			public readonly HashSet<int> S = new HashSet<int>();
			public bool Empty => X == 0 && S.Count == 0;
		}

		private static readonly Dictionary<int, Lock> locks = new Dictionary<int, Lock>();
		private static readonly Dictionary<int, Dictionary<string, Holders>> index = new Dictionary<int, Dictionary<string, Holders>>();
		private static readonly Dictionary<long, int> items = new Dictionary<long, int>();
		private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
		private static int nextId = 1;
		private static bool initialized;

		public static string Scope { get; set; } = LockScope.Connected;
		public static float ExpirySeconds { get; set; } = 90f;

		public static IEnumerable<Lock> All => locks.Values;

		public static int Counter(string name) => counters.TryGetValue(name, out int n) ? n : 0;

		private static void Count(string name) => counters[name] = Counter(name) + 1;

		public static void Initialize(string scope, int expirySeconds)
		{
			Scope = scope == LockScope.Part ? LockScope.Part : LockScope.Connected;
			ExpirySeconds = expirySeconds;
			if (initialized) return;
			initialized = true;
			PresenceEvents.Left += clientId => ReleaseOwner(clientId, "owner left");
			PresenceEvents.SceneChanged += (clientId, from, to) =>
			{
				if (from == GameScene.Garage) ReleaseOwner(clientId, $"owner left the garage for {to}");
			};
			CarPartsStore.LoaderCleared += (loader, _, reason) => ReleaseLoader(loader, $"car cleared ({reason})");
		}

		public static void Reset()
		{
			locks.Clear();
			index.Clear();
			items.Clear();
			counters.Clear();
			nextId = 1;
		}

		public static void Handle(int clientId, CarLockRequestPacket request, float now)
		{
			Count("requested");
			var refused = TryGrant(clientId, request, now, out var granted);
			if (refused != null)
			{
				Count($"denied.{refused.Refusal}");
				Logger.Info($"[Locks] Request {request.RequestId} by client {clientId} on loader {request.CarLoaderID} ({request.Kind}) denied: {refused.Refusal} {refused.Key} held by {refused.Holder}.");
				Server.SendToClient(new CarLockResultPacket
				{
					RequestId = request.RequestId, Granted = false, Refusal = refused.Refusal, HolderPlayerId = refused.Holder, ConflictKey = refused.Key
				}, clientId);
				return;
			}
			Count("granted");
			Server.SendToClient(new CarLockResultPacket { RequestId = request.RequestId, LockId = granted[0].Id, Granted = true }, clientId);
			foreach (var record in granted) Server.SendToClients(Update(record));
			Audit();
		}

		public static Refused TryGrant(int clientId, CarLockRequestPacket request, float now, out List<Lock> granted)
		{
			granted = null;
			Lock extended = null;
			if (request.ExtendLockId != 0)
			{
				if (!locks.TryGetValue(request.ExtendLockId, out extended) || extended.Owner != clientId || extended.Loader != request.CarLoaderID)
					return new Refused { Refusal = CarLockRefusal.Stale };
			}

			var entry = CarPartsStore.Get(request.CarLoaderID);
			if (entry == null || entry.SpawnSeq != request.SpawnSeq) return new Refused { Refusal = CarLockRefusal.Stale };
			if (!entry.HasBaseline) return new Refused { Refusal = CarLockRefusal.NotReady };

			CarLoaderEntry otherEntry = null;
			if (request.OtherLoaderID >= 0)
			{
				otherEntry = CarPartsStore.Get(request.OtherLoaderID);
				if (otherEntry == null || otherEntry.SpawnSeq != request.OtherSpawnSeq) return new Refused { Refusal = CarLockRefusal.Stale };
			}

			var x = request.X.Distinct().ToList();
			foreach (string key in x.Concat(request.S))
				if (!Exists(entry, key)) return new Refused { Refusal = CarLockRefusal.Invalid, Key = key };
			var s = Derive(entry, x, request.S);
			var knownItems = KnownItems(request, clientId);

			var refused = Check(clientId, request.CarLoaderID, x, s, request.Items.Distinct().ToList(), extended?.Id ?? 0);
			if (refused != null) return refused;
			if (otherEntry != null)
			{
				refused = Check(clientId, request.OtherLoaderID, new List<string> { LockKeys.Car }, new List<string>(), new List<long>(), 0);
				if (refused != null) return refused;
			}

			if (extended != null)
			{
				Unindex(extended);
				foreach (string key in x.Where(k => !extended.X.Contains(k))) extended.X.Add(key);
				extended.S = extended.S.Concat(s).Where(k => !extended.X.Contains(k)).Distinct().ToList();
				foreach (long uid in knownItems.Where(u => !extended.Items.Contains(u))) extended.Items.Add(uid);
				extended.Kind = request.Kind;
				extended.Phase++;
				extended.RenewedAt = now;
				Remember(entry, extended);
				Index(extended);
				granted = new List<Lock> { extended };
				Logger.Info($"[Locks] Lock {extended.Id} of client {clientId} extended: {Describe(extended)}.");
				return null;
			}

			var record = new Lock
			{
				Id = nextId++, Owner = clientId, Loader = request.CarLoaderID, SpawnSeq = entry.SpawnSeq, Kind = request.Kind,
				X = x, S = s, Items = knownItems, Since = now, RenewedAt = now
			};
			Remember(entry, record);
			Index(record);
			granted = new List<Lock> { record };
			if (otherEntry != null)
			{
				var linked = new Lock
				{
					Id = nextId++, LinkedId = record.Id, Owner = clientId, Loader = request.OtherLoaderID, SpawnSeq = otherEntry.SpawnSeq, Kind = request.Kind,
					X = new List<string> { LockKeys.Car }, Since = now, RenewedAt = now
				};
				record.LinkedId = linked.Id;
				Index(linked);
				granted.Add(linked);
			}
			Logger.Info($"[Locks] Lock {record.Id} granted to client {clientId}: {Describe(record)}{(granted.Count > 1 ? $", linked lock {granted[1].Id} on loader {granted[1].Loader}" : "")}.");
			return null;
		}

		private static bool Exists(CarLoaderEntry entry, string key)
		{
			if (!LockKeys.IsWellFormed(key)) return false;
			if (LockKeys.IsBody(key)) return entry.BodyParts.ContainsKey(int.Parse(key.Substring(2)));
			if (LockKeys.IsSub(key)) return entry.SubParts.ContainsKey(key.Substring(2));
			return true;
		}

		private static List<string> Derive(CarLoaderEntry entry, List<string> x, IEnumerable<string> requested)
		{
			var shared = new List<string>(requested);
			foreach (string key in x.Where(LockKeys.IsSub))
				shared.AddRange(LockKeys.AncestorCandidates(key).Where(a => entry.SubParts.ContainsKey(a.Substring(2))));
			if (!x.Contains(LockKeys.Car)) shared.Add(LockKeys.Car);
			if (Scope == LockScope.Part) shared = shared.Where(k => k == LockKeys.Car).ToList();
			return shared.Where(k => !x.Contains(k)).Distinct().ToList();
		}

		private static List<long> KnownItems(CarLockRequestPacket request, int clientId)
		{
			var inventory = GameDataManager.CurrentState.InventoryState;
			var known = new List<long>();
			foreach (long uid in request.Items.Distinct())
			{
				bool exists = inventory.InventoryItems.Any(i => i.UID == uid) || inventory.InventoryGroupItems.Any(g => g.UID == uid);
				if (exists) known.Add(uid);
				else Logger.Info($"[Locks] Request {request.RequestId} by client {clientId}: item {uid} is unknown to the server; not locked.");
			}
			return known;
		}

		private static Refused Check(int clientId, int loader, List<string> x, List<string> s, List<long> wantedItems, int ownLockId)
		{
			int away = CarAwayRegistry.OwnerOf(loader);
			if (away >= 0 && away != clientId) return new Refused { Refusal = CarLockRefusal.Away, Holder = away, Key = LockKeys.Car };
			if (index.TryGetValue(loader, out var keys))
			{
				foreach (string key in x)
				{
					if (!keys.TryGetValue(key, out var holders)) continue;
					int other = OtherOwner(holders.X, clientId, ownLockId);
					if (other >= 0) return new Refused { Refusal = CarLockRefusal.Held, Holder = other, Key = key };
					int sharedBy = holders.S.FirstOrDefault(id => OtherOwner(id, clientId, ownLockId) >= 0);
					if (sharedBy != 0) return new Refused { Refusal = CarLockRefusal.Held, Holder = locks[sharedBy].Owner, Key = locks[sharedBy].X.FirstOrDefault() ?? key };
				}
				foreach (string key in s)
				{
					if (!keys.TryGetValue(key, out var holders)) continue;
					int other = OtherOwner(holders.X, clientId, ownLockId);
					if (other >= 0) return new Refused { Refusal = CarLockRefusal.Held, Holder = other, Key = key };
				}
			}
			foreach (long uid in wantedItems)
			{
				if (!items.TryGetValue(uid, out int lockId)) continue;
				int other = OtherOwner(lockId, clientId, ownLockId);
				if (other >= 0) return new Refused { Refusal = CarLockRefusal.Item, Holder = other, Key = $"i:{uid}" };
			}
			return null;
		}

		private static int OtherOwner(int lockId, int clientId, int ownLockId)
		{
			if (lockId == 0 || lockId == ownLockId || !locks.TryGetValue(lockId, out var record)) return -1;
			return record.Owner == clientId ? -1 : record.Owner;
		}

		private static void Remember(CarLoaderEntry entry, Lock record)
		{
			foreach (string key in record.X)
			{
				if (record.StartUnmounted.ContainsKey(key)) continue;
				bool? unmounted = StoredUnmounted(entry, key);
				if (unmounted.HasValue) record.StartUnmounted[key] = unmounted.Value;
			}
		}

		public static bool? StoredUnmounted(CarLoaderEntry entry, string key)
		{
			if (LockKeys.IsBody(key) && entry.BodyParts.TryGetValue(int.Parse(key.Substring(2)), out var body)) return body.Unmounted;
			if (LockKeys.IsSub(key) && entry.SubParts.TryGetValue(key.Substring(2), out var sub)) return sub.Unmounted;
			return null;
		}

		private static void Index(Lock record)
		{
			locks[record.Id] = record;
			if (!index.TryGetValue(record.Loader, out var keys)) index[record.Loader] = keys = new Dictionary<string, Holders>();
			foreach (string key in record.X) Holder(keys, key).X = record.Id;
			foreach (string key in record.S) Holder(keys, key).S.Add(record.Id);
			foreach (long uid in record.Items) items[uid] = record.Id;
		}

		private static Holders Holder(Dictionary<string, Holders> keys, string key)
		{
			if (!keys.TryGetValue(key, out var holders)) keys[key] = holders = new Holders();
			return holders;
		}

		private static void Unindex(Lock record)
		{
			locks.Remove(record.Id);
			if (index.TryGetValue(record.Loader, out var keys))
			{
				foreach (string key in record.X.Concat(record.S))
				{
					if (!keys.TryGetValue(key, out var holders)) continue;
					if (holders.X == record.Id) holders.X = 0;
					holders.S.Remove(record.Id);
					if (holders.Empty) keys.Remove(key);
				}
				if (keys.Count == 0) index.Remove(record.Loader);
			}
			foreach (long uid in record.Items)
				if (items.TryGetValue(uid, out int id) && id == record.Id) items.Remove(uid);
		}

		public static CarLockUpdatePacket Update(Lock record, bool released = false) => new CarLockUpdatePacket
		{
			LockId = record.Id, LinkedLockId = record.LinkedId, CarLoaderID = record.Loader, SpawnSeq = record.SpawnSeq,
			OwnerPlayerId = released ? CarLockUpdatePacket.Released : record.Owner, Kind = record.Kind, Phase = record.Phase,
			X = new List<string>(record.X), S = new List<string>(record.S), Items = new List<long>(record.Items)
		};

		public static bool Release(int lockId, string reason, string counter = "released")
		{
			if (!locks.TryGetValue(lockId, out var record)) return false;
			Unindex(record);
			Count(counter);
			Logger.Info($"[Locks] Lock {record.Id} of client {record.Owner} on loader {record.Loader} released: {reason}.");
			Server.SendToClients(Update(record, released: true));
			if (record.LinkedId != 0 && locks.ContainsKey(record.LinkedId)) Release(record.LinkedId, $"linked lock {record.Id} released", counter);
			return true;
		}

		public static void OnRelease(int clientId, CarLockReleasePacket packet)
		{
			if (locks.TryGetValue(packet.LockId, out var record) && record.Owner == clientId) Release(record.Id, "released by the owner");
		}

		public static void OnRenew(int clientId, CarLockRenewPacket packet, float now)
		{
			foreach (int id in packet.LockIds)
				if (locks.TryGetValue(id, out var record) && record.Owner == clientId) record.RenewedAt = now;
		}

		public static void ReleaseOwner(int clientId, string reason)
		{
			foreach (var record in locks.Values.Where(l => l.Owner == clientId).ToList()) Release(record.Id, reason);
		}

		public static void ReleaseOwnerOnLoader(int clientId, int loader, string reason)
		{
			foreach (var record in locks.Values.Where(l => l.Owner == clientId && l.Loader == loader).ToList()) Release(record.Id, reason);
		}

		public static void ReleaseLoader(int loader, string reason)
		{
			foreach (var record in locks.Values.Where(l => l.Loader == loader).ToList()) Release(record.Id, reason);
		}

		public static void ReleaseCommitted(int clientId, CarLoaderEntry entry, int loader)
		{
			foreach (var record in locks.Values.Where(l => l.Owner == clientId && l.Loader == loader).ToList())
			{
				var parts = record.X.Where(LockKeys.IsPart).ToList();
				if (parts.Count == 0) continue;
				bool done = parts.All(key => record.StartUnmounted.TryGetValue(key, out bool start) && StoredUnmounted(entry, key) is bool now && now != start);
				if (done) Release(record.Id, "every exclusive part reached its target state", "releasedByCommit");
			}
		}

		public static int ExclusiveOwner(int loader, string key, int exceptClient)
		{
			if (!index.TryGetValue(loader, out var keys) || !keys.TryGetValue(key, out var holders)) return -1;
			return OtherOwner(holders.X, exceptClient, 0);
		}

		public static int SharedOwner(int loader, string key, int exceptClient)
		{
			if (!index.TryGetValue(loader, out var keys) || !keys.TryGetValue(key, out var holders)) return -1;
			return holders.S.Select(id => OtherOwner(id, exceptClient, 0)).Where(o => o >= 0).DefaultIfEmpty(-1).First();
		}

		public static void CountUnlockedFlip(int clientId, int loader, string key, int holder)
		{
			Count("unlockedFlip");
			Logger.Info($"[Locks] Client {clientId} flipped {key} on loader {loader}, which client {holder} holds shared; accepted.");
		}

		public static int ItemHolder(long uid, int exceptClient) =>
			locks.Values.Where(l => l.Owner != exceptClient && l.Items.Contains(uid)).Select(l => l.Owner).DefaultIfEmpty(-1).First();

		public static bool HeldByOther(int loader, int clientId) => locks.Values.Any(l => l.Loader == loader && l.Owner != clientId);

		public const string BusyPark = "park";
		public const string BusyDelete = "delete";
		public const string BusyJobEnd = "job end";
		public const string BusyLift = "lift";
		public const string BusyMove = "move";
		public const string BusySale = "sale";

		public static bool RefuseBusy(int loader, int clientId, string what)
		{
			int holder = OtherOwnerOn(loader, clientId);
			if (holder < 0) return false;
			Count($"denied.{CarLockRefusal.CarBusy}");
			Logger.Info($"[Locks] {what} of loader {loader} by client {clientId} refused: client {holder} holds a lock on the car.");
			Server.SendToClient(new CarLockResultPacket { RequestId = 0, Granted = false, Refusal = CarLockRefusal.CarBusy, HolderPlayerId = holder, ConflictKey = what }, clientId);
			return true;
		}

		public static int OtherOwnerOn(int loader, int clientId) => locks.Values.Where(l => l.Loader == loader && l.Owner != clientId).Select(l => l.Owner).DefaultIfEmpty(-1).First();

		public static bool HoldsCar(int loader, int clientId) =>
			index.TryGetValue(loader, out var keys) && keys.TryGetValue(LockKeys.Car, out var holders) && OtherOwner(holders.X, -1, 0) == clientId;

		public static Refused ConflictFor(int loader, int clientId, List<string> x, List<string> s) => Check(clientId, loader, x, s, new List<long>(), 0);

		public static void Tick(float now)
		{
			foreach (var record in locks.Values.ToList())
			{
				if (!locks.ContainsKey(record.Id)) continue;
				if ((record.Kind == CarLockKind.Lift || record.Kind == CarLockKind.Move) && now - record.Since > CarLockSeconds)
					Release(record.Id, $"{record.Kind} lock older than {CarLockSeconds:0} s", "expired");
				else if (now - record.RenewedAt > ExpirySeconds)
					Release(record.Id, $"not renewed for {ExpirySeconds:0} s", "expired");
			}
		}

		public static void SendActive(int loader, int clientId)
		{
			foreach (var record in locks.Values.Where(l => l.Loader == loader).OrderBy(l => l.Id))
				Server.SendToClient(Update(record), clientId);
		}

		private static void Audit()
		{
			foreach (var pair in index)
			foreach (var key in pair.Value)
			{
				var holders = key.Value;
				if (holders.X == 0 || !locks.TryGetValue(holders.X, out var exclusive)) continue;
				foreach (int id in holders.S)
				{
					if (!locks.TryGetValue(id, out var shared) || shared.Owner == exclusive.Owner) continue;
					Count("overlapViolations");
					Logger.Error($"[Locks] Overlap on loader {pair.Key} key {key.Key}: lock {exclusive.Id} (client {exclusive.Owner}) X and lock {shared.Id} (client {shared.Owner}) S.");
				}
			}
		}

		public static string Describe(Lock record) =>
			$"loader {record.Loader} {record.Kind} phase {record.Phase} X[{string.Join(",", record.X)}] S[{string.Join(",", record.S)}]" +
			(record.Items.Count > 0 ? $" items[{string.Join(",", record.Items)}]" : "");

		public static IEnumerable<string> Describe(float now)
		{
			yield return $"locks: {locks.Count}; " + string.Join(", ", new[] { "requested", "granted", "released", "releasedByCommit", "expired", "unlockedFlip", "overlapViolations" }
				.Select(c => $"{c} {Counter(c)}").Concat(counters.Keys.Where(k => k.StartsWith("denied.")).OrderBy(k => k).Select(k => $"{k} {counters[k]}")));
			foreach (var record in locks.Values.OrderBy(l => l.Loader).ThenBy(l => l.Id))
				yield return $"  lock {record.Id} client {record.Owner} {Describe(record)}, {now - record.Since:0} s, renewed {now - record.RenewedAt:0} s ago{(record.LinkedId != 0 ? $", linked {record.LinkedId}" : "")}";
		}

		public static IEnumerable<(string Key, int Owner)> HeldPartKeys(int loader) =>
			locks.Values.Where(l => l.Loader == loader).SelectMany(l => l.X.Where(LockKeys.IsPart).Select(k => (k, l.Owner)));
	}
}
