using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Digest;
using CMS21Together.Logic.Reconciliation;
using CMS21Together.Network.Handlers;

namespace TogetherTestHarness.Features;

public static class DigestCommands
{
    [HarnessCommand("digest-show")]
    private static object DigestShow(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var keys = parts.Length > 0
            ? new[] { (parts[0], parts.Length > 1 ? parts[1] : "") }
            : new[] { (DigestMappers.WorldKey, ""), (DigestMappers.InventoryKey, ""), (DigestMappers.PlacementKey, ""), (DigestMappers.CarsKey, "0") };
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
        if (parts.Length != 2) throw new ArgumentException("usage: digest-hold <key> on|off");
        if (parts[1] == "on") ClientDigests.HeldWrong[parts[0]] = "held";
        else ClientDigests.HeldWrong.Remove(parts[0]);
        return ClientDigests.HeldWrong.Keys.ToList();
    }

    internal static void Reset(List<string> changed)
    {
        if (ClientDigests.HeldWrong.Count > 0) changed.Add($"digest-hold {string.Join(",", ClientDigests.HeldWrong.Keys)}");
        ClientDigests.HeldWrong.Clear();
    }
}
