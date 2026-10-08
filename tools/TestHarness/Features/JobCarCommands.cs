using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Seeding;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace TogetherTestHarness.Features;

// seeded job cars (server-game-logic spike 1.5, task group 5): a digest of a job car as the take left it, and a trace
// of the Unity random state around every step of the iterators that prepare it.
[HarmonyPatch]
public static class JobCarCommands
{
    private const int TraceLimit = 4000;

    private static readonly CarDetailSection[] Sections =
    {
        CarDetailSection.Fluids, CarDetailSection.Wheels, CarDetailSection.Alignment, CarDetailSection.Tuning, CarDetailSection.Paint,
        CarDetailSection.BodyCosmetics, CarDetailSection.Plates, CarDetailSection.Info,
    };

    private static readonly List<string> trace = new List<string>();
    private static bool tracing;

    private static OrderGenerator Generator => Singleton<GameManager>.Instance.OrderGenerator;

    [HarnessCommand("jobcar-digest")]
    private static object Digest(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) throw new ArgumentException("usage: jobcar-digest <jobId> [rows]");
        int id = int.Parse(parts[0]);
        var (job, carLoader, loader) = ActiveJob(id);
        var rows = Rows(job, carLoader);
        var sections = rows.GroupBy(r => r.Key.Split(':')[0]).ToDictionary(g => g.Key, g => Hash(g.Select(r => $"{r.Key}={r.Value}")));
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["loader"] = loader,
            ["car"] = carLoader.carToLoad,
            ["hash"] = Hash(rows.Select(r => $"{r.Key}={r.Value}")),
            ["sections"] = sections,
            ["rows"] = parts.Length > 1 && parts[1] == "rows" ? rows.Select(r => $"{r.Key}={r.Value}").ToList() : null,
        };
    }

    private static SortedDictionary<string, string> Rows(Job job, CarLoader carLoader)
    {
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["car:id"] = $"{carLoader.carToLoad}/{carLoader.ConfigVersion}",
            ["job:other"] = job.otherPartsCondition.ToString("R", CultureInfo.InvariantCulture),
        };
        for (int i = 0; job.jobTasks != null && i < job.jobTasks.Length; i++)
        {
            var task = job.jobTasks[i];
            var ids = new List<string>();
            for (int p = 0; task.Parts != null && p < task.Parts.Count; p++) ids.Add(task.Parts[p].ID);
            ids.Sort(StringComparer.Ordinal);
            rows[$"job:task{i}"] = $"{task.type}/{task.subtype}/{task.easyMode}/{task.IncreaseTuneValue}/{task.partsCount}/{string.Join(",", ids)}";
        }
        var details = CarDetailsIO.Read(carLoader, CarDetailsIO.All);
        foreach (var section in Sections) rows[$"details:{section}"] = CarDetailsSync.Signature(details, section);

        var registry = PartRegistry.Build(carLoader);
        var body = new List<CarBodyPartUpdatePacket>();
        var sub = new List<CarSubPartUpdatePacket>();
        CarPartsSync.CaptureAll(carLoader, registry, body, sub);
        foreach (var part in body) rows[$"body:{part.Key}"] = JsonConvert.SerializeObject(part);
        foreach (var part in sub) rows[$"sub:{part.Key}"] = JsonConvert.SerializeObject(part);
        return rows;
    }

    private static string Hash(IEnumerable<string> lines)
    {
        using (var sha = SHA1.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Replace("-", "").Substring(0, 12);
    }

    private static (Job Job, CarLoader Loader, int Index) ActiveJob(int id)
    {
        var places = CarLoaderPlaces.Get();
        for (int i = 0; i < places.GetCarLoadersCount(); i++)
        {
            var job = Generator.GetJobForCarLoader(i);
            if (job != null && job.id == id) return (job, places.GetCarLoaderByIndex(i), i);
        }
        throw new ArgumentException($"no active job {id}");
    }

    [HarnessCommand("jobcar-trace")]
    private static object Trace(string args)
    {
        switch ((args ?? "").Trim().ToLowerInvariant())
        {
            case "on":
                trace.Clear();
                tracing = true;
                return "jobcar trace on";
            case "off":
                tracing = false;
                return $"jobcar trace off ({trace.Count} lines)";
            case "report":
            case "":
                return new List<string>(trace);
            default:
                throw new ArgumentException("usage: jobcar-trace on|off|report");
        }
    }

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("jobcar-trace");
        tracing = false;
        trace.Clear();
    }

    private static void Note(string text)
    {
        if (!tracing || trace.Count >= TraceLimit) return;
        string line = $"{Time.realtimeSinceStartup:0.000} f{Time.frameCount} {text}";
        trace.Add(line);
        MelonLogger.Msg($"[Harness] jobcar-trace {line}");
    }

    private static string RandomState() => SeededStreams.Current.ToString();

    private static int LoaderIndex(CarLoader carLoader) => carLoader == null ? -1 : CarLoaderPlaces.Get().GetCarLoaderId(carLoader);

    [HarmonyPatch(typeof(OrderGenerator._TakeJob_d__19), nameof(OrderGenerator._TakeJob_d__19.MoveNext))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void TakeJobEnter(OrderGenerator._TakeJob_d__19 __instance, out int __state)
    {
        __state = __instance.__1__state;
        if (tracing) Note($"TakeJob {__instance.id} enter {__state} loader {LoaderIndex(__instance._carLoader_5__3)} random {RandomState()}");
    }

    [HarmonyPatch(typeof(OrderGenerator._TakeJob_d__19), nameof(OrderGenerator._TakeJob_d__19.MoveNext))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void TakeJobExit(OrderGenerator._TakeJob_d__19 __instance, int __state, bool __result)
    {
        if (tracing) Note($"TakeJob {__instance.id} exit {__state} -> {__instance.__1__state} result {__result} random {RandomState()}");
    }

    [HarmonyPatch(typeof(OrderGenerator._TakeMission_d__22), nameof(OrderGenerator._TakeMission_d__22.MoveNext))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void TakeMissionEnter(OrderGenerator._TakeMission_d__22 __instance, out int __state)
    {
        __state = __instance.__1__state;
        if (tracing) Note($"TakeMission {__instance.id} enter {__state} loader {LoaderIndex(__instance._carLoader_5__3)} random {RandomState()}");
    }

    [HarmonyPatch(typeof(OrderGenerator._TakeMission_d__22), nameof(OrderGenerator._TakeMission_d__22.MoveNext))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void TakeMissionExit(OrderGenerator._TakeMission_d__22 __instance, int __state, bool __result)
    {
        if (tracing) Note($"TakeMission {__instance.id} exit {__state} -> {__instance.__1__state} result {__result} random {RandomState()}");
    }

    [HarmonyPatch(typeof(CarLoader._LoadCar_d__215), nameof(CarLoader._LoadCar_d__215.MoveNext))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void LoadCarEnter(CarLoader._LoadCar_d__215 __instance, out int __state)
    {
        __state = __instance.__1__state;
        if (tracing) Note($"LoadCar loader {LoaderIndex(__instance.__4__this)} enter {__state} random {RandomState()}");
    }

    [HarmonyPatch(typeof(CarLoader._LoadCar_d__215), nameof(CarLoader._LoadCar_d__215.MoveNext))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void LoadCarExit(CarLoader._LoadCar_d__215 __instance, int __state, bool __result)
    {
        if (tracing) Note($"LoadCar loader {LoaderIndex(__instance.__4__this)} exit {__state} -> {__instance.__1__state} result {__result} random {RandomState()}");
    }

    [HarmonyPatch(typeof(CarLoader._SetRandomColorPanels_d__321), nameof(CarLoader._SetRandomColorPanels_d__321.MoveNext))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void PanelsEnter(CarLoader._SetRandomColorPanels_d__321 __instance, out int __state)
    {
        __state = __instance.__1__state;
        if (tracing) Note($"ColorPanels loader {LoaderIndex(__instance.__4__this)} enter {__state} random {RandomState()}");
    }

    [HarmonyPatch(typeof(CarLoader._SetRandomColorPanels_d__321), nameof(CarLoader._SetRandomColorPanels_d__321.MoveNext))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void PanelsExit(CarLoader._SetRandomColorPanels_d__321 __instance, int __state, bool __result)
    {
        if (tracing) Note($"ColorPanels loader {LoaderIndex(__instance.__4__this)} exit {__state} -> {__instance.__1__state} result {__result} random {RandomState()}");
    }

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.PlaceAtPosition))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void PlaceEnter(CarLoader __instance)
    {
        if (tracing) Note($"PlaceAtPosition loader {LoaderIndex(__instance)} enter random {RandomState()}");
    }

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.PlaceAtPosition))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void PlaceExit(CarLoader __instance)
    {
        if (tracing) Note($"PlaceAtPosition loader {LoaderIndex(__instance)} exit random {RandomState()}");
    }

    [HarmonyPatch(typeof(OrderGenerator), "PrepareJob")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void PrepareEnter(CarLoader carLoader)
    {
        if (tracing) Note($"PrepareJob loader {LoaderIndex(carLoader)} enter random {RandomState()}");
    }

    [HarmonyPatch(typeof(OrderGenerator), "PrepareJob")]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void PrepareExit(CarLoader carLoader)
    {
        if (tracing) Note($"PrepareJob loader {LoaderIndex(carLoader)} exit random {RandomState()}");
    }
}
