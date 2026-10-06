using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Away;

// sync-test-drive-and-diagnostics D8: the test path takes an away claim when it starts; the claim is released with the
// car's specialState after the examine report, or by the watchdog when the player has left the path without a report.
[HarmonyPatch]
public static class PathTestSync
{
	private const float WatchdogSeconds = 5f;

	private static float outsideSince = -1f;

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	public static void Initialize()
	{
		CarAwaySync.Released += OnReleased;
	}

	[HarmonyPatch(typeof(PathTestManager), nameof(PathTestManager.Prepare))]
	[HarmonyPrefix]
	private static bool BeforePrepare(PathTestManager __instance)
	{
		if (!Active || __instance.carLoader == null) return true;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance.carLoader);
		return loader < 0 || !CarAwaySync.BlockIfLocked(loader, "test path");
	}

	[HarmonyPatch(typeof(PathTestManager), nameof(PathTestManager.Prepare))]
	[HarmonyPostfix]
	private static void AfterPrepare(PathTestManager __instance)
	{
		if (!Active || __instance.carLoader == null) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance.carLoader);
		if (loader < 0) return;
		outsideSince = -1f;
		CarAwaySync.Request(loader, CarAwayKind.PathTest, null, (refusal, owner) =>
		{
			Log.Info($"[PathTest] Loader {loader}: claim refused ({refusal}); the result will not be kept.");
			ModNotify.ShowToast(refusal == CarAwayRefusal.Busy ? $"{CarAwaySync.OwnerName(owner)} is using this car." : "Another player is working on this car.");
		});
	}

	public static void ReleaseAfterReport()
	{
		foreach (var pair in CarAwaySync.All.Where(p => p.Value.Owner == Client.Instance.ID && p.Value.Kind == CarAwayKind.PathTest).ToList())
		{
			var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(pair.Key);
			int specialState = carLoader == null ? -1 : carLoader.specialState;
			Log.Info($"[PathTest] Loader {pair.Key}: released after the report (specialState {specialState}).");
			CarAwaySync.Release(pair.Key, specialState);
		}
	}

	public static void Update()
	{
		if (!Active || !CarAwaySync.All.Any(p => p.Value.Owner == Client.Instance.ID && p.Value.Kind == CarAwayKind.PathTest))
		{
			outsideSince = -1f;
			return;
		}
		var mode = GameMode.Get()?.GetCurrentMode();
		bool inside = mode == gameMode.PathTest || mode == gameMode.UI;
		if (inside)
		{
			outsideSince = -1f;
			return;
		}
		if (outsideSince < 0f) outsideSince = Time.realtimeSinceStartup;
		else if (Time.realtimeSinceStartup - outsideSince > WatchdogSeconds)
		{
			outsideSince = -1f;
			foreach (var pair in CarAwaySync.All.Where(p => p.Value.Owner == Client.Instance.ID && p.Value.Kind == CarAwayKind.PathTest).ToList())
			{
				Log.Info($"[PathTest] Loader {pair.Key}: left the test path without a report, claim released.");
				CarAwaySync.Release(pair.Key);
			}
		}
	}

	private static void OnReleased(int loader, int specialState)
	{
		if (specialState < 0) return;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded()) return;
		carLoader.specialState = specialState;
		Log.Info($"[PathTest] Loader {loader}: specialState {specialState} applied.");
	}

	[HarmonyPatch(typeof(CMS.UI.Windows.ExamineReportWindow), nameof(CMS.UI.Windows.ExamineReportWindow.GetExaminedParts))]
	[HarmonyPostfix]
	private static void AfterExaminedParts()
	{
		if (Active) ReleaseAfterReport();
	}
}
