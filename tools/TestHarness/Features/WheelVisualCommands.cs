using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace TogetherTestHarness.Features;

// Wheel looks, measured instead of seen (headless games render nothing): the meshes, bones and materials of every
// rim and tire renderer, the car's WheelsData, and the TogetherGhost objects alive in the scene.
public static class WheelVisualCommands
{
    private const string ErrorShader = "Hidden/InternalErrorShader";

    private static CarLoader Loader(string index) =>
        CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(index)) ?? throw new ArgumentException($"no loader {index}");

    [HarnessCommand("wheel-visuals")]
    private static object WheelVisuals(string args)
    {
        var carLoader = Loader((args ?? "").Trim());
        var registry = PartRegistry.Build(carLoader);
        var parts = registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => (Key: k, Script: registry.Sub(k)))
            .Where(p => PartApplier.IsWheelPart(p.Script))
            .Select(p => (object)Part(p.Key, p.Script))
            .ToList();
        var wheels = carLoader.WheelsData?.Wheels;
        var data = new List<object>();
        for (int i = 0; wheels != null && i < wheels.Length; i++)
        {
            var w = wheels[i];
            data.Add(new { index = i, rim = w.Rim, tire = w.Tire, width = w.Width, size = w.Size, profile = w.Profile, et = w.ET });
        }
        return new Dictionary<string, object> { ["parts"] = parts, ["wheelsData"] = data, ["ghosts"] = Ghosts(), ["prefabs"] = Prefabs() };
    }

    // The rim and tire prefabs the game copies meshes and materials from; a destroyed material here stays broken
    // for every later wheel of that type until the game restarts.
    private static Dictionary<string, object> Prefabs()
    {
        var result = new Dictionary<string, object>();
        foreach (string id in new[] { "rim_atlanta", "rim_3", "tire_vintage", "tire_standard" })
        {
            var prefab = Resources.Load<GameObject>(id);
            result[id] = prefab == null ? null : prefab.GetComponentsInChildren<Renderer>(true)
                .Select(r => $"{r.name}:" + string.Join("+", r.sharedMaterials.Select(m => m == null ? "<null>" : $"{m.name}|{m.GetInstanceID()}")))
                .ToList();
        }
        return result;
    }

    private static Dictionary<string, object> Part(string key, PartScript script)
    {
        var childParts = new HashSet<int>();
        foreach (var child in script.GetComponentsInChildren<PartScript>(true))
            if (child.GetInstanceID() != script.GetInstanceID())
                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true)) childParts.Add(renderer.GetInstanceID());
        var renderers = script.GetComponentsInChildren<Renderer>(true)
            .Where(r => !childParts.Contains(r.GetInstanceID()))
            .Select(r => (object)Renderer(r))
            .ToList();
        return new Dictionary<string, object>
        {
            ["key"] = key,
            ["id"] = script.id,
            ["tuned"] = script.tunedID ?? "",
            ["group"] = script.partProperty?.SpecialGroup.ToString(),
            ["unmounted"] = script.IsUnmounted,
            ["active"] = script.gameObject.activeInHierarchy,
            ["localScale"] = Round(script.transform.localScale),
            ["position"] = Round(script.transform.position),
            ["renderers"] = renderers,
        };
    }

    private static Dictionary<string, object> Renderer(Renderer renderer)
    {
        var skinned = renderer.TryCast<SkinnedMeshRenderer>();
        var mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        var result = new Dictionary<string, object>
        {
            ["name"] = renderer.name,
            ["skinned"] = skinned != null,
            ["enabled"] = renderer.enabled,
            ["forceOff"] = renderer.forceRenderingOff,
            ["active"] = renderer.gameObject.activeInHierarchy,
            ["mesh"] = mesh == null ? null : mesh.name,
            ["vertices"] = mesh == null ? 0 : mesh.vertexCount,
            ["meshId"] = mesh == null ? 0 : mesh.GetInstanceID(),
            ["localScale"] = Round(renderer.transform.localScale),
            ["localPosition"] = Round(renderer.transform.localPosition),
            ["meshSize"] = mesh == null ? null : Round(mesh.bounds.size),
            ["scale"] = Round(renderer.transform.lossyScale),
            ["materials"] = renderer.sharedMaterials.Select(m => m == null ? "<null>" : $"{m.name}|{(m.shader == null ? "<no shader>" : m.shader.name)}|{m.GetInstanceID()}").ToList(),
            ["magenta"] = renderer.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == ErrorShader),
        };
        if (skinned != null)
        {
            var bones = skinned.bones;
            result["bones"] = bones?.Length ?? 0;
            result["nullBones"] = bones == null ? 0 : bones.Count(b => b == null);
            result["bindposes"] = mesh == null ? 0 : mesh.bindposes.Length;
            result["rootBone"] = skinned.rootBone == null ? null : skinned.rootBone.name;
            int shapes = mesh == null ? 0 : mesh.blendShapeCount;
            result["blendShapes"] = Enumerable.Range(0, shapes).Select(i => $"{mesh.GetBlendShapeName(i)}={Math.Round(skinned.GetBlendShapeWeight(i), 2)}").ToList();
        }
        return result;
    }

    private static Dictionary<string, object> Ghosts()
    {
        var ghosts = new List<object>();
        foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
        {
            if (go == null || !go.name.StartsWith("TogetherGhost[")) continue;
            ghosts.Add(new { name = go.name, parent = go.transform.parent == null ? null : go.transform.parent.name, active = go.activeInHierarchy, position = Round(go.transform.position), scale = Round(go.transform.localScale), pieces = go.transform.childCount });
        }
        return new Dictionary<string, object> { ["count"] = ghosts.Count, ["roots"] = ghosts };
    }

    private static float[] Round(Vector3 v) => new[] { (float)Math.Round(v.x, 3), (float)Math.Round(v.y, 3), (float)Math.Round(v.z, 3) };
}
