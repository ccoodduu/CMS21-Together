using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Garage;
using CMS21Together.Logic.Hook;
using CMS21Together.Logic.Jobs;
using CMS21Together.Logic.Tools;
using CMS21Together.Logic.Tools.CarTools;
using CMS21Together.Network.Handlers;
using CMS21Together.Network;
using CMS21Together.UI;

namespace CMS21Together.Logic.Reconciliation;

// Client half of desync-detection-and-resync D1/D2 and state-merges-and-contention D11: answers the server's digest
// requests with hashes of the local game state, projected through the same Core mappers the server uses, or "not
// ready" while that state is being changed.
public static class ClientDigests
{
	private const float WarehouseMoveSeconds = 3f;

	public static Dictionary<string, string> HeldWrong { get; } = new Dictionary<string, string>();

	public static HashSet<string> HeldNotReady { get; } = new HashSet<string>();

	public static void OnRequest(StateDigestRequestPacket request)
	{
		if (!ClientData.IsInitialSyncFinished || !ClientScene.IsGarageReady || SyncTracker.InSnapshot) return;
		var answer = new StateDigestPacket { Seq = request.Seq, Trigger = DigestTrigger.Poll };
		foreach (var entry in request.Entries)
		{
			var projection = Project(entry.Key, entry.SubKey);
			answer.Entries.Add(new DigestEntry
			{
				Key = entry.Key, SubKey = entry.SubKey ?? "", NotReady = projection == null, Hash = projection == null ? 0 : HashOf(entry.Key, projection)
			});
		}
		Client.Instance.Send(answer);
	}

	public static StateDigestPacket FullDigest(DigestTrigger trigger)
	{
		var digest = new StateDigestPacket { Seq = -1, Trigger = trigger };
		var keys = DigestMappers.GlobalKeys.Select(k => (Key: k, SubKey: "")).ToList();
		foreach (var sync in CarPartsSync.All.Where(s => s.Registry != null))
			keys.AddRange(DigestMappers.CarKeys.Select(k => (k, sync.Loader.ToString())));
		foreach (var (key, subKey) in keys)
		{
			var projection = Project(key, subKey);
			digest.Entries.Add(new DigestEntry { Key = key, SubKey = subKey, NotReady = projection == null, Hash = projection == null ? 0 : HashOf(key, projection) });
		}
		return digest;
	}

	public static void OnDetailRequest(StateDetailRequestPacket request)
	{
		Client.Instance.Send(new StateDetailPacket { Key = request.Key, SubKey = request.SubKey, Projection = Project(request.Key, request.SubKey) ?? new Projection() });
	}

	public static void OnNotice(DesyncNoticePacket notice)
	{
		Log.Warn($"[Desync] {notice.Key} {notice.SubKey} is out of sync (persistent: {notice.Persistent}).");
		if (notice.Persistent) ModNotify.ShowToast($"Something is out of sync ({Describe(notice)}). Press {PlayerSettings.ResyncKey} to resync.");
	}

	private static ulong HashOf(string key, Projection projection) =>
		HeldWrong.ContainsKey(key) ? 0xBADBADBADUL : projection.Hash();

	private static string Describe(DesyncNoticePacket notice) =>
		notice.Key == DigestMappers.CarsKey ? $"car on loader {notice.SubKey}"
		: notice.Key == DigestMappers.DetailsKey ? $"car details on loader {notice.SubKey}" : notice.Key;

	public static Projection Project(string key, string subKey)
	{
		if (HeldNotReady.Contains(key)) return null;
		switch (key)
		{
			case DigestMappers.WorldKey:
				return DigestMappers.World(GlobalData.PlayerMoney, GlobalData.PlayerScraps, GlobalData.PlayerLevel + 1, GlobalData.PlayerExp);
			case DigestMappers.InventoryKey:
				return Inventory();
			case DigestMappers.CarsKey:
				return int.TryParse(subKey, out int loader) ? Car(loader) : null;
			case DigestMappers.PlacementKey:
				return Placement();
			case DigestMappers.DetailsKey:
				return int.TryParse(subKey, out int detailsLoader) ? Details(detailsLoader) : null;
			case DigestMappers.ToolsKey:
				return Tools();
			case DigestMappers.WarehouseKey:
				return Warehouse();
			case DigestMappers.GarageKey:
				return Garage();
			case DigestMappers.JobsKey:
				return Jobs();
			default:
				return null;
		}
	}

