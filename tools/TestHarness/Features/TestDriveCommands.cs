using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// sync-test-drive-and-diagnostics spike 1.1: a logging-only trace of the test track, path test and dyno flow, patched
// on demand by type name (some targets are nested coroutine types), plus native-only verbs that drive a test drive.
public static class TestDriveCommands
{
    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.testdrive-trace");
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    private static readonly Dictionary<string, string> failures = new Dictionary<string, string>();
    private static readonly Dictionary<MethodBase, string> names = new Dictionary<MethodBase, string>();
    private static bool patched;
    private static bool tracing;

    private static readonly (string Type, string Method)[] Targets =
    {
        ("NotificationCenter", "SelectSceneToLoad"),
        ("CMS.UI.Logic.Map.SideCarsPanel", "DriveAction"),
        ("CMS.UI.Windows.MapWindow", "VerifyCarStateIfInterior"),
        ("CarLoader", "CloseCar"),
        ("GarageLoader", "Save"),
        ("TestTrackManager", "ReturnToGarage"),
        ("TestTrackManager", "DoneTest"),
        ("PrepareCarPhysics", "SaveMileage"),
        ("CMS.UI.Windows.ExamineReportWindow", "GetExaminedParts"),
        ("PartScript", "Examine"),
        ("DynoManager", "RunDyno"),
        ("DynoManager", "PrepareDyno"),
        ("DynoManager", "CloseDyno"),
        ("CarLoader", "MeasurePower"),
        ("PathTestManager", "Prepare"),
        ("PathTestManager", "SetCarPositionAfterLoad"),
        ("PathTestManager+_EndAllTests_d__50", "MoveNext"),
        ("PathTestManager+_ExitFromCar_d__47", "MoveNext"),
        ("GameScript", "ExitFromInterior"),
        ("GlobalData", "Load"),
    };

