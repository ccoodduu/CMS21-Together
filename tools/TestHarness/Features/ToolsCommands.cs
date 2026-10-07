using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Tools;
using CMS21Together.Logic.Tools.CarTools;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

// sync-workshop-machines harness verbs. Puts and takes call the inventory and the machine in the order the UI does
// (docs/spikes/workshop-machines.md): inventory first on a put, the machine's own take coroutine or clear on a take.
[HarmonyPatch]
public static class ToolsCommands
{
    private const string WheelRim = "rim_3";
    private const string WheelTire = "tire_standard";
    private static readonly string[] Shock = { "amortyzatorPrzod_1", "sprezynnaPrzod_1", "czapkaAmorPrzod_1" };

    private static readonly HashSet<PacketTypes> ToolPackets = new HashSet<PacketTypes>
    {
        PacketTypes.ToolSlotUpdate, PacketTypes.ToolSlotRejected, PacketTypes.ToolSlotProperty, PacketTypes.ToolPartChange,
        PacketTypes.ToolPosition, PacketTypes.ToolClaimUpdate, PacketTypes.ToolPartChangeResult,
    };

    private static readonly HashSet<PacketTypes> InventoryPackets = new HashSet<PacketTypes>
    {
        PacketTypes.InventoryItemAction, PacketTypes.InventoryGroupItemAction,
    };

    private static bool holding;
    private static bool holdingInventory;
    private static bool replaying;
    private static readonly List<(PacketTypes Id, object Data, long Sender)> held = new List<(PacketTypes, object, long)>();

    private static string[] Args(string args) => (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static ModToolId ToolArg(string value) =>
        Enum.TryParse(value, true, out ModToolId tool) ? tool : throw new ArgumentException($"unknown tool '{value}'");

    private static ToolMachine Present(ModToolId tool)
    {
        var machine = ToolSync.Machine(tool);
        if (machine == null || !machine.Present) throw new ArgumentException($"{tool} is not in this scene");
        return machine;
    }

    private static Inventory Inv => Singleton<GameManager>.Instance.Inventory;

    [HarnessCommand("tool-list")]
    private static object List(string args) => ToolSync.Machines.Select(m => new
    {
        tool = m.Tool.ToString(),
        present = m.Present,
        mirrorUid = ToolSync.Mirror(m.Tool).Uid,
        localUid = m.Present ? m.ReadLocal().Uid : 0,
        claimedBy = ToolSync.ClaimOwner(m.Tool),
    }).ToList();

    [HarnessCommand("tool-trace")]
    private static object TraceCommand(string args)
    {
        ToolSync.Trace = (args ?? "").Trim() == "on";
        return new { trace = ToolSync.Trace };
    }

    [HarmonyPatch(typeof(global::Inventory), nameof(global::Inventory.Add), typeof(Il2CppSystem.Collections.Generic.List<BaseItem>))]
    [HarmonyPrefix]
    private static void TraceAddList(Il2CppSystem.Collections.Generic.List<BaseItem> newItems) =>
        ToolSync.TraceEvent($"Inv.Add(List) {newItems?.Count ?? 0} items");

    [HarnessCommand("give-item")]
    private static object GiveItem(string args)
    {
        var parts = Args(args);
        if (parts.Length < 1) throw new ArgumentException("usage: give-item <id> [condition]");
        var item = new Item(parts[0]) { Condition = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1f };
        Inv.Add(item);
        return new { item.ID, item.UID };
    }

    [HarnessCommand("give-group")]
    private static object GiveGroup(string args)
    {
        var parts = Args(args);
        if (parts.Length < 1) throw new ArgumentException("usage: give-group <wheel|shock|groupId itemId,itemId...>");
        var group = BuildGroup(parts[0], parts.Length > 1 ? parts[1] : null);
        Inv.AddGroup(group);
        return new { group.ID, group.UID, items = group.ItemList.Count };
    }

    private static GroupItem BuildGroup(string kind, string itemIds)
    {
        string[] ids = kind switch
        {
            "wheel" => new[] { WheelRim, WheelTire },
            "shock" => Shock,
            _ => (itemIds ?? kind).Split(','),
        };
        var group = new GroupItem(ids[0]) { ItemList = new Il2CppSystem.Collections.Generic.List<Item>(), IsNormalGroup = true };
        foreach (string id in ids)
        {
            var item = new Item(id) { Condition = 1f };
            if (kind == "wheel")
            {
                var wheel = item.WheelData;
                wheel.Size = 15;
                wheel.Width = 195;
                wheel.Profile = 65;
                wheel.ET = 35;
                item.WheelData = wheel;
            }
            group.ItemList.Add(item);
        }
        return group;
    }

    private static System.Collections.IEnumerator Drive(Il2CppSystem.Collections.IEnumerator routine)
    {
        var build = routine?.TryCast<EngineStandLogic._SetGroupOnEngineStand_d__8>();
        while (routine != null)
        {
            int state = build?.__1__state ?? -99;
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning($"[Harness] stand build failed in state {state}: {e.GetType().Name} {e.Message.Split('\n')[0]}");
                yield break;
            }
            MelonLoader.MelonLogger.Msg($"[Harness] stand build state {state} -> {(more ? "running" : "done")}");
            if (!more) yield break;
            yield return null;
        }
    }

