using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class CarPartsHandlers
	{
		private static readonly Dictionary<string, List<CarPartsSnapshotPacket>> incoming = new Dictionary<string, List<CarPartsSnapshotPacket>>();

		[PacketHandler(PacketTypes.CarPartsChange)]
		public static void OnChange(long clientId, CarPartsChangePacket change)
		{
			var entry = CarPartsStore.Get(change.CarLoaderID);
			if (entry == null || entry.SpawnSeq != change.SpawnSeq || !entry.HasBaseline)
			{
				var gone = new CarPartsChangeResultPacket
				{
					CarLoaderID = change.CarLoaderID, SpawnSeq = change.SpawnSeq, TxId = change.TxId,
					Accepted = false, Reason = "the car is gone", Revision = entry?.Revision ?? 0
				};
				gone.RestoreUids.AddRange(InventoryChanges.StillHeld(change.InventoryDelta));
				Server.SendToClient(gone, (int)clientId);
				Logger.Info($"[Cars] Change {change.TxId} from client {clientId} for loader {change.CarLoaderID} rejected: no car or baseline for SpawnSeq {change.SpawnSeq}.");
				return;
			}

			var normalised = Normalise(entry, change, (int)clientId);
			string conflict = !OnlyExamines(change) && CarAwayRegistry.Blocks(change.CarLoaderID, (int)clientId, $"change {change.TxId}")
				? "the car is away"
				: FindConflict(entry, change, normalised, (int)clientId);
			if (conflict != null)
			{
				var reject = new CarPartsChangeResultPacket
				{
					CarLoaderID = change.CarLoaderID, SpawnSeq = change.SpawnSeq, TxId = change.TxId,
					Accepted = false, Reason = conflict, Revision = entry.Revision
				};
				foreach (var record in change.BodyParts)
					if (entry.BodyParts.TryGetValue(record.PartIndex, out var stored)) reject.BodyParts.Add(PartRecordMerge.WithChanged(stored, PartFields.All));
				foreach (var record in change.SubParts)
					if (entry.SubParts.TryGetValue(CarSubPartIdentity.BuildKey(record.PartIndexPath), out var stored)) reject.SubParts.Add(PartRecordMerge.WithChanged(stored, PartFields.All));
				reject.RestoreUids.AddRange(InventoryChanges.StillHeld(change.InventoryDelta));
				Server.SendToClient(reject, (int)clientId);
				Logger.Info($"[Cars] Change {change.TxId} from client {clientId} on loader {change.CarLoaderID} rejected: {conflict}");
				return;
			}

			entry.Revision++;
			var relay = new CarPartsChangePacket
			{
				CarLoaderID = change.CarLoaderID, SpawnSeq = change.SpawnSeq, TxId = change.TxId, Revision = entry.Revision,
				Preconditions = change.Preconditions, InventoryDelta = change.InventoryDelta
			};
			var result = new CarPartsChangeResultPacket
			{
				CarLoaderID = change.CarLoaderID, SpawnSeq = change.SpawnSeq, TxId = change.TxId, Accepted = true, Revision = entry.Revision
			};
			foreach (var (sent, outcome) in normalised.Body)
			{
				if (outcome.Record != null)
				{
					outcome.Record.Revision = entry.Revision;
					outcome.Record.Changed = PartFields.None;
					entry.BodyParts[sent.PartIndex] = outcome.Record;
					if (outcome.Written != PartFields.None) relay.BodyParts.Add(PartRecordMerge.WithChanged(outcome.Record, outcome.Written));
				}
				if (entry.BodyParts.TryGetValue(sent.PartIndex, out var now))
				{
					var differ = PartRecordMerge.Differ(now, sent);
					if (differ != PartFields.None) result.BodyParts.Add(PartRecordMerge.WithChanged(now, differ));
				}
			}
			foreach (var (sent, outcome) in normalised.Sub)
			{
				string key = CarSubPartIdentity.BuildKey(sent.PartIndexPath);
				if (outcome.Record != null)
				{
					outcome.Record.Revision = entry.Revision;
					outcome.Record.Changed = PartFields.None;
					entry.SubParts[key] = outcome.Record;
					if (outcome.Written != PartFields.None) relay.SubParts.Add(PartRecordMerge.WithChanged(outcome.Record, outcome.Written));
				}
				if (entry.SubParts.TryGetValue(key, out var now))
				{
					var differ = PartRecordMerge.Differ(now, sent);
					if (differ != PartFields.None) result.SubParts.Add(PartRecordMerge.WithChanged(now, differ));
				}
			}
			InventoryChanges.Apply(change.InventoryDelta, (int)clientId);

			CarLocks.ReleaseCommitted((int)clientId, entry, change.CarLoaderID);
			Server.SendToClient(result, (int)clientId);
			Server.SendToClients(relay, (int)clientId);
			Logger.Info($"[Cars] Change {change.TxId} from client {clientId} on loader {change.CarLoaderID}: revision {entry.Revision} ({change.BodyParts.Count} body, {change.SubParts.Count} mechanical, relayed {relay.BodyParts.Count + relay.SubParts.Count}, returned {result.BodyParts.Count + result.SubParts.Count}{normalised.Summary}, inventory +{change.InventoryDelta.AddedItems.Count + change.InventoryDelta.AddedGroups.Count} -{change.InventoryDelta.RemovedItemUids.Count + change.InventoryDelta.RemovedGroupUids.Count}).");
		}

		private class NormalisedChange
		{
			public readonly List<(CarBodyPartUpdatePacket Sent, Normalised<CarBodyPartUpdatePacket> Outcome)> Body = new List<(CarBodyPartUpdatePacket, Normalised<CarBodyPartUpdatePacket>)>();
			public readonly List<(CarSubPartUpdatePacket Sent, Normalised<CarSubPartUpdatePacket> Outcome)> Sub = new List<(CarSubPartUpdatePacket, Normalised<CarSubPartUpdatePacket>)>();
			public string Summary = "";

			public IEnumerable<string> FlippedKeys(CarPartsChangePacket change)
			{
				var preconditions = new HashSet<string>(change.Preconditions.Select(p => p.Key));
				foreach (var (sent, outcome) in Body)
					if (preconditions.Contains(sent.Key) || Flips(outcome.Written)) yield return sent.Key;
				foreach (var (sent, outcome) in Sub)
					if (preconditions.Contains(sent.Key) || Flips(outcome.Written)) yield return sent.Key;
			}

			private static bool Flips(PartFields written) => written.HasFlag(PartFields.Mount) || written.HasFlag(PartFields.All);
		}

		private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();

		public static int Counter(string name) => counters.TryGetValue(name, out int value) ? value : 0;

		private static void Count(string name) => counters[name] = Counter(name) + 1;

		public static string DescribeCounters() => $"part records: staleMerged {Counter("staleMerged")}, staleDropped {Counter("staleDropped")}, skippedNoMask {Counter("skippedNoMask")}";

		private static NormalisedChange Normalise(CMS21_Together_Core.Data.CarLoaderEntry entry, CarPartsChangePacket change, int clientId)
		{
			var preconditions = change.Preconditions.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
			var result = new NormalisedChange();
			var dropped = new List<string>();
			int staleMerged = 0;
			foreach (var record in change.BodyParts)
			{
				entry.BodyParts.TryGetValue(record.PartIndex, out var stored);
				var outcome = PartRecordMerge.Normalise(stored, record, preconditions.TryGetValue(record.Key, out var pre) ? pre : null);
				result.Body.Add((record, outcome));
				Note(record.Key, outcome, dropped, ref staleMerged);
			}
			foreach (var record in change.SubParts)
			{
				entry.SubParts.TryGetValue(CarSubPartIdentity.BuildKey(record.PartIndexPath), out var stored);
				var outcome = PartRecordMerge.Normalise(stored, record, preconditions.TryGetValue(record.Key, out var pre) ? pre : null);
				result.Sub.Add((record, outcome));
				Note(record.Key, outcome, dropped, ref staleMerged);
			}
			if (dropped.Count > 0)
				Logger.Info($"[Cars] Change {change.TxId} from client {clientId} on loader {change.CarLoaderID}: dropped {string.Join(", ", dropped)}.");
			if (dropped.Count > 0 || staleMerged > 0) result.Summary = $", {dropped.Count} dropped, {staleMerged} stale merged";
			return result;
		}

		private static void Note<T>(string key, Normalised<T> outcome, List<string> dropped, ref int staleMerged) where T : class
		{
			switch (outcome.Outcome)
			{
				case MergeOutcome.StaleDropped:
					Count("staleDropped");
					dropped.Add($"{key} (stale {outcome.Stale.ToString().Replace(", ", "|")})");
					break;
				case MergeOutcome.Skipped:
					Count("skippedNoMask");
					dropped.Add($"{key} (no Changed mask)");
					break;
				case MergeOutcome.StaleMerged:
					Count("staleMerged");
					staleMerged++;
					break;
			}
		}

		private static bool OnlyExamines(CarPartsChangePacket change) =>
			change.BodyParts.Count == 0 && change.Preconditions.Count == 0 && change.InventoryDelta.RemovedItemUids.Count == 0 && change.InventoryDelta.RemovedGroupUids.Count == 0
			&& change.SubParts.All(r => r.Changed == PartFields.Examined);

		private static string FindConflict(CMS21_Together_Core.Data.CarLoaderEntry entry, CarPartsChangePacket change, NormalisedChange normalised, int clientId)
		{
			foreach (var precondition in change.Preconditions)
			{
				bool? stored = null;
				if (precondition.Key.StartsWith("b:") && int.TryParse(precondition.Key.Substring(2), out int index) && entry.BodyParts.TryGetValue(index, out var body))
					stored = body.Unmounted;
				else if (precondition.Key.StartsWith("s:") && entry.SubParts.TryGetValue(precondition.Key.Substring(2), out var sub))
					stored = sub.Unmounted;
				if (stored == null) return $"unknown part {precondition.Key}";
				if (stored.Value != precondition.WasUnmounted) return $"{precondition.Key} changed already";
			}

			foreach (string key in normalised.FlippedKeys(change))
			{
				int holder = CarLocks.ExclusiveOwner(change.CarLoaderID, key, clientId);
				if (holder >= 0) return $"{key} is locked by player {holder}";
				holder = CarLocks.SharedOwner(change.CarLoaderID, key, clientId);
				if (holder >= 0) CarLocks.CountUnlockedFlip(clientId, change.CarLoaderID, key, holder);
			}

			var inventory = GameDataManager.CurrentState.InventoryState;
			foreach (long uid in change.InventoryDelta.RemovedItemUids)
				if (inventory.InventoryItems.All(i => i.UID != uid))
				{
					if (InventoryChanges.RemovedByOther(uid, clientId)) return $"item {uid} is gone";
					Logger.Info($"[Cars] Change {change.TxId} removes item {uid}, which the server does not have and no other player took; ignored.");
				}
			foreach (long uid in change.InventoryDelta.RemovedGroupUids)
				if (inventory.InventoryGroupItems.All(g => g.UID != uid))
				{
					if (InventoryChanges.RemovedByOther(uid, clientId)) return $"group {uid} is gone";
					Logger.Info($"[Cars] Change {change.TxId} removes group {uid}, which the server does not have and no other player took; ignored.");
				}
			return null;
		}

	[PacketHandler(PacketTypes.CarPartsResyncRequest)]
			[AllowBeforeSync]
		public static void OnResyncRequest(long clientId, CarPartsResyncRequestPacket packet)
		{
			var entry = CarPartsStore.Get(packet.CarLoaderID);
			if (entry == null || !entry.HasBaseline) return;
			Logger.Info($"[Cars] Client {clientId} asked to resync loader {packet.CarLoaderID}: {packet.Reason}");
			CarPartsStore.SendSnapshot(packet.CarLoaderID, entry, CarPartsSnapshotPacket.LiveSnapshot, only: (int)clientId);
		}

	[PacketHandler(PacketTypes.CarPartsSnapshot)]
		public static void OnBaseline(long clientId, CarPartsSnapshotPacket packet)
		{
			string key = $"{clientId}/{packet.CarLoaderID}/{packet.SpawnSeq}";
			if (!incoming.TryGetValue(key, out var batches))
			{
				batches = new List<CarPartsSnapshotPacket>();
				incoming[key] = batches;
			}
			batches.Add(packet);
			if (!packet.IsLastBatch) return;
			incoming.Remove(key);

			var entry = CarPartsStore.Get(packet.CarLoaderID);
			if (entry == null || entry.SpawnSeq != packet.SpawnSeq)
			{
				Logger.Debug($"[Cars] Baseline from client {clientId} for loader {packet.CarLoaderID} SpawnSeq {packet.SpawnSeq} dropped (car replaced or gone).");
				return;
			}
			if (!entry.HasBaseline && entry.SpawnedBy != (int)clientId)
			{
				Logger.Warn($"[Cars] First baseline for loader {packet.CarLoaderID} came from client {clientId}, not the spawner {entry.SpawnedBy}; dropped.");
				return;
			}

			CarPartsStore.StoreBaseline(entry, packet.EngineSwap,
				batches.SelectMany(b => b.BodyParts), batches.SelectMany(b => b.SubParts));
			CarPartsStore.SendSnapshot(packet.CarLoaderID, entry, CarPartsSnapshotPacket.LiveSnapshot, except: (int)clientId);
			GameDataManager.RequestSave();
		}
	}
}
