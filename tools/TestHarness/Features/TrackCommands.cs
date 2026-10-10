using System;
using System.Collections.Generic;
using System.Globalization;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Driving;
using CMS21Together.Network;

namespace TogetherTestHarness.Features;

// shared-race-tracks: lap and top speed records. track-lap drives the game's own RaceTrackManager.LastTime with a set
// lap time, as the finish line trigger does once every checkpoint is done.
public static class TrackCommands
{
    [HarnessCommand("track-lap")]
    private static object Lap(string args)
    {
        long ms = long.Parse((args ?? "").Trim(), CultureInfo.InvariantCulture);
        var race = UnityEngine.Object.FindObjectOfType<RaceTrackManager>() ?? throw new InvalidOperationException("no RaceTrackManager (not on the race track)");
        var timer = race.timer ?? throw new InvalidOperationException("the race track has no timer yet");
        var checkpoints = race.checkPointsList ?? throw new InvalidOperationException("the race track has no checkpoints yet");
        timer.Stop();
        timer.elapsed = ms * Il2CppSystem.Diagnostics.Stopwatch.Frequency / 1000;
        long elapsedMs = timer.ElapsedMilliseconds;
        race.numberOfCheckpoints = checkpoints.Count - 1;
        int sentBefore = TrackRecords.Sent;
        race.LastTime();
        race.laps++;
        race.numberOfCheckpoints = 0;
        var profile = Singleton<GameManager>.Instance?.GameDataManager?.CurrentProfileData;
        return new Dictionary<string, object>
        {
            ["elapsedMs"] = elapsedMs,
            ["checkpoints"] = checkpoints.Count,
            ["lastBestTime"] = race.lastBestTime,
            ["bestRaceTime"] = profile == null ? -1 : profile.BestRaceTime,
            ["sent"] = TrackRecords.Sent - sentBefore,
            ["lastSent"] = TrackRecords.LastSent,
            ["passenger"] = RideAlong.IsPassenger,
        };
    }

    [HarnessCommand("track-topspeed")]
    private static object TopSpeed(string args)
    {
        var free = UnityEngine.Object.FindObjectOfType<FreeTrackManager>() ?? throw new InvalidOperationException("no FreeTrackManager (not on the speed track)");
        free.topSpeed = float.Parse((args ?? "").Trim(), CultureInfo.InvariantCulture);
        return new { free.topSpeed };
    }

    [HarnessCommand("track-record-send")]
    private static object RecordSend(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Enum.TryParse(parts[0], out GameScene scene)) throw new ArgumentException("usage: track-record-send <RaceTrack|SpeedTrack|...> <value>");
        long value = long.Parse(parts[1], CultureInfo.InvariantCulture);
        Client.Instance.Send(new TrackRecordPacket { Scene = scene, Value = value });
        return new { scene = scene.ToString(), value };
    }

    public static object Dump()
    {
        var profile = Singleton<GameManager>.Instance?.GameDataManager?.CurrentProfileData;
        return new Dictionary<string, object>
        {
            ["groupBestLap"] = TrackRecords.GroupBestLap == null ? null : new { name = TrackRecords.GroupBestLap.PlayerName, value = TrackRecords.GroupBestLap.Value },
            ["groupTopSpeed"] = TrackRecords.GroupTopSpeed == null ? null : new { name = TrackRecords.GroupTopSpeed.PlayerName, value = TrackRecords.GroupTopSpeed.Value },
            ["bestLapMs"] = TrackRecords.BestLapMs,
            ["topSpeedKmh"] = TrackRecords.TopSpeedKmh,
            ["profileBestRaceTime"] = profile == null ? -1 : profile.BestRaceTime,
            ["profileTopSpeed"] = profile == null ? -1 : profile.TopSpeed,
            ["sent"] = TrackRecords.Sent,
            ["destination"] = ClientScene.Destination.ToString(),
        };
    }
}
