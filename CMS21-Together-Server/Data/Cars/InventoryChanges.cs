using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Cars
{
	public static class InventoryChanges
	{
		private static readonly HashSet<long> removedUids = new HashSet<long>();

		public static void NoteRemoved(long uid)
		{
			lock (removedUids) removedUids.Add(uid);
		}

		public static bool WasRemoved(long uid)
		{
			lock (removedUids) return removedUids.Contains(uid);
		}

		public static void Apply(InventoryDelta delta)
		{
			if (delta == null || delta.IsEmpty) return;
			var inventory = GameDataManager.CurrentState.InventoryState;
			foreach (long uid in delta.RemovedItemUids.Concat(delta.RemovedGroupUids)) NoteRemoved(uid);
			inventory.InventoryItems.RemoveAll(i => delta.RemovedItemUids.Contains(i.UID));
			inventory.InventoryGroupItems.RemoveAll(g => delta.RemovedGroupUids.Contains(g.UID));
			foreach (var item in delta.AddedItems)
				if (inventory.InventoryItems.All(i => i.UID != item.UID)) inventory.InventoryItems.Add(item);
			foreach (var group in delta.AddedGroups)
				if (inventory.InventoryGroupItems.All(g => g.UID != group.UID)) inventory.InventoryGroupItems.Add(group);
		}
	}
}
