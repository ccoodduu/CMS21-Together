using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;

namespace CMS21Together.Logic.Driving;

// shared-race-tracks D6: the driver's race-track laps and speed-track top speed go to the server, which keeps the
// personal bests and the group records; the session profile holds the server's personal bests.
[HarmonyPatch]
public static class TrackRecords
{
	public static ModTrackRecord GroupBestLap { get; private set; }
	public static ModTrackRecord GroupTopSpeed { get; private set; }
	public static long BestLapMs { get; private set; }
	public static int TopSpeedKmh { get; private set; }
	public static int Sent { get; private set; }
	public static long LastSent { get; private set; }

	private static long pendingLapMs = -1;

	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;

	public static void Reset()
	{
		GroupBestLap = null;
		GroupTopSpeed = null;
		BestLapMs = 0;
		TopSpeedKmh = 0;
		Sent = 0;
		LastSent = 0;
	}

	public static string Format(GameScene scene, long value) => scene == GameScene.RaceTrack
		? $"{value / 60000}:{value / 1000 % 60:00}.{value % 1000:000}"
		: $"{value} km/h";

	public static void ApplyWorld(WorldState state)
	{
		GroupBestLap = state.GroupBestLap;
		GroupTopSpeed = state.GroupTopSpeed;
	}

	public static void ApplyRestore(PlayerRestorePacket packet)
	{
		BestLapMs = packet.BestLapMs;
		TopSpeedKmh = packet.TopSpeedKmh;
		WriteProfile();
		Log.Info($"[Tracks] Personal bests from the server: lap {BestLapMs} ms, top speed {TopSpeedKmh} km/h.");
	}

	private static void WriteProfile()
	{
		var profile = Singleton<GameManager>.Instance?.GameDataManager?.CurrentProfileData;
		if (profile == null) return;
		profile.BestRaceTime = BestLapMs;
		profile.TopSpeed = TopSpeedKmh;
	}

	public static void OnUpdate(TrackRecordUpdatePacket packet)
	{
		if (packet == null || Client.Instance == null) return;
		bool mine = packet.PlayerId == Client.Instance.ID;
		if (packet.IsGroupRecord)
		{
			var record = new ModTrackRecord { PlayerName = packet.PlayerName, Value = packet.Value };
			if (packet.Scene == GameScene.RaceTrack) GroupBestLap = record;
			else GroupTopSpeed = record;
			ModNotify.ShowToast($"New group record on the {TrackScenes.NameOf(packet.Scene)}: {packet.PlayerName}, {Format(packet.Scene, packet.Value)}");
		}
		if (!mine) return;

		if (packet.Scene == GameScene.RaceTrack) BestLapMs = packet.Value;
		else TopSpeedKmh = (int)packet.Value;
		WriteProfile();
		if (packet.Scene == GameScene.RaceTrack && TrackManager.Instance != null)
		{
			var race = TrackManager.Instance.TryCast<RaceTrackManager>();
			if (race != null) race.lastBestTime = BestLapMs;
		}
		if (packet.IsPersonalBest && !packet.IsGroupRecord)
			ModNotify.ShowToast($"New personal best on the {TrackScenes.NameOf(packet.Scene)}: {Format(packet.Scene, packet.Value)}");
		Log.Info($"[Tracks] {packet.Scene}: personal best {Format(packet.Scene, packet.Value)} ({(packet.IsPersonalBest ? "new" : "kept by the server")}).");
	}

	private static void Send(GameScene scene, long value)
	{
		Sent++;
		LastSent = value;
		Log.Info($"[Tracks] {scene}: {Format(scene, value)} sent.");
		Client.Instance.Send(new TrackRecordPacket { Scene = scene, Value = value });
	}

	private static bool IsDriverOn(GameScene scene) => Connected && !RideAlong.IsPassenger && ClientScene.LocalScene == scene;

	[HarmonyPatch(typeof(RaceTrackManager), nameof(RaceTrackManager.LastTime))]
	[HarmonyPrefix]
	private static void BeforeLastTime(RaceTrackManager __instance)
	{
		pendingLapMs = -1;
		if (!IsDriverOn(GameScene.RaceTrack) || __instance.timer == null || !__instance.AllCheckpointsDone()) return;
		pendingLapMs = __instance.timer.ElapsedMilliseconds;
	}

	[HarmonyPatch(typeof(RaceTrackManager), nameof(RaceTrackManager.LastTime))]
	[HarmonyPostfix]
	private static void AfterLastTime()
	{
		if (pendingLapMs > 0 && Connected) Send(GameScene.RaceTrack, pendingLapMs);
		pendingLapMs = -1;
	}

	[HarmonyPatch(typeof(FreeTrackManager), nameof(FreeTrackManager.ReturnToGarage))]
	[HarmonyPrefix]
	private static void BeforeSpeedTrackReturn(FreeTrackManager __instance)
	{
		if (!IsDriverOn(GameScene.SpeedTrack)) return;
		int topSpeed = (int)__instance.topSpeed;
		if (topSpeed > 0) Send(GameScene.SpeedTrack, topSpeed);
	}
}
