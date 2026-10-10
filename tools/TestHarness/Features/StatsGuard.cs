using System;
using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace TogetherTestHarness.Features;

// Test games run on the user's Steam account; their junkyard trips and tire balancing unlocked real achievements.
// Steamworks.SteamUserStats lives in the game's com.rlabrecque.steamworks.net, which the harness does not reference.
public static class StatsGuard
{
    private static readonly string[] Blocked = { "SetAchievement", "SetStat", "StoreStats", "IndicateAchievementProgress", "UpdateAvgRateStat" };
    private static readonly System.Collections.Generic.HashSet<string> blockedNames = new System.Collections.Generic.HashSet<string>();

    public static bool Active => blockedNames.Contains("SetStat") && blockedNames.Contains("StoreStats") && blockedNames.Contains("SetAchievement");

    public static void Install(HarmonyLib.Harmony harmony)
    {
        try { System.Reflection.Assembly.Load("com.rlabrecque.steamworks.net"); } catch (Exception) { }
        var type = AccessTools.TypeByName("Steamworks.SteamUserStats");
        if (type == null)
        {
            MelonLogger.Warning("[Harness] Steamworks.SteamUserStats not found; achievements are not blocked.");
            return;
        }
        int patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m => Blocked.Contains(m.Name) && m.ReturnType == typeof(bool)))
        {
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(StatsGuard), nameof(Skip)));
                patched++;
                blockedNames.Add(method.Name);
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Harness] Could not block SteamUserStats.{method.Name}: {e.Message}");
            }
        }
        MelonLogger.Msg($"[Harness] Steam stats and achievements blocked ({patched} methods).");
    }

    private static bool Skip(ref bool __result)
    {
        __result = true;
        return false;
    }
}
