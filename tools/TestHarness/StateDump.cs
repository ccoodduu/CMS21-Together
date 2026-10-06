using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;
using CMS21Together.Session;
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
            ["joinStatus"] = ConnectionStatus.State.ToString(),
            ["lastDisconnect"] = Features.JoinCommands.LastDisconnect(),
            ["remotePlayers"] = PresenceManager.VisibleAvatarCount,
            ["isOrderGenerator"] = CMS21Together.Logic.Jobs.JobsSync.IsGenerator,
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
        dump["away"] = CMS21Together.Logic.Car.Away.CarAwaySync.All.OrderBy(a => a.Key)
            .Select(a => (object)new { loader = a.Key, kind = a.Value.Kind.ToString(), owner = a.Value.Owner }).ToList();
        dump["placement"] = Placement();
        dump["jobs"] = Jobs();
        dump["players"] = PresenceManager.Roster.Where(p => p.Value.HasAvatar).ToDictionary(
            p => p.Key.ToString(),
            p => (object)Vec(p.Value.Avatar.transform.position));
        dump["local"] = Local();
        dump["session"] = Features.JoinCommands.Session();
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
            var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == i);
            var car = new Dictionary<string, object>
            {
                ["index"] = i,
                ["placeNo"] = loader.placeNo,
                ["carToLoad"] = string.IsNullOrEmpty(loader.carToLoad) ? null : loader.carToLoad,
                ["customerCar"] = loader.customerCar,
                ["spawnSeq"] = sync?.SpawnSeq ?? 0,
                ["syncState"] = sync?.State.ToString() ?? "Empty",
                ["revision"] = sync?.Revision ?? 0,
                ["specialState"] = loader.specialState,
                ["claims"] = PartClaims.Held(i).OrderBy(c => c.Key, StringComparer.Ordinal)
                    .Select(c => new { key = c.Key, owner = c.Value }).ToList(),
            };
            if (!string.IsNullOrEmpty(loader.carToLoad) && loader.IsCarLoaded())
            {
                var registry = PartRegistry.Build(loader);
                var body = new List<CarBodyPartUpdatePacket>();
                var sub = new List<CarSubPartUpdatePacket>();
                CarPartsSync.CaptureAll(loader, registry, body, sub);
                car["registryHash"] = registry.Hash();
                car["bodyParts"] = body.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => new
                {
                    key = b.Key,
                    name = b.PartName,
                    unmounted = b.Unmounted,
                    switched = b.Switched,
                    condition = Round(b.State?.Condition ?? 0f),
                    dent = Round(b.State?.Dent ?? 0f),
                    quality = b.State?.Quality ?? 0,
                    tunedId = b.TunedID,
                }).ToList();
                car["subParts"] = sub.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new
                {
                    key = s.Key,
                    id = s.PartId,
                    unmounted = s.Unmounted,
                    condition = Round(s.Condition),
                    quality = s.Quality,
                    examined = s.IsExamined,
                    dust = Round(s.Dust),
                    blocked = registry.Sub(s.Key)?.IsBlocked() ?? false,
                }).ToList();
            }
            cars.Add(car);
        }
        return cars;
    }

    private static object Jobs()
    {
        var generator = Singleton<GameManager>.Instance?.OrderGenerator;
        if (generator == null) return null;
        var orders = new List<(int Id, object Row)>();
        for (int i = 0; generator.jobs != null && i < generator.jobs.Count; i++)
            orders.Add((generator.jobs[i].id, new { generator.jobs[i].id, generator.jobs[i].carFile, generator.jobs[i].IsMission }));
        var active = new List<object>();
        for (int i = 0; generator.selectedJobs != null && i < generator.selectedJobs.Count; i++)
            active.Add(new { generator.selectedJobs[i].id, generator.selectedJobs[i].carFile, generator.selectedJobs[i].carLoaderID });
        return new { openCount = GlobalData.Jobs, orders = orders.OrderBy(o => o.Id).Select(o => o.Row).ToList(), active };
    }

    private static object Placement()
    {
        var places = CarLoaderPlaces.Get();
        var garage = GarageLoader.Get();
        var data = Singleton<GameManager>.Instance?.GameDataManager;
        if (places == null || garage == null || data == null) return null;

        var lifters = new List<object>();
        for (int i = 0; garage.carLifter != null && i < garage.carLifter.Length; i++)
        {
            var connected = garage.carLifter[i].GetConnectedCarLoader();
            lifters.Add(new { index = i, state = garage.carLifter[i].GetState().ToString(), car = connected == null ? -1 : places.GetCarLoaderId(connected) });
        }
        var cars = new List<object>();
        for (int i = 0; i < places.GetCarLoadersCount(); i++)
        {
            var carLoader = places.GetCarLoaderByIndex(i);
            if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) continue;
            string inPlace = null;
            foreach (CarPlace place in System.Enum.GetValues(typeof(CarPlace)))
                if (carLoader.IsInPlace(place)) inPlace = place.ToString();
            cars.Add(new { loader = i, carLoader.carToLoad, placeNo = carLoader.GetPlaceNo(), inPlace });
        }
        var slots = new List<object>();
        for (int i = 0; i < GlobalData.GetMaxParkingPlacesAmount(); i++)
        {
            var car = data.LoadCarInParking(i);
            if (car != null && !car.IsDefault()) slots.Add(new { index = i, car.carToLoad });
        }
        return new { lifters, cars, parking = new { levels = GlobalData.UnlockedParkingLevels, slots } };
    }

    private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };
}