    [HarnessCommand("tool-stand-unpatch")]
    private static object StandUnpatch(string args)
    {
        var target = HarmonyLib.AccessTools.Method(typeof(EngineStandLogic._SetGroupOnEngineStand_d__8), "MoveNext");
        var info = HarmonyLib.Harmony.GetPatchInfo(target);
        var owners = info == null ? new List<string>() : info.Owners.ToList();
        foreach (string owner in owners) new HarmonyLib.Harmony(owner).Unpatch(target, HarmonyLib.HarmonyPatchType.All, owner);
        return new { removed = owners };
    }

    [HarnessCommand("tool-stand-reset")]
    private static object StandReset(string args)
    {
        var stand = ToolsManager.Get()?.EngineStandLogic ?? throw new InvalidOperationException("no engine stand");
        using (ToolSync.ApplyingRemote(ModToolId.EngineStand1)) stand.ClearEngineStand();
        return "cleared locally";
    }

    [HarnessCommand("tool-stand-context")]
    private static object StandContext(string args)
    {
        var game = GameScript.Get();
        var hovered = game?.IOMouseOverCarLoader;
        var stand = ToolsManager.Get()?.EngineStandLogic;
        return new
        {
            camera = Camera.main != null,
            hoveredCar = hovered == null ? null : hovered.carToLoad,
            hoveredRootCuller = hovered != null && hovered.root != null && hovered.root.GetComponent<PartScriptCuller>() != null,
            hoveredGameObject = game?.IOMouseOverGO == null ? null : game.IOMouseOverGO.name,
            standCuller = stand?.PartScriptCuller != null,
            standTransform = stand?.EngineStand != null,
            loaderCullers = Enumerable.Range(0, CarLoaderPlaces.Get().GetCarLoadersCount())
                .Select(i => CarLoaderPlaces.Get().GetCarLoaderByIndex(i))
                .Where(c => c != null && c.IsCarLoaded())
                .Select(c => $"{c.carToLoad}:{(c.root != null && c.root.GetComponent<PartScriptCuller>() != null)}").ToList(),
        };
    }

    [HarnessCommand("tool-put")]
    private static object Put(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-put <tool> <uid>");
        var tool = ToolArg(parts[0]);
        Present(tool);
        long uid = long.Parse(parts[1]);
        var tools = ToolsManager.Get();
        if (tool == ModToolId.BrakeLathe || tool == ModToolId.BatteryCharger)
        {
            var item = Inv.GetItem(uid) ?? throw new ArgumentException($"no item {uid}");
            Inv.Delete(item);
            if (tool == ModToolId.BrakeLathe) tools.BrakeLatheLogic.SetItem(item, true);
            else tools.BatteryChargerLogic.SetItemOnBatteryCharger(item, true);
            return new { item.ID, item.UID };
        }
        var group = Inv.GetGroup(uid) ?? throw new ArgumentException($"no group {uid}");
        switch (tool)
        {
            case ModToolId.TireChanger:
                Inv.DeleteGroup(uid);
                tools.TireChangerLogic.SetGroupOnTireChanger(group, true, false);
                break;
            case ModToolId.WheelBalancer:
                if (ToolSync.RefuseIfHeld(tool)) return new { refused = true };
                Inv.DeleteGroup(uid);
                tools.WheelBalancerLogic.SetGroupOnWheelBalancer(group, true);
                break;
            case ModToolId.SpringClamp:
                Inv.DeleteGroup(uid);
                tools.SpringClampLogic.SetGroupOnSpringClamp(group, true, false);
                break;
            case ModToolId.EngineStand1:
                var stand = (EngineStandSync)ToolSync.Machine(tool);
                Inv.DeleteGroup(uid);
                MelonLoader.MelonCoroutines.Start(Drive(stand.Logic.SetGroupOnEngineStand(group, false)));
                break;
            default:
                throw new ArgumentException($"{tool} cannot be loaded from the harness");
        }
        return new { group.ID, group.UID, refused = false };
    }

