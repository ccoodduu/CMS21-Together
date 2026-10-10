using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace TogetherTestHarness.Features;

// sync-tuning-bonus-and-new-engines group 3: bonus slots read from CarLoader.bonusParts, the fit through the game's
// SelectPartToMount with the slot under the mouse (mode BonusAssemble) and the remove through ClickIO (BonusDisassemble).
[HarmonyPatch]
public static class BonusCommands
{
    private static bool fitting;

    [HarmonyPatch(typeof(global::Inventory), nameof(global::Inventory.Delete), new[] { typeof(Item) })]
    [HarmonyPrefix]
    private static void TraceDelete(Item item)
    {
        if (fitting) MelonLoader.MelonLogger.Msg($"[Bonus] Inventory.Delete {item?.ID} {item?.UID} during a bonus fit");
    }

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static CarLoader Loaded(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        return carLoader;
    }

    private static BonusPart Slot(CarLoader carLoader, string index)
    {
        var list = carLoader.GetBonusParts();
        int slot = int.Parse(index);
        if (list == null || slot < 0 || slot >= list.Count) throw new ArgumentException($"no bonus slot {slot}");
        return list[slot];
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static object Describe(BonusPart part, int index) => new Dictionary<string, object>
    {
        ["index"] = index, ["id"] = part.ID, ["uid"] = part.UID, ["type"] = part.Type.ToString(), ["unmounted"] = part.IsUnmounted,
        ["dummy"] = part.IsDummy(), ["painted"] = part.IsPainted, ["paintType"] = part.PaintType.ToString(),
        ["color"] = part.Color?.Color == null ? null : string.Join(",", part.Color.Color.Select(F)),
        ["metal"] = part.PaintData.Metal, ["io"] = part.InteractiveObject == null ? null : part.InteractiveObject.name,
        ["ioType"] = part.InteractiveObject == null ? null : part.InteractiveObject.type,
        ["handle"] = part.Handle == null ? null : part.Handle.name, ["handleActive"] = part.Handle != null && part.Handle.activeInHierarchy,
    };

    [HarnessCommand("bonus-state")]
    private static object State(string args)
    {
        var carLoader = Loaded(Args(args).FirstOrDefault() ?? "0");
        var list = carLoader.GetBonusParts();
        var slots = new List<object>();
        for (int i = 0; list != null && i < list.Count; i++) slots.Add(Describe(list[i], i));
        return new Dictionary<string, object>
        {
            ["car"] = carLoader.carToLoad, ["mode"] = GameMode.Get()?.currentMode.ToString(), ["slots"] = slots, ["mismatches"] = MismatchCount(),
        };
    }

    [HarnessCommand("bonus-items")]
    private static object Items(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: bonus-items <loader> <slot>");
        var part = Slot(Loaded(parts[0]), parts[1]);
        var all = BonusPart.GetItems();
        var ids = new List<string>();
        for (int i = 0; all != null && i < all.Count; i++) ids.Add(all[i]);
        string first = null;
        try { first = part.GetFirstItemForType(part.Type); }
        catch (Exception e) { first = "error " + e.GetType().Name; }
        return new Dictionary<string, object> { ["type"] = part.Type.ToString(), ["first"] = first, ["count"] = ids.Count, ["items"] = ids.Take(60).Select(id => $"{id}:{(GameInventory.Instance?.GetItemProperty(id)?.CanPaint ?? false ? "paint" : "plain")}").ToList() };
    }

    [HarnessCommand("bonus-fit")]
    private static object Fit(string args)
    {
        var parts = Args(args);
        if (parts.Length != 3) throw new ArgumentException("usage: bonus-fit <loader> <slot> <itemUid>");
        var carLoader = Loaded(parts[0]);
        var part = Slot(carLoader, parts[1]);
        var item = Singleton<GameManager>.Instance.Inventory.GetItem(long.Parse(parts[2])) ?? throw new ArgumentException($"no item {parts[2]}");
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");
        var mode = GameMode.Get();
        if (mode.currentMode != gameMode.BonusAssemble) mode.SetCurrentMode(gameMode.BonusAssemble);
        PointAt(game, carLoader, part);
        fitting = true;
        try { game.SelectPartToMount(item); }
        finally { fitting = false; }
        return new Dictionary<string, object> { ["mode"] = mode.currentMode.ToString(), ["slot"] = Describe(part, int.Parse(parts[1])), ["itemLeft"] = Singleton<GameManager>.Instance.Inventory.GetItem(item.UID) == null };
    }

    [HarnessCommand("bonus-remove")]
    private static object Remove(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: bonus-remove <loader> <slot>");
        var carLoader = Loaded(parts[0]);
        var part = Slot(carLoader, parts[1]);
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");
        var mode = GameMode.Get();
        if (mode.currentMode != gameMode.BonusDisassemble) mode.SetCurrentMode(gameMode.BonusDisassemble);
        PointAt(game, carLoader, part);
        game.ClickIO(1);
        return new Dictionary<string, object> { ["mode"] = mode.currentMode.ToString(), ["slot"] = Describe(part, int.Parse(parts[1])) };
    }

    [HarnessCommand("bonus-mode")]
    private static object Mode(string args)
    {
        var mode = GameMode.Get();
        string value = (args ?? "").Trim();
        var target = value == "assemble" ? gameMode.BonusAssemble : value == "disassemble" ? gameMode.BonusDisassemble : gameMode.Garage;
        mode.SetCurrentMode(target);
        return new { mode = mode.currentMode.ToString() };
    }

    [HarnessCommand("bonus-apply")]
    private static object Apply(string args)
    {
        var parts = Args(args);
        if (parts.Length < 3) throw new ArgumentException("usage: bonus-apply <loader> <slot> <id|-> [r,g,b,a] [paintType] [game|manual]");
        var carLoader = Loaded(parts[0]);
        var part = Slot(carLoader, parts[1]);
        var steps = new List<string>();
        try
        {
            if (parts[2] == "-")
            {
                part.TakeOff(true);
                Singleton<GameManager>.Instance.BonusPartsManager.TryDeleteBonusPart(carLoader, part, false);
                steps.Add("removed");
            }
            else
            {
                steps.Add($"change {part.Change(parts[2], false)}");
                part.TakeOn(true);
                steps.Add("taken on");
            }
            if (parts.Length > 3)
            {
                var c = parts[3].Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var type = parts.Length > 4 ? (PaintType)Enum.Parse(typeof(PaintType), parts[4], true) : PaintType.Gloss;
                var data = new PaintData { Metal = 0.5f, Roughness = 0.3f, ClearCoat = 0.2f, NormalStrength = 0f, Fresnel = 0.1f };
                var color = new CustomColor { Color = new UnhollowerBaseLib.Il2CppStructArray<float>(4) };
                for (int i = 0; i < 4; i++) color.Color[i] = c[i];
                if (parts.Length > 5 && parts[5] == "manual")
                {
                    part.IsPainted = true;
                    part.Color = color;
                    part.PaintType = type;
                    part.PaintData = data;
                    var renderers = part.Handle.GetComponentsInChildren<Renderer>();
                    foreach (var renderer in renderers) PaintHelper.SetColor(renderer, new Color(c[0], c[1], c[2], c[3]), true);
                    if (type == PaintType.Custom) PaintHelper.SetCustomPaintType(part.Handle, data, true);
                    else PaintHelper.SetPaintType(part.Handle, type, true);
                    steps.Add($"manual paint on {renderers.Length} renderers");
                }
                else
                {
                    part.Paint(true, color, data, type);
                    steps.Add("game paint");
                }
            }
        }
        catch (Exception e) { steps.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
        var property = GameInventory.Instance?.GetItemProperty(part.ID);
        return new Dictionary<string, object> { ["steps"] = steps, ["canPaint"] = property != null && property.CanPaint, ["slot"] = Describe(part, int.Parse(parts[1])) };
    }

    private static int MismatchCount() => (int?)typeof(CMS21Together.Logic.Car.Details.CarDetailsIO).GetProperty("BonusSlotMismatch")?.GetValue(null) ?? -1;

    private static void PointAt(GameScript game, CarLoader carLoader, BonusPart part)
    {
        var io = part.InteractiveObject ?? throw new InvalidOperationException("the slot has no InteractiveObject");
        game.IOMouseOverIO = io;
        game.IOMouseOverCarLoader = carLoader;
        game.IOMouseOverType = io.type;
        game.IOMouseOverGO = io.gameObject;
    }
}
