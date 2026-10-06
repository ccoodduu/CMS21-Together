using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using CMS21Together.UI;

namespace CMS21Together.Logic.Reconciliation;

// Client half of desync-detection-and-resync D1/D2: answers the server's digest requests with hashes of the local
// game state, projected through the same Core mappers the server uses.
public static class ClientDigests
{
	public static Dictionary<string, string> HeldWrong { get; } = new Dictionary<string, string>();

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
		notice.Key == DigestMappers.CarsKey ? $"car on loader {notice.SubKey}" : notice.Key;

	public static Projection Project(string key, string subKey)
	{
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
			default:
				return null;
		}
	}

	private static Projection Inventory()
	{
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
		if (PartChangeTracker.IsPending(loader) || PartTransactions.HasOpen(loader) || PartClaims.Held(loader).Count > 0) return null;
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
