using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using HarmonyLib;
using CMS21Together.Logic.Car.Away;

namespace CMS21Together.Logic.Car.Placement;

// The pie menu builds NotificationCenter.<ChangeCarPos>d__20 inline, so the move is detected in its first MoveNext
// step (docs/spikes/car-placement.md). Remote moves skip the coroutine, which fades the screen for every player.
[HarmonyPatch]
public static class CarPlacementSync
{
	public static event Action<int> BeforeRemoteCarMove;

	private static readonly HashSet<int> applying = new HashSet<int>();
	private static readonly Dictionary<int, int> pendingPlaces = new Dictionary<int, int>();

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	[HarmonyPatch(typeof(NotificationCenter._ChangeCarPos_d__20), nameof(NotificationCenter._ChangeCarPos_d__20.MoveNext))]
	[HarmonyPrefix]
	private static bool BeforeChangeCarPosStep(NotificationCenter._ChangeCarPos_d__20 __instance, ref bool __result)
	{
		if (!Active || __instance.__1__state != 0 || __instance.carLoader == null) return true;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance.carLoader);
		if (loader < 0 || applying.Contains(loader) || !CarPartsSync.IsReady(loader)) return true;
		int from = __instance.carLoader.GetPlaceNo();
		int to = (int)__instance.pos;
		if (from == to) return true;
		if (CarAwaySync.BlockIfLocked(loader, "move"))
		{
			__result = false;
			return false;
		}
		Log.Info($"[Placement] Loader {loader}: moving {from} -> {to}.");
		Client.Instance.Send(new CarPlaceChangeRequestPacket { CarLoaderID = loader, FromPlace = from, ToPlace = to });
		return true;
	}

	public static void OnPlaceChanged(CarPlaceChangedPacket packet)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(packet.CarLoaderID);
		if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return;
		if (!carLoader.IsCarLoaded())
		{
			pendingPlaces[packet.CarLoaderID] = packet.Place;
			Log.Info($"[Placement] Loader {packet.CarLoaderID}: place {packet.Place} kept until the car has loaded.");
			return;
		}
		ApplyPlace(carLoader, packet.CarLoaderID, packet.Place);
	}

	public static void ForgetPendingPlace(int loader) => pendingPlaces.Remove(loader);

	public static int TakePendingPlace(int loader, int place)
	{
		if (!pendingPlaces.TryGetValue(loader, out int pending)) return place;
		pendingPlaces.Remove(loader);
		return pending;
	}

	public static void ApplyPlace(CarLoader carLoader, int loader, int place)
	{
		if (place < 0 || carLoader.GetPlaceNo() == place && carLoader.IsInPlace((CarPlace)place)) return;
		BeforeRemoteCarMove?.Invoke(loader);
		applying.Add(loader);
		try
		{
			carLoader.ResetCarLifter();
			carLoader.ChangePosition(place);
			Log.Info($"[Placement] Loader {loader} placed at {(CarPlace)place}.");
		}
		finally
		{
			applying.Remove(loader);
		}
	}
}
