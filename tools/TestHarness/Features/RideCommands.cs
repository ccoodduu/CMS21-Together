using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Driving;
using UnityEngine;
using VehiclePhysics;

namespace TogetherTestHarness.Features;

// ride-along harness: "ride-probe" reads the track's camera, the own track car's seats and each observer car's seats.
public static class RideCommands
{
    private static double Round(float f) => Math.Round(f, 3);
    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };

    private static string PathOf(Transform t)
    {
        if (t == null || !t) return null;
        var parts = new List<string>();
        for (var p = t; p != null && parts.Count < 10; p = p.parent) parts.Insert(0, p.name);
        return string.Join("/", parts);
    }

    private static object Seat(GameObject handle, Transform frame)
    {
        if (handle == null || !handle) return null;
        var t = handle.transform;
        return new Dictionary<string, object>
        {
            ["path"] = PathOf(t),
            ["position"] = Vec(t.position),
            ["layer"] = handle.layer,
            ["active"] = handle.activeInHierarchy,
            ["childOfFrame"] = frame != null && frame && t.IsChildOf(frame),
            ["local"] = frame != null && frame ? Vec(frame.InverseTransformPoint(t.position)) : null,
            ["localYaw"] = frame != null && frame ? Round((Quaternion.Inverse(frame.rotation) * t.rotation).eulerAngles.y) : 0,
        };
    }

    private static object SeatInfo(CarLoader carLoader, bool passenger)
    {
        if (carLoader == null || !carLoader || !RideAlong.SeatPose(carLoader, passenger, out var seat, out var frame) || !RideAlong.CarFrame(carLoader, out _, out var center)) return null;
        return new Dictionary<string, object>
        {
            ["world"] = Vec(seat),
            ["local"] = Vec(Quaternion.Inverse(frame) * (seat - center)),
            ["front"] = Vec(frame * Vector3.forward),
            ["center"] = Vec(center),
        };
    }

    [HarnessCommand("ride-state")]
    private static object State(string args) => Dump((args ?? "").Trim());

    public static Dictionary<string, object> Dump(string driverArg = "")
    {
        int driver = int.TryParse(driverArg, out int parsed) ? parsed : RideAlong.DriverId;
        var camera = Camera.main;
        var controller = UnityEngine.Object.FindObjectOfType<CameraController>();
        bool hasHead = RideAlong.TryGetPassengerHead(out var head, out _);
        var physics = PrepareCarPhysics.Get();
        var ownLoader = physics != null && physics && physics.CarLoader != null && physics.CarLoader.IsCarLoaded() ? physics.CarLoader : null;
        var copy = RemoteCars.Of(driver);
        return new Dictionary<string, object>
        {
            ["phase"] = RideAlong.Current.ToString(),
            ["driverId"] = RideAlong.DriverId,
            ["loader"] = RideAlong.Loader,
            ["endMessage"] = RideAlong.EndMessage,
            ["headMeasured"] = RideAlong.HeadMeasured,
            ["hiddenRenderers"] = RideAlong.HiddenRenderers,
            ["rides"] = RideAlong.All.Select(r => (object)new { driverId = r.DriverId, passengerId = r.PassengerId, loader = r.Loader }).ToList(),
            ["camera"] = new Dictionary<string, object>
            {
                ["placed"] = RideAlong.CameraPlaced,
                ["frames"] = RideAlong.CameraFrames,
                ["drift"] = Round(RideAlong.CameraDrift),
                ["maxDrift"] = Round(RideAlong.MaxCameraDrift),
                ["customPos"] = controller != null && controller.customCameraPosEnabled,
                ["position"] = camera == null ? null : Vec(camera.transform.position),
                ["head"] = hasHead ? Vec(head) : null,
                ["toHead"] = camera != null && hasHead ? Round(Vector3.Distance(camera.transform.position, head)) : -1,
            },
            ["own"] = physics == null || !physics ? null : new Dictionary<string, object>
            {
                ["position"] = Vec(DriveCapture.BodyOf(physics).position),
                ["kinematic"] = physics.rigidBody != null && physics.rigidBody.isKinematic,
                ["inputsEnabled"] = physics.VehicleController == null ? 0 : physics.VehicleController.GetComponentsInChildren<VPStandardInput>(true).Count(i => i.enabled),
                ["passengerSeat"] = SeatInfo(ownLoader, true),
                ["driverSeat"] = SeatInfo(ownLoader, false),
            },
            ["capture"] = new { active = DriveCapture.Active, drives = DriveCapture.Drives, sent = DriveCapture.Sent },
            ["copy"] = copy == null ? null : new Dictionary<string, object>
            {
                ["playerId"] = copy.PlayerId,
                ["shown"] = copy.Ready && copy.Root.gameObject.activeInHierarchy,
                ["passengerSeat"] = copy.Ready ? SeatInfo(copy.Loader, true) : null,
                ["driverSeat"] = copy.Ready ? SeatInfo(copy.Loader, false) : null,
            },
            ["avatars"] = CMS21Together.Logic.Player.PresenceManager.Roster.Values.Select(p =>
            {
                bool seated = RideAlong.TryGetAvatarSeat(p.Record.PlayerId, out var seatPosition, out _);
                return (object)new
                {
                    playerId = p.Record.PlayerId,
                    active = p.HasAvatar && p.Avatar.gameObject.activeInHierarchy,
                    position = p.HasAvatar ? Vec(p.Avatar.transform.position) : null,
                    seat = seated ? Vec(seatPosition) : null,
                    toSeat = seated && p.HasAvatar ? Round(Vector3.Distance(p.Avatar.transform.position, seatPosition)) : -1,
                };
            }).ToList(),
        };
    }

    [HarnessCommand("ride-probe")]
    private static object Probe(string args)
    {
        var result = new Dictionary<string, object>();
        var camera = Camera.main;
        result["cameraMain"] = camera == null ? null : new Dictionary<string, object>
        {
            ["path"] = PathOf(camera.transform),
            ["position"] = Vec(camera.transform.position),
            ["enabled"] = camera.enabled,
            ["components"] = camera.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).ToList(),
            ["parentComponents"] = camera.transform.parent == null ? null : camera.transform.parent.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).ToList(),
        };
        result["cameras"] = UnityEngine.Object.FindObjectsOfType<Camera>().Select(c => (object)new { path = PathOf(c.transform), c.enabled, c.depth, tag = c.gameObject.tag }).ToList();
        result["listeners"] = UnityEngine.Object.FindObjectsOfType<AudioListener>().Select(l => (object)new { path = PathOf(l.transform), l.enabled }).ToList();
        var controller = UnityEngine.Object.FindObjectOfType<CameraController>();
        if (controller != null)
        {
            var vp = controller.vpCamera;
            result["cameraController"] = new Dictionary<string, object>
            {
                ["path"] = PathOf(controller.transform),
                ["enabled"] = controller.enabled,
                ["customCameraPosEnabled"] = controller.customCameraPosEnabled,
                ["allowLateUpdate"] = controller.allowLateUpdate,
                ["vpCamera"] = vp == null ? null : new Dictionary<string, object>
                {
                    ["path"] = PathOf(vp.transform),
                    ["mode"] = vp.mode.ToString(),
                    ["target"] = PathOf(vp.target),
                    ["viewTarget"] = PathOf(vp.m_viewTarget),
                    ["attachTarget"] = vp.attachTo == null ? null : PathOf(vp.attachTo.attachTarget),
                    ["lookAt"] = PathOf(vp.LookAtTarget),
                },
            };
        }

        var physics = PrepareCarPhysics.Get();
        if (physics != null && physics)
        {
            var carLoader = physics.CarLoader;
            var body = DriveCapture.BodyOf(physics);
            result["ownCar"] = new Dictionary<string, object>
            {
                ["body"] = PathOf(body),
                ["bodyPosition"] = Vec(body.position),
                ["headInside"] = PathOf(physics.headInside),
                ["headLocal"] = physics.headInside == null ? null : Vec(body.InverseTransformPoint(physics.headInside.position)),
                ["rightHandDrive"] = carLoader != null && carLoader.OtherData.RightHandDrive,
                ["seatScale"] = carLoader == null ? 0 : Round(carLoader.InteriorParams.SeatScale),
                ["seatLeftMod"] = carLoader == null ? 0 : Round(carLoader.InteriorParams.SeatLeftHeightMod),
                ["seatRightMod"] = carLoader == null ? 0 : Round(carLoader.InteriorParams.SeatRightHeightMod),
                ["left"] = carLoader == null ? null : Seat(carLoader.GetLeftSeatHandle(), body),
                ["right"] = carLoader == null ? null : Seat(carLoader.GetRightSeatHandle(), body),
                ["inputs"] = physics.VehicleController == null ? null : physics.VehicleController.GetComponentsInChildren<VPStandardInput>(true).Select(i => (object)new { path = PathOf(i.transform), i.enabled }).ToList(),
                ["kinematic"] = physics.rigidBody != null && physics.rigidBody.isKinematic,
                ["bodyForward"] = Vec(body.forward),
                ["velocity"] = physics.rigidBody == null ? null : Vec(physics.rigidBody.velocity),
                ["wheelHandles"] = carLoader == null ? null : new[] { carLoader.GetWheelFLHandle(), carLoader.GetWheelFRHandle(), carLoader.GetWheelRLHandle(), carLoader.GetWheelRRHandle() }
                    .Select(w => w == null ? null : (object)new { path = PathOf(w.transform), local = Vec(body.InverseTransformPoint(w.transform.position)) }).ToList(),
                ["vppWheels"] = physics.VehicleController == null || physics.VehicleController.wheelState == null ? null
                    : physics.VehicleController.wheelState.Select(w => w?.wheelCol == null ? null : (object)new { w.steerable, path = PathOf(w.wheelCol.wheelTransform), local = w.wheelCol.wheelTransform == null ? null : Vec(body.InverseTransformPoint(w.wheelCol.wheelTransform.position)) }).ToList(),
                ["firstPart"] = carLoader != null && carLoader.carParts != null && carLoader.carParts.Count > 0 && carLoader.carParts[0].handle != null
                    ? new { path = PathOf(carLoader.carParts[0].handle.transform), local = Vec(body.InverseTransformPoint(carLoader.carParts[0].handle.transform.position)) } : null,
            };
        }

        result["copies"] = RemoteCars.All.Select(car => (object)new Dictionary<string, object>
        {
            ["playerId"] = car.PlayerId,
            ["ready"] = car.Ready,
            ["root"] = car.Ready ? PathOf(car.Root) : null,
            ["left"] = car.Loader == null || !car.Loader ? null : Seat(car.Loader.GetLeftSeatHandle(), car.Root),
            ["right"] = car.Loader == null || !car.Loader ? null : Seat(car.Loader.GetRightSeatHandle(), car.Root),
            ["rootForward"] = car.Ready ? Vec(car.Root.forward) : null,
            ["velocity"] = Vec(car.Interpolator.Velocity),
            ["wheels"] = car.Ready ? car.Wheels.Select(w => (object)Vec(car.Root.InverseTransformPoint(w.position))).ToList() : null,
            ["firstPart"] = car.Ready && car.Loader.carParts != null && car.Loader.carParts.Count > 0 && car.Loader.carParts[0].handle != null
                ? new { path = PathOf(car.Loader.carParts[0].handle.transform), local = Vec(car.Root.InverseTransformPoint(car.Loader.carParts[0].handle.transform.position)) } : null,
        }).ToList();
        result["characterMotors"] = UnityEngine.Object.FindObjectsOfType<CharacterMotor>().Select(m => (object)new { path = PathOf(m.transform), m.enabled, active = m.gameObject.activeInHierarchy }).ToList();
        return result;
    }
}
