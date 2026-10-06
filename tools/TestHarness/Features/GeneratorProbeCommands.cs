using System.Collections.Generic;
using UnityEngine;

namespace TogetherTestHarness.Features;

// Spike for the server-hosted generator client (docs/spikes/generator-client.md §5): reads the order list and turns
// rendering down, so a measurement script can compare RAM/CPU per launch variant.
public static class GeneratorProbeCommands
{
    [HarnessCommand("gen-probe")]
    private static object GenProbe(string args)
    {
        var generator = Object.FindObjectOfType<OrderGenerator>();
        var jobs = new List<object>();
        var list = generator?.Jobs;
        for (int i = 0; list != null && i < list.Count; i++)
            jobs.Add(new { list[i].id, car = list[i].carFile, list[i].forXP, timeToEnd = Mathf.Round(list[i].timeToEnd) });
        return new Dictionary<string, object>
        {
            ["generator"] = generator != null,
            ["jobs"] = jobs,
            ["time"] = Mathf.Round(Time.realtimeSinceStartup),
            ["fps"] = Mathf.Round(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)),
            ["graphics"] = SystemInfo.graphicsDeviceType.ToString(),
        };
    }

    [HarnessCommand("lowfx")]
    private static object LowFx(string args)
    {
        int cameras = 0;
        foreach (var camera in Camera.allCameras)
        {
            camera.enabled = false;
            cameras++;
        }
        QualitySettings.SetQualityLevel(0, true);
        Application.runInBackground = true;
        Application.targetFrameRate = int.TryParse((args ?? "").Trim(), out int fps) && fps > 0 ? fps : 15;
        return new { cameras, targetFrameRate = Application.targetFrameRate };
    }
}
