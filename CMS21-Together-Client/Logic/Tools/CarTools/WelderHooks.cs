using CMS21_Together_Core.Data.GameType;
using HarmonyLib;

namespace CMS21Together.Logic.Tools.CarTools;

// docs/spikes/workshop-car-tools.md: DoWorkAnim is only called after the payment; its state machine returns false once
// FinishAnim is done, which is the commit point (FinishAnim itself is built inline and never fires).
[HarmonyPatch]
public static class WelderHooks
{
	[HarmonyPatch(typeof(WelderLogic), nameof(WelderLogic.DoWorkAnim))]
	[HarmonyPrefix]
	private static void BeforeWork(CarLoader carLoader) => CarToolActions.Started(ModToolId.Welder, carLoader, ToolActionKind.Weld);

	[HarmonyPatch(typeof(WelderLogic._DoWorkAnim_d__1), nameof(WelderLogic._DoWorkAnim_d__1.MoveNext))]
	[HarmonyPostfix]
	private static void AfterWorkStep(WelderLogic._DoWorkAnim_d__1 __instance, bool __result)
	{
		if (__result) return;
		CarToolActions.Finished(ToolActionKind.Weld);
		CarToolActions.MarkParts(__instance.carLoader, "body", "details");
	}
}
