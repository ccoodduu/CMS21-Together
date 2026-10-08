using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Cars
{
	public static class InventoryChanges
	{
		public const int UnknownRemover = -1;
		private const int MaxRemovers = 10000;
		private const double CopySeconds = 60;

		private class Copy
		{
			public ModItem Item;
			public ModGroupItem Group;
			public DateTime At;
		}

		private static readonly Dictionary<long, int> removedBy = new Dictionary<long, int>();
		private static readonly Queue<long> order = new Queue<long>();
		private static readonly Dictionary<long, Copy> copies = new Dictionary<long, Copy>();
		private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();

		public static void NoteRemoved(long uid, int clientId) => Note(uid, clientId, null);

		public static void NoteRemoved(ModItem item, int clientId) => Note(item.UID, clientId, new Copy { Item = item, At = DateTime.UtcNow });

		public static void NoteRemoved(ModGroupItem group, int clientId) => Note(group.UID, clientId, new Copy { Group = group, At = DateTime.UtcNow });

		private static void Note(long uid, int clientId, Copy copy)
		{
			lock (removedBy)
			{
				if (!removedBy.ContainsKey(uid)) order.Enqueue(uid);
				removedBy[uid] = clientId;
				while (order.Count > MaxRemovers) removedBy.Remove(order.Dequeue());
				Prune();
				if (copy != null) copies[uid] = copy;
				else copies.Remove(uid);
			}
		}

		public static bool RemovedByOther(long uid, int clientId)
		{
			lock (removedBy) return removedBy.TryGetValue(uid, out int remover) && remover != clientId;
		}

		public static bool RemovedBy(long uid, int clientId)
		{
			lock (removedBy) return removedBy.TryGetValue(uid, out int remover) && remover == clientId;
		}

		public static int Remover(long uid)
		{
			lock (removedBy) return removedBy.TryGetValue(uid, out int remover) ? remover : int.MinValue;
		}

		public static bool Seen(long uid)
		{
			lock (removedBy) return removedBy.ContainsKey(uid);
		}

		public static bool TryTakeCopy(long uid, out ModItem item, out ModGroupItem group)
		{
			item = null;
			group = null;
			lock (removedBy)
			{
				Prune();
				if (!copies.TryGetValue(uid, out var copy)) return false;
				copies.Remove(uid);
				removedBy.Remove(uid);
				item = copy.Item;
				group = copy.Group;
				return true;
			}
		}

		private static void Prune()
		{
			var now = DateTime.UtcNow;
			foreach (long uid in copies.Where(c => (now - c.Value.At).TotalSeconds > CopySeconds).Select(c => c.Key).ToList()) copies.Remove(uid);
		}

		public static string DescribeRemover(long uid)
		{
			int remover;
			lock (removedBy)
				if (!removedBy.TryGetValue(uid, out remover)) return "never seen";
			if (remover == UnknownRemover) return "removed by the server";
			return $"removed by client {remover} '{Presence.PresenceRegistry.Get(remover)?.Username ?? "left"}'";
		}

		public static void Count(string name)
		{
			lock (counters) counters[name] = Counter(name) + 1;
		}

		public static int Counter(string name)
		{
			lock (counters) return counters.TryGetValue(name, out int value) ? value : 0;
		}

		public static IEnumerable<long> StillHeld(InventoryDelta delta)
		{
			var inventory = GameDataManager.CurrentState.InventoryState;
			return delta.RemovedItemUids.Where(uid => inventory.InventoryItems.Any(i => i.UID == uid))
				.Concat(delta.RemovedGroupUids.Where(uid => inventory.InventoryGroupItems.Any(g => g.UID == uid)))
				.ToList();
		}

		public static void Apply(InventoryDelta delta, int clientId)
		{
			if (delta == null || delta.IsEmpty) return;
			var inventory = GameDataManager.CurrentState.InventoryState;
			foreach (var item in inventory.InventoryItems.Where(i => delta.RemovedItemUids.Contains(i.UID)).ToList()) NoteRemoved(item, clientId);
			foreach (var group in inventory.InventoryGroupItems.Where(g => delta.RemovedGroupUids.Contains(g.UID)).ToList()) NoteRemoved(group, clientId);
			inventory.InventoryItems.RemoveAll(i => delta.RemovedItemUids.Contains(i.UID));
			inventory.InventoryGroupItems.RemoveAll(g => delta.RemovedGroupUids.Contains(g.UID));
			foreach (var item in delta.AddedItems)
				if (inventory.InventoryItems.All(i => i.UID != item.UID)) inventory.InventoryItems.Add(item);
			foreach (var group in delta.AddedGroups)
				if (inventory.InventoryGroupItems.All(g => g.UID != group.UID)) inventory.InventoryGroupItems.Add(group);
		}
	}
}
