using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Pings;
using CMS21Together.Network;
using Rewired;
using UnityEngine;

namespace TogetherTestHarness.Features;

// coop-ping harness verbs. "ping <loader> <key>" puts the part under the game's mouse-over, as the game's raycast does,
// and runs the hotkey's path (CoopPings.PingUnderCursor); "ping" without arguments stays the status probe.
public static class PingCommands
{
    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    public static object Part(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: ping <loader> <key> (no arguments: status)");
        int loader = int.Parse(parts[0]);
        string key = parts[1];
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader) ?? throw new ArgumentException($"no car loader {loader}");
        var registry = PartRegistry.Build(carLoader);
        var game = GameScript.Get() ?? throw new InvalidOperationException("no GameScript");

        var previousPart = game.partMouseOver;
        var previousType = game.IOMouseOverType;
        var previousLoader = game.IOMouseOverCarLoader;
        var previousIO = game.IOMouseOverIO;
        try
        {
            if (key.StartsWith("s:", StringComparison.Ordinal))
            {
                var script = registry.Sub(key) ?? throw new ArgumentException($"no part {key}");
                game.SetPartMouseOver(script);
            }
            else
            {
                var part = registry.Body(key) ?? throw new ArgumentException($"no body part {key}");
                var io = part.handle == null ? null : part.handle.GetComponentInChildren<InteractiveObject>(true);
                if (io == null || string.IsNullOrEmpty(io.type)) throw new ArgumentException($"body part {key} has no interactive object");
                game.SetPartMouseOver(null);
                game.IOMouseOverCarLoader = carLoader;
                game.IOMouseOverType = io.type;
                game.IOMouseOverIO = io;
            }
            var outcome = CoopPings.PingUnderCursor();
            return Result(outcome);
        }
        finally
        {
            game.SetPartMouseOver(previousPart);
            game.IOMouseOverType = previousType;
            game.IOMouseOverCarLoader = previousLoader;
            game.IOMouseOverIO = previousIO;
        }
    }

    [HarnessCommand("ping-spot")]
    private static object Spot(string args) => Result(CoopPings.PingSpot(ParseVector(args)));

    [HarnessCommand("ping-burst")]
    private static object Burst(string args)
    {
        var parts = Args(args);
        if (parts.Length != 3) throw new ArgumentException("usage: ping-burst <count> <loader> <key>");
        int count = int.Parse(parts[0]);
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[1])) ?? throw new ArgumentException($"no car loader {parts[1]}");
        var script = PartRegistry.Build(carLoader).Sub(parts[2]) ?? throw new ArgumentException($"no part {parts[2]}");
        var position = script.transform.position;
        for (int i = 0; i < count; i++)
        {
            Client.Instance.Send(new CoopPingPacket
            {
                PlayerId = Client.Instance.ID, Scene = ClientScene.LocalScene, CarLoaderID = int.Parse(parts[1]), PartKey = parts[2],
                Position = new Vector3Serializable(position.x, position.y, position.z),
            });
        }
        return new { sent = count };
    }

    [HarnessCommand("ping-markers")]
    private static object Markers(string args)
    {
        if ((args ?? "").Trim() == "clear")
        {
            CoopPings.Reset();
            return new { cleared = true };
        }
        return Dump();
    }

    [HarnessCommand("input-bindings")]
    private static object InputBindings(string args)
    {
        if (!ReInput.isReady) throw new InvalidOperationException("Rewired is not ready");
        string filter = (args ?? "").Trim();
        var rows = new List<object>();
        var players = new List<Player>();
        for (int id = 0; id < ReInput.players.playerCount; id++) players.Add(ReInput.players.GetPlayer(id));
        players.Add(ReInput.players.GetSystemPlayer());
        foreach (var player in players.Where(p => p != null))
        foreach (var type in new[] { ControllerType.Keyboard, ControllerType.Mouse })
        {
            var maps = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
            player.controllers.maps.GetAllMaps(type, maps);
            for (int m = 0; m < maps.Count; m++)
            {
                var map = maps[m];
                var category = ReInput.mapping.GetMapCategory(map.categoryId);
                foreach (var element in map.GetElementMaps())
                {
                    if (element == null) continue;
                    string action = ReInput.mapping.GetAction(element.actionId)?.name ?? element.actionId.ToString();
                    string binding = type == ControllerType.Keyboard ? element.keyCode.ToString() : element.elementIdentifierName;
                    if (filter.Length > 0 && !string.Equals(binding, filter, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(element.elementIdentifierName, filter, StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(new
                    {
                        player = player.name, controller = type.ToString(), category = category?.name ?? map.categoryId.ToString(),
                        mapEnabled = map.enabled, action, binding, element = element.elementIdentifierName, modifiers = element.hasModifiers,
                    });
                }
            }
        }
        return rows;
    }

    public static Dictionary<string, object> Dump()
    {
        var camera = Camera.main;
        return new Dictionary<string, object>
        {
            ["hotkey"] = PlayerSettings.PingKey.ToString(),
            ["defaultHotkey"] = PlayerSettings.DefaultPingKey,
            ["markers"] = PingMarkers.All.Select(m =>
            {
                bool projected = PingMarkers.TryScreenRect(m, camera, out var rect, out bool onScreen);
                return (object)new Dictionary<string, object>
                {
                    ["playerId"] = m.PlayerId, ["name"] = m.Name, ["own"] = m.Own, ["kind"] = m.IsPart ? "part" : "spot",
                    ["loader"] = m.Loader, ["key"] = m.Key, ["resolved"] = m.Resolved,
                    ["position"] = Vec(PingMarkers.WorldPoint(m)), ["sentPosition"] = Vec(m.Fallback),
                    ["anchor"] = m.Anchor == null ? null : Vec(m.Anchor.position), ["renderers"] = m.Renderers.Count,
                    ["flashed"] = m.Flashed != null, ["flashing"] = m.Flashed != null && m.Flashed.ho != null && m.Flashed.ho.flashing,
                    ["ageMs"] = Math.Round((Time.realtimeSinceStartup - m.ShownAt) * 1000f), ["remainingMs"] = Math.Round(m.Remaining * 1000f),
                    ["screen"] = projected ? new { x = Math.Round(rect.x), y = Math.Round(rect.y), w = Math.Round(rect.width), h = Math.Round(rect.height), onScreen } : null,
                };
            }).ToList(),
            ["counters"] = CoopPings.Counters.ToDictionary(c => c.Key, c => c.Value),
            ["lastSent"] = CoopPings.LastSent == null ? null : new
            {
                loader = CoopPings.LastSent.CarLoaderID, key = CoopPings.LastSent.PartKey, scene = CoopPings.LastSent.Scene.ToString(),
                position = new { x = CoopPings.LastSent.Position.X, y = CoopPings.LastSent.Position.Y, z = CoopPings.LastSent.Position.Z },
            },
        };
    }

    private static object Result(PingOutcome outcome) => new Dictionary<string, object>
    {
        ["outcome"] = outcome.ToString(),
        ["lastSent"] = CoopPings.LastSent == null ? null : new
        {
            loader = CoopPings.LastSent.CarLoaderID, key = CoopPings.LastSent.PartKey,
            position = new { x = CoopPings.LastSent.Position.X, y = CoopPings.LastSent.Position.Y, z = CoopPings.LastSent.Position.Z },
        },
    };

    private static Vector3 ParseVector(string args)
    {
        var values = (args ?? "").Split(',').Select(v => float.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (values.Length != 3) throw new ArgumentException("usage: ping-spot x,y,z");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static object Vec(Vector3 v) => new { x = Math.Round(v.x, 3), y = Math.Round(v.y, 3), z = Math.Round(v.z, 3) };
}
