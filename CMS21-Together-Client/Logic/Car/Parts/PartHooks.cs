using CMS21_Together_Core.Data.Enum;
using CMS21Together.Data;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Parts;

[HarmonyPatch]
public static class PartHooks
{
	[HarmonyPatch(typeof(PartScript), nameof(PartScript.Hide))]
	[HarmonyPostfix]
	private static void AfterHide(PartScript __instance) => MarkPart(__instance);

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.DoMount))]
	[HarmonyPostfix]
	private static void AfterDoMount(PartScript __instance) => MarkPart(__instance);

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.ShowMounted))]
	[HarmonyPostfix]
	private static void AfterShowMounted(PartScript __instance) => MarkPart(__instance);

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.Examine))]
	[HarmonyPostfix]
	private static void AfterExamine(PartScript __instance) => MarkPart(__instance);

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), typeof(string))]
	[HarmonyPostfix]
	private static void AfterTakeOffCarPart(CarLoader __instance, bool __runOriginal)
	{
		if (__runOriginal) MarkLoader(__instance);
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SwitchCarPart), typeof(string))]
	[HarmonyPostfix]
	private static void AfterSwitchCarPart(CarLoader __instance) => MarkLoader(__instance);

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.ExamineAllParts))]
	[HarmonyPostfix]
	private static void AfterExamineAll(CarLoader __instance) => MarkLoader(__instance);

	private static void MarkPart(PartScript script)
	{
		CMS21_Together_Core.Logging.Log.Debug($"[Parts] Hook on part {script?.id} (garage ready {ClientScene.IsGarageReady}).");
		if (!ClientScene.IsGarageReady || script == null) return;
		foreach (var sync in CarPartsSync.All)
		{
			if (sync.Registry != null && sync.Registry.TryGetSubPath(script, out _))
			{
				PartTransactions.OpenForPart(sync.Loader, sync.Registry, script);
				PartChangeTracker.MarkDirty(sync.Loader);
				return;
			}
		}
		Logic.Tools.EngineStandParts.MarkPart(script);
	}

	private static void MarkLoader(CarLoader carLoader)
	{
		if (!ClientScene.IsGarageReady || carLoader == null) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		if (loader >= 0) PartChangeTracker.MarkDirty(loader);
	}
}
