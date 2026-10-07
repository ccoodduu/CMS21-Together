using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CMS.UI;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car;
using CMS21Together.Logic.Driving;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using VehiclePhysics;

namespace TogetherTestHarness.Features;

// remote-visual-feedback part 2 harness: the dump section "remoteCars", drive-trace (logging only), the spike probes
// and the verbs that drive the local track car (VPP external input, or a push of the rigidbody when that has no effect).
public static class DriveCommands
{
    private const int TraceLimit = 500;
    private const int HistoryLimit = 1200;
    private const float HistoryInterval = 0.05f;

    private static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("together.harness.drive-trace");
    private static readonly List<string> trace = new List<string>();
    private static readonly Dictionary<MethodBase, string> names = new Dictionary<MethodBase, string>();
    private static readonly Dictionary<string, string> failures = new Dictionary<string, string>();
    private static readonly List<(float Time, Vector3 Position)> history = new List<(float, Vector3)>();
    private static bool patched;
    private static bool tracing;
    private static Input input;
    private static int inputSerial;
    private static byte[] savedBlob;
    private static string savedCar;
    private static int testCars;
    private static float lastHistory;
    private static bool historyRunning;

    private class Input
    {
        public int Serial;
        public float Throttle;
        public float Steer;
        public float Seconds;
        public float Started;
        public string Mode = "external";
        public bool Running = true;
        public bool Braking;
        public string State = "running";
    }

