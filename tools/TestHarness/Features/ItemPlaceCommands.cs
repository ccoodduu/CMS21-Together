using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Tools;

namespace TogetherTestHarness.Features;

public static class ItemPlaceCommands
{
    [HarnessCommand("item-where")]
    private static object ItemWhere(string args)
    {
        long uid = long.Parse((args ?? "").Trim());
        var places = new List<string>();
        var inventory = Singleton<GameManager>.Instance.Inventory;
        if (inventory.GetItem(uid) != null || inventory.GetGroup(uid) != null) places.Add("inventory");
        var warehouse = Singleton<GameManager>.Instance.Warehouse?.GetAllItemsAndGroups();
        for (int i = 0; warehouse != null && i < warehouse.Count; i++)
            if (warehouse[i].UID == uid) places.Add("warehouse");
        foreach (var machine in ToolSync.Machines.Where(m => m.Present))
            if (machine.ReadLocal().Uids().Contains(uid)) places.Add($"machine:{machine.Tool}");
        return new { uid, where = places.Count == 0 ? "none" : string.Join(",", places), count = places.Count };
    }

    [HarnessCommand("warehouse-move")]
    private static object WarehouseMove(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[1] != "to" && parts[1] != "from") throw new ArgumentException("usage: warehouse-move <uid> to|from");
        long uid = long.Parse(parts[0]);
        bool toWarehouse = parts[1] == "to";
        var center = NotificationCenter.Get() ?? throw new InvalidOperationException("NotificationCenter not available");
        if (toWarehouse)
        {
            var inventory = Singleton<GameManager>.Instance.Inventory;
            var group = inventory.GetGroup(uid);
            if (group != null) center.MoveItem(group, true, "Warehouse");
            else center.MoveItem(inventory.GetItem(uid) ?? throw new ArgumentException($"no item or group {uid} in the inventory"), true, "Warehouse");
            return new { uid, to = "warehouse" };
        }
        var all = Singleton<GameManager>.Instance.Warehouse?.GetAllItemsAndGroups() ?? throw new InvalidOperationException("no warehouse");
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].UID != uid) continue;
            var groupItem = all[i].TryCast<GroupItem>();
            if (groupItem != null) center.MoveItem(groupItem, false, "Warehouse");
            else center.MoveItem(all[i].Cast<Item>(), false, "Warehouse");
            return new { uid, to = "inventory" };
        }
        throw new ArgumentException($"no item or group {uid} in the warehouse");
    }
}
