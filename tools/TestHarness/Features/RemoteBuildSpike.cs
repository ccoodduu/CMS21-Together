using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CMS21Together.Logic.Driving;
using CMS21Together.Logic.Player;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// faster-remote-cars spike. remote-build wait game|none|settle <s>: replaces the observer build's 3 s settle (none skips
// the wait for the own track car too). trace on|off|report: one event per observer build step (BuildSteps.MoveNext,
// its StopPhysics share) and per helper, with frame and real clock. park <playerId>: hides and shows a built copy and
// times both. count <playerId>: the copy's component counts.
public static class RemoteBuildSpike
{
    private const int EventLimit = 4000;
    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.remote-build");
    private static readonly List<Dictionary<string, object>> events = new List<Dictionary<string, object>>();
    private static readonly Stack<long> started = new Stack<long>();
    private static FieldInfo readySinceField;
    private static string waitMode = "game";
    private static float settleSeconds = RemoteCars.LocalCarSettleSeconds;
    private static bool tracing;
    private static bool patched;
    private static double stopPhysicsMs;
    private static int stopPhysicsCalls;

    [HarnessCommand("remote-build")]
    private static object Command(string args)
    {
        var parts = (args ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts.Length > 0 ? parts[0] : "report";
        PatchOnce();
        switch (verb)
        {
            case "wait":
                waitMode = parts.Length > 1 ? parts[1] : "game";
                if (waitMode == "settle") settleSeconds = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                else if (waitMode != "game" && waitMode != "none") throw new ArgumentException("usage: remote-build wait game|none|settle <s>");
                return new { waitMode, settleSeconds };
            case "trace":
                string mode = parts.Length > 1 ? parts[1] : "report";
                if (mode == "on") { events.Clear(); tracing = true; }
                else if (mode == "off") tracing = false;
                else if (mode == "clear") events.Clear();
                return new Dictionary<string, object> { ["tracing"] = tracing, ["waitMode"] = waitMode, ["settleSeconds"] = settleSeconds, ["clock"] = Clock(), ["events"] = events.ToList() };
            case "park": return Park(int.Parse(parts[1]));
            case "count": return Count(int.Parse(parts[1]));
            case "clock": return Clock();
            case "ghost": return Ghost(int.Parse(parts[1]), parts.Length > 2 ? parts[2] : null);
            case "state":
                var car = RemoteCars.Of(int.Parse(parts[1]));
                if (car == null) return new { exists = false };
                return new
                {
                    exists = true, playerId = car.PlayerId, mode = car.Mode, visible = car.Ready && car.Root.gameObject.activeInHierarchy,
                    shownAfter = car.ShownAfter, shownAt = car.ShownAt, waitSeconds = car.WaitSeconds, readyAt = RemoteCars.SceneReadyAt, buildSeconds = car.BuildSeconds, longestFrame = car.LongestFrame,
                };
            case "remove":
                RemoteCars.Remove(int.Parse(parts[1]), "harness");
                return Clock();
            default: throw new ArgumentException("usage: remote-build wait|trace|park|count|clock");
        }
    }

