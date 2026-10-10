using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Car.Parts;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// part-locks D13: lock-try drives a gated entry point the way a player's click does and reports what the gate did;
// "finish" completes the started work under the held lock (bolts out or in, then the part commits), so the release
// on commit really happens. "repoint <key>" moves the game's mouse-over to another part while the item pick waits for
// its lock answer, as a player's mouse does during the round trip.
public static class LockTryCommands
{
    private const float FinishTimeoutSeconds = 60f;

    private sealed class Try
    {
        public int Id;
        public string What;
        public int Loader;
        public string Key;
        public bool Finish;
        public bool Release;
        public long ItemUid;
        public List<long> GroupUids;
        public string Repoint;
        public bool Repointed;
        public GateReport Report;
        public string State = "waiting";
        public bool Finished;
        public float StartedAt;
        public PartScript Part;
        public CarPart Body;
        public bool BodyUnmountedTarget = true;
        public Func<bool> Done;
        public Action Finisher;
    }

    private static readonly Dictionary<int, Try> tries = new Dictionary<int, Try>();
    private static Try current;
    private static int nextId = 1;
    private static bool subscribed;

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    internal static void Reset(List<string> changed)
    {
        if (tries.Values.Any(t => !t.Finished && t.State != "done")) changed.Add("lock-try (unfinished)");
        tries.Clear();
        current = null;
    }

