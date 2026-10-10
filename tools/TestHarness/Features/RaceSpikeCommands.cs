using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using CMS;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// track-races spike 1.1: the states of RaceTrackManager's Restart and Prepare coroutines with wall-clock times, the
// moment readySetGo turns true, and two knobs: "eof null" yields null instead of WaitForEndOfFrame (headless games never
// resume after it) and "throttle on" presses the throttle the light sequence waits for.
[HarmonyPatch]
public static class RaceSpikeCommands
{
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static readonly List<object> events = new List<object>();
    private static WaitForEndOfFrame savedEof;
    private static bool forceThrottle;
    private static bool watching;
    private static bool lastReady;
    private static int prepareCalls;

    private static long Wall => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static void Add(string what, object detail = null)
    {
        if (events.Count < 400) events.Add(new { t = clock.ElapsedMilliseconds, wall = Wall, frame = Time.frameCount, what, detail });
    }

    [HarmonyPatch(typeof(RaceTrackManager._Prepare_d__19), nameof(RaceTrackManager._Prepare_d__19.MoveNext))]
    [HarmonyPrefix]
    private static void BeforePrepare(RaceTrackManager._Prepare_d__19 __instance, out int __state)
    {
        __state = __instance.__1__state;
        prepareCalls++;
        if (forceThrottle && (__state == 3 || __state == 4))
        {
            var input = __instance.__4__this?.carInput;
            if (input != null) input.throttle = 1f;
        }
    }

    [HarmonyPatch(typeof(RaceTrackManager._Prepare_d__19), nameof(RaceTrackManager._Prepare_d__19.MoveNext))]
    [HarmonyPostfix]
    private static void AfterPrepare(RaceTrackManager._Prepare_d__19 __instance, bool __result, int __state)
    {
        int to = __instance.__1__state;
        if (to == __state && __result) return;
        Add("prepare", new { from = __state, to, result = __result, current = Describe(__instance.__2__current) });
    }

    [HarmonyPatch(typeof(RaceTrackManager._Restart_d__20), nameof(RaceTrackManager._Restart_d__20.MoveNext))]
    [HarmonyPrefix]
    private static void BeforeRestart(RaceTrackManager._Restart_d__20 __instance, out int __state) => __state = __instance.__1__state;

    [HarmonyPatch(typeof(RaceTrackManager._Restart_d__20), nameof(RaceTrackManager._Restart_d__20.MoveNext))]
    [HarmonyPostfix]
    private static void AfterRestart(RaceTrackManager._Restart_d__20 __instance, bool __result, int __state)
    {
        int to = __instance.__1__state;
        if (to == __state && __result) return;
        Add("restart", new { from = __state, to, result = __result, current = Describe(__instance.__2__current) });
    }

    private static string Describe(Il2CppSystem.Object current)
    {
        if (current == null) return null;
        string name = current.GetIl2CppType().Name;
        var wait = current.TryCast<WaitForSeconds>();
        return wait != null ? $"{name}({wait.m_Seconds})" : name;
    }

    private static IEnumerator Watch()
    {
        while (watching)
        {
            var race = TrackManager.Instance?.TryCast<RaceTrackManager>();
            bool ready = race != null && race.readySetGo;
            if (ready != lastReady) Add(ready ? "green" : "not-ready", new { timerRunning = race?.timer?.IsRunning, laps = race?.laps });
            lastReady = ready;
            yield return null;
        }
    }

    public static void Reset(List<string> changed)
    {
        if (forceThrottle) changed.Add("race-spike-throttle");
        forceThrottle = false;
        if (savedEof != null && YieldInstructions.WaitForEndOfFrame == null)
        {
            YieldInstructions.WaitForEndOfFrame = savedEof;
            changed.Add("race-spike-eof");
        }
    }

    [HarnessCommand("race-spike-eof")]
    private static object Eof(string args)
    {
        bool on = (args ?? "").Trim() == "null";
        if (on && savedEof == null) savedEof = YieldInstructions.WaitForEndOfFrame;
        YieldInstructions.WaitForEndOfFrame = on ? null : savedEof ?? YieldInstructions.WaitForEndOfFrame;
        if (!watching)
        {
            watching = true;
            MelonCoroutines.Start(Watch());
        }
        return new { eofNull = YieldInstructions.WaitForEndOfFrame == null };
    }

    [HarnessCommand("race-spike-throttle")]
    private static object Throttle(string args)
    {
        forceThrottle = (args ?? "").Trim() == "on";
        Add(forceThrottle ? "throttle-on" : "throttle-off");
        return new { forceThrottle };
    }

    [HarnessCommand("race-spike-restart")]
    private static object Restart(string args)
    {
        var track = TrackManager.Instance ?? throw new InvalidOperationException("no TrackManager");
        Add("run-restart", new { track.SupportsRestart });
        track.RunRestart();
        return new { track.SupportsRestart };
    }

    [HarnessCommand("race-spike-events")]
    private static object Events(string args)
    {
        var race = TrackManager.Instance?.TryCast<RaceTrackManager>();
        var fader = ScreenFader.Get();
        var result = new Dictionary<string, object>
        {
            ["events"] = new List<object>(events),
            ["prepareCalls"] = prepareCalls,
            ["readySetGo"] = race?.readySetGo,
            ["isLoadingRace"] = race?.isLoadingRace,
            ["canMove"] = race?.carInput?.canMove,
            ["throttle"] = race?.carInput?.throttle,
            ["timerRunning"] = race?.timer?.IsRunning,
            ["timerMs"] = race?.timer?.ElapsedMilliseconds,
            ["laps"] = race?.laps,
            ["faderComplete"] = fader == null ? (bool?)null : fader.fadeComplete,
            ["waitForSecond"] = YieldInstructions.WaitForSecond?.m_Seconds,
            ["fps"] = 1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f),
            ["wall"] = Wall,
        };
        if ((args ?? "").Trim() == "clear") events.Clear();
        return result;
    }
}
