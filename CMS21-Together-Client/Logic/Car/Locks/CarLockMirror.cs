using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

public enum LockOutcome
{
	Granted,
	Denied,
	Timeout,
	Dropped
}

public sealed class LockRecord
{
	public int LockId;
	public int LinkedLockId;
	public int Loader;
	public int SpawnSeq;
	public int Owner;
	public CarLockKind Kind;
	public byte Phase;
	public List<string> X = new List<string>();
	public List<string> S = new List<string>();
	public List<long> Items = new List<long>();
	public bool Ending;

	public static LockRecord From(CarLockUpdatePacket packet) => new LockRecord
	{
		LockId = packet.LockId, LinkedLockId = packet.LinkedLockId, Loader = packet.CarLoaderID, SpawnSeq = packet.SpawnSeq,
		Owner = packet.OwnerPlayerId, Kind = packet.Kind, Phase = packet.Phase,
		X = packet.X ?? new List<string>(), S = packet.S ?? new List<string>(), Items = packet.Items ?? new List<long>()
	};
}

public sealed class LockAnswer
{
	public LockOutcome Outcome;
	public int LockId;
	public CarLockRefusal Refusal;
	public int Holder = -1;
	public string ConflictKey;
	public float WaitedMs;
}

public sealed class LockConflict
{
	public int Holder;
	public string Key;
	public CarLockRefusal Refusal = CarLockRefusal.Held;
}

public static class CarLockMirror
{
	public const float TimeoutSeconds = 3f;

	private sealed class Pending
	{
		public int RequestId;
		public LockSet Set;
		public float SentAt;
		public int ExtendLockId;
		public Action<LockAnswer> Done;
	}

	private sealed class Holders
	{
		public int X;
		public readonly HashSet<int> S = new HashSet<int>();
	}

	private static readonly Dictionary<int, LockRecord> records = new Dictionary<int, LockRecord>();
	private static readonly Dictionary<int, Dictionary<string, Holders>> index = new Dictionary<int, Dictionary<string, Holders>>();
	private static readonly Dictionary<long, int> items = new Dictionary<long, int>();
	private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
	private static readonly HashSet<int> dropped = new HashSet<int>();
	private static readonly HashSet<int> droppedExtends = new HashSet<int>();
	private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
	private static readonly HashSet<int> ownIds = new HashSet<int>();
	private static int nextRequestId = 1;
	private static float nextRenew;

	public static bool RenewEnabled { get; set; } = true;

	public static event Action<LockRecord, bool, bool> Changed;

	public static event Action<int, CarLockRefusal, string> BusyRefused;

	public static IEnumerable<LockRecord> All => records.Values;

	public static IReadOnlyDictionary<string, int> Counters => counters;

	public static int PendingCount => pending.Count;

	public static IEnumerable<(int RequestId, LockSet Set, float AgeMs)> PendingRequests =>
		pending.Values.Select(p => (p.RequestId, p.Set, (Time.realtimeSinceStartup - p.SentAt) * 1000f));

	private static int Me => Client.Instance?.ID ?? -1;

	public static void Count(string name) => counters[name] = (counters.TryGetValue(name, out int n) ? n : 0) + 1;

	public static void Reset()
	{
		records.Clear();
		index.Clear();
		items.Clear();
		pending.Clear();
		dropped.Clear();
		droppedExtends.Clear();
		ownIds.Clear();
		LockSets.Reset();
	}

	public static void ResetCounters() => counters.Clear();

	public static void Initialize()
	{
		ClientScene.LeavingScene += (from, to) =>
		{
			if (from == CMS21_Together_Core.Data.Enum.GameScene.Garage) DropOwn($"left {from}");
		};
	}

	public static void DropOwn(string why)
	{
		foreach (var request in pending.Values.ToList())
		{
			pending.Remove(request.RequestId);
			Drop(request);
			request.Done?.Invoke(new LockAnswer { Outcome = LockOutcome.Dropped });
		}
		foreach (var record in records.Values.Where(r => r.Owner == Me).ToList()) record.Ending = true;
		Log.Debug($"[Locks] Own pending requests and locks dropped ({why}).");
	}

	public static void ForgetLoader(int loader, int spawnSeq)
	{
		foreach (var record in records.Values.Where(r => r.Loader == loader && r.SpawnSeq != spawnSeq).ToList()) Remove(record);
		LockSets.Forget(loader);
	}

	public static void OnUpdate(CarLockUpdatePacket packet)
	{
		records.TryGetValue(packet.LockId, out var old);
		if (old != null) Unindex(old);
		if (packet.OwnerPlayerId == CarLockUpdatePacket.Released)
		{
			ownIds.Remove(packet.LockId);
			if (old != null) Raise(old, released: true);
			return;
		}
		var record = LockRecord.From(packet);
		if (record.Owner == Me) ownIds.Add(record.LockId);
		if (old != null) record.Ending = old.Ending;
		records[record.LockId] = record;
		Index(record);
		if (old == null || old.X.Count != record.X.Count) Raise(record, released: false);
	}

