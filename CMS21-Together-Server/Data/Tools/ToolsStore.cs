using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Tools
{
	// sync-workshop-machines D2/D5/D6/D7/D9: slot states with compare-and-set on the held UID, machine properties, tool
	// positions, the wheel balancer reservation and the part overlay of the engine stands. Callers hold StateLock.
	public static class ToolsStore
	{
		public const float ClaimExpirySeconds = 300f;

		private static readonly HashSet<ModToolId> ItemTools = new HashSet<ModToolId> { ModToolId.BrakeLathe, ModToolId.BatteryCharger };
		private static readonly Dictionary<ModToolId, string> IdPrefixes = new Dictionary<ModToolId, string> { [ModToolId.BatteryCharger] = "akumulator" };
		private static readonly HashSet<int> MovableTools = new HashSet<int> { 11, 12, 13, 14, 17, 24 };

		private class Claim
		{
			public int Owner;
			public float Since;
		}

		private static readonly Dictionary<ModToolId, Claim> claims = new Dictionary<ModToolId, Claim>();

		private static ToolsState State => GameDataManager.CurrentState.ToolsState;

		public static int CreatedEngines { get; private set; }

		public static void ResetCounters() => CreatedEngines = 0;

		public static void Initialize()
		{
			PresenceEvents.Left += ReleaseOwner;
			PresenceEvents.SceneChanged += OnSceneChanged;
		}

		public static void ResetRuntime() => claims.Clear();

		public static ToolSlotState Slot(ModToolId tool) => State.Slots.TryGetValue(tool, out var slot) ? slot : ToolSlotState.Empty(tool);

		public static int ClaimOwner(ModToolId tool) => claims.TryGetValue(tool, out var claim) ? claim.Owner : ToolClaimUpdatePacket.Released;

		public static void OnSlotUpdate(int clientId, ToolSlotUpdatePacket packet)
		{
			var incoming = packet.State;
			if (incoming == null || !ModTools.IsMachine(incoming.Tool)) return;
			var current = Slot(incoming.Tool);
			string reason = TryStore(clientId, packet);
			if (reason != null)
			{
				var outcome = IsPut(current, incoming) ? PutRefused(clientId, incoming) : SlotItemOutcome.Unchanged;
				Server.SendToClient(new ToolSlotRejectedPacket { Current = current, Reason = reason, ClientSeq = packet.ClientSeq, Item = outcome }, clientId);
				Logger.Info($"[Tools] {incoming.Tool}: update from client {clientId} rejected ({reason}{(packet.Created ? ", a built engine" : "")}; item {outcome}).");
				return;
			}
			Server.SendToClients(packet, clientId);
		}

		public static string TryStore(int clientId, ToolSlotUpdatePacket packet)
		{
			var incoming = packet.State;
			var current = Slot(incoming.Tool);
			string reason = Check(clientId, current, incoming, packet.ExpectedUid);
			if (reason != null) return reason;
			var stored = Normalize(incoming, current);
			State.Slots[stored.Tool] = stored;
			packet.State = stored;
			if (packet.Created && IsPut(current, stored))
			{
				CreatedEngines++;
				Logger.Info($"[Tools] {stored.Tool}: client {clientId} built a new engine {stored.Group?.ID} ({stored.Uid}).");
			}
			Logger.Info($"[Tools] {stored.Tool}: {Describe(stored)} from client {clientId} (was {current.Uid}).");
			return null;
		}

		private static string Check(int clientId, ToolSlotState current, ToolSlotState incoming, long expectedUid)
		{
			if (current.Uid != expectedUid) return $"holds {current.Uid}, expected {expectedUid}";
			if (claims.TryGetValue(incoming.Tool, out var claim) && claim.Owner != clientId) return $"reserved by player {claim.Owner}";
			if (incoming.IsEmpty) return null;
			if (incoming.Item != null && incoming.Group != null) return "both an item and a group";
			if (ItemTools.Contains(incoming.Tool) != (incoming.Item != null)) return incoming.Item != null ? "needs a group" : "needs an item";
			string id = incoming.Item?.ID ?? incoming.Group?.ID ?? "";
			if (IdPrefixes.TryGetValue(incoming.Tool, out string prefix) && !id.StartsWith(prefix, StringComparison.Ordinal)) return $"'{id}' does not fit";
			var other = State.Slots.Values.FirstOrDefault(s => s.Tool != incoming.Tool && !s.IsEmpty && s.Uid == incoming.Uid);
			if (other != null) return $"{incoming.Uid} is on {other.Tool}";
			if (!IsPut(current, incoming)) return null;
			int itemHolder = CarLocks.ItemHolder(incoming.Uid, clientId);
			if (itemHolder >= 0) return $"{incoming.Uid} held by player {itemHolder}";
			if (InInventory(incoming.Uid)) return null;
			if (InventoryChanges.RemovedByOther(incoming.Uid, clientId)) return $"{incoming.Uid} used by player {InventoryChanges.Remover(incoming.Uid)}";
			if (!InventoryChanges.Seen(incoming.Uid))
			{
				InventoryChanges.Count("unknownSlotItem");
				Logger.Info($"[Tools] {incoming.Tool}: client {clientId} puts {incoming.Uid} '{id}', which the server never saw; accepted.");
			}
			return null;
		}

		private static bool IsPut(ToolSlotState current, ToolSlotState incoming) => !incoming.IsEmpty && incoming.Uid != current.Uid;

		private static bool InInventory(long uid)
		{
			var inventory = GameDataManager.CurrentState.InventoryState;
			return inventory.InventoryItems.Any(i => i.UID == uid) || inventory.InventoryGroupItems.Any(g => g.UID == uid);
		}

		private static SlotItemOutcome PutRefused(int clientId, ToolSlotState incoming)
		{
			long uid = incoming.Uid;
			if (InInventory(uid)) return SlotItemOutcome.Unchanged;
			if (InventoryChanges.RemovedByOther(uid, clientId)) return SlotItemOutcome.Gone;
			if (!InventoryChanges.RemovedBy(uid, clientId) || !InventoryChanges.TryTakeCopy(uid, out var item, out var group)) return SlotItemOutcome.Unchanged;
			var inventory = GameDataManager.CurrentState.InventoryState;
			if (item != null)
			{
				inventory.InventoryItems.Add(item);
				Server.SendToClients(new InventoryItemActionPacket { Action = ItemActionType.Add, Item = item }, clientId);
			}
			else
			{
				inventory.InventoryGroupItems.Add(group);
				Server.SendToClients(new InventoryGroupItemActionPacket { Action = ItemActionType.Add, GroupItem = group }, clientId);
			}
			Logger.Info($"[Tools] {incoming.Tool}: {uid} is back in the inventory after client {clientId}'s refused put.");
			return SlotItemOutcome.Returned;
		}

		private static ToolSlotState Normalize(ToolSlotState incoming, ToolSlotState current)
		{
			incoming.Angle = current.Angle;
			if (incoming.IsEmpty)
			{
				incoming.Parts = new Dictionary<string, CarSubPartUpdatePacket>();
				incoming.Active = false;
				incoming.Balanced = false;
				incoming.Mounting = false;
			}
			else if (incoming.Uid == current.Uid && (incoming.Parts == null || incoming.Parts.Count == 0))
			{
				incoming.Parts = current.Parts;
			}
			incoming.Parts ??= new Dictionary<string, CarSubPartUpdatePacket>();
			return incoming;
		}

		public static void OnProperty(int clientId, ToolSlotPropertyPacket packet)
		{
			if (!ModTools.IsMachine(packet.Tool) || float.IsNaN(packet.Value) || float.IsInfinity(packet.Value)) return;
			var slot = Slot(packet.Tool);
			if (packet.Property == ToolProperty.Angle) slot.Angle = packet.Value;
			else if (packet.Property == ToolProperty.Active) slot.Active = packet.Value != 0f;
			else return;
			State.Slots[packet.Tool] = slot;
			Server.SendToClients(packet, clientId);
			Logger.Debug($"[Tools] {packet.Tool}: {packet.Property} = {packet.Value} from client {clientId}.");
		}

		public static void OnPosition(int clientId, ToolPositionPacket packet)
		{
			if (!MovableTools.Contains(packet.IoSpecialType) || packet.CarPlace < ToolPositionPacket.DefaultPosition || packet.CarPlace > 8) return;
			State.Positions[packet.IoSpecialType] = packet.CarPlace;
			Server.SendToClients(packet, clientId);
			Logger.Info($"[Tools] Tool {packet.IoSpecialType} moved to {(packet.CarPlace == ToolPositionPacket.DefaultPosition ? "default" : packet.CarPlace.ToString())} by client {clientId}.");
		}

		public static void OnPartChange(int clientId, ToolPartChangePacket change)
		{
			var slot = Slot(change.Tool);
			string conflict = slot.IsEmpty || slot.Uid != change.EngineUid ? $"engine {change.EngineUid} is not on {change.Tool}" : FindConflict(slot, change, clientId);
			if (conflict != null)
			{
				var reject = new ToolPartChangeResultPacket { Tool = change.Tool, EngineUid = change.EngineUid, TxId = change.TxId, Accepted = false, Reason = conflict };
				foreach (var record in change.SubParts)
					if (slot.Parts.TryGetValue(record.Key, out var stored)) reject.SubParts.Add(PartRecordMerge.WithChanged(stored, PartFields.All));
				reject.RestoreUids.AddRange(InventoryChanges.StillHeld(change.Delta));
				Server.SendToClient(reject, clientId);
				Logger.Info($"[Tools] {change.Tool}: part change {change.TxId} from client {clientId} rejected: {conflict}");
				return;
			}

			var preconditions = change.Preconditions.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
			var relay = new ToolPartChangePacket { Tool = change.Tool, EngineUid = change.EngineUid, TxId = change.TxId, Preconditions = change.Preconditions, Delta = change.Delta };
			var result = new ToolPartChangeResultPacket { Tool = change.Tool, EngineUid = change.EngineUid, TxId = change.TxId, Accepted = true };
			var dropped = new List<string>();
			foreach (var record in change.SubParts)
			{
				slot.Parts.TryGetValue(record.Key, out var stored);
				var outcome = PartRecordMerge.Normalise(stored, record, preconditions.TryGetValue(record.Key, out var pre) ? pre : null);
				if (outcome.Record != null)
				{
					outcome.Record.Changed = PartFields.None;
					slot.Parts[record.Key] = outcome.Record;
					if (outcome.Written != PartFields.None) relay.SubParts.Add(PartRecordMerge.WithChanged(outcome.Record, outcome.Written));
				}
				else dropped.Add($"{record.Key} ({outcome.Outcome} {outcome.Stale})");
				if (slot.Parts.TryGetValue(record.Key, out var now) && PartRecordMerge.Differ(now, record) is var differ && differ != PartFields.None)
					result.SubParts.Add(PartRecordMerge.WithChanged(now, differ));
			}
			InventoryChanges.Apply(change.Delta, clientId);
			Server.SendToClient(result, clientId);
			Server.SendToClients(relay, clientId);
			Logger.Info($"[Tools] {change.Tool}: part change {change.TxId} from client {clientId} ({change.SubParts.Count} parts, relayed {relay.SubParts.Count}, returned {result.SubParts.Count}{(dropped.Count > 0 ? $", dropped {string.Join(", ", dropped)}" : "")}, inventory +{change.Delta.AddedItems.Count + change.Delta.AddedGroups.Count} -{change.Delta.RemovedItemUids.Count + change.Delta.RemovedGroupUids.Count}).");
		}

		private static string FindConflict(ToolSlotState slot, ToolPartChangePacket change, int clientId)
		{
			foreach (var precondition in change.Preconditions)
				if (slot.Parts.TryGetValue(precondition.Key, out var stored) && stored.Unmounted != precondition.WasUnmounted)
					return $"{precondition.Key} is already {(stored.Unmounted ? "unmounted" : "mounted")}";
			var inventory = GameDataManager.CurrentState.InventoryState;
			foreach (long uid in change.Delta.RemovedItemUids)
				if (inventory.InventoryItems.All(i => i.UID != uid) && InventoryChanges.RemovedByOther(uid, clientId)) return $"item {uid} is gone";
			foreach (long uid in change.Delta.RemovedGroupUids)
				if (inventory.InventoryGroupItems.All(g => g.UID != uid) && InventoryChanges.RemovedByOther(uid, clientId)) return $"group {uid} is gone";
			return null;
		}

		public static void OnClaim(int clientId, ToolClaimPacket packet)
		{
			if (!ModTools.IsMachine(packet.Tool)) return;
			claims.TryGetValue(packet.Tool, out var claim);
			if (packet.Release)
			{
				if (claim != null && claim.Owner == clientId) Release(packet.Tool, "released");
				return;
			}
			if (claim != null && claim.Owner != clientId)
			{
				Server.SendToClient(new ToolClaimUpdatePacket { Tool = packet.Tool, OwnerPlayerId = claim.Owner }, clientId);
				Logger.Info($"[Tools] {packet.Tool}: claim by client {clientId} denied, held by {claim.Owner}.");
				return;
			}
			claims[packet.Tool] = new Claim { Owner = clientId, Since = ServerTime.Time };
			Server.SendToClients(new ToolClaimUpdatePacket { Tool = packet.Tool, OwnerPlayerId = clientId });
			Logger.Info($"[Tools] {packet.Tool}: claimed by client {clientId}.");
		}

		public static void ReleaseOwner(int clientId)
		{
			foreach (var pair in claims.Where(c => c.Value.Owner == clientId).ToList()) Release(pair.Key, $"client {clientId} left");
		}

		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			if (from == GameScene.Garage && to != GameScene.Garage) ReleaseOwner(clientId);
		}

		public static void Expire(float now)
		{
			foreach (var pair in claims.Where(c => now - c.Value.Since > ClaimExpirySeconds).ToList()) Release(pair.Key, "expired");
		}

		private static void Release(ModToolId tool, string why)
		{
			if (!claims.Remove(tool)) return;
			Server.SendToClients(new ToolClaimUpdatePacket { Tool = tool, OwnerPlayerId = ToolClaimUpdatePacket.Released });
			Logger.Info($"[Tools] {tool}: claim {why}.");
		}

		public static int SendSnapshot(int clientId)
		{
			Server.SendToClient(new ToolsStatePacket
			{
				Slots = State.Slots.ToDictionary(p => p.Key, p => p.Value),
				Positions = State.Positions.ToDictionary(p => p.Key, p => p.Value),
				Claims = claims.ToDictionary(p => p.Key, p => p.Value.Owner)
			}, clientId);
			return 1;
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"items: unknownSlotItem {InventoryChanges.Counter("unknownSlotItem")}, removeMissing {InventoryChanges.Counter("removeMissing")}; createdEngines {CreatedEngines}";
			foreach (var tool in Enum.GetValues(typeof(ModToolId)).Cast<ModToolId>().Where(ModTools.IsMachine))
			{
				var slot = Slot(tool);
				string claim = claims.TryGetValue(tool, out var c) ? $", claimed by {c.Owner}" : "";
				yield return $"{tool}: {Describe(slot)}, angle {slot.Angle:0}{claim}";
			}
			foreach (var pair in State.Positions.OrderBy(p => p.Key))
				yield return $"tool {pair.Key}: {(pair.Value == ToolPositionPacket.DefaultPosition ? "default" : $"car place {pair.Value}")}";
		}

		private static string Describe(ToolSlotState slot)
		{
			if (slot.IsEmpty) return "empty";
			string what = slot.Item != null ? $"item {slot.Item.ID} {slot.Item.UID}" : $"group {slot.Group.ID} {slot.Group.UID} ({slot.Group.ItemList?.Count ?? 0} items)";
			return $"{what}{(slot.Mounting ? ", mounting" : "")}{(slot.Balanced ? ", balanced" : "")}{(slot.Parts.Count > 0 ? $", {slot.Parts.Count} part records" : "")}";
		}
	}
}