    [HarnessCommand("tool-take")]
    private static object Take(string args)
    {
        var tool = ToolArg(Args(args).FirstOrDefault() ?? "");
        Present(tool);
        if (ToolSync.RefuseIfHeld(tool)) return new { refused = true };
        var tools = ToolsManager.Get();
        switch (tool)
        {
            case ModToolId.TireChanger:
                tools.TireChangerLogic.StartCoroutine(tools.TireChangerLogic.Clear());
                break;
            case ModToolId.WheelBalancer:
                tools.WheelBalancerLogic.StartCoroutine(tools.WheelBalancerLogic.Clear());
                break;
            case ModToolId.SpringClamp:
                tools.SpringClampLogic.ClearSpringClamp();
                break;
            case ModToolId.BrakeLathe:
                tools.BrakeLatheLogic.Clear();
                break;
            case ModToolId.BatteryCharger:
                tools.BatteryChargerLogic.ClearBatteryCharger();
                break;
            default:
                var stand = ((EngineStandSync)ToolSync.Machine(tool)).Logic;
                var group = stand.GetGroupOnEngineStand();
                if (group == null) throw new ArgumentException($"{tool} is empty");
                Inv.groups.Add(group);
                stand.ClearEngineStand();
                break;
        }
        return new { refused = false };
    }

    [HarnessCommand("tool-local-put")]
    private static object LocalPut(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-local-put <tool> <id|wheel|shock>");
        var tool = ToolArg(parts[0]);
        var machine = Present(tool);
        var state = new ToolSlotState { Tool = tool };
        if (tool == ModToolId.BrakeLathe || tool == ModToolId.BatteryCharger) state.Item = new Item(parts[1]) { Condition = 0.5f }.ToModItem();
        else state.Group = BuildGroup(parts[1], null).ToModGroupItem();
        MelonCoroutines.Start(Scoped(tool, machine.Put(state), state.Uids()));
        return new { tool = tool.ToString(), uid = state.Uid };
    }

    private static IEnumerator Scoped(ModToolId tool, IEnumerator routine, IEnumerable<long> neutralUids)
    {
        var scope = ToolSync.ApplyingRemote(tool, neutralUids);
        try
        {
            while (routine.MoveNext()) yield return routine.Current;
        }
        finally
        {
            scope.Dispose();
        }
    }

    [HarnessCommand("tool-mount")]
    private static object Mount(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-mount <TireChanger|SpringClamp> <true|false>");
        var tool = ToolArg(parts[0]);
        var machine = Present(tool);
        var state = machine.ReadLocal();
        if (state.IsEmpty) throw new ArgumentException($"{tool} is empty");
        bool mount = bool.Parse(parts[1]);
        MelonCoroutines.Start(Remount(tool, machine, state, mount));
        return new { tool = tool.ToString(), uid = state.Uid, mount };
    }

    private static IEnumerator Remount(ModToolId tool, ToolMachine machine, ToolSlotState state, bool mount)
    {
        var clear = Scoped(tool, machine.Clear(), state.Uids());
        while (clear.MoveNext()) yield return clear.Current;
        var tools = ToolsManager.Get();
        if (tool == ModToolId.TireChanger) tools.TireChangerLogic.SetGroupOnTireChanger(state.Group.ToGameGroupItem(), true, mount);
        else tools.SpringClampLogic.SetGroupOnSpringClamp(state.Group.ToGameGroupItem(), true, mount);
    }

