using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Driving;

// track-races D2: a racer's client restarts the race track at once (the game's own Restart: start spot, timer, laps),
// holds the light sequence at its throttle wait and releases it LightsMs before the shared start, so the game's own
// lights turn green together (spike 1.1: release to green is 3 s plus a frame). D3: laps after the green go to the
// server; a pause-menu restart during the race quits it.
[HarmonyPatch]
public static class TrackRaceSync
{
	public const int LightsMs = 3000;
	// The release waits for the next frame and the three one-second waits end a frame late, so release this many frames early.
	private const float ReleaseLeadFrames = 1.5f;
	public const int MinLaps = 1;
	public const int MaxLaps = 20;
	public const int KeptResults = 10;

	public enum Phase
	{
		Countdown,
		Racing,
		Finished,
		Out,
		Watching
	}

	public class RaceView
	{
		public int RaceId;
		public int Laps;
		public int StarterId;
		public List<int> Participants;
		public Phase Phase;
		public int PingMs;
		public long StartAtMs;
		public long StartWallMs;
		public long ReleasedWallMs;
		public long GreenWallMs;
		public int LapsSent;
		public long TotalMs;
		public bool Restarted;
	}

	private static bool subscribed;
	private static bool ownRestart;

	public static RaceView Current { get; private set; }
	public static List<ModRaceResult> Results { get; private set; } = new List<ModRaceResult>();
	public static string Message { get; private set; } = "";
	public static int LapsChoice { get; set; } = 3;
	public static int QuitsSent { get; private set; }

	private static long NowMs => PacketClock.NowMs;
	private static long WallMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;
	private static bool Racing => Current != null && (Current.Phase == Phase.Countdown || Current.Phase == Phase.Racing);

