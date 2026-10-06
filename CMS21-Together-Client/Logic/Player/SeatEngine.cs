using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Player;

// sync-players-and-scenes D7: SitInside/ExitFromInterior only create coroutines, so the seat is confirmed by polling
// the game mode.
[HarmonyPatch]
public static class SeatEngine
{
	private const float PollSeconds = 0.25f;
	private const float PendingTimeoutSeconds = 10f;

	private static int pendingLoader = PlayerPresenceRecord.NoCar;
	private static bool pendingLeft;
	private static float pendingSince;
	private static float nextPoll;

	public static int SeatCarLoaderId { get; private set; } = PlayerPresenceRecord.NoCar;
	public static bool SeatLeft { get; private set; }
	public static bool IsSeated => SeatCarLoaderId != PlayerPresenceRecord.NoCar;
	public static int PendingCarLoaderId => pendingLoader;

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

	public static void Reset()
	{
		pendingLoader = PlayerPresenceRecord.NoCar;
		SeatCarLoaderId = PlayerPresenceRecord.NoCar;
		SeatLeft = false;
	}

	public static void FillRecord(PlayerPresenceRecord record)
	{
		record.SeatCarLoaderId = SeatCarLoaderId;
		record.SeatLeft = SeatLeft;
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
	}

	private static void PollSeat()
	{
		bool seatedMode = InSeatedMode;
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