    internal static Dictionary<string, object> Clock() => new Dictionary<string, object>
    {
        ["realtime"] = Math.Round(Time.realtimeSinceStartup, 4),
        ["wallMs"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
        ["frame"] = Time.frameCount,
    };

    private static void PatchOnce()
    {
        if (patched) return;
        patched = true;
        var type = typeof(RemoteCars);
        readySinceField = AccessTools.Field(type, "localReadySince");
        harmony.Patch(AccessTools.Method(type, "LocalCarReady"), postfix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(ReadyPostfix)));
        var steps = type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).First(t => t.Name.Contains("BuildSteps"));
        harmony.Patch(AccessTools.Method(steps, "MoveNext"), prefix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(StepPrefix)), postfix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(StepPostfix)));
        harmony.Patch(AccessTools.Method(type, "StopPhysics"), prefix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(TimePrefix)), postfix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(StopPhysicsPostfix)));
        foreach (string name in new[] { "CreateLoader", "MakeInert", "FindWheels", "FindBody", "Destroy", "Spawn" })
            harmony.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(TimePrefix)), postfix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(HelperPostfix)));
        harmony.Patch(AccessTools.Method(typeof(RemoteEngines), "Create"), prefix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(TimePrefix)), postfix: new HarmonyMethod(typeof(RemoteBuildSpike), nameof(HelperPostfix)));
        MelonLogger.Msg("[Harness] remote-build patched RemoteCars.");
    }

    private static void ReadyPostfix(ref bool __result)
    {
        if (waitMode == "game") return;
        if (waitMode == "none") { __result = true; return; }
        if (__result) return;
        float since = (float)readySinceField.GetValue(null);
        if (since >= 0f && Time.realtimeSinceStartup - since >= settleSeconds) __result = true;
    }

    private static void TimePrefix() => started.Push(Stopwatch.GetTimestamp());

    private static double Pop() => started.Count == 0 ? 0 : (Stopwatch.GetTimestamp() - started.Pop()) * 1000.0 / Stopwatch.Frequency;

    private static void StepPrefix()
    {
        stopPhysicsMs = 0;
        stopPhysicsCalls = 0;
        started.Push(Stopwatch.GetTimestamp());
    }

    private static void StepPostfix(bool __result)
    {
        double ms = Pop();
        Add("step", ms, new Dictionary<string, object> { ["more"] = __result, ["stopPhysicsMs"] = Math.Round(stopPhysicsMs, 2), ["stopPhysicsCalls"] = stopPhysicsCalls });
    }

    private static void StopPhysicsPostfix()
    {
        stopPhysicsMs += Pop();
        stopPhysicsCalls++;
    }

    private static void HelperPostfix(MethodBase __originalMethod) => Add(__originalMethod.Name, Pop(), null);

    private static void Add(string what, double ms, Dictionary<string, object> extra)
    {
        if (!tracing || events.Count >= EventLimit) return;
        var e = new Dictionary<string, object>
        {
            ["what"] = what,
            ["ms"] = Math.Round(ms, 2),
            ["end"] = Math.Round(Time.realtimeSinceStartup, 4),
            ["frame"] = Time.frameCount,
        };
        if (extra != null) foreach (var pair in extra) e[pair.Key] = pair.Value;
        events.Add(e);
    }

    private static int ghosts;

    private static object Ghost(int fromPlayerId, string route)
    {
        var source = RemoteCars.Of(fromPlayerId) ?? throw new ArgumentException($"no observer car of player {fromPlayerId}");
        if (!source.Ready) throw new InvalidOperationException("source not built");
        int id = 9100 + ghosts++;
        var start = source.Start;
        var packet = new CMS21_Together_Core.Network.Packets.CarDriveStartPacket
        {
            PlayerId = id, DriveId = id, Scene = start.Scene, CarToLoad = start.CarToLoad, CarLoaderID = -1,
            CarBlob = route == "base" ? null : start.CarBlob, CarBlobVersion = start.CarBlobVersion,
        };
        RemoteCars.Spawn(packet, route == "base" ? "base" : null);
        var at = source.Root.position + source.Root.right * (5f * ghosts);
        RemoteCars.Push(id, new CMS21_Together_Core.Network.Packets.DriveState
        {
            Time = Time.time, PosX = at.x, PosY = at.y, PosZ = at.z, RotW = 1f, Rpm = 900f,
        });
        return new { playerId = id, route = route ?? "clone", bytes = packet.CarBlob?.Length ?? 0 };
    }

    private static object Park(int playerId)
    {
        var car = RemoteCars.Of(playerId) ?? throw new ArgumentException($"no observer car of player {playerId}");
        if (!car.Ready) throw new InvalidOperationException("not built");
        var watch = Stopwatch.StartNew();
        car.Root.gameObject.SetActive(false);
        double hideMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        car.Root.gameObject.SetActive(true);
        double showMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var parked = car.Holder.transform.position;
        car.Holder.transform.position = parked + Vector3.down * 500f;
        car.Holder.transform.position = parked;
        double moveMs = watch.Elapsed.TotalMilliseconds;
        return new { hideMs = Math.Round(hideMs, 2), showMs = Math.Round(showMs, 2), moveMs = Math.Round(moveMs, 2), frame = Time.frameCount };
    }

    private static object Count(int playerId)
    {
        var car = RemoteCars.Of(playerId) ?? throw new ArgumentException($"no observer car of player {playerId}");
        if (!car.Ready) throw new InvalidOperationException("not built");
        var holder = car.Holder;
        var renderers = holder.GetComponentsInChildren<Renderer>(true);
        return new
        {
            transforms = holder.GetComponentsInChildren<Transform>(true).Length,
            renderers = renderers.Length,
            renderersOn = renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy),
            colliders = holder.GetComponentsInChildren<Collider>(true).Length,
            rigidbodies = holder.GetComponentsInChildren<Rigidbody>(true).Length,
            partScripts = holder.GetComponentsInChildren<PartScript>(true).Length,
            carParts = car.Loader.carParts?.Count ?? -1,
        };
    }
}
