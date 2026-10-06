using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CMS.UI;
using CMS.UI.Logic;
using CMS.UI.Logic.CaseOpening;
using CMS.UI.Logic.Map;
using CMS.UI.Logic.Upgrades;
using CMS.UI.Windows;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// economy-audit spike 1.1: a logging-only trace of the economy mutators and of every scope opener and Trade entry of
// design.md D4-D10, patched on demand by type name, plus native-only verbs that drive each money path.
public static partial class EconomyCommands
{
    private const int TraceLines = 60;

    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.economy-trace");
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    private static readonly Dictionary<string, string> failures = new Dictionary<string, string>();
    private static readonly Dictionary<MethodBase, string> names = new Dictionary<MethodBase, string>();
    private static readonly List<string> running = new List<string>();
    private static readonly Queue<string> lines = new Queue<string>();
    private static int withoutCaller;
    private static bool patched;
    private static bool tracing;

    private static readonly HashSet<string> Mutators = new HashSet<string>
    {
        "GlobalData.AddPlayerMoney", "GlobalData.AddPlayerScraps", "GlobalData.SetPlayerScraps", "GlobalData.AddPlayerExp",
        "GlobalData.SetPlayerMoney", "GlobalData.set_BarnsAmount",
    };

    private static readonly (string Type, string Method)[] Targets =
    {
        ("GlobalData", "AddPlayerMoney"),
        ("GlobalData", "AddPlayerScraps"),
        ("GlobalData", "SetPlayerScraps"),
        ("GlobalData", "AddPlayerExp"),
        ("GlobalData", "SetPlayerMoney"),
        ("GlobalData", "set_BarnsAmount"),
        ("CMS.UI.Windows.MapWindow", "SubmitPanelAction"),
        ("CMS.UI.Windows.MapWindow", "MeasurePowerForSelectedCarLoader"),
        ("NotificationCenter", "ButtonAccept"),
        ("NotificationCenter", "NewButtonAccept"),
        ("PartScript+_Hide_d__159", "MoveNext"),
        ("FluidRefill", "Hide"),
        ("CMS.Managers.PaintshopManager", "TryGetMoneyForPaint"),
        ("CMS.UI.Windows.PaintshopWindow+_ShowCoroutine_d__10", "MoveNext"),
        ("CMS.UI.Windows.TintingWindow+_ShowCoroutine_d__18", "MoveNext"),
        ("CMS.UI.Windows.TintingWindow", "TintAction"),
        ("WelderLogic+__c__DisplayClass5_0", "Method_Internal_Void_Boolean_PDM_0"),
        ("InteriorDetailingToolkitLogic+__c__DisplayClass6_0", "Method_Internal_Void_Boolean_PDM_0"),
        ("CMS.UI.Windows.RepairPartWindow", "ProcessGameResult"),
        ("CMS.UI.Windows.CaseOpeningWindow", "SetItem"),
        ("CMS.UI.Windows.CaseOpeningWindow", "TakeLoot"),
        ("CMS.UI.Windows.CaseOpeningWindow", "Hide"),
        ("GameScript", "SellCar"),
        ("GameScript+_SellCarCoroutine_d__135", "MoveNext"),
        ("CMS.UI.Logic.Upgrades.SkillsTab", "ResetSkillsAction"),
        ("CMS.UI.Logic.Upgrades.SkillsTab", "CanReset"),
        ("CMS.UI.Logic.Scrap.ScrapProduction", "ProcessGameResult"),
        ("CMS.UI.Logic.Scrap.ScrapProduction", "MakeScrap"),
        ("CMS.UI.Windows.ScrapPerConditionWindow", "AcceptAction"),
        ("Inventory", "ScrapPerCondition"),
        ("CMS.UI.Logic.Scrap.ScrapUpgrade", "UpgradeItem"),
        ("CMS.UI.Logic.Scrap.ScrapUpgrade", "UpgradeAction"),
        ("CMS.UI.Logic.Scrap.ScrapUpgrade", "UpgradeFromOutside"),
        ("CMS.UI.Windows.ShopLicenseBuyWindow", "BuyItem"),
        ("NotificationCenter+__c__DisplayClass17_0", "Method_Internal_Void_Boolean_PDM_0"),
        ("NotificationCenter+__c__DisplayClass17_0", "Method_Internal_Void_Boolean_PDM_1"),
        ("CMS.Difficulty.DifficultyManager", "ActivateDifficultyLevel"),
    };

    private sealed class Call
    {
        public string Name;
        public string Before;
    }

