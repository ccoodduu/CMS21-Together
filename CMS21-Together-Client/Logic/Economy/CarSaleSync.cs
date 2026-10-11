using System;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;

namespace CMS21Together.Logic.Economy;

// economy-audit D6: the server deletes the sold car for everyone (row 1's delete) and pays the shared money.
[HarmonyPatch]
public static class CarSaleSync
{
	private const int BigMoneySound = 10000;

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.SellCar))]
	[HarmonyPrefix]
	private static bool BeforeSellCar(CarLoader carLoader, int sellPrice, bool __runOriginal)
	{
		if (!__runOriginal) return false;
		if (!EconomyHooks.Connected || carLoader == null) return true;
		int loader = CarLoaderPlaces.Get()?.GetCarLoaderId(carLoader) ?? -1;
		if (loader < 0)
		{
			UIManager.Get()?.ShowInfoWindow("This car cannot be sold while playing together.");
			return false;
		}
		if (CarAwaySync.BlockIfLocked(loader, "sale")) return false;
		Log.Info($"[Economy] Asking to sell loader {loader} for {sellPrice}.");
		var stats = CountedStats(carLoader);
		EconomyRequests.Send(new EconomyRequestPacket
		{
			Reason = EconomyReason.CarSale, Money = sellPrice, CarLoaderId = loader, SpawnSeq = CarPartsSync.SpawnSeq(loader),
			Arg = stats.Profit, Arg2 = stats.FixedCar ? 1 : 0,
		}, result => OnResult(result, sellPrice));
		return false;
	}

	// What the game's SellCarAction counted from this tab before calling SellCar; a sale not made through the tab counted nothing.
	private static (int Profit, bool FixedCar) CountedStats(CarLoader carLoader)
	{
		try
		{
			var tab = WindowManager.Instance?.GetWindowByID<CarInfoWindow>(WindowID.CarInfo)?.carSummaryTab;
			if (tab == null || tab.currentCarLoader == null || tab.currentCarLoader.Pointer != carLoader.Pointer) return (0, false);
			var info = carLoader.CarInfoData;
			return (Math.Max(0, tab.sellPrice - info.BuyPrice), info.CarFrom != CarFrom.Salon && tab.globalCondition == 1f);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Economy] Reading the sale stats failed: {ex.Message}");
			return (0, false);
		}
	}

	private static void OnResult(EconomyResultPacket result, int sellPrice)
	{
		if (!result.Accepted)
		{
			UIManager.Get()?.ShowInfoWindow($"The car cannot be sold. {EconomyRequests.RefusalText(result.Refusal)}");
			return;
		}
		try
		{
			WindowManager.Instance?.HideIfActive(WindowID.CarInfo, true);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Economy] Closing the car info failed: {ex.Message}");
		}
		TradeHooks.PlaySound(sellPrice > BigMoneySound ? "AddMoneyBig" : "AddMoney");
	}
}