    [HarnessCommand("tool-balance")]
    private static object Balance(string args)
    {
        var logic = ToolsManager.Get().WheelBalancerLogic;
        if (logic.GetGroupOnWheelBalancer() == null) throw new ArgumentException("the balancer is empty");
        var window = WindowManager.Instance?.GetWindowByID<WheelBalanceWindow>(WindowID.WheelBalance);
        if (window != null && window.isActive) window.Hide(false);
        logic.SetCanceled(false);
        logic.FinishBalanceInternal();
        return new { balanced = !logic.IsCanceled() };
    }

    [HarnessCommand("tool-balance-open")]
    private static object BalanceOpen(string args)
    {
        var logic = ToolsManager.Get().WheelBalancerLogic;
        if (logic.GetGroupOnWheelBalancer() == null) throw new ArgumentException("the balancer is empty");
        bool refused = ToolSync.RefuseIfHeld(ModToolId.WheelBalancer);
        if (!refused) logic.Balance(false);
        return new { refused };
    }

    [HarnessCommand("tool-balance-cancel")]
    private static object BalanceCancel(string args)
    {
        var window = WindowManager.Instance?.GetWindowByID<WheelBalanceWindow>(WindowID.WheelBalance);
        bool open = window != null && window.isActive;
        if (open) window.CancelAction();
        else ToolSync.ReleaseClaim(ModToolId.WheelBalancer);
        return new { windowWasOpen = open };
    }

    [HarnessCommand("tool-charger")]
    private static object Charger(string args)
    {
        bool on = (args ?? "").Trim() == "on";
        ToolsManager.Get().BatteryChargerLogic.BatteryChargerActivate(on);
        return new { on };
    }

    [HarnessCommand("tool-angle")]
    private static object Angle(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-angle <EngineStand1|EngineStand2> <degrees>");
        var stand = (EngineStandSync)Present(ToolArg(parts[0]));
        float target = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        float delta = Mathf.DeltaAngle(stand.Logic.EngineStandAngle, target);
        stand.Logic.IncreaseEngineStandAngle(delta);
        return new { from = stand.Logic.EngineStandAngle - delta, to = stand.Logic.EngineStandAngle };
    }

    [HarnessCommand("tool-stand-create")]
    private static object StandCreate(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-stand-create <EngineStand1|EngineStand2> <engineId>");
        var stand = (EngineStandSync)Present(ToolArg(parts[0]));
        GameInventory.Instance.GetEnginesToCreate(out var creatable);
        var engines = new List<string>();
        for (int i = 0; creatable != null && i < creatable.Count; i++) engines.Add(creatable[i]);
        string id = parts[1] == "auto" ? engines.FirstOrDefault() : parts[1];
        if (id == null) throw new InvalidOperationException("the game lists no engines to create");
        stand.Logic.SetEngineOnEngineStand(new Item(id));
        return new { building = id, creatable = engines.Count, listed = engines.Contains(id) };
    }

    [HarnessCommand("tool-stand-part")]
    private static object StandPart(string args)
    {
        var parts = Args(args);
        if (parts.Length != 3) throw new ArgumentException("usage: tool-stand-part <EngineStand1|EngineStand2> <key|auto> <unmount|mount>");
        var stand = (EngineStandSync)Present(ToolArg(parts[0]));
        var registry = stand.Registry ?? throw new ArgumentException("no engine registry on the stand");
        bool unmount = parts[2] == "unmount";
        string key = parts[1] != "auto" ? parts[1]
            : registry.SubKeys.OrderBy(k => k, StringComparer.Ordinal).First(k => registry.Sub(k).IsUnmounted != unmount
                && registry.Sub(k).GetUnmountWith().Count == 0 && !registry.Sub(k).IsBlocked());
        var script = registry.Sub(key) ?? throw new ArgumentException($"no part {key}");
        if (unmount) script.FastUnmount();
        else script.FastMount();
        return new { key, id = script.id };
    }

    [HarnessCommand("tool-move")]
    private static object Move(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-move <Welder|Oilbin|EngineCrane|...> <CarPlace|default>");
        var tool = (IOSpecialType)Enum.Parse(typeof(IOSpecialType), parts[0], true);
        var manager = ToolsMoveManager.Get();
        if (parts[1] == "default")
        {
            manager.SetOnDefaultPosition(tool);
            return new { tool = tool.ToString(), place = "default" };
        }
        var place = (CarPlace)Enum.Parse(typeof(CarPlace), parts[1], true);
        manager.MoveTo(tool, place, true);
        return new { tool = tool.ToString(), place = place.ToString(), moved = !manager.IsOnDefaultPosition(tool) };
    }

