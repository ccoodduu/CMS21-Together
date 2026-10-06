using System.Collections;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Data;
using HarmonyLib;

namespace CMS21Together.Logic.Tools;

public sealed class SpringClampSync : ToolMachine
{
	private static SpringClampLogic Logic => ToolsManager.Get()?.SpringClampLogic;

	public override ModToolId Tool => ModToolId.SpringClamp;

	public override bool Present => Logic != null;

	public override ToolSlotState ReadLocal()
	{
		var group = Logic?.GroupOnSpringClamp;
		return new ToolSlotState { Tool = Tool, Group = group?.ToModGroupItem(), Mounting = group != null && Logic.GroupOnSpringClampIsMounting };
	}

	public override IEnumerator Put(ToolSlotState state)
	{
		Logic.SetGroupOnSpringClamp(state.Group.ToGameGroupItem(), true, state.Mounting);
		yield break;
	}

	public override IEnumerator Clear()
	{
		Logic.ClearSpringClamp();
		yield break;
	}
}

[HarmonyPatch]
public static class SpringClampHooks
{
	[HarmonyPatch(typeof(SpringClampLogic), nameof(SpringClampLogic.SetGroupOnSpringClamp))]
	[HarmonyPostfix]
	private static void AfterPut() => ToolSync.SendLocalOf(ModToolId.SpringClamp);

	[HarmonyPatch(typeof(SpringClampLogic), nameof(SpringClampLogic.ClearSpringClamp))]
	[HarmonyPrefix]
	private static void BeforeTake() => ToolSync.MarkTakeStart(ModToolId.SpringClamp);

	[HarmonyPatch(typeof(SpringClampLogic), nameof(SpringClampLogic.ClearSpringClamp))]
	[HarmonyPostfix]
	private static void AfterTake() => ToolSync.SendLocalOf(ModToolId.SpringClamp);
}