    private static void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        LockGate.Reported += (action, report) =>
        {
            var t = current;
            if (t == null || t.Report != null) return;
            t.Report = report;
            current = null;
            OnReport(t);
        };
    }

    private static Dictionary<string, object> Result(Try t) => new Dictionary<string, object>
    {
        ["tryId"] = t.Id,
        ["what"] = t.What,
        ["result"] = t.Report?.Result ?? (t.State == "not gated" ? "not gated" : "pending"),
        ["holder"] = t.Report?.Holder ?? -1,
        ["conflictKey"] = t.Report?.ConflictKey,
        ["waitedMs"] = Math.Round(t.Report?.WaitedMs ?? 0f),
        ["ran"] = t.Report?.Ran ?? false,
        ["started"] = t.Report?.Started ?? false,
        ["lockId"] = t.Report?.LockId ?? 0,
        ["state"] = t.State,
        ["finished"] = t.Finished,
        ["repointed"] = t.Repointed,
    };

    [HarnessCommand("lock-try")]
    private static object LockTry(string args)
    {
        Subscribe();
        var parts = Args(args);
        if (parts.Length == 2 && parts[0] == "result") return tries.TryGetValue(int.Parse(parts[1]), out var found) ? Result(found) : throw new ArgumentException($"no lock-try {parts[1]}");
        if (parts.Length < 2) throw new ArgumentException("usage: lock-try <loader> unmount <key>|mount <key> [uid|group <uid...>] [repoint <key>]|body <index>|body-mount <index> <uid> [repoint <index>]|crane-out|fill <type> <id> [level <x>]|drain <type> <id>|oil|lift <lifter> up|down [nogate]|move <place> [nogate] [finish|hold|release] | result <id>");
        int loader = int.Parse(parts[0]);
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader) ?? throw new ArgumentException($"no car loader {loader}");
        var t = new Try { Id = nextId++, Loader = loader, What = string.Join(" ", parts.Skip(1)), Finish = parts.Contains("finish"), Release = parts.Contains("release") };
        int repointAt = Array.IndexOf(parts, "repoint");
        if (repointAt > 0) t.Repoint = parts[repointAt + 1];
        tries[t.Id] = t;
        current = t;
        var game = GameScript.Get();
        game.IOMouseOverCarLoader = carLoader;
        game.UnmountGroup = 10;
        try
        {
            switch (parts[1])
            {
                case "unmount":
                {
                    t.Key = parts[2];
                    t.Part = PartRegistry.Build(carLoader).Sub(t.Key) ?? throw new ArgumentException($"no part {t.Key}");
                    SetMode(gameMode.PartSelect);
                    if (!t.Part.enabled && !t.Part.canBeUnmount && !t.Part.IsBlocked()) t.Part.canBeUnmount = true;
                    t.Part.ActionUnMount();
                    break;
                }
                case "mount":
                {
                    t.Key = parts[2];
                    t.Part = PartRegistry.Build(carLoader).Sub(t.Key) ?? throw new ArgumentException($"no part {t.Key}");
                    int groupAt = Array.IndexOf(parts, "group");
                    if (groupAt > 0) t.GroupUids = parts.Skip(groupAt + 1).TakeWhile(p => long.TryParse(p, out _)).Select(long.Parse).ToList();
                    else if (parts.Length > 3 && long.TryParse(parts[3], out long uid)) t.ItemUid = uid;
                    SetMode(gameMode.PartSelectMount);
                    NotificationCenter.Get().SetMountGroup(MountGroupOf(t.Part));
                    t.Part.ActionMount(true);
                    break;
                }
                case "body":
                {
                    var part = carLoader.carParts[int.Parse(parts[2])];
                    t.Key = PartKeys.Body(int.Parse(parts[2]));
                    t.Body = part;
                    SetMode(gameMode.GarageDisassemble);
                    carLoader.TakeOffCarPart(part.name);
                    break;
                }
                case "body-mount":
                {
                    int index = int.Parse(parts[2]);
                    t.Key = PartKeys.Body(index);
                    t.Body = carLoader.carParts[index];
                    t.BodyUnmountedTarget = false;
                    var item = Singleton<GameManager>.Instance.Inventory.GetItem(long.Parse(parts[3])) ?? throw new ArgumentException($"no item {parts[3]}");
                    SetMode(gameMode.GarageAssemble);
                    PointAtBody(game, carLoader, t.Body);
                    game.SelectPartToMount(item);
                    if (t.Repoint != null && current == t && LockGate.HasPending)
                    {
                        PointAtBody(game, carLoader, carLoader.carParts[int.Parse(t.Repoint)]);
                        t.Repointed = true;
                    }
                    break;
                }
                case "crane-out":
                {
                    t.Key = LockKeys.Engine;
                    NotificationCenter.Get().ActionUnMountGroup(carLoader.e_engine_h.GetComponent<InteractiveObject>());
                    break;
                }
                case "fill":
                case "drain":
                {
                    var type = (CarFluidType)Enum.Parse(typeof(CarFluidType), parts[2], true);
                    int fluidId = int.Parse(parts[3]);
                    t.Key = LockSets.FluidKey(type, fluidId);
                    carLoader.CurrentUsedFluid = type;
                    carLoader.CurrentUsedFluidId = fluidId;
                    var tools = ToolsManager.Get();
                    if (tools.ItemWorkOn == null) tools.ItemWorkOn = FluidPart(loader, carLoader, t.Key);
                    int levelAt = Array.IndexOf(parts, "level");
                    float? level = levelAt > 0 ? float.Parse(parts[levelAt + 1], System.Globalization.CultureInfo.InvariantCulture) : (float?)null;
                    if (parts[1] == "fill")
                    {
                        var tool = FindRefill(type);
                        t.Done = () => !tool.IsActive;
                        t.Finisher = () =>
                        {
                            if (level.HasValue) SetFluidLevel(carLoader, type, fluidId, level.Value);
                            tool.Hide();
                        };
                        SetMode(gameMode.Garage);
                        if (parts.Contains("nocar"))
                        {
                            game.IOMouseOverCarLoader = null;
                            try { tool.Use(); }
                            catch (Exception e) { t.State = $"vanilla threw: {e.Message}"; }
                        }
                        else tool.Use();
                    }
                    else
                    {
                        var tool = FindAll<FluidExtractor>().FirstOrDefault() ?? throw new InvalidOperationException("no FluidExtractor in the scene");
                        t.Done = () => CarLockMirror.Get(t.Report?.LockId ?? 0) == null;
                        SetMode(gameMode.Garage);
                        tool.Use();
                    }
                    break;
                }
                case "oil":
                {
                    t.Key = LockSets.OilKey;
                    t.Done = () => !CMS21Together.Logic.Tools.CarTools.OilBinHooks.IsDraining(carLoader);
                    carLoader.UseOilbin();
                    break;
                }
                case "lift":
                {
                    int index = int.Parse(parts[2]);
                    var lifter = GarageLoader.Get().carLifter[index];
                    t.Key = LockKeys.Car;
                    t.Done = () => !lifter.isMoving;
                    int state = (int)lifter.GetState();
                    if (parts.Contains("nogate"))
                    {
                        CMS21Together.Network.Client.Instance.Send(new LifterActionRequestPacket { LifterIndex = index, FromState = state, ToState = state + (parts[3] == "up" ? 1 : -1) });
                        current = null;
                        t.State = "sent";
                        break;
                    }
                    lifter.Action(parts[3] == "up" ? 0 : 1);
                    break;
                }
                case "move":
                {
                    var place = (CarPlace)Enum.Parse(typeof(CarPlace), parts[2], true);
                    t.Key = LockKeys.Car;
                    t.Done = () => !CMS21Together.Logic.Car.Placement.CarPlacementSync.IsMovingLocally(loader);
                    if (parts.Contains("nogate"))
                    {
                        CMS21Together.Network.Client.Instance.Send(new CarPlaceChangeRequestPacket { CarLoaderID = loader, FromPlace = carLoader.GetPlaceNo(), ToPlace = (int)place });
                        current = null;
                        t.State = "sent";
                        break;
                    }
                    var mode = GameMode.Get();
                    mode.previousMode = mode.currentMode;
                    var center = NotificationCenter.Get();
                    center.StartCoroutine(center.ChangeCarPos(carLoader, place, false));
                    break;
                }
                default:
                    throw new ArgumentException($"unknown lock-try kind '{parts[1]}'");
            }
        }
        finally
        {
            if (current == t && t.Report == null && !LockGate.HasPending)
            {
                current = null;
                t.State = "not gated";
            }
        }
        return Result(t);
    }

    private static void SetMode(gameMode mode)
    {
        var gameMode = GameMode.Get();
        if (gameMode != null && gameMode.currentMode != mode) gameMode.SetCurrentMode(mode);
    }

    private static void OnReport(Try t)
    {
        t.State = t.Report.Result;
        if (t.Report.Result != "granted" || !t.Report.Started) return;
        t.StartedAt = Time.realtimeSinceStartup;
        if (t.What.StartsWith("mount") && t.GroupUids != null)
        {
            MelonCoroutines.Start(PickGroup(t));
            return;
        }
        if (t.What.StartsWith("mount") && t.ItemUid != 0)
        {
            MelonCoroutines.Start(PickItem(t));
            return;
        }
        if (t.Release)
        {
            CarLockMirror.Release(t.Report.LockId);
            t.State = "released";
            return;
        }
        if (t.Finish && t.Part != null) MelonCoroutines.Start(FinishBolts(t, mount: false));
        if (t.Finish && t.Body != null) MelonCoroutines.Start(FinishBody(t));
        if (t.Finish && t.Done != null) MelonCoroutines.Start(FinishWhen(t));
    }

    private static IEnumerator PickItem(Try t)
    {
        yield return null;
        var inventory = Singleton<GameManager>.Instance.Inventory;
        BaseItem item = inventory.GetItem(t.ItemUid);
        if (item == null) item = inventory.GetGroup(t.ItemUid);
        if (item == null)
        {
            t.State = $"no item {t.ItemUid}";
            yield break;
        }
        var inner = new Try { Id = nextId++, Loader = t.Loader, Key = t.Key, What = $"item {t.ItemUid}" };
        tries[inner.Id] = inner;
        current = inner;
        GameScript.Get().SelectPartToMount(item);
        if (t.Repoint != null && inner.Report == null && LockGate.HasPending)
        {
            var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(t.Loader);
            GameScript.Get().SetPartMouseOver(PartRegistry.Build(carLoader).Sub(t.Repoint) ?? throw new ArgumentException($"no part {t.Repoint}"));
            t.Repointed = true;
        }
        float deadline = Time.realtimeSinceStartup + CarLockMirror.TimeoutSeconds + 1f;
        while (inner.Report == null && Time.realtimeSinceStartup < deadline) yield return null;
        t.State = $"item {inner.Report?.Result ?? "no answer"}";
        if (inner.Report == null || inner.Report.Result != "granted" || !inner.Report.Started) yield break;
        var window = WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
        if (window != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp)) window.Hide(false);
        if (t.Finish) yield return FinishBolts(t, mount: true);
    }

    // Raycast.PartSelectMount hands NotificationCenter the part's interactive object (its parent's, else its
    // grandparent's) before ActionMount; MountGroup reads it when the chooser submits a group.
    private static InteractiveObject MountGroupOf(PartScript part)
    {
        var parent = part.transform.parent;
        var io = parent == null ? null : parent.GetComponent<InteractiveObject>();
        if (io == null && parent != null && parent.parent != null) io = parent.parent.GetComponent<InteractiveObject>();
        return io;
    }

    private static void PointAtBody(GameScript game, CarLoader carLoader, CarPart part)
    {
        var io = part.handle == null ? null : part.handle.GetComponentInChildren<InteractiveObject>(true);
        if (io == null || string.IsNullOrEmpty(io.type)) throw new ArgumentException($"body part {part.name} has no interactive object");
        game.SetPartMouseOver(null);
        game.IOMouseOverCarLoader = carLoader;
        game.IOMouseOverType = io.type;
        game.IOMouseOverIO = io;
    }

    private static IEnumerator PickGroup(Try t)
    {
        yield return null;
        var inventory = Singleton<GameManager>.Instance.Inventory;
        var window = WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
        foreach (long uid in t.GroupUids)
        {
            var item = inventory.GetItem(uid);
            if (item == null || window == null)
            {
                t.State = $"no item {uid}";
                CloseChooser();
                yield break;
            }
            var inner = new Try { Id = nextId++, Loader = t.Loader, Key = t.Key, What = $"pick {uid}" };
            tries[inner.Id] = inner;
            current = inner;
            window.SelectItemInCreateGroup(item);
            float deadline = Time.realtimeSinceStartup + CarLockMirror.TimeoutSeconds + 1f;
            while (inner.Report == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (current == inner) current = null;
            if (inner.Report == null || inner.Report.Result != "granted" || !inner.Report.Started)
            {
                t.State = $"item {inner.Report?.Result ?? "no answer"}";
                t.Report.Holder = inner.Report?.Holder ?? -1;
                t.Report.ConflictKey = inner.Report?.ConflictKey;
                if (t.Finish) CloseChooser();
                yield break;
            }
        }
        window.SubmitGroupItem();
        yield return null;
        t.State = "item granted";
        if (t.Finish) yield return FinishBolts(t, mount: true);
    }

    private static IEnumerator FinishBody(Try t)
    {
        var part = t.Body;
        bool target = t.BodyUnmountedTarget;
        float deadline = Time.realtimeSinceStartup + FinishTimeoutSeconds;
        while ((part.TakeOnOffInProgress || part.Unmounted != target) && Time.realtimeSinceStartup < deadline) yield return null;
        t.Finished = part.Unmounted == target && !part.TakeOnOffInProgress;
        t.State = t.Finished ? "finished" : "not committed";
    }

    private static IEnumerator FinishWhen(Try t)
    {
        yield return new WaitForSeconds(0.3f);
        try { t.Finisher?.Invoke(); }
        catch (Exception e) { t.State = $"finisher failed: {e.Message}"; yield break; }
        float deadline = Time.realtimeSinceStartup + FinishTimeoutSeconds;
        while (!SafeDone(t) && Time.realtimeSinceStartup < deadline) yield return null;
        t.Finished = SafeDone(t);
        t.State = t.Finished ? "finished" : "not finished";
    }

    private static bool SafeDone(Try t)
    {
        try { return t.Done(); }
        catch (Exception) { return true; }
    }

    private static IEnumerable<T> FindAll<T>() where T : Component
    {
        foreach (var found in Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<T>()))
        {
            var component = found.TryCast<T>();
            if (component != null && component.gameObject.scene.IsValid()) yield return component;
        }
    }

    private static FluidRefill FindRefill(CarFluidType type)
    {
        var all = FindAll<FluidRefill>().ToList();
        var tool = all.FirstOrDefault(r => r.carFluidType == type) ?? all.FirstOrDefault() ?? throw new InvalidOperationException("no FluidRefill in the scene");
        tool.carFluidType = type;
        return tool;
    }

    private static GameObject FluidPart(int loader, CarLoader carLoader, string fluidKey)
    {
        var relations = LockSets.Relations(loader);
        string key = relations?.Fluids.FirstOrDefault(f => f.Value.Contains(fluidKey)).Key;
        var part = key != null ? relations.Registry.Sub(key) : null;
        return part != null ? part.gameObject : carLoader.gameObject;
    }

    private static void SetFluidLevel(CarLoader carLoader, CarFluidType type, int id, float level)
    {
        var details = CMS21Together.Logic.Car.Details.CarDetailsIO.Read(carLoader, CMS21_Together_Core.Data.GameType.CarDetailSection.Fluids);
        foreach (var fluid in details.Fluids)
            if ((int)fluid.Type == (int)type && fluid.Id == id) fluid.Level = level;
        CMS21Together.Logic.Car.Details.CarDetailsIO.Apply(carLoader, details);
    }

    private static void CloseChooser()
    {
        var window = WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
        if (window != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp)) window.Hide(false);
    }

    private static IEnumerator FinishBolts(Try t, bool mount)
    {
        var part = t.Part;
        float deadline = Time.realtimeSinceStartup + FinishTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            var bolt = part.MountObjects?.FirstOrDefault(m => m != null && (mount ? m.GetMountState() < 1f : !m.IsUnmounted()));
            if (bolt == null) break;
            bolt.SetCanAction(true);
            bolt.Action();
            yield return null;
        }
        if (!part.enabled && part.IsUnmounted != !mount) part.StartCoroutine(mount ? part.ShowMounted() : part.Hide());
        while (part.IsUnmounted != !mount && Time.realtimeSinceStartup < deadline) yield return null;
        t.Finished = part.IsUnmounted == !mount;
        t.State = t.Finished ? "finished" : "not committed";
        var mode = GameMode.Get();
        if (mode != null && (mode.currentMode == gameMode.PartUnMount || mode.currentMode == gameMode.PartMount))
            mode.SetCurrentMode(mount ? gameMode.PartSelectMount : gameMode.PartSelect);
    }
}
