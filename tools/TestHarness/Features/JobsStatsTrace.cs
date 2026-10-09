using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace TogetherTestHarness.Features;

// shared-job-achievements 3.2: counts the job stats where every stat call ends, SteamAchievements.IncrementStat (the
// three virtual calls of EndJobCoroutine and PlatformManager.IncrementStat), so an outer call never counts twice.
// stat_level and the other stats are not counted. StatsGuard still keeps the Steam writes out.
public static class JobsStatsTrace
{
    private static readonly string[] JobStats = { "stat_finish_order", "stat_bonus_exp", "stat_bonus_money", "stat_finish_allmissions" };
    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.stats-trace");
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    private static bool patched;
    private static string patchError;
    private static bool tracing;

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("stats-trace");
        tracing = false;
        counts.Clear();
    }

    [HarnessCommand("stats-trace")]
    private static object Command(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on":
                Patch();
                counts.Clear();
                tracing = true;
                break;
            case "off":
                tracing = false;
                break;
            case "report":
                break;
            default:
                throw new ArgumentException("usage: stats-trace on|off|report");
        }
        return new Dictionary<string, object>
        {
            ["tracing"] = tracing,
            ["patched"] = patched && patchError == null,
            ["error"] = patchError,
            ["counts"] = JobStats.ToDictionary(id => id, id => counts.TryGetValue(id, out int c) ? c : 0),
        };
    }

    private static void Patch()
    {
        if (patched) return;
        patched = true;
        try
        {
            var method = AccessTools.Method(typeof(CMS.Platforms.Steam.SteamAchievements), "IncrementStat") ?? throw new MissingMethodException("SteamAchievements.IncrementStat");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(JobsStatsTrace), nameof(Prefix)));
        }
        catch (Exception e)
        {
            patchError = e.GetType().Name + ": " + e.Message.Split('\n')[0];
            MelonLogger.Warning($"[Harness] stats-trace cannot patch SteamAchievements.IncrementStat: {patchError}");
        }
    }

    private static void Prefix(string statID, int amount)
    {
        if (!tracing || !JobStats.Contains(statID)) return;
        counts[statID] = (counts.TryGetValue(statID, out int c) ? c : 0) + amount;
        MelonLogger.Msg($"[Harness] stats-trace {statID} +{amount} (now {counts[statID]})");
    }
}
