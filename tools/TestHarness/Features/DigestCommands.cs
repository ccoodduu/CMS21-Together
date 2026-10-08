using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Reconciliation;
using CMS21Together.Logic.Tools;
using CMS21Together.Network.Handlers;

namespace TogetherTestHarness.Features;

public static class DigestCommands
{
    [HarnessCommand("digest-show")]
    private static object DigestShow(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var keys = parts.Length > 0
            ? new List<(string, string)> { (parts[0], parts.Length > 1 ? parts[1] : "") }
            : DigestMappers.GlobalKeys.Select(k => (k, "")).Concat(DigestMappers.CarKeys.Select(k => (k, "0"))).ToList();
        var result = new Dictionary<string, object>();
        foreach (var (key, subKey) in keys)
        {
            var projection = ClientDigests.Project(key, subKey);
            result[string.IsNullOrEmpty(subKey) ? key : $"{key}:{subKey}"] = projection == null ? "not ready" : (object)new
            {
                hash = projection.Hash().ToString("X16"),
                rows = parts.Length > 0 ? projection.Sorted().Select(r => $"{r.Id}|{r.Field}={r.Value}").ToList() : null,
            };
        }
        return result;
    }

    [HarnessCommand("inv-corrupt")]
    private static object InvCorrupt(string args)
    {
        var inventory = Singleton<GameManager>.Instance.Inventory;
        var items = inventory.GetItems();
        if (items == null || items.Count == 0) throw new InvalidOperationException("no single item to remove");
        var item = items[0];
        bool previous = InventoryHandlers.IgnoreInventoryHooks;
        InventoryHandlers.IgnoreInventoryHooks = true;
        try { inventory.Delete(item); }
        finally { InventoryHandlers.IgnoreInventoryHooks = previous; }
        return new Dictionary<string, object> { ["id"] = item.ID, ["uid"] = item.UID };
    }

    [HarnessCommand("digest-hold")]
    private static object DigestHold(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[1] != "on" && parts[1] != "notready" && parts[1] != "off") throw new ArgumentException("usage: digest-hold <key> on|notready|off");
        ClientDigests.HeldWrong.Remove(parts[0]);
        ClientDigests.HeldNotReady.Remove(parts[0]);
        if (parts[1] == "on") ClientDigests.HeldWrong[parts[0]] = "held";
        if (parts[1] == "notready") ClientDigests.HeldNotReady.Add(parts[0]);
        return new { wrong = ClientDigests.HeldWrong.Keys.ToList(), notReady = ClientDigests.HeldNotReady.ToList() };
    }

    // Changes local state of one digest key without a packet, so only the digest can find it.
    [HarnessCommand("state-corrupt")]
    private static object StateCorrupt(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) throw new ArgumentException("usage: state-corrupt <car-details|workshop-tools|warehouse|garage> [loader]");
        switch (parts[0])
        {
            case DigestMappers.DetailsKey:
            {
                int loader = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
                if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException($"no car on loader {loader}");
                var fluid = CarDetailsIO.Read(carLoader, CarDetailSection.Fluids).Fluids.FirstOrDefault(f => f.Type == ModCarFluidType.EngineCoolant)
                            ?? throw new InvalidOperationException("no coolant");
                fluid.Level = fluid.Level > 0.5f ? 0.1f : 0.9f;
                CarDetailsIO.Apply(carLoader, new ModCarDetails { Fluids = new List<ModFluidLevel> { fluid } });
                CarDetailsSync.RememberCurrent(loader);
                return new { key = parts[0], loader, entry = CarDetailEntries.Fluid(fluid.Type, fluid.Id), level = fluid.Level };
            }
            case DigestMappers.ToolsKey:
            {
                var machine = ToolSync.Machine(ModToolId.BrakeLathe);
                if (machine == null || !machine.Present) throw new InvalidOperationException("no brake lathe");
                if (!machine.ReadLocal().IsEmpty) throw new InvalidOperationException("the brake lathe is not empty");
                var item = new ModItem { ID = "tarczaHamulcowa_1", UID = 9_000_000_000L + DateTime.UtcNow.Ticks % 1_000_000, Condition = 0.3f };
                using (ToolSync.ApplyingRemote(ModToolId.BrakeLathe, new[] { item.UID }))
                {
                    var put = machine.Put(new ToolSlotState { Tool = ModToolId.BrakeLathe, Item = item });
                    while (put.MoveNext()) { }
                }
                return new { key = parts[0], tool = "BrakeLathe", uid = item.UID };
            }
            case DigestMappers.WarehouseKey:
            {
                var warehouse = Singleton<GameManager>.Instance.Warehouse ?? throw new InvalidOperationException("no warehouse");
                var item = new ModItem { ID = "tarczaHamulcowa_1", UID = 9_100_000_000L + DateTime.UtcNow.Ticks % 1_000_000, Condition = 0.6f };
                bool previous = InventoryHandlers.IgnoreInventoryHooks;
                InventoryHandlers.IgnoreInventoryHooks = true;
                try { warehouse.Add(item.ToGameItem()); }
                finally { InventoryHandlers.IgnoreInventoryHooks = previous; }
                return new { key = parts[0], uid = item.UID };
            }
            case DigestMappers.GarageKey:
                GlobalData.BarnsAmount += 1;
                return new { key = parts[0], barns = GlobalData.BarnsAmount };
            default:
                throw new ArgumentException($"no corrupt mode for '{parts[0]}'");
        }
    }

    internal static void Reset(List<string> changed)
    {
        if (ClientDigests.HeldWrong.Count > 0) changed.Add($"digest-hold {string.Join(",", ClientDigests.HeldWrong.Keys)}");
        if (ClientDigests.HeldNotReady.Count > 0) changed.Add($"digest-hold notready {string.Join(",", ClientDigests.HeldNotReady)}");
        ClientDigests.HeldWrong.Clear();
        ClientDigests.HeldNotReady.Clear();
    }
}
