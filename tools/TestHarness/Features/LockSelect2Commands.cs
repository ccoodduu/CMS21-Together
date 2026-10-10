using System;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21Together.Guard;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;
using UnityEngine;

namespace TogetherTestHarness.Features;

// part-locks-2 verbs: the state behind the mount-mode previews, the item chooser rows and the car pie options, read
// from the game objects because headless games draw no pixels.
[HarmonyPatch]
public static class LockSelect2Commands
{
    private static CarLoader pinned;

    internal static void Reset(List<string> changed)
    {
        if (pinned != null) changed.Add("lock-pie (car pinned)");
        pinned = null;
    }

    // The pie reads the car under the cursor when it prepares its icons, a moment after it opens; the harness has no
    // cursor, so the car stays pinned until "lock-pie close".
    [HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.PrepareIcons))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void PinCar()
    {
        if (pinned != null) GameScript.Get().IOMouseOverCarLoader = pinned;
    }

    // Without the pie key held, the game's own pie input sometimes accepts the option under its idle cursor
    // (HandleInput -> NotificationCenter.ButtonAccept) and moves the car while the harness reads the options.
    [HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.HandleInput))]
    [HarmonyPrefix]
    private static bool HoldPieInput() => pinned == null;

    private const int PartsDisabledLayer = 28;
    private const int HiddenPreviewLayer = 26;
    private const string CarPieEntry = "!ChangeCarPosition";

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static CarLoader Loader(string index) =>
        CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(index)) ?? throw new ArgumentException($"no car loader {index}");

    // Headless test games keep every part on layer 28 (PartsDisabled), which GameScript.PrepareItemsToMount skips; "arm"
    // puts the car's unmounted parts on the layer a player's game leaves them on outside mount mode.
    [HarnessCommand("lock-preview")]
    private static object Preview(string args)
    {
        var parts = Args(args);
        if (parts.Length < 2) throw new ArgumentException("usage: lock-preview <loader> arm | <loader> <key...>");
        var carLoader = Loader(parts[0]);
        var registry = PartRegistry.Build(carLoader);
        if (parts[1] == "arm")
        {
            var armed = new List<string>();
            foreach (string key in registry.SubKeys)
            {
                var script = registry.Sub(key);
                if (script == null || !script.IsUnmounted || script.gameObject.layer != PartsDisabledLayer) continue;
                script.SetLayerRecursively(HiddenPreviewLayer);
                armed.Add(key);
            }
            return new { armed = armed.Count };
        }
        return parts.Skip(1).Select(key =>
        {
            var script = registry.Sub(key) ?? throw new ArgumentException($"no part {key}");
            var colliders = script.gameObject.GetComponents<Collider>();
            bool collider = colliders != null && colliders.Any(c => c != null && c.enabled);
            int layer = script.gameObject.layer;
            return (object)new Dictionary<string, object>
            {
                ["key"] = key, ["unmounted"] = script.IsUnmounted, ["layer"] = layer, ["collider"] = collider,
                ["shown"] = script.IsUnmounted && layer == LockPreviews.PartLayer && collider,
                ["message"] = LockSelection.BlockedMessage(script),
                ["mode"] = GameMode.Get()?.currentMode.ToString(),
            };
        }).ToList();
    }

    [HarnessCommand("lock-chooser-rows")]
    private static object ChooserRows(string args)
    {
        bool open = WindowManager.Instance != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp);
        return new Dictionary<string, object>
        {
            ["open"] = open,
            ["mode"] = GameMode.Get()?.currentMode.ToString(),
            ["rows"] = open ? LockChooser.Rows().Select(r => (object)new { uid = r.Uid, locked = r.Locked, text = r.Text }).ToList() : new List<object>(),
        };
    }

    // The chooser's accept for one item (NotificationCenter.NewButtonAccept "SelectItem"): the window hides, then
    // GameScript.SelectPartToMount runs in the same frame.
    [HarnessCommand("lock-chooser-pick")]
    private static object ChooserPick(string args)
    {
        var parts = Args(args);
        if (parts.Length != 1) throw new ArgumentException("usage: lock-chooser-pick <uid>");
        long uid = long.Parse(parts[0]);
        var item = Singleton<GameManager>.Instance.Inventory.GetItem(uid) ?? throw new ArgumentException($"no item {uid}");
        WindowManager.Instance.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp).Hide(false);
        GameScript.Get().SelectPartToMount(item);
        return new { picked = uid, mode = GameMode.Get()?.currentMode.ToString() };
    }

    [HarnessCommand("lock-pie")]
    private static object Pie(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2 || (parts[1] != "open" && parts[1] != "state" && parts[1] != "close"))
            throw new ArgumentException("usage: lock-pie <loader> open|state|close");
        var controller = UnityEngine.Object.FindObjectOfType<PieMenuController>() ?? throw new InvalidOperationException("no PieMenuController");
        if (parts[1] == "open")
        {
            pinned = Loader(parts[0]);
            GameScript.Get().IOMouseOverCarLoader = pinned;
            WindowManager.Instance.Show(WindowID.PieMenu, false);
            controller.PreparePieMenuWithFader(controller.ReadOptionsFromIni(CarPieEntry));
            return new { opened = CarPieEntry };
        }
        if (parts[1] == "close")
        {
            pinned = null;
            WindowManager.Instance.Hide(WindowID.PieMenu, false);
            return new { closed = true };
        }
        var names = controller.NameList;
        var elements = controller.LayoutElements;
        var options = new List<object>();
        for (int i = 0; names != null && i < names.Length; i++)
        {
            string id = names[i];
            if (string.IsNullOrEmpty(id)) continue;
            bool known = controller.options != null && controller.options.ContainsKey(id);
            PieOptionState.Blocks.TryGetValue(id, out var block);
            options.Add(new Dictionary<string, object>
            {
                ["id"] = id,
                ["enabled"] = known && controller.options[id].Enabled,
                ["available"] = elements != null && i < elements.Length && elements[i].IsAvailable,
                ["source"] = block.Source?.Name,
                ["reason"] = block.Reason,
            });
        }
        return new { open = controller.IsPieMenuOpen, options };
    }
}
