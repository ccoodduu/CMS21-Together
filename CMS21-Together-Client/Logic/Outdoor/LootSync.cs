using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.Network.Handlers;
using CMS21Together.UI;
using HarmonyLib;
using UnhollowerRuntimeLib;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

[HarmonyPatch]
public static class LootSync
{
	public class Pile
	{
		public int Index;
		public string Key;
		public Junk Junk;
	}

	private class PileRef
	{
		public int Index;
		public string Key;
	}

	private static readonly Dictionary<long, PileRef> pileOfUid = new Dictionary<long, PileRef>();
	private static readonly Dictionary<long, LootItemStatus> states = new Dictionary<long, LootItemStatus>();
	private static readonly Dictionary<long, int> holders = new Dictionary<long, int>();
	private static readonly HashSet<long> held = new HashSet<long>();
	private static bool suppress;

	public static bool PilesReady { get; private set; }
	public static int UnmatchedPiles { get; private set; }
	public static int SkippedGroupItems { get; private set; }
	public static string LastWindowType { get; private set; }

	public static IReadOnlyCollection<long> Held => held;

	private static bool Active => OutdoorSession.IsShared && OutdoorScenes.HasPiles(OutdoorSession.Scene);

	public static bool SharedPiles => Active;

	public static void Reset()
	{
		pileOfUid.Clear();
		states.Clear();
		holders.Clear();
		held.Clear();
		PilesReady = false;
		UnmatchedPiles = 0;
		SkippedGroupItems = 0;
	}

