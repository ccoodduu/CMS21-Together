using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Driving;
using MelonLoader;
using UnityEngine;
using VehiclePhysics;

namespace TogetherTestHarness.Features;

// track-collisions harness: remote-collider (state of each observer copy's box), collide-set (switches), test copies
// placed relative to the local car (collide-ghost*), collide-cruise (holds the local car at a speed until it touches a
// copy and records what happens after), and the spike probes (layers, wheel ground hits, a raw box, checkpoints).
public static class CollideCommands
{
    private const int FirstTestId = 9500;
    private static int testCars;
    private static GameObject testBox;
    private static Cruise cruise;
    private static bool holding;
    private static bool tracing;
    private static bool watching;
    private static readonly List<object> trace = new List<object>();

    private class Cruise
    {
        public float Kmh;
        public float Seconds;
        public float Started;
        public bool Running = true;
        public string State = "cruising";
        public Vector3 Start;
        public Vector3 Forward;
        public float MaxSpeed;
        public float ContactSpeed = -1f;
        public float ContactAt = -1f;
        public float MinSpeedAfter = -1f;
        public float SpeedAfter300 = -1f;
        public float EndSpeed;
        public float Travelled;
        public float TravelledAtContact = -1f;
        public float MaxFrameMs;
        public int TouchedId = -1;
    }

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    private static double Round(float f) => Math.Round(f, 3);
    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };
    private static long WallMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    internal static void Reset(List<string> changed)
    {
        if (RemoteCollider.Forced != null || !PlayerSettings.TrackCollisions || RemoteCollider.DepenetrationCap != RemoteCollider.DefaultDepenetrationCap || holding || tracing) changed.Add("collide-set");
        RemoteCollider.Forced = null;
        PlayerSettings.TrackCollisions = true;
        RemoteCollider.DepenetrationCap = RemoteCollider.DefaultDepenetrationCap;
        holding = false;
        tracing = false;
        trace.Clear();
        for (int id = FirstTestId; id < FirstTestId + testCars; id++) RemoteCars.Remove(id, "harness reset");
        testCars = 0;
        if (testBox != null && testBox) UnityEngine.Object.Destroy(testBox);
        testBox = null;
        if (cruise != null) cruise.Running = false;
        cruise = null;
    }

    private static PrepareCarPhysics TrackCar()
    {
        var physics = PrepareCarPhysics.Get();
        if (physics == null || !physics || physics.rigidBody == null) throw new InvalidOperationException("no track car");
        return physics;
    }

    public static object Car(RemoteCar car)
    {
        var state = car.Collider;
        if (state == null) return new Dictionary<string, object> { ["playerId"] = car.PlayerId, ["built"] = false, ["reason"] = car.Ready ? "building" : "loading" };
        var local = RemoteCollider.LocalBody;
        return new Dictionary<string, object>
        {
            ["playerId"] = car.PlayerId,
            ["built"] = true,
            ["enabled"] = state.Enabled,
            ["reason"] = state.Reason,
            ["overlap"] = state.Overlap,
            ["touching"] = state.Touching,
            ["contacts"] = state.Contacts,
            ["lastContactAgo"] = state.LastContactAt < 0f ? -1 : Round(Time.time - state.LastContactAt),
            ["size"] = Vec(state.Size),
            ["center"] = Vec(state.Center),
            ["renderers"] = state.Renderers,
            ["layer"] = state.Object != null && state.Object ? state.Object.layer : -1,
            ["boxPosition"] = state.Body != null && state.Body ? Vec(state.Body.position) : null,
            ["copyPosition"] = Vec(car.Interpolator.Position),
            ["copySpeed"] = Round(car.Interpolator.Velocity.magnitude),
            ["renderTime"] = Round(car.Interpolator.RenderTime),
            ["snaps"] = car.Interpolator.Snaps,
            ["toLocal"] = local != null && local ? Round((car.Interpolator.Position - local.position).magnitude) : -1,
            ["time"] = Round(Time.time),
            ["wallMs"] = WallMs,
        };
    }

    [HarnessCommand("remote-collider")]
    private static object State(string args)
    {
        string arg = (args ?? "").Trim();
        var cars = RemoteCars.All.Where(c => arg == "" || c.PlayerId.ToString() == arg).Select(Car).ToList();
        return new Dictionary<string, object>
        {
            ["setting"] = PlayerSettings.TrackCollisions,
            ["hostOff"] = ClientData.ServerInfo?.TrackCollisionsOff == true,
            ["cap"] = RemoteCollider.DepenetrationCap,
            ["forced"] = RemoteCollider.Forced?.ToString(),
            ["layer"] = RemoteCollider.Layer,
            ["localMask"] = RemoteCollider.LocalMask,
            ["cars"] = cars,
        };
    }

    [HarnessCommand("collide-set")]
    private static object Set(string args)
    {
        var a = Args(args);
        if (a.Length != 2) throw new ArgumentException("usage: collide-set setting on|off | forced on|off|auto");
        switch (a[0])
        {
            case "setting": PlayerSettings.TrackCollisions = a[1] == "on"; break;
            case "cap": RemoteCollider.DepenetrationCap = F(a[1]); break;
            case "forced": RemoteCollider.Forced = a[1] == "auto" ? (bool?)null : a[1] == "on"; break;
            default: throw new ArgumentException($"unknown switch {a[0]}");
        }
        return State("");
    }

    private static DriveState Pose(PrepareCarPhysics physics, float forward, float right, float yaw)
    {
        var frame = DriveCapture.BodyOf(physics);
        var vehicle = physics.rigidBody.transform;
        var position = frame.position + vehicle.forward * forward + vehicle.right * right;
        var rotation = Quaternion.AngleAxis(yaw, vehicle.up) * frame.rotation;
        return new DriveState
        {
            Time = Time.time, PosX = position.x, PosY = position.y, PosZ = position.z,
            RotX = rotation.x, RotY = rotation.y, RotZ = rotation.z, RotW = rotation.w, Rpm = 900f
        };
    }

    [HarnessCommand("collide-ghost")]
    private static object Ghost(string args)
    {
        var a = Args(args);
        if (a.Length < 2) throw new ArgumentException("usage: collide-ghost <forward> <right> [yaw]");
        var physics = TrackCar();
        int id = FirstTestId + testCars++;
        RemoteCars.Spawn(new CarDriveStartPacket { PlayerId = id, DriveId = id, Scene = ClientScene.LocalScene, CarToLoad = physics.CarLoader.carToLoad }, "base");
        var state = Pose(physics, F(a[0]), F(a[1]), a.Length > 2 ? F(a[2]) : 0f);
        RemoteCars.Push(id, state);
        return new { playerId = id, at = Vec(DriveInterpolator.Pos(state)) };
    }

    [HarnessCommand("collide-ghost-move")]
    private static object GhostMove(string args)
    {
        var a = Args(args);
        if (a.Length < 3) throw new ArgumentException("usage: collide-ghost-move <id> <forward> <right> [yaw]");
        var physics = TrackCar();
        var state = Pose(physics, F(a[1]), F(a[2]), a.Length > 3 ? F(a[3]) : 0f);
        RemoteCars.Push(int.Parse(a[0]), state);
        return new { playerId = int.Parse(a[0]), at = Vec(DriveInterpolator.Pos(state)) };
    }

    [HarnessCommand("collide-ghost-clear")]
    private static object GhostClear(string args)
    {
        for (int id = FirstTestId; id < FirstTestId + testCars; id++) RemoteCars.Remove(id, "harness");
        return "cleared";
    }

    [HarnessCommand("collide-ghost-checkpoint")]
    private static object GhostCheckpoint(string args)
    {
        int id = int.Parse((args ?? "").Trim());
        var race = UnityEngine.Object.FindObjectOfType<RaceTrackManager>() ?? throw new InvalidOperationException("no RaceTrackManager");
        var car = RemoteCars.Of(id) ?? throw new InvalidOperationException($"no copy {id}");
        GameObject target = null;
        var list = race.checkPointsList;
        for (int i = 0; list != null && i < list.Count; i++)
            if (list[i] != null && list[i].activeInHierarchy) { target = list[i]; break; }
        if (target == null) throw new InvalidOperationException("no active checkpoint");
        var rotation = car.Interpolator.Rotation;
        var position = target.transform.position;
        var collider = target.GetComponent<Collider>();
        if (collider != null) position = collider.bounds.center;
        RemoteCars.Push(id, new DriveState
        {
            Time = Time.time, PosX = position.x, PosY = position.y, PosZ = position.z,
            RotX = rotation.x, RotY = rotation.y, RotZ = rotation.z, RotW = rotation.w, Rpm = 900f
        });
        return new
        {
            checkpoint = target.name, layer = target.layer, tag = target.tag, trigger = collider != null && collider.isTrigger,
            at = Vec(position), race.numberOfCheckpoints, race.laps
        };
    }

    [HarnessCommand("collide-race")]
    private static object Race(string args)
    {
        var race = UnityEngine.Object.FindObjectOfType<RaceTrackManager>() ?? throw new InvalidOperationException("no RaceTrackManager");
        var list = race.checkPointsList;
        int active = -1;
        for (int i = 0; list != null && i < list.Count; i++)
            if (list[i] != null && list[i].activeInHierarchy) { active = i; break; }
        return new { race.numberOfCheckpoints, race.laps, active, count = list?.Count ?? 0 };
    }

    [HarnessCommand("collide-clock")]
    private static object Clock(string args)
    {
        var physics = PrepareCarPhysics.Get();
        bool has = physics != null && physics && physics.rigidBody != null;
        return new
        {
            time = Round(Time.time), wallMs = WallMs,
            position = has ? Vec(DriveCapture.BodyOf(physics).position) : null,
            velocity = has ? Vec(physics.rigidBody.velocity) : null,
            speed = has ? Round(physics.rigidBody.velocity.magnitude) : 0,
        };
    }

    [HarnessCommand("collide-probe")]
    private static object Probe(string args)
    {
        var physics = TrackCar();
        RemoteCollider.RefreshLocal();
        var body = physics.rigidBody;
        var own = body.GetComponentsInChildren<Collider>(true);
        var groups = own.GroupBy(c => $"{c.gameObject.layer} {LayerMask.LayerToName(c.gameObject.layer)} | {c.GetIl2CppType().Name} | trigger {c.isTrigger} | enabled {c.enabled} | rb {(c.attachedRigidbody == body ? "own" : c.attachedRigidbody == null ? "none" : c.attachedRigidbody.name)}")
            .Select(g => (object)new { key = g.Key, count = g.Count(), sample = g.First().name }).ToList();
        var all = UnityEngine.Object.FindObjectsOfType<Collider>();
        var layers = new List<object>();
        for (int layer = 0; layer < 32; layer++)
        {
            int colliders = all.Count(c => c.gameObject.layer == layer);
            int triggers = all.Count(c => c.gameObject.layer == layer && c.isTrigger);
            var ignores = new List<int>();
            for (int other = 0; other < 32; other++) if (Physics.GetIgnoreLayerCollision(layer, other)) ignores.Add(other);
            layers.Add(new { layer, name = LayerMask.LayerToName(layer), colliders, triggers, ignores });
        }
        var race = UnityEngine.Object.FindObjectOfType<RaceTrackManager>();
        var checkpoints = new List<object>();
        for (int i = 0; race != null && race.checkPointsList != null && i < race.checkPointsList.Count; i++)
        {
            var cp = race.checkPointsList[i];
            if (cp == null) continue;
            var c = cp.GetComponent<Collider>();
            checkpoints.Add(new { cp.name, cp.layer, cp.tag, active = cp.activeInHierarchy, trigger = c != null && c.isTrigger, scripts = cp.GetComponents<MonoBehaviour>().Select(m => m.GetIl2CppType().Name).ToList() });
        }
        var vehicle = physics.VehicleController;
        var wheels = vehicle == null || vehicle.wheelState == null ? null : vehicle.wheelState.Select(w => w?.wheelCol == null ? null : (object)new
        {
            w.wheelCol.name, layer = w.wheelCol.gameObject.layer, wheelColliders = w.wheelCol.GetComponentsInChildren<Collider>(true).Select(c => $"{c.GetIl2CppType().Name}@{c.gameObject.layer}").ToList()
        }).ToList();
        return new Dictionary<string, object>
        {
            ["bodyLayer"] = body.gameObject.layer,
            ["bodyTag"] = body.gameObject.tag,
            ["bodyName"] = body.name,
            ["bodyMass"] = Round(body.mass),
            ["bodyCollision"] = body.collisionDetectionMode.ToString(),
            ["localMask"] = RemoteCollider.LocalMask,
            ["ownColliders"] = groups,
            ["wheels"] = wheels,
            ["layers"] = layers,
            ["checkpoints"] = checkpoints,
            ["fixedDeltaTime"] = Round(Time.fixedDeltaTime),
            ["remoteLayer"] = RemoteCollider.Layer,
            ["physics"] = new
            {
                defaultMaxDepenetrationVelocity = Round(Physics.defaultMaxDepenetrationVelocity), bounceThreshold = Round(Physics.bounceThreshold),
                defaultContactOffset = Round(Physics.defaultContactOffset), Physics.defaultSolverIterations, Physics.defaultSolverVelocityIterations,
                bodyMaxDepenetration = Round(body.maxDepenetrationVelocity), bodySolverIterations = body.solverIterations, bodyVelocityIterations = body.solverVelocityIterations,
                bodyDrag = Round(body.drag), bodyCenterOfMass = Vec(body.centerOfMass), bodyInterpolation = body.interpolation.ToString(),
            },
            ["bodyColliders"] = own.Where(c => c.enabled && c.attachedRigidbody == body && c.GetIl2CppType().Name != "WheelCollider").Select(c => (object)new
            {
                c.name, type = c.GetIl2CppType().Name, convex = c.TryCast<MeshCollider>()?.convex, contactOffset = Round(c.contactOffset),
                material = c.sharedMaterial == null ? null : c.sharedMaterial.name, bounciness = c.sharedMaterial == null ? 0 : Round(c.sharedMaterial.bounciness),
                bounceCombine = c.sharedMaterial == null ? null : c.sharedMaterial.bounceCombine.ToString(), size = Vec(c.bounds.size),
            }).ToList(),
        };
    }

    [HarnessCommand("collide-wheels")]
    private static object Wheels(string args)
    {
        var physics = TrackCar();
        var vehicle = physics.VehicleController ?? throw new InvalidOperationException("no vehicle controller");
        var body = physics.rigidBody;
        return new
        {
            position = Vec(body.position), speed = Round(body.velocity.magnitude),
            wheels = vehicle.wheelState.Select(w => (object)new
            {
                w.grounded,
                collider = w.grounded && w.hit.collider != null ? w.hit.collider.name : null,
                layer = w.grounded && w.hit.collider != null ? w.hit.collider.gameObject.layer : -1,
                point = w.grounded ? Vec(w.hit.point) : null,
                compression = Round(w.suspensionCompression),
                wheel = w.wheelCol == null ? null : Vec(w.wheelCol.transform.position),
            }).ToList(),
        };
    }

    private static object BoxAhead(float distance, string kind)
    {
        var physics = TrackCar();
        RemoteCollider.RefreshLocal();
        var vehicle = physics.rigidBody.transform;
        var frame = DriveCapture.BodyOf(physics);
        bool kinematic = kind == "kinematic";
        testBox = new GameObject("CollideTestBox") { layer = kinematic ? RemoteCollider.Layer : 0 };
        if (kinematic)
        {
            var body = testBox.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        var box = testBox.AddComponent<BoxCollider>();
        box.size = new Vector3(2f, 1.3f, 5.2f);
        testBox.transform.SetPositionAndRotation(frame.position + vehicle.forward * distance + vehicle.up * 0.6f, vehicle.rotation);
        return new { at = Vec(testBox.transform.position), kind, layer = testBox.layer };
    }

    [HarnessCommand("collide-box")]
    private static object Box(string args)
    {
        var a = Args(args);
        if (testBox != null && testBox) UnityEngine.Object.Destroy(testBox);
        testBox = null;
        if (a.Length == 1 && a[0] == "clear") return "cleared";
        if (a.Length == 3 && a[0] == "ahead") return BoxAhead(F(a[1]), a[2]);
        if (a.Length != 2 || a[0] != "wheel") throw new ArgumentException("usage: collide-box wheel <top above ground> | ahead <distance> static|kinematic | clear");
        var physics = TrackCar();
        RemoteCollider.RefreshLocal();
        var vehicle = physics.VehicleController;
        var wheel = vehicle.wheelState[0];
        if (wheel == null || !wheel.grounded) throw new InvalidOperationException("wheel 0 is not grounded");
        float top = F(a[1]);
        var up = physics.rigidBody.transform.up;
        const float thickness = 0.1f;
        testBox = new GameObject("CollideTestBox") { layer = RemoteCollider.Layer };
        var body = testBox.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        var box = testBox.AddComponent<BoxCollider>();
        box.size = new Vector3(0.8f, thickness, 0.8f);
        testBox.transform.SetPositionAndRotation(wheel.hit.point + up * (top - thickness * 0.5f), physics.rigidBody.rotation);
        return new { at = Vec(testBox.transform.position), ground = Vec(wheel.hit.point), wheel = wheel.wheelCol.name };
    }

    [HarnessCommand("collide-hold")]
    private static object Hold(string args)
    {
        holding = (args ?? "").Trim() == "on";
        EnsureWatch();
        return new { holding };
    }

    [HarnessCommand("collide-trace")]
    private static object Trace(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on": trace.Clear(); tracing = true; EnsureWatch(); return new { tracing };
            case "off": tracing = false; return new { tracing };
            case "report": return new { tracing, frames = trace.ToList() };
            default: throw new ArgumentException("usage: collide-trace on|off|report");
        }
    }

    private static void EnsureWatch()
    {
        if (watching) return;
        watching = true;
        MelonCoroutines.Start(Watch());
    }

    private static IEnumerator Watch()
    {
        while (true)
        {
            var physics = PrepareCarPhysics.Get();
            var body = physics != null && physics ? physics.rigidBody : null;
            if (body != null && body)
            {
                if (holding && (cruise == null || !cruise.Running))
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                if (tracing && trace.Count < 3000)
                    trace.Add(new
                    {
                        t = Round(Time.time), p = Vec(body.position), v = Vec(body.velocity), w = Round(body.angularVelocity.magnitude),
                        up = Round(body.transform.up.y), dep = Round(body.maxDepenetrationVelocity),
                        cars = RemoteCars.All.Where(c => c.Collider != null).Select(c => (object)new
                        {
                            id = c.PlayerId, box = Vec(c.Collider.Body.position), on = c.Collider.Enabled, touch = c.Collider.Touching, r = c.Collider.Reason,
                        }).ToList(),
                    });
            }
            yield return null;
        }
    }

    // drive-stop leaves the brake pressed, and VPP shifts into reverse when the brake is held at a standstill.
    private static int Release(PrepareCarPhysics physics)
    {
        int inputs = 0;
        var vehicle = physics.VehicleController;
        if (vehicle == null) return 0;
        foreach (var standard in vehicle.GetComponentsInChildren<VPStandardInput>(true))
        {
            standard.externalThrottle = 0f;
            standard.externalBrake = 0f;
            standard.externalSteer = 0f;
            standard.reverse = false;
            inputs++;
        }
        return inputs;
    }

    [HarnessCommand("collide-cruise")]
    private static object CruiseStart(string args)
    {
        var a = Args(args);
        if (a.Length != 2) throw new ArgumentException("usage: collide-cruise <kmh> <seconds>");
        var physics = TrackCar();
        if (cruise != null) cruise.Running = false;
        var body = physics.rigidBody;
        cruise = new Cruise { Kmh = F(a[0]), Seconds = F(a[1]), Started = Time.time, Start = body.position, Forward = body.transform.forward };
        Release(physics);
        MelonCoroutines.Start(RunCruise(cruise));
        return new { cruise.Kmh, cruise.Seconds };
    }

    [HarnessCommand("collide-cruise-state")]
    private static object CruiseState(string args) => cruise == null ? null : new
    {
        cruise.Running, cruise.State, cruise.Kmh, maxKmh = Round(cruise.MaxSpeed * 3.6f), contactKmh = Round(cruise.ContactSpeed * 3.6f),
        contactAfter = Round(cruise.ContactAt), minKmhAfter = Round(cruise.MinSpeedAfter * 3.6f), kmhAfter300 = Round(cruise.SpeedAfter300 * 3.6f),
        endKmh = Round(cruise.EndSpeed * 3.6f), travelled = Round(cruise.Travelled), travelledAtContact = Round(cruise.TravelledAtContact),
        maxFrameMs = Round(cruise.MaxFrameMs), cruise.TouchedId,
    };

    private static IEnumerator RunCruise(Cruise run)
    {
        float lastFrame = Time.realtimeSinceStartup;
        while (run.Running && cruise == run)
        {
            float frameMs = (Time.realtimeSinceStartup - lastFrame) * 1000f;
            lastFrame = Time.realtimeSinceStartup;
            run.MaxFrameMs = Mathf.Max(run.MaxFrameMs, frameMs);
            var physics = PrepareCarPhysics.Get();
            if (physics == null || !physics || physics.rigidBody == null)
            {
                run.State = "no track car";
                break;
            }
            var body = physics.rigidBody;
            float elapsed = Time.time - run.Started;
            float speed = Vector3.Dot(body.velocity, run.Forward);
            run.MaxSpeed = Mathf.Max(run.MaxSpeed, speed);
            run.Travelled = Vector3.Dot(body.position - run.Start, run.Forward);
            var touched = RemoteCars.All.FirstOrDefault(c => c.Collider != null && c.Collider.Touching);
            bool blocked = run.MaxSpeed >= run.Kmh / 3.6f * 0.85f && speed < run.Kmh / 3.6f * 0.5f;
            if (run.ContactAt < 0f && (touched != null || blocked))
            {
                run.ContactAt = elapsed;
                run.ContactSpeed = speed;
                run.TravelledAtContact = run.Travelled;
                run.MinSpeedAfter = speed;
                run.TouchedId = touched != null ? touched.PlayerId : -2;
                run.State = "contact";
            }
            if (run.ContactAt >= 0f)
            {
                run.MinSpeedAfter = Mathf.Min(run.MinSpeedAfter, speed);
                if (run.SpeedAfter300 < 0f && elapsed - run.ContactAt >= 0.3f) run.SpeedAfter300 = speed;
                if (elapsed - run.ContactAt >= 1.5f)
                {
                    run.State = "done after contact";
                    break;
                }
            }
            else if (elapsed >= run.Seconds)
            {
                run.State = "done without contact";
                break;
            }
            else
            {
                float target = run.Kmh / 3.6f;
                var lateral = body.velocity - run.Forward * Vector3.Dot(body.velocity, run.Forward);
                body.velocity = run.Forward * target + new Vector3(0f, lateral.y, 0f);
            }
            yield return null;
        }
        var still = PrepareCarPhysics.Get();
        if (still != null && still && still.rigidBody != null) run.EndSpeed = still.rigidBody.velocity.magnitude;
        run.Running = false;
    }
}
