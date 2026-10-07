using CMS.Difficulty;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;

namespace CMS21Together.Logic.Economy;

// economy-audit D1: one prefix per mutator. Money and scrap need a scope (else they are dropped and counted);
// experience without a scope is sent as Work.
[HarmonyPatch]
public static class EconomyHooks
{
	public static bool Connected => Client.Instance != null && Client.Instance.IsConnected;

	private static bool Active => Connected && !ClientData.IsServerUpdating && ClientData.IsInitialSyncFinished;

	[HarmonyPatch(typeof(GlobalData), nameof(GlobalData.AddPlayerMoney))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeAddMoney(int money)
	{
		if (!Active || money == 0 || GameSettings.UnlimitedMoney) return true;
		var scope = EconomyScope.Find(EconomyKind.Money);
		if (scope == null)
		{
			EconomyAudit.Drop("money", money);
			return false;
		}
		scope.Money += money;
		return Apply(scope, () => EconomyRequests.SendFee(scope, money, 0, 0), scope.CaptureMoney, money);
	}

	[HarmonyPatch(typeof(GlobalData), nameof(GlobalData.AddPlayerScraps))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeAddScraps(int amount) => OnScraps(amount);

	[HarmonyPatch(typeof(GlobalData), nameof(GlobalData.SetPlayerScraps))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeSetScraps(int scraps) => OnScraps(scraps - GlobalData.PlayerScraps);

	private static bool OnScraps(int amount)
	{
		if (!Active || amount == 0 || GameSettings.UnlimitedScraps) return true;
		var scope = EconomyScope.Find(EconomyKind.Scraps);
		if (scope == null)
		{
			EconomyAudit.Drop("scrap", amount);
			return false;
		}
		scope.Scraps += amount;
		return Apply(scope, () => EconomyRequests.SendFee(scope, 0, amount, 0), null, amount);
	}

	[HarmonyPatch(typeof(GlobalData), nameof(GlobalData.AddPlayerExp))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeAddExp(int exp)
	{
		if (!Active || exp <= 0) return true;
		var scope = EconomyScope.Find(EconomyKind.Exp);
		if (scope == null)
		{
			EconomyRequests.SendWork(exp);
			return true;
		}
		scope.Exp += exp;
		return Apply(scope, () => EconomyRequests.SendFee(scope, 0, 0, exp), scope.CaptureExp, exp);
	}

	private static bool Apply(EconomyScopeEntry scope, System.Action send, System.Action<int> capture, int amount)
	{
		switch (scope.Mode)
		{
			case EconomyMode.Fee:
				send();
				return true;
			case EconomyMode.Covered:
				capture?.Invoke(amount);
				EconomyAudit.Count(EconomyAudit.CoveredCalls, scope.Name);
				return true;
			default:
				EconomyAudit.Count(EconomyAudit.SuppressedCalls, scope.Name);
				return false;
		}
	}

	[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.ActivateDifficultyLevel))]
	[HarmonyPrefix]
	private static void BeforeActivateDifficulty(out bool __state)
	{
		__state = ClientData.IsServerUpdating;
		ClientData.IsServerUpdating = true;
	}

	[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.ActivateDifficultyLevel))]
	[HarmonyPostfix]
	private static void AfterActivateDifficulty(bool __state) => ClientData.IsServerUpdating = __state;
}
