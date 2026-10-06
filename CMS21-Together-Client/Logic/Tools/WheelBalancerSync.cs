using System.Collections;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Data;
using HarmonyLib;

namespace CMS21Together.Logic.Tools;

// "Balanced" is the machine flag !balanceCanceled until the take-off writes it into the wheel (spike), so the slot
// carries it instead of the items' WheelData. SetGroupOnWheelBalancer(g, false) opens the minigame: remote puts pass true.
public sealed class WheelBalancerSync : ToolMachine
{
	private static WheelBalancerLogic Logic => ToolsManager.Get()?.WheelBalancerLogic;

	public override ModToolId Tool => ModToolId.WheelBalancer;

	public override bool Present => Logic != null;

	public override ToolSlotState ReadLocal()
	{
		var group = Logic?.GetGroupOnWheelBalancer();
		return new ToolSlotState { Tool = Tool, Group = group?.ToModGroupItem(), Balanced = group != null && !Logic.IsCanceled() };
	}

	public override IEnumerator Put(ToolSlotState state)
	{
		Logic.SetGroupOnWheelBalancer(state.Group.ToGameGroupItem(), true);
		yield break;
	}

	public override void ApplyFlags(ToolSlotState state)
	{
		if (!state.IsEmpty) Logic.SetCanceled(!state.Balanced);
	}

	public override IEnumerator Clear() => Step(Logic.ClearForTutorial());

	public override void OnClaimLost() => CloseWindow();

	public static bool WindowOpen
	{
		get
		{
			var window = WindowManager.Instance?.GetWindowByID<WheelBalanceWindow>(WindowID.WheelBalance);
			return window != null && window.isActive;
		}
	}

	public static void CloseWindow()
	{
		if (WindowOpen) WindowManager.Instance.GetWindowByID<WheelBalanceWindow>(WindowID.WheelBalance).CancelAction();
	}
}

[HarmonyPatch]
public static class WheelBalancerHooks
{
	private const ModToolId Tool = ModToolId.WheelBalancer;

	[HarmonyPatch(typeof(WheelBalancerLogic), nameof(WheelBalancerLogic.SetGroupOnWheelBalancer))]
	[HarmonyPostfix]
	private static void AfterPut(WheelBalancerLogic __instance, bool instant)
	{
		if (ToolSync.IsApplyingRemote(Tool) || !ToolSync.CanSend) return;
		if (instant) __instance.SetCanceled(true);
		var state = ToolSync.Machine(Tool).ReadLocal();
		state.Balanced = false;
		ToolSync.SendLocal(state);
	}

	[HarmonyPatch(typeof(WheelBalancerLogic), nameof(WheelBalancerLogic.Balance))]
	[HarmonyPrefix]
	private static bool BeforeBalance()
	{
		ToolSync.TraceEvent("WheelBalancer minigame opens");
		if (ToolSync.IsApplyingRemote(Tool) || !ToolSync.CanSend) return true;
		if (ToolSync.RefuseIfHeld(Tool)) return false;
		ToolSync.Claim(Tool);
		return true;
	}

	[HarmonyPatch(typeof(WheelBalancerLogic), "FinishBalanceInternal")]
	[HarmonyPostfix]
	private static void AfterBalanceFinished()
	{
		ToolSync.TraceEvent("WheelBalancer balance finished");
		if (ToolSync.IsApplyingRemote(Tool)) return;
		ToolSync.SendLocalOf(Tool);
		ToolSync.ReleaseClaim(Tool);
	}

	[HarmonyPatch(typeof(WheelBalanceWindow), "CancelAction")]
	[HarmonyPostfix]
	private static void AfterCancel()
	{
		ToolSync.TraceEvent("WheelBalanceWindow cancel");
		ToolSync.ReleaseClaim(Tool);
	}

	[HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController._GetOnClick_b__72_64))]
	[HarmonyPrefix]
	private static bool BeforeTakeAction(ref bool __result) => AllowUnlessHeld(ref __result);

	[HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController._GetOnClick_b__72_65))]
	[HarmonyPrefix]
	private static bool BeforeBalanceAction(ref bool __result) => AllowUnlessHeld(ref __result);

	private static bool AllowUnlessHeld(ref bool __result)
	{
		if (!ToolSync.RefuseIfHeld(Tool)) return true;
		__result = true;
		return false;
	}

	[HarmonyPatch(typeof(WheelBalancerLogic._Clear_d__26), nameof(WheelBalancerLogic._Clear_d__26.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeTakeStep(WheelBalancerLogic._Clear_d__26 __instance)
	{
		if (__instance.__1__state == 0) ToolSync.MarkTakeStart(Tool);
	}

	[HarmonyPatch(typeof(WheelBalancerLogic._Clear_d__26), nameof(WheelBalancerLogic._Clear_d__26.MoveNext))]
	[HarmonyPostfix]
	private static void AfterTakeStep(bool __result)
	{
		if (!__result) ToolSync.SendLocalOf(Tool);
	}
}
