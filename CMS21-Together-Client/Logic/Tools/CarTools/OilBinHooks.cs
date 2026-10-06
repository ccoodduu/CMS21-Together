using CMS21_Together_Core.Data.GameType;
using HarmonyLib;

namespace CMS21Together.Logic.Tools.CarTools;

// CarLoader.UseOilbin builds <UseOilDrain>d__40 inline. Its first step ends the coroutine without a change when there is
// no oil or no drain plug, so the action is sent only when that step yields; the oil is written on the last step.
[HarmonyPatch]
public static class OilBinHooks
{
	[HarmonyPatch(typeof(ToolsManager._UseOilDrain_d__40), nameof(ToolsManager._UseOilDrain_d__40.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeDrainStep(ToolsManager._UseOilDrain_d__40 __instance, out int __state) => __state = __instance.__1__state;

	[HarmonyPatch(typeof(ToolsManager._UseOilDrain_d__40), nameof(ToolsManager._UseOilDrain_d__40.MoveNext))]
	[HarmonyPostfix]
	private static void AfterDrainStep(ToolsManager._UseOilDrain_d__40 __instance, bool __result, int __state)
	{
		if (__state == 0 && __result)
		{
			CarToolActions.Started(ModToolId.OilBin, __instance.carLoader, ToolActionKind.DrainOil);
		}
		else if (__state != 0 && !__result)
		{
			CarToolActions.Finished(ToolActionKind.DrainOil);
			CarToolActions.MarkDetails(__instance.carLoader, CarDetailSection.Fluids);
		}
	}
}
