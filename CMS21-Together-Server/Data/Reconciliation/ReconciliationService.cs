using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Jobs;
using CMS21_Together_Server.Data.Persistence.Sections;
using CMS21_Together_Server.Data.Placement;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Data.Tools;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Data.Reconciliation
{
	// desync-detection-and-resync D2/D3 and state-merges-and-contention D11/D12: the server pulls digests, confirms a
	// mismatch over two answers with unchanged hashes (a "not ready" answer in between keeps it; it expires after four
	// asks), logs the field diff and resends that key's state to that client when its resend is on. A key that stays
	// "not ready" for desync_stall_seconds is reported once. Callers hold StateLock.
	public static class ReconciliationService
	{
		private const float BackoffWindowSeconds = 60f;
		private const float PersistentSeconds = 300f;
		private const float DetailTimeoutSeconds = 5f;
		private const int KeptRecords = 20;
		private const int MismatchAsks = 4;

		public static readonly string[] DefaultResendKeys =
		{
			DigestMappers.WorldKey, DigestMappers.InventoryKey, DigestMappers.CarsKey, DigestMappers.PlacementKey,
			DigestMappers.DetailsKey, DigestMappers.ToolsKey, DigestMappers.WarehouseKey, DigestMappers.GarageKey, DigestMappers.JobsKey,
		};

		private class Pending
		{
			public ulong Client;
			public ulong Server;
			public int Asks;
		}

		private class ClientRound
		{
			public int Seq;
			public int OutstandingSeq = -1;
			public int CarCursor;
			public bool Verbose;
			public readonly Dictionary<string, Pending> Mismatches = new Dictionary<string, Pending>();
			public readonly Dictionary<string, float> LastResend = new Dictionary<string, float>();
			public readonly Dictionary<string, float> PersistentUntil = new Dictionary<string, float>();
			public readonly Dictionary<string, float> AwaitingDetail = new Dictionary<string, float>();
			public readonly Dictionary<string, Series> NotReady = new Dictionary<string, Series>();
		}

		private class Series
		{
			public float First;
			public float Last;
			public bool Warned;
		}

		private static readonly Dictionary<int, ClientRound> rounds = new Dictionary<int, ClientRound>();
		private static readonly List<string> recent = new List<string>();
		private static float lastTick = float.MinValue;
		private static float lastNow;

		public static float IntervalSeconds { get; set; } = 5f;
		public static bool AutoFix { get; set; } = true;
		public static float StallSeconds { get; set; } = 120f;
		public static HashSet<string> ResendKeys { get; set; } = new HashSet<string>(DefaultResendKeys);

		public static void Tick(float now, bool force = false)
		{
			lastNow = now;
			foreach (var pair in rounds.ToList())
				foreach (var detail in pair.Value.AwaitingDetail.Where(d => now - d.Value > DetailTimeoutSeconds).ToList())
					Repair(pair.Key, pair.Value, detail.Key, null, now);

			if (!force && now - lastTick < IntervalSeconds) return;
			lastTick = now;
			foreach (var client in Server.Clients.Values.Where(c => c.IsConnected && c.SyncState == Network.SyncState.InSession))
			{
				if (PresenceRegistry.Get(client.ID)?.Scene != GameScene.Garage) continue;
				if (!rounds.TryGetValue(client.ID, out var round)) rounds[client.ID] = round = new ClientRound();
				var request = new StateDigestRequestPacket { Seq = ++round.Seq };
				foreach (string key in DigestMappers.GlobalKeys) request.Entries.Add(new DigestRequestEntry { Key = key });
				var cars = GameDataManager.CurrentState.CarState.LoadedCars.Where(c => c.Value.HasBaseline).Select(c => c.Key).OrderBy(k => k).ToList();
				var asked = force ? cars : cars.Count > 0 ? new List<int> { cars[round.CarCursor++ % cars.Count] } : new List<int>();
				foreach (int loader in asked)
					foreach (string key in DigestMappers.CarKeys)
						request.Entries.Add(new DigestRequestEntry { Key = key, SubKey = loader.ToString() });
				round.OutstandingSeq = request.Seq;
				round.Verbose = force;
				Server.SendToClient(request, client.ID);
			}
		}

		public static void OnLeft(int clientId) => rounds.Remove(clientId);

		public static void OnDigest(int clientId, StateDigestPacket digest, float now)
		{
			lastNow = now;
			if (digest.Trigger == DigestTrigger.ManualResync)
			{
				var differing = digest.Entries.Where(e => !e.NotReady && Project(e.Key, e.SubKey)?.Hash() is ulong hash && hash != e.Hash).Select(e => Id(e.Key, e.SubKey)).ToList();
				string line = $"[Desync] manual resync by {PresenceRegistry.Get(clientId)?.Username ?? $"player {clientId}"}; differing at that moment: {(differing.Count == 0 ? "none" : string.Join(", ", differing))}.";
				Logger.Info(line);
				Remember(line);
				return;
			}
			if (!rounds.TryGetValue(clientId, out var round) || digest.Seq != round.OutstandingSeq) return;
			round.OutstandingSeq = -1;
			bool verbose = round.Verbose;
			round.Verbose = false;
			foreach (var entry in digest.Entries)
			{
				string id = Id(entry.Key, entry.SubKey);
				TrackStall(clientId, round, id, entry.NotReady, now);
				var server = Project(entry.Key, entry.SubKey);
				if (entry.NotReady || server == null)
				{
					if (verbose) Logger.Info($"[Desync] {id} for client {clientId}: not ready.");
					CountAsk(clientId, round, id);
					continue;
				}
				ulong serverHash = server.Hash();
				if (serverHash == entry.Hash)
				{
					if (verbose) Logger.Info($"[Desync] {id} for client {clientId}: match.");
					if (round.Mismatches.Remove(id)) Logger.Debug($"[Desync] {id} for client {clientId} matches again.");
					continue;
				}
				bool confirmed = round.Mismatches.TryGetValue(id, out var previous) && previous.Client == entry.Hash && previous.Server == serverHash;
				if (!confirmed) round.Mismatches[id] = new Pending { Client = entry.Hash, Server = serverHash, Asks = 1 };
				Logger.Info($"[Desync] {id} for client {clientId}: mismatch ({(confirmed ? "confirmed" : "waiting for the next round")}).");
				if (confirmed) Confirm(clientId, round, entry.Key, entry.SubKey, now);
			}
		}

		private static void CountAsk(int clientId, ClientRound round, string id)
		{
			if (!round.Mismatches.TryGetValue(id, out var pending)) return;
			if (++pending.Asks < MismatchAsks) return;
			round.Mismatches.Remove(id);
			Logger.Debug($"[Desync] {id} for client {clientId}: the pending mismatch expired after {MismatchAsks} asks without a confirmation.");
		}

		private static void TrackStall(int clientId, ClientRound round, string id, bool notReady, float now)
		{
			round.NotReady.TryGetValue(id, out var series);
			if (series != null && now - series.Last > SeriesGapSeconds) series = null;
			if (!notReady)
			{
				if (series != null && series.Warned) Logger.Info($"[Desync] {id} for client {clientId} is ready again after {now - series.First:0} s.");
				round.NotReady.Remove(id);
				return;
			}
			if (series == null) round.NotReady[id] = series = new Series { First = now };
			series.Last = now;
			if (series.Warned || now - series.First < StallSeconds) return;
			series.Warned = true;
			string line = $"[Desync] {id} for client {clientId} has not been ready for {now - series.First:0} s.";
			Logger.Warn(line);
			Remember(line);
		}

		private static float SeriesGapSeconds =>
			IntervalSeconds * (GameDataManager.CurrentState.CarState.LoadedCars.Count + 1) * 3f + 10f;

		private static void Confirm(int clientId, ClientRound round, string key, string subKey, float now)
		{
			string id = Id(key, subKey);
			round.Mismatches.Remove(id);
			if (round.PersistentUntil.TryGetValue(id, out float until) && now < until) return;
			if (ResendKeys.Contains(key) && round.LastResend.TryGetValue(id, out float last) && now - last < BackoffWindowSeconds)
			{
				round.PersistentUntil[id] = now + PersistentSeconds;
				Logger.Warn($"[Desync] {id} for client {clientId} is persistent: no automatic resend for {PersistentSeconds / 60f:0} min.");
				Server.SendToClient(new DesyncNoticePacket { Key = key, SubKey = subKey, Persistent = true }, clientId);
				return;
			}
			if (!AutoFix)
			{
				Logger.Warn($"[Desync] {id} for client {clientId} confirmed (autofix off).");
				return;
			}
			round.AwaitingDetail[id] = now;
			Server.SendToClient(new StateDetailRequestPacket { Key = key, SubKey = subKey }, clientId);
		}

		public static void OnDetail(int clientId, StateDetailPacket detail, float now)
		{
			if (!rounds.TryGetValue(clientId, out var round)) return;
			Repair(clientId, round, Id(detail.Key, detail.SubKey), detail.Projection, now);
		}

		private static void Repair(int clientId, ClientRound round, string id, Projection clientProjection, float now)
		{
			if (!round.AwaitingDetail.Remove(id)) return;
			string[] parts = id.Split(new[] { ':' }, 2);
			string key = parts[0];
			string subKey = parts.Length > 1 ? parts[1] : "";
			var server = Project(key, subKey) ?? new Projection();
			string player = PresenceRegistry.Get(clientId)?.Username ?? $"player {clientId}";
			string summary = clientProjection == null ? "no detail from the client" : Summarize(Projection.Diff(clientProjection, server));
			bool resend = ResendKeys.Contains(key);
			string line = resend ? $"[Desync] {id} {player}: {summary}; resending." : $"[Desync] {id} {player}: {summary}; log only (resend off for {key}).";
			Logger.Warn(line);
			Remember(line);
			WriteRecord(clientId, player, key, subKey, clientProjection, server);
			if (!resend)
			{
				round.PersistentUntil[id] = now + PersistentSeconds;
				return;
			}
			round.LastResend[id] = now;
			Resend(clientId, key, subKey);
		}

		private static string Summarize(List<(ProjectionRow Client, ProjectionRow Server)> diff)
		{
			var shown = diff.Take(3).Select(d => $"{d.Client?.Id ?? d.Server?.Id}.{d.Client?.Field ?? d.Server?.Field} {d.Client?.Value ?? "-"} vs {d.Server?.Value ?? "-"}");
			return $"{diff.Count} fields differ ({string.Join(", ", shown)}{(diff.Count > 3 ? ", …" : "")})";
		}

		private static void WriteRecord(int clientId, string player, string key, string subKey, Projection client, Projection server)
		{
			try
			{
				string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log", "desync");
				Directory.CreateDirectory(dir);
				string name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{clientId}_{key}_{subKey}.json";
				var diff = client == null ? null : Projection.Diff(client, server).Select(d => new { Id = d.Client?.Id ?? d.Server?.Id, Field = d.Client?.Field ?? d.Server?.Field, Client = d.Client?.Value, Server = d.Server?.Value });
				File.WriteAllText(Path.Combine(dir, name), JsonConvert.SerializeObject(new { player, slot = clientId, key, subKey, diff, client, server }, Formatting.Indented));
			}
			catch (Exception e)
			{
				Logger.Error($"[Desync] Could not write the diff record: {e.Message}");
			}
		}

		private static void Resend(int clientId, string key, string subKey)
		{
			var state = GameDataManager.CurrentState;
			int loader;
			switch (key)
			{
				case DigestMappers.WorldKey:
					state.WorldState.updateGamemode = false;
					Server.SendToClient(state.WorldState, clientId);
					break;
				case DigestMappers.InventoryKey:
				case DigestMappers.WarehouseKey:
					new InventorySection().SendSnapshot(clientId);
					break;
				case DigestMappers.CarsKey:
					if (int.TryParse(subKey, out loader) && state.CarState.LoadedCars.TryGetValue(loader, out var entry))
						CarPartsStore.SendSnapshot(loader, entry, CarPartsSnapshotPacket.LiveSnapshot, only: clientId);
					break;
				case DigestMappers.DetailsKey:
					if (int.TryParse(subKey, out loader)) CarDetailsStore.SendTo(loader, clientId);
					break;
				case DigestMappers.PlacementKey:
					ParkingService.SendState(clientId);
					for (int lifter = 0; lifter < 2; lifter++) PlacementRules.SendLifter(lifter, instant: true, only: clientId);
					foreach (var car in state.CarState.LoadedCars)
						Server.SendToClient(new CarPlaceChangedPacket { CarLoaderID = car.Key, Place = car.Value.Spawn?.PlaceNo ?? -1 }, clientId);
					break;
				case DigestMappers.ToolsKey:
					foreach (var tool in Enum.GetValues(typeof(ModToolId)).Cast<ModToolId>().Where(ModTools.IsMachine))
						Server.SendToClient(new ToolSlotUpdatePacket { State = ToolsStore.Slot(tool) }, clientId);
					break;
				case DigestMappers.GarageKey:
					state.WorldState.updateGamemode = false;
					Server.SendToClient(state.WorldState, clientId);
					new GarageSection().SendSnapshot(clientId);
					break;
				case DigestMappers.JobsKey:
					JobsService.SendSnapshot(clientId);
					break;
			}
		}

		public static Projection Project(string key, string subKey)
		{
			var state = GameDataManager.CurrentState;
			switch (key)
			{
				case DigestMappers.WorldKey:
					return DigestMappers.World(state.WorldState.Money, state.WorldState.Scraps, state.WorldState.Level, state.WorldState.Exp);
				case DigestMappers.InventoryKey:
					return DigestMappers.Inventory(state.InventoryState.InventoryItems, state.InventoryState.InventoryGroupItems, null, null);
				case DigestMappers.CarsKey:
					if (!int.TryParse(subKey, out int loader) || !state.CarState.LoadedCars.TryGetValue(loader, out var entry) || !entry.HasBaseline) return null;
					return DigestMappers.Car(entry.Spawn?.CarToLoad, entry.BodyParts.Values, entry.SubParts.Values);
				case DigestMappers.PlacementKey:
					var lifters = new Dictionary<int, int> { [0] = PlacementRules.LifterState(0), [1] = PlacementRules.LifterState(1) };
					var cars = state.CarState.LoadedCars.ToDictionary(c => c.Key, c => c.Value.Spawn?.PlaceNo ?? -1);
					var parked = state.PlacementState.Parking.Slots.ToDictionary(s => s.Key, s => s.Value.CarToLoad);
					return DigestMappers.Placement(lifters, cars, parked, state.PlacementState.Parking.UnlockedLevels);
				default:
					return ProjectState(state, key, subKey);
			}
		}

		public static Projection ProjectState(ModGameState state, string key, string subKey)
		{
			switch (key)
			{
				case DigestMappers.DetailsKey:
					if (!int.TryParse(subKey, out int loader) || !state.CarState.LoadedCars.TryGetValue(loader, out var entry) || !entry.HasBaseline) return null;
					if (!state.CarState.Details.TryGetValue(loader, out var details) || details == null || !details.HasSnapshot || details.SpawnSeq != entry.SpawnSeq) return null;
					return DigestMappers.Details(details);
				case DigestMappers.ToolsKey:
					return DigestMappers.Tools(state.ToolsState.Slots.Values);
				case DigestMappers.WarehouseKey:
					return DigestMappers.Warehouse(state.InventoryState.WarehouseItems, state.InventoryState.WarehouseGroupItems);
				case DigestMappers.GarageKey:
					return DigestMappers.Garage(state.GarageState.GarageUpgradeLevels, state.GarageState.PlayerUpgradeLevels, state.WorldState.Barns);
				case DigestMappers.JobsKey:
					return DigestMappers.Jobs(state.JobsState.Orders.Select(o => o.Job.id),
						state.JobsState.ActiveJobs.Select(a => new KeyValuePair<int, int>(a.Job.id, a.CarLoaderId)));
				default:
					return null;
			}
		}

		private static string Id(string key, string subKey) => string.IsNullOrEmpty(subKey) ? key : $"{key}:{subKey}";

		private static void Remember(string line)
		{
			recent.Add($"{DateTime.Now:HH:mm:ss} {line}");
			if (recent.Count > KeptRecords) recent.RemoveAt(0);
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"desync checks every {IntervalSeconds:0} s, autofix {(AutoFix ? "on" : "off")}, resend for {string.Join(", ", ResendKeys.OrderBy(k => k, StringComparer.Ordinal))}, stall warning after {StallSeconds:0} s, {recent.Count} recent repairs";
			foreach (string line in recent) yield return "  " + line;
			foreach (var pair in rounds.OrderBy(r => r.Key))
				foreach (var series in pair.Value.NotReady.Where(s => s.Value.Warned && lastNow - s.Value.Last <= SeriesGapSeconds).OrderBy(s => s.Key, StringComparer.Ordinal))
					yield return $"  open stall: {series.Key} for client {pair.Key}, not ready for {series.Value.Last - series.Value.First:0} s";
		}
	}
}
