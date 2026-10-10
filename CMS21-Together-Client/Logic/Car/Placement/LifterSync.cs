using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using CMS21Together.Logic.Car.Away;

namespace CMS21Together.Logic.Car.Placement;

[HarmonyPatch]
public static class LifterSync
{
	private const float CarWaitSeconds = 25f;
	private const float StepWaitSeconds = 15f;
	private const int MaxSteps = 3;
	private const float StuckSeconds = 20f;
	private const float StuckCheckSeconds = 1f;

	private static readonly HashSet<int> applying = new HashSet<int>();
	private static readonly Dictionary<int, int> remoteSteps = new Dictionary<int, int>();
	private static readonly Dictionary<int, float> movingSince = new Dictionary<int, float>();
	private static float nextStuckCheck;

	public static bool IsApplying(int lifterIndex) => applying.Contains(lifterIndex) || remoteSteps.ContainsKey(lifterIndex);

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	private static int IndexOf(CarLifter lifter)
	{
		var lifters = GarageLoader.Get()?.carLifter;
		for (int i = 0; lifters != null && i < lifters.Length; i++)
			if (lifters[i] == lifter) return i;
		return -1;
	}

	private static bool InGarage(int index, CarLifter lifter)
	{
		if (!ClientScene.IsGarageReady || lifter == null) return false;
		var lifters = GarageLoader.Get()?.carLifter;
		return lifters != null && index < lifters.Length && lifters[index] == lifter;
	}

	[HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
	[HarmonyPrefix]
	private static bool BeforeAction(CarLifter __instance, int actionType, out (int State, bool Moving) __state)
	{
		if (__instance == null)
		{
			__state = default;
			return false;
		}
		__state = ((int)__instance.GetState(), __instance.isMoving);
		int index = IndexOf(__instance);
		var connected = __instance.GetConnectedCarLoader();
		if (!Active || applying.Contains(index) || connected == null) return true;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(connected);
		if (loader < 0) return true;
		return !CarAwaySync.BlockIfLocked(loader, "lift") && Locks.LockCarHooks.Gate(Locks.LockCarHooks.LiftAction(__instance, index, actionType));
	}

	[HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
	[HarmonyPostfix]
	private static void AfterAction(CarLifter __instance, int actionType, (int State, bool Moving) __state, bool __runOriginal)
	{
		if (!__runOriginal || __instance == null || !Active || __state.Moving || !__instance.isMoving) return;
		int index = IndexOf(__instance);
		if (index < 0 || applying.Contains(index)) return;
		int to = __state.State + (actionType == 0 ? 1 : -1);
		Log.Info($"[Placement] Lift {index}: {__state.State} -> {to}.");
		Client.Instance.Send(new LifterActionRequestPacket { LifterIndex = index, FromState = __state.State, ToState = to });
	}

	public static void OnLifterState(LifterStatePacket packet, int snapshotId)
	{
		if (CarPlacementSync.KeepLifterUntilLocalMoveEnds(packet, snapshotId)) return;
		MelonCoroutines.Start(Apply(packet, snapshotId));
	}

	private static IEnumerator Apply(LifterStatePacket packet, int snapshotId)
	{
		int index = packet.LifterIndex;
		var lifters = GarageLoader.Get()?.carLifter;
		if (lifters == null || index < 0 || index >= lifters.Length)
		{
			Log.Error($"[Placement] Lift {index} not found.");
			SyncTracker.Applied(SyncOrder.CarPlacementKey, snapshotId);
			yield break;
		}
		var lifter = lifters[index];
		remoteSteps[index] = (remoteSteps.TryGetValue(index, out int running) ? running : 0) + 1;
		try
		{
			float deadline = Time.realtimeSinceStartup + CarWaitSeconds;
			while (InGarage(index, lifter) && (lifter.isMoving || lifter.GetConnectedCarLoader() == null && packet.State != 0) && Time.realtimeSinceStartup < deadline)
				yield return new WaitForSeconds(0.25f);

			if (!InGarage(index, lifter))
			{
				Log.Info($"[Placement] Lift {index}: step to {packet.State} dropped, the garage was left.");
			}
			else if (lifter.GetConnectedCarLoader() == null && packet.State != 0)
			{
				Log.Error($"[Placement] Lift {index}: no car on it after {CarWaitSeconds} s; leaving it on the floor.");
			}
			else if (packet.Instant)
			{
				Set(index, lifter, packet.State);
			}
			else
			{
				for (int step = 0; step < MaxSteps && InGarage(index, lifter) && (int)lifter.GetState() != packet.State; step++)
				{
					applying.Add(index);
					try { lifter.Action(packet.State > (int)lifter.GetState() ? 0 : 1); }
					finally { applying.Remove(index); }
					if (!lifter.isMoving)
					{
						Set(index, lifter, packet.State);
						break;
					}
					deadline = Time.realtimeSinceStartup + StepWaitSeconds;
					while (InGarage(index, lifter) && lifter.isMoving && Time.realtimeSinceStartup < deadline) yield return new WaitForSeconds(0.1f);
				}
				if (!InGarage(index, lifter))
				{
					Log.Info($"[Placement] Lift {index}: step to {packet.State} dropped, the garage was left.");
				}
				else if ((int)lifter.GetState() != packet.State)
				{
					if (lifter.isMoving) Log.Warn($"[Placement] Lift {index}: still {(int)lifter.GetState()} and moving after {MaxSteps} steps toward {packet.State}.");
					else Set(index, lifter, packet.State);
				}
			}
		}
		finally
		{
			if (--remoteSteps[index] <= 0) remoteSteps.Remove(index);
		}
		SyncTracker.Applied(SyncOrder.CarPlacementKey, snapshotId);
	}

	private static void Set(int index, CarLifter lifter, int state)
	{
		if ((int)lifter.GetState() == state) return;
		applying.Add(index);
		try { lifter.InstantSet(state, true); }
		finally { applying.Remove(index); }
		Log.Info($"[Placement] Lift {index} set to {state}.");
	}

	public static void Update()
	{
		if (Time.realtimeSinceStartup < nextStuckCheck) return;
		nextStuckCheck = Time.realtimeSinceStartup + StuckCheckSeconds;
		var lifters = ClientScene.IsGarageReady ? GarageLoader.Get()?.carLifter : null;
		if (lifters == null)
		{
			movingSince.Clear();
			return;
		}
		for (int i = 0; i < lifters.Length; i++)
		{
			var lifter = lifters[i];
			if (lifter == null || !lifter.isMoving)
			{
				movingSince.Remove(i);
				continue;
			}
			if (!movingSince.TryGetValue(i, out float since))
			{
				movingSince[i] = Time.time;
				continue;
			}
			if (Time.time - since < StuckSeconds) continue;
			movingSince.Remove(i);
			Unstick(i, lifter);
		}
	}

	// The game's lift coroutine and tween callbacks throw when the car is disconnected mid-move, and isMoving stays
	// set; the lift then ignores every Action and InstantSet.
	private static void Unstick(int index, CarLifter lifter)
	{
		bool hasCar = lifter.GetConnectedCarLoader() != null;
		int state = hasCar ? (int)lifter.GetState() : 0;
		Log.Warn($"[Placement] Lift {index}: still moving after {StuckSeconds:0} s ({(hasCar ? "car on it" : "no car")}); setting it to {state}.");
		lifter.isMoving = false;
		applying.Add(index);
		try { lifter.InstantSet(state, true); }
		finally { applying.Remove(index); }
	}
}
