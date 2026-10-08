using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21Together.Logic.Car;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace TogetherTestHarness.Features;

public static class CarCommands
{
    [HarnessCommand("car-list")]
    private static object CarList(string args)
    {
        var loader = Singleton<GameManager>.Instance.CarBundleLoader;
        var cars = new List<object>();
        foreach (string name in loader.GetCarNames())
        {
            cars.Add(new Dictionary<string, object>
            {
                ["id"] = name,
                ["configs"] = loader.GetConfigCounts(name),
                ["dlc"] = loader.CheckHaveDLCForCar(name),
                ["mod"] = loader.IsModCar(name),
            });
        }
        return cars;
    }

    [HarnessCommand("car-spawn")]
    private static object CarSpawn(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("usage: car-spawn <loader> <car> [config] [CarPlace|auto]");
        var carLoader = Loader(parts[0]);
        carLoader.ConfigVersion = parts.Length > 2 ? int.Parse(parts[2]) : 0;
        if (parts.Length > 3)
        {
            var place = parts[3] == "auto" ? FreePlace() : (CarPlace)Enum.Parse(typeof(CarPlace), parts[3], true);
            carLoader.placeNo = (int)place;
            MelonLoader.MelonCoroutines.Start(SpawnAt(carLoader, parts[1], place));
            return $"spawning {parts[1]} on loader {parts[0]} at {place}";
        }
        carLoader.StartCoroutine(carLoader.LoadCar(parts[1]));
        return $"spawning {parts[1]} on loader {parts[0]}";
    }

    private static readonly CarPlace[] SpawnPlaces = { CarPlace.Entrance1, CarPlace.Entrance2, CarPlace.Entrance3, CarPlace.CarLifter1, CarPlace.CarLifter2 };

    private static CarPlace FreePlace()
    {
        var places = CarLoaderPlaces.Get();
        foreach (var candidate in SpawnPlaces)
            if (places.GetCarLoaderForPlace(candidate) == null) return candidate;
        throw new InvalidOperationException("no free car place");
    }

    private static System.Collections.IEnumerator SpawnAt(CarLoader carLoader, string car, CarPlace place)
    {
        carLoader.StartCoroutine(carLoader.LoadCar(car));
        while (!carLoader.IsCarLoaded()) yield return null;
        carLoader.ResetCarLifter();
        carLoader.ChangePosition((int)place);
    }

    [HarnessCommand("car-delete")]
    private static object CarDelete(string args)
    {
        var carLoader = Loader(args);
        if (string.IsNullOrEmpty(carLoader.carToLoad)) return "empty";
        carLoader.DeleteCar(true);
        return "deleted";
    }

    [HarnessCommand("car-hold-snapshot")]
    private static object CarHoldSnapshot(string args)
    {
        CarPartsSync.TestSnapshotDelaySeconds = float.Parse((args ?? "0").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        return CarPartsSync.TestSnapshotDelaySeconds;
    }

    [HarnessCommand("part-hold-remote")]
    private static object PartHoldRemote(string args)
    {
        if ((args ?? "").Trim() == "on") PartChanges.TestHoldRemote = true;
        else PartChanges.TestReleaseRemote();
        return PartChanges.TestHoldRemote;
    }

    internal static void Reset(List<string> changed)
    {
        if (CarPartsSync.TestSnapshotDelaySeconds > 0f) changed.Add($"car-hold-snapshot {CarPartsSync.TestSnapshotDelaySeconds}");
        CarPartsSync.TestSnapshotDelaySeconds = 0f;
        var heldRemote = HarmonyLib.AccessTools.Field(typeof(PartChanges), "heldForTest")?.GetValue(null) as System.Collections.IList
            ?? throw new MissingFieldException(nameof(PartChanges), "heldForTest");
        if (PartChanges.TestHoldRemote || heldRemote.Count > 0) changed.Add($"part-hold-remote (dropped {heldRemote.Count} held changes)");
        PartChanges.TestHoldRemote = false;
        heldRemote.Clear();
    }

    [HarnessCommand("car-dlc-cars")]
    private static object CarDlcCars(string args)
    {
        var bundles = UnityEngine.Object.FindObjectOfType<CarBundleLoader>();
        var cars = new List<object>();
        for (int i = 0; bundles?.CarNamesData != null && i < bundles.CarNamesData.Count; i++)
        {
            var car = bundles.CarNamesData[i];
            if (car != null && CarDlc.For(car.CarID) != CarDlc.BaseGame) cars.Add(new { car = car.CarID, dlc = CarDlc.For(car.CarID) });
        }
        return cars;
    }