    [HarnessCommand("testdrive-trace")]
    private static object Trace(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on": PatchAll(); tracing = true; break;
            case "off": tracing = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: testdrive-trace on|off|report");
        }
        return new Dictionary<string, object>
        {
            ["tracing"] = tracing,
            ["counts"] = Targets.Select(t => $"{t.Type}.{t.Method}").ToDictionary(n => n, n => counts.TryGetValue(n, out int c) ? c : 0),
            ["failures"] = failures,
        };
    }

    private static void PatchAll()
    {
        if (patched) return;
        patched = true;
        var prefix = new HarmonyMethod(typeof(TestDriveCommands).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var target in Targets)
        {
            string name = $"{target.Type}.{target.Method}";
            try
            {
                var type = AccessTools.TypeByName(target.Type) ?? throw new TypeLoadException(target.Type);
                var methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == target.Method).ToList();
                if (methods.Count == 0) throw new MissingMethodException(name);
                foreach (var method in methods)
                {
                    names[method] = name;
                    harmony.Patch(method, prefix: prefix);
                }
            }
            catch (Exception e)
            {
                failures[name] = (e.GetType().Name + ": " + e.Message.Split('\n')[0]).Replace("HarmonyException", "patch error");
                MelonLogger.Warning($"[Harness] testdrive-trace cannot patch {name}: {failures[name]}");
            }
        }
    }

    private static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        if (!tracing) return;
        string key = names.TryGetValue(__originalMethod, out string known) ? known : $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        string state = "";
        if (__originalMethod.Name == "MoveNext" && __instance != null)
        {
            try { state = $" state {__instance.GetType().GetProperty("__1__state")?.GetValue(__instance)}"; }
            catch (Exception) { state = " state ?"; }
        }
        string arguments = __args == null ? "" : string.Join(", ", __args.Select(a => a?.ToString() ?? "null"));
        MelonLogger.Msg($"[Harness] testdrive-trace {key}({arguments}){state} NewMileage {GlobalData.NewMileage}, SelectedCarLoader '{GlobalData.SelectedCarLoader}', TestToShow '{GlobalData.TestToShow}' #{counts[key]}");
    }

    private enum HoldMode { Off, Hold, Release, Cancel }

    private static HoldMode holdMode;
    private static bool holdPatched;
    private static int heldFrames;

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("testdrive-trace");
        if (holdMode != HoldMode.Off) changed.Add($"testdrive-hold {holdMode}");
        tracing = false;
        counts.Clear();
        holdMode = HoldMode.Off;
        heldFrames = 0;
    }

    [HarnessCommand("testdrive-hold")]
    private static object Hold(string args)
    {
        if (!holdPatched)
        {
            holdPatched = true;
            var target = AccessTools.Method(typeof(NotificationCenter._SelectSceneToLoad_d__34), "MoveNext");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TestDriveCommands).GetMethod(nameof(HoldPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
        }
        switch ((args ?? "").Trim())
        {
            case "on": holdMode = HoldMode.Hold; heldFrames = 0; break;
            case "release": holdMode = HoldMode.Release; break;
            case "cancel": holdMode = HoldMode.Cancel; break;
            case "off": holdMode = HoldMode.Off; break;
            case "state": break;
            default: throw new ArgumentException("usage: testdrive-hold on|release|cancel|off|state");
        }
        var center = NotificationCenter.m_instance;
        return new
        {
            mode = holdMode.ToString(),
            heldFrames,
            loadingScene = center != null && center.loadingScene,
            gameMode = GameMode.Get()?.GetCurrentMode().ToString(),
            scene = CMS21Together.Data.ClientScene.LocalScene.ToString(),
        };
    }

    private static bool HoldPrefix(NotificationCenter._SelectSceneToLoad_d__34 __instance, ref bool __result)
    {
        if (holdMode == HoldMode.Off || __instance.__1__state != 0 || __instance.sceneType != SceneType.TestTrack) return true;
        switch (holdMode)
        {
            case HoldMode.Hold:
                heldFrames++;
                __result = true;
                return false;
            case HoldMode.Cancel:
                MelonLogger.Msg($"[Harness] testdrive-hold cancelled the departure after {heldFrames} frames");
                holdMode = HoldMode.Off;
                __result = false;
                return false;
            default:
                MelonLogger.Msg($"[Harness] testdrive-hold released the departure after {heldFrames} frames");
                holdMode = HoldMode.Off;
                return true;
        }
    }

    [HarnessCommand("testdrive-go")]
    private static object Go(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        GlobalData.SelectedCarLoader = carLoader.gameObject.name;
        GlobalData.TestToShow = "ExamineReport";
        var center = NotificationCenter.m_instance;
        center.StartCoroutine(center.SelectSceneToLoad("Test_track_1", SceneType.TestTrack, true, true));
        return new { selected = GlobalData.SelectedCarLoader };
    }

    [HarnessCommand("testdrive-drive")]
    private static object Drive(string args)
    {
        var physics = UnityEngine.Object.FindObjectOfType<PrepareCarPhysics>() ?? throw new InvalidOperationException("no PrepareCarPhysics (not on the track)");
        physics.mileage += float.Parse((args ?? "0").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        return new { physics.mileage };
    }

    private static CarLoader LoadedCar(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        return carLoader;
    }

    private static Dictionary<string, object> CarState(CarLoader carLoader)
    {
        var engine = carLoader.EngineData;
        return new Dictionary<string, object>
        {
            ["specialState"] = carLoader.specialState,
            ["measuredDragIndex"] = carLoader.MeasuredDragIndex,
            ["engine"] = $"peak {engine.peakRpm}/{engine.peakRpmTorque} max {engine.maxRpm} tuning {engine.tuningValue} measured {engine.measured}",
            ["mode"] = GameMode.Get().GetCurrentMode().ToString(),
        };
    }

    [HarnessCommand("dyno-run")]
    private static object Dyno(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: dyno-run <loader> start|measure|close|state");
        var carLoader = LoadedCar(parts[0]);
        var manager = DynoManager.Get() ?? throw new InvalidOperationException("no DynoManager");
        var window = UnityEngine.Object.FindObjectOfType<CMS.UI.Windows.DynoWindow>();
        switch (parts[1])
        {
            case "start":
                manager.CarLoader = carLoader;
                manager.RunDyno();
                break;
            case "measure":
                (window ?? throw new InvalidOperationException("no DynoWindow")).StartAction();
                break;
            case "close":
                (window ?? throw new InvalidOperationException("no DynoWindow")).HideAction();
                break;
            case "state": break;
            default: throw new ArgumentException("usage: dyno-run <loader> start|measure|close|state");
        }
        var state = CarState(carLoader);
        state["dynoMeasured"] = manager.DynoMeasured;
        state["haveJob"] = manager.haveJob;
        state["job"] = manager.job == null ? "none" : manager.job.id.ToString();
        return state;
    }

    [HarnessCommand("pathtest-run")]
    private static object PathTest(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: pathtest-run <loader> prepare|claim|end|exit|report|state");
        var carLoader = LoadedCar(parts[0]);
        var manager = PathTestManager.Get() ?? throw new InvalidOperationException("no PathTestManager");
        switch (parts[1])
        {
            case "prepare":
                manager.testIsComplete = false;
                manager.carLoader = carLoader;
                carLoader.specialState = 0;
                CMS21Together.Logic.Car.Away.CarAwaySync.LastBlocked = null;
                manager.Prepare();
                break;
            case "end":
                manager.StartCoroutine(manager.EndAllTests());
                break;
            case "exit":
                manager.StartCoroutine(manager.ExitFromCar());
                break;
            case "claim":
                CMS21Together.Logic.Car.Away.PathTestSync.RequestClaim(int.Parse(parts[0]));
                break;
            case "report":
                CMS21Together.Logic.Car.Away.PathTestSync.ReleaseAfterReport();
                break;
            case "state": break;
            default: throw new ArgumentException("usage: pathtest-run <loader> prepare|claim|end|exit|report|state");
        }
        var state = CarState(carLoader);
        state["testIsComplete"] = manager.testIsComplete;
        state["inProgress"] = manager.InProgress;
        state["blocked"] = CMS21Together.Logic.Car.Away.CarAwaySync.LastBlocked;
        return state;
    }

    [HarnessCommand("diag-examine")]
    private static object DiagExamine(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 && (parts.Length != 3 || parts[2] != "keys")) throw new ArgumentException("usage: diag-examine <loader> <ToolType> [keys]");
        var carLoader = LoadedCar(parts[0]);
        var tool = (ToolType)Enum.Parse(typeof(ToolType), parts[1]);
        var toExamine = CarHelper.GetPartsToExamine(carLoader, tool);
        int count = toExamine?.Count ?? 0;
        if (parts.Length == 3)
        {
            var registry = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader);
            var keys = new List<string>();
            for (int i = 0; i < count; i++)
                if (registry.TryGetSubPath(toExamine[i], out var path)) keys.Add(CMS21_Together_Core.Network.Packets.PartKeys.Sub(path));
            return new { tool = tool.ToString(), keys = keys.OrderBy(k => k, StringComparer.Ordinal).ToList() };
        }
        for (int i = 0; i < count; i++) toExamine[i].Examine(true);
        return new { tool = tool.ToString(), examined = count };
    }

    [HarnessCommand("testdrive-skip-result")]
    private static object SkipResult(string args)
    {
        CMS21Together.Logic.Car.Away.TestDriveSync.SkipNextResult = (args ?? "").Trim() == "on";
        return new { skip = CMS21Together.Logic.Car.Away.TestDriveSync.SkipNextResult, newMileage = GlobalData.NewMileage };
    }

    [HarnessCommand("away-try")]
    private static object AwayTry(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new ArgumentException("usage: away-try <loader> unmount <key>|move <CarPlace>");
        var carLoader = LoadedCar(parts[0]);
        CMS21Together.Logic.Car.Away.CarAwaySync.LastBlocked = null;
        switch (parts[1])
        {
            case "unmount":
                var script = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader).Sub(parts[2]) ?? throw new ArgumentException($"no part {parts[2]}");
                script.ActionUnMount();
                break;
            case "move":
                var center = NotificationCenter.Get();
                center.StartCoroutine(center.ChangeCarPos(carLoader, (CarPlace)Enum.Parse(typeof(CarPlace), parts[2]), false));
                break;
            default: throw new ArgumentException("usage: away-try <loader> unmount <key>|move <CarPlace>");
        }
        string blocked = CMS21Together.Logic.Car.Away.CarAwaySync.LastBlocked;
        return new { blocked = blocked != null, what = blocked };
    }

    [HarnessCommand("testdrive-partnames")]
    private static object PartNames(string args)
    {
        var places = CarLoaderPlaces.Get();
        var carLoader = places != null
            ? places.GetCarLoaderByIndex(int.Parse((args ?? "0").Trim()))
            : UnityEngine.Object.FindObjectOfType<CarLoader>();
        if (carLoader == null || carLoader.carParts == null) throw new InvalidOperationException("no car");
        var names = new List<string>();
        for (int i = 0; i < carLoader.carParts.Count; i++) names.Add(carLoader.carParts[i].name);
        return new { onTrack = places == null, count = names.Count, names = string.Join(",", names) };
    }

    [HarnessCommand("testdrive-finish")]
    private static object Finish(string args)
    {
        var manager = UnityEngine.Object.FindObjectOfType<TestTrackManager>() ?? throw new InvalidOperationException("no TestTrackManager (not on the track)");
        if ((args ?? "").Trim() == "all")
            for (int i = 0; manager.ListOfTestOnTestTrack != null && i < manager.ListOfTestOnTestTrack.Count; i++) manager.DoneTest(i);
        if (CMS21Together.Data.ClientScene.LocalScene == CMS21_Together_Core.Data.Enum.GameScene.Loading) return "returned by the last test";
        manager.ReturnToGarage();
        return "returning";
    }
}