    [HarnessCommand("tool-repair")]
    private static object Repair(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-repair <uid> <success|fail>");
        var item = Inv.GetItem(long.Parse(parts[0])) ?? throw new ArgumentException($"no item {parts[0]}");
        if (parts[1] == "success")
        {
            item.Condition = 1f;
            item.Dent = 0f;
        }
        else
        {
            item.Condition = Mathf.Round(item.Condition * 500f) / 1000f;
        }
        ToolSync.SendItemUpdate(item);
        return new { item.UID, condition = item.Condition };
    }

    [HarnessCommand("tool-paint-part")]
    private static object PaintPart(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-paint-part <uid> <r,g,b>");
        var item = Inv.GetItem(long.Parse(parts[0])) ?? throw new ArgumentException($"no item {parts[0]}");
        var rgb = parts[1].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        item.Color = new ModColor { r = rgb[0], g = rgb[1], b = rgb[2], a = 1f }.ToCustomColor();
        item.IsPainted = true;
        ToolSync.SendItemUpdate(item);
        return new { item.UID, painted = true };
    }

    private static CustomColor ToCustomColor(this ModColor color) =>
        new ModItem { ID = "color", Color = color }.ToGameItem().Color;

    [HarnessCommand("tool-hold")]
    private static object Hold(string args)
    {
        var parts = Args(args);
        if (parts.FirstOrDefault() == "on")
        {
            holding = true;
            holdingInventory = parts.Contains("inventory");
            return holdingInventory ? "holding tool and inventory packets" : "holding tool packets";
        }
        holding = false;
        holdingInventory = false;
        var replay = new List<(PacketTypes Id, object Data, long Sender)>(held);
        held.Clear();
        replaying = true;
        try
        {
            foreach (var packet in replay) PacketRouter.Dispatch(packet.Id, packet.Data, packet.Sender);
        }
        finally
        {
            replaying = false;
        }
        return $"replayed {replay.Count}";
    }

    internal static void Reset(List<string> changed)
    {
        if (holding || held.Count > 0) changed.Add($"tool-hold{(holdingInventory ? " inventory" : "")} (dropped {held.Count} held packets)");
        if (ToolSync.Trace) changed.Add("tool-trace");
        holding = false;
        holdingInventory = false;
        held.Clear();
        ToolSync.Trace = false;
    }

    [HarmonyPatch(typeof(PacketRouter), nameof(PacketRouter.Dispatch))]
    [HarmonyPrefix]
    private static bool BeforeDispatch(PacketTypes id, object deserializedData, long senderId)
    {
        if (!holding || replaying || !(ToolPackets.Contains(id) || holdingInventory && InventoryPackets.Contains(id))) return true;
        held.Add((id, deserializedData, senderId));
        return false;
    }

    public static object Dump()
    {
        if (ToolsManager.Get() == null) return null;
        var result = new Dictionary<string, object>();
        foreach (var machine in ToolSync.Machines.Where(m => m.Present))
        {
            var state = machine.ReadLocal();
            var stand = machine as EngineStandSync;
            int claim = ToolSync.ClaimOwner(machine.Tool);
            var items = state.Group?.ItemList ?? (state.Item != null ? new List<ModItem> { state.Item } : new List<ModItem>());
            result[machine.Tool.ToString()] = new
            {
                id = state.Uid == 0 ? null : state.Item?.ID ?? state.Group?.ID,
                uid = state.Uid,
                items = stand != null
                    ? items.Select(i => i.ID).OrderBy(i => i, StringComparer.Ordinal).Cast<object>().ToList()
                    : items.Select(i => i.UID).OrderBy(u => u).Cast<object>().ToList(),
                mounting = state.Mounting,
                active = state.Active,
                balanced = machine is WheelBalancerSync && WheelBalancerSync.WindowOpen ? ToolSync.Mirror(machine.Tool).Balanced : state.Balanced,
                wheelsBalanced = items.Select(i => i.WheelData?.IsBalanced ?? false).ToList(),
                angle = stand != null ? Mathf.Round(Mathf.Repeat(state.Angle, 360f)) : 0f,
                unmountedParts = stand?.Registry == null ? null
                    : stand.Registry.SubKeys.Where(k => stand.Registry.Sub(k).IsUnmounted).OrderBy(k => k, StringComparer.Ordinal).ToList(),
                claimedBy = claim == ToolClaimUpdatePacket.Released ? null : (int?)claim,
            };
        }
        return result;
    }

