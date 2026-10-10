using System;
using System.Collections.Generic;
using CMS21Together.Data;
using UnityEngine;

namespace TogetherTestHarness.Features;

public static class PerfCommands
{
    private const float WindowSeconds = 10f;
    private const int Capacity = 32768;

    private static readonly float[] frameTimes = new float[Capacity];
    private static readonly float[] frameDeltas = new float[Capacity];
    private static readonly float[] sorted = new float[Capacity];
    private static int next;
    private static int count;
    private static float lastFrameAt = -1f;

    internal static void RecordFrame()
    {
        float now = Time.realtimeSinceStartup;
        StackSampler.Beat();
        frameTimes[next] = now;
        frameDeltas[next] = lastFrameAt < 0f ? 0f : now - lastFrameAt;
        lastFrameAt = now;
        next = (next + 1) % Capacity;
        if (count < Capacity) count++;
    }

    [HarnessCommand("perf")]
    private static object Perf(string args)
    {
        float since = Time.realtimeSinceStartup - WindowSeconds;
        int frames = 0;
        double total = 0;
        float max = 0;
        for (int i = 0; i < count; i++)
        {
            int index = (next - 1 - i + Capacity) % Capacity;
            if (frameTimes[index] < since) break;
            float delta = frameDeltas[index];
            sorted[frames++] = delta;
            total += delta;
            if (delta > max) max = delta;
        }
        Array.Sort(sorted, 0, frames);
        float p95 = frames == 0 ? 0 : sorted[Math.Min(frames - 1, (int)Math.Ceiling(frames * 0.95) - 1)];

        return new Dictionary<string, object>
        {
            ["windowS"] = WindowSeconds,
            ["frames"] = frames,
            ["avgMs"] = frames == 0 ? 0 : Math.Round(total / frames * 1000, 2),
            ["p95Ms"] = Math.Round(p95 * 1000, 2),
            ["maxMs"] = Math.Round(max * 1000, 2),
            ["fps"] = frames == 0 ? 0 : Math.Round(frames / total, 1),
            ["managedHeap"] = GC.GetTotalMemory(false),
            ["il2cppHeapUsed"] = Il2CppHeap(() => UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()),
            ["il2cppHeapSize"] = Il2CppHeap(() => UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong()),
            ["scene"] = SceneState.Current,
            ["playable"] = SceneState.Playable,
            ["syncAcked"] = SyncTracker.Acked,
            ["targetFrameRate"] = Application.targetFrameRate,
            ["vSyncCount"] = QualitySettings.vSyncCount,
        };
    }

    private static long Il2CppHeap(Func<long> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return -1;
        }
    }

    [HarnessCommand("frame-log")]
    private static object FrameLog(string args)
    {
        var parts = (args ?? "0").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        float from = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        float minMs = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
        var frames = new List<object>();
        int total = 0;
        float oldest = -1f;
        for (int i = count - 1; i >= 0; i--)
        {
            int index = (next - 1 - i + Capacity) % Capacity;
            if (oldest < 0f) oldest = frameTimes[index];
            if (frameTimes[index] < from) continue;
            total++;
            if (frameDeltas[index] * 1000f < minMs) continue;
            frames.Add(new[] { Math.Round(frameTimes[index], 4), Math.Round(frameDeltas[index] * 1000, 2) });
        }
        return new Dictionary<string, object>
        {
            ["clock"] = RemoteBuildSpike.Clock(), ["from"] = from, ["oldestKept"] = Math.Round(oldest, 4), ["framesInWindow"] = total,
            ["minMs"] = minMs, ["frames"] = frames,
        };
    }

    [HarnessCommand("fps-cap")]
    private static object FpsCap(string args)
    {
        int fps = int.TryParse((args ?? "").Trim(), out int parsed) && parsed > 0 ? parsed : -1;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = fps;
        return new { targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount };
    }
}