    [HarnessCommand("econ-trace")]
    private static object Trace(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on": PatchAll(); tracing = true; break;
            case "off": tracing = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: econ-trace on|off|report");
        }
        return new Dictionary<string, object>
        {
            ["tracing"] = tracing,
            ["counts"] = Targets.Select(t => $"{TypeLabel(t.Type)}.{t.Method}").Distinct().ToDictionary(n => n, n => counts.TryGetValue(n, out int c) ? c : 0),
            ["failures"] = failures,
            ["mutatorCallsWithoutCaller"] = withoutCaller,
            ["lines"] = lines.ToList(),
        };
    }

    private static string TypeLabel(string type) => type.Substring(type.LastIndexOf('.') + 1);

    private static void PatchAll()
    {
        if (patched) return;
        patched = true;
        var prefix = new HarmonyMethod(typeof(EconomyCommands).GetMethod(nameof(TracePrefix), BindingFlags.NonPublic | BindingFlags.Static));
        var postfix = new HarmonyMethod(typeof(EconomyCommands).GetMethod(nameof(TracePostfix), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var target in Targets)
        {
            string name = $"{TypeLabel(target.Type)}.{target.Method}";
            try
            {
                var type = AccessTools.TypeByName(target.Type) ?? throw new TypeLoadException(target.Type);
                var methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == target.Method).ToList();
                if (methods.Count == 0) throw new MissingMethodException(name);
                foreach (var method in methods)
                {
                    names[method] = name;
                    harmony.Patch(method, prefix: prefix, postfix: postfix);
                }
            }
            catch (Exception e)
            {
                failures[name] = (e.GetType().Name + ": " + e.Message.Split('\n')[0]).Replace("HarmonyException", "patch error");
                MelonLogger.Warning($"[Harness] econ-trace cannot patch {name}: {failures[name]}");
            }
        }
    }

    private static void TracePrefix(MethodBase __originalMethod, object[] __args, out Call __state)
    {
        __state = null;
        if (!tracing) return;
        string name = names.TryGetValue(__originalMethod, out string known) ? known : $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
        counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
        __state = new Call { Name = name, Before = Stats() };
        if (!Mutators.Contains(name))
        {
            running.Add(name);
            return;
        }
        string caller = running.Count > 0 ? running[running.Count - 1] : "none";
        if (running.Count == 0) withoutCaller++;
        string arguments = __args == null ? "" : string.Join(", ", __args.Select(a => a?.ToString() ?? "null"));
        Remember($"{name}({arguments}) caller {caller}, scene {CMS21Together.Data.ClientScene.LocalScene}, mode {GameMode.Get()?.GetCurrentMode()}, window {TopWindow()}, before {__state.Before}");
    }

    private static void TracePostfix(Call __state)
    {
        if (__state == null) return;
        if (!Mutators.Contains(__state.Name))
        {
            int index = running.LastIndexOf(__state.Name);
            if (index >= 0) running.RemoveAt(index);
            return;
        }
        Remember($"{__state.Name} after {Stats()}");
    }

    private static void Remember(string line)
    {
        MelonLogger.Msg($"[Harness] econ-trace {line}");
        lines.Enqueue(line);
        while (lines.Count > TraceLines) lines.Dequeue();
    }

    private static string Stats() =>
        $"money {GlobalData.PlayerMoney} scrap {GlobalData.PlayerScraps} exp {GlobalData.PlayerExp} level {GlobalData.PlayerLevel} barns {GlobalData.BarnsAmount}";

    private static string TopWindow()
    {
        try
        {
            var window = WindowManager.Instance?.GetLastOpenedWindow();
            return window == null ? "none" : window.gameObject.name;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string[] Split(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static CarLoader LoadedCar(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        return carLoader;
    }

    private static Item InventoryItem(string uid)
    {
        var item = Singleton<GameManager>.Instance.Inventory.GetItem(long.Parse(uid));
        return item ?? throw new ArgumentException($"no inventory item {uid}");
    }

    private static T Window<T>(WindowID id, bool show) where T : CMS.UI.Windows.Base.Window
    {
        var manager = WindowManager.Instance ?? throw new InvalidOperationException("no WindowManager");
        if (show && !manager.IsWindowActive(id)) manager.Show(id, false);
        return manager.GetWindowByID<T>(id) ?? throw new InvalidOperationException($"no {id} window");
    }

    private static Dictionary<string, object> Result(string before, Dictionary<string, object> extra = null)
    {
        var result = extra ?? new Dictionary<string, object>();
        result["before"] = before;
        result["after"] = Stats();
        return result;
    }

    [HarnessCommand("econ-map-travel")]
    private static object MapTravel(string args)
    {
        var destination = (MapDestinationID)Enum.Parse(typeof(MapDestinationID), (args ?? "").Trim(), true);
        if (destination != MapDestinationID.Junkyard && destination != MapDestinationID.Auction && destination != MapDestinationID.Barn)
            throw new ArgumentException("usage: econ-map-travel Junkyard|Auction|Barn");
        string before = Stats();
        var map = Window<MapWindow>(WindowID.Map, false);
        map.currentSelectedDestination = destination;
        map.haveToSelectCar = false;
        map.SubmitPanelAction();
        return Result(before, new Dictionary<string, object> { ["destination"] = destination.ToString(), ["travelHaveCost"] = GameSettings.GameSettingsData.TravelHaveCost });
    }

    [HarnessCommand("econ-fee")]
    private static object Fee(string args)
    {
        var parts = Split(args);
        if (parts.Length < 2) throw new ArgumentException("usage: econ-fee spill|refill|wash-paint|wash-tint <loader> | tint <loader> <windows>");
        string before = Stats();
        var carLoader = LoadedCar(parts[1]);
        var result = new Dictionary<string, object> { ["fee"] = parts[0] };
        switch (parts[0])
        {
            case "spill":
                var registry = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader);
                string key = registry.SubKeys.FirstOrDefault(k =>
                {
                    var part = registry.Sub(k);
                    return !part.IsUnmounted && !string.IsNullOrEmpty(part.sendMessageOnHide) && part.sendMessageOnHide.StartsWith("Zero") && !part.IsBlocked();
                }) ?? throw new InvalidOperationException("no mounted part that holds fluid");
                var script = registry.Sub(key);
                result["key"] = key;
                result["message"] = script.sendMessageOnHide;
                script.StartCoroutine(script.Hide());
                break;
            case "refill":
                var refill = UnityEngine.Object.FindObjectOfType<FluidRefill>() ?? throw new InvalidOperationException("no FluidRefill tool in this scene");
                result["start"] = refill.startFluidAmount;
                refill.carLoader = carLoader;
                refill.Hide();
                break;
            case "wash-paint":
                result["shown"] = WindowManager.Instance.Show(WindowID.Paintshop, false);
                break;
            case "wash-tint":
                result["shown"] = WindowManager.Instance.Show(WindowID.Tinting, false);
                break;
            case "tint":
                if (parts.Length != 3) throw new ArgumentException("usage: econ-fee tint <loader> <windows>");
                var tinting = Window<TintingWindow>(WindowID.Tinting, true);
                var status = tinting.windowStatus ?? throw new InvalidOperationException("the tinting window has no windows (no car selected)");
                int windows = int.Parse(parts[2]);
                for (int i = 0; i < status.Length; i++) status[i] = i < windows;
                result["cost"] = tinting.GetTintCost();
                tinting.TintAction();
                break;
            default:
                throw new ArgumentException("usage: econ-fee spill|refill|wash-paint|wash-tint <loader> | tint <loader> <windows>");
        }
        return Result(before, result);
    }

    [HarnessCommand("econ-unmount")]
    private static object Unmount(string args)
    {
        var carLoader = LoadedCar((args ?? "").Trim());
        var registry = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader);
        string key = registry.SubKeys.FirstOrDefault(k =>
        {
            var part = registry.Sub(k);
            return !part.IsUnmounted && part.GetUnmountWith().Count == 0 && !part.IsBlocked() && string.IsNullOrEmpty(part.sendMessageOnHide);
        }) ?? throw new InvalidOperationException("no free mounted part without fluid");
        string before = Stats();
        var script = registry.Sub(key);
        script.StartCoroutine(script.Hide());
        return Result(before, new Dictionary<string, object> { ["key"] = key });
    }

    [HarnessCommand("econ-skill-reset")]
    private static object SkillReset(string args)
    {
        string before = Stats();
        WindowManager.Instance.Show(WindowID.Upgrades, false);
        var tabs = Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<SkillsTab>());
        var tab = tabs != null && tabs.Length > 0 ? tabs[0].Cast<SkillsTab>() : throw new InvalidOperationException("no SkillsTab");
        bool canReset = tab.CanReset(out ResetUpgradeLockReason reason);
        int points = tab.pointsToReset;
        tab.ResetSkillsAction();
        return Result(before, new Dictionary<string, object> { ["canReset"] = canReset, ["reason"] = reason.ToString(), ["points"] = points });
    }

    [HarnessCommand("econ-sell-car")]
    private static object SellCar(string args)
    {
        var parts = Split(args);
        if (parts.Length < 1) throw new ArgumentException("usage: econ-sell-car <loader> [price]");
        string before = Stats();
        var carLoader = LoadedCar(parts[0]);
        int price = parts.Length > 1 ? int.Parse(parts[1]) : 10000;
        GameScript.Get().SellCar(carLoader, price);
        return Result(before, new Dictionary<string, object> { ["loader"] = parts[0], ["price"] = price });
    }

    [HarnessCommand("econ-scrap")]
    private static object Scrap(string args)
    {
        var parts = Split(args);
        if (parts.Length != 2) throw new ArgumentException("usage: econ-scrap <uid> <grade 0|1|2>");
        string before = Stats();
        var item = InventoryItem(parts[0]);
        var bar = parts[1] switch { "0" => BarType.Success, "1" => BarType.Bonus, "2" => BarType.BigBonus, _ => throw new ArgumentException("grade 0, 1 or 2") };
        var production = Window<ScrapWindow>(WindowID.Scrap, true).scrapProduction ?? throw new InvalidOperationException("no ScrapProduction");
        int expected = GlobalData.GetScrapFromItem(item, (ScrapType)int.Parse(parts[1]));
        production.currentItem = item;
        production.ProcessGameResult(bar);
        return Result(before, new Dictionary<string, object> { ["id"] = item.ID, ["expected"] = expected });
    }

    [HarnessCommand("econ-scrap-condition")]
    private static object ScrapCondition(string args)
    {
        float percent = float.Parse((args ?? "").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        string before = Stats();
        var window = Window<ScrapPerConditionWindow>(WindowID.ScrapPerCondition, true);
        window.currentSliderValue = percent;
        window.AcceptAction();
        return Result(before, new Dictionary<string, object> { ["percent"] = percent });
    }

    [HarnessCommand("econ-scrap-upgrade")]
    private static object ScrapUpgrade(string args)
    {
        var parts = Split(args);
        if (parts.Length < 1) throw new ArgumentException("usage: econ-scrap-upgrade <uid>");
        string before = Stats();
        var item = InventoryItem(parts[0]);
        var upgrade = Window<ScrapWindow>(WindowID.Scrap, true).scrapUpgrade ?? throw new InvalidOperationException("no ScrapUpgrade");
        upgrade.currentItem = item;
        upgrade.CalculateUpgrade();
        int cost = upgrade.upgradeCost;
        int quality = upgrade.newQuality;
        upgrade.UpgradeItem();
        return Result(before, new Dictionary<string, object> { ["id"] = item.ID, ["cost"] = cost, ["quality"] = quality });
    }

    [HarnessCommand("econ-license")]
    private static object License(string args)
    {
        var parts = Split(args);
        if (parts.Length < 1) throw new ArgumentException("usage: econ-license <amount> [text] [plateId]");
        string before = Stats();
        var window = Window<ShopLicenseBuyWindow>(WindowID.ShopLicenseBuy, true);
        bool custom = parts.Length > 1;
        if (parts.Length > 2 || string.IsNullOrEmpty(window.itemID)) window.itemID = parts.Length > 2 ? parts[2] : "LicensePlate";
        window.customLicensePlate = custom;
        window.currentAmount = int.Parse(parts[0]);
        window.UpdatePriceText(window.currentAmount, custom);
        float price = window.currentPrice;
        window.BuyItem(custom, false, custom ? parts[1] : "");
        return Result(before, new Dictionary<string, object> { ["price"] = price, ["plate"] = window.itemID });
    }

    private static CaseOpeningWindow OpenCase(string uid)
    {
        var item = InventoryItem(uid);
        var window = Window<CaseOpeningWindow>(WindowID.CaseOpening, true);
        window.SetItem(item);
        return window;
    }

    [HarnessCommand("econ-crate")]
    private static object Crate(string args)
    {
        var parts = Split(args);
        if (parts.Length != 2) throw new ArgumentException("usage: econ-crate <uid> <card 0|1|2|3>");
        string before = Stats();
        var type = (CaseCardType)int.Parse(parts[1]);
        var window = OpenCase(parts[0]);
        var cards = window.cards ?? throw new InvalidOperationException("the case window has no cards");
        CaseOpeningItem card = null;
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null && cards[i].TypeOfCard == type) card = cards[i];
        if (card == null)
        {
            card = cards[0];
            card.TypeOfCard = type;
            CMS.Helpers.UIHelper.SetCaseOpeningCardValue(card);
        }
        int value = card.itemValue;
        window.TakeLoot(card);
        return Result(before, new Dictionary<string, object> { ["card"] = type.ToString(), ["value"] = value, ["case"] = parts[0] });
    }

    [HarnessCommand("econ-crate-close")]
    private static object CrateClose(string args)
    {
        string before = Stats();
        var window = OpenCase((args ?? "").Trim());
        window.HideAction();
        return Result(before, new Dictionary<string, object> { ["caseOpened"] = window.caseOpened });
    }

    [HarnessCommand("econ-barn-map")]
    private static object BarnMap(string args)
    {
        string before = Stats();
        var lambda = new NotificationCenter.__c__DisplayClass17_0 { item = InventoryItem((args ?? "").Trim()) };
        lambda.Method_Internal_Void_Boolean_PDM_1(true);
        return Result(before);
    }
}
