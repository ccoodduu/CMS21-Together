using System.Collections;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Data;
using HarmonyLib;

namespace CMS21Together.Logic.Tools;

public sealed class TireChangerSync : ToolMachine
{
	private static TireChangerLogic Logic => ToolsManager.Get()?.TireChangerLogic;

	public override ModToolId Tool => ModToolId.TireChanger;

	public override bool Present => Logic != null;

	public override ToolSlotState ReadLocal()
	{
		var group = Logic?.GroupOnTireChanger;
		return new ToolSlotState { Tool = Tool, Group = group?.ToModGroupItem(), Mounting = group != null && Logic.GroupOnTireChangerIsMounting };
	}

	public override IEnumerator Put(ToolSlotState state)
	{
		Logic.SetGroupOnTireChanger(state.Group.ToGameGroupItem(), true, state.Mounting);
		yield break;
	}

	public override IEnumerator Clear() => Step(Logic.ClearForTutorial());
}

// docs/spikes/workshop-machines.md: the take pie action builds <Clear>d__23 itself, so the take is seen in its MoveNext.
[HarmonyPatch]
public static class TireChangerHooks
{
	[HarmonyPatch(typeof(TireChangerLogic), nameof(TireChangerLogic.SetGroupOnTireChanger))]
	[HarmonyPostfix]
	private static void AfterPut() => ToolSync.SendLocalOf(ModToolId.TireChanger);

	[HarmonyPatch(typeof(TireChangerLogic._Clear_d__23), nameof(TireChangerLogic._Clear_d__23.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeTakeStep(TireChangerLogic._Clear_d__23 __instance)
	{
		if (__instance.__1__state == 0) ToolSync.MarkTakeStart(ModToolId.TireChanger);
	}

	[HarmonyPatch(typeof(TireChangerLogic._Clear_d__23), nameof(TireChangerLogic._Clear_d__23.MoveNext))]
	[HarmonyPostfix]
	private static void AfterTakeStep(bool __result)
	{
		if (!__result) ToolSync.SendLocalOf(ModToolId.TireChanger);
	}
}
