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
// on commit really happens.
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
        public GateReport Report;
        public string State = "waiting";
        public bool Finished;
        public float StartedAt;
        public PartScript Part;
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
    };

    [HarnessCommand("lock-try")]
    private static object LockTry(string args)
    {
        Subscribe();
        var parts = Args(args);
        if (parts.Length == 2 && parts[0] == "result") return tries.TryGetValue(int.Parse(parts[1]), out var found) ? Result(found) : throw new ArgumentException($"no lock-try {parts[1]}");
        if (parts.Length < 2) throw new ArgumentException("usage: lock-try <loader> unmount <key>|mount <key> [uid]|body <index>|crane-out [finish|hold|release] | result <id>");
        int loader = int.Parse(parts[0]);
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader) ?? throw new ArgumentException($"no car loader {loader}");
        var t = new Try { Id = nextId++, Loader = loader, What = string.Join(" ", parts.Skip(1)), Finish = parts.Contains("finish"), Release = parts.Contains("release") };
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
                    if (parts.Length > 3 && long.TryParse(parts[3], out long uid)) t.ItemUid = uid;
                    SetMode(gameMode.PartSelectMount);
                    t.Part.ActionMount(true);
                    break;
                }
                case "body":
                {
                    var part = carLoader.carParts[int.Parse(parts[2])];
                    t.Key = PartKeys.Body(int.Parse(parts[2]));
                    carLoader.TakeOffCarPart(part.name);
                    break;
                }
                case "crane-out":
                {
                    t.Key = LockKeys.Engine;
                    NotificationCenter.Get().ActionUnMountGroup(carLoader.e_engine_h.GetComponent<InteractiveObject>());
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
        float deadline = Time.realtimeSinceStartup + CarLockMirror.TimeoutSeconds + 1f;
        while (inner.Report == null && Time.realtimeSinceStartup < deadline) yield return null;
        t.State = $"item {inner.Report?.Result ?? "no answer"}";
        if (inner.Report == null || inner.Report.Result != "granted" || !inner.Report.Started) yield break;
        var window = WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
        if (window != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp)) window.Hide(false);
        if (t.Finish) yield return FinishBolts(t, mount: true);
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
