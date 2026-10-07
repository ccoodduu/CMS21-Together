using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace TogetherTestHarness.Features;

// sync-players-and-scenes spike 5.1: a logging-only trace of sitting in a car and the engine, patched on demand by
// type name (the coroutine bodies are nested types).
public static class SeatEngineCommands
{
    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.seat-trace");
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    private static readonly Dictionary<string, string> failures = new Dictionary<string, string>();
    private static readonly Dictionary<MethodBase, string> names = new Dictionary<MethodBase, string>();
    private static bool patched;
    private static bool tracing;

    private static readonly (string Type, string Method)[] Targets =
    {
        ("GameScript", "SitInside"),
        ("GameScript+_SitInside_d__104", "MoveNext"),
        ("GameScript", "ExitFromInterior"),
        ("GameScript+_ExitFromInterior_d__105", "MoveNext"),
        ("EngineAudioController", "StartIgnition"),
        ("EngineAudioController+_StartIgnition_d__35", "MoveNext"),
        ("EngineAudioController", "EngineStop"),
        ("GameMode", "SetCurrentMode"),
    };

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("seat-trace");
        tracing = false;
        counts.Clear();
    }

    [HarnessCommand("seat-trace")]
    private static object Trace(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on": PatchAll(); tracing = true; break;
            case "off": tracing = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: seat-trace on|off|report");
        }
        return new Dictionary<string, object>
        {
            ["tracing"] = tracing,
            ["counts"] = Targets.Select(t => $"{t.Type}.{t.Method}").ToDictionary(n => n, n => counts.TryGetValue(n, out int c) ? c : 0),
            ["failures"] = failures,
            ["state"] = GameState(),
        };
    }

    private static void PatchAll()
    {
        if (patched) return;
        patched = true;
        var prefix = new HarmonyMethod(typeof(SeatEngineCommands).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
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
                MelonLogger.Warning($"[Harness] seat-trace cannot patch {name}: {failures[name]}");
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
        string arguments = __args == null ? "" : string.Join(", ", __args.Select(Describe));
        var game = GameState();
        MelonLogger.Msg($"[Harness] seat-trace {key}({arguments}){state} mode {game["mode"]}, inInterior {game["inInterior"]}, engine {game["engine"]} #{counts[key]}");
    }

    [HarnessCommand("sit")]
    private static object Sit(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[1] != "left" && parts[1] != "right") throw new ArgumentException("usage: sit <carLoaderId> left|right");
        var carLoader = LoadedCar(parts[0]);
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");
        game.StartCoroutine(game.SitInside(carLoader, parts[1] == "left", gameMode.Interior));
        return new { loader = int.Parse(parts[0]), side = parts[1] };
    }

    [HarnessCommand("stand")]
    private static object Stand(string args)
    {
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");
        game.StartCoroutine(game.ExitFromInterior(true));
        return GameState();
    }

    [HarnessCommand("engine")]
    private static object Engine(string args)
    {
        var controller = Singleton<GameManager>.Instance?.EngineAudioController ?? throw new InvalidOperationException("no EngineAudioController");
        switch ((args ?? "").Trim())
        {
            case "on":
                var game = GameScript.Get();
                int loader = CMS21Together.Logic.Player.SeatEngine.SeatCarLoaderId;
                if (loader < 0) throw new InvalidOperationException("not seated in a car");
                bool seatedMode = game != null && game.inInterior;
                controller.StartCoroutine(controller.StartIgnition(seatedMode, LoadedCar(loader.ToString()), 0f));
                break;
            case "off":
                controller.EngineStop();
                break;
            default: throw new ArgumentException("usage: engine on|off");
        }
        return GameState();
    }

    private static CarLoader LoadedCar(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad) || !carLoader.IsCarLoaded()) throw new ArgumentException($"no loaded car on loader {index}");
        return carLoader;
    }

    private static string Describe(object value)
    {
        if (value is CarLoader carLoader) return $"loader {CarLoaderPlaces.Get()?.GetCarLoaderId(carLoader).ToString() ?? "?"} '{carLoader.carToLoad}'";
        return value?.ToString() ?? "null";
    }

    internal static Dictionary<string, object> GameState()
    {
        var game = GameScript.Get();
        var controller = Singleton<GameManager>.Instance?.EngineAudioController;
        string engine = "none";
        if (controller != null)
        {
            var car = controller.carLoader;
            int loader = car == null ? -1 : CarLoaderPlaces.Get()?.GetCarLoaderId(car) ?? -1;
            engine = $"{(controller.GetEngineStartingOrWorking() ? "on" : "off")} loader {loader} rpm {controller.CurrentRpm:F0} idle {controller.IdleRpm:F0}";
        }
        return new Dictionary<string, object>
        {
            ["mode"] = GameMode.Get()?.GetCurrentMode().ToString() ?? "none",
            ["inInterior"] = game != null && game.inInterior,
            ["engine"] = engine,
        };
    }

    [HarnessCommand("audio-clips")]
    private static object AudioClips(string args)
    {
        string filter = (args ?? "").Trim().ToLowerInvariant();
        var clips = UnityEngine.Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<UnityEngine.AudioClip>());
        var found = new List<string>();
        for (int i = 0; clips != null && i < clips.Length; i++)
            if (clips[i].name.ToLowerInvariant().Contains(filter)) found.Add(clips[i].name);
        var sounds = UnityEngine.Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<RealisticEngineSound>());
        return new { count = found.Count, names = found.Distinct().OrderBy(n => n).Take(80).ToList(), engineSounds = sounds?.Length ?? 0 };
    }
}
