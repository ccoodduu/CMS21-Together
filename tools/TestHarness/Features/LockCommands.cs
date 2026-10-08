using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Locks;

namespace TogetherTestHarness.Features;

// part-locks D13 harness verbs and the dump section "locks". lock-take is a bare request without an action (it
// replaces part-claim as a reservation); lock-try drives a gated entry point (see LockTryCommands).
public static class LockCommands
{
    private static readonly Dictionary<int, LockAnswer> answers = new Dictionary<int, LockAnswer>();
    private static readonly List<int> order = new List<int>();

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    internal static void Reset(List<string> changed)
    {
        if (!CarLockMirror.RenewEnabled) changed.Add("lock-renew off");
        CarLockMirror.RenewEnabled = true;
        if (LockLifecycle.BoltIdleSeconds != 300f || LockLifecycle.ChooserIdleSeconds != 60f) changed.Add("lock-idle");
        LockLifecycle.BoltIdleSeconds = 300f;
        LockLifecycle.ChooserIdleSeconds = 60f;
        answers.Clear();
        order.Clear();
        watchLoader = -1;
    }

    public static Dictionary<string, object> Dump() => new Dictionary<string, object>
    {
        ["mirror"] = CarLockMirror.All.OrderBy(r => r.Loader).ThenBy(r => r.LockId).Select(r => (object)new Dictionary<string, object>
        {
            ["lockId"] = r.LockId, ["linked"] = r.LinkedLockId, ["loader"] = r.Loader, ["owner"] = r.Owner, ["kind"] = r.Kind.ToString(),
            ["phase"] = r.Phase, ["x"] = r.X, ["s"] = r.S, ["items"] = r.Items, ["ending"] = r.Ending,
        }).ToList(),
        ["pending"] = CarLockMirror.PendingRequests.Select(p => (object)new { requestId = p.RequestId, set = p.Set.ToString(), ageMs = Math.Round(p.AgeMs) }).ToList(),
        ["counters"] = new Dictionary<string, int>(CarLockMirror.Counters.ToDictionary(c => c.Key, c => c.Value)),
        ["answers"] = order.Select(id => (object)Answer(id)).ToList(),
        ["lastMessage"] = LockMessages.Last,
    };

    private static Dictionary<string, object> Answer(int requestId)
    {
        if (!answers.TryGetValue(requestId, out var answer)) return new Dictionary<string, object> { ["requestId"] = requestId, ["result"] = "pending" };
        return new Dictionary<string, object>
        {
            ["requestId"] = requestId,
            ["result"] = answer.Outcome == LockOutcome.Granted ? "granted" : answer.Outcome == LockOutcome.Denied ? "denied" : answer.Outcome.ToString().ToLowerInvariant(),
            ["lockId"] = answer.LockId,
            ["refusal"] = answer.Refusal.ToString(),
            ["holder"] = answer.Holder,
            ["conflictKey"] = answer.ConflictKey,
            ["waitedMs"] = Math.Round(answer.WaitedMs),
        };
    }

    public static CarLockKind ParseKind(string text)
    {
        switch (text.ToLowerInvariant())
        {
            case "unmount": return CarLockKind.PartUnmount;
            case "mount": return CarLockKind.PartMount;
            case "body": return CarLockKind.BodyPart;
            case "fluid": case "fill": case "drain": return CarLockKind.Fluid;
            case "oil": return CarLockKind.OilDrain;
            case "crane": return CarLockKind.Crane;
            case "lift": return CarLockKind.Lift;
            case "move": return CarLockKind.Move;
            default: return (CarLockKind)Enum.Parse(typeof(CarLockKind), text, true);
        }
    }

    public static LockSet SetFor(int loader, CarLockKind kind, IList<string> keys, bool bare)
    {
        if (!bare && keys.Count == 1)
        {
            string key = keys[0];
            if (LockKeys.IsSub(key) && (kind == CarLockKind.PartUnmount || kind == CarLockKind.PartMount)) return LockSets.ForPart(loader, key, kind) ?? throw new ArgumentException($"no part {key} on loader {loader}");
            if (LockKeys.IsBody(key)) return LockSets.ForBody(loader, int.Parse(key.Substring(2)));
            if (LockKeys.IsFluid(key)) return LockSets.ForFluid(loader, kind, key);
            if (key == LockKeys.Engine) return LockSets.ForCrane(loader) ?? throw new ArgumentException($"loader {loader} has no engine");
            if (key == LockKeys.Car) return LockSets.ForCar(loader, kind);
        }
        var set = new LockSet { Loader = loader, Kind = kind };
        set.X.AddRange(keys);
        if (!set.X.Contains(LockKeys.Car)) set.S.Add(LockKeys.Car);
        return set;
    }