	public static bool PanelShown => Connected && ClientScene.LocalScene == GameScene.RaceTrack;
	public static ModRaceResult LastResult => Results.LastOrDefault();

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		ClientScene.LeavingScene += (from, to) =>
		{
			if (Current == null || from != GameScene.RaceTrack) return;
			if (Racing) Log.Info($"[Race] Race {Current.RaceId}: left the race track during the race.");
			Current = null;
		};
	}

	public static void Reset()
	{
		Current = null;
		Results = new List<ModRaceResult>();
		Message = "";
		QuitsSent = 0;
	}

	public static void ApplyWorld(WorldState state) => Results = state.RaceResults?.ToList() ?? new List<ModRaceResult>();

	private static string NameOf(int playerId)
	{
		if (Client.Instance != null && playerId == Client.Instance.ID) return "You";
		return PresenceManager.Roster.TryGetValue(playerId, out var player) ? player.Record.Username : $"Player {playerId}";
	}

	public static string RequestStart(int laps)
	{
		string problem = !Connected ? "Not in a session."
			: ClientScene.LocalScene != GameScene.RaceTrack ? "Races are only on the race track."
			: RideAlong.IsPassenger ? "Passengers cannot start a race."
			: !DriveCapture.Active ? "Drive a car on the race track to start a race."
			: laps < MinLaps || laps > MaxLaps ? $"Choose {MinLaps} to {MaxLaps} laps."
			: Racing ? "A race is already running on the race track."
			: null;
		if (problem != null)
		{
			Message = problem;
			return problem;
		}
		Client.Instance.Send(new RaceStartRequestPacket { Scene = GameScene.RaceTrack, Laps = laps });
		Message = $"Starting a {laps}-lap race...";
		Log.Info($"[Race] Start of a {laps}-lap race requested.");
		return null;
	}

	public static void OnRefused(RaceRefusedPacket packet)
	{
		Message = packet.Reason switch
		{
			RaceRefusal.RaceRunning => "A race is already running on the race track.",
			RaceRefusal.NotDriving => "Drive a car on the race track to start a race.",
			RaceRefusal.Passenger => "Passengers cannot start a race.",
			RaceRefusal.LapsOutOfRange => $"Choose {MinLaps} to {MaxLaps} laps.",
			_ => "Races are only on the race track.",
		};
		ModNotify.ShowToast(Message);
		Log.Info($"[Race] Start of a {packet.Laps}-lap race refused: {packet.Reason} (running race {packet.RunningRaceId}).");
	}

	public static void OnCountdown(RaceCountdownPacket packet)
	{
		if (packet == null || Client.Instance == null || ClientScene.LocalScene != packet.Scene) return;
		bool participant = packet.Participants != null && packet.Participants.Contains(Client.Instance.ID) && !RideAlong.IsPassenger;
		int ping = ClientData.PlayerPings.TryGetValue(Client.Instance.ID, out int ms) ? ms : 0;
		long startIn = packet.StartInMs - ping / 2;
		long waited = Math.Max(0, NowMs - PacketClock.ReceivedMs);
		Current = new RaceView
		{
			RaceId = packet.RaceId, Laps = packet.Laps, StarterId = packet.StarterId, Participants = packet.Participants?.ToList() ?? new List<int>(),
			Phase = participant ? Phase.Countdown : Phase.Watching, PingMs = ping, StartAtMs = NowMs - waited + startIn, StartWallMs = WallMs - waited + startIn,
		};
		string laps = packet.Laps == 1 ? "1-lap" : $"{packet.Laps}-lap";
		Message = "";
		if (!participant)
		{
			ModNotify.ShowToast(packet.StartInMs > 0
				? $"{NameOf(packet.StarterId)} started a {laps} race; it starts in {packet.StartInMs / 1000} s. You are watching."
				: $"A {laps} race is running on the race track. You are watching.");
			Log.Info($"[Race] Race {packet.RaceId}: watching ({packet.StartInMs} ms to the start).");
			return;
		}
		ModNotify.ShowToast($"{NameOf(packet.StarterId)} started a {laps} race. The lights go green in {startIn / 1000} s.");
		Log.Info($"[Race] Race {packet.RaceId}: {packet.Laps} laps with {string.Join(", ", Current.Participants.Select(NameOf))}; green planned at {Current.StartWallMs} (in {startIn} ms, ping {ping} ms).");

		var track = TrackManager.Instance;
		if (track == null || !track.SupportsRestart)
		{
			Log.Warn($"[Race] Race {packet.RaceId}: no track manager to restart.");
			return;
		}
		ownRestart = true;
		try
		{
			track.RunRestart();
			Current.Restarted = true;
		}
		finally
		{
			ownRestart = false;
		}
	}

	public static void Update()
	{
		if (Current == null || Current.Phase != Phase.Countdown || Current.ReleasedWallMs == 0) return;
		var race = TrackManager.Instance?.TryCast<RaceTrackManager>();
		if (race == null || !race.readySetGo) return;
		Current.GreenWallMs = WallMs;
		Current.Phase = Phase.Racing;
		Log.Info($"[Race] Race {Current.RaceId}: green at {Current.GreenWallMs} ({Current.GreenWallMs - Current.StartWallMs:+0;-0;0} ms from the plan).");
	}

	public static void OnLapDone(long lapMs)
	{
		if (Current == null || Current.Phase != Phase.Racing || !Connected) return;
		Current.LapsSent++;
		Current.TotalMs += lapMs;
		Client.Instance.Send(new RaceLapPacket { RaceId = Current.RaceId, Lap = Current.LapsSent, LapMs = lapMs });
		Log.Info($"[Race] Race {Current.RaceId}: lap {Current.LapsSent}/{Current.Laps} in {TrackRecords.Format(GameScene.RaceTrack, lapMs)} sent.");
		if (Current.LapsSent < Current.Laps) return;
		Current.Phase = Phase.Finished;
		ModNotify.ShowToast($"Finished in {TrackRecords.Format(GameScene.RaceTrack, Current.TotalMs)}. Waiting for the others.");
	}

	public static void OnResult(RaceResultPacket packet)
	{
		var result = packet?.Result;
		if (result == null) return;
		Results.RemoveAll(r => r.RaceId == result.RaceId);
		Results.Add(result);
		while (Results.Count > KeptResults) Results.RemoveAt(0);
		if (Current != null && Current.RaceId == result.RaceId) Current = null;
		ModNotify.ShowToast($"Race result: {Summary(result)}");
		Log.Info($"[Race] Race {result.RaceId} result: {Summary(result)}.");
	}

	public static string Summary(ModRaceResult result) => string.Join(", ", result.Order.Select((e, i) => e.Dnf
		? $"{e.PlayerName} DNF ({DnfText(e.DnfReason)})"
		: $"{i + 1}. {e.PlayerName} {TrackRecords.Format(GameScene.RaceTrack, e.TotalMs)}"));

	private static string DnfText(RaceDnfReason reason) => reason switch
	{
		RaceDnfReason.Quit => "restarted",
		RaceDnfReason.LeftTrack => "left the track",
		RaceDnfReason.Disconnected => "disconnected",
		RaceDnfReason.Timeout => "out of time",
		_ => reason.ToString(),
	};

	public static string Status()
	{
		if (Current == null) return Message;
		long toStart = Current.StartAtMs - NowMs;
		return Current.Phase switch
		{
			Phase.Countdown => toStart > 0 ? $"Race {Current.RaceId}: green in {toStart / 1000 + 1} s." : $"Race {Current.RaceId}: get ready.",
			Phase.Racing => $"Race {Current.RaceId}: lap {Math.Min(Current.LapsSent + 1, Current.Laps)} of {Current.Laps}.",
			Phase.Finished => $"Race {Current.RaceId}: finished in {TrackRecords.Format(GameScene.RaceTrack, Current.TotalMs)}, waiting for the others.",
			Phase.Out => $"Race {Current.RaceId}: you are out.",
			_ => $"Race {Current.RaceId} is running. You are watching.",
		};
	}

	[HarmonyPatch(typeof(RaceTrackManager._Prepare_d__19), nameof(RaceTrackManager._Prepare_d__19.MoveNext))]
	[HarmonyPrefix]
	private static void HoldLights(RaceTrackManager._Prepare_d__19 __instance)
	{
		if (Current == null || Current.Phase != Phase.Countdown || !Current.Restarted) return;
		int state = __instance.__1__state;
		if (state != 3 && state != 4) return;
		var input = __instance.__4__this?.carInput;
		if (input == null) return;
		long now = NowMs;
		long releaseAt = Current.StartAtMs - LightsMs - (long)(ReleaseLeadFrames * 1000f * Mathf.Min(Time.smoothDeltaTime, 0.1f));
		if (now < releaseAt)
		{
			input.throttle = 0f;
			return;
		}
		input.throttle = 1f;
		if (Current.ReleasedWallMs != 0) return;
		Current.ReleasedWallMs = WallMs;
		long late = now - releaseAt;
		Log.Info($"[Race] Race {Current.RaceId}: lights released{(late > 100 ? $" {late} ms late (the restart took longer)" : "")}.");
	}

	[HarmonyPatch(typeof(RaceTrackManager._Restart_d__20), nameof(RaceTrackManager._Restart_d__20.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeRestart(RaceTrackManager._Restart_d__20 __instance)
	{
		if (ownRestart || __instance.__1__state != 0 || !Racing || !Connected) return;
		Current.Phase = Phase.Out;
		QuitsSent++;
		Client.Instance.Send(new RaceQuitPacket { RaceId = Current.RaceId });
		ModNotify.ShowToast("You restarted, so you are out of the race.");
		Log.Info($"[Race] Race {Current.RaceId}: restarted from the pause menu; quit sent.");
	}
}
