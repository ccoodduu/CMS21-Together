using System;
using System.Collections.Generic;
using System.Linq;
using CMS.Helpers;
using CMS.UI;
using HarmonyLib;
using MelonLoader;
using UnhollowerBaseLib;
using UnityEngine;

namespace TogetherTestHarness.Features;

[HarmonyPatch]
public static class GuardTraceCommands
{
    private static bool tracing;

    private static void Trace(string text)
    {
        if (tracing) MelonLogger.Msg($"[Harness] guard-trace {text}");
    }

    [HarnessCommand("guard-trace")]
    private static object GuardTrace(string args)
    {
        switch ((args ?? "").Trim().ToLowerInvariant())
        {
            case "on":
                tracing = true;
                return "tracing";
            case "off":
                tracing = false;
                return "not tracing";
            case "ini":
                var controller = UnityEngine.Object.FindObjectOfType<PieMenuController>();
                return controller?.PieMenuConfigFile?.text;
            case "machines":
                var menus = new Dictionary<string, string>();
                foreach (IOSpecialType type in Enum.GetValues(typeof(IOSpecialType)))
                    menus[type.ToString()] = PieMenuHelper.GetIniEntryForMachine(type, out string entry) ? entry : null;
                foreach (gameMode mode in Enum.GetValues(typeof(gameMode)))
                    menus["mode " + mode] = PieMenuHelper.GetIniEntryForSpecialType(mode);
                return menus;
            case "state":
                var manager = WindowManager.Instance;
                return new Dictionary<string, object>
                {
                    ["mode"] = GameMode.Get()?.GetCurrentMode().ToString(),
                    ["previousMode"] = GameMode.Get()?.GetPreviousMode().ToString(),
                    ["timeScale"] = Time.timeScale,
                    ["activeWindows"] = manager == null ? null
                        : Enum.GetValues(typeof(WindowID)).Cast<WindowID>().Where(id => manager.IsWindowActive(id)).Select(id => id.ToString()).ToList(),
                };
            default:
                throw new ArgumentException("usage: guard-trace on|off|ini|machines|state");
        }
    }

    [HarmonyPatch(typeof(WindowManager), nameof(WindowManager.Show), typeof(WindowID), typeof(bool))]
    [HarmonyPostfix]
    private static void Show(WindowID windowID, bool __result, bool __runOriginal) => Trace($"Show({windowID}) -> {__result} ran={__runOriginal}");

    [HarmonyPatch(typeof(WindowManager), nameof(WindowManager.Show), typeof(WindowID), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
    [HarmonyPostfix]
    private static void ShowArgs(WindowID windowID, bool __result, bool __runOriginal) => Trace($"Show({windowID}, args) -> {__result} ran={__runOriginal}");

    [HarmonyPatch(typeof(WindowManager), nameof(WindowManager.ShowAfterFrame))]
    [HarmonyPrefix]
    private static void ShowAfterFrame(WindowID windowID) => Trace($"ShowAfterFrame({windowID})");

    [HarmonyPatch(typeof(WindowManager), nameof(WindowManager.ShowAfterWindowClose))]
    [HarmonyPrefix]
    private static void ShowAfterWindowClose(WindowID windowID, WindowID windowToWaitForClosing) => Trace($"ShowAfterWindowClose({windowID} after {windowToWaitForClosing})");

    [HarmonyPatch(typeof(WindowManager), nameof(WindowManager.SetWindowAsActive))]
    [HarmonyPrefix]
    private static void SetWindowAsActive(WindowID windowID, bool active) => Trace($"SetWindowAsActive({windowID}, {active})");

    [HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.PrepareIcons))]
    [HarmonyPostfix]
    private static void PrepareIcons(Il2CppStringArray iconsToLoad) => Trace($"PrepareIcons({string.Join(",", iconsToLoad?.ToArray() ?? new string[0])})");

    [HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.GetOnClick))]
    [HarmonyPrefix]
    private static void GetOnClick(string id) => Trace($"GetOnClick({id})");

    [HarmonyPatch(typeof(PieMenuHelper), nameof(PieMenuHelper.GetIniEntryForMachine))]
    [HarmonyPostfix]
    private static void GetIniEntryForMachine(IOSpecialType machine, string iniEntryForSpecialType) => Trace($"GetIniEntryForMachine({machine}) -> {iniEntryForSpecialType}");

    [HarmonyPatch(typeof(GameMode), nameof(GameMode.SetCurrentMode))]
    [HarmonyPostfix]
    private static void SetCurrentMode(GameMode __instance, gameMode newGameMode, bool __runOriginal) =>
        Trace($"SetCurrentMode({newGameMode}) ran={__runOriginal} now={__instance.currentMode}");
}