	private static Projection Details(int loader)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded() || !CarPartsSync.IsReady(loader) || CarAwaySync.All.ContainsKey(loader)) return null;
		if (OilBinHooks.IsDraining(carLoader) || LockLifecycle.All.Any(t => t.Loader == loader && t.FlushFluids)) return null;
		var details = CarDetailsIO.Read(carLoader, CarDetailsIO.All);
		return CarDetailsSync.IsSettled(loader, details) ? DigestMappers.Details(details) : null;
	}

	private static Projection Tools()
	{
		if (ToolSync.IsBusy || ToolSync.SentRecently || WheelBalancerOpen()) return null;
		return DigestMappers.Tools(ToolSync.Machines.Where(m => m.Present).Select(m => m.ReadLocal()));
	}

	private static bool WheelBalancerOpen()
	{
		var window = CMS.UI.WindowManager.Instance?.GetWindowByID<CMS.UI.Windows.WheelBalanceWindow>(CMS.UI.WindowID.WheelBalance);
		return window != null && window.isActive;
	}

	private static Projection Warehouse()
	{
		if (InventoryHandlers.FullSyncOpen || UnityEngine.Time.realtimeSinceStartup - NotificationCenterItemsHook.LastWarehouseMoveAt < WarehouseMoveSeconds) return null;
		var warehouse = Singleton<GameManager>.Instance?.Warehouse;
		var all = warehouse?.GetAllItemsAndGroups();
		if (all == null) return null;
		var items = new List<CMS21_Together_Core.Data.GameType.ModItem>();
		var groups = new List<CMS21_Together_Core.Data.GameType.ModGroupItem>();
		for (int i = 0; i < all.Count; i++)
		{
			var group = all[i].TryCast<GroupItem>();
			if (group != null) groups.Add(group.ToModGroupItem());
			else
			{
				var item = all[i].TryCast<Item>();
				if (item != null) items.Add(item.ToModItem());
			}
		}
		return DigestMappers.Warehouse(items, groups);
	}

	private static Projection Garage()
	{
		var system = GameData.Instance?.GarageTools?.upgradeSystem;
		if (system == null || system.UpgradesForMoney == null || system.UpgradesForPoints == null) return null;
		if (GarageLookSync.LastApplied == null || GarageLookSync.IsApplying) return null;
		return DigestMappers.Garage(Unlocked(system.UpgradesForMoney), Unlocked(system.UpgradesForPoints), GlobalData.BarnsAmount, GarageLookSync.LastApplied);
	}

	private static Dictionary<string, bool[]> Unlocked(Il2CppSystem.Collections.Generic.List<Upgrade> upgrades)
	{
		var result = new Dictionary<string, bool[]>();
		foreach (var upgrade in upgrades)
		{
			if (upgrade?.Unlocked == null || string.IsNullOrEmpty(upgrade.ID)) continue;
			var levels = new bool[upgrade.Unlocked.Length];
			for (int i = 0; i < levels.Length; i++) levels[i] = upgrade.Unlocked[i];
			result[upgrade.ID] = levels;
		}
		return result;
	}

	private static Projection Jobs()
	{
		var generator = Singleton<GameManager>.Instance?.OrderGenerator;
		if (generator == null || JobsSync.PendingTake >= 0) return null;
		var orders = new List<int>();
		for (int i = 0; generator.jobs != null && i < generator.jobs.Count; i++) orders.Add(generator.jobs[i].id);
		var active = new List<KeyValuePair<int, int>>();
		for (int i = 0; generator.selectedJobs != null && i < generator.selectedJobs.Count; i++)
			active.Add(new KeyValuePair<int, int>(generator.selectedJobs[i].id, generator.selectedJobs[i].carLoaderID));
		return DigestMappers.Jobs(orders, active);
	}

	private static Projection Inventory()
	{
		if (PartTransactions.HoldsInventoryChanges) return null;
		var inventory = Singleton<GameManager>.Instance.Inventory;
		var items = new List<CMS21_Together_Core.Data.GameType.ModItem>();
		var gameItems = inventory.GetItems();
		for (int i = 0; gameItems != null && i < gameItems.Count; i++) items.Add(gameItems[i].ToModItem());
		var groups = new List<CMS21_Together_Core.Data.GameType.ModGroupItem>();
		var gameGroups = inventory.GetGroups();
		for (int i = 0; gameGroups != null && i < gameGroups.Count; i++) groups.Add(gameGroups[i].ToModGroupItem());
		return DigestMappers.Inventory(items, groups, null, null);
	}

	private static Projection Car(int loader)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == loader);
		if (carLoader == null || sync?.Registry == null || !CarPartsSync.IsReady(loader) || sync.Pending.Count > 0) return null;
		if (PartChangeTracker.IsPending(loader) || PartTransactions.HasOpen(loader)) return null;
		var body = new List<CarBodyPartUpdatePacket>();
		var sub = new List<CarSubPartUpdatePacket>();
		CarPartsSync.CaptureAll(carLoader, sync.Registry, body, sub);
		return DigestMappers.Car(carLoader.carToLoad, body, sub);
	}

	private static Projection Placement()
	{
		var places = CarLoaderPlaces.Get();
		var garage = GarageLoader.Get();
		var data = Singleton<GameManager>.Instance.GameDataManager;
		if (places == null || garage?.carLifter == null) return null;

		var lifters = new Dictionary<int, int>();
		for (int i = 0; i < garage.carLifter.Length; i++)
		{
			if (garage.carLifter[i].isMoving) return null;
			lifters[i] = (int)garage.carLifter[i].GetState();
		}
		var cars = new Dictionary<int, int>();
		for (int i = 0; i < places.GetCarLoadersCount(); i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) continue;
			if (!CarPartsSync.IsReady(i)) return null;
			cars[i] = carLoader.GetPlaceNo();
		}
		var parked = new Dictionary<int, string>();
		for (int slot = 0; slot < GlobalData.GetMaxParkingPlacesAmount(); slot++)
		{
			var car = data.LoadCarInParking(slot);
			if (car != null && !car.IsDefault()) parked[slot] = car.carToLoad;
		}
		return DigestMappers.Placement(lifters, cars, parked, GlobalData.UnlockedParkingLevels);
	}
}
