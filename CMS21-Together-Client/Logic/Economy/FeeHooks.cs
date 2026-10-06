using System;
using System.Reflection;
using CMS.Managers;
using CMS.UI.Logic;
using CMS.UI.Logic.CaseOpening;
using CMS.UI.Logic.Map;
using CMS.UI.Windows;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Guard;
using HarmonyLib;

namespace CMS21Together.Logic.Economy;

// economy-audit D4: the game methods that call a mutator after the fact. Each opens a Fee scope for its call.
[HarmonyPatch]
public static class FeeHooks
{
	private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("CMS21Together.Economy");

	public static readonly System.Collections.Generic.Dictionary<string, string> LambdaPatches = new System.Collections.Generic.Dictionary<string, string>();

	private static EconomyScopeEntry Open(EconomyReason reason, EconomyKind claims = EconomyKind.Money, int arg = 0, long itemUid = 0, int loader = -1) =>
		EconomyHooks.Connected ? EconomyScope.Push(reason, EconomyMode.Fee, claims, arg, itemUid, loader) : null;

	private static void Close(EconomyScopeEntry __state) => EconomyScope.Pop(__state);

	[HarmonyPatch(typeof(MapWindow), nameof(MapWindow.SubmitPanelAction))]
	[HarmonyPrefix]
	private static bool BeforeSubmitPanel(MapWindow __instance, out EconomyScopeEntry __state)
	{
		__state = null;
		if (!EconomyHooks.Connected) return true;
		var destination = __instance.currentSelectedDestination;
		string scene = destination switch
		{
			MapDestinationID.Auction => "Auction",
			MapDestinationID.Junkyard => "Junkyard",
			MapDestinationID.Barn => "Barn",
			_ => null,
		};
		if (scene == null) return true;
		if (FeatureGuard.Decide(GuardKind.Scene, scene) != GuardDecision.Allow) return false;
		__state = EconomyScope.Push(EconomyReason.TravelFee, EconomyMode.Suppressed, EconomyKind.Money, (int)destination);
		__state.Before = GlobalData.BarnsAmount;
		return true;
	}

