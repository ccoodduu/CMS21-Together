using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        if (parts.Length < 2) throw new ArgumentException("usage: car-spawn <loader> <car> [config]");
        var carLoader = Loader(parts[0]);
        carLoader.ConfigVersion = parts.Length > 2 ? int.Parse(parts[2]) : 0;
        carLoader.StartCoroutine(carLoader.LoadCar(parts[1]));
        return $"spawning {parts[1]} on loader {parts[0]}";
    }

    [HarnessCommand("car-delete")]
    private static object CarDelete(string args)
    {
        var carLoader = Loader(args);
        if (string.IsNullOrEmpty(carLoader.carToLoad)) return "empty";
        carLoader.DeleteCar();
        return "deleted";
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
            .Concat(sub.Select(s => $"{s.Key}|{s.Unmounted}|{s.TunedID}|{s.Condition:F3}|{s.Quality}|{s.IsExamined}"))
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

    private static CarLoader Loader(string index)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((index ?? "").Trim()));
        if (carLoader == null) throw new ArgumentException($"no car loader {index}");
        return carLoader;
    }
}
