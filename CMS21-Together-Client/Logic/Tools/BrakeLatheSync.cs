using System.Collections;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Data;
using HarmonyLib;

namespace CMS21Together.Logic.Tools;

public sealed class BrakeLatheSync : ToolMachine
{
	private static BrakeLatheLogic Logic => ToolsManager.Get()?.BrakeLatheLogic;

	public override ModToolId Tool => ModToolId.BrakeLathe;

	public override bool Present => Logic != null;

	public override ToolSlotState ReadLocal() => new ToolSlotState { Tool = Tool, Item = Logic?.Item?.ToModItem() };

	public override IEnumerator Put(ToolSlotState state)
	{
		Logic.SetItem(state.Item.ToGameItem(), true);
		yield break;
	}

	public override IEnumerator Clear()
	{
		Logic.Clear();
		yield break;
	}
}

[HarmonyPatch]
public static class BrakeLatheHooks
{
	[HarmonyPatch(typeof(BrakeLatheLogic), nameof(BrakeLatheLogic.SetItem))]
	[HarmonyPostfix]
	private static void AfterPut() => ToolSync.SendLocalOf(ModToolId.BrakeLathe);

	[HarmonyPatch(typeof(BrakeLatheLogic), nameof(BrakeLatheLogic.Clear))]
	[HarmonyPrefix]
	private static void BeforeTake() => ToolSync.MarkTakeStart(ModToolId.BrakeLathe);

	[HarmonyPatch(typeof(BrakeLatheLogic), nameof(BrakeLatheLogic.Clear))]
	[HarmonyPostfix]
	private static void AfterTake() => ToolSync.SendLocalOf(ModToolId.BrakeLathe);
}
