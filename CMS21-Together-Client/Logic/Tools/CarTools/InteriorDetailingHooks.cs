using CMS21_Together_Core.Data.GameType;
using HarmonyLib;

namespace CMS21Together.Logic.Tools.CarTools;

// The stationary kit uses the car at the car wash and the same InteriorDetailingToolkitLogic as the portable kit.
[HarmonyPatch]
public static class InteriorDetailingHooks
{
	private static readonly string[] InteriorParts = { "details", "benchFront", "bench", "steeringWheel", "seatLeft", "seatRight" };

	[HarmonyPatch(typeof(InteriorDetailingToolkitLogic), nameof(InteriorDetailingToolkitLogic.DoWorkAnim))]
	[HarmonyPrefix]
	private static void BeforeWork(CarLoader carLoader)
	{
		var tool = carLoader != null && carLoader.IsInPlace(CarPlace.CarWash) ? ModToolId.InteriorDetailingStationary : ModToolId.InteriorDetailing;
		CarToolActions.Started(tool, carLoader, ToolActionKind.InteriorDetailing);
	}

	[HarmonyPatch(typeof(InteriorDetailingToolkitLogic._DoWorkAnim_d__1), nameof(InteriorDetailingToolkitLogic._DoWorkAnim_d__1.MoveNext))]
	[HarmonyPostfix]
	private static void AfterWorkStep(InteriorDetailingToolkitLogic._DoWorkAnim_d__1 __instance, bool __result)
	{
		if (__result) return;
		CarToolActions.Finished(ToolActionKind.InteriorDetailing);
		CarToolActions.MarkParts(__instance.carLoader, InteriorParts);
		CarToolActions.MarkDetails(__instance.carLoader, CarDetailSection.BodyCosmetics);
	}
}
