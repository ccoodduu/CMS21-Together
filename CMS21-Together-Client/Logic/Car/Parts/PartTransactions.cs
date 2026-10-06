using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.Network.Handlers;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

// A mount/unmount and the inventory change it causes travel as one CarPartsChange, so the server can reject both
// together when another player was faster (design D3). Inventory events of an open transaction are kept here
// instead of being sent; a transaction that never commits flushes them through the normal inventory packets.
public static class PartTransactions
{
	private const float IdleFlushSeconds = 10f;

	private class Transaction
	{
		public int Loader;
		public readonly HashSet<string> Keys = new HashSet<string>();
		public readonly HashSet<string> ItemIds = new HashSet<string>();
		public readonly InventoryDelta Delta = new InventoryDelta();
		public readonly Dictionary<long, ModItem> RemovedItems = new Dictionary<long, ModItem>();
		public readonly Dictionary<long, ModGroupItem> RemovedGroups = new Dictionary<long, ModGroupItem>();
		public float LastActivity;
	}

	private static readonly List<Transaction> open = new List<Transaction>();
	private static readonly Dictionary<int, (InventoryDelta Delta, Dictionary<long, ModItem> Items, Dictionary<long, ModGroupItem> Groups)> committed =
		new Dictionary<int, (InventoryDelta, Dictionary<long, ModItem>, Dictionary<long, ModGroupItem>)>();

	public static void Reset()
	{
		open.Clear();
		committed.Clear();
	}

	public static void Open(int loader, IEnumerable<string> keys, IEnumerable<string> itemIds)
	{
		var tx = open.FirstOrDefault(t => t.Loader == loader && t.Keys.Overlaps(keys));
		if (tx == null)
		{
			tx = new Transaction { Loader = loader };
			open.Add(tx);
		}
		tx.Keys.UnionWith(keys);
		tx.ItemIds.UnionWith(itemIds.Where(id => !string.IsNullOrEmpty(id)));
		tx.LastActivity = Time.realtimeSinceStartup;
	}

	public static void OpenForPart(int loader, PartRegistry registry, PartScript script)
	{
		var keys = new List<string>();
		var ids = new List<string>();
		var parts = new List<PartScript> { script };
		foreach (var member in script.GetUnmountWith()) parts.Add(member);
		foreach (var part in parts)
		{
			if (!registry.TryGetSubPath(part, out var path)) continue;
			keys.Add(PartKeys.Sub(path));
			ids.Add(part.GetID());
			ids.Add(part.GetIDWithTuned());
		}
		if (keys.Count > 0) Open(loader, keys, ids);
	}

	public static void OpenForBody(int loader, int index, CarPart part) =>
		Open(loader, new[] { PartKeys.Body(index) }, new[] { part.GetIDWithTuned(), part.name });

	public static bool CaptureAdd(ModItem item)
	{
		var tx = Match(item.ID);
		if (tx == null) return false;
		if (tx.Delta.RemovedItemUids.Remove(item.UID)) tx.RemovedItems.Remove(item.UID);
		else tx.Delta.AddedItems.Add(item);
		tx.LastActivity = Time.realtimeSinceStartup;
		return true;
	}

	public static bool CaptureDelete(ModItem item)
	{
		var tx = Match(item.ID);
		if (tx == null) return false;
		if (tx.Delta.AddedItems.RemoveAll(i => i.UID == item.UID) == 0)
		{
			tx.Delta.RemovedItemUids.Add(item.UID);
			tx.RemovedItems[item.UID] = item;
		}
		tx.LastActivity = Time.realtimeSinceStartup;
		return true;
	}

	public static bool CaptureAddGroup(ModGroupItem group)
	{
		var tx = Match(group.ID);
		if (tx == null) return false;
		if (tx.Delta.RemovedGroupUids.Remove(group.UID)) tx.RemovedGroups.Remove(group.UID);
		else tx.Delta.AddedGroups.Add(group);
		tx.LastActivity = Time.realtimeSinceStartup;
		return true;
	}

