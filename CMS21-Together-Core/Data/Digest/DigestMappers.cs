using System.Collections.Generic;
using System.Globalization;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Core.Data.Digest;

// One mapper per section, shared by client and server, so both sides project the same fields the same way.
public static class DigestMappers
{
	public const string WorldKey = SyncOrder.WorldKey;
	public const string InventoryKey = SyncOrder.InventoryKey;
	public const string CarsKey = SyncOrder.CarsKey;
	public const string PlacementKey = SyncOrder.CarPlacementKey;
	public const string DetailsKey = SyncOrder.CarDetailsKey;
	public const string ToolsKey = SyncOrder.WorkshopToolsKey;
	public const string WarehouseKey = "warehouse";
	public const string GarageKey = SyncOrder.GarageKey;
	public const string JobsKey = SyncOrder.JobsKey;

	public static readonly string[] GlobalKeys = { WorldKey, InventoryKey, PlacementKey, ToolsKey, WarehouseKey, GarageKey, JobsKey };

	public static readonly string[] CarKeys = { CarsKey, DetailsKey };

	public static Projection World(int money, int scraps, int level, int exp) => new Projection()
		.Add("world", "money", money)
		.Add("world", "scraps", scraps)
		.Add("world", "level", level)
		.Add("world", "exp", exp);

	public static Projection Inventory(IEnumerable<ModItem> items, IEnumerable<ModGroupItem> groups,
		IEnumerable<ModItem> warehouseItems, IEnumerable<ModGroupItem> warehouseGroups)
	{
		var projection = new Projection();
		AddItems(projection, "inv", items);
		AddGroups(projection, "inv", groups);
		AddItems(projection, "wh", warehouseItems);
		AddGroups(projection, "wh", warehouseGroups);
		return projection;
	}

	private static void AddItems(Projection projection, string prefix, IEnumerable<ModItem> items)
	{
		if (items == null) return;
		foreach (var item in items)
		{
			string id = $"{prefix}:item:{item.UID.ToString(CultureInfo.InvariantCulture)}";
			projection.Add(id, "id", item.ID).Add(id, "condition", item.Condition).Add(id, "quality", item.Quality);
		}
	}

	private static void AddGroups(Projection projection, string prefix, IEnumerable<ModGroupItem> groups)
	{
		if (groups == null) return;
		foreach (var group in groups)
		{
			string id = $"{prefix}:group:{group.UID.ToString(CultureInfo.InvariantCulture)}";
			projection.Add(id, "id", group.ID).Add(id, "size", group.ItemList?.Count ?? 0);
			if (group.ItemList == null) continue;
			for (int i = 0; i < group.ItemList.Count; i++)
				projection.Add(id, $"item{i}", $"{group.ItemList[i].ID}@{group.ItemList[i].Condition.ToString("0.000", CultureInfo.InvariantCulture)}");
		}
	}

	public static Projection Car(string carToLoad, IEnumerable<CarBodyPartUpdatePacket> body, IEnumerable<CarSubPartUpdatePacket> sub)
	{
		var projection = new Projection().Add("car", "carToLoad", carToLoad);
		foreach (var record in body)
		{
			string id = record.Key;
			projection.Add(id, "unmounted", record.Unmounted).Add(id, "switched", record.Switched).Add(id, "tunedId", record.TunedID)
				.Add(id, "condition", record.State?.Condition ?? 0f).Add(id, "dent", record.State?.Dent ?? 0f).Add(id, "quality", record.State?.Quality ?? 0);
		}
		foreach (var record in sub)
		{
			string id = record.Key;
			projection.Add(id, "unmounted", record.Unmounted).Add(id, "partId", record.EffectiveId).Add(id, "condition", record.Condition)
				.Add(id, "quality", record.Quality).Add(id, "examined", record.IsExamined);
		}
		return projection;
	}

	public static Projection Placement(IDictionary<int, int> lifterStates, IDictionary<int, int> carPlaces, IDictionary<int, string> parkedCars, int unlockedLevels)
	{
		var projection = new Projection().Add("parking", "levels", unlockedLevels);
		foreach (var pair in lifterStates) projection.Add($"lift:{pair.Key}", "state", pair.Value);
		foreach (var pair in carPlaces) projection.Add($"loader:{pair.Key}", "place", pair.Value);
		foreach (var pair in parkedCars) projection.Add($"slot:{pair.Key}", "car", pair.Value);
		return projection;
	}

	public static Projection Details(ModCarDetails details)
	{
		var projection = new Projection();
		foreach (var pair in CarDetailEntries.Split(details))
		{
			if (pair.Key == CarDetailEntries.Info) projection.Add(pair.Key, "mileage", ((ModCarInfo)pair.Value).Mileage);
			else projection.Add(pair.Key, "value", CarDetailEntries.Signature(pair.Value));
		}
		return projection;
	}

	public static Projection Tools(IEnumerable<ToolSlotState> slots)
	{
		var projection = new Projection();
		foreach (var slot in slots)
		{
			if (slot == null || slot.IsEmpty || slot.Uid == 0 || !ModTools.IsMachine(slot.Tool)) continue;
			string id = $"tool:{slot.Tool}";
			projection.Add(id, "uid", slot.Uid.ToString(CultureInfo.InvariantCulture)).Add(id, "item", slot.Item?.ID ?? slot.Group?.ID)
				.Add(id, "mounting", slot.Mounting).Add(id, "balanced", slot.Balanced);
		}
		return projection;
	}

	public static Projection Warehouse(IEnumerable<ModItem> items, IEnumerable<ModGroupItem> groups)
	{
		var projection = new Projection();
		AddItems(projection, "wh", items);
		AddGroups(projection, "wh", groups);
		return projection;
	}

	public static Projection Garage(IDictionary<string, bool[]> garageUpgrades, IDictionary<string, bool[]> skills, int barns)
	{
		var projection = new Projection().Add("garage", "barns", barns);
		AddUnlocked(projection, "upgrade", garageUpgrades);
		AddUnlocked(projection, "skill", skills);
		return projection;
	}

	private static void AddUnlocked(Projection projection, string prefix, IDictionary<string, bool[]> levels)
	{
		if (levels == null) return;
		foreach (var pair in levels)
			for (int i = 0; pair.Value != null && i < pair.Value.Length; i++)
				if (pair.Value[i]) projection.Add($"{prefix}:{pair.Key}", $"level{i}", true);
	}

	public static Projection Jobs(IEnumerable<int> orderIds, IEnumerable<KeyValuePair<int, int>> activeJobLoaders)
	{
		var projection = new Projection();
		foreach (int id in orderIds) projection.Add($"order:{id}", "open", true);
		foreach (var pair in activeJobLoaders) projection.Add($"job:{pair.Key}", "loader", pair.Value);
		return projection;
	}
}
