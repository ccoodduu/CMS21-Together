using System;
using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Tools.CarTools;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockFluidHooks
{
	private static readonly HashSet<IntPtr> extractorsDone = new HashSet<IntPtr>();

	public static void Reset() => extractorsDone.Clear();

	public static int LoaderOf(CarLoader carLoader) => carLoader == null ? -1 : CarLoaderPlaces.Get()?.GetCarLoaderId(carLoader) ?? -1;

	private static bool Gate(GatedAction action) => action == null || LockGate.Enter(action);

	public static GatedAction RefillAction(FluidRefill tool)
	{
		var game = GameScript.Get();
		var carLoader = game?.GetIOMouseOverCarLoader2();
		int loader = LoaderOf(carLoader);
		if (loader < 0 || tool.IsActive) return null;
		int fluidId = carLoader.CurrentUsedFluidId;
		string key = LockSets.FluidKey(tool.carFluidType, fluidId);
		var set = LockSets.ForFluid(loader, CarLockKind.Fluid, key);
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = set, Target = tool, TargetKey = key,
			Context = () => !tool.IsActive && carLoader != null && CarPartsSync.SpawnSeq(loader) == seq,
			Run = () =>
			{
				game.IOMouseOverCarLoader = carLoader;
				carLoader.CurrentUsedFluidId = fluidId;
				tool.Use();
			},
			Started = () => tool.IsActive,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, key, finished: () => !tool.IsActive).FlushFluids = true,
		};
	}

	public static GatedAction ExtractorAction(FluidExtractor tool)
	{
		var game = GameScript.Get();
		var carLoader = game?.GetIOMouseOverCarLoader2();
		int loader = LoaderOf(carLoader);
		if (loader < 0 || tool.IsActive) return null;
		var type = carLoader.CurrentUsedFluid;
		int fluidId = carLoader.CurrentUsedFluidId;
		string key = LockSets.FluidKey(type, fluidId);
		var set = LockSets.ForFluid(loader, CarLockKind.Fluid, key);
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = set, Target = tool, TargetKey = key,
			Context = () => !tool.IsActive && carLoader != null && CarPartsSync.SpawnSeq(loader) == seq,
			Run = () =>
			{
				game.IOMouseOverCarLoader = carLoader;
				carLoader.CurrentUsedFluid = type;
				carLoader.CurrentUsedFluidId = fluidId;
				extractorsDone.Remove(tool.Pointer);
				tool.Use();
			},
			Started = () => tool.IsActive,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, key, finished: () => !tool.IsActive || extractorsDone.Contains(tool.Pointer)).FlushFluids = true,
		};
	}

	public static GatedAction OilDrainAction(CarLoader carLoader)
	{
		int loader = LoaderOf(carLoader);
		if (loader < 0 || OilBinHooks.IsDraining(carLoader)) return null;
		var set = LockSets.ForFluid(loader, CarLockKind.OilDrain, LockSets.OilKey);
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = set, Target = carLoader, TargetKey = LockSets.OilKey,
			Context = () => carLoader != null && !OilBinHooks.IsDraining(carLoader) && CarPartsSync.SpawnSeq(loader) == seq,
			Run = () => carLoader.UseOilbin(),
			Started = () => OilBinHooks.IsDraining(carLoader),
			OnStarted = lockId => LockLifecycle.Track(lockId, set, LockSets.OilKey, finished: () => !OilBinHooks.IsDraining(carLoader)).FlushFluids = true,
		};
	}

	[HarmonyPatch(typeof(FluidRefill), nameof(FluidRefill.Use))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeRefillUse(FluidRefill __instance) => !LockGate.Active || Gate(RefillAction(__instance));

	[HarmonyPatch(typeof(FluidExtractor), nameof(FluidExtractor.Use))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeExtractorUse(FluidExtractor __instance) => !LockGate.Active || Gate(ExtractorAction(__instance));

	[HarmonyPatch(typeof(FluidExtractor._UseAnim_d__5), nameof(FluidExtractor._UseAnim_d__5.MoveNext))]
	[HarmonyPostfix]
	private static void AfterExtractorStep(FluidExtractor._UseAnim_d__5 __instance, bool __result)
	{
		var tool = __instance.__4__this;
		if (!__result && tool != null) extractorsDone.Add(tool.Pointer);
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.UseOilbin))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeUseOilbin(CarLoader __instance) => !LockGate.Active || Gate(OilDrainAction(__instance));
}
