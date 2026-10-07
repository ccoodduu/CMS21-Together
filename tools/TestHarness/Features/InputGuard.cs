using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TogetherTestHarness.Features;

// Headless test games still receive the user's keyboard: Enter in another window pressed main-menu buttons that opened
// the CMS 2026, Discord and Steam pages, and F7/F8/F9 would trigger the mod's own hotkeys. Test games never open web
// or store pages, and headless ones read no keyboard (Rewired keyboard off, the mod's hotkeys set to None, no UI
// navigation). The harness drives the game through code, not keys.
public static class InputGuard
{
    private static readonly (string Type, string Method)[] Pages =
    {
        ("UnityEngine.Application", "OpenURL"),
        ("PlatformManager", "OpenUrl"),
        ("PlatformManager", "OpenStorePage"),
        ("Steamworks.SteamFriends", "ActivateGameOverlayToWebPage"),
        ("Steamworks.SteamFriends", "ActivateGameOverlayToStore"),
        ("Steamworks.SteamFriends", "ActivateGameOverlay"),
    };

    private static readonly string[] Hotkeys = { "ResyncHotkey", "SessionPanelHotkey", "BugReportHotkey" };

    private static PropertyInfo keyboardEnabled;
    private static object keyboard;
    private static float nextKeyboardCheck;
    private static bool hotkeysCleared;

    public static void Install(HarmonyLib.Harmony harmony)
    {
        try { Assembly.Load("com.rlabrecque.steamworks.net"); } catch (Exception) { }
        foreach (var (typeName, methodName) in Pages)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) continue;
            foreach (var method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name != methodName) continue;
                Patch(harmony, method, nameof(BeforePage));
            }
        }
    }

    private static void Patch(HarmonyLib.Harmony harmony, MethodBase method, string prefix)
    {
        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(InputGuard), prefix));
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[Harness] Could not block {method.DeclaringType?.Name}.{method.Name}: {e.Message}");
        }
    }

    private static bool BeforePage(MethodBase __originalMethod)
    {
        MelonLogger.Msg($"[Harness] Blocked {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
        return false;
    }

    public static void Update()
    {
        if (!Application.isBatchMode) return;
        var events = EventSystem.current;
        if (events != null && events.sendNavigationEvents) events.sendNavigationEvents = false;
        if (Time.realtimeSinceStartup < nextKeyboardCheck) return;
        nextKeyboardCheck = Time.realtimeSinceStartup + 2f;
        DisableRewiredKeyboard();
        ClearHotkeys();
    }

    private static void ClearHotkeys()
    {
        if (hotkeysCleared) return;
        foreach (var name in Hotkeys)
        {
            var entry = MelonPreferences.GetEntry<string>("CMS21Together", name);
            if (entry == null) return;
            entry.Value = "None";
        }
        hotkeysCleared = true;
        MelonLogger.Msg("[Harness] The mod's hotkeys are set to None in the headless game.");
    }

    private static void DisableRewiredKeyboard()
    {
        try
        {
            if (keyboard == null)
            {
                var reInput = AccessTools.TypeByName("Rewired.ReInput");
                if (reInput == null || !(bool)(AccessTools.Property(reInput, "isReady")?.GetValue(null) ?? false)) return;
                var controllers = AccessTools.Property(reInput, "controllers")?.GetValue(null);
                keyboard = controllers == null ? null : AccessTools.Property(controllers.GetType(), "Keyboard")?.GetValue(controllers);
                keyboardEnabled = keyboard == null ? null : AccessTools.Property(keyboard.GetType(), "enabled");
                if (keyboardEnabled == null) return;
                MelonLogger.Msg("[Harness] Rewired keyboard disabled in the headless game.");
            }
            if ((bool)keyboardEnabled.GetValue(keyboard)) keyboardEnabled.SetValue(keyboard, false);
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[Harness] Could not disable the Rewired keyboard: {e.Message}");
            nextKeyboardCheck = float.MaxValue;
        }
    }
}
