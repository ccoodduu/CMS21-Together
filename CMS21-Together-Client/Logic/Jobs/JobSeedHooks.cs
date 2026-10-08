using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21Together.Logic.Seeding;
using CMS21Together.Network;
using HarmonyLib;

namespace CMS21Together.Logic.Jobs;

// server-game-logic D8: the taking client prepares the job car inside random streams seeded from the order's PrepSeed,
// one per iterator, so the same order gives the same car on every take.
[HarmonyPatch]
public static class JobSeedHooks
{
	private static readonly HashSet<IntPtr> seeded = new HashSet<IntPtr>();

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid;

	private static bool Begin(IntPtr iterator, int state, int jobId, string kind)
	{
		if (state != 0) return seeded.Contains(iterator) && SeededStreams.Resume(iterator);
		if (seeded.Remove(iterator)) SeededStreams.Forget(iterator);
		if (!Connected || jobId < 0) return false;
		int seed = JobsSync.PrepSeedOf(jobId);
		if (seed == 0) return false;
		if (kind == "job" || kind == "mission") Log.Info($"[Jobs] Order {jobId}: preparing the car from seed {seed}.");
		seeded.Add(iterator);
		return SeededStreams.Begin(iterator, seed, kind, 0);
	}

	private static Exception End(Exception exception, bool begun, IntPtr iterator, bool result)
	{
		bool finished = exception != null || !result;
		SeededStreams.End(begun, iterator, finished);
		if (begun && finished) seeded.Remove(iterator);
		return exception;
	}

	private static int TakingJobOn(CarLoader carLoader) =>
		carLoader != null && JobsSync.PendingTake >= 0 && carLoader.orderConnection == JobsSync.PendingTake ? JobsSync.PendingTake : -1;

	[HarmonyPatch(typeof(OrderGenerator._TakeJob_d__19), nameof(OrderGenerator._TakeJob_d__19.MoveNext))]
	[HarmonyPrefix]
	private static void TakeJobStep(OrderGenerator._TakeJob_d__19 __instance, out bool __state) =>
		__state = Begin(__instance.Pointer, __instance.__1__state, __instance.id, "job");

	[HarmonyPatch(typeof(OrderGenerator._TakeJob_d__19), nameof(OrderGenerator._TakeJob_d__19.MoveNext))]
	[HarmonyFinalizer]
	private static Exception TakeJobStepEnd(Exception __exception, bool __state, bool __result, OrderGenerator._TakeJob_d__19 __instance) =>
		End(__exception, __state, __instance.Pointer, __result);

	[HarmonyPatch(typeof(OrderGenerator._TakeMission_d__22), nameof(OrderGenerator._TakeMission_d__22.MoveNext))]
	[HarmonyPrefix]
	private static void TakeMissionStep(OrderGenerator._TakeMission_d__22 __instance, out bool __state) =>
		__state = Begin(__instance.Pointer, __instance.__1__state, __instance.id, "mission");

	[HarmonyPatch(typeof(OrderGenerator._TakeMission_d__22), nameof(OrderGenerator._TakeMission_d__22.MoveNext))]
	[HarmonyFinalizer]
	private static Exception TakeMissionStepEnd(Exception __exception, bool __state, bool __result, OrderGenerator._TakeMission_d__22 __instance) =>
		End(__exception, __state, __instance.Pointer, __result);

	[HarmonyPatch(typeof(CarLoader._LoadCar_d__215), nameof(CarLoader._LoadCar_d__215.MoveNext))]
	[HarmonyPrefix]
	private static void LoadCarStep(CarLoader._LoadCar_d__215 __instance, out bool __state) =>
		__state = Begin(__instance.Pointer, __instance.__1__state, TakingJobOn(__instance.__4__this), "job-load");

	[HarmonyPatch(typeof(CarLoader._LoadCar_d__215), nameof(CarLoader._LoadCar_d__215.MoveNext))]
	[HarmonyFinalizer]
	private static Exception LoadCarStepEnd(Exception __exception, bool __state, bool __result, CarLoader._LoadCar_d__215 __instance) =>
		End(__exception, __state, __instance.Pointer, __result);

	[HarmonyPatch(typeof(CarLoader._SetRandomColorPanels_d__321), nameof(CarLoader._SetRandomColorPanels_d__321.MoveNext))]
	[HarmonyPrefix]
	private static void ColourPanelsStep(CarLoader._SetRandomColorPanels_d__321 __instance, out bool __state) =>
		__state = Begin(__instance.Pointer, __instance.__1__state, TakingJobOn(__instance.__4__this), "job-panels");

	[HarmonyPatch(typeof(CarLoader._SetRandomColorPanels_d__321), nameof(CarLoader._SetRandomColorPanels_d__321.MoveNext))]
	[HarmonyFinalizer]
	private static Exception ColourPanelsStepEnd(Exception __exception, bool __state, bool __result, CarLoader._SetRandomColorPanels_d__321 __instance) =>
		End(__exception, __state, __instance.Pointer, __result);
}
