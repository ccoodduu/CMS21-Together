using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;
using CMS21Together.Logic.Visuals;
using CMS21Together.Managers;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// remote-visual-feedback harness: the dump section "visuals" and the vfx-* verbs. vfx-unscrew and vfx-tool drive the
// game's real paths on the actor (a player's click and hold); everything else only reads or toggles visuals.
public static class VisualCommands
{
    private const int TraceLimit = 2000;
    private const float UnscrewTimeoutSeconds = 90f;
    private const float FinishTimeoutSeconds = 5f;

    private static readonly List<string> trace = new List<string>();
    private static bool tracing;
    private static bool subscribed;
    private static Unscrew unscrew;

    private class Unscrew
    {
        public int Loader;
        public string Key;
        public PartScript Script;
        public bool Mount;
        public float PauseAt = 2f;
        public bool Running;
        public string State = "starting";
        public int Frames;
        public float Started;
    }

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    public static Dictionary<string, object> Dump()
    {
        return new Dictionary<string, object>
        {
            ["enabled"] = VisualScope.Enabled,
            ["hold"] = VisualScope.Hold,
            ["ghostsActive"] = VisualScope.Effects.Where(e => !e.Ended && e.Kind != VisualKind.Bolts).Select(e => (object)new
            {
                kind = e.Kind.ToString(),
                loader = e.Loader,
                key = e.Key,
                playerId = e.PlayerId,
                phase = e.Phase,
                fade = e.Fade,
                outward = e is MoveEffect move ? Outward(e.Loader, move.Origin, move.Offset) : null,
            }).ToList(),
            ["ghostsStarted"] = Counts(VisualScope.Started),
            ["ghostsFinished"] = Counts(VisualScope.Finished),
            ["ghostsSkipped"] = Counts(VisualScope.Skipped),
            ["boltsActive"] = BoltReplay.Active.Select(e => (object)new
            {
                loader = e.Loader,
                key = e.Key,
                owner = e.PlayerId,
                done = e.BoltsDone,
                total = e.BoltCount,
                shown = Round(e.Shown),
                mounting = e.Mounting,
                phase = e.Phase,
            }).ToList(),
            ["renderersHidden"] = VisualScope.RenderersHidden,
            ["leaks"] = VisualScope.Leaks,
            ["activitySent"] = ActivityCapture.Sent,
            ["activityDropped"] = ActivityCapture.Dropped,
            ["activityReceived"] = RemoteActivity.Received,
            ["localActivity"] = Activity(ActivityCapture.Current),
            ["players"] = PresenceManager.Roster.ToDictionary(p => p.Key.ToString(), p => (object)Player(p.Value)),
            ["unscrew"] = unscrew == null ? null : new { loader = unscrew.Loader, key = unscrew.Key, state = unscrew.State, mount = unscrew.Mount, frames = unscrew.Frames },
        };
    }

    [HarnessCommand("vfx-enable")]
    private static object Enable(string args)
    {
        PlayerSettings.RemoteVisuals = OnOff(args, "vfx-enable on|off");
        if (!PlayerSettings.RemoteVisuals) VisualScope.CancelAll("visuals turned off");
        return new { enabled = VisualScope.Enabled };
    }

    [HarnessCommand("vfx-hold")]
    private static object HoldCommand(string args)
    {
        VisualScope.Hold = OnOff(args, "vfx-hold on|off");
        return new { hold = VisualScope.Hold };
    }

    [HarnessCommand("vfx-trace")]
    private static object TraceCommand(string args)
    {
        string mode = (args ?? "").Trim();
        switch (mode)
        {
            case "on":
                Subscribe();
                tracing = true;
                ActivityCapture.Tracing = true;
                trace.Clear();
                MelonCoroutines.Start(SampleActor());
                return new { trace = true };
            case "off":
                tracing = false;
                ActivityCapture.Tracing = false;
                return new { trace = false };
            case "report":
                return Report();
            default:
                throw new ArgumentException("usage: vfx-trace on|off|report");
        }
    }

    [HarnessCommand("vfx-unscrew")]
    private static object UnscrewCommand(string args)
    {
        var parts = Args(args);
        if (parts.Length < 2) throw new ArgumentException("usage: vfx-unscrew <loader> <key> [mount] [pause <fraction>] | <loader> <key> resume|undo|status");
        int loader = int.Parse(parts[0]);
        string key = parts[1];
        string verb = parts.Length > 2 ? parts[2] : "";

