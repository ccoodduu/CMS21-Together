using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CMS.UI;
using CMS.UI.Logic;
using CMS.UI.Windows;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;
using MelonLoader;
using UnhollowerBaseLib;
using UnityEngine;

namespace TogetherTestHarness.Features;

// part-locks spikes (group 1): a logging-only trace of the hover, selection, chooser, fluid, lift and move hooks, the
// relations dump behind LockSets, a scratch re-invocation of every gated entry point (spike 1.4), and lock-click, the
// input shim that makes the game's own Raycast see a press over a part (spike 1.7).
[HarmonyPatch]
public static class LockTraceCommands
{
    private const int TraceLimit = 4000;
    private const float ReinvokeDelaySeconds = 0.15f;

    private static readonly List<string> lines = new List<string>();
    private static bool tracing;
    private static float traceStart;
    private static bool reinvoke;
    private static bool bypass;
    private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
    private static readonly Dictionary<IntPtr, bool> ioMouseOver = new Dictionary<IntPtr, bool>();
    private static string lastHovered;
    private static float lastHoveredAt;
    private static string lastPartMouseOver;
    private static string lastIODescription;
    private static bool lastCanCount;
    private static float holdStartedAt = -1f;
    private static bool holdFull;

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static void Trace(string text)
    {
        if (!tracing) return;
        string line = $"{(Time.realtimeSinceStartup - traceStart) * 1000f:0} f{Time.frameCount} {text}";
        lines.Add(line);
        if (lines.Count > TraceLimit) lines.RemoveAt(0);
        MelonLogger.Msg($"[Harness] lock-trace {line}");
    }

