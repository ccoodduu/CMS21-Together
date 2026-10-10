using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Visuals;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TogetherTestHarness.Features;

// remote-fluid-visuals harness: vfx-fluids reports the fluid replays by what is in the scene (copies named
// TogetherFluid*, their particles and sound, the traced SoundManager loop calls), so it reads the same on code
// without the replay. probe lists the game's own drain and refill objects (spike runtime facts).
[HarmonyPatch]
public static class VisualFluidCommands
{
    private const string CopyPrefix = "TogetherFluid";
    private const string PlugPath = "korek_spustowy_1(0)";
    private const int TraceLimit = 500;

    private static readonly List<object> sounds = new List<object>();
    private static bool tracing;

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("vfx-fluids off");
        tracing = false;
        sounds.Clear();
    }

    [HarnessCommand("vfx-fluids")]
    private static object Fluids(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        switch (parts.Length > 0 ? parts[0] : "")
        {
            case "on":
                tracing = true;
                sounds.Clear();
                return new { tracing };
            case "off":
                tracing = false;
                return new { tracing };
            case "report":
                return Report(parts.Length > 1 ? int.Parse(parts[1]) : -1);
            case "probe":
                return Probe();
            default:
                throw new ArgumentException("usage: vfx-fluids on|off|report [loader]|probe");
        }
    }

    private static Dictionary<string, object> Report(int loader)
    {
        var copies = new List<object>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                if (root != null && root.name.StartsWith(CopyPrefix, StringComparison.Ordinal) && !root.name.EndsWith("Staging", StringComparison.Ordinal))
                    copies.Add(Describe(root));
        }
        var plug = loader < 0 ? null : CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader)?.e_engine_h?.transform.Find(PlugPath);
        var plugRenderers = plug == null ? new List<Renderer>() : plug.GetComponentsInChildren<Renderer>(true).ToList();
        return new Dictionary<string, object>
        {
            ["effects"] = VisualScope.Effects.Where(e => !e.Ended && (e.Kind.ToString() == "Drain" || e.Kind.ToString() == "Pour")).Select(e => (object)new
            {
                kind = e.Kind.ToString(), loader = e.Loader, key = e.Key, playerId = e.PlayerId, phase = e.Phase,
            }).ToList(),
            ["copies"] = copies,
            ["sounds"] = new List<object>(sounds),
            ["plug"] = plug == null ? null : new
            {
                active = plug.gameObject.activeSelf,
                renderers = plugRenderers.Count,
                hidden = plugRenderers.Count(r => r.forceRenderingOff),
            },
            ["renderersHidden"] = VisualScope.RenderersHidden,
            ["started"] = VisualScope.Started.ToDictionary(p => p.Key, p => p.Value),
            ["finished"] = VisualScope.Finished.ToDictionary(p => p.Key, p => p.Value),
            ["skipped"] = VisualScope.Skipped.ToDictionary(p => p.Key, p => p.Value),
            ["leaks"] = VisualScope.Leaks,
        };
    }

    private static object Describe(GameObject root)
    {
        return new
        {
            name = root.name,
            active = root.activeInHierarchy,
            position = Vec(root.transform.position),
            behaviours = root.GetComponentsInChildren<MonoBehaviour>(true).Count(),
            particles = root.GetComponentsInChildren<ParticleSystem>(true).ToArray().Select(p => (object)new
            {
                name = p.name, playing = p.isPlaying, emitting = p.isEmitting, alive = p.IsAlive(false), count = p.particleCount,
            }).ToList(),
            audio = root.GetComponentsInChildren<AudioSource>(true).ToArray().Select(a => (object)new
            {
                playing = a.isPlaying, clip = a.clip == null ? null : a.clip.name,
            }).ToList(),
        };
    }

    private static Dictionary<string, object> Probe()
    {
        var tools = ToolsManager.Get() ?? throw new InvalidOperationException("no ToolsManager in this scene");
        var refills = new Dictionary<string, object>();
        foreach (var (name, refill) in new[]
                 {
                     ("OilRefill", tools.OilRefill), ("BrakeRefill", tools.BrakeRefill), ("CoolantRefill", tools.CoolantRefill),
                     ("WindscreenWashRefill", tools.WindscreenWashRefill), ("PowerSteeringRefill", tools.PowerSteeringRefill),
                 })
        {
            var logic = refill == null ? null : refill.fluidRefillLogic;
            refills[name] = refill == null ? null : new
            {
                tool = Tree(refill.gameObject),
                logic = logic == null ? null : Tree(logic.gameObject),
                logicIsChildOfTool = logic != null && logic.transform.IsChildOf(refill.transform),
                puszka = logic?.puszka == null ? null : logic.puszka.name,
                emit = logic?.emit == null ? null : logic.emit.name,
                emitFull = logic?.emitFull == null ? null : logic.emitFull.name,
            };
        }
        return new Dictionary<string, object>
        {
            ["gameVolume"] = GameSettings.AudioSettingsData.GameVolume,
            ["oilDrain"] = tools.Oil_drain_h == null ? null : Tree(tools.Oil_drain_h),
            ["refills"] = refills,
        };
    }

    private static List<object> Tree(GameObject root)
    {
        var lines = new List<object>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var components = t.GetComponents<Component>().ToArray().Where(c => c != null).Select(c => c.GetIl2CppType().Name).Where(n => n != "Transform");
            lines.Add($"{Path(root.transform, t)} [{string.Join(", ", components)}] layer {t.gameObject.layer} {(t.gameObject.activeSelf ? "on" : "off")}");
        }
        return lines;
    }

    private static string Path(Transform root, Transform t)
    {
        var names = new List<string>();
        for (var c = t; c != null && c != root.parent; c = c.parent) names.Add(c.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static float[] Vec(Vector3 v) => new[] { (float)Math.Round(v.x, 3), (float)Math.Round(v.y, 3), (float)Math.Round(v.z, 3) };

    private static void Note(object entry)
    {
        if (!tracing) return;
        sounds.Add(entry);
        if (sounds.Count > TraceLimit) sounds.RemoveAt(0);
    }

    [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PlayLoopSFX))]
    [HarmonyPostfix]
    private static void AfterPlayLoop(GameObject go, string soundName) =>
        Note(new { call = "PlayLoopSFX", go = go == null ? null : go.name, sound = soundName, t = Math.Round(Time.realtimeSinceStartup, 2) });

    [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.StopLoopSFX))]
    [HarmonyPostfix]
    private static void AfterStopLoop(GameObject go, bool playEnd) =>
        Note(new { call = "StopLoopSFX", go = go == null ? null : go.name, playEnd, t = Math.Round(Time.realtimeSinceStartup, 2) });
}