	private static void Remove(LockRecord record)
	{
		Unindex(record);
		Raise(record, released: true);
	}

	private static void Raise(LockRecord record, bool released)
	{
		try
		{
			PartClaims.OnLockChanged(record, released, SyncTracker.InSnapshot);
			Changed?.Invoke(record, released, SyncTracker.InSnapshot);
		}
		catch (Exception e)
		{
			Log.Error($"[Locks] Lock change handler failed on loader {record.Loader}: {e.Message}");
		}
	}

	private static void Index(LockRecord record)
	{
		if (!index.TryGetValue(record.Loader, out var keys)) index[record.Loader] = keys = new Dictionary<string, Holders>();
		foreach (string key in record.X) Holder(keys, key).X = record.LockId;
		foreach (string key in record.S) Holder(keys, key).S.Add(record.LockId);
		foreach (long uid in record.Items) items[uid] = record.LockId;
	}

	private static Holders Holder(Dictionary<string, Holders> keys, string key)
	{
		if (!keys.TryGetValue(key, out var holders)) keys[key] = holders = new Holders();
		return holders;
	}

	private static void Unindex(LockRecord record)
	{
		records.Remove(record.LockId);
		if (index.TryGetValue(record.Loader, out var keys))
		{
			foreach (string key in record.X.Concat(record.S))
			{
				if (!keys.TryGetValue(key, out var holders)) continue;
				if (holders.X == record.LockId) holders.X = 0;
				holders.S.Remove(record.LockId);
				if (holders.X == 0 && holders.S.Count == 0) keys.Remove(key);
			}
		}
		foreach (long uid in record.Items)
			if (items.TryGetValue(uid, out int id) && id == record.LockId) items.Remove(uid);
	}

	private static int OtherOwner(int lockId) =>
		lockId != 0 && records.TryGetValue(lockId, out var record) && record.Owner != Me ? record.Owner : -1;

	public static LockConflict Conflict(LockSet set)
	{
		if (set == null) return null;
		if (CarAwaySync.LockedForMe(set.Loader, out int away, out _)) return new LockConflict { Holder = away, Key = LockKeys.Car, Refusal = CarLockRefusal.Away };
		if (index.TryGetValue(set.Loader, out var keys))
		{
			foreach (string key in set.X)
			{
				if (!keys.TryGetValue(key, out var holders)) continue;
				int other = OtherOwner(holders.X);
				if (other >= 0) return new LockConflict { Holder = other, Key = key };
				int sharedBy = holders.S.FirstOrDefault(id => OtherOwner(id) >= 0);
				if (sharedBy != 0) return new LockConflict { Holder = records[sharedBy].Owner, Key = records[sharedBy].X.FirstOrDefault() ?? key };
			}
			foreach (string key in set.S)
			{
				if (!keys.TryGetValue(key, out var holders)) continue;
				int other = OtherOwner(holders.X);
				if (other >= 0) return new LockConflict { Holder = other, Key = key };
			}
		}
		foreach (long uid in set.Items)
		{
			if (!items.TryGetValue(uid, out int lockId)) continue;
			int other = OtherOwner(lockId);
			if (other >= 0) return new LockConflict { Holder = other, Key = $"i:{uid}", Refusal = CarLockRefusal.Item };
		}
		return null;
	}

	public static LockRecord OwnLockFor(int loader, string key) =>
		records.Values.FirstOrDefault(r => r.Owner == Me && r.Loader == loader && !r.Ending && r.X.Contains(key));

	public static IEnumerable<LockRecord> Own(int loader) => records.Values.Where(r => r.Owner == Me && r.Loader == loader);

	public static bool OwnFluidLockCovers(int loader, IEnumerable<string> flippedKeys)
	{
		var keys = new HashSet<string>(flippedKeys);
		return records.Values.Any(r => r.Owner == Me && r.Loader == loader && r.X.Any(keys.Contains) && r.X.Concat(r.S).Any(LockKeys.IsFluid));
	}

	public static int OtherOwnerOn(int loader) => records.Values.Where(r => r.Loader == loader && r.Owner != Me).Select(r => r.Owner).DefaultIfEmpty(-1).First();

	public static bool HoldsAny(int loader) => records.Values.Any(r => r.Owner == Me && r.Loader == loader);

	public static bool AnyOnCar(int loader) => records.Values.Any(r => r.Loader == loader);

	public static LockRecord Get(int lockId) => records.TryGetValue(lockId, out var record) ? record : null;