        if (verb == "resume" || verb == "undo" || verb == "status")
        {
            if (unscrew == null || unscrew.Loader != loader || unscrew.Key != key) throw new InvalidOperationException($"no vfx-unscrew on {loader} {key}");
            if (verb == "status") return Status(unscrew);
            if (verb == "resume")
            {
                unscrew.PauseAt = 2f;
                if (!unscrew.Running) MelonCoroutines.Start(Drive(unscrew));
                return Status(unscrew);
            }
            var undone = unscrew;
            unscrew = null;
            if (undone.Mount) undone.Script.UndoMounting();
            else undone.Script.UndoUnMounting();
            undone.State = "undone";
            Note($"undo {loader} {key}");
            return Status(undone);
        }

        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader) ?? throw new ArgumentException($"no car loader {loader}");
        var script = PartRegistry.Build(carLoader).Sub(key) ?? throw new ArgumentException($"no part {key}");
        bool mount = parts.Contains("mount");
        int pauseIndex = Array.IndexOf(parts, "pause");
        float pauseAt = pauseIndex >= 0 && pauseIndex + 1 < parts.Length ? float.Parse(parts[pauseIndex + 1], CultureInfo.InvariantCulture) : 2f;
        if (mount != script.IsUnmounted) throw new InvalidOperationException(mount ? $"{key} is already mounted" : $"{key} is already unmounted");

        bool blocked = PartClaims.HeldByOther(loader, new[] { key }, out int owner);
        // A disabled PartScript (seen on headless test games) never refreshes canBeUnmount in its Update (spike 1.3).
        bool forced = !mount && !script.enabled && !script.canBeUnmount && !script.IsBlocked();
        if (forced) script.canBeUnmount = true;
        GameScript.Get().IOMouseOverCarLoader = carLoader;
        if (mount) script.ActionMount(false);
        else script.ActionUnMount();
        if (blocked) return new Dictionary<string, object> { ["blocked"] = true, ["owner"] = owner };

