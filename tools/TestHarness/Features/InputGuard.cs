using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TogetherTestHarness.Features;

// Headless test games still receive the user's keyboard: Enter submitted the selected main-menu button, which opened
// the CMS 2026 web page. Test games never open URLs, and headless ones get no UI navigation (submit, cancel, move).
public static class InputGuard
{
    public static void Install(HarmonyLib.Harmony harmony)
    {
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Application), nameof(Application.OpenURL), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(InputGuard), nameof(BeforeOpenUrl)));
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[Harness] Could not block Application.OpenURL: {e.Message}");
        }
    }

    private static bool BeforeOpenUrl(string url)
    {
        MelonLogger.Msg($"[Harness] Blocked Application.OpenURL({url}).");
        return false;
    }

    public static void Update()
    {
        if (!Application.isBatchMode) return;
        var events = EventSystem.current;
        if (events != null && events.sendNavigationEvents) events.sendNavigationEvents = false;
    }
}
