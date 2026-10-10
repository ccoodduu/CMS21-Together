using System;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Logic.Tools;
using HarmonyLib;

namespace TogetherTestHarness.Features;

// sync-tuning-bonus-and-new-engines group 4: building a new engine on stand 1 through CreateEngineWindow.CreateEngineAction.
// The game's build waits for the end of a frame between its steps, which never comes in a headless harness game;
// "stand-nofade on" makes SetEngineOnEngineStand run the same coroutine without the fade, stepped every frame as the
// mod's remote put does.
[HarmonyPatch]
public static class EngineBuildCommands
{
    private static bool noFade;

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static EngineStandLogic Stand => ToolsManager.Get()?.EngineStandLogic ?? throw new InvalidOperationException("no engine stand");

    private static string EngineId(string arg)
    {
        if (arg != "auto" && !arg.StartsWith("car:")) return arg;
        int loader = arg.StartsWith("car:") ? int.Parse(arg.Substring(4)) : 0;
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException($"no loaded car on loader {loader}");
        return carLoader.GetEngineName();
    }

    private static GroupItem Build(string id)
    {
        var engine = new Item(id);
        var list = new Il2CppSystem.Collections.Generic.List<Item>();
        list.Add(engine);
        return new GroupItem(id) { ItemList = list, IsNormalGroup = false };
    }

    [HarmonyPatch(typeof(EngineStandLogic), nameof(EngineStandLogic.SetEngineOnEngineStand))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static bool BeforeSetEngine(EngineStandLogic __instance, Item engine)
    {
        if (!noFade || engine == null) return true;
        var group = Build(engine.ID);
        group.ItemList[0] = engine;
        MelonLoader.MelonCoroutines.Start(StepEach(__instance.SetGroupOnEngineStand(group, false)));
        return false;
    }

    private static System.Collections.IEnumerator StepEach(Il2CppSystem.Collections.IEnumerator routine)
    {
        while (routine != null && routine.MoveNext()) yield return null;
    }

    [HarnessCommand("stand-nofade")]
    private static object NoFade(string args)
    {
        noFade = (args ?? "").Trim() == "on";
        return new { noFade };
    }

    [HarnessCommand("stand-engines")]
    private static object Engines(string args)
    {
        var window = WindowManager.Instance?.GetWindowByID<CreateEngineWindow>(WindowID.CreateEngine);
        var ids = new List<string>();
        string error = null;
        try
        {
            var items = window?.GetEngines();
            for (int i = 0; items != null && i < items.Count; i++) ids.Add(items[i].BaseItem?.ID);
        }
        catch (Exception e) { error = e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        string carEngine = null;
        try { carEngine = EngineId("auto"); }
        catch (Exception) { }
        return new { window = window != null, count = ids.Count, carEngine, listed = carEngine != null && ids.Contains(carEngine), error, ids = ids.Take(40).ToList() };
    }

    [HarnessCommand("tool-stand-create")]
    private static object Create(string args)
    {
        var parts = Args(args);
        if (parts.Length != 1) throw new ArgumentException("usage: tool-stand-create <engineId|auto|car:<loader>>");
        string id = EngineId(parts[0]);
        var window = WindowManager.Instance?.GetWindowByID<CreateEngineWindow>(WindowID.CreateEngine) ?? throw new InvalidOperationException("no create engine window");
        var before = Stand.GroupOnEngineStand;
        window.currentEngine = new Item(id);
        window.CreateEngineAction();
        var after = Stand.GroupOnEngineStand;
        return new Dictionary<string, object>
        {
            ["engine"] = id, ["noFade"] = noFade, ["before"] = before?.UID ?? 0, ["after"] = after?.UID ?? 0,
            ["changed"] = (before?.Pointer ?? IntPtr.Zero) != (after?.Pointer ?? IntPtr.Zero), ["money"] = GlobalData.PlayerMoney,
        };
    }

    [HarnessCommand("stand-state")]
    private static object State(string args)
    {
        var logic = Stand;
        var group = logic.GroupOnEngineStand;
        var stand = (EngineStandSync)ToolSync.Machine(ModToolId.EngineStand1);
        return new Dictionary<string, object>
        {
            ["group"] = group?.ID, ["uid"] = group?.UID ?? 0, ["items"] = group?.ItemList?.Count ?? 0, ["engineObject"] = logic.engineGameObject != null,
            ["appliedUid"] = stand?.AppliedUid ?? 0, ["mirrorUid"] = ToolSync.Mirror(ModToolId.EngineStand1).Uid,
            ["parts"] = stand?.Registry?.SubKeys.Count() ?? 0, ["unmountedParts"] = stand?.Registry?.SubKeys.Count(k => stand.Registry.Sub(k).IsUnmounted) ?? 0,
        };
    }

    internal static void Reset(List<string> changed)
    {
        if (noFade) changed.Add("stand-nofade");
        noFade = false;
    }
}