	public static int Request(LockSet set, Action<LockAnswer> done, int extendLockId = 0, int otherLoader = -1)
	{
		int requestId = nextRequestId++;
		pending[requestId] = new Pending { RequestId = requestId, Set = set, SentAt = Time.realtimeSinceStartup, ExtendLockId = extendLockId, Done = done };
		Count("requested");
		var packet = new CarLockRequestPacket
		{
			RequestId = requestId, CarLoaderID = set.Loader, SpawnSeq = CarPartsSync.SpawnSeq(set.Loader), Kind = set.Kind,
			X = set.X.ToList(), S = set.S.ToList(), Items = set.Items.ToList(), ExtendLockId = extendLockId,
			OtherLoaderID = otherLoader, OtherSpawnSeq = otherLoader >= 0 ? CarPartsSync.SpawnSeq(otherLoader) : 0
		};
		Log.Debug($"[Locks] Request {requestId}: {set}{(extendLockId != 0 ? $" extends {extendLockId}" : "")}.");
		Client.Instance.Send(packet);
		return requestId;
	}

	private static void Drop(Pending request)
	{
		dropped.Add(request.RequestId);
		if (request.ExtendLockId != 0) droppedExtends.Add(request.RequestId);
	}

	public static void Cancel(int requestId)
	{
		if (!pending.TryGetValue(requestId, out var request)) return;
		pending.Remove(requestId);
		Drop(request);
	}

	public static void OnResult(CarLockResultPacket result)
	{
		if (result.RequestId == 0)
		{
			Count($"denied.{result.Refusal}");
			BusyRefused?.Invoke(result.HolderPlayerId, result.Refusal, result.ConflictKey);
			return;
		}
		if (!pending.TryGetValue(result.RequestId, out var request))
		{
			if (droppedExtends.Remove(result.RequestId))
			{
				if (result.Granted) Log.Info($"[Locks] Late grant for dropped extension {result.RequestId} of lock {result.LockId}; the lock stays with its earlier phase.");
			}
			else if (result.Granted && dropped.Contains(result.RequestId))
			{
				Count("lateGrantsReleased");
				Log.Info($"[Locks] Late grant {result.LockId} for dropped request {result.RequestId} released.");
				Release(result.LockId);
			}
			dropped.Remove(result.RequestId);
			return;
		}
		pending.Remove(result.RequestId);
		var answer = new LockAnswer
		{
			Outcome = result.Granted ? LockOutcome.Granted : LockOutcome.Denied, LockId = result.LockId, Refusal = result.Refusal,
			Holder = result.HolderPlayerId, ConflictKey = result.ConflictKey, WaitedMs = (Time.realtimeSinceStartup - request.SentAt) * 1000f
		};
		Count(result.Granted ? "granted" : $"denied.{result.Refusal}");
		try { request.Done?.Invoke(answer); }
		catch (Exception e) { Log.Error($"[Locks] Lock answer handler failed: {e.Message}"); }
	}

	public static void Release(int lockId)
	{
		if (lockId == 0) return;
		if (records.TryGetValue(lockId, out var record)) record.Ending = true;
		Client.Instance.Send(new CarLockReleasePacket { LockId = lockId });
	}

	public static void ReleaseOwn(int loader = -1, string why = "")
	{
		var ids = loader < 0 ? ownIds.ToList() : ownIds.Where(id => records.TryGetValue(id, out var r) && r.Loader == loader).ToList();
		foreach (int id in ids)
		{
			Log.Debug($"[Locks] Releasing own lock {id}{(why.Length > 0 ? $" ({why})" : "")}.");
			Release(id);
		}
	}

	public static void MarkEnding(int lockId)
	{
		if (records.TryGetValue(lockId, out var record)) record.Ending = true;
	}

	public static void Update()
	{
		float now = Time.realtimeSinceStartup;
		foreach (var request in pending.Values.Where(p => now - p.SentAt > TimeoutSeconds).ToList())
		{
			pending.Remove(request.RequestId);
			Drop(request);
			Count("timeouts");
			Log.Info($"[Locks] Request {request.RequestId} timed out after {TimeoutSeconds:0} s.");
			try { request.Done?.Invoke(new LockAnswer { Outcome = LockOutcome.Timeout, WaitedMs = (now - request.SentAt) * 1000f }); }
			catch (Exception e) { Log.Error($"[Locks] Lock timeout handler failed: {e.Message}"); }
		}

		if (!RenewEnabled || now < nextRenew) return;
		int expiry = ClientData.ServerInfo?.LockExpirySeconds ?? 0;
		nextRenew = now + Mathf.Max(2f, (expiry > 0 ? expiry : 90) / 3f);
		var own = ownIds.ToList();
		if (own.Count > 0) Client.Instance.Send(new CarLockRenewPacket { LockIds = own });
	}
}
