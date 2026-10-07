using System.Collections;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Hook;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Away;

// sync-test-drive-and-diagnostics D2/D4/D5/D6: the departure to the test track waits for the server's away claim
// (the departure coroutine is held before its first step), the drive's mileage and dirt are sent when leaving the
// track, and the claim is released once the return's examine report has run.
[HarmonyPatch]
public static class TestDriveSync
{
	private const float FlushWaitSeconds = 3f;
	private const float ReadyWaitSeconds = 20f;

	private enum Phase { None, Asking, Granted, Refused }

	private static Phase phase;
	private static int departingLoader = -1;
	private static string departingScene;
	private static float grantedAt;
	private static string refusalMessage;

	public static bool SkipNextResult { get; set; }

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;

	public static void Initialize()
	{
		ClientScene.LeavingScene += OnLeavingScene;
	}

	public static int LoaderOfSelectedCar()
	{
		var places = CarLoaderPlaces.Get();
		for (int i = 0; places != null && i < places.GetCarLoadersCount(); i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader != null && carLoader.IsCarLoaded() && carLoader.GetSaveName() == GlobalData.SelectedCarLoader) return i;
		}
		return -1;
	}

	public static bool HoldDeparture(string sceneName, SceneType sceneType)
	{
		if (sceneType != SceneType.TestTrack || !Connected || ClientScene.LocalScene != GameScene.Garage) return false;
		int loader = LoaderOfSelectedCar();
		if (loader < 0 || CarAwaySync.IsMine(loader, CarAwayKind.TestTrack)) return false;

		phase = Phase.Asking;
		departingLoader = loader;
		departingScene = sceneName;
		CarAwaySync.Request(loader, CarAwayKind.TestTrack,
			() =>
			{
				phase = Phase.Granted;
				grantedAt = Time.realtimeSinceStartup;
			},
			(refusal, owner) =>
			{
				phase = Phase.Refused;
				refusalMessage = refusal switch
				{
					CarAwayRefusal.Busy => $"{CarAwaySync.OwnerName(owner)} has this car {CarAwaySync.Activity(CarAwaySync.All.TryGetValue(loader, out var away) ? away.Kind : CarAwayKind.TestTrack)}.",
					CarAwayRefusal.InUse => "Another player is working on this car.",
					CarAwayRefusal.NotReady => "This car is still loading for multiplayer.",
					_ => "The server did not answer. Try again.",
				};
			});
		return true;
	}

	[HarmonyPatch(typeof(NotificationCenter._SelectSceneToLoad_d__34), nameof(NotificationCenter._SelectSceneToLoad_d__34.MoveNext))]
	[HarmonyPrefix]
	private static bool BeforeDepartureStep(NotificationCenter._SelectSceneToLoad_d__34 __instance, ref bool __result)
	{
		if (phase == Phase.None || __instance.__1__state != 0 || __instance.sceneType != SceneType.TestTrack) return true;
		switch (phase)
		{
			case Phase.Asking:
				__result = true;
				return false;
			case Phase.Granted:
				bool pending = PartChangeTracker.IsPending(departingLoader) || CarDetailsSync.IsDirty(departingLoader);
				if (pending && Time.realtimeSinceStartup - grantedAt < FlushWaitSeconds)
				{
					__result = true;
					return false;
				}
				phase = Phase.None;
				Log.Info($"[TestDrive] Loader {departingLoader}: departing to the test track.");
				SceneHooks.Leave(departingScene, SceneType.TestTrack);
				return true;
			default:
				phase = Phase.None;
				Log.Info($"[TestDrive] Loader {departingLoader}: departure cancelled ({refusalMessage}).");
				GlobalData.SelectedCarLoader = "";
				GlobalData.TestToShow = "";
				ModNotify.ShowToast(refusalMessage);
				__result = false;
				return false;
		}
	}

	private static void OnLeavingScene(GameScene from, GameScene to)
	{
		if (from != GameScene.TestTrack || to != GameScene.Garage) return;
		foreach (var pair in CarAwaySync.All)
		{
			if (pair.Value.Owner != Client.Instance.ID || pair.Value.Kind != CarAwayKind.TestTrack) continue;
			if (SkipNextResult)
			{
				SkipNextResult = false;
				Log.Info($"[TestDrive] Loader {pair.Key}: result skipped (harness), NewMileage {GlobalData.NewMileage} kept.");
				return;
			}
			var trackCar = PrepareCarPhysics.Get()?.CarLoader ?? Object.FindObjectOfType<CarLoader>();
			var cosmetics = trackCar == null ? null : CarDetailsIO.Read(trackCar, CarDetailSection.BodyCosmetics).BodyCosmetics;
			Log.Info($"[TestDrive] Loader {pair.Key}: result sent (+{GlobalData.NewMileage} km, {cosmetics?.Count ?? 0} parts).");
			Client.Instance.Send(new TestDriveResultPacket { CarLoaderID = pair.Key, SpawnSeq = pair.Value.SpawnSeq, MileageDeltaKm = GlobalData.NewMileage, Cosmetics = cosmetics });
			return;
		}
	}

	public static void OnResultAck(TestDriveResultAckPacket packet)
	{
		Log.Info($"[TestDrive] Loader {packet.CarLoaderID}: result {(packet.Applied ? "applied" : "not applied")} by the server.");
		if (packet.Applied) GlobalData.NewMileage = 0;
	}

	public static IEnumerator AfterReturnSync()
	{
		int loader = LoaderOfSelectedCar();
		float deadline = Time.realtimeSinceStartup + ReadyWaitSeconds;
		while (loader >= 0 && (!CarPartsSync.IsReady(loader) || CarDetailsSync.IsApplying(loader)) && Time.realtimeSinceStartup < deadline)
			yield return null;
		if (loader >= 0 && !CarPartsSync.IsReady(loader)) Log.Warn($"[TestDrive] Loader {loader} was not ready after {ReadyWaitSeconds} s.");

		if (GlobalData.NewMileage != 0)
		{
			var carLoader = loader < 0 ? null : CarLoaderPlaces.Get().GetCarLoaderByIndex(loader);
			if (carLoader != null)
			{
				var info = carLoader.CarInfoData;
				info.Mileage += GlobalData.NewMileage;
				carLoader.CarInfoData = info;
				Log.Info($"[TestDrive] Loader {loader}: {GlobalData.NewMileage} km added locally (no result applied).");
			}
			else
			{
				Log.Info($"[TestDrive] {GlobalData.NewMileage} km dropped: no car '{GlobalData.SelectedCarLoader}'.");
			}
			GlobalData.NewMileage = 0;
		}
	}

	public static void ReleaseAfterReturn()
	{
		foreach (var pair in CarAwaySync.All)
			if (pair.Value.Owner == Client.Instance.ID && pair.Value.Kind == CarAwayKind.TestTrack)
			{
				MelonLoader.MelonCoroutines.Start(ReleaseWhenFlushed(pair.Key));
				return;
			}
	}

	private static IEnumerator ReleaseWhenFlushed(int loader)
	{
		float deadline = Time.realtimeSinceStartup + FlushWaitSeconds;
		yield return new WaitForSeconds(0.5f);
		while (PartChangeTracker.IsPending(loader) && Time.realtimeSinceStartup < deadline) yield return null;
		Log.Info($"[TestDrive] Loader {loader}: back from the test track, claim released.");
		CarAwaySync.Release(loader);
	}

	[HarmonyPatch(typeof(CMS.UI.Windows.ExamineReportWindow), nameof(CMS.UI.Windows.ExamineReportWindow.GetExaminedParts))]
	[HarmonyPostfix]
	private static void AfterExaminedParts()
	{
		if (Connected) ReleaseAfterReturn();
	}
}
