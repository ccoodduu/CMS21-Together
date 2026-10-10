using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Car;
using UnityEngine;

namespace TogetherTestHarness.Features;

public static class CarPlacementCommands
{
    // car-place <loader> <CarPlace> [<CarPlace>...]: each place in turn within one frame, as a remote move does.
    [HarnessCommand("car-place")]
    private static object CarPlace(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("usage: car-place <loader> <CarPlace> [<CarPlace>...]");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        foreach (var name in parts.Skip(1))
        {
            carLoader.ResetCarLifter();
            carLoader.ChangePosition((int)ParsePlace(name));
        }
        return Placement(carLoader);
    }

    [HarnessCommand("car-move")]
    private static object CarMove(string args)
    {
        var (carLoader, place) = LoaderAndPlace(args, "usage: car-move <loader> <CarPlace>");
        var mode = GameMode.Get();
        // ChangeCarPos ends with SetCurrentMode(previousMode), which the skipped pie menu sets; a stale Interior throws.
        mode.previousMode = mode.currentMode;
        var center = NotificationCenter.Get();
        center.StartCoroutine(center.ChangeCarPos(carLoader, place, false));
        return "moving";
    }

    [HarnessCommand("lift")]
    private static object Lift(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || (parts[1] != "up" && parts[1] != "down")) throw new ArgumentException("usage: lift <index> up|down");
        var lifter = GarageLoader.Get().carLifter[int.Parse(parts[0])];
        var before = Lifter(int.Parse(parts[0]), lifter);
        lifter.Action(parts[1] == "up" ? 0 : 1);
        return new Dictionary<string, object> { ["before"] = before, ["after"] = Lifter(int.Parse(parts[0]), lifter) };
    }

    [HarnessCommand("lifters")]
    private static object Lifters(string args)
    {
        var lifters = GarageLoader.Get().carLifter;
        var result = new List<object>();
        for (int i = 0; i < lifters.Length; i++) result.Add(Lifter(i, lifters[i]));
        return result;
    }

    [HarnessCommand("placement")]
    private static object PlacementAll(string args)
    {
        var places = CarLoaderPlaces.Get();
        var result = new List<object>();
        for (int i = 0; i < places.GetCarLoadersCount(); i++)
        {
            var carLoader = places.GetCarLoaderByIndex(i);
            if (carLoader != null && !string.IsNullOrEmpty(carLoader.carToLoad)) result.Add(Placement(carLoader));
        }
        return result;
    }

    [HarnessCommand("park")]
    private static object Park(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "").Trim()));
        var center = NotificationCenter.Get();
        center.StartCoroutine(center.MoveCarToParking(carLoader));
        return "parking";
    }

    [HarnessCommand("unpark")]
    private static object Unpark(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: unpark <slot> <loader>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[1]));
        carLoader.StartCoroutine(carLoader.LoadCarFromFile(int.Parse(parts[0]), true));
        return "unparking";
    }

    [HarnessCommand("park-swap")]
    private static object ParkSwap(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: park-swap <from> <to>");
        CMS.Managers.ParkingCarPlaceManager.MoveCar(int.Parse(parts[0]), int.Parse(parts[1]));
        return "swapped";
    }

    [HarnessCommand("parking-unlock")]
    private static object ParkingUnlock(string args)
    {
        int levels = GlobalData.UnlockedParkingLevels;
        var request = new CMS21_Together_Core.Network.Packets.ParkingLevelUnlockRequestPacket { TargetLevels = levels + 1, Price = levels * 50000 };
        CMS21Together.Network.Client.Instance.Send(request);
        return new { target = request.TargetLevels, price = request.Price };
    }

    [HarnessCommand("park-incoming")]
    private static object ParkIncoming(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: park-incoming <slot with a car> <price>");
        var data = Singleton<GameManager>.Instance.GameDataManager.LoadCarInParking(int.Parse(parts[0]));
        if (data == null || data.IsDefault()) throw new ArgumentException($"slot {parts[0]} is empty");
        var request = new CMS21_Together_Core.Network.Packets.CarParkRequestPacket { RequestId = 9000, Car = NewCarDataCodec.ToParkedCar(data), Price = int.Parse(parts[1]) };
        CMS21Together.Network.Client.Instance.Send(request);
        return "sent";
    }

    [HarnessCommand("dev-spawn")]
    private static object DevSpawn(string args)
    {
        CMS21Together.Logic.Car.Placement.DevCarSpawner.SpawnRandom();
        return "spawning";
    }

    [HarnessCommand("parking")]
    private static object Parking(string args)
    {
        var data = Singleton<GameManager>.Instance.GameDataManager;
        var slots = new List<object>();
        int max = GlobalData.GetMaxParkingPlacesAmount();
        for (int i = 0; i < max; i++)
        {
            var car = data.LoadCarInParking(i);
            if (car != null && !car.IsDefault()) slots.Add(new { index = i, carToLoad = car.carToLoad, uid = car.UId });
        }
        return new Dictionary<string, object> { ["levels"] = GlobalData.UnlockedParkingLevels, ["max"] = max, ["slots"] = slots };
    }

    [HarnessCommand("parking-probe")]
    private static object ParkingProbe(string args)
    {
        int slot = int.Parse((args ?? "0").Trim());
        var data = Singleton<GameManager>.Instance.GameDataManager;
        var viaMethod = data.LoadCarInParking(slot);
        string viaArray;
        try { viaArray = data.CurrentProfileData.carsOnParking[slot]?.carToLoad ?? "(null)"; }
        catch (Exception e) { viaArray = "error: " + e.GetType().Name; }
        var result = new Dictionary<string, object>
        {
            ["slot"] = slot,
            ["viaLoadCarInParking"] = viaMethod?.carToLoad,
            ["viaCarsOnParking"] = viaArray,
            ["arrayLength"] = data.CurrentProfileData.carsOnParking?.Length ?? -1,
            ["emptyIsDefault"] = new NewCarData().IsDefault(),
            ["saveVersion"] = NewCarDataCodec.SaveVersion,
        };
        if (viaMethod != null && !viaMethod.IsDefault())
        {
            try
            {
                var bytes = NewCarDataCodec.Serialize(viaMethod);
                result["blobBytes"] = bytes.Length;
                var back = NewCarDataCodec.Deserialize(bytes, NewCarDataCodec.SaveVersion);
                result["roundTripCarToLoad"] = back.carToLoad;
                result["roundTripSameBytes"] = Enumerable.SequenceEqual(NewCarDataCodec.Serialize(back), bytes);
            }
            catch (Exception e) { result["codecError"] = e.Message.Split(new[] { '\n' }, 2)[0]; }
            try
            {
                var bytes = NewCarDataCodec.Serialize(viaMethod);
                var target = viaMethod.Clone();
                target.Deserialize(new Il2CppSystem.IO.BinaryReader(new Il2CppSystem.IO.MemoryStream(bytes)), NewCarDataCodec.SaveVersion);
                result["intoCloneCarToLoad"] = target.carToLoad;
            }
            catch (Exception e) { result["intoCloneError"] = e.Message.Split(new[] { '\n' }, 2)[0]; }
            try
            {
                var bytes = NewCarDataCodec.Serialize(viaMethod);
                var reader = new Il2CppSystem.IO.BinaryReader(new Il2CppSystem.IO.MemoryStream(bytes));
                result["readerFirstString"] = reader.ReadString();
            }
            catch (Exception e) { result["readerError"] = e.Message.Split(new[] { '\n' }, 2)[0]; }
        }
        return result;
    }

    // car-pose <loader> [part key]: the car root's yaw and the entrance angle, and the world position of a part (by
    // default the sub part farthest from the root, so another client can be asked for the same key).
    [HarnessCommand("car-pose")]
    private static object CarPose(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts.Length > 2) throw new ArgumentException("usage: car-pose <loader> [part key]");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        var root = CMS21Together.Logic.Visuals.VisualScope.CarRoot(carLoader);
        var registry = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader);
        string key = parts.Length > 1 ? parts[1]
            : registry.SubKeys.Where(k => registry.Sub(k) != null).OrderByDescending(k => Vector3.Distance(registry.Sub(k).transform.position, root.position)).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
        var part = key == null ? null : registry.Sub(key);
        var ground = carLoader.groundPosition;
        var rotation = ground == null ? null : ground.GetComponent<CarPlaceRotation>();
        return new Dictionary<string, object>
        {
            ["carToLoad"] = carLoader.carToLoad,
            ["place"] = ground == null ? null : CarLoaderPlaces.Get().TransformToCarPlace(ground).ToString(),
            ["placeRotation"] = rotation == null ? null : new[] { rotation.yawRange.x, rotation.yawRange.y },
            ["additionalCarRot"] = Math.Round(carLoader.additionalCarRot, 4),
            ["yaw"] = Math.Round(root.eulerAngles.y, 4),
            ["root"] = Precise(root.position),
            ["key"] = key,
            ["part"] = part == null ? null : Precise(part.transform.position),
        };
    }

    private static object Precise(Vector3 v) => new { x = Math.Round(v.x, 4), y = Math.Round(v.y, 4), z = Math.Round(v.z, 4) };

    private static (CarLoader, CarPlace) LoaderAndPlace(string args, string usage)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException(usage);
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        return (carLoader, ParsePlace(parts[1]));
    }

    private static CarPlace ParsePlace(string text) => int.TryParse(text, out int number) ? (CarPlace)number : (CarPlace)Enum.Parse(typeof(CarPlace), text);

    private static Dictionary<string, object> Lifter(int index, CarLifter lifter)
    {
        var connected = lifter.GetConnectedCarLoader();
        var places = CarLoaderPlaces.Get();
        string nearest = null;
        float best = float.MaxValue;
        foreach (var place in new[] { global::CarPlace.CarLifter1, global::CarPlace.CarLifter2 })
        {
            var t = places.GetPlaceTransform(place);
            if (t == null) continue;
            float d = Vector3.Distance(t.position, lifter.transform.position);
            if (d < best) { best = d; nearest = place.ToString(); }
        }
        return new Dictionary<string, object>
        {
            ["index"] = index,
            ["state"] = lifter.GetState().ToString(),
            ["isMoving"] = lifter.isMoving,
            ["applying"] = CMS21Together.Logic.Car.Placement.LifterSync.IsApplying(index),
            ["connectedLoader"] = connected == null ? -1 : places.GetCarLoaderId(connected),
            ["blocked"] = connected != null && connected.ToolsData.OilbinIsConnected || lifter.IsPlayerInside() && lifter.GetState() == CarLifterState.Up,
            ["connectedObject"] = lifter.connectedGameObject != null,
            ["active"] = lifter.gameObject.activeInHierarchy,
            ["nearestPlace"] = nearest,
            ["distance"] = Mathf.Round(best * 100f) / 100f,
        };
    }

    private static Dictionary<string, object> Placement(CarLoader carLoader)
    {
        var places = new List<string>();
        foreach (CarPlace place in Enum.GetValues(typeof(CarPlace)))
            if (carLoader.IsInPlace(place)) places.Add(place.ToString());
        return new Dictionary<string, object>
        {
            ["loader"] = CarLoaderPlaces.Get().GetCarLoaderId(carLoader),
            ["carToLoad"] = carLoader.carToLoad,
            ["placeNo"] = carLoader.GetPlaceNo(),
            ["inPlace"] = places,
        };
    }
}