    private static void Count(string what) => counters[what] = (counters.TryGetValue(what, out int n) ? n : 0) + 1;

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("lock-trace");
        if (reinvoke) changed.Add("lock-trace reinvoke");
        if (click != null) changed.Add("lock-click");
        tracing = false;
        reinvoke = false;
        click = null;
        lines.Clear();
        counters.Clear();
    }

    [HarnessCommand("lock-trace")]
    private static object LockTrace(string args)
    {
        var parts = Args(args);
        switch (parts.Length > 0 ? parts[0] : "")
        {
            case "on":
                tracing = true;
                traceStart = Time.realtimeSinceStartup;
                lines.Clear();
                counters.Clear();
                return "tracing";
            case "off":
                tracing = false;
                return "not tracing";
            case "clear":
                lines.Clear();
                counters.Clear();
                return "cleared";
            case "report":
                int take = parts.Length > 1 ? int.Parse(parts[1]) : 300;
                return new Dictionary<string, object> { ["lines"] = lines.Skip(Math.Max(0, lines.Count - take)).ToList(), ["counters"] = new Dictionary<string, int>(counters) };
            case "state":
                return State();
            case "reinvoke":
                reinvoke = parts.Length > 1 && parts[1] == "on";
                return reinvoke ? "re-invoking gated entry points after 150 ms" : "direct";
            case "relations":
                if (parts.Length < 3) throw new ArgumentException("usage: lock-trace relations <loader> <file>");
                return Relations(int.Parse(parts[1]), string.Join(" ", parts.Skip(2)));
            default:
                throw new ArgumentException("usage: lock-trace on|off|clear|report [n]|state|reinvoke on|off|relations <loader> <file>");
        }
    }

    private static string Id(PartScript script) => script == null ? "null" : script.id;

    private static string KeyOf(PartScript script)
    {
        if (script == null) return "null";
        foreach (var sync in CarPartsSync.All)
            if (sync.Registry != null && sync.Registry.TryGetSubPath(script, out var path)) return $"{sync.Loader}/{PartKeys.Sub(path)}({script.id})";
        return $"?({script.id})";
    }

    private static string Mode() => GameMode.Get() == null ? "none" : GameMode.Get().currentMode.ToString();

    private static string Hold()
    {
        var cursor = Cursor3D.Get();
        if (cursor == null) return "cursor none";
        var image = cursor.cursorTimerImage;
        return $"fill {(image == null ? -1f : image.fillAmount):F2} hold {cursor.holdTime:0} fillTime {cursor.fillTime:0} count {cursor.canCountTime} holden {cursor.isCursorHolden}";
    }

    private static string Item(BaseItem item)
    {
        if (item == null) return "null";
        var group = item.TryCast<GroupItem>();
        if (group != null)
        {
            var members = new List<string>();
            for (int i = 0; group.ItemList != null && i < group.ItemList.Count; i++) members.Add($"{group.ItemList[i].ID}#{group.ItemList[i].UID}");
            return $"group {group.ID}#{group.UID} [{string.Join(",", members)}]";
        }
        var single = item.TryCast<Item>();
        return single != null ? $"item {single.ID}#{single.UID}" : $"base {item.GetType().Name}";
    }

    private static Dictionary<string, object> State()
    {
        var game = GameScript.Get();
        var chooser = WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
        var ui = UIManager.Get();
        return new Dictionary<string, object>
        {
            ["mode"] = Mode(),
            ["previousMode"] = GameMode.Get()?.previousMode.ToString(),
            ["partMouseOver"] = KeyOf(game?.partMouseOver),
            ["selectedPart"] = KeyOf(game?.SelectedPart),
            ["selectedToMount"] = Item(game?.SelectedToMount),
            ["raycastOnItemName"] = game?.raycastOnItemName,
            ["ioDescription"] = ui?.TextDescription?.text,
            ["unmountGroup"] = game?.UnmountGroup ?? -1,
            ["chooserActive"] = WindowManager.Instance != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp),
            ["chooserIsActive"] = chooser != null && chooser.isActive,
            ["hold"] = Hold(),
            ["refillActive"] = UnityEngine.Object.FindObjectOfType<FluidRefill>()?.IsActive ?? false,
            ["extractorActive"] = UnityEngine.Object.FindObjectOfType<FluidExtractor>()?.IsActive ?? false,
            ["counters"] = new Dictionary<string, int>(counters),
            ["click"] = click == null ? null : click.Describe(),
        };
    }

    public static void Update()
    {
        UpdateClick();
        if (!tracing) return;
        var cursor = Cursor3D.Get();
        if (cursor == null) return;
        bool counting = cursor.canCountTime;
        if (counting && !lastCanCount)
        {
            holdStartedAt = Time.realtimeSinceStartup;
            holdFull = false;
            Trace($"hold start over {KeyOf(GameScript.Get()?.partMouseOver)} mode {Mode()} fillTime {cursor.fillTime:0}");
        }
        var image = cursor.cursorTimerImage;
        if (counting && !holdFull && image != null && image.fillAmount >= 1f)
        {
            holdFull = true;
            Trace($"hold full after {(Time.realtimeSinceStartup - holdStartedAt) * 1000f:0} ms");
        }
        if (!counting && lastCanCount) Trace($"hold end ({Hold()})");
        lastCanCount = counting;
    }

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionUnMount))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeActionUnMount(PartScript __instance)
    {
        Trace($"ActionUnMount {KeyOf(__instance)} mode {Mode()} canBeUnmount {__instance.canBeUnmount} unmounted {__instance.IsUnmounted} {Hold()}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var target = __instance;
        Defer("ActionUnMount", () => target.ActionUnMount(), () => $"selectedPart {KeyOf(GameScript.Get()?.SelectedPart)} unmounted {target.IsUnmounted}");
        return false;
    }

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionUnMount))]
    [HarmonyPostfix]
    private static void AfterActionUnMount(PartScript __instance, bool __runOriginal) =>
        Trace($"ActionUnMount done {KeyOf(__instance)} ran {__runOriginal} mode {Mode()} selectedPart {KeyOf(GameScript.Get()?.SelectedPart)} {Hold()}");

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionMount))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeActionMount(PartScript __instance, bool showMenu)
    {
        Trace($"ActionMount({showMenu}) {KeyOf(__instance)} mode {Mode()} unmounted {__instance.IsUnmounted}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var target = __instance;
        Defer("ActionMount", () => target.ActionMount(showMenu), () => $"chooser {WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp)}");
        return false;
    }

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionMount))]
    [HarmonyPostfix]
    private static void AfterActionMount(PartScript __instance, bool __runOriginal) =>
        Trace($"ActionMount done {KeyOf(__instance)} ran {__runOriginal} mode {Mode()} chooser {WindowManager.Instance?.IsWindowActive(WindowID.ChoosePartUp)} partMouseOver {KeyOf(GameScript.Get()?.partMouseOver)}");

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.SetMouseOver), new Type[0])]
    [HarmonyPrefix]
    private static void BeforeSetMouseOverGroup(PartScript __instance)
    {
        Count("PartScript.SetMouseOver()");
        string key = KeyOf(__instance);
        if (key == lastHovered && Time.realtimeSinceStartup - lastHoveredAt < 1f) return;
        lastHovered = key;
        lastHoveredAt = Time.realtimeSinceStartup;
        Trace($"PartScript.SetMouseOver() {key} mode {Mode()}");
    }

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.SetMouseOver), typeof(bool))]
    [HarmonyPrefix]
    private static void BeforeSetMouseOverBool(PartScript __instance, bool b)
    {
        Count("PartScript.SetMouseOver(bool)");
        if (b) Trace($"PartScript.SetMouseOver(true) {KeyOf(__instance)}");
    }

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.MouseOverGroup))]
    [HarmonyPrefix]
    private static void BeforeMouseOverGroup(PartScript __instance) => Count("PartScript.MouseOverGroup");

    [HarmonyPatch(typeof(InteractiveObject), nameof(InteractiveObject.SetMouseOver), typeof(bool))]
    [HarmonyPrefix]
    private static void BeforeIOSetMouseOver(InteractiveObject __instance, bool b) => IOMouseOver(__instance, b, "bool");

    [HarmonyPatch(typeof(InteractiveObject), nameof(InteractiveObject.SetMouseOver), typeof(bool), typeof(Color))]
    [HarmonyPrefix]
    private static void BeforeIOSetMouseOverColor(InteractiveObject __instance, bool b) => IOMouseOver(__instance, b, "bool, Color");

    private static void IOMouseOver(InteractiveObject io, bool b, string overload)
    {
        Count($"InteractiveObject.SetMouseOver({overload})");
        if (io == null) return;
        if (ioMouseOver.TryGetValue(io.Pointer, out bool last) && last == b) return;
        ioMouseOver[io.Pointer] = b;
        Trace($"InteractiveObject.SetMouseOver({overload}) {io.name} -> {b} mode {Mode()}");
    }

    [HarmonyPatch(typeof(GameScript), nameof(GameScript.SetPartMouseOver))]
    [HarmonyPostfix]
    private static void AfterSetPartMouseOver(GameScript __instance, PartScript part)
    {
        Count("GameScript.SetPartMouseOver");
        string key = KeyOf(part);
        if (key == lastPartMouseOver) return;
        lastPartMouseOver = key;
        Trace($"SetPartMouseOver {key} mode {Mode()} label '{__instance.raycastOnItemName}' description '{UIManager.Get()?.TextDescription?.text}'");
    }

    [HarmonyPatch(typeof(GameScript), nameof(GameScript.SetIOMouseOver))]
    [HarmonyPostfix]
    private static void AfterSetIOMouseOver(GameScript __instance, InteractiveObject io) =>
        Trace($"SetIOMouseOver {(io == null ? "null" : io.name)} car {(__instance.IOMouseOverCarLoader == null ? "none" : __instance.IOMouseOverCarLoader.carToLoad)} label '{__instance.raycastOnItemName}'");

    [HarmonyPatch(typeof(UIManager), nameof(UIManager.SetIODescription))]
    [HarmonyPrefix]
    private static void BeforeSetIODescription(string text, AlternativeDescriptionID type)
    {
        Count("UIManager.SetIODescription");
        string value = $"{type}:{text}";
        if (value == lastIODescription) return;
        lastIODescription = value;
        Trace($"SetIODescription {type} '{text}'");
    }

    [HarmonyPatch(typeof(GameScript), nameof(GameScript.SelectToUnMount))]
    [HarmonyPrefix]
    private static void BeforeSelectToUnMount(PartScript target) => Trace($"SelectToUnMount {KeyOf(target)} mode {Mode()}");

    [HarmonyPatch(typeof(GameScript), nameof(GameScript.SelectPartToMount))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeSelectPartToMount(GameScript __instance, BaseItem selectToMount)
    {
        Trace($"SelectPartToMount {Item(selectToMount)} partMouseOver {KeyOf(__instance.partMouseOver)} mode {Mode()}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var game = __instance;
        var item = selectToMount;
        Defer("SelectPartToMount", () => game.SelectPartToMount(item), () => $"selectedToMount {Item(GameScript.Get()?.SelectedToMount)}");
        return false;
    }

    [HarmonyPatch(typeof(GameScript), nameof(GameScript.CleanUnfinishedMount))]
    [HarmonyPrefix]
    private static void BeforeCleanUnfinishedMount() => Trace($"CleanUnfinishedMount mode {Mode()}");

    [HarmonyPatch(typeof(GameScript), nameof(GameScript.CleanUnfinishedUnMount))]
    [HarmonyPrefix]
    private static void BeforeCleanUnfinishedUnMount() => Trace($"CleanUnfinishedUnMount mode {Mode()}");

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.UndoMounting))]
    [HarmonyPrefix]
    private static void BeforeUndoMounting(PartScript __instance) => Trace($"UndoMounting {KeyOf(__instance)}");

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.UndoUnMounting))]
    [HarmonyPrefix]
    private static void BeforeUndoUnMounting(PartScript __instance) => Trace($"UndoUnMounting {KeyOf(__instance)}");

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.DoMount))]
    [HarmonyPrefix]
    private static void BeforeDoMount(PartScript __instance) => Trace($"DoMount {KeyOf(__instance)} selectedToMount {Item(GameScript.Get()?.SelectedToMount)}");

    [HarmonyPatch(typeof(PartScript), nameof(PartScript.Hide))]
    [HarmonyPrefix]
    private static void BeforeHide(PartScript __instance) => Trace($"Hide {KeyOf(__instance)}");

    [HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.Show), typeof(Il2CppSystem.Collections.Generic.List<BaseItem>), typeof(ChoosePartUpWindowType))]
    [HarmonyPrefix]
    private static void BeforeChooserShowList(Il2CppSystem.Collections.Generic.List<BaseItem> itemsToShow)
    {
        var items = new List<string>();
        for (int i = 0; itemsToShow != null && i < itemsToShow.Count; i++) items.Add(Item(itemsToShow[i]));
        Trace($"ChoosePartUpWindow.Show(list) [{string.Join("; ", items)}] mode {Mode()}");
    }

    [HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.Show), typeof(string), typeof(ChoosePartUpWindowType))]
    [HarmonyPrefix]
    private static void BeforeChooserShowType(string windowType) => Trace($"ChoosePartUpWindow.Show({windowType}) mode {Mode()}");

    [HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.Hide))]
    [HarmonyPrefix]
    private static void BeforeChooserHide(bool hiddenFromOutside) => Trace($"ChoosePartUpWindow.Hide({hiddenFromOutside}) mode {Mode()} selectedToMount {Item(GameScript.Get()?.SelectedToMount)}");

    [HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.SelectItemInCreateGroup))]
    [HarmonyPrefix]
    private static void BeforeSelectItemInCreateGroup(Item item) => Trace($"SelectItemInCreateGroup {Item(item)}");

    [HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.SubmitGroupItem))]
    [HarmonyPrefix]
    private static void BeforeSubmitGroupItem(ChoosePartUpWindow __instance)
    {
        var selected = __instance.selectedItemsToCreateGroup;
        var items = new List<string>();
        for (int i = 0; selected != null && i < selected.Count; i++) items.Add(Item(selected[i]));
        Trace($"SubmitGroupItem [{string.Join("; ", items)}]");
    }

    [HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.SubmitAction))]
    [HarmonyPrefix]
    private static void BeforeChooserSubmit() => Trace("ChoosePartUpWindow.SubmitAction");

    [HarmonyPatch(typeof(GameMode), nameof(GameMode.SetCurrentMode))]
    [HarmonyPrefix]
    private static void BeforeSetCurrentMode(GameMode __instance, gameMode newGameMode, out string __state) =>
        __state = $"{__instance.currentMode} (previous {__instance.previousMode})";

    [HarmonyPatch(typeof(GameMode), nameof(GameMode.SetCurrentMode))]
    [HarmonyPostfix]
    private static void AfterSetCurrentMode(GameMode __instance, gameMode newGameMode, bool __runOriginal, string __state) =>
        Trace($"SetCurrentMode {__state} -> {newGameMode} ran {__runOriginal} now {__instance.currentMode}");

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Delete), typeof(Item))]
    [HarmonyPrefix]
    private static void BeforeInventoryDelete(Item item) => Trace($"Inventory.Delete {Item(item)}");

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.DeleteGroup), typeof(long))]
    [HarmonyPrefix]
    private static void BeforeInventoryDeleteGroup(long UId) => Trace($"Inventory.DeleteGroup {UId}");

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Add), typeof(Item), typeof(bool))]
    [HarmonyPrefix]
    private static void BeforeInventoryAdd(Item item) => Trace($"Inventory.Add {Item(item)}");

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddGroup), typeof(GroupItem))]
    [HarmonyPrefix]
    private static void BeforeInventoryAddGroup(GroupItem group) => Trace($"Inventory.AddGroup {Item(group)}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), typeof(string))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeTakeOffCarPart(CarLoader __instance, string partName)
    {
        Trace($"TakeOffCarPart({partName}) mode {Mode()}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var carLoader = __instance;
        Defer("TakeOffCarPart", () => carLoader.TakeOffCarPart(partName), () =>
        {
            var part = carLoader.GetCarPart(partName);
            return part == null ? "no part" : $"takeOnOffInProgress {part.TakeOnOffInProgress} unmounted {part.Unmounted}";
        });
        return false;
    }

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.CanTakeOffCarPart))]
    [HarmonyPostfix]
    private static void AfterCanTakeOffCarPart(string name, bool __result)
    {
        Count("CarLoader.CanTakeOffCarPart");
        Trace($"CanTakeOffCarPart({name}) -> {__result}");
    }

    [HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeLifterAction(CarLifter __instance, int actionType)
    {
        Trace($"CarLifter.Action({actionType}) state {__instance.GetState()} moving {__instance.isMoving}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var lifter = __instance;
        Defer("CarLifter.Action", () => lifter.Action(actionType), () => $"moving {lifter.isMoving} state {lifter.GetState()}");
        return false;
    }

    [HarmonyPatch(typeof(FluidRefill), nameof(FluidRefill.Use))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeRefillUse(FluidRefill __instance)
    {
        Trace($"FluidRefill.Use car {(GameScript.Get()?.GetIOMouseOverCarLoader2() == null ? "none" : "yes")} active {__instance.IsActive}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var tool = __instance;
        Defer("FluidRefill.Use", () => tool.Use(), () => $"active {tool.IsActive} fluid {tool.carFluidType}.{tool.fluidId}");
        return false;
    }

    [HarmonyPatch(typeof(FluidRefill), nameof(FluidRefill.Hide))]
    [HarmonyPrefix]
    private static void BeforeRefillHide(FluidRefill __instance) => Trace($"FluidRefill.Hide active {__instance.IsActive} fluid {__instance.carFluidType}.{__instance.fluidId}");

    [HarmonyPatch(typeof(FluidExtractor), nameof(FluidExtractor.Use))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeExtractorUse(FluidExtractor __instance)
    {
        Trace($"FluidExtractor.Use active {__instance.IsActive}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var tool = __instance;
        Defer("FluidExtractor.Use", () => tool.Use(), () => $"active {tool.IsActive} mode {Mode()}");
        return false;
    }

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.UseOilbin))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeUseOilbin(CarLoader __instance)
    {
        Trace($"CarLoader.UseOilbin oil {__instance.FluidsData.Oil?.Level:F2}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var carLoader = __instance;
        Defer("UseOilbin", () => carLoader.UseOilbin(), () => $"oil {carLoader.FluidsData.Oil?.Level:F2}");
        return false;
    }

    [HarmonyPatch(typeof(ToolsManager._UseOilDrain_d__40), nameof(ToolsManager._UseOilDrain_d__40.MoveNext))]
    [HarmonyPostfix]
    private static void AfterOilDrainStep(ToolsManager._UseOilDrain_d__40 __instance, bool __result) =>
        Trace($"UseOilDrain.MoveNext -> {__result} state {__instance.__1__state}");

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ActionUnMountGroup))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeActionUnMountGroup(NotificationCenter __instance, InteractiveObject iO)
    {
        Trace($"ActionUnMountGroup {(iO == null ? "null" : iO.name)}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var center = __instance;
        var io = iO;
        Defer("ActionUnMountGroup", () => center.ActionUnMountGroup(io), () => $"engine on car {(io != null && io.gameObject.activeInHierarchy)}");
        return false;
    }

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.InsertEngineToCar))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool BeforeInsertEngineToCar(NotificationCenter __instance, GroupItem engine)
    {
        Trace($"InsertEngineToCar {Item(engine)}{(bypass ? " (re-invoked)" : "")}");
        if (!reinvoke || bypass) return true;
        var center = __instance;
        var group = engine;
        Defer("InsertEngineToCar", () => center.InsertEngineToCar(group), () => "inserted");
        return false;
    }

    [HarmonyPatch(typeof(NotificationCenter._ChangeCarPos_d__20), nameof(NotificationCenter._ChangeCarPos_d__20.MoveNext))]
    [HarmonyPrefix]
    private static void BeforeChangeCarPosStep(NotificationCenter._ChangeCarPos_d__20 __instance)
    {
        if (__instance.__1__state == 0) Trace($"ChangeCarPos start {__instance.carLoader?.carToLoad} -> {__instance.pos} movePlayer {__instance.movePlayerToCar}");
    }

    private static void Defer(string what, Action action, Func<string> started)
    {
        Trace($"{what} blocked; re-invoking in {ReinvokeDelaySeconds * 1000f:0} ms");
        MelonCoroutines.Start(Reinvoke(what, action, started));
    }

    private static IEnumerator Reinvoke(string what, Action action, Func<string> started)
    {
        float at = Time.realtimeSinceStartup + ReinvokeDelaySeconds;
        while (Time.realtimeSinceStartup < at) yield return null;
        bypass = true;
        string error = null;
        try { action(); }
        catch (Exception e) { error = $"{e.GetType().Name}: {e.Message}"; }
        finally { bypass = false; }
        string state;
        try { state = started(); }
        catch (Exception e) { state = $"state unreadable ({e.GetType().Name})"; }
        Trace($"{what} re-invoked: {(error ?? "ok")}; mode {Mode()}; {state}");
    }

    private static object Relations(int loader, string file)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader) ?? throw new ArgumentException($"no car loader {loader}");
        var registry = PartRegistry.Build(carLoader);
        var relations = LockSets.Build(carLoader, registry);
        var output = new List<string> { $"car {carLoader.carToLoad} fluids [{string.Join(",", relations.AllFluids)}] engineParts {relations.EngineParts.Count}" };
        var sizes = new List<(string Key, string Id, int X, int S)>();
        foreach (string key in registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var script = registry.Sub(key);
            var set = SetFor(relations, key);
            sizes.Add((key, script.id, set.X.Count, set.S.Count));
            var parentFluid = script.transform.parent == null ? null : script.transform.parent.GetComponent<CarFluid>();
            output.Add(string.Join("\t", key, script.id,
                $"main={relations.MainOf[key]}",
                $"members=[{string.Join(",", relations.Members[key])}]",
                $"ancestors=[{string.Join(",", relations.Ancestors[key])}]",
                $"unblocks=[{string.Join(",", (script.unblockOnUnmount ?? new Il2CppReferenceArray<PartScript>(0)).Where(p => p != null).Select(p => registry.TryGetSubPath(p, out var path) ? PartKeys.Sub(path) : $"?{p.id}"))}]",
                $"blocking=[{string.Join(",", relations.BlockingOf(key))}]",
                $"fluids=[{string.Join(",", relations.FluidsOf(key))}]",
                $"parentFluid={(parentFluid == null ? "-" : $"{parentFluid.FluidType}.{parentFluid.ID}")}",
                $"refillLock={script.FluidRefillLockType}",
                $"container={script.IsFluidContainer()}",
                $"onHide={script.sendMessageOnHide}",
                $"sendMessage={script.sendMessage}",
                $"canBeUnmount={script.canBeUnmount}",
                $"oneClick={script.oneClickUnmount}",
                $"group={script.ForcePartGroup}/{script.partProperty?.PartGroup}",
                $"set X{set.X.Count} S{set.S.Count} {set}"));
        }
        for (int i = 0; carLoader.carParts != null && i < carLoader.carParts.Count; i++)
        {
            var part = carLoader.carParts[i];
            var names = new List<string>();
            for (int n = 0; part.ConnectedParts != null && n < part.ConnectedParts.Count; n++) names.Add(part.ConnectedParts[n]);
            output.Add(string.Join("\t", PartKeys.Body(i), part.name, $"connected=[{string.Join(",", names)}]", $"keys=[{string.Join(",", relations.BodyConnected[PartKeys.Body(i)])}]"));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllLines(file, output);
        var largest = sizes.OrderByDescending(s => s.X + s.S).Take(10).Select(s => $"{s.Key}({s.Id}) X{s.X} S{s.S}").ToList();
        return new Dictionary<string, object>
        {
            ["file"] = file,
            ["car"] = carLoader.carToLoad,
            ["parts"] = sizes.Count,
            ["maxKeys"] = sizes.Count == 0 ? 0 : sizes.Max(s => s.X + s.S),
            ["over40"] = sizes.Count(s => s.X + s.S > 40),
            ["mean"] = sizes.Count == 0 ? 0 : Math.Round(sizes.Average(s => s.X + s.S), 1),
            ["largest"] = largest,
            ["withBlocking"] = relations.Blocking.Count,
            ["withFluids"] = relations.Fluids.Count,
            ["fluids"] = relations.AllFluids,
            ["engineParts"] = relations.EngineParts.Count,
        };
    }

    private static LockSet SetFor(CarRelations relations, string key)
    {
        string main = relations.MainOf[key];
        var set = new LockSet { Kind = CarLockKind.PartUnmount };
        set.X.Add(main);
        set.X.AddRange(relations.Members[main].Where(m => m != main));
        var shared = new HashSet<string>();
        foreach (string part in set.X)
        {
            shared.UnionWith(relations.Ancestors[part]);
            shared.UnionWith(relations.BlockingOf(part));
            shared.UnionWith(relations.FluidsOf(part));
            if (relations.EngineParts.Contains(part)) shared.Add(LockKeys.Engine);
        }
        shared.Add(LockKeys.Car);
        set.S.AddRange(shared.Where(k => !set.X.Contains(k)).OrderBy(k => k, StringComparer.Ordinal));
        return set;
    }

    [HarnessCommand("lock-probe")]
    private static object Probe(string args)
    {
        var parts = Args(args);
        if (parts.Length == 0) throw new ArgumentException("usage: lock-probe unmount|mount|bolts|select|group|chooser-close|chooser-submit|body-off|fill|extract|oil|lift|crane-out|mode ...");
        var game = GameScript.Get();
        switch (parts[0])
        {
            case "unmount":
            {
                var (carLoader, script) = Part(parts);
                game.IOMouseOverCarLoader = carLoader;
                if (!parts.Contains("noforce")) ForceCanBeUnmount(script);
                script.ActionUnMount();
                return State();
            }
            case "mount":
            {
                var (carLoader, script) = Part(parts);
                game.IOMouseOverCarLoader = carLoader;
                script.ActionMount(true);
                return State();
            }
            case "part":
            {
                var (_, script) = Part(parts);
                return new Dictionary<string, object>
                {
                    ["id"] = script.id, ["unmounted"] = script.IsUnmounted, ["canBeUnmount"] = script.canBeUnmount, ["blocked"] = script.IsBlocked(),
                    ["bolts"] = (script.MountObjects ?? new Il2CppReferenceArray<MountObject>(0)).Where(m => m != null).Select(m => Math.Round(m.GetMountState(), 2)).ToList(),
                    ["mode"] = Mode(),
                };
            }
            case "bolts":
            {
                var (_, script) = Part(parts);
                bool mount = parts.Contains("mount");
                MelonCoroutines.Start(Bolts(script, mount));
                return new { bolts = script.MountObjects?.Length ?? 0, mount };
            }
            case "select":
            {
                var item = FindItem(long.Parse(parts[1]));
                game.SelectPartToMount(item);
                return State();
            }
            case "group":
            {
                var window = WindowManager.Instance.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
                foreach (string uid in parts.Skip(1)) window.SelectItemInCreateGroup(FindItem(long.Parse(uid)).Cast<Item>());
                window.SubmitGroupItem();
                return State();
            }
            case "chooser-close":
            {
                var window = WindowManager.Instance.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
                window.Hide(false);
                return State();
            }
            case "chooser-submit":
            {
                WindowManager.Instance.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp).SubmitAction();
                return State();
            }
            case "chooser-items":
            {
                var window = WindowManager.Instance.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
                var result = new List<string>();
                var down = window.currentDownItems;
                for (int s = 0; down != null && s < down.Length; s++)
                for (int i = 0; down[s] != null && i < down[s].Count; i++) result.Add($"{s}: {Item(down[s][i])}");
                return result;
            }
            case "body-off":
            {
                var carLoader = Loader(parts[1]);
                var part = carLoader.carParts[int.Parse(parts[2])];
                game.IOMouseOverCarLoader = carLoader;
                carLoader.TakeOffCarPart(part.name);
                return new { part = part.name, inProgress = part.TakeOnOffInProgress, unmounted = part.Unmounted };
            }
            case "fill":
            {
                var tool = FindInScene<FluidRefill>() ?? throw new InvalidOperationException("no FluidRefill in the scene");
                if (parts.Length > 1) game.IOMouseOverCarLoader = Loader(parts[1]);
                tool.Use();
                return new { active = tool.IsActive, fluid = $"{tool.carFluidType}.{tool.fluidId}" };
            }
            case "extract":
            {
                var tool = FindInScene<FluidExtractor>() ?? throw new InvalidOperationException("no FluidExtractor in the scene");
                if (parts.Length > 1) game.IOMouseOverCarLoader = Loader(parts[1]);
                tool.Use();
                return new { active = tool.IsActive, mode = Mode() };
            }
            case "oil":
            {
                var carLoader = Loader(parts[1]);
                carLoader.UseOilbin();
                return new { oil = carLoader.FluidsData.Oil?.Level ?? -1f };
            }
            case "lift":
            {
                var lifter = GarageLoader.Get().carLifter[int.Parse(parts[1])];
                lifter.Action(parts[2] == "up" ? 0 : 1);
                return new { moving = lifter.isMoving, state = lifter.GetState().ToString() };
            }
            case "crane-out":
            {
                var carLoader = Loader(parts[1]);
                NotificationCenter.Get().ActionUnMountGroup(carLoader.e_engine_h.GetComponent<InteractiveObject>());
                return State();
            }
            case "mode":
            {
                if (parts.Length > 2) game.IOMouseOverCarLoader = Loader(parts[2]);
                game.UnmountGroup = 10;
                GameMode.Get().SetCurrentMode((gameMode)Enum.Parse(typeof(gameMode), parts[1], true));
                return State();
            }
            default:
                throw new ArgumentException($"unknown lock-probe action '{parts[0]}'");
        }
    }

    private static IEnumerator Bolts(PartScript script, bool mount)
    {
        float deadline = Time.time + 60f;
        while (Time.time < deadline)
        {
            var bolt = script.MountObjects?.FirstOrDefault(m => m != null && (mount ? m.GetMountState() < 1f : !m.IsUnmounted()));
            if (bolt == null) break;
            bolt.SetCanAction(true);
            bolt.Action();
            yield return null;
        }
        Trace($"bolts done {KeyOf(script)} mount {mount} mode {Mode()} unmounted {script.IsUnmounted}");
    }

    // A disabled PartScript (headless test games) never refreshes canBeUnmount in its Update (row 17 spike 1.3).
    private static void ForceCanBeUnmount(PartScript script)
    {
        if (script.enabled || script.canBeUnmount || script.IsBlocked()) return;
        script.canBeUnmount = true;
        Trace($"forced canBeUnmount on disabled {KeyOf(script)}");
    }

    private static T FindInScene<T>() where T : Component
    {
        foreach (var found in Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<T>()))
        {
            var component = found.TryCast<T>();
            if (component != null && component.gameObject.scene.IsValid()) return component;
        }
        return null;
    }

    private static BaseItem FindItem(long uid)
    {
        var inventory = Singleton<GameManager>.Instance.Inventory;
        var item = inventory.GetItem(uid);
        if (item != null) return item;
        var group = inventory.GetGroup(uid);
        if (group != null) return group;
        throw new ArgumentException($"no item or group {uid}");
    }

    private static CarLoader Loader(string index) =>
        CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(index)) ?? throw new ArgumentException($"no car loader {index}");

    private static (CarLoader, PartScript) Part(string[] parts)
    {
        var carLoader = Loader(parts[1]);
        var script = PartRegistry.Build(carLoader).Sub(parts[2]) ?? throw new ArgumentException($"no part {parts[2]}");
        return (carLoader, script);
    }

    private sealed class Click
    {
        public PartScript Target;
        public string Key;
        public float HoldSeconds;
        public float Started;
        public float DownAt;
        public int DownFrame = -1;
        public int UpFrame = -1;
        public bool Done;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector2 Point;
        public bool Aimed;
        public string Aim;
        public int Frames;

        public object Describe() => new Dictionary<string, object>
        {
            ["key"] = Key, ["holdMs"] = HoldSeconds * 1000f, ["aimed"] = Aimed, ["aim"] = Aim, ["downFrame"] = DownFrame, ["upFrame"] = UpFrame,
            ["done"] = Done, ["frames"] = Frames,
        };
    }

    private static Click click;

    private static bool ClickActive => click != null && !click.Done;

    [HarnessCommand("lock-click")]
    private static object LockClick(string args)
    {
        var parts = Args(args);
        if (parts.Length == 1 && parts[0] == "status") return click?.Describe();
        if (parts.Length < 2) throw new ArgumentException("usage: lock-click <loader> <key> [hold <ms>|click] | status");
        var carLoader = Loader(parts[0]);
        var script = PartRegistry.Build(carLoader).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        int holdIndex = Array.IndexOf(parts, "hold");
        float hold = holdIndex >= 0 && holdIndex + 1 < parts.Length ? float.Parse(parts[holdIndex + 1], CultureInfo.InvariantCulture) / 1000f : 0f;
        bool mount = parts.Contains("mount");
        var raycast = UnityEngine.Object.FindObjectOfType<Raycast>() ?? throw new InvalidOperationException("no Raycast in this scene");
        var game = GameScript.Get();
        game.IOMouseOverCarLoader = carLoader;
        game.UnmountGroup = 10;
        if (!mount) ForceCanBeUnmount(script);
        var mode = GameMode.Get();
        var wanted = mount ? gameMode.PartSelectMount : gameMode.PartSelect;
        if (mode.currentMode != wanted) mode.SetCurrentMode(wanted);

        var camera = raycast.mainCamera ?? Camera.main ?? throw new InvalidOperationException("no camera");
        var next = new Click { Target = script, Key = parts[1], HoldSeconds = hold, Started = Time.realtimeSinceStartup };
        next.Point = new Vector2(camera.pixelWidth / 2f, camera.pixelHeight / 2f);
        int mask = mount ? raycast.partSelectMountRaycastMask.value : raycast.partSelectRaycastMask.value;
        // Headless test games keep every PartScript disabled on layer 28 (PartsDisabled), where the game's part raycast
        // (layers 16 Part and 23) cannot see it; a player's game has the part enabled on layer 16.
        if (script.gameObject.layer == LayerMask.NameToLayer("PartsDisabled"))
        {
            script.gameObject.layer = LayerMask.NameToLayer("Part");
            next.Aim = "moved from PartsDisabled to Part; ";
        }
        if (!script.enabled)
        {
            script.enabled = true;
            next.Aim += "enabled the PartScript; ";
        }
        Aim(next, script, carLoader, mask);
        click = next;
        Trace($"lock-click {parts[1]} ({script.id}) hold {hold * 1000f:0} ms aimed {next.Aimed} {next.Aim} camera {camera.name} {camera.pixelWidth}x{camera.pixelHeight}");
        return next.Describe();
    }

    private static void Aim(Click job, PartScript script, CarLoader carLoader, int mask)
    {
        var collider = script.GetComponent<Collider>();
        if (collider == null)
        {
            job.Aim = "the part has no collider on its own object";
            return;
        }
        Vector3 center = collider.bounds.center;
        Vector3 carCenter = carLoader.transform.position + Vector3.up * 0.6f;
        var directions = new List<Vector3> { (center - carCenter).normalized, Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        foreach (var a in new[] { Vector3.left, Vector3.right })
        foreach (var b in new[] { Vector3.forward, Vector3.back, Vector3.up, Vector3.down })
            directions.Add((a + b).normalized);
        foreach (var direction in directions.Where(d => d.sqrMagnitude > 0.5f))
        foreach (float distance in new[] { 0.4f, 0.8f, 1.4f, 2.2f })
        {
            var position = center + direction * distance;
            var ray = new Ray(position, (center - position).normalized);
            if (!Physics.Raycast(ray, out var hit, 3.4f, mask)) continue;
            if (hit.collider == null || hit.collider.gameObject != script.gameObject) continue;
            job.Position = position;
            job.Rotation = Quaternion.LookRotation(center - position, Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up);
            job.Aimed = true;
            job.Aim += $"from {direction} at {distance} m";
            return;
        }
        var maskLayers = Enumerable.Range(0, 32).Where(l => (mask & (1 << l)) != 0).Select(l => $"{l}={LayerMask.LayerToName(l)}");
        var colliders = script.GetComponentsInChildren<Collider>(true).Take(6)
            .Select(c => $"{c.gameObject.name}:{c.gameObject.layer}={LayerMask.LayerToName(c.gameObject.layer)}{(c.enabled ? "" : "(off)")}{(c.GetComponent<PartScript>() == script ? "*" : "")}");
        var carLayers = (carLoader.root != null ? carLoader.root.transform : carLoader.transform).GetComponentsInChildren<PartScript>(true).GroupBy(p => p.gameObject.layer).Select(g => $"{g.Key}={LayerMask.LayerToName(g.Key)}x{g.Count()}");
        job.Aim += $"no clear line to {collider.name}; mask [{string.Join(",", maskLayers)}]; colliders [{string.Join(", ", colliders)}]; car part layers [{string.Join(", ", carLayers)}]";
    }

    private static void UpdateClick()
    {
        if (!ClickActive) return;
        if (click.UpFrame >= 0 && Time.frameCount > click.UpFrame + 2)
        {
            click.Done = true;
            Trace($"lock-click done ({Hold()}) mode {Mode()}");
            return;
        }
        if (Time.realtimeSinceStartup - click.Started > click.HoldSeconds + 10f)
        {
            click.Done = true;
            Trace("lock-click timed out");
        }
    }

    [HarmonyPatch(typeof(Raycast), nameof(Raycast.Update))]
    [HarmonyPrefix]
    private static void BeforeRaycastUpdate(Raycast __instance)
    {
        if (!ClickActive || !click.Aimed) return;
        var camera = __instance.mainCamera;
        if (camera == null) return;
        camera.transform.SetPositionAndRotation(click.Position, click.Rotation);
        click.Frames++;
    }

    [HarmonyPatch(typeof(ProMouse), nameof(ProMouse.GetLocalMousePosition))]
    [HarmonyPostfix]
    private static void AfterGetLocalMousePosition(ref Vector2 __result)
    {
        if (ClickActive && click.Aimed) __result = click.Point;
    }

    private static bool Pressed(bool down = false)
    {
        if (click.DownFrame < 0)
        {
            if (!down) return false;
            click.DownFrame = Time.frameCount;
            click.DownAt = Time.realtimeSinceStartup;
        }
        if (click.UpFrame < 0 && Time.realtimeSinceStartup - click.DownAt >= click.HoldSeconds && Time.frameCount > click.DownFrame) click.UpFrame = Time.frameCount;
        return click.UpFrame < 0 || Time.frameCount < click.UpFrame;
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GameplayMechanicActionButtonDown))]
    [HarmonyPrefix]
    private static bool BeforeButtonDown(ref bool __result)
    {
        if (!ClickActive || !click.Aimed) return true;
        Pressed(down: true);
        __result = Time.frameCount == click.DownFrame;
        return false;
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GameplayMechanicActionButton))]
    [HarmonyPrefix]
    private static bool BeforeButton(ref bool __result)
    {
        if (!ClickActive || !click.Aimed) return true;
        __result = Pressed();
        return false;
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GameplayMechanicActionButtonUp))]
    [HarmonyPrefix]
    private static bool BeforeButtonUp(ref bool __result)
    {
        if (!ClickActive || !click.Aimed) return true;
        Pressed();
        __result = Time.frameCount == click.UpFrame;
        return false;
    }
}