    [HarnessCommand("car-request")]
    private static object CarRequest(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: car-request <loader> <car>|delete");
        if (parts[1] == "delete")
        {
            CMS21Together.Network.Client.Instance.Send(new CMS21_Together_Core.Network.Packets.CarSpawnDeletePacket { CarLoaderID = int.Parse(parts[0]) });
            return "delete sent";
        }
        var request = new CMS21_Together_Core.Network.Packets.CarSpawnRequestPacket
        {
            CarLoaderID = int.Parse(parts[0]), CarToLoad = parts[1], JobID = -1, Dlc = CarDlc.For(parts[1])
        };
        CMS21Together.Network.Client.Instance.Send(request);
        return new { car = parts[1], dlc = request.Dlc };
    }

    [HarnessCommand("car-loaded")]
    private static object CarLoaded(string args)
    {
        var carLoader = Loader(args);
        return new Dictionary<string, object> { ["car"] = carLoader.carToLoad, ["loaded"] = !string.IsNullOrEmpty(carLoader.carToLoad) && carLoader.IsCarLoaded() };
    }

    [HarnessCommand("car-ready")]
    private static object CarReady(string args)
    {
        int loader = int.Parse((args ?? "").Trim());
        var carLoader = Loader(args);
        var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == loader);
        var result = new Dictionary<string, object>
        {
            ["car"] = carLoader.carToLoad,
            ["loaded"] = !string.IsNullOrEmpty(carLoader.carToLoad) && carLoader.IsCarLoaded(),
            ["state"] = sync?.State.ToString() ?? "Empty",
            ["spawnSeq"] = sync?.SpawnSeq ?? 0,
            ["revision"] = sync?.Revision ?? 0,
        };
        if ((bool)result["loaded"])
        {
            var registry = PartRegistry.Build(carLoader);
            var body = new List<CMS21_Together_Core.Network.Packets.CarBodyPartUpdatePacket>();
            var sub = new List<CMS21_Together_Core.Network.Packets.CarSubPartUpdatePacket>();
            CarPartsSync.CaptureAll(carLoader, registry, body, sub);
            result["registryHash"] = registry.Hash();
            result["body"] = body.Count;
            result["sub"] = sub.Count;
            result["unmounted"] = body.Count(b => b.Unmounted) + sub.Count(s => s.Unmounted);
            result["stateHash"] = StateHash(body, sub);
        }
        return result;
    }

    private static string StateHash(List<CMS21_Together_Core.Network.Packets.CarBodyPartUpdatePacket> body, List<CMS21_Together_Core.Network.Packets.CarSubPartUpdatePacket> sub)
    {
        var lines = body.Select(b => $"{b.Key}|{b.Unmounted}|{b.Switched}|{b.TunedID}|{b.State?.Condition:F3}|{b.State?.Dent:F3}|{b.State?.Quality}")
            .Concat(sub.Select(s => $"{s.Key}|{s.Unmounted}|{s.EffectiveId}|{s.Condition:F3}|{s.Quality}|{s.IsExamined}"))
            .OrderBy(l => l, StringComparer.Ordinal);
        using (var sha = System.Security.Cryptography.SHA1.Create())
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Replace("-", "").Substring(0, 12);
    }

    [HarnessCommand("part-state")]
    private static object PartState(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: part-state <loader> <file>");
        var carLoader = Loader(parts[0]);
        var registry = PartRegistry.Build(carLoader);
        var body = new List<CMS21_Together_Core.Network.Packets.CarBodyPartUpdatePacket>();
        var sub = new List<CMS21_Together_Core.Network.Packets.CarSubPartUpdatePacket>();
        CarPartsSync.CaptureAll(carLoader, registry, body, sub);
        File.WriteAllLines(parts[1], body.Select(b => $"{b.Key} {b.PartName} unmounted={b.Unmounted} switched={b.Switched} tuned={b.TunedID} cond={b.State?.Condition:F3} dent={b.State?.Dent:F3} q={b.State?.Quality}")
            .Concat(sub.Select(s => $"{s.Key} {s.PartId} unmounted={s.Unmounted} tuned={s.TunedID} cond={s.Condition:F3} q={s.Quality} examined={s.IsExamined}")));
        return parts[1];
    }

    [HarnessCommand("part-unmount")]
    private static object PartUnmount(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var carLoader = Loader(parts[0]);
        var registry = PartRegistry.Build(carLoader);
        string key = parts.Length > 1 ? parts[1] : registry.SubKeys.First(k => !registry.Sub(k).IsUnmounted && registry.Sub(k).GetUnmountWith().Count == 0);
        var script = registry.Sub(key) ?? throw new ArgumentException($"no part {key}");
        script.HideBySavegame(false, carLoader);
        return new Dictionary<string, object> { ["key"] = key, ["id"] = script.id };
    }

