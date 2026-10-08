using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;

namespace TogetherTestHarness.Features;

// Spikes 1.2 and 1.3 of state-merges-and-contention: which record fields an action changes, and which car detail
// entries change by themselves.
public static class MergeProbeCommands
{
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> partSnaps =
        new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
    private static readonly Dictionary<string, SortedDictionary<string, string>> detailSnaps = new Dictionary<string, SortedDictionary<string, string>>();

    [HarnessCommand("part-records")]
    private static object PartRecords(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[1] != "snap" && parts[1] != "diff") throw new ArgumentException("usage: part-records <loader> snap|diff <name>");
        var now = Fields(Loaded(parts[0]));
        if (parts[1] == "snap")
        {
            partSnaps[parts[2]] = now;
            return new { keys = now.Count };
        }
        if (!partSnaps.TryGetValue(parts[2], out var before)) throw new ArgumentException($"no snapshot {parts[2]}");
        var changed = new List<object>();
        foreach (var pair in now.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(pair.Key, out var old)) continue;
            var fields = pair.Value.Where(f => !old.TryGetValue(f.Key, out string value) || value != f.Value)
                .Select(f => $"{f.Key}: {(old.TryGetValue(f.Key, out string v) ? v : "-")} -> {f.Value}").ToList();
            if (fields.Count > 0) changed.Add(new { key = pair.Key, fields });
        }
        return new { changed = changed.Count, records = changed.Take(60).ToList() };
    }

    [HarnessCommand("cardetails-drift")]
    private static object Drift(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[1] != "snap" && parts[1] != "diff") throw new ArgumentException("usage: cardetails-drift <loader> snap|diff <name>");
        var now = CarDetailEntries.Signatures(CarDetailsIO.Read(Loaded(parts[0]), CarDetailsIO.All));
        if (parts[1] == "snap")
        {
            detailSnaps[parts[2]] = now;
            return new { entries = now.Count };
        }
        if (!detailSnaps.TryGetValue(parts[2], out var before)) throw new ArgumentException($"no snapshot {parts[2]}");
        var changed = now.Keys.Union(before.Keys).OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => !before.TryGetValue(k, out string old) || !now.TryGetValue(k, out string value) || old != value)
            .Select(k => new { entry = k, before = before.TryGetValue(k, out string old) ? old : null, after = now.TryGetValue(k, out string value) ? value : null })
            .ToList();
        return new { changed = changed.Count, entries = changed };
    }

    [HarnessCommand("part-condition")]
    private static object PartCondition(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new ArgumentException("usage: part-condition <loader> <key> <value>");
        int loader = int.Parse(parts[0]);
        var carLoader = Loaded(parts[0]);
        float value = float.Parse(parts[2], CultureInfo.InvariantCulture);
        var registry = PartRegistry.Build(carLoader);
        if (parts[1].StartsWith("b:"))
        {
            var body = registry.Body(parts[1]) ?? throw new ArgumentException($"no body part {parts[1]}");
            carLoader.SetCondition(body, value);
            carLoader.UpdateCarBodyPart(body);
        }
        else
        {
            var script = registry.Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
            if (script.ho != null) script.SetConditionNormal(value);
            else
            {
                script.Condition = UnityEngine.Mathf.Clamp01(value);
                script.UpdateShaderParams(true);
            }
        }
        PartChangeTracker.MarkDirty(loader);
        return new { key = parts[1], condition = value };
    }

    // The game's mount with the item's own condition, quality and paint: DoMount with the item selected, then
    // ShowMounted, which the last bolt would start (part-fast-mount skips DoMount).
    [HarnessCommand("part-domount")]
    private static object PartDoMount(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new ArgumentException("usage: part-domount <loader> <key> <itemUid>");
        var carLoader = Loaded(parts[0]);
        var script = PartRegistry.Build(carLoader).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        var item = Singleton<GameManager>.Instance.Inventory.GetItem(long.Parse(parts[2])) ?? throw new ArgumentException($"no item {parts[2]}");
        var game = GameScript.Get();
        game.IOMouseOverCarLoader = carLoader;
        game.SelectedToMount = item;
        MelonLoader.MelonCoroutines.Start(DoMount(script));
        return new { key = parts[1], id = script.id, item = item.ID, quality = item.Quality, condition = item.Condition };
    }

    private static System.Collections.IEnumerator DoMount(PartScript script)
    {
        yield return script.StartCoroutine(script.DoMount());
        yield return new UnityEngine.WaitForSeconds(0.5f);
        if (script.IsUnmounted) script.StartCoroutine(script.ShowMounted());
    }

    private static Dictionary<string, Dictionary<string, string>> Fields(CarLoader carLoader)
    {
        var registry = PartRegistry.Build(carLoader);
        var body = new List<CarBodyPartUpdatePacket>();
        var sub = new List<CarSubPartUpdatePacket>();
        CarPartsSync.CaptureAll(carLoader, registry, body, sub);
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var b in body)
            result[b.Key] = new Dictionary<string, string>
            {
                ["Unmounted"] = b.Unmounted.ToString(), ["Switched"] = b.Switched.ToString(), ["TunedID"] = b.TunedID ?? "",
                ["Condition"] = F(b.State?.Condition), ["Dent"] = F(b.State?.Dent), ["Quality"] = b.State?.Quality.ToString(),
            };
        foreach (var s in sub)
        {
            var fields = new Dictionary<string, string>
            {
                ["PartId"] = s.PartId ?? "", ["TunedID"] = s.TunedID ?? "", ["Unmounted"] = s.Unmounted.ToString(), ["Condition"] = F(s.Condition),
                ["Quality"] = s.Quality.ToString(), ["IsExamined"] = s.IsExamined.ToString(), ["IsPainted"] = s.IsPainted.ToString(), ["Dust"] = F(s.Dust),
                ["Mount.ParentPath"] = s.MountObjectData?.ParentPath ?? "",
                ["Mount.Condition"] = string.Join(",", (s.MountObjectData?.Condition ?? new float[0]).Select(c => F(c))),
                ["Mount.IsStuck"] = string.Join(",", (s.MountObjectData?.IsStuck ?? new bool[0]).Select(c => c ? "1" : "0")),
            };
            result[s.Key] = fields;
        }
        return result;
    }

    private static string F(float? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "";

    private static CarLoader Loaded(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        return carLoader;
    }
}
