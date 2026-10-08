using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Cars
{
	public static class InventoryChanges
	{
		public const int UnknownRemover = -1;

		private static readonly Dictionary<long, int> removedBy = new Dictionary<long, int>();

		public static void NoteRemoved(long uid, int clientId)
		{
			lock (removedBy) removedBy[uid] = clientId;
		}

		public static bool RemovedByOther(long uid, int clientId)
		{
			lock (removedBy) return removedBy.TryGetValue(uid, out int remover) && remover != clientId;
		}

		public static string DescribeRemover(long uid)
		{
			int remover;
			lock (removedBy)
				if (!removedBy.TryGetValue(uid, out remover)) return "never seen";
			if (remover == UnknownRemover) return "removed by the server";
			return $"removed by client {remover} '{Presence.PresenceRegistry.Get(remover)?.Username ?? "left"}'";
		}

		public static IEnumerable<long> StillHeld(InventoryDelta delta)
		{
			var inventory = GameDataManager.CurrentState.InventoryState;
			return delta.RemovedItemUids.Where(uid => inventory.InventoryItems.Any(i => i.UID == uid))
				.Concat(delta.RemovedGroupUids.Where(uid => inventory.InventoryGroupItems.Any(g => g.UID == uid)))
				.ToList();
		}

		public static void Apply(InventoryDelta delta, int clientId)
		{
			if (delta == null || delta.IsEmpty) return;
			var inventory = GameDataManager.CurrentState.InventoryState;
			foreach (var item in inventory.InventoryItems.Where(i => delta.RemovedItemUids.Contains(i.UID))) NoteRemoved(item.UID, clientId);
			foreach (var group in inventory.InventoryGroupItems.Where(g => delta.RemovedGroupUids.Contains(g.UID))) NoteRemoved(group.UID, clientId);
			inventory.InventoryItems.RemoveAll(i => delta.RemovedItemUids.Contains(i.UID));
			inventory.InventoryGroupItems.RemoveAll(g => delta.RemovedGroupUids.Contains(g.UID));
			foreach (var item in delta.AddedItems)
				if (inventory.InventoryItems.All(i => i.UID != item.UID)) inventory.InventoryItems.Add(item);
			foreach (var group in delta.AddedGroups)
				if (inventory.InventoryGroupItems.All(g => g.UID != group.UID)) inventory.InventoryGroupItems.Add(group);
		}
	}
}
