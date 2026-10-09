using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS.PartModules;
using CMS.UI;
using CMS.UI.Logic;
using CMS.UI.Logic.Tune;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Car.Parts;
using UnhollowerBaseLib;

namespace TogetherTestHarness.Features;

// sync-tuning-bonus-and-new-engines group 2: the tune window through the dyno computer's click (#dynoTune), its tabs'
// own apply actions, and what the game keeps on a tuned part's item.
public static class TuneCommands
{
    private const string ClickType = "#dynoTune";

    private static TuneWindow Window =>
        WindowManager.Instance?.GetWindowByID<TuneWindow>(WindowID.Tune) ?? throw new InvalidOperationException("no tune window");

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static CarLoader Loaded(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        return carLoader;
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static PartScript GearboxPart(CarLoader carLoader)
    {
        var handle = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
        return handle == null ? null : handle.gameObject.GetComponent<PartScript>();
    }

    private static string KeyOf(CarLoader carLoader, PartScript script)
    {
        int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
        var registry = loader < 0 ? null : CarPartsSync.Get(loader).Registry;
        return registry != null && script != null && registry.TryGetSubPath(script, out var path) ? PartKeys.Sub(path) : null;
    }

    private static object Part(CarLoader carLoader, PartScript script) => script == null ? null : new Dictionary<string, object>
    {
        ["key"] = KeyOf(carLoader, script), ["id"] = script.id, ["tunedId"] = script.tunedID, ["tuned"] = script.IsTuned(),
        ["unmounted"] = script.IsUnmounted, ["blocked"] = script.IsBlocked(),
    };

    [HarnessCommand("tune-probe")]
    private static object Probe(string args)
    {
        var carLoader = Loaded(Args(args).FirstOrDefault() ?? "0");
        var handle = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
        var gearbox = GearboxPart(carLoader);
        var modules = new List<object>();
        var ids = new HashSet<string>();
        if (gearbox != null) ids.Add(gearbox.id);
        foreach (var module in carLoader.GetRoot().GetComponentsInChildren<PartModule>(true))
        {
            var script = module.PartScript;
            if (script != null) ids.Add(script.id);
            modules.Add(new Dictionary<string, object>
            {
                ["type"] = module.GetIl2CppType().Name, ["part"] = Part(carLoader, script), ["moduleTuned"] = module.IsTuned(),
                ["values"] = module.GetValues() == null ? null : string.Join(",", module.GetValues().Select(v => v.ToString())),
            });
        }
        var rows = new List<object>();
        var inventory = GameInventory.Instance;
        var tuning = inventory?.tuningArray;
        for (int i = 0; tuning != null && i < tuning.Count && rows.Count < 40; i++)
        {
            var row = tuning[i];
            if (row == null) continue;
            var cells = row.ToArray();
            if (!cells.Any(c => ids.Contains(c) || (c ?? "").Contains("gearbox") && cells.Any(ids.Contains))) continue;
            rows.Add(new Dictionary<string, object>
            {
                ["row"] = string.Join("|", cells),
                ["isTuning"] = cells.Where(c => !string.IsNullOrEmpty(c)).ToDictionary(c => c, c => ids.Where(id => id != c).Select(id => $"{id}:{SafeIsTuning(inventory, c, id)}").ToList()),
            });
        }
        var sample = new List<string>();
        for (int i = 0; tuning != null && i < tuning.Count && i < 5; i++) sample.Add(tuning[i] == null ? null : string.Join("|", tuning[i].ToArray()));
        return new Dictionary<string, object>
        {
            ["car"] = carLoader.carToLoad, ["place"] = carLoader.placeNo, ["fromOrder"] = carLoader.customerCar,
            ["gearbox"] = Part(carLoader, gearbox),
            ["ratios"] = handle?.gearRatio == null ? null : string.Join(",", handle.gearRatio.Select(F)),
            ["finalDrive"] = handle == null ? 0f : handle.finalDriveRatio,
            ["modules"] = modules, ["tuningRows"] = tuning?.Count ?? -1, ["rows"] = rows, ["sample"] = sample,
        };
    }

    private static string SafeIsTuning(GameInventory inventory, string id, string other)
    {
        try { return inventory.IsTuning(id, other, false).ToString(); }
        catch (Exception e) { return "error " + e.GetType().Name; }
    }

    [HarnessCommand("tune-part")]
    private static object TunePart(string args)
    {
        var parts = Args(args);
        if (parts.Length != 3) throw new ArgumentException("usage: tune-part <loader> <key|gearbox> <tunedId>");
        var carLoader = Loaded(parts[0]);
        var script = parts[1] == "gearbox" ? GearboxPart(carLoader) : PartRegistry.Build(carLoader).Sub(parts[1]);
        if (script == null) throw new ArgumentException($"no part {parts[1]}");
        bool changed = script.TunePart(parts[2]);
        PartChangeTracker.MarkDirty(CarLoaderPlaces.Get().GetCarLoaderId(carLoader));
        return new { changed, part = Part(carLoader, script) };
    }

    [HarnessCommand("tune-open")]
    private static object Open(string args)
    {
        var carLoader = Loaded(Args(args).FirstOrDefault() ?? "0");
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");
        string previous = game.IOMouseOverType;
        game.IOMouseOverType = ClickType;
        try { game.ClickIO(0); }
        finally { game.IOMouseOverType = previous; }
        return State(carLoader);
    }

    [HarnessCommand("tune-close")]
    private static object Close(string args)
    {
        var window = Window;
        if (!window.isActive) throw new InvalidOperationException("the window is not open");
        window.HideAction();
        return new { open = window.isActive };
    }

    [HarnessCommand("tune-state")]
    private static object TuneState(string args) => State(null);

    private static Dictionary<string, object> State(CarLoader expected)
    {
        var window = WindowManager.Instance?.GetWindowByID<TuneWindow>(WindowID.Tune);
        var tab = window?.gearboxTab;
        return new Dictionary<string, object>
        {
            ["open"] = window != null && window.isActive,
            ["car"] = window?.carLoader?.carToLoad,
            ["sameCar"] = expected == null || window?.carLoader != null && window.carLoader.Pointer == expected.Pointer,
            ["mode"] = GameMode.Get()?.currentMode.ToString(),
            ["tab"] = window == null ? null : window.currentTab.ToString(),
            ["gearboxTab"] = tab == null ? null : new Dictionary<string, object>
            {
                ["hasGearbox"] = tab.hasGearbox, ["canTune"] = tab.CanTune, ["gears"] = tab.gearsAmount, ["final"] = tab.gearsFinalRatio,
                ["sameCar"] = tab.carLoader != null && window.carLoader != null && tab.carLoader.Pointer == window.carLoader.Pointer,
            },
        };
    }

    [HarnessCommand("cardetails-ui")]
    private static object DetailsUi(string args)
    {
        var parts = Args(args);
        if (parts.Length < 3 || parts[0] != "gearbox") throw new ArgumentException("usage: cardetails-ui gearbox <loader> <finalRatio>");
        var carLoader = Loaded(parts[1]);
        var window = Window;
        if (!window.isActive || window.carLoader == null || window.carLoader.Pointer != carLoader.Pointer) throw new InvalidOperationException("the tune window is not open on this car");
        var tab = window.gearboxTab;
        if (!tab.hasGearbox) throw new InvalidOperationException("the gearbox tab has no gearbox");
        float final = float.Parse(parts[2], CultureInfo.InvariantCulture);
        tab.gearsFinalRatioSlider.SetValue(final, true);
        float shown = tab.gearsFinalRatio;
        tab.ApplyAction();
        var handle = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
        return new Dictionary<string, object>
        {
            ["slider"] = shown, ["final"] = handle?.finalDriveRatio ?? 0f,
            ["ratios"] = handle?.gearRatio == null ? null : string.Join(",", handle.gearRatio.Select(F)),
            ["entry"] = Entry(carLoader, CarDetailEntries.Gearbox),
        };
    }

    [HarnessCommand("tune-ecu")]
    private static object Ecu(string args)
    {
        var parts = Args(args);
        if (parts.Length < 1) throw new ArgumentException("usage: tune-ecu <loader> [seed]");
        var carLoader = Loaded(parts[0]);
        var window = Window;
        if (!window.isActive) throw new InvalidOperationException("the tune window is not open");
        var ecu = window.ecuTuning;
        var carb = window.carbTuning;
        string tab;
        if (ecu != null && ecu.CanTune)
        {
            window.OnTabChange((int)TuneWindowTabs.Ecu);
            ecu.ApplyAction();
            tab = "ecu";
        }
        else if (carb != null && carb.CanTune)
        {
            window.OnTabChange((int)TuneWindowTabs.Carb1);
            carb.ApplyAction();
            tab = "carb";
        }
        else throw new InvalidOperationException("neither the ECU nor the carburettor tab can tune this car");
        var modules = new Dictionary<string, object>();
        foreach (var module in carLoader.GetRoot().GetComponentsInChildren<PartModule>(true))
        {
            string key = KeyOf(carLoader, module.PartScript);
            if (key != null) modules[key] = Entry(carLoader, CarDetailEntries.Module(key));
        }
        return new { tab, modules };
    }

    [HarnessCommand("cardetails-gearbox")]
    private static object SetGearbox(string args)
    {
        var parts = Args(args);
        if (parts.Length < 2) throw new ArgumentException("usage: cardetails-gearbox <loader> <final> [r1,r2,...]");
        var carLoader = Loaded(parts[0]);
        var handle = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>() ?? throw new InvalidOperationException("no gearbox");
        handle.finalDriveRatio = float.Parse(parts[1], CultureInfo.InvariantCulture);
        if (parts.Length > 2) handle.gearRatio = parts[2].Split(',').Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToArray();
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Tuning);
        return Entry(carLoader, CarDetailEntries.Gearbox);
    }

