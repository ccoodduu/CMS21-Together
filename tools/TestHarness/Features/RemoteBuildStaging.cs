using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// faster-remote-cars spike, prototype of design D2. remote-stage on|off|report: for observer copies (loaders named
// RemoteCar[...]) the game's LoadCar stage calls after CreateInterior are recorded and skipped in LoadCar's one frame,
// LoadCar is held at its next state, and the calls are replayed in the game's order over the following frames, split
// where the game's own (disabled) asyncLoading path yields. report: per replayed group its calls and milliseconds.
public static class RemoteBuildStaging
{
    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.remote-stage");
    private static readonly string[] Deferred =
    {
        "CreateEngine", "SetEngine", "CreateDriveshaft", "SetDriveshaft", "CreateExterior", "SetExterior", "CreateParts",
        "CreateBonusParts", "SetLicensePlateNumber", "SetParts", "SetBlockedBy", "SetUnmountWithCarParts",
    };
    private static readonly HashSet<string> GroupStarts = new HashSet<string> { "CreateEngine", "CreateDriveshaft", "CreateParts", "CreateBonusParts" };

    private class Call
    {
        public MethodBase Method;
        public object[] Args;
    }

    private static readonly Dictionary<IntPtr, Queue<Call>> pending = new Dictionary<IntPtr, Queue<Call>>();
    private static readonly Dictionary<IntPtr, CarLoader> loaders = new Dictionary<IntPtr, CarLoader>();
    private static readonly List<Dictionary<string, object>> groups = new List<Dictionary<string, object>>();
    private static readonly List<string> errors = new List<string>();
    private static bool enabled;
    private static bool patched;
    private static bool replaying;
    private static PropertyInfo loadCarThis;
    private static int held;
    private static bool inLoadCar;

    [HarnessCommand("remote-stage")]
    private static object Command(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on": PatchOnce(); enabled = true; groups.Clear(); errors.Clear(); held = 0; break;
            case "off": enabled = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: remote-stage on|off|report");
        }
        return new Dictionary<string, object> { ["enabled"] = enabled, ["pending"] = pending.Count, ["heldFrames"] = held, ["errors"] = errors.ToList(), ["groups"] = groups.ToList() };
    }

    private static void PatchOnce()
    {
        if (patched) return;
        patched = true;
        foreach (string name in Deferred)
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(CarLoader)).Where(m => m.Name == name))
        {
            string prefix = method.GetParameters().Length switch
            {
                0 => nameof(Prefix0),
                1 when method.GetParameters()[0].ParameterType == typeof(string) => nameof(PrefixString),
                1 => nameof(PrefixBool),
                _ => nameof(PrefixBoolBool),
            };
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(RemoteBuildStaging), prefix));
        }
        var loadCar = typeof(CarLoader).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).First(t => t.Name.Contains("_LoadCar_d__"));
        loadCarThis = AccessTools.Property(loadCar, "__4__this");
        harmony.Patch(AccessTools.Method(loadCar, "MoveNext"), prefix: new HarmonyMethod(typeof(RemoteBuildStaging), nameof(HoldLoadCar)), postfix: new HarmonyMethod(typeof(RemoteBuildStaging), nameof(LoadCarDone)));
        MelonLogger.Msg("[Harness] remote-stage patched CarLoader.");
    }

    private static bool IsRemote(CarLoader loader)
    {
        try { return loader != null && loader.gameObject.name.StartsWith("RemoteCar["); }
        catch (Exception) { return false; }
    }

    private static bool Defer(CarLoader loader, MethodBase method, object[] args)
    {
        if (!enabled || replaying || !inLoadCar || !IsRemote(loader)) return true;
        var key = loader.Pointer;
        if (!pending.TryGetValue(key, out var queue)) pending[key] = queue = new Queue<Call>();
        loaders[key] = loader;
        queue.Enqueue(new Call { Method = method, Args = args });
        return false;
    }

    private static bool Prefix0(CarLoader __instance, MethodBase __originalMethod) => Defer(__instance, __originalMethod, new object[0]);
    private static bool PrefixString(CarLoader __instance, MethodBase __originalMethod, string __0) => Defer(__instance, __originalMethod, new object[] { __0 });
    private static bool PrefixBool(CarLoader __instance, MethodBase __originalMethod, bool __0) => Defer(__instance, __originalMethod, new object[] { __0 });
    private static bool PrefixBoolBool(CarLoader __instance, MethodBase __originalMethod, bool __0, bool __1) => Defer(__instance, __originalMethod, new object[] { __0, __1 });

    private static bool HoldLoadCar(object __instance, ref bool __result)
    {
        inLoadCar = true;
        if (!enabled || pending.Count == 0) return true;
        var loader = loadCarThis.GetValue(__instance) as CarLoader;
        if (loader == null || !pending.ContainsKey(loader.Pointer)) return true;
        inLoadCar = false;
        held++;
        __result = true;
        return false;
    }

    private static void LoadCarDone() => inLoadCar = false;

    internal static void Update()
    {
        if (pending.Count == 0) return;
        foreach (var key in pending.Keys.ToList())
        {
            var queue = pending[key];
            var loader = loaders[key];
            var names = new List<string>();
            var watch = Stopwatch.StartNew();
            replaying = true;
            try
            {
                while (queue.Count > 0)
                {
                    var call = queue.Peek();
                    if (names.Count > 0 && GroupStarts.Contains(call.Method.Name)) break;
                    queue.Dequeue();
                    names.Add(call.Method.Name);
                    if (loader == null || !loader) continue;
                    try { call.Method.Invoke(loader, call.Args); }
                    catch (Exception e) { errors.Add($"{call.Method.Name}: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}"); }
                }
            }
            finally
            {
                replaying = false;
            }
            groups.Add(new Dictionary<string, object>
            {
                ["calls"] = string.Join("+", names), ["ms"] = Math.Round(watch.Elapsed.TotalMilliseconds, 2),
                ["end"] = Math.Round(Time.realtimeSinceStartup, 4), ["frame"] = Time.frameCount,
            });
            if (queue.Count == 0)
            {
                pending.Remove(key);
                loaders.Remove(key);
            }
        }
    }
}
