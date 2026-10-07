using System;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

[HarmonyPatch]
public static class GeneratorHooks
{
	private const int FirstWaitState = 1;
	private const int WaitState = 2;

	private static float holdStartedAt = -1f;

	public static float LastHoldSeconds { get; private set; }

	public static void Reset()
	{
		holdStartedAt = -1f;
		LastHoldSeconds = 0f;
	}

	private static bool Hold(GameScene scene, int state)
	{
		if (state != FirstWaitState && state != WaitState) return false;
		if (OutdoorSession.HoldsGenerator(scene))
		{
			if (holdStartedAt < 0f)
			{
				holdStartedAt = Time.realtimeSinceStartup;
				Log.Info($"[Outdoor] {scene} generator held until the instance arrives.");
			}
			return true;
		}
		if (holdStartedAt >= 0f)
		{
			LastHoldSeconds = Time.realtimeSinceStartup - holdStartedAt;
			holdStartedAt = -1f;
			Log.Info($"[Outdoor] {scene} generator released after {LastHoldSeconds:0.00} s ({OutdoorSession.Visit}).");
		}
		return false;
	}

	[HarmonyPatch(typeof(JunkyardGenerator._Generate_d__18), nameof(JunkyardGenerator._Generate_d__18.MoveNext))]
	[HarmonyPrefix]
	private static bool JunkyardStep(JunkyardGenerator._Generate_d__18 __instance, ref bool __result, out bool __state)
	{
		__state = false;
		int state = __instance.__1__state;
		if (Hold(GameScene.Junkyard, state))
		{
			__instance.__1__state = WaitState;
			__instance.__2__current = null;
			__result = true;
			return false;
		}
		var generator = __instance.__4__this;
		if (OutdoorSession.IsShared && OutdoorSession.Instance.FillAllSpawnPoints && generator != null)
			generator.CarsPercentage = new Vector2(100f, 100f);
		__state = Reseed.Begin("junkyard", state, __instance._i_5__11, -1);
		return true;
	}

	[HarmonyPatch(typeof(JunkyardGenerator._Generate_d__18), nameof(JunkyardGenerator._Generate_d__18.MoveNext))]
	[HarmonyFinalizer]
	private static Exception JunkyardStepEnd(Exception __exception, bool __state)
	{
		Reseed.End(__state);
		return __exception;
	}

	[HarmonyPatch(typeof(JunkyardGenerator._CreateCar_d__19), nameof(JunkyardGenerator._CreateCar_d__19.MoveNext))]
	[HarmonyPrefix]
	private static void JunkyardCarStep(JunkyardGenerator._CreateCar_d__19 __instance, out bool __state) =>
		__state = Reseed.Begin("junkyard-car", __instance.__1__state, 0, __instance.index);

	[HarmonyPatch(typeof(JunkyardGenerator._CreateCar_d__19), nameof(JunkyardGenerator._CreateCar_d__19.MoveNext))]
	[HarmonyPostfix]
	private static void JunkyardCarDone(JunkyardGenerator._CreateCar_d__19 __instance, bool __result)
	{
		if (!__result) OutdoorCarSync.Created(__instance._carLoader_5__3, __instance.index);
	}

	[HarmonyPatch(typeof(JunkyardGenerator._CreateCar_d__19), nameof(JunkyardGenerator._CreateCar_d__19.MoveNext))]
	[HarmonyFinalizer]
	private static Exception JunkyardCarStepEnd(Exception __exception, bool __state)
	{
		Reseed.End(__state);
		return __exception;
	}

	[HarmonyPatch(typeof(ShedManager._Generate_d__29), nameof(ShedManager._Generate_d__29.MoveNext))]
	[HarmonyPrefix]
	private static bool BarnStep(ShedManager._Generate_d__29 __instance, ref bool __result, out bool __state)
	{
		__state = false;
		int state = __instance.__1__state;
		if (Hold(GameScene.Barn, state))
		{
			__instance.__1__state = WaitState;
			__instance.__2__current = null;
			__result = true;
			return false;
		}
		__state = Reseed.Begin("barn", state, __instance._i_5__12, -1);
		return true;
	}

	[HarmonyPatch(typeof(ShedManager._Generate_d__29), nameof(ShedManager._Generate_d__29.MoveNext))]
	[HarmonyFinalizer]
	private static Exception BarnStepEnd(Exception __exception, bool __state)
	{
		Reseed.End(__state);
		return __exception;
	}

	[HarmonyPatch(typeof(ShedManager._CreateCar_d__30), nameof(ShedManager._CreateCar_d__30.MoveNext))]
	[HarmonyPrefix]
	private static void BarnCarStep(ShedManager._CreateCar_d__30 __instance, out bool __state) =>
		__state = Reseed.Begin("barn-car", __instance.__1__state, 0, __instance.index);

	[HarmonyPatch(typeof(ShedManager._CreateCar_d__30), nameof(ShedManager._CreateCar_d__30.MoveNext))]
	[HarmonyPostfix]
	private static void BarnCarDone(ShedManager._CreateCar_d__30 __instance, bool __result)
	{
		if (!__result) OutdoorCarSync.Created(__instance._cl_5__3, __instance.index);
	}

	[HarmonyPatch(typeof(ShedManager._CreateCar_d__30), nameof(ShedManager._CreateCar_d__30.MoveNext))]
	[HarmonyFinalizer]
	private static Exception BarnCarStepEnd(Exception __exception, bool __state)
	{
		Reseed.End(__state);
		return __exception;
	}

	[HarmonyPatch(typeof(JunkyardGenerator), nameof(JunkyardGenerator.CreateCar))]
	[HarmonyPrefix]
	private static void JunkyardCreateCar(int index, ref CarsIdWithConfig randomCar) =>
		OutdoorCarSync.ApplyPick(GameScene.Junkyard, index, ref randomCar);

	[HarmonyPatch(typeof(ShedManager), nameof(ShedManager.CreateCar))]
	[HarmonyPrefix]
	private static void BarnCreateCar(int index, ref CarsIdWithConfig carIdWithConfig) =>
		OutdoorCarSync.ApplyPick(GameScene.Barn, index, ref carIdWithConfig);
}
