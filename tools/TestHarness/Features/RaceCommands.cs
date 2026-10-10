using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21Together.Logic.Driving;

namespace TogetherTestHarness.Features;

// track-races: race-start asks for a race as the session panel's button does; race-restart is the pause menu's restart
// (TrackManager.RunRestart, not the countdown's own); race-state shows the local race with wall-clock times.
public static class RaceCommands
{
    [HarnessCommand("race-start")]
    private static object Start(string args)
    {
        int laps = int.Parse((args ?? "").Trim(), CultureInfo.InvariantCulture);
        string problem = TrackRaceSync.RequestStart(laps);
        return new { sent = problem == null, message = TrackRaceSync.Message };
    }

    [HarnessCommand("race-restart")]
    private static object Restart(string args)
    {
        var track = TrackManager.Instance ?? throw new InvalidOperationException("no TrackManager (not on a track)");
        int quits = TrackRaceSync.QuitsSent;
        track.RunRestart();
        return new { quitSent = TrackRaceSync.QuitsSent - quits };
    }

    [HarnessCommand("race-state")]
    private static object State(string args) => Dump();

    public static object Dump()
    {
        var race = TrackRaceSync.Current;
        return new Dictionary<string, object>
        {
            ["race"] = race == null ? null : new
            {
                race.RaceId, race.Laps, race.StarterId, race.Participants, race.Grid, race.Box, phase = race.Phase.ToString(), race.PingMs,
                race.StartWallMs, race.ReleasedWallMs, race.GreenWallMs, race.LapsSent, race.TotalMs, race.Restarted,
            },
            ["status"] = TrackRaceSync.Status(),
            ["message"] = TrackRaceSync.Message,
            ["quitsSent"] = TrackRaceSync.QuitsSent,
            ["results"] = TrackRaceSync.Results.Select(r => (object)new
            {
                r.RaceId, r.Laps,
                order = r.Order.Select(e => (object)new { e.PlayerId, name = e.PlayerName, e.Laps, e.TotalMs, e.BestLapMs, e.Dnf, reason = e.DnfReason.ToString() }).ToList(),
                summary = TrackRaceSync.Summary(r),
            }).ToList(),
            ["panelShown"] = TrackRaceSync.PanelShown,
            ["spawnMoved"] = RaceGrid.Moved,
            ["lastBox"] = RaceGrid.LastBox,
            ["wall"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }
}
