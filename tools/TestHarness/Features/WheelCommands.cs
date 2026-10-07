using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Car.Parts;

namespace TogetherTestHarness.Features;

public static class WheelCommands
{
    private static CarLoader Loader(string index) =>
        CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(index)) ?? throw new ArgumentException($"no loader {index}");

    [HarnessCommand("wheel-parts")]
    private static object WheelParts(string args)
    {
        var registry = PartRegistry.Build(Loader((args ?? "").Trim()));
        return registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => (Key: k, Script: registry.Sub(k)))
            .Where(p => PartApplier.IsWheelPart(p.Script))
            .Select(p => new Dictionary<string, object>
            {
                ["key"] = p.Key, ["id"] = p.Script.id, ["tuned"] = p.Script.tunedID ?? "", ["unmounted"] = p.Script.IsUnmounted,
            })
            .ToList();
    }

    // The game's mount of a wheel group from the inventory: DoMount with the group selected, then ShowMounted, which the
    // player's last bolt would start.
    [HarnessCommand("wheel-mount")]
    private static object WheelMount(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new ArgumentException("usage: wheel-mount <loader> <key> <groupUid>");
        var carLoader = Loader(parts[0]);
        var script = PartRegistry.Build(carLoader).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        var group = Singleton<GameManager>.Instance.Inventory.GetGroup(long.Parse(parts[2])) ?? throw new ArgumentException($"no group {parts[2]}");
        var game = GameScript.Get();
        game.IOMouseOverCarLoader = carLoader;
        game.SelectedToMount = group;
        MelonLoader.MelonCoroutines.Start(Mount(script));
        return new Dictionary<string, object> { ["key"] = parts[1], ["id"] = script.id, ["group"] = group.ID };
    }

    private static System.Collections.IEnumerator Mount(PartScript script)
    {
        yield return script.StartCoroutine(script.DoMount());
        yield return new UnityEngine.WaitForSeconds(0.5f);
        if (script.IsUnmounted) script.StartCoroutine(script.ShowMounted());
    }
}