    private static readonly (string Type, string Method)[] Targets =
    {
        ("PieMenuController", "_GetOnClick_b__72_52"),
        ("CMS.UI.WindowManager", "Show"),
        ("GameMode", "SetCurrentMode"),
        ("NotificationCenter", "SelectSceneToLoad"),
        ("CMS.UI.Windows.MapWindow", "Show"),
        ("CMS.UI.Windows.MapWindow", "VerifyCarStateIfInterior"),
        ("CMS.UI.Windows.MapWindow", "SubmitPanelAction"),
        ("ParkingSpace", "DriveIn"),
        ("ParkingSpace", "DriveOut"),
        ("PrepareCarPhysics", "LoadCar"),
        ("PrepareCarPhysics", "EnableKinematic"),
        ("PrepareCarPhysics", "EnableGasPedal"),
        ("CarLoader", "PlaceAtPosition"),
    };

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    private static double Round(float f) => Math.Round(f, 3);
    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };

    internal static void Reset(List<string> changed)
    {
        if (tracing) changed.Add("drive-trace");
        if (input != null && input.Running) changed.Add("drive-input");
        tracing = false;
        trace.Clear();
        if (input != null) input.Running = false;
        input = null;
        for (int id = 9000; id < 9000 + testCars; id++) RemoteCars.Remove(id, "harness reset");
        testCars = 0;
        history.Clear();
    }

    public static Dictionary<string, object> Dump()
    {
        return new Dictionary<string, object>
        {
            ["local"] = new Dictionary<string, object>
            {
                ["active"] = DriveCapture.Active,
                ["driveId"] = DriveCapture.DriveId,
                ["drives"] = DriveCapture.Drives,
                ["sent"] = DriveCapture.Sent,
                ["dropped"] = DriveCapture.Dropped,
                ["time"] = Round(Time.time),
                ["position"] = DriveCapture.Active ? Vec(DriveInterpolator.Pos(DriveCapture.Last)) : null,
            },
            ["statesReceived"] = RemoteCars.StatesReceived,
            ["statesIgnored"] = RemoteCars.StatesIgnored,
            ["cars"] = RemoteCars.All.Select(Car).ToList(),
        };
    }

    private static object Car(RemoteCar car)
    {
        var interpolator = car.Interpolator;
        return new Dictionary<string, object>
        {
            ["playerId"] = car.PlayerId,
            ["driveId"] = car.DriveId,
            ["carToLoad"] = car.Start.CarToLoad,
            ["carLoaderId"] = car.Start.CarLoaderID,
            ["mode"] = car.Mode,
            ["route"] = car.Route,
            ["ready"] = car.Ready,
            ["visible"] = car.Ready && car.Root.gameObject.activeInHierarchy,
            ["position"] = car.Ready ? Vec(car.Root.position) : null,
            ["yaw"] = car.Ready ? Round(car.Root.eulerAngles.y) : 0,
            ["speed"] = Round(interpolator.Velocity.magnitude),
            ["renderTime"] = Round(interpolator.RenderTime),
            ["newestTime"] = Round(interpolator.Newest.Time),
            ["bufferMs"] = Round(interpolator.BufferMs),
            ["extrapolatedMs"] = Round(interpolator.ExtrapolatedMs),
            ["maxExtrapolatedMs"] = Round(interpolator.MaxExtrapolatedMs),
            ["snaps"] = interpolator.Snaps,
            ["late"] = interpolator.Late,
            ["statesReceived"] = interpolator.Received,
            ["kinematic"] = car.Kinematic,
            ["collidersOff"] = car.CollidersOff,
            ["disabledScripts"] = car.DisabledScripts,
            ["wheels"] = car.Wheels.Count,
            ["engine"] = car.Engine != null && car.Engine.IsAlive,
            ["rpm"] = car.Engine != null && car.Engine.IsAlive ? Round(car.Engine.Res.engineCurrentRPM) : 0,
            ["buildSeconds"] = Round(car.BuildSeconds),
            ["buildMb"] = Round(car.BuildBytes / 1048576f),
        };
    }

    [HarnessCommand("drive-trace")]
    private static object Trace(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on": PatchAll(); tracing = true; trace.Clear(); break;
            case "off": tracing = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: drive-trace on|off|report");
        }
        return new Dictionary<string, object> { ["tracing"] = tracing, ["failures"] = failures, ["events"] = trace.ToList() };
    }

    private static void PatchAll()
    {
        if (patched) return;
        patched = true;
        var prefix = new HarmonyMethod(typeof(DriveCommands).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
        foreach (var target in Targets)
        {
            string name = $"{target.Type}.{target.Method}";
            try
            {
                var type = AccessTools.TypeByName(target.Type) ?? throw new TypeLoadException(target.Type);
                var methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == target.Method).ToList();
                if (methods.Count == 0) throw new MissingMethodException(name);
                foreach (var method in methods)
                {
                    names[method] = name;
                    harmony.Patch(method, prefix: prefix);
                }
            }
            catch (Exception e)
            {
                failures[name] = e.GetType().Name + ": " + e.Message.Split('\n')[0];
                MelonLogger.Warning($"[Harness] drive-trace cannot patch {name}: {failures[name]}");
            }
        }
    }

    private static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        if (!tracing) return;
        string key = names.TryGetValue(__originalMethod, out string known) ? known : __originalMethod.Name;
        string arguments = __args == null ? "" : string.Join(", ", __args.Select(a => a?.ToString() ?? "null"));
        string line = $"{Time.time:F2} {key}({arguments}) mode {GameMode.Get()?.GetCurrentMode()} scene {ClientScene.LocalScene}";
        MelonLogger.Msg($"[Harness] drive-trace {line}");
        if (trace.Count < TraceLimit) trace.Add(line);
    }

    private static object GarageSnapshot()
    {
        var places = CarLoaderPlaces.Get();
        var loaders = new List<object>();
        for (int i = 0; places != null && i < places.GetCarLoadersCount(); i++)
        {
            var carLoader = places.GetCarLoaderByIndex(i);
            if (carLoader == null || !carLoader.IsCarLoaded()) continue;
            loaders.Add(new { loader = i, place = carLoader.GetPlaceNo(), position = Vec(carLoader.GetRootPosition()) });
        }
        var windows = WindowManager.Instance;
        return new
        {
            mode = GameMode.Get()?.GetCurrentMode().ToString(),
            scene = ClientScene.LocalScene.ToString(),
            mapActive = windows != null && windows.IsWindowActive(WindowID.Map),
            loaders,
        };
    }

    [HarnessCommand("drive-pie")]
    private static object Pie(string args)
    {
        if ((args ?? "").Trim() == "close")
        {
            bool hidden = WindowManager.Instance != null && WindowManager.Instance.Hide(WindowID.Map, true);
            return new { hidden, after = GarageSnapshot() };
        }
        var pie = UnityEngine.Object.FindObjectOfType<PieMenuController>() ?? throw new InvalidOperationException("no PieMenuController");
        var before = GarageSnapshot();
        bool returned = pie._GetOnClick_b__72_52();
        return new { returned, before, after = GarageSnapshot() };
    }

    private static PrepareCarPhysics TrackCar() => PrepareCarPhysics.Get() ?? throw new InvalidOperationException("no PrepareCarPhysics (not on the track)");

    private static string PathOf(Transform t)
    {
        if (t == null || !t) return null;
        var parts = new List<string>();
        for (var p = t; p != null && parts.Count < 8; p = p.parent) parts.Insert(0, p.name);
        return string.Join("/", parts);
    }

    [HarnessCommand("drive-probe")]
    private static object Probe(string args)
    {
        var physics = TrackCar();
        var carLoader = physics.CarLoader;
        var root = carLoader == null ? null : carLoader.GetRootTransform();
        var model = physics.carModel;
        var body = physics.rigidBody;
        var vehicle = physics.VehicleController;
        var result = new Dictionary<string, object>
        {
            ["mode"] = GameMode.Get()?.GetCurrentMode().ToString(),
            ["physics"] = PathOf(physics.transform),
            ["carLoader"] = carLoader == null ? null : PathOf(carLoader.transform),
            ["carLoaderName"] = carLoader == null ? null : carLoader.gameObject.name,
            ["carLoaders"] = UnityEngine.Object.FindObjectsOfType<CarLoader>().Select(c => PathOf(c.transform)).ToList(),
            ["root"] = PathOf(root),
            ["rootPosition"] = root == null ? null : Vec(root.position),
            ["carModel"] = PathOf(model),
            ["carModelPosition"] = model == null ? null : Vec(model.position),
            ["body"] = body == null ? null : PathOf(body.transform),
            ["bodyPosition"] = body == null ? null : Vec(body.position),
            ["velocity"] = body == null ? null : Vec(body.velocity),
            ["kinematic"] = body != null && body.isKinematic,
            ["captureBody"] = PathOf(DriveCapture.BodyOf(physics)),
            ["firstPart"] = carLoader != null && carLoader.carParts != null && carLoader.carParts.Count > 0 && carLoader.carParts[0].handle != null
                ? PathOf(carLoader.carParts[0].handle.transform) : null,
            ["wheelHandles"] = carLoader == null ? null : new[] { carLoader.GetWheelFLHandle(), carLoader.GetWheelFRHandle(), carLoader.GetWheelRLHandle(), carLoader.GetWheelRRHandle() }
                .Select(w => w == null ? null : PathOf(w.transform)).ToList(),
            ["res"] = physics.res == null ? null : (object)Round(physics.res.engineCurrentRPM),
            ["capture"] = new { DriveCapture.Active, DriveCapture.DriveId, DriveCapture.Sent },
            ["input"] = input == null ? null : new { input.Mode, input.State, input.Running, input.Throttle, input.Steer, elapsed = Round(Time.time - input.Started) },
        };
        if (vehicle != null)
        {
            result["vehicle"] = PathOf(vehicle.transform);
            result["initialized"] = vehicle.initialized;
            result["components"] = vehicle.GetComponents<MonoBehaviour>().Select(c => c.GetIl2CppType().Name).ToList();
            var data = vehicle.data;
            if (data != null)
                result["bus"] = new
                {
                    speed = data.Get(Channel.Vehicle, VehicleData.Speed),
                    rpm = data.Get(Channel.Vehicle, VehicleData.EngineRpm),
                    working = data.Get(Channel.Vehicle, VehicleData.EngineWorking),
                    gear = data.Get(Channel.Vehicle, VehicleData.GearboxGear),
                    gearMode = data.Get(Channel.Vehicle, VehicleData.GearboxMode),
                    steerIn = data.Get(Channel.Input, InputData.Steer),
                    throttleIn = data.Get(Channel.Input, InputData.Throttle),
                    brakeIn = data.Get(Channel.Input, InputData.Brake),
                    key = data.Get(Channel.Input, InputData.Key),
                };
            var wheels = vehicle.wheelState;
            result["wheels"] = wheels == null ? null : wheels.Select(w => (object)new
            {
                steerable = w.steerable,
                steer = Round(w.steerAngle),
                spin = Round(w.angularVelocity),
                transform = w.wheelCol == null ? null : PathOf(w.wheelCol.wheelTransform),
            }).ToList();
            result["inputs"] = vehicle.GetComponentsInChildren<VPStandardInput>(true).Select(i => (object)new
            {
                i.enabled, throttle = Round(i.externalThrottle), brake = Round(i.externalBrake), steer = Round(i.externalSteer)
            }).ToList();
        }
        if (DriveCapture.TryRead(physics, out var state))
            result["state"] = new { pos = Vec(DriveInterpolator.Pos(state)), steer = Round(state.SteerDegrees), wheel = Round(state.WheelRadPerSecond), rpm = Round(state.Rpm), state.Gear, flags = state.Flags.ToString() };
        return result;
    }

    [HarnessCommand("drive-input")]
    private static object DriveInput(string args)
    {
        var a = Args(args);
        if (a.Length != 3) throw new ArgumentException("usage: drive-input <throttle> <steer> <seconds>");
        TrackCar();
        if (input != null) input.Running = false;
        input = new Input { Serial = ++inputSerial, Throttle = F(a[0]), Steer = F(a[1]), Seconds = F(a[2]), Started = Time.time };
        EnsureHistory();
        MelonCoroutines.Start(RunInput(input));
        return new { input.Serial, input.Throttle, input.Steer, input.Seconds };
    }

    [HarnessCommand("drive-stop")]
    private static object DriveStop(string args)
    {
        TrackCar();
        if (input != null) input.Running = false;
        input = new Input { Serial = ++inputSerial, Seconds = 8f, Started = Time.time, Braking = true, State = "braking" };
        MelonCoroutines.Start(RunInput(input));
        return new { input.Serial, braking = true };
    }

    [HarnessCommand("drive-input-state")]
    private static object InputState(string args) => input == null ? null : new { input.Serial, input.Mode, input.State, input.Running, input.Braking };

    private static IEnumerator RunInput(Input run)
    {
        float lastPush = Time.time;
        while (run.Running && input == run)
        {
            var physics = PrepareCarPhysics.Get();
            if (physics == null || !physics)
            {
                run.State = "no track car";
                break;
            }
            var vehicle = physics.VehicleController;
            var body = physics.rigidBody;
            float elapsed = Time.time - run.Started;
            if (elapsed >= run.Seconds)
            {
                run.State = "done";
                break;
            }
            float speed = body == null ? 0f : body.velocity.magnitude;
            if (run.Braking)
            {
                SetExternal(vehicle, 0f, 1f, 0f);
                if (run.Mode == "push" || elapsed > 2f && speed > 0.05f && body != null)
                {
                    body.velocity = Vector3.MoveTowards(body.velocity, Vector3.zero, 20f * Time.deltaTime);
                    body.angularVelocity = Vector3.zero;
                }
                if (speed < 0.05f && elapsed > 0.5f)
                {
                    run.State = "stopped";
                    break;
                }
            }
            else
            {
                SetExternal(vehicle, run.Throttle, 0f, run.Steer);
                if (run.Mode == "external" && elapsed > 1.5f && speed < 0.5f && Mathf.Abs(run.Throttle) > 0.01f) run.Mode = "push";
                if (run.Mode == "push" && body != null)
                {
                    float dt = Time.time - lastPush;
                    var forward = body.transform.forward;
                    var target = forward * (run.Throttle * 15f);
                    body.velocity = new Vector3(target.x, body.velocity.y, target.z);
                    body.MoveRotation(body.rotation * Quaternion.Euler(0f, run.Steer * 30f * dt, 0f));
                }
            }
            lastPush = Time.time;
            yield return null;
        }
        var still = PrepareCarPhysics.Get();
        if (still != null && still) SetExternal(still.VehicleController, 0f, run.Braking ? 1f : 0f, 0f);
        run.Running = false;
    }

    private static void SetExternal(VPVehicleController vehicle, float throttle, float brake, float steer)
    {
        if (vehicle == null) return;
        foreach (var standard in vehicle.GetComponentsInChildren<VPStandardInput>(true))
        {
            standard.externalThrottle = Mathf.Max(0f, throttle);
            standard.reverse = throttle < 0f;
            standard.externalBrake = brake;
            standard.externalSteer = steer;
        }
    }

    private static void EnsureHistory()
    {
        if (historyRunning) return;
        historyRunning = true;
        MelonCoroutines.Start(RecordHistory());
    }

    private static IEnumerator RecordHistory()
    {
        while (true)
        {
            var physics = PrepareCarPhysics.Get();
            if (physics != null && physics && Time.time - lastHistory >= HistoryInterval)
            {
                lastHistory = Time.time;
                history.Add((Time.time, DriveCapture.BodyOf(physics).position));
                if (history.Count > HistoryLimit) history.RemoveAt(0);
            }
            yield return null;
        }
    }

    [HarnessCommand("drive-history")]
    private static object History(string args)
    {
        var a = Args(args);
        if (a.Length != 1) throw new ArgumentException("usage: drive-history <time>");
        float time = F(a[0]);
        if (history.Count == 0) throw new InvalidOperationException("no history (drive-input first)");
        int i = history.FindIndex(h => h.Time >= time);
        Vector3 position;
        if (i < 0) position = history[history.Count - 1].Position;
        else if (i == 0) position = history[0].Position;
        else
        {
            var before = history[i - 1];
            var after = history[i];
            position = Vector3.Lerp(before.Position, after.Position, (time - before.Time) / Mathf.Max(after.Time - before.Time, 1e-4f));
        }
        return new { time = Round(time), first = Round(history[0].Time), last = Round(history[history.Count - 1].Time), position = Vec(position), now = Round(Time.time) };
    }

    [HarnessCommand("drive-codec-check")]
    private static object CodecCheck(string args)
    {
        var random = new System.Random(17);
        float R(float range) => (float)(random.NextDouble() * 2 - 1) * range;
        double maxPos = 0, maxRotDeg = 0, maxVel = 0, maxSteer = 0, maxWheel = 0, maxRpm = 0;
        int gearErrors = 0, flagErrors = 0;
        for (int n = 0; n < 1000; n++)
        {
            var rotation = UnityEngine.Random.rotationUniform;
            var s = new DriveState
            {
                Time = Math.Abs(R(10000)), PosX = R(3000), PosY = R(200), PosZ = R(3000),
                RotX = rotation.x, RotY = rotation.y, RotZ = rotation.z, RotW = rotation.w,
                VelX = R(90), VelY = R(20), VelZ = R(90), SteerDegrees = R(45), WheelRadPerSecond = R(300), Rpm = Math.Abs(R(9000)),
                Gear = random.Next(-1, 7), Flags = (DriveFlags)random.Next(0, 16)
            };
            if (!DriveStateCodec.TryDecode(DriveStateCodec.Encode(s), out var d)) throw new InvalidOperationException("decode failed");
            maxPos = Math.Max(maxPos, (DriveInterpolator.Pos(s) - DriveInterpolator.Pos(d)).magnitude);
            maxRotDeg = Math.Max(maxRotDeg, Quaternion.Angle(DriveInterpolator.Rot(s), DriveInterpolator.Rot(d)));
            maxVel = Math.Max(maxVel, (DriveInterpolator.Vel(s) - DriveInterpolator.Vel(d)).magnitude);
            maxSteer = Math.Max(maxSteer, Math.Abs(s.SteerDegrees - d.SteerDegrees));
            maxWheel = Math.Max(maxWheel, Math.Abs(s.WheelRadPerSecond - d.WheelRadPerSecond));
            maxRpm = Math.Max(maxRpm, Math.Abs(s.Rpm - d.Rpm));
            if (s.Gear != d.Gear) gearErrors++;
            if (s.Flags != d.Flags) flagErrors++;
        }
        bool passed = maxPos < 1e-3 && maxRotDeg < 0.05 && maxVel <= 0.01 && maxSteer <= 0.25 && maxWheel <= 0.006 && maxRpm <= 0.5 && gearErrors == 0 && flagErrors == 0;
        return new { passed, size = DriveStateCodec.Size, maxPos, maxRotDeg, maxVel, maxSteer, maxWheel, maxRpm, gearErrors, flagErrors };
    }

    [HarnessCommand("drive-blob")]
    private static object Blob(string args)
    {
        int loader = int.Parse((args ?? "0").Trim());
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        var data = Singleton<GameManager>.Instance.GameDataManager.LoadCarInGarage(Helper.GetIndexFromCarLoaderName(carLoader.GetSaveName()));
        if (data == null || data.IsDefault()) throw new InvalidOperationException("no saved data for that car (save it first)");
        savedBlob = NewCarDataCodec.Serialize(data);
        savedCar = data.carToLoad;
        return new { car = savedCar, bytes = savedBlob.Length };
    }

    [HarnessCommand("drive-ghost-test")]
    private static object GhostTest(string args)
    {
        string route = (args ?? "").Trim();
        if (route == "clear")
        {
            for (int test = 9000; test < 9000 + testCars; test++) RemoteCars.Remove(test, "harness");
            testCars = 0;
            return "cleared";
        }
        if (route != "clone" && route != "new" && route != "base") throw new ArgumentException("usage: drive-ghost-test clone|new|base|clear");
        if (savedBlob == null && route != "base") throw new InvalidOperationException("no blob (drive-blob <loader> in the garage first)");
        var physics = TrackCar();
        var body = DriveCapture.BodyOf(physics);
        int id = 9000 + testCars++;
        var packet = new CarDriveStartPacket
        {
            PlayerId = id, DriveId = id, Scene = ClientScene.LocalScene, CarToLoad = savedCar ?? physics.CarLoader.carToLoad,
            CarBlob = savedBlob, CarBlobVersion = NewCarDataCodec.SaveVersion
        };
        RemoteCars.Spawn(packet, route);
        var position = body.position + body.right * (4f * testCars);
        var state = new DriveState
        {
            Time = Time.time, PosX = position.x, PosY = position.y, PosZ = position.z,
            RotX = body.rotation.x, RotY = body.rotation.y, RotZ = body.rotation.z, RotW = body.rotation.w, Rpm = 900f
        };
        RemoteCars.Push(id, state);
        return new { playerId = id, route, car = packet.CarToLoad, at = Vec(position) };
    }

    [HarnessCommand("drive-start")]
    private static object Start(string args)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse((args ?? "").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car (garage driving does not exist; drive-start takes a garage car to the test track)");
        GlobalData.SelectedCarLoader = carLoader.gameObject.name;
        GlobalData.TestToShow = "ExamineReport";
        var center = NotificationCenter.m_instance;
        center.StartCoroutine(center.SelectSceneToLoad("Test_track_1", SceneType.TestTrack, true, true));
        return new { selected = GlobalData.SelectedCarLoader };
    }
}
