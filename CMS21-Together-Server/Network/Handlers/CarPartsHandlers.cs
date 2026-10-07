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
				Logger.Debug($"[Cars] Change {change.TxId} from client {clientId} for loader {change.CarLoaderID} dropped (no car or baseline for SpawnSeq {change.SpawnSeq}).");
				return;
			}

			string conflict = !OnlyExamines(entry, change) && CarAwayRegistry.Blocks(change.CarLoaderID, (int)clientId, $"change {change.TxId}")
				? "the car is away"
				: FindConflict(entry, change);
			if (conflict != null)
			{
				var reject = new CarPartsChangeResultPacket
				{
					CarLoaderID = change.CarLoaderID, SpawnSeq = change.SpawnSeq, TxId = change.TxId,
					Accepted = false, Reason = conflict, Revision = entry.Revision
				};
				foreach (var record in change.BodyParts)
					if (entry.BodyParts.TryGetValue(record.PartIndex, out var stored)) reject.BodyParts.Add(stored);
				foreach (var record in change.SubParts)
					if (entry.SubParts.TryGetValue(CarSubPartIdentity.BuildKey(record.PartIndexPath), out var stored)) reject.SubParts.Add(stored);
				reject.RestoreUids.AddRange(change.InventoryDelta.RemovedItemUids.Concat(change.InventoryDelta.RemovedGroupUids));
				Server.SendToClient(reject, (int)clientId);
				Logger.Info($"[Cars] Change {change.TxId} from client {clientId} on loader {change.CarLoaderID} rejected: {conflict}");
				return;
			}

			var merged = new List<CarSubPartUpdatePacket>();
			entry.Revision++;
			foreach (var record in change.BodyParts)
			{
				record.Revision = entry.Revision;
				entry.BodyParts[record.PartIndex] = record;
			}
			foreach (var record in change.SubParts)
			{
				string key = CarSubPartIdentity.BuildKey(record.PartIndexPath);
				if (entry.SubParts.TryGetValue(key, out var stored) && stored.IsExamined && !record.IsExamined)
				{
					record.IsExamined = true;
					merged.Add(record);
				}
				record.Revision = entry.Revision;
				entry.SubParts[key] = record;
			}
			InventoryChanges.Apply(change.InventoryDelta);

			CarClaims.ReleaseCommitted((int)clientId, change.CarLoaderID,
				change.BodyParts.Select(b => PartKeys.Body(b.PartIndex)).Concat(change.SubParts.Select(s => PartKeys.Sub(s.PartIndexPath))));
			change.Revision = entry.Revision;
			Server.SendToClient(new CarPartsChangeResultPacket
			{
				CarLoaderID = change.CarLoaderID, SpawnSeq = change.SpawnSeq, TxId = change.TxId, Accepted = true, Revision = entry.Revision, SubParts = merged
			}, (int)clientId);
			Server.SendToClients(change, (int)clientId);
			Logger.Info($"[Cars] Change {change.TxId} from client {clientId} on loader {change.CarLoaderID}: revision {entry.Revision} ({change.BodyParts.Count} body, {change.SubParts.Count} mechanical, inventory +{change.InventoryDelta.AddedItems.Count + change.InventoryDelta.AddedGroups.Count} -{change.InventoryDelta.RemovedItemUids.Count + change.InventoryDelta.RemovedGroupUids.Count})."); 
		}

		private static bool OnlyExamines(CMS21_Together_Core.Data.CarLoaderEntry entry, CarPartsChangePacket change)
		{
			if (change.BodyParts.Count > 0 || change.Preconditions.Count > 0 || change.InventoryDelta.RemovedItemUids.Count > 0 || change.InventoryDelta.RemovedGroupUids.Count > 0)
				return false;
			foreach (var record in change.SubParts)
			{
				if (!entry.SubParts.TryGetValue(CarSubPartIdentity.BuildKey(record.PartIndexPath), out var stored)) return false;
				bool examined = record.IsExamined;
				int revision = record.Revision;
				record.IsExamined = stored.IsExamined;
				record.Revision = stored.Revision;
				bool same = Newtonsoft.Json.JsonConvert.SerializeObject(record) == Newtonsoft.Json.JsonConvert.SerializeObject(stored);
				record.IsExamined = examined;
				record.Revision = revision;
				if (!same) return false;
			}
			return true;
		}

		private static string FindConflict(CMS21_Together_Core.Data.CarLoaderEntry entry, CarPartsChangePacket change)
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

			var inventory = GameDataManager.CurrentState.InventoryState;
			foreach (long uid in change.InventoryDelta.RemovedItemUids)
				if (inventory.InventoryItems.All(i => i.UID != uid)) return $"item {uid} is gone";
			foreach (long uid in change.InventoryDelta.RemovedGroupUids)
				if (inventory.InventoryGroupItems.All(g => g.UID != uid)) return $"group {uid} is gone";
			return null;
		}

		[PacketHandler(PacketTypes.CarPartClaim)]
		public static void OnClaim(long clientId, CarPartClaimPacket packet)
		{
			CarClaims.Handle((int)clientId, packet, ServerTime.Time);
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
