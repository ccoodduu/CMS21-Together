using System.Linq;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Cars
{
	public static class InventoryChanges
	{
		public static void Apply(InventoryDelta delta)
		{
			if (delta == null || delta.IsEmpty) return;
			var inventory = GameDataManager.CurrentState.InventoryState;
			inventory.InventoryItems.RemoveAll(i => delta.RemovedItemUids.Contains(i.UID));
			inventory.InventoryGroupItems.RemoveAll(g => delta.RemovedGroupUids.Contains(g.UID));
			foreach (var item in delta.AddedItems)
				if (inventory.InventoryItems.All(i => i.UID != item.UID)) inventory.InventoryItems.Add(item);
			foreach (var group in delta.AddedGroups)
				if (inventory.InventoryGroupItems.All(g => g.UID != group.UID)) inventory.InventoryGroupItems.Add(group);
		}
	}
}