	[HarmonyPatch(typeof(MapWindow), nameof(MapWindow.SubmitPanelAction))]
	[HarmonyPostfix]
	private static void AfterSubmitPanel(EconomyScopeEntry __state)
	{
		if (__state == null) return;
		EconomyScope.Pop(__state);
		bool barnUsed = __state.Arg == (int)MapDestinationID.Barn && GlobalData.BarnsAmount < __state.Before;
		if (__state.Arg == (int)MapDestinationID.Barn && !barnUsed) return;
		EconomyRequests.Send(new EconomyRequestPacket
		{
			Reason = EconomyReason.TravelFee, Arg = __state.Arg, Arg2 = barnUsed ? 1 : 0, Money = __state.Money,
		});
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ButtonAccept))]
	[HarmonyPrefix]
	private static void BeforeButtonAccept(out EconomyScopeEntry __state) => __state = Open(EconomyReason.TravelFeeLegacy);

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ButtonAccept))]
	[HarmonyPostfix]
	private static void AfterButtonAccept(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.NewButtonAccept))]
	[HarmonyPrefix]
	private static void BeforeNewButtonAccept(out EconomyScopeEntry __state) =>
		__state = EconomyHooks.Connected ? EconomyScope.Covered("SellItem", EconomyKind.Money) : null;

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.NewButtonAccept))]
	[HarmonyPostfix]
	private static void AfterNewButtonAccept(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(PartScript._Hide_d__159), nameof(PartScript._Hide_d__159.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeUnmountStep(out EconomyScopeEntry __state) => __state = Open(EconomyReason.FluidSpill);

	[HarmonyPatch(typeof(PartScript._Hide_d__159), nameof(PartScript._Hide_d__159.MoveNext))]
	[HarmonyPostfix]
	private static void AfterUnmountStep(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(FluidRefill), nameof(FluidRefill.Hide))]
	[HarmonyPrefix]
	private static void BeforeRefillHide(out EconomyScopeEntry __state) => __state = Open(EconomyReason.FluidRefill);

	[HarmonyPatch(typeof(FluidRefill), nameof(FluidRefill.Hide))]
	[HarmonyPostfix]
	private static void AfterRefillHide(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(PaintshopManager), nameof(PaintshopManager.TryGetMoneyForPaint))]
	[HarmonyPrefix]
	private static void BeforePaintPayment(PaintshopManager __instance, out EconomyScopeEntry __state) =>
		__state = Open(EconomyReason.PaintCar, arg: (int)__instance.paintshopType);

	[HarmonyPatch(typeof(PaintshopManager), nameof(PaintshopManager.TryGetMoneyForPaint))]
	[HarmonyPostfix]
	private static void AfterPaintPayment(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(PaintshopWindow._ShowCoroutine_d__10), nameof(PaintshopWindow._ShowCoroutine_d__10.MoveNext))]
	[HarmonyPrefix]
	private static void BeforePaintshopShowStep(out EconomyScopeEntry __state) => __state = Open(EconomyReason.WashBeforePaint);

	[HarmonyPatch(typeof(PaintshopWindow._ShowCoroutine_d__10), nameof(PaintshopWindow._ShowCoroutine_d__10.MoveNext))]
	[HarmonyPostfix]
	private static void AfterPaintshopShowStep(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(TintingWindow._ShowCoroutine_d__18), nameof(TintingWindow._ShowCoroutine_d__18.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeTintingShowStep(out EconomyScopeEntry __state) => __state = Open(EconomyReason.WashBeforeTint);

	[HarmonyPatch(typeof(TintingWindow._ShowCoroutine_d__18), nameof(TintingWindow._ShowCoroutine_d__18.MoveNext))]
	[HarmonyPostfix]
	private static void AfterTintingShowStep(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(TintingWindow), nameof(TintingWindow.TintAction))]
	[HarmonyPrefix]
	private static void BeforeTint(out EconomyScopeEntry __state) => __state = Open(EconomyReason.Tint);

	[HarmonyPatch(typeof(TintingWindow), nameof(TintingWindow.TintAction))]
	[HarmonyPostfix]
	private static void AfterTint(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(RepairPartWindow), nameof(RepairPartWindow.ProcessGameResult))]
	[HarmonyPrefix]
	private static void BeforeRepairResult(RepairPartWindow __instance, out EconomyScopeEntry __state)
	{
		long uid = 0;
		try { uid = __instance.currentItemInfo?.Item?.UID ?? 0; }
		catch (Exception) { }
		__state = Open(EconomyReason.PartRepair, itemUid: uid);
	}

	[HarmonyPatch(typeof(RepairPartWindow), nameof(RepairPartWindow.ProcessGameResult))]
	[HarmonyPostfix]
	private static void AfterRepairResult(EconomyScopeEntry __state) => Close(__state);

	[HarmonyPatch(typeof(CaseOpeningWindow), nameof(CaseOpeningWindow.TakeLoot))]
	[HarmonyPrefix]
	private static void BeforeTakeLoot(CaseOpeningWindow __instance, CaseOpeningItem card, out EconomyScopeEntry __state)
	{
		__state = null;
		if (card == null) return;
		var reason = card.TypeOfCard switch
		{
			CaseCardType.CR => EconomyReason.CrateMoney,
			CaseCardType.XP => EconomyReason.CrateExp,
			CaseCardType.Scrap => EconomyReason.CrateScrap,
			_ => (EconomyReason?)null,
		};
		if (reason == null) return;
		__state = Open(reason.Value, EconomyKind.All, itemUid: __instance.originalItem?.UID ?? 0);
	}

	[HarmonyPatch(typeof(CaseOpeningWindow), nameof(CaseOpeningWindow.TakeLoot))]
	[HarmonyPostfix]
	private static void AfterTakeLoot(EconomyScopeEntry __state) => Close(__state);

	// The welder and interior-detailing accept lambdas have generated interop names, so they are patched by hand and a
	// missing one is logged instead of stopping the mod's other patches.
	public static void InstallLambdaHooks()
	{
		PatchLambda(typeof(WelderLogic.__c__DisplayClass5_0), "Method_Internal_Void_Boolean_PDM_0", nameof(BeforeWelderAccept));
		PatchLambda(typeof(InteriorDetailingToolkitLogic.__c__DisplayClass6_0), "Method_Internal_Void_Boolean_PDM_0", nameof(BeforeDetailingAccept));
		PatchLambda(typeof(NotificationCenter.__c__DisplayClass17_0), "Method_Internal_Void_Boolean_PDM_0", nameof(TradeHooks.BeforeAskWindowAnswer), typeof(TradeHooks));
		PatchLambda(typeof(NotificationCenter.__c__DisplayClass17_0), "Method_Internal_Void_Boolean_PDM_1", nameof(TradeHooks.BeforeAskWindowAnswer), typeof(TradeHooks));
	}

	private static void PatchLambda(Type type, string method, string prefix, Type owner = null)
	{
		string name = $"{type.Name}.{method}";
		try
		{
			var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(name);
			var prefixMethod = new HarmonyMethod((owner ?? typeof(FeeHooks)).GetMethod(prefix, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
			var postfixMethod = owner == null ? new HarmonyMethod(typeof(FeeHooks).GetMethod(nameof(Close), BindingFlags.NonPublic | BindingFlags.Static)) : null;
			harmony.Patch(target, prefix: prefixMethod, postfix: postfixMethod);
			LambdaPatches[name] = "patched";
		}
		catch (Exception ex)
		{
			LambdaPatches[name] = ex.Message.Split('\n')[0];
			Log.Warn($"[Economy] Cannot patch {name}: {LambdaPatches[name]}");
		}
	}

	private static void BeforeWelderAccept(WelderLogic.__c__DisplayClass5_0 __instance, out EconomyScopeEntry __state) =>
		__state = Open(EconomyReason.Welder, loader: LoaderOf(__instance.carLoader));

	private static void BeforeDetailingAccept(InteriorDetailingToolkitLogic.__c__DisplayClass6_0 __instance, out EconomyScopeEntry __state) =>
		__state = Open(EconomyReason.InteriorDetailing, loader: LoaderOf(__instance.carLoader));

	private static int LoaderOf(CarLoader carLoader)
	{
		if (carLoader == null) return -1;
		try { return CarLoaderPlaces.Get()?.GetCarLoaderId(carLoader) ?? -1; }
		catch (Exception) { return -1; }
	}
}