    [HarnessCommand("item-tuning")]
    private static object ItemTuning(string args)
    {
        string id = (args ?? "").Trim();
        var inventory = Singleton<GameManager>.Instance.Inventory;
        var result = new List<object>();
        foreach (var item in inventory.items)
        {
            if (item.ID != id && item.UID.ToString() != id) continue;
            var tuning = item.tuningData;
            var gearbox = item.GearboxData;
            result.Add(new Dictionary<string, object>
            {
                ["id"] = item.ID, ["uid"] = item.UID,
                ["tuned"] = tuning.IsTuned, ["values"] = tuning.Values == null ? null : string.Join(",", tuning.Values.Select(v => v.ToString())),
                ["tuningValue"] = tuning.TuningValue,
                ["gearRatio"] = gearbox.GearRatio == null ? null : string.Join(",", gearbox.GearRatio.Select(F)), ["final"] = gearbox.FinalDriveRatio,
            });
        }
        return result;
    }

    private static object Entry(CarLoader carLoader, string id)
    {
        var signatures = CarDetailEntries.Signatures(CarDetailsIO.Read(carLoader, CarDetailSection.Tuning));
        return signatures.TryGetValue(id, out string value) ? value : null;
    }

    private static System.Reflection.PropertyInfo IdleSetting => typeof(LockLifecycle).GetProperty("TuneIdleSeconds");

    [HarnessCommand("tune-idle")]
    private static object Idle(string args)
    {
        var setting = IdleSetting ?? throw new InvalidOperationException("this build has no tune idle cap");
        setting.SetValue(null, float.Parse((args ?? "").Trim(), CultureInfo.InvariantCulture));
        return new { seconds = setting.GetValue(null) };
    }

    internal static void Reset(List<string> changed)
    {
        var setting = IdleSetting;
        if (setting == null || Math.Abs((float)setting.GetValue(null) - 300f) < 0.01f) return;
        changed.Add("tune-idle");
        setting.SetValue(null, 300f);
    }
}
