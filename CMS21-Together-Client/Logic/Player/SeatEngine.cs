using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Placement;
using CMS21Together.Logic.Tools;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Player;

// sync-players-and-scenes D7: SitInside/ExitFromInterior only create coroutines, so the seat is confirmed by polling
// the game mode.
[HarmonyPatch]
public static class SeatEngine
{
	private const float PollSeconds = 0.25f;
	private const float PendingTimeoutSeconds = 10f;
	private const float RpmThreshold = 150f;

	private static int pendingLoader = PlayerPresenceRecord.NoCar;
	private static bool pendingLeft;
	private static float pendingSince;
	private static float nextPoll;
	private static bool subscribed;
	private static bool wasSeatedMode;

	public static int SeatCarLoaderId { get; private set; } = PlayerPresenceRecord.NoCar;
	public static bool SeatLeft { get; private set; }
	public static bool IsSeated => SeatCarLoaderId != PlayerPresenceRecord.NoCar;
	public static int PendingCarLoaderId => pendingLoader;
	public static int EngineCarLoaderId { get; private set; } = PlayerPresenceRecord.NoCar;
	public static bool EngineRunning { get; private set; }
	public static float EngineRpm { get; private set; }

	public static bool InSeatedMode
	{
		get
		{
			var gameMode = GameMode.Get();
			if (gameMode == null) return false;
			var mode = gameMode.GetCurrentMode();
			return mode == global::gameMode.Interior || mode == global::gameMode.CarDrive;
		}
	}

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		CarPlacementSync.BeforeRemoteCarMove += loader => MelonCoroutines.Start(PresenceManager.EnsureNotSeatedIn(loader));
	}

	public static void Reset()
	{
		pendingLoader = PlayerPresenceRecord.NoCar;
		wasSeatedMode = false;
		SeatCarLoaderId = PlayerPresenceRecord.NoCar;
		SeatLeft = false;
		EngineCarLoaderId = PlayerPresenceRecord.NoCar;
		EngineRunning = false;
		EngineRpm = 0f;
	}

	public static void FillRecord(PlayerPresenceRecord record)
	{
		record.SeatCarLoaderId = SeatCarLoaderId;
		record.SeatLeft = SeatLeft;
		record.EngineCarLoaderId = EngineCarLoaderId;
		record.EngineRunning = EngineRunning;
		record.EngineRpm = EngineRpm;
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.SitInside))]
	[HarmonyPrefix]
	private static void BeforeSitInside(CarLoader carLoader, bool left)
	{
		if (!Active || carLoader == null) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		if (loader < 0) return;
		pendingLoader = loader;
		pendingLeft = left;
		pendingSince = Time.realtimeSinceStartup;
		Log.Debug($"[Presence] Sitting down in car {loader} ({(left ? "left" : "right")}).");
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.ExitFromInterior))]
	[HarmonyPrefix]
	private static void BeforeExitFromInterior()
	{
		pendingLoader = PlayerPresenceRecord.NoCar;
		if (Active && IsSeated) SetSeat(PlayerPresenceRecord.NoCar, false);
	}

	public static void Update()
	{
		if (!Active || Time.realtimeSinceStartup < nextPoll) return;
		nextPoll = Time.realtimeSinceStartup + PollSeconds;
		PollSeat();
		PollEngine();
		RemoteEngines.Refresh();
	}

	private static void PollSeat()
	{
		bool seatedMode = InSeatedMode;
		bool seatedModeEnded = wasSeatedMode && !seatedMode;
		wasSeatedMode = seatedMode;
		if (pendingLoader != PlayerPresenceRecord.NoCar)
		{
			if (seatedMode)
			{
				SetSeat(pendingLoader, pendingLeft);
				pendingLoader = PlayerPresenceRecord.NoCar;
			}
			else if (Time.realtimeSinceStartup - pendingSince > PendingTimeoutSeconds)
			{
				Log.Warn($"[Presence] Sitting down in car {pendingLoader} was not confirmed by the game mode.");
				pendingLoader = PlayerPresenceRecord.NoCar;
			}
			return;
		}
		if (IsSeated && !seatedMode) SetSeat(PlayerPresenceRecord.NoCar, false);
		else if (seatedModeEnded) Movement.ForceSend();
	}

	private static void PollEngine()
	{
		var controller = Singleton<GameManager>.Instance?.EngineAudioController;
		int loader = PlayerPresenceRecord.NoCar;
		float rpm = 0f;
		if (controller != null && controller.GetEngineStartingOrWorking() && controller.carLoader != null)
		{
			loader = CarLoaderPlaces.Get().GetCarLoaderId(controller.carLoader);
			if (loader >= 0) rpm = controller.CurrentRpm;
			else loader = PlayerPresenceRecord.NoCar;
		}
		bool running = loader != PlayerPresenceRecord.NoCar;
		if (running == EngineRunning && loader == EngineCarLoaderId && Mathf.Abs(rpm - EngineRpm) <= RpmThreshold) return;

		if (running != EngineRunning || loader != EngineCarLoaderId)
			Log.Info(running ? $"[Presence] Engine of car {loader} running." : "[Presence] Engine stopped.");
		EngineCarLoaderId = loader;
		EngineRunning = running;
		EngineRpm = rpm;
		PresenceManager.PublishLocal();
	}

	public static void OnSeatRefused(SeatRefusedPacket packet)
	{
		if (pendingLoader == packet.CarLoaderID && pendingLeft == packet.SeatLeft) pendingLoader = PlayerPresenceRecord.NoCar;
		if (SeatCarLoaderId != packet.CarLoaderID || SeatLeft != packet.SeatLeft) return;
		var game = GameScript.Get();
		if (game == null) return;
		Log.Info($"[Presence] Seat {(packet.SeatLeft ? "left" : "right")} of car {packet.CarLoaderID} is taken by player {packet.HolderPlayerId}; leaving it.");
		if (EngineCarLoaderId == packet.CarLoaderID) Singleton<GameManager>.Instance?.EngineAudioController?.EngineStop();
		game.StartCoroutine(game.ExitFromInterior(true));
		ModNotify.ShowToast($"{ToolSync.PlayerName(packet.HolderPlayerId)} is sitting there.");
	}

	private static void SetSeat(int loader, bool left)
	{
		SeatCarLoaderId = loader;
		SeatLeft = left;
		Log.Info(loader == PlayerPresenceRecord.NoCar ? "[Presence] Seat left." : $"[Presence] Seated in car {loader} ({(left ? "left" : "right")}).");
		PresenceManager.PublishLocal();
		if (loader == PlayerPresenceRecord.NoCar) Movement.ForceSend();
	}
}
