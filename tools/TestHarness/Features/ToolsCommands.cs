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

    private static bool holding;
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
                NotificationCenter.Get().ActionHangOn(group);
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
        if (!manager.CanMove(tool, place)) return new { tool = tool.ToString(), place = place.ToString(), moved = false };
        manager.MoveTo(tool, place, true);
        return new { tool = tool.ToString(), place = place.ToString(), moved = true };
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
        if ((args ?? "").Trim() == "on")
        {
            holding = true;
            return "holding tool packets";
        }
        holding = false;
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

    [HarmonyPatch(typeof(PacketRouter), nameof(PacketRouter.Dispatch))]
    [HarmonyPrefix]
    private static bool BeforeDispatch(PacketTypes id, object deserializedData, long senderId)
    {
        if (!holding || replaying || !ToolPackets.Contains(id)) return true;
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

    public static object Positions()
    {
        var manager = ToolsMoveManager.Get();
        if (manager == null) return null;
        return ToolPositionSync.Movable.ToDictionary(t => t.ToString(), t => (object)(manager.IsOnDefaultPosition(t) ? "default"
            : ToolSync.Positions.TryGetValue((int)t, out int place) && place >= 0 ? ((CarPlace)place).ToString() : "moved"));
    }
}
