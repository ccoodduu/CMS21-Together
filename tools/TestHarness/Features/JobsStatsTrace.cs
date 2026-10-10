using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace TogetherTestHarness.Features;

// shared-job-achievements 3.2: counts every stat call where it ends, SteamAchievements.IncrementStat (the game's
// virtual calls and PlatformManager.IncrementStat), so an outer call never counts twice. The four job stats are always
// in the report. StatsGuard still keeps the Steam writes out.
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

    // "zero <id>..." clears the game's in-memory values only, so a check the user's own Steam account already passed
    // runs again; StatsGuard must be blocking the writes.
    [HarnessCommand("stats-trace")]
    private static object Command(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts.Length > 0 ? parts[0] : "";
        var ids = parts.Skip(1).ToArray();
        switch (verb)
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
            case "value":
                break;
            case "zero":
                if (!StatsGuard.Active) throw new InvalidOperationException("StatsGuard is not blocking the Steam writes; refusing to touch stat values");
                if (ids.Length == 0) throw new ArgumentException("usage: stats-trace zero <stat id>...");
                var achievements = Singleton<GameManager>.Instance.PlatformManager.platform.AchievementSystem;
                foreach (string id in ids) achievements.ClearStat(id);
                break;
            default:
                throw new ArgumentException("usage: stats-trace on|off|report|value <id>...|zero <id>...");
        }
        var platform = Singleton<GameManager>.Instance?.PlatformManager;
        return new Dictionary<string, object>
        {
            ["tracing"] = tracing,
            ["patched"] = patched && patchError == null,
            ["error"] = patchError,
            ["guard"] = StatsGuard.Active,
            ["counts"] = JobStats.Concat(counts.Keys).Distinct().ToDictionary(id => id, id => counts.TryGetValue(id, out int c) ? c : 0),
            ["values"] = platform == null ? null : ids.ToDictionary(id => id, id => platform.GetStatValue(id)),
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
        if (!tracing || statID == null) return;
        counts[statID] = (counts.TryGetValue(statID, out int c) ? c : 0) + amount;
        MelonLogger.Msg($"[Harness] stats-trace {statID} {amount:+0;-0;0} (now {counts[statID]})");
    }
}
