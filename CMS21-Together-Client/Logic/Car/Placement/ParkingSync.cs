using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Hook;
using CMS21Together.Network;
using CMS21Together.UI;
using CMS.UI.Windows;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Car.Placement;

// Parked cars live on the server as NewCarData blobs; the local profile's parking slots are a mirror written through
// GameDataManager.SaveCarInParking. Park/unpark hooks act only in the garage scene: the same calls run for purchases
// outside the garage and when the parking scene shows a car (docs/spikes/outdoor-scenes.md).
[HarmonyPatch]
public static class ParkingSync
{
	private const float ParkingMarkSeconds = 5f;
	private const float LoadTimeoutSeconds = 90f;
	private const int BaseParkingLevelPrice = 50000;

	public static event Action<CarParkResultPacket> ParkResultReceived;

	private static readonly Dictionary<int, Guid> mirror = new Dictionary<int, Guid>();
	private static readonly HashSet<int> parking = new HashSet<int>();
	private static int nextRequestId = 1;
	private static bool applying;

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	private static GameDataManager Data => Singleton<GameManager>.Instance.GameDataManager;

	public static bool IsParking(int loader) => parking.Contains(loader);

	public static void Reset()
	{
		mirror.Clear();
		parking.Clear();
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SaveCarToFile), typeof(int), typeof(bool))]
	[HarmonyPostfix]
	private static void AfterSaveCarToFile(CarLoader __instance, int index, bool toParking)
	{
		if (!toParking || !Active || applying) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance);
		var data = Data.LoadCarInParking(index);
		if (loader < 0 || data == null || data.IsDefault()) return;
		parking.Add(loader);
		MelonCoroutines.Start(ForgetParking(loader));
		var request = new CarParkRequestPacket { RequestId = nextRequestId++, CarLoaderID = loader, PreferredSlot = index, Car = NewCarDataCodec.ToParkedCar(data) };
		Log.Info($"[Parking] Loader {loader}: parking {data.carToLoad} in slot {index} ({request.Car.Data.Length} bytes).");
		Client.Instance.Send(request);
	}

	private static IEnumerator ForgetParking(int loader)
	{
		yield return new WaitForSeconds(ParkingMarkSeconds);
		parking.Remove(loader);
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.LoadCarFromFile), typeof(int), typeof(bool))]
	[HarmonyPrefix]
	private static void BeforeLoadCarFromFile(CarLoader __instance, int index, bool fromParking)
	{
		if (!fromParking || !Active || applying) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance);
		if (loader < 0) return;
		CarSpawnHooks.Suppress(loader);
		MelonCoroutines.Start(SendUnpark(__instance, loader, index));
	}

	private static IEnumerator SendUnpark(CarLoader carLoader, int loader, int slot)
	{
		float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
		while (!carLoader.IsCarLoaded() && Time.realtimeSinceStartup < deadline) yield return null;
		deadline = Time.realtimeSinceStartup + 2f;
		while (carLoader.GetPlaceNo() < 0 && Time.realtimeSinceStartup < deadline) yield return null;
		CarSpawnHooks.Release(loader);
		if (!carLoader.IsCarLoaded()) yield break;
		mirror.TryGetValue(slot, out var id);
		Log.Info($"[Parking] Loader {loader}: unparking slot {slot} ({carLoader.carToLoad}) to place {carLoader.GetPlaceNo()}.");
		Client.Instance.Send(new CarUnparkRequestPacket
		{
			Slot = slot, ParkedCarId = id, CarLoaderID = loader, Place = carLoader.GetPlaceNo(), ConfigVersion = carLoader.ConfigVersion
		});
	}

	[HarmonyPatch(typeof(CMS.Managers.ParkingCarPlaceManager), nameof(CMS.Managers.ParkingCarPlaceManager.MoveCar))]
	[HarmonyPostfix]
	private static void AfterMoveCar(int from, int to)
	{
		if (!Active || applying) return;
		mirror.TryGetValue(from, out var fromId);
		mirror.TryGetValue(to, out var toId);
		Log.Info($"[Parking] Swapping slots {from} and {to}.");
		Client.Instance.Send(new ParkingMoveRequestPacket { From = from, To = to, FromId = fromId, ToId = toId });
	}

	[HarmonyPatch(typeof(ParkingManagementWindow), "UnlockParkingLevelAction")]
	[HarmonyPrefix]
	private static bool BeforeUnlock(ParkingManagementWindow __instance)
	{
		if (!Active) return true;
		int levels = GlobalData.UnlockedParkingLevels;
		int price = __instance.parkingLevelPrice > 0 ? __instance.parkingLevelPrice : levels * BaseParkingLevelPrice;
		Log.Info($"[Parking] Asking to unlock level {levels + 1} for {price}.");
		Client.Instance.Send(new ParkingLevelUnlockRequestPacket { TargetLevels = levels + 1, Price = price });
		return false;
	}

	[HarmonyPatch(typeof(ParkingWindow), "MoveCarToGarageAction")]
	[HarmonyPrefix]
	private static bool BeforeMoveCarToGarage()
	{
		if (Client.Instance == null || !Client.Instance.IsConnectionValid) return true;
		ModNotify.ShowToast("Take the car out from the parking menu in the garage while playing together.");
		return false;
	}

	public static void OnParkResult(CarParkResultPacket result)
	{
		if (!result.Accepted) Log.Warn($"[Parking] Park request {result.RequestId} refused: {result.Reason}.");
		if (!result.Accepted && result.Reason == ParkRefusal.ParkingFull) ModNotify.ShowToast("The parking is full.");
		ParkResultReceived?.Invoke(result);
	}

	public static void OnSlotUpdate(ParkingSlotUpdatePacket packet, int snapshotId)
	{
		WriteSlot(packet.Slot, packet.Car);
		RefreshWindow();
		SyncTracker.Applied(SyncOrder.CarPlacementKey, snapshotId);
	}

	public static void OnState(ParkingStatePacket packet, int snapshotId)
	{
		GlobalData.UnlockedParkingLevels = packet.UnlockedLevels;
		var profile = Data.CurrentProfileData;
		if (profile?.globalDataWrapper != null) profile.globalDataWrapper.UnlockedParkingLevels = packet.UnlockedLevels;
		int usable = ParkingLayout.UsableSlots(packet.UnlockedLevels);
		foreach (int slot in mirror.Keys.Concat(Enumerable.Range(0, usable)).Distinct().ToList())
			if (!packet.Occupied.ContainsKey(slot) && !IsEmpty(slot)) WriteSlot(slot, null);
		RefreshWindow();
		SyncTracker.Applied(SyncOrder.CarPlacementKey, snapshotId);
	}

	private static bool IsEmpty(int slot)
	{
		var car = Data.LoadCarInParking(slot);
		return car == null || car.IsDefault();
	}

	private static void WriteSlot(int slot, ParkedCar car)
	{
		NewCarData data = null;
		if (car != null && car.SaveVersion == NewCarDataCodec.SaveVersion) data = NewCarDataCodec.Deserialize(car.Data, (byte)car.SaveVersion);
		else if (car != null) Log.Warn($"[Parking] Slot {slot}: {car.CarToLoad} has save version {car.SaveVersion}, this game uses {NewCarDataCodec.SaveVersion}; leaving the slot empty.");
		applying = true;
		try { Data.SaveCarInParking(data ?? new NewCarData(), slot); }
		finally { applying = false; }
		if (car == null) mirror.Remove(slot);
		else mirror[slot] = car.Id;
		Log.Info($"[Parking] Slot {slot}: {(car == null ? "empty" : car.CarToLoad)}.");
	}

	private static void RefreshWindow()
	{
		var window = UnityEngine.Object.FindObjectOfType<ParkingManagementWindow>();
		if (window != null && window.isActiveAndEnabled) window.RefreshPanels();
	}

	public static List<object> Describe() =>
		mirror.OrderBy(p => p.Key).Select(p => (object)new { slot = p.Key, id = p.Value.ToString() }).ToList();
}
