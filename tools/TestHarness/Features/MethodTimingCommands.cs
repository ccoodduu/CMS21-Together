using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// method-time on <Type|Type.Method>[,...] [min=<ms>]: times every call of the given game methods (a bare type takes
// all its methods and the MoveNext of its coroutines) and keeps the calls slower than min. Finds the call that blocks
// a frame. report: the slow calls in start order and per-method totals; off: stops recording.
public static class MethodTimingCommands
{
    private const int SlowLimit = 400;

    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.method-time");
    private static readonly Dictionary<MethodBase, string> names = new Dictionary<MethodBase, string>();
    private static readonly Dictionary<string, string> failures = new Dictionary<string, string>();
    private static readonly Dictionary<string, double[]> totals = new Dictionary<string, double[]>();
    private static readonly List<Dictionary<string, object>> slow = new List<Dictionary<string, object>>();
    private static readonly Stack<long> started = new Stack<long>();
    private static bool timing;
    private static double minMs = 50;

    [HarnessCommand("method-time")]
    private static object MethodTime(string args)
    {
        var parts = (args ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string mode = parts.Length > 0 ? parts[0] : "report";
        switch (mode)
        {
            case "on":
                foreach (var part in parts.Skip(1))
                {
                    if (part.StartsWith("min=")) minMs = double.Parse(part.Substring(4), System.Globalization.CultureInfo.InvariantCulture);
                    else foreach (var spec in part.Split(',')) Patch(spec.Trim());
                }
                slow.Clear();
                totals.Clear();
                timing = true;
                break;
            case "off": timing = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: method-time on <Type|Type.Method>[,...] [min=<ms>] | off | report");
        }
        return new Dictionary<string, object>
        {
            ["timing"] = timing,
            ["minMs"] = minMs,
            ["patched"] = names.Count,
            ["failures"] = failures,
            ["slow"] = slow.ToList(),
            ["top"] = totals.OrderByDescending(t => t.Value[2]).Take(40)
                .Select(t => new { method = t.Key, count = (int)t.Value[0], totalMs = Math.Round(t.Value[1], 1), maxMs = Math.Round(t.Value[2], 1) }).ToList(),
        };
    }

    private static void Patch(string spec)
    {
        if (spec.Length == 0) return;
        var methods = new List<MethodInfo>();
        var type = AccessTools.TypeByName(spec);
        if (type != null)
        {
            methods.AddRange(AccessTools.GetDeclaredMethods(type).Where(m => !m.IsGenericMethodDefinition && !m.IsAbstract && m.DeclaringType == type));
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                var moveNext = AccessTools.DeclaredMethod(nested, "MoveNext");
                if (moveNext != null) methods.Add(moveNext);
            }
        }
        else
        {
            int dot = spec.LastIndexOf('.');
            type = dot > 0 ? AccessTools.TypeByName(spec.Substring(0, dot)) : null;
            if (type == null) { failures[spec] = "type not found"; return; }
            methods.AddRange(AccessTools.GetDeclaredMethods(type).Where(m => m.Name == spec.Substring(dot + 1)));
            if (methods.Count == 0) { failures[spec] = "method not found"; return; }
        }

        var prefix = new HarmonyMethod(typeof(MethodTimingCommands).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
        var postfix = new HarmonyMethod(typeof(MethodTimingCommands).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var method in methods)
        {
            if (names.ContainsKey(method)) continue;
            string name = $"{method.DeclaringType?.Name}.{method.Name}";
            try
            {
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                names[method] = name;
            }
            catch (Exception e)
            {
                failures[name] = e.GetType().Name + ": " + e.Message.Split('\n')[0];
            }
        }
        MelonLogger.Msg($"[Harness] method-time patched {names.Count} methods ({failures.Count} failed) after {spec}.");
    }

    private static void Prefix() => started.Push(Stopwatch.GetTimestamp());

    private static void Postfix(MethodBase __originalMethod)
    {
        long startedAt = started.Count > 0 ? started.Pop() : Stopwatch.GetTimestamp();
        if (!timing) return;
        double ms = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        string name = names.TryGetValue(__originalMethod, out string known) ? known : __originalMethod.Name;
        if (!totals.TryGetValue(name, out var total)) totals[name] = total = new double[3];
        total[0]++;
        total[1] += ms;
        total[2] = Math.Max(total[2], ms);
        if (ms < minMs || slow.Count >= SlowLimit) return;
        slow.Add(new Dictionary<string, object>
        {
            ["method"] = name,
            ["ms"] = Math.Round(ms, 1),
            ["start"] = Math.Round(Time.realtimeSinceStartup - ms / 1000.0, 3),
            ["frame"] = Time.frameCount,
        });
    }
}