    [HarnessCommand("part-fast-unmount")]
    private static object PartFastUnmount(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var carLoader = Loader(parts[0]);
        var registry = PartRegistry.Build(carLoader);
        bool group = parts.Length > 1 && parts[1] == "group";
        string key = parts.Length > 1 && !group ? parts[1] : registry.SubKeys.First(k =>
        {
            var part = registry.Sub(k);
            int members = part.GetUnmountWith().Count;
            return !part.IsUnmounted && !part.IsBlocked() && (group ? members > 0 && part.GetUnmountWith().ToArray().All(m => !m.IsUnmounted) : members == 0);
        });
        var script = registry.Sub(key) ?? throw new ArgumentException($"no part {key}");
        var memberKeys = script.GetUnmountWith().ToArray()
            .Select(m => registry.TryGetSubPath(m, out var path) ? CMS21_Together_Core.Network.Packets.PartKeys.Sub(path) : null)
            .Where(k => k != null).ToList();
        // Hide reads the car under the mouse for fluid and suspension parts and throws without one.
        GameScript.Get().IOMouseOverCarLoader = carLoader;
        script.FastUnmount();
        return new Dictionary<string, object> { ["key"] = key, ["id"] = script.id, ["members"] = memberKeys };
    }

    [HarnessCommand("part-corrupt")]
    private static object PartCorrupt(string args)
    {
        int loader = int.Parse((args ?? "").Trim());
        var carLoader = Loader(args);
        var registry = PartRegistry.Build(carLoader);
        string key = registry.SubKeys.First(k => !registry.Sub(k).IsUnmounted && registry.Sub(k).GetUnmountWith().Count == 0 && !registry.Sub(k).IsBlocked());
        using (ApplyingRemote.Scope(loader))
            registry.Sub(key).HideBySavegame(false, carLoader);
        return new Dictionary<string, object> { ["key"] = key };
    }

    [HarnessCommand("resync")]
    private static object Resync(string args) =>
        CMS21Together.Session.ResyncController.Request((args ?? "").Trim() == "force") ?? "reloading";