        unscrew = new Unscrew { Loader = loader, Key = key, Script = script, Mount = mount, PauseAt = pauseAt, Started = Time.time };
        Note($"unscrew {loader} {key} mount={mount} forcedCanBeUnmount={forced} partCanUpdate={script.canUpdate} bolts={script.MountObjects?.Length ?? 0} mode={GameMode.Get()?.currentMode}");
        MelonCoroutines.Start(Drive(unscrew));
        return Status(unscrew);
    }

    [HarnessCommand("vfx-tool")]
    private static object Tool(string args)
    {
        var parts = Args(args);
        if (parts.Length < 1) throw new ArgumentException("usage: vfx-tool <ToolType|none> [loader]");
        var tools = ToolsManager.Get() ?? throw new InvalidOperationException("no ToolsManager in this scene");
        if (parts[0] == "none")
        {
            tools.HideTool();
            Note("tool none");
            return new { active = tools.ToolIsActive };
        }
        if (!Enum.TryParse(parts[0], true, out ToolType type)) throw new ArgumentException($"unknown tool '{parts[0]}'");
        if (parts.Length > 1)
        {
            var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[1])) ?? throw new ArgumentException($"no car loader {parts[1]}");
            GameScript.Get().IOMouseOverCarLoader = carLoader;
        }
        tools.Use(type);
        Note($"tool {type} active={tools.ToolIsActive}");
        return new { tool = type.ToString(), active = tools.ToolIsActive };
    }

    [HarnessCommand("vfx-parts")]
    private static object Parts(string args)
    {
        var parts = Args(args);
        if (parts.Length < 1) throw new ArgumentException("usage: vfx-parts <loader> [all]");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        int minBolts = parts.Length > 1 && parts[1] == "all" ? 0 : 2;
        var registry = PartRegistry.Build(carLoader);
        return registry.SubKeys.Select(k => (Key: k, Part: registry.Sub(k)))
            .Where(p => !p.Part.IsUnmounted && !p.Part.IsBlocked() && p.Part.GetUnmountWith().Count == 0 && (p.Part.MountObjects?.Length ?? 0) >= minBolts)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (object)new { key = p.Key, id = p.Part.id, bolts = p.Part.MountObjects?.Length ?? 0 })
            .ToList();
    }

    [HarnessCommand("vfx-probe")]
    private static object Probe(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: vfx-probe <loader> <key>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        var script = PartRegistry.Build(carLoader).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        var mode = GameMode.Get();
        return new Dictionary<string, object>
        {
            ["id"] = script.id,
            ["unmounted"] = script.IsUnmounted,
            ["canBeUnmount"] = script.canBeUnmount,
            ["partCanUpdate"] = script.canUpdate,
            ["partEnabled"] = script.enabled,
            ["blockedNo"] = script.blockedNo,
            ["skipPartsAwake"] = GameSettings.SkipPartsAwake,
            ["active"] = script.gameObject.activeInHierarchy,
            ["offset"] = Offset(script.transform.position - carLoader.transform.position),
            ["mode"] = mode == null ? null : mode.currentMode.ToString(),
            ["mountUnMountMode"] = mode != null && mode.mountUnMountMode,
            ["selected"] = PartGhosts.PartRenderers(script).Count,
            ["renderers"] = script.GetComponentsInChildren<Renderer>(true).ToArray().Select(r => (object)new
            {
                name = r.name,
                type = r.GetIl2CppType().Name,
                enabled = r.enabled,
                active = r.gameObject.activeInHierarchy,
                forceOff = r.forceRenderingOff,
                layer = r.gameObject.layer,
                filter = r.GetComponent<MeshFilter>() != null,
                path = PathTo(script.transform, r.transform),
            }).ToList(),
            ["bolts"] = (script.MountObjects ?? new UnhollowerBaseLib.Il2CppReferenceArray<MountObject>(0)).ToArray().Where(m => m != null).Select(m => (object)new
            {
                name = m.name,
                state = Round(m.GetMountState()),
                unmounted = m.IsUnmounted(),
                canBeUnmount = m.GetCanBeUnmount(),
                canAction = m.GetCanAction(),
                canUpdate = m.canUpdate,
                reverse = m.reverseMode,
                renderers = m.renderers?.Length ?? -1,
            }).ToList(),
        };
    }

    private static float[] Offset(Vector3 v) => new[] { Round(v.x), Round(v.y), Round(v.z) };

    private static string PathTo(Transform root, Transform t)
    {
        var names = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) names.Add(c.name);
        names.Reverse();
        return string.Join("/", names);
    }

    [HarnessCommand("vfx-switch")]
    private static object Switch(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: vfx-switch <loader> <body part name>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        var part = carLoader.GetCarPart(parts[1]) ?? throw new ArgumentException($"no body part {parts[1]}");
        bool before = part.Switched;
        carLoader.SwitchCarPart(parts[1]);
        return new { part = parts[1], before, inProgress = part.InProgress };
    }

    [HarnessCommand("vfx-stand")]
    private static object Stand(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: vfx-stand <loader> <metres beside the car>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        if (!PresenceManager.HasLocalMotor) throw new InvalidOperationException("no local player in this scene");
        float metres = float.Parse(parts[1], CultureInfo.InvariantCulture);
        var motor = PresenceManager.LocalMotor;
        var root = VisualScope.CarRoot(carLoader);
        var target = root.position + root.right * (1f + metres);
        target.y = motor.transform.position.y;
        var controller = motor.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        motor.transform.position = target;
        motor.transform.rotation = Quaternion.LookRotation(-root.right, Vector3.up);
        if (controller != null) controller.enabled = wasEnabled;
        Movement.ForceSend();
        return new { x = Round(target.x), y = Round(target.y), z = Round(target.z) };
    }

    // vfx-shot <file> <loader> <car|part:<key>|body:<name>|player:<name>> <right> <up> <forward> [aim=<metres up>]
    // [fov=<deg>] [size=<w>x<h>]: renders a camera of our own (RaceGridCommands.StartShot, works with the window
    // minimized) placed at the target plus the offset in the loader's axes, looking at the target. For a part the right
    // and forward offsets are mirrored to the part's side of the car, so positive ones look from outside the car. Poll
    // grid-shot-state for the file.
    [HarnessCommand("vfx-shot")]
    private static object Shot(string args)
    {
        args = args ?? "";
        int cut = args.IndexOf(".png", StringComparison.OrdinalIgnoreCase);
        if (cut < 0) throw new ArgumentException("vfx-shot needs a .png file path first");
        var parts = new[] { args.Substring(0, cut + 4) }.Concat(Args(args.Substring(cut + 4))).ToArray();
        if (parts.Length < 6) throw new ArgumentException("usage: vfx-shot <file> <loader> <car|part:<key>|player:<name>> <right> <up> <forward> [aim=<m>] [fov=<deg>] [size=<w>x<h>]");
        float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[1])) ?? throw new ArgumentException($"no car loader {parts[1]}");
        var axes = VisualScope.CarRoot(carLoader);
        Vector3 target;
        bool outside = false;
        if (parts[2] == "car") target = axes.position;
        else if (parts[2].StartsWith("part:"))
        {
            var script = PartRegistry.Build(carLoader).Sub(parts[2].Substring(5)) ?? throw new ArgumentException($"no part {parts[2]}");
            target = script.transform.position;
            outside = true;
        }
        else if (parts[2].StartsWith("body:"))
        {
            var body = carLoader.GetCarPart(parts[2].Substring(5)) ?? throw new ArgumentException($"no body part {parts[2]}");
            target = body.handle.transform.position;
            outside = true;
        }
        else if (parts[2].StartsWith("player:"))
        {
            string name = parts[2].Substring(7);
            var player = PresenceManager.Roster.Values.FirstOrDefault(p => p.Record.Username == name);
            if (player == null || !player.HasAvatar) throw new ArgumentException($"no visible player '{name}'");
            target = player.Avatar.transform.position;
        }
        else throw new ArgumentException($"unknown target {parts[2]}");

        float aim = 0f, fov = 60f;
        int w = 1280, h = 720;
        foreach (var option in parts.Skip(6))
        {
            if (option.StartsWith("aim=")) aim = F(option.Substring(4));
            else if (option.StartsWith("fov=")) fov = F(option.Substring(4));
            else if (option.StartsWith("size="))
            {
                var size = option.Substring(5).Split('x');
                w = int.Parse(size[0]);
                h = int.Parse(size[1]);
            }
            else throw new ArgumentException($"unknown option {option}");
        }
        var fromCenter = target - axes.position;
        float right = F(parts[3]) * (outside && Vector3.Dot(fromCenter, axes.right) < 0f ? -1f : 1f);
        float forward = F(parts[5]) * (outside && Vector3.Dot(fromCenter, axes.forward) < 0f ? -1f : 1f);
        var position = target + axes.right * right + Vector3.up * F(parts[4]) + axes.forward * forward;
        var lookAt = target + Vector3.up * aim;
        var rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
        RaceGridCommands.StartShot(parts[0], w, h, position, rotation, fov, -1f, 10);
        return new { file = parts[0], target = Offset(target), position = Offset(position), loader = Offset(carLoader.transform.position), root = Offset(axes.position) };
    }

    // vfx-car <loader>: where the CarLoader object and the car's root are (they differ unless the car is on the
    // place the loader object stands on).
    [HarnessCommand("vfx-car")]
    private static object Car(string args)
    {
        var parts = Args(args);
        if (parts.Length != 1) throw new ArgumentException("usage: vfx-car <loader>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        var root = VisualScope.CarRoot(carLoader);
        return new { loader = Offset(carLoader.transform.position), root = Offset(root.position), apart = Round(Vector3.Distance(carLoader.transform.position, root.position)) };
    }

    // Angle between a flat direction and the flat direction from the car's root to a point: 0 = straight away from the
    // car, 180 = into it.
    private static float? Outward(int loader, Vector3 from, Vector3 direction)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
        if (carLoader == null) return null;
        var out1 = from - VisualScope.CarRoot(carLoader).position;
        out1.y = 0f;
        direction.y = 0f;
        if (out1.sqrMagnitude < 0.0001f || direction.sqrMagnitude < 0.0001f) return null;
        return Round(Vector3.Angle(out1, direction));
    }

    internal static void Reset(List<string> changed)
    {
        if (!PlayerSettings.RemoteVisuals)
        {
            PlayerSettings.RemoteVisuals = true;
            changed.Add("vfx-enable on");
        }
        if (VisualScope.Hold)
        {
            VisualScope.Hold = false;
            changed.Add("vfx-hold off");
        }
        if (tracing)
        {
            tracing = false;
            ActivityCapture.Tracing = false;
            changed.Add("vfx-trace off");
        }
        if (unscrew != null)
        {
            changed.Add($"vfx-unscrew {unscrew.Loader} {unscrew.Key} stopped");
            unscrew = null;
        }
    }

    private static IEnumerator Drive(Unscrew job)
    {
        job.Running = true;
        try
        {
            // ActionUnMount waits for the part lock (part-locks): the bolts only move once the re-invoked call has run.
            float lockDeadline = Time.time + CMS21Together.Logic.Car.Locks.CarLockMirror.TimeoutSeconds + 1f;
            while (unscrew == job && CMS21Together.Logic.Car.Locks.LockGate.HasPending && Time.time < lockDeadline) yield return null;
            while (unscrew == job)
            {
                float progress = ActivityCapture.PartProgress(job.Script, job.Mount);
                if (progress >= job.PauseAt)
                {
                    job.State = "paused";
                    Note($"paused {job.Key} at {progress:F2}");
                    yield break;
                }
                var bolt = job.Script.MountObjects?.FirstOrDefault(m => m != null && (job.Mount ? m.GetMountState() < 1f : !m.IsUnmounted()));
                if (bolt == null) break;
                if (Time.time - job.Started > UnscrewTimeoutSeconds)
                {
                    job.State = "timeout";
                    Note($"timeout {job.Key} at {progress:F2}");
                    yield break;
                }
                job.State = "running";
                bolt.SetCanAction(true);
                bolt.Action();
                job.Frames++;
                yield return null;
            }
            if (unscrew != job) yield break;

            job.State = "bolts done";
            Note($"bolts done {job.Key} partEnabled={job.Script.enabled}");
            // PartScript.Update commits the part once its bolts are done, but it does not run while the script is disabled.
            if (!job.Script.enabled) job.Script.StartCoroutine(job.Mount ? job.Script.ShowMounted() : job.Script.Hide());
            float deadline = Time.time + FinishTimeoutSeconds;
            while (unscrew == job && job.Script.IsUnmounted != !job.Mount && Time.time < deadline) yield return null;
            job.State = job.Script.IsUnmounted == !job.Mount ? "finished" : "part not committed";
            Note($"{job.State} {job.Key} mode={GameMode.Get()?.currentMode}");
        }
        finally
        {
            job.Running = false;
        }
    }

    private static IEnumerator SampleActor()
    {
        string lastBolts = null, lastMode = null, lastTool = null;
        while (tracing)
        {
            var current = ActivityCapture.Current;
            string mode = GameMode.Get()?.currentMode.ToString();
            if (mode != lastMode) Note($"mode {lastMode = mode}");
            var tools = ToolsManager.Get();
            string tool = tools == null ? "none" : $"active={tools.ToolIsActive} type={tools.currentUsedTool} object={ObjectName(tools.CurrentUsedTool)}";
            if (tool != lastTool) Note($"tools {lastTool = tool}");
            if (unscrew != null || (current.CarLoaderID >= 0 && current.PartKey != null))
            {
                int loader = unscrew?.Loader ?? current.CarLoaderID;
                string key = unscrew?.Key ?? current.PartKey;
                var script = VisualScope.RegistryOf(loader)?.Sub(key);
                if (script?.MountObjects != null)
                {
                    string bolts = string.Join(",", script.MountObjects.Where(m => m != null).Select(m => m.GetMountState().ToString("F2", CultureInfo.InvariantCulture)));
                    if (bolts != lastBolts) Note($"bolts {key} [{lastBolts = bolts}]");
                }
            }
            yield return null;
        }
    }

    private static void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        PartClaims.ClaimChanged += (loader, keys, owner, fromSnapshot) => Note($"claim loader {loader} [{string.Join(",", keys)}] owner {owner}{(fromSnapshot ? " snapshot" : "")}");
        PartChanges.RemoteChangeApplying += (loader, body, sub) => Note($"change loader {loader} body [{string.Join(",", body.Select(b => $"{b.Key}{(b.Unmounted ? "-" : "+")}"))}] sub [{string.Join(",", sub.Select(s => $"{s.Key}{(s.Unmounted ? "-" : "+")}"))}]");
        PartChanges.RemoteChangeApplied += (loader, body, sub) => Note($"applied loader {loader}");
    }

    private static object Report()
    {
        var lines = trace.Concat(ActivityCapture.Trace.Select(l => $"activity {l}")).ToList();
        var tools = ToolsManager.Get();
        return new Dictionary<string, object>
        {
            ["lines"] = lines,
            ["bones"] = Bones(),
            ["wrenchMeshes"] = Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<Mesh>())
                .Select(m => m.name).Where(n => n != null && (n.IndexOf("wrench", StringComparison.OrdinalIgnoreCase) >= 0
                                                             || n.IndexOf("ratchet", StringComparison.OrdinalIgnoreCase) >= 0
                                                             || n.IndexOf("spanner", StringComparison.OrdinalIgnoreCase) >= 0))
                .Distinct().OrderBy(n => n).ToList(),
            ["tools"] = tools == null ? null : new
            {
                active = tools.ToolIsActive,
                type = tools.currentUsedTool.ToString(),
                currentObject = ObjectName(tools.CurrentUsedTool),
                currentLayer = tools.CurrentUsedTool == null ? -1 : tools.CurrentUsedTool.layer,
            },
        };
    }

    private static List<string> Bones()
    {
        var names = new List<string>();
        var prefab = ModGameManager.PlayerPrefab;
        if (prefab == null) return names;
        void Walk(Transform t)
        {
            names.Add(t.name);
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
        }
        Walk(prefab.transform);
        return names;
    }

    private static object Player(RemotePlayer player)
    {
        var work = player.HasAvatar ? player.Avatar.Work : null;
        return new
        {
            activity = Activity(player.Record?.Activity),
            pose = work?.Pose ?? WorkPose.Idle,
            facing = Round(work?.Facing ?? 0f),
            facingCar = FacingCar(player),
            propActive = work?.Props.Active ?? false,
            propTool = work?.Props.ToolName,
            arms = work?.HasArms ?? false,
        };
    }

    // The avatar's turn away from its activity's car root (0 = facing the car), independent of WorkPose's own target.
    private static float? FacingCar(RemotePlayer player)
    {
        var activity = player.Record?.Activity;
        if (!player.HasAvatar || activity == null || activity.IsIdle || activity.CarLoaderID < 0) return null;
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(activity.CarLoaderID);
        if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return null;
        var avatar = player.Avatar.transform;
        var to = VisualScope.CarRoot(carLoader).position - avatar.position;
        var forward = avatar.forward;
        to.y = 0f;
        forward.y = 0f;
        if (to.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f) return null;
        return Round(Vector3.Angle(forward, to));
    }

    private static object Activity(PlayerActivityState state)
    {
        if (state == null || state.IsIdle) return new { kind = ActivityKind.None.ToString() };
        return new
        {
            kind = state.Kind.ToString(),
            loader = state.CarLoaderID,
            key = state.PartKey,
            tool = state.ToolType == PlayerActivityState.NoTool ? null : ((ToolType)state.ToolType).ToString(),
            modTool = state.ModTool == PlayerActivityState.NoTool ? null : ((CMS21_Together_Core.Data.GameType.ModToolId)state.ModTool).ToString(),
            progress = Round(state.ProgressFraction),
        };
    }

    private static object Status(Unscrew job) => new Dictionary<string, object>
    {
        ["blocked"] = false,
        ["key"] = job.Key,
        ["state"] = job.State,
        ["bolts"] = job.Script.MountObjects?.Length ?? 0,
        ["progress"] = Round(ActivityCapture.PartProgress(job.Script, job.Mount)),
    };

    private static Dictionary<string, int> Counts(IReadOnlyDictionary<string, int> counts) => counts.ToDictionary(c => c.Key, c => c.Value);

    private static bool OnOff(string args, string usage)
    {
        string value = (args ?? "").Trim();
        if (value == "on") return true;
        if (value == "off") return false;
        throw new ArgumentException($"usage: {usage}");
    }

    private static string ObjectName(GameObject go) => go == null ? null : go.name;

    private static void Note(string text)
    {
        if (!tracing) return;
        trace.Add($"{Time.frameCount} {Time.realtimeSinceStartup:F3} {text}");
        CMS21_Together_Core.Logging.Log.Info($"[VisualTrace] {text}");
        if (trace.Count > TraceLimit) trace.RemoveAt(0);
    }

    private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;
}