    [HarnessCommand("lock-take")]
    private static object LockTake(string args)
    {
        var parts = Args(args);
        if (parts.Length == 2 && parts[0] == "result") return Answer(int.Parse(parts[1]));
        if (parts.Length < 3) throw new ArgumentException("usage: lock-take <loader> <kind> <key...> [bare] [items <uid...>] [release] | result <requestId>");
        int loader = int.Parse(parts[0]);
        var kind = ParseKind(parts[1]);
        var rest = parts.Skip(2).ToList();
        if (rest.Contains("release"))
        {
            var keys = rest.Where(k => k != "release").ToList();
            var own = CarLockMirror.Own(loader).Where(r => keys.Any(k => r.X.Contains(k))).ToList();
            foreach (var record in own) CarLockMirror.Release(record.LockId);
            return new { released = own.Select(r => r.LockId).ToList() };
        }
        bool bare = rest.Remove("bare");
        var uids = new List<long>();
        int itemsAt = rest.IndexOf("items");
        if (itemsAt >= 0)
        {
            uids = rest.Skip(itemsAt + 1).Select(long.Parse).ToList();
            rest = rest.Take(itemsAt).ToList();
        }
        var set = SetFor(loader, kind, rest, bare);
        set.Items.AddRange(uids);
        int requestId = 0;
        requestId = CarLockMirror.Request(set, answer => answers[requestId] = answer);
        order.Add(requestId);
        return new Dictionary<string, object> { ["requestId"] = requestId, ["set"] = set.ToString() };
    }

    [HarnessCommand("lock-release")]
    private static object LockRelease(string args)
    {
        var parts = Args(args);
        if (parts.Length == 0) throw new ArgumentException("usage: lock-release <loader>|all|<loader> lock <id>");
        if (parts.Length == 3 && parts[1] == "lock")
        {
            CarLockMirror.Release(int.Parse(parts[2]));
            return new { released = new[] { int.Parse(parts[2]) } };
        }
        int loader = parts[0] == "all" ? -1 : int.Parse(parts[0]);
        var own = CarLockMirror.All.Where(r => r.Owner == CMS21Together.Network.Client.Instance.ID && (loader < 0 || r.Loader == loader)).Select(r => r.LockId).ToList();
        CarLockMirror.ReleaseOwn(loader, "harness");
        return new { released = own };
    }

    [HarnessCommand("lock-renew")]
    private static object LockRenew(string args)
    {
        CarLockMirror.RenewEnabled = (args ?? "").Trim() != "off";
        return new { renew = CarLockMirror.RenewEnabled };
    }

    [HarnessCommand("lock-idle")]
    private static object LockIdle(string args)
    {
        var parts = Args(args);
        if (parts.Length == 1 && parts[0] == "default")
        {
            LockLifecycle.BoltIdleSeconds = 300f;
            LockLifecycle.ChooserIdleSeconds = 60f;
        }
        else if (parts.Length == 2)
        {
            LockLifecycle.BoltIdleSeconds = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            LockLifecycle.ChooserIdleSeconds = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        else throw new ArgumentException("usage: lock-idle <bolt seconds> <chooser seconds> | default");
        return new { bolt = LockLifecycle.BoltIdleSeconds, chooser = LockLifecycle.ChooserIdleSeconds };
    }

    [HarnessCommand("lock-chooser")]
    private static object LockChooser(string args)
    {
        var parts = Args(args);
        if (parts.Length != 3 || (parts[2] != "open" && parts[2] != "close")) throw new ArgumentException("usage: lock-chooser <loader> <key> open|close");
        if (parts[2] == "open") return Commands.Execute("lock-try", $"{parts[0]} mount {parts[1]}");
        var window = CMS.UI.WindowManager.Instance?.GetWindowByID<CMS.UI.Windows.ChoosePartUpWindow>(CMS.UI.WindowID.ChoosePartUp);
        bool open = window != null && CMS.UI.WindowManager.Instance.IsWindowActive(CMS.UI.WindowID.ChoosePartUp);
        if (open) window.Hide(false);
        return new { closed = open };
    }