    [HarnessCommand("part-fast-mount")]
    private static object PartFastMount(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 && parts.Length != 3) throw new ArgumentException("usage: part-fast-mount <loader> <key> [itemUid]");
        var registry = PartRegistry.Build(Loader(parts[0]));
        var script = registry.Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        if (parts.Length == 3)
            GameScript.Get().SelectedToMount = Singleton<GameManager>.Instance.Inventory.GetItem(long.Parse(parts[2])) ?? throw new ArgumentException($"no item {parts[2]}");
        script.FastMount();
        return new Dictionary<string, object> { ["key"] = parts[1], ["id"] = script.id };
    }

    [HarnessCommand("part-blocks")]
    private static object PartBlocks(string args)
    {
        var carLoader = Loader((args ?? "").Trim());
        var registry = PartRegistry.Build(carLoader);
        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in registry.SubKeys)
        {
            int count = registry.Sub(key).blockedNo;
            if (count != 0) actual[key] = count;
        }
        var counts = PartBlocking.ExpectedCounts(registry);
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in registry.SubKeys)
            if (counts.TryGetValue(registry.Sub(key).GetInstanceID(), out int count)) expected[key] = count;
        var neighbours = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string key in registry.SubKeys)
        {
            var keys = PartBlocking.BlockedBy(registry.Sub(key))
                .Select(n => registry.TryGetSubPath(n, out var path) ? CMS21_Together_Core.Network.Packets.PartKeys.Sub(path) : null)
                .Where(k => k != null).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (keys.Count > 0) neighbours[key] = keys;
        }
        var candidates = neighbours.Keys
            .Where(k => !registry.Sub(k).IsUnmounted && !registry.Sub(k).IsBlocked() && registry.Sub(k).GetUnmountWith().Count == 0)
            .OrderByDescending(k => neighbours[k].Count).ToList();
        return new Dictionary<string, object> { ["actual"] = actual, ["expected"] = expected, ["blocks"] = neighbours, ["candidates"] = candidates };
    }

    [HarnessCommand("part-special")]
    private static object PartSpecial(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: part-special <loader> <specialGroup>");
        var registry = PartRegistry.Build(Loader(parts[0]));
        int group = int.Parse(parts[1]);
        return registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => registry.Sub(k).partProperty != null && (int)registry.Sub(k).partProperty.SpecialGroup == group)
            .Select(k => new { key = k, id = registry.Sub(k).id, unmounted = registry.Sub(k).IsUnmounted, unmountWith = registry.Sub(k).GetUnmountWith().Count })
            .ToList();
    }

    [HarnessCommand("part-twins")]
    private static object PartTwins(string args)
    {
        var registry = PartRegistry.Build(Loader((args ?? "").Trim()));
        var twins = registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => (Key: k, Script: registry.Sub(k)))
            .Where(p => !p.Script.IsUnmounted && !p.Script.IsBlocked() && p.Script.GetUnmountWith().Count == 0)
            .GroupBy(p => p.Script.id)
            .FirstOrDefault(g => g.Count() >= 2) ?? throw new ArgumentException("no two free parts share an id");
        return new Dictionary<string, object> { ["id"] = twins.Key, ["keys"] = twins.Take(2).Select(p => p.Key).ToList() };
    }

    [HarnessCommand("part-claim")]
    private static object PartClaim(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("usage: part-claim <loader> <key> [release]");
        PartClaims.Claim(int.Parse(parts[0]), new[] { parts[1] }, release: parts.Length > 2 && parts[2] == "release");
        return "sent";
    }

    [HarnessCommand("part-action-unmount")]
    private static object PartActionUnmount(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: part-action-unmount <loader> <key>");
        int loader = int.Parse(parts[0]);
        var script = PartRegistry.Build(Loader(parts[0])).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        bool held = PartClaims.HeldByOther(loader, new[] { parts[1] }, out int owner);
        if (held)
        {
            script.ActionUnMount();
            return new Dictionary<string, object> { ["blocked"] = PartClaims.LastBlocked == parts[1], ["owner"] = owner };
        }
        return new Dictionary<string, object> { ["blocked"] = false, ["owner"] = owner };
    }

    [HarnessCommand("car-baseline")]
    private static object CarBaseline(string args)
    {
        CarPartsSync.UploadBaseline(int.Parse((args ?? "").Trim()));
        return "baseline sent";
    }

    [HarnessCommand("part-keys")]
    private static object PartKeys(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: part-keys <loader> <file>");
        var carLoader = Loader(parts[0]);
        var lines = new List<string> { $"car {carLoader.carToLoad} config {carLoader.ConfigVersion} root {(carLoader.root != null ? carLoader.root.name : "loader")}" };

        var body = carLoader.carParts;
        for (int i = 0; body != null && i < body.Count; i++)
            lines.Add($"body {i} {body[i].name}");

        var root = carLoader.root != null ? carLoader.root.transform : carLoader.transform;
        foreach (var script in root.GetComponentsInChildren<PartScript>(true))
            lines.Add($"sub {Path(root, script.transform)} {script.id}");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(parts[1]));
        File.WriteAllLines(parts[1], lines);
        return new Dictionary<string, object> { ["body"] = body?.Count ?? 0, ["sub"] = lines.Count(l => l.StartsWith("sub ")), ["file"] = parts[1] };
    }

    private static string Path(Transform root, Transform target)
    {
        var indices = new List<int>();
        for (var t = target; t != null && t != root; t = t.parent) indices.Add(t.GetSiblingIndex());
        indices.Reverse();
        return string.Join(".", indices);
    }

    [HarnessCommand("crane-out")]
    private static object CraneOut(string args)
    {
        var carLoader = Loader(args);
        var engine = carLoader.e_engine_h ?? throw new ArgumentException("the car has no engine");
        NotificationCenter.Get().ActionUnMountGroup(engine.GetComponent<InteractiveObject>());
        return new Dictionary<string, object> { ["engine"] = engine.name, ["group"] = EngineGroup(engine.name)?.UID ?? 0 };
    }

    [HarnessCommand("crane-in")]
    private static object CraneIn(string args)
    {
        var carLoader = Loader(args);
        var engine = carLoader.e_engine_h ?? throw new ArgumentException("the car has no engine");
        var group = EngineGroup(engine.name) ?? throw new ArgumentException($"no {engine.name} group in the inventory");
        var tools = carLoader.ToolsData;
        tools.EngineCraneIsConnected = true;
        carLoader.ToolsData = tools;
        NotificationCenter.Get().InsertEngineToCar(group);
        return new Dictionary<string, object> { ["engine"] = engine.name, ["group"] = group.UID };
    }

    private static GroupItem EngineGroup(string engineName)
    {
        var groups = Singleton<GameManager>.Instance.Inventory.GetGroups();
        for (int i = groups.Count - 1; i >= 0; i--)
            if (groups[i].ID == engineName) return groups[i];
        return null;
    }

    private static CarLoader Loader(string index)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((index ?? "").Trim()));
        if (carLoader == null) throw new ArgumentException($"no car loader {index}");
        return carLoader;
    }
}
