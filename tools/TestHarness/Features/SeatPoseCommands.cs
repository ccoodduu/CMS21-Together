using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Driving;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using UnityEngine;

namespace TogetherTestHarness.Features;

// seated-avatars 3.1: the offset of each seated garage avatar from its seat pose, sampled every frame in the harness's
// OnLateUpdate (after the mod placed the avatar and after the lift moved the car), kept in a ring buffer of the last
// RingFrames frames. The handle's travel is split by where in the frame it moved (spike 1.1: before or after
// OnLateUpdate).
public static class SeatPoseCommands
{
    private const int RingFrames = 120;
    private const float SeatedAvatarDrop = 0.45f;

    private class Samples
    {
        public readonly float[] Ring = new float[RingFrames];
        public int Count;
        public int Next;
        public float Last = -1f;
        public float Max = -1f;
        public Vector3? HandleAtUpdate;
        public Vector3? HandleAtLate;
        public float MovedBeforeLate;
        public float MovedAfterLate;
    }

    private static readonly Dictionary<int, Samples> samples = new Dictionary<int, Samples>();

    public static void Update()
    {
        foreach (var player in PresenceManager.Roster.Values)
        {
            var handle = Handle(player.Record);
            if (handle == null) continue;
            var s = Of(player.Record.PlayerId);
            var position = handle.position;
            if (s.HandleAtLate.HasValue) s.MovedAfterLate += Vector3.Distance(s.HandleAtLate.Value, position);
            s.HandleAtUpdate = position;
        }
    }

    public static void LateUpdate()
    {
        foreach (var player in PresenceManager.Roster.Values)
        {
            var record = player.Record;
            var s = Of(record.PlayerId);
            var handle = Handle(record);
            if (handle != null)
            {
                if (s.HandleAtUpdate.HasValue) s.MovedBeforeLate += Vector3.Distance(s.HandleAtUpdate.Value, handle.position);
                s.HandleAtLate = handle.position;
            }
            else s.HandleAtLate = null;

            if (!Seated(player, out var pose))
            {
                s.Last = -1f;
                continue;
            }
            s.Last = Vector3.Distance(player.Avatar.transform.position, pose);
            if (s.Last > s.Max) s.Max = s.Last;
            s.Ring[s.Next] = s.Last;
            s.Next = (s.Next + 1) % RingFrames;
            if (s.Count < RingFrames) s.Count++;
        }
    }

    private static bool Seated(RemotePlayer player, out Vector3 pose)
    {
        pose = Vector3.zero;
        if (!player.HasAvatar || !player.Avatar.gameObject.activeSelf) return false;
        var handle = Handle(player.Record);
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(player.Record.SeatCarLoaderId);
        if (handle == null || !CarPartsSync.IsReady(player.Record.SeatCarLoaderId) || !RideAlong.CarFrame(carLoader, out var frame, out _)) return false;
        pose = handle.position - frame * Vector3.up * SeatedAvatarDrop;
        return true;
    }

    private static bool InGarageSeat(PlayerPresenceRecord record) =>
        record.Scene == GameScene.Garage && record.SeatCarLoaderId != PlayerPresenceRecord.NoCar && ClientScene.LocalScene == GameScene.Garage;

    private static Transform Handle(PlayerPresenceRecord record)
    {
        if (!InGarageSeat(record)) return null;
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(record.SeatCarLoaderId);
        if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return null;
        var handle = record.SeatLeft ? carLoader.GetLeftSeatHandle() : carLoader.GetRightSeatHandle();
        return handle == null || !handle ? null : handle.transform;
    }

    private static Samples Of(int playerId)
    {
        if (!samples.TryGetValue(playerId, out var s)) samples[playerId] = s = new Samples();
        return s;
    }

    internal static object Describe(RemotePlayer player)
    {
        samples.TryGetValue(player.Record.PlayerId, out var s);
        bool seated = Seated(player, out _);
        return new
        {
            seated,
            side = InGarageSeat(player.Record) ? (player.Record.SeatLeft ? "left" : "right") : null,
            toSeat = seated && s != null ? Round(s.Last) : -1f,
            maxToSeat = s == null || s.Count == 0 ? -1f : Round(s.Ring.Take(s.Count).Max()),
            maxSinceReset = s == null ? -1f : Round(s.Max),
            frames = s?.Count ?? 0,
            handleMovedBeforeLate = s == null ? 0f : Round(s.MovedBeforeLate),
            handleMovedAfterLate = s == null ? 0f : Round(s.MovedAfterLate),
        };
    }

    [HarnessCommand("seat-pose")]
    private static object Poses(string args)
    {
        object local = null;
        if (PresenceManager.HasLocalMotor && Client.Instance != null && Client.Instance.IsConnected)
        {
            var movement = Movement.CaptureLocal();
            local = new
            {
                position = new { x = Round(movement.Position.X), y = Round(movement.Position.Y), z = Round(movement.Position.Z) },
                seat = SeatEngine.SeatCarLoaderId,
                seatLeft = SeatEngine.SeatLeft,
                seatedMode = SeatEngine.InSeatedMode,
            };
        }
        return new
        {
            local,
            players = PresenceManager.Roster.ToDictionary(p => p.Key.ToString(), p => (object)new
            {
                active = p.Value.HasAvatar && p.Value.Avatar.gameObject.activeSelf,
                position = p.Value.HasAvatar ? Vec(p.Value.Avatar.transform.position) : null,
                seat = p.Value.Record.SeatCarLoaderId,
                seatPose = Describe(p.Value),
            }),
        };
    }

    [HarnessCommand("seat-handles")]
    private static object Handles(string args)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse((args ?? "").Trim()));
        if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) throw new System.ArgumentException($"no car on loader {args}");
        var left = carLoader.GetLeftSeatHandle();
        var right = carLoader.GetRightSeatHandle();
        var camera = Camera.main;
        return new
        {
            car = carLoader.carToLoad,
            rightHandDrive = carLoader.OtherData.RightHandDrive,
            left = left == null ? null : Vec(left.transform.position),
            right = right == null ? null : Vec(right.transform.position),
            center = left == null || right == null ? null : Vec((left.transform.position + right.transform.position) * 0.5f),
            camera = camera == null ? null : Vec(camera.transform.position),
            cameraToLeft = camera == null || left == null ? -1f : Round(Vector3.Distance(camera.transform.position, left.transform.position)),
            cameraToRight = camera == null || right == null ? -1f : Round(Vector3.Distance(camera.transform.position, right.transform.position)),
        };
    }

    [HarnessCommand("seat-pose-reset")]
    private static object ResetCommand(string args)
    {
        samples.Clear();
        return "reset";
    }

    internal static void Reset(List<string> changed) => samples.Clear();

    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };

    private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;
}
