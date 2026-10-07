using System;
using System.Collections;
using System.Collections.Generic;
using CMS.Managers;
using CMS.UI.Logic.Auction;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Guard;
using CMS21Together.Logic.Car;
using CMS21Together.Logic.Car.Placement;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Economy;

// sync-players-and-scenes D8: a car bought outside the garage (junkyard, barn, salon, auction) always goes to the
// shared parking through row 2's CarParkRequest with CarLoaderID -1; the server checks the money and takes it once.
// The vanilla debit is suppressed, and the location window can only pick the parking.
[HarmonyPatch]
public static class CarPurchaseSync
{
	public const string ScopeName = "CarPurchase";
	private const float SaveTimeoutSeconds = 5f;
	private const float ResultTimeoutSeconds = 15f;
	private const int FirstRequestId = 100000;
	private const string ParkingTarget = "Parking";

	private sealed class Capture
	{
		public int RequestId;
		public int Price;
		public string Source;
		public string Car;
		public bool Chosen;
	}

	private static readonly Dictionary<int, Capture> sent = new Dictionary<int, Capture>();
	private static Capture open;
	private static IDisposable locationBypass;
	private static int nextRequestId = FirstRequestId;

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid;

	public static bool IsOpen => open != null;

	public static int Pending => sent.Count;

	public static void Initialize()
	{
		ParkingSync.ParkResultReceived += OnParkResult;
		ClientScene.LeavingScene += (from, to) => Drop($"left {from}");
	}