    [HarnessCommand("lock-hover")]
    private static object LockHover(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: lock-hover <loader> <key>");
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse(parts[0])) ?? throw new ArgumentException($"no car loader {parts[0]}");
        var script = CMS21Together.Logic.Car.Parts.PartRegistry.Build(carLoader).Sub(parts[1]) ?? throw new ArgumentException($"no part {parts[1]}");
        var game = GameScript.Get();
        game.IOMouseOverCarLoader = carLoader;
        script.MouseOver = false;
        game.SetPartMouseOver(null);
        script.SetMouseOver();
        bool highlighted = script.MouseOver;
        game.SetPartMouseOver(script);
        return new Dictionary<string, object>
        {
            ["highlighted"] = highlighted,
            ["label"] = UIManager.Get()?.TextDescription?.text,
            ["partMouseOver"] = game.partMouseOver != null && game.partMouseOver.Pointer == script.Pointer,
            ["message"] = LockSelection.BlockedMessage(script),
        };
    }

    private static int watchLoader = -1;
    private static string watchFluid;
    private static readonly List<object> watched = new List<object>();
    private static bool watchSubscribed;

    // locks-fluid's order check: the fluid level this client already has when another player's lock release arrives.
    [HarnessCommand("lock-watch")]
    private static object LockWatch(string args)
    {
        var parts = Args(args);
        if (parts.Length == 1 && parts[0] == "report") return new { loader = watchLoader, fluid = watchFluid, releases = watched.ToList() };
        if (parts.Length == 1 && parts[0] == "off")
        {
            watchLoader = -1;
            return new { watching = false };
        }
        if (parts.Length != 2) throw new ArgumentException("usage: lock-watch <loader> <fluidKey> | off | report");
        watchLoader = int.Parse(parts[0]);
        watchFluid = parts[1];
        watched.Clear();
        if (!watchSubscribed)
        {
            watchSubscribed = true;
            CarLockMirror.Changed += (record, released, snapshot) =>
            {
                if (!released || record.Loader != watchLoader) return;
                watched.Add(new { lockId = record.LockId, owner = record.Owner, x = record.X, level = FluidLevel(watchLoader, watchFluid) });
            };
        }
        return new { watching = true, level = FluidLevel(watchLoader, watchFluid) };
    }

    internal static float FluidLevel(int loader, string fluidKey)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader);
        var details = CMS21Together.Logic.Car.Details.CarDetailsIO.Read(carLoader, CMS21_Together_Core.Data.GameType.CarDetailSection.Fluids);
        var fluid = details.Fluids?.FirstOrDefault(f => LockSets.FluidKey(f.Type, f.Id) == fluidKey);
        return fluid == null ? -1f : (float)Math.Round(fluid.Level, 3);
    }

    [HarnessCommand("lock-fluid")]
    private static object LockFluid(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: lock-fluid <loader> <fluidKey>");
        return new { level = FluidLevel(int.Parse(parts[0]), parts[1]) };
    }

    [HarnessCommand("lock-tool-end")]
    private static object LockToolEnd(string args)
    {
        var ended = new List<string>();
        foreach (var found in UnityEngine.Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<FluidRefill>()))
        {
            var tool = found.TryCast<FluidRefill>();
            if (tool == null || !tool.IsActive) continue;
            tool.Hide();
            ended.Add($"refill {tool.carFluidType}");
        }
        foreach (var found in UnityEngine.Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<FluidExtractor>()))
        {
            var tool = found.TryCast<FluidExtractor>();
            if (tool == null || !tool.IsActive) continue;
            tool.Hide();
            ended.Add("extractor");
        }
        return new { ended };
    }

    private static readonly List<object> gateReports = new List<object>();
    private static bool gateSubscribed;

    [HarnessCommand("lock-reports")]
    private static object LockReports(string args)
    {
        if (!gateSubscribed)
        {
            gateSubscribed = true;
            LockGate.Reported += (action, report) => gateReports.Add(new
            {
                kind = action.Set?.Kind.ToString(), key = action.TargetKey, result = report.Result, waitedMs = Math.Round(report.WaitedMs),
                prefetched = action.PrefetchedLockId != 0, started = report.Started,
            });
        }
        if ((args ?? "").Trim() == "clear") gateReports.Clear();
        return gateReports.ToList();
    }

    [HarnessCommand("lock-tracked")]
    private static object LockTracked(string args) =>
        LockLifecycle.All.Select(t => (object)new { lockId = t.LockId, kind = t.Kind.ToString(), key = t.MainKey, phase = t.Phase.ToString(), ending = t.EndingSince >= 0f, itemPicked = t.ItemPicked }).ToList();

    [HarnessCommand("lock-counters")]
    private static object LockCounters(string args)
    {
        if ((args ?? "").Trim() == "reset") CarLockMirror.ResetCounters();
        return CarLockMirror.Counters.ToDictionary(c => c.Key, c => c.Value);
    }
}
