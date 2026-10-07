using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// sync-orders-and-jobs spike 1.1: logging-only patches on the native job flow with a fire counter per hook. Patched
// on demand, one target at a time, so a target Harmony cannot patch is reported instead of breaking the others.
public static class JobsTrace
{
    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.jobs-trace");
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    private static readonly Dictionary<string, string> failures = new Dictionary<string, string>();
    private static readonly Dictionary<string, float> lastLogged = new Dictionary<string, float>();
    private static bool patched;
    private static bool tracing;

    private static readonly (Type Type, string Method, Type[] Args)[] Targets =
    {
        (typeof(OrderGenerator), "Update", null),
        (typeof(OrderGenerator), "GenerateNewJob", null),
        (typeof(OrderGenerator), "GenerateMission", null),
        (typeof(OrderGenerator), "PrepareJob", null),
        (typeof(OrderGenerator), "CancelJob", null),
        (typeof(OrderGenerator), "Load", null),
        (typeof(OrderGenerator), "Save", null),
        (typeof(OrderGenerator._TakeJob_d__19), "MoveNext", null),
        (typeof(OrderGenerator._TakeMission_d__22), "MoveNext", null),
        (typeof(CMS.UI.Windows.OrdersWindow), "AcceptOrderAction", null),
        (typeof(CMS.UI.Windows.OrdersWindow), "DeclineOrderAction", null),
        (typeof(GameScript), "EndJob", null),
        (typeof(GameScript._EndJobCoroutine_d__139), "MoveNext", null),
        (typeof(GlobalData), "AddPlayerMoney", null),
        (typeof(GlobalData), "AddPlayerExp", null),
        (typeof(GlobalData), "AddJob", null),
        (typeof(CarLoader), "LoadCar", new[] { typeof(string) }),
        (typeof(CarLoader), "DeleteCar", Type.EmptyTypes),
        (typeof(JobHelper), "CheckJob", null),
        (typeof(Job), "StartTimer", null),
        (typeof(Job), "StopTimer", null),
        (typeof(UIManager), "ShowFullGarageInfo", null),
        (typeof(CMS.MainMenu.Windows.TutorialsWindow), "RunTutorialAction", null),
        (typeof(CMS.Platforms.Steam.SteamAchievements), "IncrementStat", null),
        (typeof(CMS.Platforms.Steam.SteamAchievements), "Unlock", new[] { typeof(string) }),
    };

    [HarnessCommand("jobs-trace")]
    private static object Command(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on":
                PatchAll();
                tracing = true;
                return Report();
            case "off":
                tracing = false;
                return Report();
            case "report":
                return Report();
            default:
                throw new ArgumentException("usage: jobs-trace on|off|report");
        }
    }

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("jobs-trace");
        tracing = false;
        counts.Clear();
        lastLogged.Clear();
    }

    private static object Report() => new Dictionary<string, object>
    {
        ["tracing"] = tracing,
        ["counts"] = Targets.Select(Name).ToDictionary(n => n, n => counts.TryGetValue(n, out int c) ? c : 0),
        ["failures"] = failures,
    };

    private static string Name((Type Type, string Method, Type[] Args) target) => $"{target.Type.Name}.{target.Method}";

    private static void PatchAll()
    {
        if (patched) return;
        patched = true;
        var prefix = new HarmonyMethod(typeof(JobsTrace).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var target in Targets)
        {
            string name = Name(target);
            try
            {
                var method = target.Args == null ? AccessTools.Method(target.Type, target.Method) : AccessTools.Method(target.Type, target.Method, target.Args);
                if (method == null) throw new MissingMethodException(name);
                harmony.Patch(method, prefix: prefix);
            }
            catch (Exception e)
            {
                failures[name] = (e.GetType().Name + ": " + e.Message.Split('\n')[0]).Replace("HarmonyException", "patch error");
                MelonLogger.Warning($"[Harness] jobs-trace cannot patch {name}: {failures[name]}");
            }
        }
    }

    private static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        if (!tracing) return;
        string name = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
        counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
        if (__originalMethod.Name == "Update")
        {
            if (lastLogged.TryGetValue(name, out float last) && Time.realtimeSinceStartup - last < 1f) return;
            lastLogged[name] = Time.realtimeSinceStartup;
        }
        string state = "";
        if (__originalMethod.Name == "MoveNext" && __instance != null)
        {
            try { state = $" state {__instance.GetType().GetProperty("__1__state")?.GetValue(__instance)}"; }
            catch (Exception) { state = " state ?"; }
        }
        string arguments = __args == null ? "" : string.Join(", ", __args.Select(Describe));
        MelonLogger.Msg($"[Harness] jobs-trace {name}({arguments}){state} #{counts[name]}");
    }

    private static string Describe(object value)
    {
        switch (value)
        {
            case null: return "null";
            case Job job: return $"Job#{job.id}:{job.carFile}";
            case CarLoader carLoader: return $"Loader:{carLoader.carToLoad}/order {carLoader.orderConnection}/customer {carLoader.customerCar}";
            default: return value.ToString();
        }
    }
}
