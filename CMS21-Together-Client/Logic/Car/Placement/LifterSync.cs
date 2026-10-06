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

namespace CMS21Together.Logic.Car.Placement;

[HarmonyPatch]
public static class LifterSync
{
	private const float CarWaitSeconds = 25f;
	private const float StepWaitSeconds = 15f;

	private static readonly HashSet<int> applying = new HashSet<int>();

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	private static int IndexOf(CarLifter lifter)
	{
		var lifters = GarageLoader.Get()?.carLifter;
		for (int i = 0; lifters != null && i < lifters.Length; i++)
			if (lifters[i] == lifter) return i;
		return -1;
	}

	[HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
	[HarmonyPrefix]
	private static void BeforeAction(CarLifter __instance, out (int State, bool Moving) __state) =>
		__state = ((int)__instance.GetState(), __instance.isMoving);

	[HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
	[HarmonyPostfix]
	private static void AfterAction(CarLifter __instance, int actionType, (int State, bool Moving) __state)
	{
		if (!Active || __state.Moving || !__instance.isMoving) return;
		int index = IndexOf(__instance);
		if (index < 0 || applying.Contains(index)) return;
		int to = __state.State + (actionType == 0 ? 1 : -1);
		Log.Info($"[Placement] Lift {index}: {__state.State} -> {to}.");
		Client.Instance.Send(new LifterActionRequestPacket { LifterIndex = index, FromState = __state.State, ToState = to });
	}

	public static void OnLifterState(LifterStatePacket packet, int snapshotId)
	{
		MelonCoroutines.Start(Apply(packet, snapshotId));
	}

	private static IEnumerator Apply(LifterStatePacket packet, int snapshotId)
	{
		var lifters = GarageLoader.Get()?.carLifter;
		if (lifters == null || packet.LifterIndex < 0 || packet.LifterIndex >= lifters.Length)
		{
			Log.Error($"[Placement] Lift {packet.LifterIndex} not found.");
			SyncTracker.Applied(SyncOrder.CarPlacementKey, snapshotId);
			yield break;
		}
		var lifter = lifters[packet.LifterIndex];

		float deadline = Time.realtimeSinceStartup + CarWaitSeconds;
		while ((lifter.isMoving || lifter.GetConnectedCarLoader() == null && packet.State != 0) && Time.realtimeSinceStartup < deadline)
			yield return new WaitForSeconds(0.25f);

		if (lifter.GetConnectedCarLoader() == null && packet.State != 0)
		{
			Log.Error($"[Placement] Lift {packet.LifterIndex}: no car on it after {CarWaitSeconds} s; leaving it on the floor.");
		}
		else if (packet.Instant)
		{
			Set(packet.LifterIndex, lifter, packet.State);
		}
		else
		{
			while ((int)lifter.GetState() != packet.State)
			{
				applying.Add(packet.LifterIndex);
				try { lifter.Action(packet.State > (int)lifter.GetState() ? 0 : 1); }
				finally { applying.Remove(packet.LifterIndex); }
				if (!lifter.isMoving)
				{
					Set(packet.LifterIndex, lifter, packet.State);
					break;
				}
				deadline = Time.realtimeSinceStartup + StepWaitSeconds;
				while (lifter.isMoving && Time.realtimeSinceStartup < deadline) yield return new WaitForSeconds(0.1f);
			}
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
}
