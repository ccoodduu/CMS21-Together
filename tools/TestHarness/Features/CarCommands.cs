using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
