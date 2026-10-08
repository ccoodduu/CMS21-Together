using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21Together.Network;

namespace CMS21Together.Logic;

// UIDManager starts at the profile's value on every client, so each player id hands out UIDs from its own range.
public static class UidRange
{
	public static long ServerFloor;

	public static void Apply()
	{
		if (Client.Instance == null || Client.Instance.ID <= 0) return;
		int id = Client.Instance.ID;
		long last = UidRanges.Start(id);
		var inventory = Singleton<GameManager>.Instance?.Inventory;
		if (inventory != null)
		{
			foreach (var item in inventory.items)
				if (UidRanges.Contains(id, item.UID) && item.UID > last) last = item.UID;
			foreach (var group in inventory.groups)
				if (UidRanges.Contains(id, group.UID) && group.UID > last) last = group.UID;
		}
		long local = last;
		if (UidRanges.Contains(id, ServerFloor) && ServerFloor > last) last = ServerFloor;
		long current = UIDManager.GetDataForSave();
		if (UidRanges.Contains(id, current) && current > last) last = current;
		UIDManager.LoadDataFromSave(last);
		Log.Info($"[Inventory] New UIDs for player {id} continue after {last} (inventory {local}, server {ServerFloor}, counter {current}).");
	}
}
