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

    // vfx-pour drives a refill on the actor as a player does: the cap opens (ActionAutomatic), ToolsManager.Use through
    // the lock gate, the pour button held by keeping FluidRefillLogic.power at 1, then the tool put away.
    [HarnessCommand("vfx-pour")]
    private static object Pour(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new ArgumentException("usage: vfx-pour <loader> <ToolType> start|hold <seconds>|end|status");
        int loader = int.Parse(parts[0]);
        var type = (ToolType)Enum.Parse(typeof(ToolType), parts[1], true);
        var tools = ToolsManager.Get() ?? throw new InvalidOperationException("no ToolsManager in this scene");
        var refill = RefillOf(tools, type) ?? throw new ArgumentException($"{type} is not a refill can");
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader) ?? throw new ArgumentException($"no car loader {loader}");
        var logic = refill.fluidRefillLogic;
        switch (parts[2])
        {
            case "start":
            {
                var fluid = refill.carFluidType;
                var registry = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader);
                string capKey = registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault(k =>
                {
                    var container = registry.Sub(k).transform.parent?.GetComponent<CarFluid>();
                    return container != null && container.FluidType == fluid && container.ID == 0;
                });
                var cap = capKey == null ? null : registry.Sub(capKey);
                if (cap == null) throw new InvalidOperationException($"no cap in a {fluid} container");
                carLoader.CurrentUsedFluid = fluid;
                carLoader.CurrentUsedFluidId = 0;
                GameScript.Get().IOMouseOverCarLoader = carLoader;
                tools.ItemWorkOn = cap.gameObject;
                bool opened = false;
                if (!cap.IsUnmounted)
                {
                    cap.StartCoroutine(cap.ActionAutomatic());
                    opened = true;
                }
                tools.Use(type);
                return new { capKey, cap = cap.id, opened, fluid = fluid.ToString(), active = tools.ToolIsActive };
            }
            case "hold":
            {
                float seconds = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                MelonLoader.MelonCoroutines.Start(Hold(logic, seconds));
                return PourStatus(tools, refill, carLoader);
            }
            case "end":
                refill.Hide();
                tools.HideTool();
                return PourStatus(tools, refill, carLoader);
            case "status":
                return PourStatus(tools, refill, carLoader);
            default:
                throw new ArgumentException("usage: vfx-pour <loader> <ToolType> start|hold <seconds>|end|status");
        }
    }

    private static System.Collections.IEnumerator Hold(FluidRefillLogic logic, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until && logic != null)
        {
            logic.power = 1f;
            yield return null;
        }
    }

    private static object PourStatus(ToolsManager tools, FluidRefill refill, CarLoader carLoader)
    {
        var logic = refill.fluidRefillLogic;
        var cap = tools.ItemWorkOn == null ? null : tools.ItemWorkOn.GetComponent<PartScript>();
        return new
        {
            toolActive = tools.ToolIsActive,
            used = tools.currentUsedTool.ToString(),
            logicActive = logic != null && logic.gameObject.activeSelf,
            canUse = logic != null && logic.canUse,
            power = logic == null ? 0f : logic.power,
            capUnmounted = cap != null && cap.IsUnmounted,
            capAnimationDone = cap != null && cap.MountAnimationCompleted,
            level = carLoader.FluidsData.GetLevel(refill.carFluidType, 0, false),
            logicPosition = logic == null ? null : Vec(logic.transform.position),
            capPosition = cap == null ? null : Vec(cap.transform.position),
        };
    }

    private static FluidRefill RefillOf(ToolsManager tools, ToolType type) => type switch
    {
        ToolType.OilRefill => tools.OilRefill,
        ToolType.BrakeRefill => tools.BrakeRefill,
        ToolType.CoolantRefill => tools.CoolantRefill,
        ToolType.WindscreenWashRefill => tools.WindscreenWashRefill,
        ToolType.PowerSteeringRefill => tools.PowerSteeringRefill,
        _ => null
    };

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
