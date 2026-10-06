using System;
using System.Collections.Generic;
using CMS.UI;
using CMS.UI.Logic;
using CMS.UI.Logic.Scrap;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network.Handlers;
using HarmonyLib;

namespace CMS21Together.Logic.Economy;

// economy-audit D5/D8/D10: Trades ask the server first and skip the game method; the server's inventory, stats and
// EconomyResult bring the effect back.
[HarmonyPatch]
public static class TradeHooks
{
	private static int lastScrapGrade = -1;

	[HarmonyPatch(typeof(ScrapProduction), nameof(ScrapProduction.ProcessGameResult))]
	[HarmonyPrefix]
	private static void BeforeScrapResult(BarType result) =>
		lastScrapGrade = result switch { BarType.Success => 0, BarType.Bonus => 1, BarType.BigBonus => 2, _ => -1 };

	[HarmonyPatch(typeof(ScrapProduction), nameof(ScrapProduction.MakeScrap))]
	[HarmonyPrefix]
	private static bool BeforeMakeScrap(ScrapProduction __instance, int amount)
	{
		if (!EconomyHooks.Connected) return true;
		var item = __instance.currentItem;
		if (item == null) return false;
		PlaySound("ScrapMaking");
		EconomyRequests.Send(new EconomyRequestPacket
		{
			Reason = EconomyReason.ScrapItem, ItemUid = item.UID, Arg = Math.Max(0, lastScrapGrade), Scraps = amount,
		}, result => AfterScrapTrade(result));
		return false;
	}

	[HarmonyPatch(typeof(ScrapPerConditionWindow), nameof(ScrapPerConditionWindow.AcceptAction))]
	[HarmonyPrefix]
	private static bool BeforeScrapPerCondition(ScrapPerConditionWindow __instance)
	{
		if (!EconomyHooks.Connected) return true;
		EconomyRequests.Send(new EconomyRequestPacket
		{
			Reason = EconomyReason.ScrapPerCondition, Arg = (int)Math.Round(__instance.currentSliderValue),
		}, result => AfterScrapTrade(result));
		__instance.Hide(false);
		return false;
	}

	[HarmonyPatch(typeof(ScrapUpgrade), nameof(ScrapUpgrade.UpgradeItem))]
	[HarmonyPrefix]
	private static bool BeforeUpgradeItem(ScrapUpgrade __instance) => Upgrade(__instance, false);

	[HarmonyPatch(typeof(ScrapUpgrade), nameof(ScrapUpgrade.UpgradeAction))]
	[HarmonyPrefix]
	private static bool BeforeUpgradeAction(ScrapUpgrade __instance) => Upgrade(__instance, false);

	[HarmonyPatch(typeof(ScrapUpgrade), nameof(ScrapUpgrade.UpgradeFromOutside))]
	[HarmonyPrefix]
	private static bool BeforeUpgradeFromOutside(ScrapUpgrade __instance) => Upgrade(__instance, true);

	private static bool Upgrade(ScrapUpgrade upgrade, bool checkScraps)
	{
		if (!EconomyHooks.Connected) return true;
		if (checkScraps && !upgrade.HaveScraps()) return true;
		var item = upgrade.currentItem;
		if (item == null) return false;
		EconomyRequests.Send(new EconomyRequestPacket
		{
			Reason = EconomyReason.ScrapUpgrade, ItemUid = item.UID, Arg = upgrade.newQuality, Scraps = -upgrade.upgradeCost,
		}, result =>
		{
			if (result.Accepted) PlaySound("RepairSuccess");
			else ShowRefusal(result);
			try { upgrade.UpdateItems(); }
			catch (Exception ex) { Log.Warn($"[Economy] Refreshing the upgrade page failed: {ex.Message}"); }
		});
		return false;
	}

	private static void AfterScrapTrade(EconomyResultPacket result)
	{
		if (!result.Accepted) ShowRefusal(result);
		try
		{
			var window = WindowManager.Instance?.GetWindowByID<ScrapWindow>(WindowID.Scrap);
			if (window != null && window.isActive) window.scrapProduction?.UpdateItems(false);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Economy] Refreshing the scrap page failed: {ex.Message}");
		}
		InventoryHandlers.RefreshInventoryWindow();
	}

	[HarmonyPatch(typeof(ShopLicenseBuyWindow), nameof(ShopLicenseBuyWindow.BuyItem))]
	[HarmonyPrefix]
	private static bool BeforeBuyPlates(ShopLicenseBuyWindow __instance, bool custom, bool blank, string text)
	{
		if (!EconomyHooks.Connected) return true;
		if (GlobalData.PlayerMoney < __instance.currentPrice) return true;
		int amount = __instance.currentAmount;
		string plateId = __instance.itemID;
		string plateText = blank || !custom ? "" : text ?? "";
		var plates = new List<ModItem>();
		for (int i = 0; i < amount; i++)
		{
			var plate = new Item("LicensePlate") { Condition = 1f, IsExamined = true, PaintType = (PaintType)1 }.ToModItem();
			plate.Color = new ModColor { r = 1f, g = 1f, b = 1f, a = 1f };
			plate.LPData = new ModLPData { Name = plateId, Custom = plateText };
			plates.Add(plate);
		}
		EconomyRequests.Send(new EconomyRequestPacket
		{
			Reason = EconomyReason.LicensePlates, Money = -(int)__instance.currentPrice, Arg = custom ? 1 : 0, Items = plates,
		}, result =>
		{
			if (!result.Accepted)
			{
				if (result.Refusal == EconomyRefusal.NoMoney) UIManager.Get()?.ShowInfoWindow("GUI_BrakKasy");
				else ShowRefusal(result);
				return;
			}
			string name = GameInventory.Instance?.GetItemLocalizeName(plateId) ?? plateId;
			UIManager.Get()?.ShowPopup("PopUp_NewItem", amount > 1 ? $"{name} x{amount}" : name, PopupType.Buy);
			try { SoundManager.Get()?.PlaySFXOneShot("Popup"); }
			catch (Exception) { }
			if (__instance != null && __instance.isActive) __instance.Hide(false);
		});
		return false;
	}

	public static bool BeforeAskWindowAnswer(NotificationCenter.__c__DisplayClass17_0 __instance, bool wasAccepted)
	{
		if (!wasAccepted || !EconomyHooks.Connected) return true;
		var item = __instance.item;
		if (item == null || !IsBarnMap(item.ID)) return true;
		EconomyRequests.Send(new EconomyRequestPacket { Reason = EconomyReason.BarnMap, ItemUid = item.UID }, result =>
		{
			if (result.Accepted) PlaySound("AddMap");
			else ShowRefusal(result);
			InventoryHandlers.RefreshInventoryWindow();
		});
		return false;
	}

	private static bool IsBarnMap(string id)
	{
		if (id == "specialMap") return true;
		try { return GameInventory.Instance?.GetItemProperty(id)?.SpecialGroup == SpecialGroup.SpecialMap; }
		catch (Exception) { return false; }
	}

	public static void PlaySound(string name)
	{
		try { SoundManager.Get()?.PlaySFX(name); }
		catch (Exception ex) { Log.Warn($"[Economy] Sound {name} failed: {ex.Message}"); }
	}

	public static void ShowRefusal(EconomyResultPacket result) =>
		UIManager.Get()?.ShowInfoWindow(EconomyRequests.RefusalText(result.Refusal));
}
