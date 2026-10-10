using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
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

	private const float LocalMoveSeconds = 15f;
	private static readonly Dictionary<IntPtr, (int Loader, int From, float Since)> localMoves = new Dictionary<IntPtr, (int, int, float)>();
	private static readonly List<(int Loader, int Place)> afterLocalMove = new List<(int, int)>();
	private static readonly Dictionary<int, (LifterStatePacket Packet, int SnapshotId)> liftersAfterLocalMove = new Dictionary<int, (LifterStatePacket, int)>();

	public static bool IsApplying(int loader) => applying.Contains(loader);
	private static readonly Dictionary<int, int> pendingPlaces = new Dictionary<int, int>();

	private static bool LocalMoveRunning
	{
		get
		{
			DropStaleLocalMoves();
			return localMoves.Count > 0;
		}
	}

	public static void Reset()
	{
		localMoves.Clear();
		afterLocalMove.Clear();
		liftersAfterLocalMove.Clear();
	}

	public static bool KeepLifterUntilLocalMoveEnds(LifterStatePacket packet, int snapshotId)
	{
		if (!LocalMoveRunning) return false;
		if (liftersAfterLocalMove.TryGetValue(packet.LifterIndex, out var replaced)) SyncTracker.Applied(SyncOrder.CarPlacementKey, replaced.SnapshotId);
		liftersAfterLocalMove[packet.LifterIndex] = (packet, snapshotId);
		Log.Info($"[Placement] Lift {packet.LifterIndex}: state {packet.State} kept until the local move ends.");
		return true;
	}

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
		if (CarAwaySync.BlockIfLocked(loader, "move") || !Locks.LockCarHooks.Gate(Locks.LockCarHooks.MoveAction(__instance.carLoader, __instance.pos, __instance.movePlayerToCar)))
		{
			__result = false;
			return false;
		}
		localMoves[__instance.Pointer] = (loader, from, UnityEngine.Time.realtimeSinceStartup);
		var other = CarLoaderPlaces.Get().GetCarLoaderForPlace((CarPlace)to);
		int toLift = LiftStateAfterArrival(__instance.carLoader, to);
		int fromLift = other == null ? 0 : LiftStateAfterArrival(other, from);
		Log.Info($"[Placement] Loader {loader}: moving {from} -> {to} (lift states after the move: to {toLift}, from {fromLift}).");
		Client.Instance.Send(new CarPlaceChangeRequestPacket { CarLoaderID = loader, FromPlace = from, ToPlace = to, ToLiftState = toLift, FromLiftState = fromLift });
		return true;
	}

	[HarmonyPatch(typeof(NotificationCenter._ChangeCarPos_d__20), nameof(NotificationCenter._ChangeCarPos_d__20.MoveNext))]
	[HarmonyPostfix]
	private static void AfterChangeCarPosStep(NotificationCenter._ChangeCarPos_d__20 __instance, bool __result)
	{
		if (__result || !localMoves.Remove(__instance.Pointer)) return;
		if (!LocalMoveRunning) ApplyAfterLocalMove();
	}

	private static void DropStaleLocalMoves()
	{
		float now = UnityEngine.Time.realtimeSinceStartup;
		var stale = localMoves.Where(m => now - m.Value.Since > LocalMoveSeconds).ToList();
		if (stale.Count == 0) return;
		foreach (var move in stale) localMoves.Remove(move.Key);
		Log.Warn($"[Placement] The local move of loader(s) {string.Join(", ", stale.Select(m => m.Value.Loader))} did not end in {LocalMoveSeconds:0} s.");
		if (localMoves.Count == 0) ApplyAfterLocalMove();
	}

	private static void ApplyAfterLocalMove()
	{
		var places = afterLocalMove.ToList();
		afterLocalMove.Clear();
		foreach (var (loader, place) in places)
		{
			var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
			if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad) || !carLoader.IsCarLoaded()) continue;
			ApplyPlace(carLoader, loader, place);
		}
		var lifters = liftersAfterLocalMove.Values.ToList();
		liftersAfterLocalMove.Clear();
		foreach (var (packet, snapshotId) in lifters) LifterSync.OnLifterState(packet, snapshotId);
	}

	public static bool IsMovingLocally(int loader) => LocalMoveRunning && localMoves.Values.Any(m => m.Loader == loader);

	// ChangeCarPos raises the lift to Middle when the car it puts on a lift misses a wheel or has wheels of different
	// sizes on one axle (native NotificationCenter.<ChangeCarPos>d__20 states 4 and 6).
	public static int LiftStateAfterArrival(CarLoader carLoader, int place)
	{
		if (place != (int)CarPlace.CarLifter1 && place != (int)CarPlace.CarLifter2) return 0;
		bool unsteady = carLoader.CheckIfHaveWheels() == CheckCarCanDriveState.MissingWheels
			|| !carLoader.FrontWheelsHaveThisSameSize() || !carLoader.RearWheelsHaveThisSameSize();
		return unsteady ? (int)CarLifterState.Middle : 0;
	}

	public static void OnPlaceChanged(CarPlaceChangedPacket packet)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(packet.CarLoaderID);
		if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return;
		if (!carLoader.IsCarLoaded() || CarLoading.IsLoading(packet.CarLoaderID))
		{
			pendingPlaces[packet.CarLoaderID] = packet.Place;
			Log.Info($"[Placement] Loader {packet.CarLoaderID}: place {packet.Place} kept until the car has loaded.");
			return;
		}
		// ChangeCarPos throws on a car that has no place yet, so such a car takes the server's place at once.
		if (LocalMoveRunning && !localMoves.Values.Any(m => m.Loader == packet.CarLoaderID && m.From < 0))
		{
			afterLocalMove.RemoveAll(p => p.Loader == packet.CarLoaderID);
			afterLocalMove.Add((packet.CarLoaderID, packet.Place));
			Log.Info($"[Placement] Loader {packet.CarLoaderID}: place {packet.Place} kept until the local move ends.");
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
