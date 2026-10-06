using CMS21_Together_Core.Data.GameType;
using CMS21Together.Logic.Car.Details;
using HarmonyLib;

namespace CMS21Together.Logic.Tools.CarTools;

[HarmonyPatch]
public static class CarWashHooks
{
	[HarmonyPatch(typeof(CarWashLogic), nameof(CarWashLogic.DoWorkAnim))]
	[HarmonyPrefix]
	private static void BeforeWork(CarLoader carLoader) => CarToolActions.Started(ModToolId.CarWash, carLoader, ToolActionKind.Wash);

	[HarmonyPatch(typeof(CarWashLogic._DoWorkAnim_d__1), nameof(CarWashLogic._DoWorkAnim_d__1.MoveNext))]
	[HarmonyPostfix]
	private static void AfterWorkStep(CarWashLogic._DoWorkAnim_d__1 __instance, bool __result)
	{
		if (__result) return;
		CarToolActions.Finished(ToolActionKind.Wash);
		CarToolActions.MarkDetails(__instance.carLoader, CarDetailSection.BodyCosmetics);
	}

	// The paint shop and tint windows offer to wash a dirty car first; that wash sets every part at once (part == null).
	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.EnableDust))]
	[HarmonyPostfix]
	private static void AfterEnableDust(CarLoader __instance, CarPart part)
	{
		if (part == null) CarDetailsSync.MarkDirty(__instance, CarDetailSection.BodyCosmetics);
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SetWashFactor))]
	[HarmonyPostfix]
	private static void AfterSetWashFactor(CarLoader __instance, CarPart part)
	{
		if (part == null) CarDetailsSync.MarkDirty(__instance, CarDetailSection.BodyCosmetics);
	}
}
