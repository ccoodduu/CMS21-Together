using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace TogetherTestHarness.Features;

// The wear a part shows: the RustWeight its renderers' materials carry, read from the renderers PartScript.UpdateShaderParams
// writes to (its own, not a child part's, layer 15 skipped). "refresh" then runs UpdateShaderParams, as F7 does, and
// reads them again, so a scenario can tell stale values from current ones.
public static class PartShaderCommands
{
    private const int SkippedLayer = 15;

    [HarnessCommand("part-shader")]
    private static object PartShader(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("usage: part-shader <loader> <key> [refresh]");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        var script = PartRegistry.Build(carLoader).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        var result = new Dictionary<string, object>
        {
            ["key"] = parts[1],
            ["id"] = PartApplier.EffectiveId(script),
            ["condition"] = Math.Round(script.Condition, 3),
            ["unmounted"] = script.IsUnmounted,
            ["rust"] = RustWeights(script),
        };
        if (parts.Contains("refresh"))
        {
            script.UpdateShaderParams(true);
            result["fresh"] = RustWeights(script);
        }
        return result;
    }

    private static List<double> RustWeights(PartScript script)
    {
        int rust = CMS.ShaderProperties.RustWeight;
        var weights = new List<double>();
        foreach (var renderer in script.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.gameObject.layer == SkippedLayer) continue;
            var owner = renderer.gameObject.GetComponent<PartScript>();
            if (owner != null && owner.Pointer != script.Pointer) continue;
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.HasProperty(rust)) weights.Add(Math.Round(material.GetFloat(rust), 3));
        }
        return weights;
    }
}
