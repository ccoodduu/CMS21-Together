using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using UnityEngine;

namespace TogetherTestHarness;

public static class StateDump
{
    public static Dictionary<string, object> Status()
    {
        var client = Client.Instance;
        return new Dictionary<string, object>
        {
            ["instance"] = HarnessMod.InstanceName,
            ["time"] = Time.unscaledTime,
            ["scene"] = SceneState.Current,
            ["playable"] = SceneState.Playable,
            ["connected"] = client != null && client.IsConnected,
            ["connectionValid"] = client != null && client.IsConnectionValid,
            ["playerId"] = client?.ID ?? 0,
            ["initialSyncFinished"] = ClientData.IsInitialSyncFinished,
            ["snapshotId"] = SyncTracker.CurrentSnapshotId,
            ["syncAcked"] = SyncTracker.Acked,
            ["remotePlayers"] = PresenceManager.VisibleAvatarCount,
        };
    }

    public static void WriteStatus()
    {
        try
        {
            CommandChannel.WriteJson(Path.Combine(HarnessMod.Dir, "status.json"), Status());
        }
        catch (IOException)
        {
        }
    }

    public static Dictionary<string, object> Full()
    {
        var dump = Status();
        dump["stats"] = new Dictionary<string, object>
        {
            ["money"] = GlobalData.PlayerMoney,
            ["scrap"] = GlobalData.PlayerScraps,
            ["exp"] = GlobalData.PlayerExp,
            ["level"] = GlobalData.PlayerLevel,
        };
        dump["inventory"] = Inventory();
        dump["cars"] = Cars();
        dump["players"] = PresenceManager.Roster.Where(p => p.Value.HasAvatar).ToDictionary(
            p => p.Key.ToString(),
            p => (object)Vec(p.Value.Avatar.transform.position));
        dump["local"] = Local();
        dump["roster"] = PresenceManager.Roster.ToDictionary(p => p.Key.ToString(), p => (object)new
        {
            name = p.Value.Record.Username,
            scene = p.Value.Record.Scene.ToString(),
            seat = p.Value.Record.SeatCarLoaderId,
            engineRunning = p.Value.Record.EngineRunning,
            avatarActive = p.Value.HasAvatar && p.Value.Avatar.gameObject.activeSelf,
            avatarPosition = p.Value.HasAvatar ? Vec(p.Value.Avatar.transform.position) : null,
            nameTag = NameTags.IsDrawn(p.Value),
        });
        return dump;
    }

    private static object Local()
    {
        if (!PresenceManager.HasLocalMotor || Client.Instance == null || !Client.Instance.IsConnected)
            return new { scene = ClientScene.LocalScene.ToString(), name = PlayerSettings.PlayerName };
        var movement = Movement.CaptureLocal();
        return new
        {
            position = new { x = Round(movement.Position.X), y = Round(movement.Position.Y), z = Round(movement.Position.Z) },
            scene = ClientScene.LocalScene.ToString(),
            name = PlayerSettings.PlayerName,
        };
    }

    private static object Inventory()
    {
        var manager = Singleton<GameManager>.Instance;
        var inventory = manager == null ? null : manager.Inventory;
        if (inventory == null) return null;

        var items = new List<Item>();
        foreach (var item in inventory.items) items.Add(item);

        var groups = new List<GroupItem>();
        foreach (var group in inventory.groups) groups.Add(group);

        return new
        {
            items = items.OrderBy(i => i.ID).ThenBy(i => i.UID)
                .Select(i => new { i.ID, i.UID, condition = Round(i.Condition) }).ToList(),
            groups = groups.OrderBy(g => g.ID).ThenBy(g => g.UID)
                .Select(g => new { g.ID, g.UID, size = g.ItemList?.Count ?? 0 }).ToList(),
        };
    }

    private static object Cars()
    {
        var game = GameScript.Get();
        if (game == null || game.carOnScene == null) return null;

        var cars = new List<object>();
        for (int i = 0; i < game.carOnScene.Length; i++)
        {
            var loader = game.carOnScene[i];
            if (loader == null) continue;
            var parts = new List<object>();
            if (loader.carParts != null)
                foreach (var part in loader.carParts)
                    parts.Add(new { part.name, part.Unmounted, condition = Round(part.Condition), part.Switched });

            cars.Add(new
            {
                index = i,
                loader.placeNo,
                carToLoad = string.IsNullOrEmpty(loader.carToLoad) ? null : loader.carToLoad,
                loader.customerCar,
                bodyParts = parts,
            });
        }
        return cars;
    }

    private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };
}
