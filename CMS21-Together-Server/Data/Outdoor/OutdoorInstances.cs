using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data.Outdoor
{
	public static class OutdoorInstances
	{
		public const int JunkyardPickCount = 40;
		public const int BarnPickCount = 3;

		private static readonly Dictionary<GameScene, OutdoorInstance> open = new Dictionary<GameScene, OutdoorInstance>();
		private static int nextInstanceId = 1;

		public static Random Rng { get; set; } = new Random();
		public static ICarSelector Selector { get; set; } = new BasicCarSelector();
		public static readonly SelectionHistory History = new SelectionHistory();
		public static HashSet<GameScene> SharedScenes { get; private set; } = new HashSet<GameScene>(OutdoorScenes.All);
		public static float GraceSeconds { get; set; } = 60f;
		public static bool FillAllSpawnPoints { get; set; }
		public static readonly Dictionary<AuctionKind, AuctionAmountRange> AuctionAmounts = new Dictionary<AuctionKind, AuctionAmountRange>();

		public static IEnumerable<OutdoorInstance> Open => open.Values;

		public static void Configure(IEnumerable<GameScene> shared, string selector, int graceSeconds, bool fillAllSpawnPoints)
		{
			SharedScenes = new HashSet<GameScene>(shared ?? Enumerable.Empty<GameScene>());
			Selector = CarSelectors.Create(selector);
			GraceSeconds = graceSeconds;
			FillAllSpawnPoints = fillAllSpawnPoints;
		}

		public static void Initialize()
		{
			PresenceEvents.SceneChanged += OnSceneChanged;
			PresenceEvents.Left += OnLeft;
			SharedDlc.Changed += _ => CarCatalog.Recompute();
		}

		public static void Reset()
		{
			open.Clear();
			nextInstanceId = 1;
			AuctionAmounts.Clear();
		}

		public static bool IsShared(GameScene scene) => SharedScenes.Contains(scene);

		public static bool IsOpen(GameScene scene) => open.ContainsKey(scene);

		public static OutdoorInstance Get(int instanceId) =>
			instanceId <= 0 ? null : open.Values.FirstOrDefault(i => i.InstanceId == instanceId);

		public static OutdoorInstance Of(GameScene scene) => open.TryGetValue(scene, out var instance) ? instance : null;

		public static OutdoorInstance MemberOf(int playerId) => open.Values.FirstOrDefault(i => i.HasMember(playerId));

		public static OutdoorInstance ForMember(int playerId, int instanceId)
		{
			var instance = Get(instanceId);
			return instance != null && instance.HasMember(playerId) ? instance : null;
		}

		public static OutdoorInstancePacket Enter(int playerId, GameScene scene, float now)
		{
			foreach (var other in open.Values.Where(i => i.Scene != scene && i.HasMember(playerId)).ToList())
				Leave(playerId, other, false, now, $"entered {scene}");

			if (!IsShared(scene))
			{
				Logger.Info($"[Outdoor] {scene} is not shared (shared_outdoor_scenes = {OutdoorScenes.Format(SharedScenes)}); player {playerId} generates it locally.");
				return new OutdoorInstancePacket { InstanceId = OutdoorInstancePacket.NotShared, Scene = scene };
			}

			if (!open.TryGetValue(scene, out var instance))
			{
				instance = OpenNew(scene, playerId);
			}
			else if (!instance.HasMember(playerId))
			{
				instance.Members.Add(playerId);
				Logger.Info($"[Outdoor] {instance.Label} joined by {playerId} ({instance.Members.Count} players).");
			}
			instance.EmptySince = -1f;
			if (!instance.HasMember(instance.GeneratorId))
			{
				Logger.Info($"[Outdoor] {instance.Label}: generator is now {playerId} (was {instance.GeneratorId}).");
				instance.GeneratorId = playerId;
			}
			OutdoorNet.MembershipChanged(playerId, instance.InstanceId);
			return instance.ToPacket(playerId, FillAllSpawnPoints);
		}

		private static OutdoorInstance OpenNew(GameScene scene, int playerId)
		{
			var instance = new OutdoorInstance
			{
				InstanceId = nextInstanceId++,
				Scene = scene,
				Seed = Rng.Next(1, int.MaxValue),
				GeneratorId = playerId,
				OpenedUtc = DateTime.UtcNow,
			};
			instance.Members.Add(playerId);
			open[scene] = instance;

			if (scene == GameScene.Junkyard) instance.Picks.AddRange(SelectAndRecord(OutdoorCatalogScene.Junkyard, JunkyardPickCount));
			else if (scene == GameScene.Barn) instance.Picks.AddRange(SelectAndRecord(OutdoorCatalogScene.Barn, BarnPickCount));
			else if (scene == GameScene.Auction) AuctionService.BuildLots(instance);

			Logger.Info($"[Outdoor] {instance.Label} opened by {playerId} (seed {instance.Seed}, {instance.Picks.Count} picks, {instance.Lots.Count} lots{(instance.LotsPending ? ", lots wait for the auction amounts" : "")}).");
			if (scene != GameScene.Auction && instance.Picks.Count == 0)
				Logger.Warn($"[Outdoor] {instance.Label}: no car catalog from the players yet; every player keeps its own cars.");
			return instance;
		}

		public static IReadOnlyList<OutdoorCarPick> SelectAndRecord(OutdoorCatalogScene scene, int count)
		{
			var picks = Selector.Select(new SelectionRequest
			{
				Scene = scene, Count = count, Candidates = CarCatalog.For(scene), History = History, Random = Rng,
			});
			if (picks.Count > 0)
			{
				History.Record(scene, picks.Select(p => p.CarId));
				OutdoorNet.HistoryChanged();
			}
			return picks;
		}

		public static void Leave(int playerId, OutdoorInstance instance, bool disconnected, float now, string why)
		{
			if (!instance.Members.Remove(playerId)) return;
			Logger.Info($"[Outdoor] {instance.Label} left by {playerId} ({why}, {instance.Members.Count} players left).");
			OutdoorNet.MembershipChanged(playerId, OutdoorInstancePacket.NotShared);
			LootService.ReleaseHeld(instance, playerId, why);
			AuctionService.ReleaseOwner(instance, playerId, why);

			if (instance.Members.Count == 0)
			{
				if (disconnected && GraceSeconds > 0)
				{
					instance.EmptySince = now;
					Logger.Info($"[Outdoor] {instance.Label} stays open for {GraceSeconds:0} s for player {playerId} to return.");
				}
				else Close(instance, "last player left");
				return;
			}

			if (instance.GeneratorId == playerId)
			{
				instance.GeneratorId = instance.Members[0];
				Logger.Info($"[Outdoor] {instance.Label}: generator {playerId} left, {instance.GeneratorId} takes over{(instance.NeedsGeneratorUpload ? " and uploads" : "")}.");
				if (instance.NeedsGeneratorUpload)
					OutdoorNet.Send(instance.GeneratorId, instance.ToPacket(instance.GeneratorId, FillAllSpawnPoints));
			}
		}

		public static void OnSceneChanged(int playerId, GameScene from, GameScene to)
		{
			foreach (var instance in open.Values.Where(i => i.HasMember(playerId)).ToList())
			{
				bool leftScene = from == instance.Scene && to != instance.Scene;
				bool elsewhere = to != GameScene.Loading && to != instance.Scene;
				if (leftScene || elsewhere) Leave(playerId, instance, false, OutdoorNet.Now(), $"went to {to}");
			}
		}

		public static void OnLeft(int playerId)
		{
			CarCatalog.Remove(playerId);
			foreach (var instance in open.Values.Where(i => i.HasMember(playerId)).ToList())
				Leave(playerId, instance, true, OutdoorNet.Now(), "disconnected");
		}

		public static void Tick(float now)
		{
			foreach (var instance in open.Values.Where(i => i.Members.Count == 0 && i.EmptySince >= 0f && now - i.EmptySince >= GraceSeconds).ToList())
				Close(instance, $"nobody returned within {GraceSeconds:0} s");
		}

		private static void Close(OutdoorInstance instance, string why)
		{
			if (Of(instance.Scene) == instance) open.Remove(instance.Scene);
			Logger.Info($"[Outdoor] {instance.Label} closed ({why}).");
		}

		public static void SendState(OutdoorInstance instance, int except = -1)
		{
			foreach (int member in instance.Members.ToList())
				if (member != except) OutdoorNet.Send(member, instance.ToPacket(member, FillAllSpawnPoints));
		}

		public static void OnLootRecord(int playerId, OutdoorLootRecordPacket packet)
		{
			var instance = ForMember(playerId, packet.InstanceId);
			if (instance == null)
			{
				Logger.Info($"[Outdoor] Loot record from {playerId} for instance {packet.InstanceId} ignored: not a member.");
				return;
			}
			if (LootService.Record(instance, playerId, packet)) SendState(instance, except: playerId);
		}

		public static void OnDigest(int playerId, OutdoorDigestPacket packet)
		{
			var instance = ForMember(playerId, packet.InstanceId);
			if (instance == null)
			{
				Logger.Info($"[Outdoor] Digest from {playerId} for instance {packet.InstanceId} ignored: not a member.");
				return;
			}

			bool stateChanged = false;
			if (packet.AuctionAmounts != null && packet.AuctionAmounts.Count > 0)
			{
				foreach (var range in packet.AuctionAmounts) AuctionAmounts[range.Kind] = range;
				if (instance.LotsPending && AuctionService.BuildLots(instance)) stateChanged = true;
			}
			if (packet.LotValues != null && packet.LotValues.Count > 0 && AuctionService.ApplyValues(instance, playerId, packet.LotValues))
				stateChanged = true;
			if (packet.Rows != null && packet.Rows.Count > 0) CompareDigest(instance, playerId, packet.Rows);
			if (stateChanged) SendState(instance);
		}

		private static void CompareDigest(OutdoorInstance instance, int playerId, List<string> rows)
		{
			instance.Digests[playerId] = rows;
			instance.DigestCompared.Remove(playerId);
			if (instance.ReferenceDigest == null && playerId == instance.GeneratorId)
			{
				instance.ReferenceDigest = rows;
				Logger.Info($"[Outdoor] {instance.Label}: reference digest from {playerId} ({rows.Count} rows).");
				TrimHistory(instance, rows);
			}
			if (instance.ReferenceDigest == null) return;

			foreach (var digest in instance.Digests.Where(d => d.Value != instance.ReferenceDigest && !instance.DigestCompared.Contains(d.Key)).ToList())
			{
				instance.DigestCompared.Add(digest.Key);
				var differences = OutdoorDigestRow.Compare(WithoutSold(instance, instance.ReferenceDigest), WithoutSold(instance, digest.Value));
				if (differences.Count == 0)
				{
					Logger.Info($"[Outdoor] {instance.Label}: digest of {digest.Key} equals the reference.");
					continue;
				}
				instance.DigestMismatches += differences.Count;
				foreach (string difference in differences)
				{
					string line = $"{instance.Scene} player {digest.Key}: {difference}";
					instance.MismatchLines.Add(line);
					Logger.Warn($"[Outdoor] digest mismatch {line}");
				}
			}
		}

		private static List<string> WithoutSold(OutdoorInstance instance, List<string> rows) =>
			rows.Where(r => !instance.Sold.Any(i => OutdoorDigestRow.Parse(r).Key == OutdoorDigestRow.CarPrefix + i)).ToList();

		private static void TrimHistory(OutdoorInstance instance, List<string> rows)
		{
			if (instance.Scene != GameScene.Junkyard) return;
			var used = rows.Select(OutdoorDigestRow.Parse)
				.Where(r => r.Key.StartsWith(OutdoorDigestRow.CarPrefix) && r.Fields.ContainsKey(OutdoorDigestRow.CarIdField))
				.Select(r => r.Fields[OutdoorDigestRow.CarIdField]).ToList();
			if (used.Count == 0) return;
			History.TrimLast(OutdoorCatalogScene.Junkyard, used);
			OutdoorNet.HistoryChanged();
		}

		public static ParkRefusal CheckCarPurchase(int playerId, CarParkRequestPacket request)
		{
			if (request.SourceInstanceId <= 0) return ParkRefusal.None;
			var instance = Get(request.SourceInstanceId);
			if (instance == null)
			{
				Logger.Info($"[Outdoor] Purchase from {playerId} names instance {request.SourceInstanceId}, which is closed; treated as a local car.");
				return ParkRefusal.None;
			}
			if (request.SourceLot >= 0) return AuctionService.CheckPurchase(instance, playerId, request.SourceLot);
			if (request.SourceCarIndex >= 0 && instance.Sold.Contains(request.SourceCarIndex))
			{
				Logger.Info($"[Outdoor] {instance.Label} car {request.SourceCarIndex} already sold; purchase from {playerId} refused.");
				return ParkRefusal.Taken;
			}
			return ParkRefusal.None;
		}

		public static void CarPurchaseAccepted(int playerId, CarParkRequestPacket request)
		{
			var instance = Get(request.SourceInstanceId);
			if (instance == null) return;
			if (request.SourceLot >= 0)
			{
				AuctionService.Close(instance, request.SourceLot, AuctionLotStatus.Won, playerId, "bought");
				return;
			}
			if (request.SourceCarIndex < 0 || !instance.Sold.Add(request.SourceCarIndex)) return;
			Logger.Info($"[Outdoor] {instance.Label} car {request.SourceCarIndex} sold to {playerId}.");
			OutdoorNet.SendTo(instance.Members, new OutdoorCarRemovedPacket { InstanceId = instance.InstanceId, Index = request.SourceCarIndex, By = playerId }, except: playerId);
		}

		public static void CarPurchaseRefused(int playerId, CarParkRequestPacket request, ParkRefusal reason)
		{
			if (request.SourceLot < 0 || reason == ParkRefusal.Taken) return;
			var instance = Get(request.SourceInstanceId);
			if (instance != null) AuctionService.Close(instance, request.SourceLot, AuctionLotStatus.Lost, playerId, $"purchase refused: {reason}");
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"Shared scenes: {OutdoorScenes.Format(SharedScenes)}, selector {Selector.Name}, grace {GraceSeconds:0} s, fill all spawn points {FillAllSpawnPoints}";
			if (open.Count == 0) yield return "No open instance.";
			foreach (var instance in open.Values.OrderBy(i => i.Scene))
				yield return $"{instance.Label}: players [{string.Join(", ", instance.Members)}], generator {instance.GeneratorId}, seed {instance.Seed}, {instance.Picks.Count} picks, sold [{string.Join(", ", instance.Sold.OrderBy(i => i))}], " +
				             $"loot {(instance.LootRecorded ? $"{instance.Piles.Count} piles / {instance.Loot.Count} items, held {instance.Loot.Values.Count(e => e.Status == LootItemStatus.Held)}, bought {instance.Loot.Values.Count(e => e.Status == LootItemStatus.Bought)}" : "not recorded")}, " +
				             $"lots {instance.Lots.Count}, digest mismatches {instance.DigestMismatches}{(instance.EmptySince >= 0f ? ", waiting for a returning player" : "")}";
			foreach (string line in CarCatalog.Describe()) yield return line;
			foreach (string line in History.Describe()) yield return $"History {line}";
		}

		public static IEnumerable<string> DescribeScene(GameScene scene)
		{
			var instance = Of(scene);
			if (instance == null)
			{
				yield return $"{scene}: no open instance{(IsShared(scene) ? "" : " (not shared)")}.";
				yield break;
			}
			yield return $"{instance.Label}: seed {instance.Seed}, generator {instance.GeneratorId}, players [{string.Join(", ", instance.Members.Select(m => $"{m} '{OutdoorNet.NameOf(m)}'"))}], opened {instance.OpenedUtc:HH:mm:ss} UTC";
			for (int i = 0; i < instance.Picks.Count; i++)
				yield return $"  pick {i}: {instance.Picks[i]}{(instance.Sold.Contains(i) ? " (sold)" : "")}";
			if (instance.LootRecorded)
			{
				foreach (var pile in instance.Piles)
					yield return $"  pile {pile.Index} [{pile.Key}]: {string.Join(", ", pile.Items.Select(item => $"{item.ID}#{item.UID}{StateSuffix(instance, item.UID)}"))}";
				yield return $"  random show: {string.Join("", (instance.RandomShowActive ?? new List<bool>()).Select(b => b ? '1' : '0'))}";
			}
			foreach (var lot in instance.Lots)
			{
				instance.LotStates.TryGetValue(lot.Index, out var state);
				yield return $"  lot {lot.Index} {lot.Kind} {lot.CarId}/{lot.Version} seed {lot.Seed} start {(lot.ValuesKnown ? lot.StartingPrice.ToString() : "?")}: {state?.Status}{(state?.LastBid != null ? $" bid {state.LastBid.CurrentBid} by owner {state.OwnerId}, team leads {state.LastBid.TeamLeads}, {state.LastBid.SecondsLeft:0.0} s" : "")}";
			}
			yield return $"  digests from [{string.Join(", ", instance.Digests.Keys)}], mismatches {instance.DigestMismatches}";
			foreach (string line in instance.MismatchLines.Skip(Math.Max(0, instance.MismatchLines.Count - 20)))
				yield return $"    {line}";
		}

		private static string StateSuffix(OutdoorInstance instance, long uid) =>
			instance.Loot.TryGetValue(uid, out var entry) && entry.Status != LootItemStatus.Available ? $" ({entry.Status} {entry.HolderId})" : "";
	}
}
