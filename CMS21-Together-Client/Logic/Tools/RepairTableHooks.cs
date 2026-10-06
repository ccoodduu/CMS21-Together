using CMS.UI.Windows;
using HarmonyLib;

namespace CMS21Together.Logic.Tools;

// UpdateItemCondition, RepairItem and BreakItem are inlined into ProcessGameResult (spike), which edits the item in place.
[HarmonyPatch]
public static class RepairTableHooks
{
	[HarmonyPatch(typeof(RepairPartWindow), "ProcessGameResult")]
	[HarmonyPostfix]
	private static void AfterRepair(RepairPartWindow __instance) => ToolSync.SendItemUpdate(__instance.currentItemInfo.Item);
}
