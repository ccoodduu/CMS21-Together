using System.Collections;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Data;
using HarmonyLib;

namespace CMS21Together.Logic.Tools;

// BatteryChargerActivate is only called by the setter and the clear (spike), so "active" is "a battery is on it".
public sealed class BatteryChargerSync : ToolMachine
{
	private static BatteryChargerLogic Logic => ToolsManager.Get()?.BatteryChargerLogic;

	public override ModToolId Tool => ModToolId.BatteryCharger;

	public override bool Present => Logic != null;

	public override ToolSlotState ReadLocal()
	{
		var item = Logic?.ItemOnBatteryCharger;
		return new ToolSlotState { Tool = Tool, Item = item?.ToModItem(), Active = item != null };
	}

	public override IEnumerator Put(ToolSlotState state)
	{
		Logic.SetItemOnBatteryCharger(state.Item.ToGameItem(), true);
		yield break;
	}

	public override IEnumerator Clear()
	{
		Logic.ClearBatteryCharger();
		yield break;
	}
}

[HarmonyPatch]
public static class BatteryChargerHooks
{
	[HarmonyPatch(typeof(BatteryChargerLogic), nameof(BatteryChargerLogic.SetItemOnBatteryCharger))]
	[HarmonyPostfix]
	private static void AfterPut() => ToolSync.SendLocalOf(ModToolId.BatteryCharger);

	[HarmonyPatch(typeof(BatteryChargerLogic), nameof(BatteryChargerLogic.ClearBatteryCharger))]
	[HarmonyPrefix]
	private static void BeforeTake() => ToolSync.MarkTakeStart(ModToolId.BatteryCharger);

	[HarmonyPatch(typeof(BatteryChargerLogic), nameof(BatteryChargerLogic.ClearBatteryCharger))]
	[HarmonyPostfix]
	private static void AfterTake() => ToolSync.SendLocalOf(ModToolId.BatteryCharger);
}
