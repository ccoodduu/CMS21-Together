using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data.Outdoor
{
	public static class LootService
	{
		public static bool Record(OutdoorInstance instance, int playerId, OutdoorLootRecordPacket packet)
		{
			if (!OutdoorScenes.HasPiles(instance.Scene))
			{
				Logger.Info($"[Outdoor] {instance.Label}: loot record from {playerId} ignored, the scene has no piles.");
				return false;
			}
			if (playerId != instance.GeneratorId)
			{
				Logger.Info($"[Outdoor] {instance.Label}: loot record from {playerId} ignored, the generator is {instance.GeneratorId}.");
				return false;
			}
			if (instance.LootRecorded)
			{
				Logger.Info($"[Outdoor] {instance.Label}: second loot record from {playerId} ignored.");
				return false;
			}

			instance.Piles = (packet.Piles ?? new List<LootPile>()).Where(p => p != null).ToList();
			instance.RandomShowActive = packet.RandomShowActive?.ToList() ?? new List<bool>();
			int duplicates = 0;
			foreach (var pile in instance.Piles)
			{
				pile.Items = (pile.Items ?? new List<ModItem>()).Where(i => i != null).ToList();
				foreach (var item in pile.Items)
				{
					if (instance.Loot.ContainsKey(item.UID))
					{
						duplicates++;
						continue;
					}
					instance.Loot[item.UID] = new LootEntry { Uid = item.UID, PileIndex = pile.Index, PileKey = pile.Key, Item = item };
				}
			}
			Logger.Info($"[Outdoor] {instance.Label}: loot recorded by {playerId}, {instance.Piles.Count} piles, {instance.Loot.Count} items{(duplicates > 0 ? $", {duplicates} duplicate UIDs dropped" : "")}.");
			return true;
		}

		public static void Take(int playerId, LootTakePacket packet)
		{
			var instance = OutdoorInstances.ForMember(playerId, packet.InstanceId);
			if (instance == null || !instance.Loot.TryGetValue(packet.Uid, out var entry))
			{
				Logger.Info($"[Outdoor] Take of {packet.Uid} by {playerId} in instance {packet.InstanceId} refused: {(instance == null ? "not a member" : "unknown item")}.");
				OutdoorNet.Send(playerId, new LootTakeRefusedPacket { InstanceId = packet.InstanceId, Uid = packet.Uid, By = 0, ByName = null });
				return;
			}

			if (entry.Status == LootItemStatus.Held && entry.HolderId == playerId) return;
			if (entry.Status != LootItemStatus.Available)
			{
				Logger.Info($"[Outdoor] {instance.Label}: take of {entry.Item.ID}#{entry.Uid} by {playerId} refused, {entry.Status} by {entry.HolderId}.");
				OutdoorNet.Send(playerId, new LootTakeRefusedPacket { InstanceId = instance.InstanceId, Uid = entry.Uid, By = entry.HolderId, ByName = OutdoorNet.NameOf(entry.HolderId) });
				return;
			}

			entry.Status = LootItemStatus.Held;
			entry.HolderId = playerId;
			Logger.Info($"[Outdoor] {instance.Label}: {entry.Item.ID}#{entry.Uid} from pile {entry.PileIndex} taken by {playerId}.");
			OutdoorNet.SendTo(instance.Members, Update(instance, entry, playerId), except: playerId);
		}

		public static void PutBack(int playerId, LootPutBackPacket packet)
		{
			var instance = OutdoorInstances.ForMember(playerId, packet.InstanceId);
			if (instance == null || !instance.Loot.TryGetValue(packet.Uid, out var entry) || entry.Status != LootItemStatus.Held || entry.HolderId != playerId)
			{
				Logger.Info($"[Outdoor] Put back of {packet.Uid} by {playerId} in instance {packet.InstanceId} ignored: not held by that player.");
				return;
			}
			MakeAvailable(instance, entry, playerId, "put back");
		}

		public static void ReleaseHeld(OutdoorInstance instance, int playerId, string why)
		{
			foreach (var entry in instance.Loot.Values.Where(e => e.Status == LootItemStatus.Held && e.HolderId == playerId).ToList())
				MakeAvailable(instance, entry, playerId, why);
		}

		private static void MakeAvailable(OutdoorInstance instance, LootEntry entry, int playerId, string why)
		{
			entry.Status = LootItemStatus.Available;
			entry.HolderId = 0;
			Logger.Info($"[Outdoor] {instance.Label}: {entry.Item.ID}#{entry.Uid} back in pile {entry.PileIndex} ({why} by {playerId}).");
			OutdoorNet.SendTo(instance.Members, Update(instance, entry, playerId), except: playerId);
		}

		private static LootUpdatePacket Update(OutdoorInstance instance, LootEntry entry, int by) => new LootUpdatePacket
		{
			InstanceId = instance.InstanceId,
			Uid = entry.Uid,
			Status = entry.Status,
			By = by,
			PileIndex = entry.PileIndex,
			PileKey = entry.PileKey,
			Item = entry.Status == LootItemStatus.Available ? entry.Item : null,
		};

		public static List<ModItem> HeldBy(int playerId, int instanceId, IEnumerable<ModItem> items, out List<ModItem> dropped)
		{
			var instance = OutdoorInstances.ForMember(playerId, instanceId);
			var accepted = new List<ModItem>();
			dropped = new List<ModItem>();
			foreach (var item in items ?? Enumerable.Empty<ModItem>())
			{
				if (item == null) continue;
				if (instance != null && instance.Loot.TryGetValue(item.UID, out var entry) && entry.Status == LootItemStatus.Held && entry.HolderId == playerId && !accepted.Any(a => a.UID == item.UID))
					accepted.Add(item);
				else
					dropped.Add(item);
			}
			if (dropped.Count > 0)
				Logger.Warn($"[Outdoor] Purchase by {playerId} in instance {instanceId}: {dropped.Count} items not held by the buyer dropped ({string.Join(", ", dropped.Select(i => $"{i.ID}#{i.UID}"))}).");
			return accepted;
		}

		public static void MarkBought(int playerId, int instanceId, IEnumerable<long> uids)
		{
			var instance = OutdoorInstances.Get(instanceId);
			if (instance == null) return;
			int count = 0;
			foreach (long uid in uids)
			{
				if (!instance.Loot.TryGetValue(uid, out var entry)) continue;
				entry.Status = LootItemStatus.Bought;
				entry.HolderId = playerId;
				count++;
			}
			Logger.Info($"[Outdoor] {instance.Label}: {count} items bought by {playerId}.");
		}
	}
}
