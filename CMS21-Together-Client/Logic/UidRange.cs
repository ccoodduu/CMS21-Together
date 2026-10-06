using CMS21_Together_Core.Logging;
using CMS21Together.Network;

namespace CMS21Together.Logic;

// UIDManager starts at the profile's value on every client, so each player id hands out UIDs from its own range.
public static class UidRange
{
	private const long RangeSize = 1_000_000_000_000L;

	public static void Apply()
	{
		if (Client.Instance == null || Client.Instance.ID <= 0) return;
		long start = Client.Instance.ID * RangeSize;
		long end = start + RangeSize;
		long last = start;
		var inventory = Singleton<GameManager>.Instance?.Inventory;
		if (inventory != null)
		{
			foreach (var item in inventory.items)
				if (item.UID >= start && item.UID < end && item.UID > last) last = item.UID;
			foreach (var group in inventory.groups)
				if (group.UID >= start && group.UID < end && group.UID > last) last = group.UID;
		}
		UIDManager.LoadDataFromSave(last);
		Log.Info($"[Inventory] New UIDs for player {Client.Instance.ID} continue after {last}.");
	}
}
