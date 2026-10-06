using System;
using System.Collections.Generic;
using System.Linq;
using CMS.Helpers;
using CMS.UI;
using CMS21Together.Guard;
using UnhollowerBaseLib;
using UnityEngine;

namespace TogetherTestHarness.Features;

public static class GuardCommands
{
    [HarnessCommand("guard-set")]
    private static object GuardSet(string args)
    {
        if (!Enum.TryParse((args ?? "").Trim(), true, out GuardMode mode))
            throw new ArgumentException("usage: guard-set <enforce|logonly|off>");
        FeatureGuard.ModeOverride = mode;
        return $"guard mode {mode}";
    }

    [HarnessCommand("guard-allow")]
    private static object GuardAllow(string args)
    {
        var (kind, id) = ParseKey(args);
        FeatureGuard.AllowAtRuntime(FeatureGuard.Key(kind, id));
        return $"allowed {FeatureGuard.Key(kind, id)}";
    }

    [HarnessCommand("guard-log")]
    private static object GuardLog(string args) => new Dictionary<string, object>
    {
        ["mode"] = FeatureGuard.Mode.ToString(),
        ["active"] = FeatureGuard.IsSessionActive,
        ["blocks"] = FeatureGuard.Blocks.Select(block => block.ToString()).ToList(),
        ["keys"] = FeatureGuard.Blocks.Select(block => block.Key).Distinct().ToList(),
        ["lastMessage"] = GuardNotice.LastText,
    };

    [HarnessCommand("guard-rules")]
    private static object GuardRulesTable(string args) =>
        GuardRules.All.Select(rule => $"{(rule.Allowed ? "allow" : "deny ")} {FeatureGuard.Key(rule.Kind, rule.Id),-36} {rule.Owner,-22} {rule.Label}").ToList();

    [HarnessCommand("guard-try")]
    private static object GuardTry(string args)
    {
        var parts = (args ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("usage: guard-try <Kind:Id> [keep|void]");
        string option = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
        if (parts[0].StartsWith("PieMenu:", StringComparison.OrdinalIgnoreCase)) return OpenMachineMenu(parts[0].Substring("PieMenu:".Length));

        var (kind, id) = ParseKey(parts[0]);
        string key = FeatureGuard.Key(kind, id);
        GuardNotice.Forget();
        var result = new Dictionary<string, object> { ["key"] = key };
        switch (kind)
        {
            case GuardKind.Window:
                TryWindow(id, option == "keep", result);
                break;
            case GuardKind.Mode:
                TryMode(id, result);
                break;
            case GuardKind.Scene:
                TryScene(id, option == "void", result);
                break;
            case GuardKind.Pie:
                TryPie(id, result);
                break;
        }

        var last = FeatureGuard.LastDecision;
        result["result"] = last == null || last.Value.Key != key ? "not reached"
            : last.Value.Decision == GuardDecision.Allow ? "allowed" : "blocked";
        result["message"] = GuardNotice.LastText;
        result["gameMode"] = GameMode.Get()?.GetCurrentMode().ToString();
        return result;
    }

    private static void TryWindow(string id, bool keep, Dictionary<string, object> result)
    {
        var window = (WindowID)Enum.Parse(typeof(WindowID), id, true);
        var manager = WindowManager.Instance;
        bool shown = manager.Show(window, false);
        result["shown"] = shown;
        result["windowActive"] = manager.IsWindowActive(window);
        if (shown && !keep) manager.Hide(window, true);
    }

    private static void TryMode(string id, Dictionary<string, object> result)
    {
        var mode = (gameMode)Enum.Parse(typeof(gameMode), id, true);
        var gameMode = GameMode.Get();
        result["modeBefore"] = gameMode.GetCurrentMode().ToString();
        gameMode.SetCurrentMode(mode);
    }

    private static void TryScene(string id, bool viaVoid, Dictionary<string, object> result)
    {
        var type = (SceneType)Enum.Parse(typeof(SceneType), id, true);
        string sceneName = type == SceneType.Garage ? "garage" : type.ToString();
        var center = NotificationCenter.m_instance;
        if (viaVoid)
            center.StartSelectSceneToLoad(sceneName, type, true, false);
        else
            center.StartCoroutine(center.SelectSceneToLoad(sceneName, type, true, false));
        result["path"] = viaVoid ? "StartSelectSceneToLoad" : "SelectSceneToLoad coroutine";
    }

    private static void TryPie(string id, Dictionary<string, object> result)
    {
        var controller = UnityEngine.Object.FindObjectOfType<PieMenuController>();
        if (controller == null) throw new InvalidOperationException("no PieMenuController in this scene");
        result["known"] = controller.options != null && controller.options.ContainsKey(id);
        var names = controller.NameList;
        int current = controller.CurrOption;
        var single = new Il2CppStringArray(1);
        single[0] = id;
        controller.NameList = single;
        controller.CurrOption = 0;
        try
        {
            controller.CheckSelectedOption();
        }
        finally
        {
            controller.NameList = names;
            controller.CurrOption = current;
        }
    }

    private static object OpenMachineMenu(string machine)
    {
        var type = (IOSpecialType)Enum.Parse(typeof(IOSpecialType), machine.Trim(), true);
        var controller = UnityEngine.Object.FindObjectOfType<PieMenuController>();
        if (controller == null) throw new InvalidOperationException("no PieMenuController in this scene");
        if (!PieMenuHelper.GetIniEntryForMachine(type, out string entry)) throw new ArgumentException($"no pie menu for {type}");
        var options = controller.ReadOptionsFromIni(entry);
        WindowManager.Instance.Show(WindowID.PieMenu, false);
        controller.PreparePieMenuWithFader(options);
        return new Dictionary<string, object>
        {
            ["entry"] = entry,
            ["options"] = options.Select(id => $"{id} enabled={controller.options.ContainsKey(id) && controller.options[id].Enabled}").ToList(),
        };
    }

    private static (GuardKind, string) ParseKey(string args)
    {
        var text = (args ?? "").Trim();
        int colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1 || !Enum.TryParse(text.Substring(0, colon), true, out GuardKind kind))
            throw new ArgumentException("expected Kind:Id with Kind = Window, Pie, Mode or Scene");
        return (kind, text.Substring(colon + 1));
    }
}