	public static string Key(Vector3 position) =>
		string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0}", position.x, position.y, position.z);

	public static List<Pile> Scan()
	{
		var junks = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Junk>())
			.Select(o => o.TryCast<Junk>())
			.Where(j => j != null && j.gameObject.scene.IsValid() && j.gameObject.scene.isLoaded)
			.Select(j => (Junk: j, Key: Key(j.transform.position)))
			.OrderBy(j => j.Key, StringComparer.Ordinal)
			.ToList();
		var piles = new List<Pile>();
		var seen = new Dictionary<string, int>();
		foreach (var junk in junks)
		{
			string key = junk.Key;
			if (seen.TryGetValue(key, out int count))
			{
				seen[key] = count + 1;
				key = $"{key}#{count + 1}";
			}
			else seen[key] = 0;
			piles.Add(new Pile { Index = piles.Count, Key = key, Junk = junk.Junk });
		}
		return piles;
	}

	private static List<bool> RandomShowActive()
	{
		var generator = UnityEngine.Object.FindObjectOfType<JunkyardGenerator>();
		var root = generator == null ? null : generator.JunkyardRandomShow;
		var active = new List<bool>();
		for (int i = 0; root != null && i < root.childCount; i++) active.Add(root.GetChild(i).gameObject.activeSelf);
		return active;
	}

	private static void ApplyRandomShow(List<bool> active)
	{
		var generator = UnityEngine.Object.FindObjectOfType<JunkyardGenerator>();
		var root = generator == null ? null : generator.JunkyardRandomShow;
		if (root == null || active == null) return;
		for (int i = 0; i < root.childCount && i < active.Count; i++) root.GetChild(i).gameObject.SetActive(active[i]);
	}

	private static List<Item> Items(Junk junk)
	{
		var items = new List<Item>();
		var list = junk.ItemsInTrash;
		for (int i = 0; list != null && i < list.Count; i++)
		{
			var item = list[i]?.TryCast<Item>();
			if (item != null) items.Add(item);
			else if (list[i] != null) SkippedGroupItems++;
		}
		return items;
	}

	public static void Record()
	{
		if (!Active) return;
		var piles = Scan();
		var packet = new OutdoorLootRecordPacket { InstanceId = OutdoorSession.InstanceId, RandomShowActive = RandomShowActive() };
		foreach (var pile in piles)
		{
			var recorded = new LootPile { Index = pile.Index, Key = pile.Key };
			foreach (var item in Items(pile.Junk))
			{
				recorded.Items.Add(item.ToModItem());
				pileOfUid[item.UID] = new PileRef { Index = pile.Index, Key = pile.Key };
			}
			packet.Piles.Add(recorded);
		}
		Client.Instance.Send(packet);
		PilesReady = true;
		Log.Info($"[Outdoor] Loot record sent: {packet.Piles.Count} piles, {pileOfUid.Count} items{(SkippedGroupItems > 0 ? $", {SkippedGroupItems} group items left local" : "")}, random show {packet.RandomShowActive.Count(a => a)}/{packet.RandomShowActive.Count}.");
	}

	public static void ReplayIfNeeded()
	{
		if (!Active || PilesReady || OutdoorSession.IsGenerator) return;
		var instance = OutdoorSession.Instance;
		if (instance.Piles == null)
		{
			Log.Info("[Outdoor] Piles wait for the first player's record.");
			return;
		}

		foreach (var state in instance.ItemStates)
		{
			states[state.Uid] = state.Status;
			holders[state.Uid] = state.HolderId;
		}
		var local = Scan();
		var byKey = instance.Piles.Where(p => p.Key != null).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
		var used = new HashSet<LootPile>();
		int unmatched = 0, items = 0;
		suppress = true;
		try
		{
			foreach (var pile in local)
			{
				if (!byKey.TryGetValue(pile.Key, out var recorded) || used.Contains(recorded))
					recorded = instance.Piles.FirstOrDefault(p => p.Index == pile.Index && !used.Contains(p));
				pile.Junk.ItemsInTrash.Clear();
				if (recorded == null)
				{
					unmatched++;
					continue;
				}
				used.Add(recorded);
				foreach (var modItem in recorded.Items)
				{
					pileOfUid[modItem.UID] = new PileRef { Index = recorded.Index, Key = recorded.Key };
					if (StatusOf(modItem.UID) != LootItemStatus.Available) continue;
					pile.Junk.ItemsInTrash.Add(modItem.ToGameItem());
					items++;
				}
			}
			ApplyRandomShow(instance.RandomShowActive);
		}
		finally
		{
			suppress = false;
		}
		UnmatchedPiles = unmatched;
		PilesReady = true;
		Log.Info($"[Outdoor] Piles replayed: {local.Count} local piles, {instance.Piles.Count} recorded, {items} items available, {unmatched} unmatched piles emptied.");
		RefreshWindow();
	}

	private static LootItemStatus StatusOf(long uid) => states.TryGetValue(uid, out var status) ? status : LootItemStatus.Available;

	private static Junk FindJunk(int index, string key)
	{
		var piles = Scan();
		return (piles.FirstOrDefault(p => p.Key == key) ?? piles.FirstOrDefault(p => p.Index == index))?.Junk;
	}

	private static void RefreshWindow()
	{
		var windows = WindowManager.Instance;
		if (windows == null || !windows.IsWindowActive(WindowID.ItemsExchange)) return;
		windows.GetWindowByID<ItemsExchangeWindow>(WindowID.ItemsExchange)?.Refresh(false);
	}

	private static string NameOf(int playerId) =>
		Logic.Player.PresenceManager.Roster.TryGetValue(playerId, out var player) ? player.Record.Username : $"Player {playerId}";

	[HarmonyPatch(typeof(ItemsExchangeWindow), nameof(ItemsExchangeWindow.Show))]
	[HarmonyPrefix]
	private static bool BeforePileWindow(ref bool __result)
	{
		if (!Active || PilesReady) return true;
		ModNotify.ShowToast("The piles are not shared yet; try again in a moment.");
		__result = false;
		return false;
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.MoveItem), typeof(Item), typeof(bool), typeof(string))]
	[HarmonyPrefix]
	private static bool BeforeMoveItem(Item itemToMove, bool toWarehouse, string windowType)
	{
		if (suppress || !Active || itemToMove == null) return true;
		LastWindowType = windowType;
		long uid = itemToMove.UID;
		if (!pileOfUid.ContainsKey(uid)) return true;
		if (toWarehouse)
		{
			if (StatusOf(uid) != LootItemStatus.Available && !held.Contains(uid))
			{
				ModNotify.ShowToast($"{NameOf(holders.TryGetValue(uid, out int by) ? by : 0)} took it first.");
				return false;
			}
			SendTake(uid);
			return true;
		}
		if (held.Remove(uid)) Client.Instance.Send(new LootPutBackPacket { InstanceId = OutdoorSession.InstanceId, Uid = uid });
		return true;
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.MoveItem), typeof(GroupItem), typeof(bool), typeof(string))]
	[HarmonyPrefix]
	private static void BeforeMoveGroupItem(GroupItem itemToMove, bool toWarehouse, string windowType)
	{
		if (!Active || itemToMove == null) return;
		LastWindowType = windowType;
		Log.Info($"[Outdoor] Group item {itemToMove.ID}#{itemToMove.UID} moved ({(toWarehouse ? "taken" : "put back")}, window {windowType}); group items stay local.");
	}

	[HarmonyPatch(typeof(TempInventory), nameof(TempInventory.RemoveItem), typeof(BaseItem))]
	[HarmonyPrefix]
	private static void BeforeRemoveTaken(BaseItem item)
	{
		if (item != null) ReturnRemoved(item.UID, item.TryCast<Item>());
	}

	[HarmonyPatch(typeof(TempInventory), nameof(TempInventory.RemoveItem), typeof(long))]
	[HarmonyPrefix]
	private static void BeforeRemoveTakenUid(long itemUID) => ReturnRemoved(itemUID, null);

	private static void ReturnRemoved(long uid, Item item)
	{
		if (suppress || !Active || !held.Remove(uid)) return;
		Client.Instance.Send(new LootPutBackPacket { InstanceId = OutdoorSession.InstanceId, Uid = uid });
		if (item == null || !pileOfUid.TryGetValue(uid, out var pile)) return;
		var junk = FindJunk(pile.Index, pile.Key);
		if (junk != null && !Contains(junk, uid)) junk.ItemsInTrash.Add(item);
	}

	private static void SendTake(long uid)
	{
		held.Add(uid);
		var pile = pileOfUid[uid];
		Client.Instance.Send(new LootTakePacket { InstanceId = OutdoorSession.InstanceId, Uid = uid, PileIndex = pile.Index, PileKey = pile.Key });
	}

	private static bool Contains(Junk junk, long uid)
	{
		var list = junk.ItemsInTrash;
		for (int i = 0; list != null && i < list.Count; i++)
			if (list[i] != null && list[i].UID == uid) return true;
		return false;
	}

	private static bool RemoveFromPiles(long uid)
	{
		bool removed = false;
		foreach (var pile in Scan())
		{
			var list = pile.Junk.ItemsInTrash;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				if (list[i] == null || list[i].UID != uid) continue;
				list.RemoveAt(i);
				removed = true;
			}
		}
		return removed;
	}

	public static void OnUpdate(LootUpdatePacket packet)
	{
		if (!Active || packet.InstanceId != OutdoorSession.InstanceId) return;
		states[packet.Uid] = packet.Status;
		holders[packet.Uid] = packet.By;
		if (packet.Status == LootItemStatus.Available)
		{
			holders.Remove(packet.Uid);
			if (packet.Item == null) return;
			pileOfUid[packet.Uid] = new PileRef { Index = packet.PileIndex, Key = packet.PileKey };
			var junk = FindJunk(packet.PileIndex, packet.PileKey);
			if (junk != null && !Contains(junk, packet.Uid) && !held.Contains(packet.Uid))
			{
				suppress = true;
				try { junk.ItemsInTrash.Add(packet.Item.ToGameItem()); }
				finally { suppress = false; }
			}
		}
		else if (!held.Contains(packet.Uid))
		{
			RemoveFromPiles(packet.Uid);
		}
		RefreshWindow();
	}

	public static void OnRefused(LootTakeRefusedPacket packet)
	{
		if (!Active || packet.InstanceId != OutdoorSession.InstanceId) return;
		held.Remove(packet.Uid);
		if (packet.By > 0)
		{
			states[packet.Uid] = LootItemStatus.Held;
			holders[packet.Uid] = packet.By;
		}
		var manager = Singleton<GameManager>.Instance;
		suppress = true;
		InventoryHandlers.IgnoreInventoryHooks = true;
		try
		{
			manager.TempInventory.RemoveItem(packet.Uid);
			var item = manager.Inventory.GetItem(packet.Uid);
			if (item != null) manager.Inventory.Delete(item);
			RemoveFromPiles(packet.Uid);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Outdoor] Undoing the take of {packet.Uid} failed: {ex.Message}");
		}
		finally
		{
			InventoryHandlers.IgnoreInventoryHooks = false;
			suppress = false;
		}
		string by = packet.ByName ?? (packet.By > 0 ? NameOf(packet.By) : "the server");
		Log.Info($"[Outdoor] Take of {packet.Uid} refused: {by} has it.");
		ModNotify.ShowToast(packet.By > 0 ? $"{by} took it first." : "That item is not shared; it stays in the pile.");
		RefreshWindow();
	}

	public static Item HarnessTake(int pileIndex, string pileKey, long uid, int itemIndex)
	{
		var junk = FindJunk(pileIndex, pileKey) ?? throw new InvalidOperationException($"no pile {pileKey ?? pileIndex.ToString()}");
		var items = Items(junk);
		var item = uid != 0 ? items.FirstOrDefault(i => i.UID == uid) : itemIndex >= 0 && itemIndex < items.Count ? items[itemIndex] : null;
		if (item == null) throw new InvalidOperationException($"no item {(uid != 0 ? uid.ToString() : $"#{itemIndex}")} in that pile ({items.Count} items)");
		if (Active && pileOfUid.ContainsKey(item.UID)) SendTake(item.UID);
		junk.ItemsInTrash.Remove(item);
		var manager = Singleton<GameManager>.Instance;
		manager.TempInventory.AddItem(item);
		InventoryHandlers.IgnoreInventoryHooks = true;
		try { manager.Inventory.Add(item, false); }
		finally { InventoryHandlers.IgnoreInventoryHooks = false; }
		return item;
	}

	public static List<object> Describe() =>
		Scan().Select(p => (object)new
		{
			index = p.Index,
			key = p.Key,
			items = Items(p.Junk).Select(i => new { i.ID, i.UID, condition = Mathf.Round(i.Condition * 1000f) / 1000f }).ToList(),
		}).ToList();

	public static object States() => new
	{
		ready = PilesReady,
		known = pileOfUid.Count,
		held = held.OrderBy(u => u).ToList(),
		taken = states.Where(s => s.Value != LootItemStatus.Available).OrderBy(s => s.Key).Select(s => new { uid = s.Key, status = s.Value.ToString(), by = holders.TryGetValue(s.Key, out int by) ? by : 0 }).ToList(),
		unmatchedPiles = UnmatchedPiles,
		groupItemsLeftLocal = SkippedGroupItems,
		lastWindowType = LastWindowType,
	};
}