	public static bool CaptureDeleteGroup(ModGroupItem group)
	{
		var tx = Match(group.ID);
		if (tx == null) return false;
		if (tx.Delta.AddedGroups.RemoveAll(g => g.UID == group.UID) == 0)
		{
			tx.Delta.RemovedGroupUids.Add(group.UID);
			tx.RemovedGroups[group.UID] = group;
		}
		tx.LastActivity = Time.realtimeSinceStartup;
		return true;
	}

	public static InventoryDelta TakeFor(int loader, IEnumerable<string> changedKeys, int txId)
	{
		var keys = new HashSet<string>(changedKeys);
		var delta = new InventoryDelta();
		var items = new Dictionary<long, ModItem>();
		var groups = new Dictionary<long, ModGroupItem>();
		foreach (var tx in open.Where(t => t.Loader == loader && t.Keys.Overlaps(keys)).ToList())
		{
			delta.AddedItems.AddRange(tx.Delta.AddedItems);
			delta.AddedGroups.AddRange(tx.Delta.AddedGroups);
			delta.RemovedItemUids.AddRange(tx.Delta.RemovedItemUids);
			delta.RemovedGroupUids.AddRange(tx.Delta.RemovedGroupUids);
			foreach (var pair in tx.RemovedItems) items[pair.Key] = pair.Value;
			foreach (var pair in tx.RemovedGroups) groups[pair.Key] = pair.Value;
			open.Remove(tx);
		}
		if (!delta.IsEmpty) committed[txId] = (delta, items, groups);
		return delta;
	}

	public static void OnResult(CarPartsChangeResultPacket result)
	{
		if (!committed.TryGetValue(result.TxId, out var removed)) return;
		committed.Remove(result.TxId);
		if (result.Accepted) return;
		var sentDelta = removed.Delta;

		var inventory = Singleton<GameManager>.Instance.Inventory;
		bool previous = InventoryHandlers.IgnoreInventoryHooks;
		InventoryHandlers.IgnoreInventoryHooks = true;
		try
		{
			foreach (var item in sentDelta.AddedItems)
			{
				var local = inventory.GetItem(item.UID);
				if (local != null) inventory.Delete(local);
			}
			foreach (var group in sentDelta.AddedGroups) inventory.DeleteGroup(group.UID);
			foreach (long uid in result.RestoreUids)
			{
				if (removed.Items.TryGetValue(uid, out var item)) inventory.Add(item.ToGameItem());
				else if (removed.Groups.TryGetValue(uid, out var group)) inventory.AddGroup(group.ToGameGroupItem());
			}
		}
		finally
		{
			InventoryHandlers.IgnoreInventoryHooks = previous;
		}
		InventoryHandlers.RefreshInventoryWindow();
	}

	public static void FlushIdle()
	{
		float now = Time.realtimeSinceStartup;
		foreach (var tx in open.Where(t => now - t.LastActivity > IdleFlushSeconds).ToList())
		{
			open.Remove(tx);
			if (tx.Delta.IsEmpty) continue;
			Log.Debug($"[Parts] Loader {tx.Loader}: transaction for {string.Join(",", tx.Keys)} never committed; sending its inventory changes on their own.");
			foreach (var item in tx.Delta.AddedItems) Client.Instance.Send(new InventoryItemActionPacket { Action = ItemActionType.Add, Item = item });
			foreach (var item in tx.RemovedItems.Values) Client.Instance.Send(new InventoryItemActionPacket { Action = ItemActionType.Remove, Item = item });
			foreach (var group in tx.Delta.AddedGroups) Client.Instance.Send(new InventoryGroupItemActionPacket { Action = ItemActionType.Add, GroupItem = group });
			foreach (var group in tx.RemovedGroups.Values) Client.Instance.Send(new InventoryGroupItemActionPacket { Action = ItemActionType.Remove, GroupItem = group });
		}
	}

	private static Transaction Match(string itemId)
	{
		if (string.IsNullOrEmpty(itemId) || !ClientScene.IsGarageReady) return null;
		return open.LastOrDefault(t => t.ItemIds.Contains(itemId));
	}
}
