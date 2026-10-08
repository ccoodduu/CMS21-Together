using System;
using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.Persistence;

namespace TogetherTestHarness.Features;

public static class SessionCommands
{
    [HarnessCommand("sell-item")]
    private static object SellItem(string args)
    {
        var inventory = Singleton<GameManager>.Instance.Inventory;
        string uidArg = (args ?? "").Trim();
        if (uidArg.Length > 0)
        {
            long uid = long.Parse(uidArg);
            var group = inventory.GetGroup(uid);
            if (group != null)
            {
                NotificationCenter.Get().SellItem(group, false, true);
                return new Dictionary<string, object> { ["id"] = group.ID, ["uid"] = group.UID, ["group"] = true };
            }
            var single = inventory.GetItem(uid) ?? throw new ArgumentException($"no item or group {uid}");
            NotificationCenter.Get().SellItem(single, false, true);
            return new Dictionary<string, object> { ["id"] = single.ID, ["uid"] = single.UID, ["group"] = false };
        }
        var items = inventory.GetItems();
        if (items == null || items.Count == 0) throw new InvalidOperationException("the inventory has no single items");
        var item = items[0];
        int expected = Helper.GetPrice(item, 0.5f);
        NotificationCenter.Get().SellItem(item, false, true);
        return new Dictionary<string, object> { ["id"] = item.ID, ["uid"] = item.UID, ["expected"] = expected };
    }

    [HarnessCommand("inv-add-local")]
    private static object InvAddLocal(string args)
    {
        var item = new Item((args ?? "").Trim());
        item.Condition = 0.5f;
        Singleton<GameManager>.Instance.Inventory.Add(item, false);
        return new Dictionary<string, object> { ["id"] = item.ID, ["uid"] = item.UID };
    }

    [HarnessCommand("inv-send")]
    private static object InvSend(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 3 || !Enum.TryParse(parts[0], true, out ItemActionType action))
            throw new ArgumentException("usage: inv-send <add|update|remove> <uid> [condition]");
        if (Client.Instance == null || !Client.Instance.IsConnected) throw new InvalidOperationException("not connected");
        var item = Singleton<GameManager>.Instance.Inventory.GetItem(long.Parse(parts[1])) ?? throw new ArgumentException($"no item {parts[1]}");
        if (parts.Length == 3) item.Condition = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        Client.Instance.Send(new InventoryItemActionPacket { Action = action, Item = item.ToModItem() });
        return new Dictionary<string, object> { ["action"] = action.ToString(), ["id"] = item.ID, ["uid"] = item.UID, ["condition"] = item.Condition };
    }

    [HarnessCommand("junkyard-buy")]
    private static object JunkyardBuy(string args)
    {
        var packet = new ItemsExchangePacket { IsJunkyard = true, ItemsToBuy = new List<CMS21_Together_Core.Data.GameType.ModItem>() };
        packet.ItemsToBuy.Add(new CMS21_Together_Core.Data.GameType.ModItem { ID = (args ?? "").Trim(), Condition = 0.5f, ConditionToShow = 0.5f, Quality = 1 });
        Client.Instance.Send(packet);
        return "sent";
    }

    [HarnessCommand("stats-add")]
    private static object StatsAdd(string args)
    {
        var packet = ParseStats(args, "stats-add");
        Client.Instance.Send(packet);
        return $"sent scrap {packet.ScrapsDelta}, exp {packet.ExpDelta}";
    }

    [HarnessCommand("send-early-stats")]
    private static object SendEarlyStats(string args)
    {
        var packet = ParseStats(args, "send-early-stats");
        if (ClientData.IsInitialSyncFinished) throw new InvalidOperationException("the join already finished; send this while the garage is loading");

        Client.Instance.Send(packet);
        return $"sent scrap {packet.ScrapsDelta}, exp {packet.ExpDelta} before the sync ack (snapshot {SyncTracker.CurrentSnapshotId})";
    }

    private static StatsActionPacket ParseStats(string args, string verb)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int scrap) || !int.TryParse(parts[1], out int exp))
            throw new ArgumentException($"usage: {verb} <scrap> <exp>");
        if (Client.Instance == null || !Client.Instance.IsConnected) throw new InvalidOperationException("not connected");
        return new StatsActionPacket { ScrapsDelta = scrap, ExpDelta = exp };
    }

    [HarnessCommand("player-key")]
    private static object PlayerKey(string args)
    {
        string value = (args ?? "").Trim();
        if (value.Length > 0)
            PlayerIdentity.Override = string.Equals(value, "reset", StringComparison.OrdinalIgnoreCase) ? null : value;
        return new Dictionary<string, object>
        {
            ["key"] = PlayerIdentity.Key,
            ["override"] = !string.IsNullOrEmpty(PlayerIdentity.Override),
            ["file"] = PlayerIdentity.FilePath,
        };
    }

    [HarnessCommand("to-menu")]
    private static object ToMenu(string args)
    {
        bool save = string.Equals((args ?? "").Trim(), "save", StringComparison.OrdinalIgnoreCase);
        if (Client.Instance != null && Client.Instance.IsConnected) Client.Instance.Disconnect();

        var center = NotificationCenter.m_instance;
        if (center == null) throw new InvalidOperationException("NotificationCenter not available");
        center.StartCoroutine(center.SelectSceneToLoad("Menu", SceneType.Menu, true, save));
        return save ? "loading menu (save)" : "loading menu";
    }
}