    private static CarLoader CarArg(string value)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(value));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException($"no loaded car on loader {value}");
        return carLoader;
    }

    private static void ConnectCrane(CarLoader carLoader)
    {
        var tools = carLoader.ToolsData;
        tools.EngineCraneIsConnected = true;
        carLoader.ToolsData = tools;
    }

    // sync-workshop-car-tools verbs. tool-use starts the tool's own DoWorkAnim (what the ask window's accept does, minus
    // the fee), so the actor hooks and the commit point run as in the game. "paid" runs the accept lambda itself, which
    // charges the fee first (welder and interior detailing).
    [HarnessCommand("tool-use")]
    private static object Use(string args)
    {
        var parts = Args(args);
        if (parts.Length < 2 || parts.Length > 3 || (parts.Length == 3 && parts[2] != "paid"))
            throw new ArgumentException("usage: tool-use <Welder|CarWash|InteriorDetailing|InteriorDetailingStationary|OilBin> <loader> [paid]");
        var tool = ToolArg(parts[0]);
        var carLoader = CarArg(parts[1]);
        var tools = ToolsMoveManager.Get() ?? throw new ArgumentException("no workshop tools in this scene");
        if (parts.Length == 3) return UsePaid(tool, carLoader, tools);
        if (tool == ModToolId.OilBin)
        {
            float oil = carLoader.FluidsData.Oil?.Level ?? 0f;
            carLoader.UseOilbin();
            return new { tool = tool.ToString(), oil };
        }
        if (tool == ModToolId.InteriorDetailingStationary && !carLoader.IsInPlace(CarPlace.CarWash))
            throw new ArgumentException("the stationary kit works on the car at the car wash");
        GarageTool logic = tool switch
        {
            ModToolId.Welder => tools.WelderLogic,
            ModToolId.CarWash => tools.CarWashLogic,
            ModToolId.InteriorDetailing or ModToolId.InteriorDetailingStationary => tools.InteriorDetailingToolkitLogic,
            _ => throw new ArgumentException($"{tool} is not a car tool"),
        };
        logic.StartCoroutine(logic.DoWorkAnim(carLoader));
        return new { tool = tool.ToString(), effectTime = logic.effectTime };
    }

    private static object UsePaid(ModToolId tool, CarLoader carLoader, ToolsMoveManager tools)
    {
        long before = GlobalData.PlayerMoney;
        switch (tool)
        {
            case ModToolId.Welder:
                var weld = new WelderLogic.__c__DisplayClass5_0 { carLoader = carLoader, __4__this = tools.WelderLogic };
                weld.Method_Internal_Void_Boolean_PDM_0(true);
                break;
            case ModToolId.InteriorDetailing:
                var detail = new InteriorDetailingToolkitLogic.__c__DisplayClass6_0 { carLoader = carLoader, __4__this = tools.InteriorDetailingToolkitLogic };
                detail.Method_Internal_Void_Boolean_PDM_0(true);
                break;
            default:
                throw new ArgumentException($"{tool} has no paid accept (welder and interior detailing only)");
        }
        return new { tool = tool.ToString(), paid = true, moneyBefore = before, moneyAfter = GlobalData.PlayerMoney };
    }

    [HarnessCommand("tool-engine-out")]
    private static object EngineOut(string args)
    {
        var carLoader = CarArg((args ?? "").Trim());
        var engine = carLoader.e_engine_h ?? throw new ArgumentException("the car has no engine");
        var io = engine.GetComponent<InteractiveObject>();
        var blockers = new List<string>();
        foreach (var component in io.unMountPartsToUnmountGroup ?? new UnhollowerBaseLib.Il2CppReferenceArray<Component>(0))
        {
            var script = component?.TryCast<PartScript>();
            if (script != null && !script.IsUnmounted) blockers.Add(script.name);
        }
        if (io.GetMountedItemsAmount() < 1) blockers.Add("empty engine");
        if (!carLoader.EngineData.isElectric && (carLoader.FluidsData.Oil?.Level ?? 0f) > 0f) blockers.Add("engine oil");
        ConnectCrane(carLoader);
        carLoader.UseEngineCrane();
        return new { engine = engine.name, blockers };
    }

    [HarnessCommand("tool-engine-in")]
    private static object EngineIn(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-engine-in <loader> <groupUid|swap>");
        var carLoader = CarArg(parts[0]);
        string current = carLoader.e_engine_h?.name ?? throw new ArgumentException("the car has no engine");
        ConnectCrane(carLoader);
        if (parts[1] != "swap")
        {
            var group = Inv.GetGroup(long.Parse(parts[1])) ?? throw new ArgumentException($"no group {parts[1]}");
            NotificationCenter.Get().InsertEngineToCar(group);
            return new { engine = group.ID, group.UID };
        }

        string other = null;
        var options = carLoader.EngineParams?.Swapoptions;
        for (int i = 0; options != null && i < options.Length && other == null; i++)
            if (!string.IsNullOrEmpty(options[i]) && options[i] != current) other = options[i];
        if (other == null) return new { engine = (string)null, UID = 0L, refused = false, swapOption = false };
        var swap = new GroupItem(other) { ItemList = new Il2CppSystem.Collections.Generic.List<Item>(), IsNormalGroup = true };
        Inv.AddGroup(swap);
        NotificationCenter.Get().InsertEngineToCar(swap);
        bool refused = Inv.GetGroup(swap.UID) != null && carLoader.e_engine_h?.name == current;
        if (refused) Inv.DeleteGroup(swap.UID);
        return new { engine = other, swap.UID, refused, swapOption = true };
    }

    [HarnessCommand("tool-paint-car")]
    private static object PaintCar(string args)
    {
        var parts = Args(args);
        if (parts.Length != 2) throw new ArgumentException("usage: tool-paint-car <loader> <r,g,b>");
        var carLoader = CarArg(parts[0]);
        var rgb = parts[1].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var paintshop = UnityEngine.Object.FindObjectOfType<CMS.Managers.PaintshopManager>() ?? throw new ArgumentException("no paint shop in this scene");
        carLoader.SetCarColor(null, new Color(rgb[0], rgb[1], rgb[2], 1f));
        paintshop.carLoader = carLoader;
        paintshop.PaintshopType = CMS.UI.Logic.PaintshopType.Garage;
        if (!paintshop.TryGetMoneyForPaint()) throw new InvalidOperationException("not enough money to paint");
        paintshop.SubmitColor(false);
        return new { painted = true };
    }

    [HarnessCommand("tool-dyno")]
    private static object Dyno(string args)
    {
        var carLoader = CarArg((args ?? "").Trim());
        carLoader.MeasurePower();
        return new { measured = carLoader.EngineData.measured, dragIndex = carLoader.MeasuredDragIndex };
    }

    public static object ActionsSeen() => CarToolActions.Seen.OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString(), p => (object)p.Value);

    public static object LifterButtons()
    {
        var garage = GarageLoader.Get();
        var places = CarLoaderPlaces.Get();
        if (garage == null || garage.carLifter == null || places == null) return null;
        var result = new Dictionary<string, object>();
        foreach (var lifter in garage.carLifter)
        {
            var car = lifter?.GetConnectedCarLoader();
            if (car == null) continue;
            result[places.GetCarLoaderId(car).ToString()] = Enabled(lifter.ButtonUp) && Enabled(lifter.ButtonDown);
        }
        return result;
    }

    private static bool Enabled(InteractiveObject button) => button != null && button.enabled && button.gameObject.activeSelf;

    public static object Positions()
    {
        var manager = ToolsMoveManager.Get();
        if (manager == null) return null;
        return ToolPositionSync.Movable.ToDictionary(t => t.ToString(), t => (object)(manager.IsOnDefaultPosition(t) ? "default"
            : ToolSync.Positions.TryGetValue((int)t, out int place) && place >= 0 ? ((CarPlace)place).ToString() : "moved"));
    }
}