	public static void Reset()
	{
		open = null;
		sent.Clear();
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.BuyCar))]
	[HarmonyPrefix]
	private static bool BeforeBuyCar(CarLoader carLoader, int buyPrice, out EconomyScopeEntry __state)
	{
		bool run = Begin("BuyCar", carLoader, buyPrice, out __state);
		if (run && __state != null) locationBypass = FeatureGuard.Bypass();
		return run;
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.BuyCar))]
	[HarmonyFinalizer]
	private static Exception AfterBuyCar(Exception __exception, EconomyScopeEntry __state)
	{
		EconomyScope.Pop(__state);
		locationBypass?.Dispose();
		locationBypass = null;
		if (__exception == null || __state == null) return __exception;
		var windows = CMS.UI.WindowManager.Instance;
		if (windows == null || !windows.IsWindowActive(CMS.UI.WindowID.CarLocationWindow))
		{
			Drop($"BuyCar threw before the location window opened: {__exception.Message.Split('\n')[0]}");
			return __exception;
		}
		// BuyCar ends with play-time bookkeeping on the selected profile, which has no profile data in a session slot.
		Log.Debug($"[Purchase] BuyCar threw after showing the location window: {__exception.Message.Split('\n')[0]}");
		return null;
	}

	[HarmonyPatch(typeof(AuctionBidding), nameof(AuctionBidding.ReceiveCarAction))]
	[HarmonyPrefix]
	private static bool BeforeReceiveCar(AuctionBidding __instance, out EconomyScopeEntry __state) =>
		Begin("Auction", __instance.auctionManager?.playerCarLoader, __instance.currentBid, out __state);

	[HarmonyPatch(typeof(AuctionBidding), nameof(AuctionBidding.ReceiveCarAction))]
	[HarmonyPostfix]
	private static void AfterReceiveCar(EconomyScopeEntry __state) => EconomyScope.Pop(__state);

	private static bool Begin(string source, CarLoader carLoader, int price, out EconomyScopeEntry scope)
	{
		scope = null;
		if (!Connected || ClientScene.LocalScene == GameScene.Garage) return true;
		if (ParkingCarPlaceManager.ParkingIsFull())
		{
			Log.Info($"[Purchase] {source}: {carLoader?.carToLoad} not bought, the shared parking is full.");
			UIManager.Get()?.ShowInfoWindow("The shared parking is full. While playing together, bought cars go to the parking.");
			return false;
		}
		if (open != null) Log.Warn($"[Purchase] Capture {open.RequestId} ({open.Car}) replaced before the car was saved.");
		open = new Capture { RequestId = nextRequestId++, Price = price, Source = source, Car = carLoader?.carToLoad };
		Log.Info($"[Purchase] {source}: capture {open.RequestId} opened for {open.Car} at {price} in {ClientScene.LocalScene}.");
		scope = EconomyScope.Push(CMS21_Together_Core.Network.Packets.EconomyReason.Work, EconomyMode.Suppressed, EconomyKind.Money, name: ScopeName);
		return true;
	}

	[HarmonyPatch(typeof(CarLocationWindow), nameof(CarLocationWindow.Prepare))]
	[HarmonyPostfix]
	private static void AfterPrepareLocationWindow(CarLocationWindow __instance)
	{
		if (open == null) return;
		try
		{
			__instance.freePlacesInGarage = false;
			var buttons = __instance.buttons;
			if (buttons != null && buttons.Length > 0 && buttons[0] != null) buttons[0].SetDisabled(true, true);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Purchase] Disabling the garage button failed: {ex.Message}");
		}
	}

	[HarmonyPatch(typeof(NotificationCenter._BuyCar_d__21), nameof(NotificationCenter._BuyCar_d__21.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeLocationChosen(NotificationCenter._BuyCar_d__21 __instance)
	{
		var capture = open;
		if (capture == null || capture.Chosen || __instance.__1__state != 0) return;
		var hash = __instance.hash;
		string target = hash?.GetFromKey("PositionTo")?.ToString();
		if (hash != null && target != ParkingTarget)
		{
			Log.Info($"[Purchase] Capture {capture.RequestId}: location {target ?? "none"} changed to {ParkingTarget}.");
			hash.ChangeKeyValue("PositionTo", (Il2CppSystem.String)ParkingTarget);
		}
		capture.Chosen = true;
		MelonCoroutines.Start(ExpireIfNotSaved(capture));
	}

	private static IEnumerator ExpireIfNotSaved(Capture capture)
	{
		float deadline = Time.realtimeSinceStartup + SaveTimeoutSeconds;
		while (open == capture && Time.realtimeSinceStartup < deadline) yield return null;
		if (open != capture) yield break;
		open = null;
		Log.Error($"[Purchase] Capture {capture.RequestId} ({capture.Car}): the car was not saved within {SaveTimeoutSeconds} s of choosing the parking; nothing was sent and no money moved.");
	}

	// Not GameDataManager.SaveCar: patching a method with the NewCarData struct by value crashes the game.
	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SaveCarToFile), typeof(int), typeof(bool))]
	[HarmonyPostfix]
	private static void AfterSaveCarToFile(int index, bool toParking)
	{
		var capture = open;
		if (capture == null || !capture.Chosen) return;
		open = null;
		if (!Connected) return;
		ParkedCar car = null;
		try
		{
			var data = Singleton<GameManager>.Instance.GameDataManager;
			var carData = toParking ? data.LoadCarInParking(index) : data.LoadCarInGarage(index);
			if (carData != null && !carData.IsDefault()) car = NewCarDataCodec.ToParkedCar(carData);
		}
		catch (Exception ex)
		{
			Log.Error($"[Purchase] Capture {capture.RequestId}: encoding {capture.Car} failed: {ex.Message}");
		}
		ClearLocalSlot(index, toParking);
		if (car == null)
		{
			Log.Error($"[Purchase] Capture {capture.RequestId}: no car data to send for {capture.Car}; nothing was sent and no money moved.");
			ModNotify.ShowMessage("Car not bought", "The car could not be sent to the server. Nothing was paid.");
			return;
		}

		sent[capture.RequestId] = capture;
		Log.Info($"[Purchase] Capture {capture.RequestId}: sending {car.CarToLoad} ({car.Data.Length} bytes) for {capture.Price}; vanilla {(toParking ? "parking" : "garage")} slot {index} cleared.");
		Client.Instance.Send(new CarParkRequestPacket
		{
			RequestId = capture.RequestId, CarLoaderID = -1, PreferredSlot = -1, Car = car, Price = capture.Price
		});
		MelonCoroutines.Start(WarnIfUnanswered(capture));
	}

	private static void ClearLocalSlot(int index, bool toParking)
	{
		var data = Singleton<GameManager>.Instance.GameDataManager;
		if (toParking) data.SaveCarInParking(new NewCarData(), index);
		else data.SaveCarInGarage(new NewCarData(), index);
	}

	private static IEnumerator WarnIfUnanswered(Capture capture)
	{
		float deadline = Time.realtimeSinceStartup + ResultTimeoutSeconds;
		while (sent.ContainsKey(capture.RequestId) && Time.realtimeSinceStartup < deadline) yield return null;
		if (sent.Remove(capture.RequestId))
			Log.Warn($"[Purchase] Request {capture.RequestId} ({capture.Car}): no answer from the server within {ResultTimeoutSeconds} s.");
	}

	private static void OnParkResult(CarParkResultPacket result)
	{
		if (!sent.TryGetValue(result.RequestId, out var capture)) return;
		sent.Remove(result.RequestId);
		if (result.Accepted)
		{
			Log.Info($"[Purchase] Request {result.RequestId}: {capture.Car} bought for {capture.Price}, parking slot {result.Slot}.");
			ModNotify.ShowToast("The car is in the shared parking.");
			return;
		}
		Log.Warn($"[Purchase] Request {result.RequestId}: buying {capture.Car} for {capture.Price} refused: {result.Reason}.");
		ModNotify.ShowMessage("Car not bought", $"{RefusalText(result.Reason)} Nothing was paid and the car was not kept.");
	}

	private static string RefusalText(ParkRefusal reason) => reason switch
	{
		ParkRefusal.NoMoney => "There is not enough shared money.",
		ParkRefusal.ParkingFull => "The shared parking is full.",
		_ => "The server refused the purchase.",
	};

	private static void Drop(string why)
	{
		if (open == null) return;
		Log.Info($"[Purchase] Capture {open.RequestId} ({open.Car}) dropped: {why}.");
		open = null;
	}
}
