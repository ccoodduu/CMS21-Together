using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.Network.Handlers;
using CMS21Together.UI;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Tools;

// sync-workshop-machines D2-D4/D10: the client mirror of the server's machine state, local change sends with the
// expected UID, loser compensation, and inventory-neutral remote applies serialized per machine.
public static class ToolSync
{
	private const float PendingSeconds = 60f;
	private const float ApplyAllTimeoutSeconds = 30f;

	private class PendingUpdate
	{
		public ToolSlotState Previous;
		public ToolSlotState Attempted;
		public List<long> Added;
		public float SentAt;
	}

	public static readonly ToolMachine[] Machines =
	{
		new TireChangerSync(), new WheelBalancerSync(), new SpringClampSync(), new EngineStandSync(ModToolId.EngineStand1),
		new EngineStandSync(ModToolId.EngineStand2), new BrakeLatheSync(), new BatteryChargerSync(),
	};

	private static readonly Dictionary<ModToolId, ToolSlotState> slots = new Dictionary<ModToolId, ToolSlotState>();
	private static readonly Dictionary<int, int> positions = new Dictionary<int, int>();
	private static readonly Dictionary<ModToolId, int> claims = new Dictionary<ModToolId, int>();
	private static readonly HashSet<ModToolId> ownClaims = new HashSet<ModToolId>();
	private static readonly Dictionary<int, PendingUpdate> pending = new Dictionary<int, PendingUpdate>();
	private static readonly Dictionary<ModToolId, HashSet<long>> takeStart = new Dictionary<ModToolId, HashSet<long>>();
	private static readonly Dictionary<ModToolId, int> applying = new Dictionary<ModToolId, int>();
	private static readonly Dictionary<long, int> neutral = new Dictionary<long, int>();
	private static readonly HashSet<long> seenByHooks = new HashSet<long>();
	private static readonly HashSet<ModToolId> applyRequested = new HashSet<ModToolId>();
	private static readonly HashSet<ModToolId> applyRunning = new HashSet<ModToolId>();
	private static int nextSeq = 1;

	public static bool Trace { get; set; }

	public static bool CanSend => Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished
	                              && ClientScene.IsGarageReady && !SyncTracker.InSnapshot;

	public static IReadOnlyDictionary<int, int> Positions => positions;

	public static ToolMachine Machine(ModToolId tool) => Machines.FirstOrDefault(m => m.Tool == tool);

	public static ToolSlotState Mirror(ModToolId tool) => slots.TryGetValue(tool, out var slot) ? slot : ToolSlotState.Empty(tool);

	public static int ClaimOwner(ModToolId tool) => claims.TryGetValue(tool, out int owner) ? owner : ToolClaimUpdatePacket.Released;

	public static bool IsApplyingRemote(ModToolId tool) => applying.ContainsKey(tool);

	public static bool IsBusy => applyRunning.Count > 0;

	public static void Reset()
	{
		slots.Clear();
		positions.Clear();
		claims.Clear();
		ownClaims.Clear();
		pending.Clear();
		takeStart.Clear();
		applyRequested.Clear();
		ToolPositionSync.Reset();
		EngineStandParts.Reset();
		CarTools.CarToolActions.Reset();
	}

	public static void TraceEvent(string text)
	{
		if (Trace) Log.Info($"[ToolTrace] {Time.frameCount} {text}");
	}

	// Local changes

	public static void MarkTakeStart(ModToolId tool)
	{
		if (!CanSend || IsApplyingRemote(tool)) return;
		takeStart[tool] = InventoryUids();
		TraceEvent($"{tool} take started");
	}

	public static void SendLocalOf(ModToolId tool)
	{
		var machine = Machine(tool);
		if (machine != null && machine.Present) SendLocal(machine.ReadLocal());
	}

	public static void SendLocal(ToolSlotState state, IEnumerable<long> extraAdded = null)
	{
		TraceEvent($"{state.Tool} local state {state.Uid} (send {CanSend && !IsApplyingRemote(state.Tool)})");
		if (!CanSend || IsApplyingRemote(state.Tool)) return;
		var previous = Mirror(state.Tool);
		if (previous.Uid == state.Uid && previous.Mounting == state.Mounting && previous.Balanced == state.Balanced && state.Parts.Count == 0) return;

		var added = new List<long>();
		if (takeStart.TryGetValue(state.Tool, out var before))
		{
			added.AddRange(InventoryUids().Where(uid => !before.Contains(uid)));
			takeStart.Remove(state.Tool);
		}
		if (extraAdded != null) added.AddRange(extraAdded);

		state.Angle = previous.Angle;
		if (state.Uid == previous.Uid && state.Parts.Count == 0) state.Parts = previous.Parts;
		float now = Time.realtimeSinceStartup;
		foreach (int old in pending.Where(p => now - p.Value.SentAt > PendingSeconds).Select(p => p.Key).ToList()) pending.Remove(old);
		int seq = nextSeq++;
		pending[seq] = new PendingUpdate { Previous = previous, Attempted = state, Added = added, SentAt = now };
		slots[state.Tool] = state;
		Log.Info($"[Tools] {state.Tool}: local change {previous.Uid} -> {state.Uid}.");
		Client.Instance.Send(new ToolSlotUpdatePacket { State = state, ExpectedUid = previous.Uid, ClientSeq = seq });
	}

	public static void SendProperty(ModToolId tool, ToolProperty property, float value)
	{
		TraceEvent($"{tool} {property} = {value}");
		if (!CanSend || IsApplyingRemote(tool)) return;
		var slot = Mirror(tool);
		if (property == ToolProperty.Angle) slot.Angle = value;
		else slot.Active = value != 0f;
		slots[tool] = slot;
		Client.Instance.Send(new ToolSlotPropertyPacket { Tool = tool, Property = property, Value = value });
	}

	public static void SendPosition(int ioSpecialType, int carPlace)
	{
		TraceEvent($"tool {ioSpecialType} moved to {carPlace}");
		if (!CanSend) return;
		positions[ioSpecialType] = carPlace;
		Client.Instance.Send(new ToolPositionPacket { IoSpecialType = ioSpecialType, CarPlace = carPlace });
	}

	public static void SendItemUpdate(Item item)
	{
		if (item == null) return;
		TraceEvent($"item update {item.ID} {item.UID}");
		if (!CanSend || Singleton<GameManager>.Instance.Inventory.GetItem(item.UID) == null) return;
		Client.Instance.Send(new InventoryItemActionPacket { Action = ItemActionType.Update, Item = item.ToModItem() });
	}

	public static void Claim(ModToolId tool)
	{
		if (!CanSend) return;
		ownClaims.Add(tool);
		Client.Instance.Send(new ToolClaimPacket { Tool = tool });
	}

	public static void ReleaseClaim(ModToolId tool)
	{
		if (!ownClaims.Remove(tool) && ClaimOwner(tool) != Client.Instance.ID) return;
		if (Client.Instance.IsConnectionValid) Client.Instance.Send(new ToolClaimPacket { Tool = tool, Release = true });
	}

	public static bool HeldByOther(ModToolId tool, out int owner)
	{
		owner = ClaimOwner(tool);
		return Client.Instance != null && Client.Instance.IsConnectionValid && owner != ToolClaimUpdatePacket.Released && owner != Client.Instance.ID;
	}

	public static bool RefuseIfHeld(ModToolId tool)
	{
		if (!HeldByOther(tool, out int owner)) return false;
		NotifyHeld(tool, owner);
		return true;
	}

	public static void NotifyHeld(ModToolId tool, int owner) => ModNotify.ShowToast($"{PlayerName(owner)} is using the {Label(tool)}.");

	// Remote changes

	public static void OnRemoteSlot(ToolSlotState state)
	{
		slots[state.Tool] = state;
		RequestApply(state.Tool);
	}

	public static void MirrorSlot(ToolSlotState state) => slots[state.Tool] = state;

	public static void OnRejected(ToolSlotRejectedPacket packet)
	{
		var current = packet.Current;
		slots[current.Tool] = current;
		pending.TryGetValue(packet.ClientSeq, out var update);
		pending.Remove(packet.ClientSeq);
		Log.Warn($"[Tools] {current.Tool}: local change rejected ({packet.Reason}); applying the server's state.");
		ModNotify.ShowToast($"Another player changed the {Label(current.Tool)} first.");
		MelonCoroutines.Start(Run(Compensate(current.Tool, update)));
	}

	public static void OnRemoteProperty(ToolSlotPropertyPacket packet)
	{
		var slot = Mirror(packet.Tool);
		if (packet.Property == ToolProperty.Angle) slot.Angle = packet.Value;
		else if (packet.Property == ToolProperty.Active) slot.Active = packet.Value != 0f;
		slots[packet.Tool] = slot;
		var machine = Machine(packet.Tool);
		if (machine == null || !machine.Present) return;
		Enter(packet.Tool, Enumerable.Empty<long>());
		try { machine.ApplyProperty(packet.Property, packet.Value); }
		finally { Exit(packet.Tool, Enumerable.Empty<long>()); }
	}

	public static void MirrorProperty(ToolSlotPropertyPacket packet)
	{
		var slot = Mirror(packet.Tool);
		if (packet.Property == ToolProperty.Angle) slot.Angle = packet.Value;
		else if (packet.Property == ToolProperty.Active) slot.Active = packet.Value != 0f;
		slots[packet.Tool] = slot;
	}

	public static void MirrorPosition(ToolPositionPacket packet) => positions[packet.IoSpecialType] = packet.CarPlace;

	public static void OnClaimUpdate(ToolClaimUpdatePacket packet)
	{
		if (packet.OwnerPlayerId == ToolClaimUpdatePacket.Released) claims.Remove(packet.Tool);
		else claims[packet.Tool] = packet.OwnerPlayerId;
		if (packet.OwnerPlayerId == ToolClaimUpdatePacket.Released || packet.OwnerPlayerId == Client.Instance.ID || !ownClaims.Remove(packet.Tool)) return;
		Log.Info($"[Tools] {packet.Tool}: claim lost to player {packet.OwnerPlayerId}.");
		NotifyHeld(packet.Tool, packet.OwnerPlayerId);
		var machine = Machine(packet.Tool);
		if (machine != null && machine.Present) machine.OnClaimLost();
	}

	public static void ReplaceMirror(ToolsStatePacket packet)
	{
		slots.Clear();
		foreach (var pair in packet.Slots ?? new Dictionary<ModToolId, ToolSlotState>())
		{
			pair.Value.Parts ??= new Dictionary<string, CarSubPartUpdatePacket>();
			slots[pair.Key] = pair.Value;
		}
		positions.Clear();
		foreach (var pair in packet.Positions ?? new Dictionary<int, int>()) positions[pair.Key] = pair.Value;
		claims.Clear();
		foreach (var pair in packet.Claims ?? new Dictionary<ModToolId, int>()) claims[pair.Key] = pair.Value;
		ownClaims.Clear();
		pending.Clear();
		takeStart.Clear();
	}

	public static void OnState(ToolsStatePacket packet, int snapshotId)
	{
		ReplaceMirror(packet);
		MelonCoroutines.Start(Run(ApplyAll(snapshotId)));
	}

	private static IEnumerator ApplyAll(int snapshotId)
	{
		try
		{
			foreach (var machine in Machines) RequestApply(machine.Tool);
			float deadline = Time.realtimeSinceStartup + ApplyAllTimeoutSeconds;
			while (applyRunning.Count > 0 && Time.realtimeSinceStartup < deadline) yield return null;
			if (applyRunning.Count > 0) Log.Warn($"[Tools] Snapshot apply still running for {string.Join(", ", applyRunning)}.");
			ToolPositionSync.ApplyAll();
			Log.Info($"[Tools] Snapshot applied ({slots.Values.Count(s => !s.IsEmpty)} loaded machines, {positions.Count} tool positions).");
		}
		finally
		{
			SyncTracker.Applied(SyncOrder.WorkshopToolsKey, snapshotId);
		}
	}

	public static void RequestApply(ModToolId tool)
	{
		applyRequested.Add(tool);
		if (applyRunning.Add(tool)) MelonCoroutines.Start(RunApplies(tool));
	}

	private static IEnumerator RunApplies(ModToolId tool)
	{
		while (applyRequested.Remove(tool))
		{
			var apply = Run(ApplyTo(tool));
			while (apply.MoveNext()) yield return apply.Current;
		}
		applyRunning.Remove(tool);
	}

	private static IEnumerator ApplyTo(ModToolId tool)
	{
		var machine = Machine(tool);
		if (machine == null || !machine.Present) yield break;
		var target = Mirror(tool);
		var local = machine.ReadLocal();
		var uids = local.Uids().Concat(target.Uids()).ToList();
		var before = InventoryUids();
		Enter(tool, uids);
		try
		{
			if (local.Uid != target.Uid || (!target.IsEmpty && local.Mounting != target.Mounting))
			{
				TraceEvent($"{tool} remote apply {local.Uid} -> {target.Uid}");
				if (!local.IsEmpty)
				{
					var clear = machine.Clear();
					while (clear.MoveNext()) yield return clear.Current;
				}
				if (!target.IsEmpty)
				{
					var put = machine.Put(target);
					while (put.MoveNext()) yield return put.Current;
				}
			}
			machine.ApplyFlags(target);
		}
		finally
		{
			RemoveSilentAdditions(before);
			Exit(tool, uids);
		}
	}

	private static IEnumerator Compensate(ModToolId tool, PendingUpdate update)
	{
		RequestApply(tool);
		while (applyRunning.Contains(tool)) yield return null;
		if (update == null) yield break;

		var inventory = Singleton<GameManager>.Instance.Inventory;
		if (!update.Attempted.IsEmpty && update.Attempted.Uid != update.Previous.Uid)
		{
			long uid = update.Attempted.Uid;
			bool onMachine = slots.Values.Any(s => s.Uids().Contains(uid));
			bool inInventory = update.Attempted.Item != null ? inventory.GetItem(uid) != null : inventory.GetGroup(uid) != null;
			if (onMachine || inInventory) yield break;
			Log.Info($"[Tools] {tool}: returning {uid} to the inventory.");
			if (update.Attempted.Item != null) inventory.Add(update.Attempted.Item.ToGameItem());
			else inventory.AddGroup(update.Attempted.Group.ToGameGroupItem());
		}
		else if (update.Attempted.IsEmpty && !update.Previous.IsEmpty)
		{
			var keep = new HashSet<long>(update.Previous.Uids());
			foreach (long uid in update.Added.Where(uid => !keep.Contains(uid)))
			{
				Log.Info($"[Tools] {tool}: dropping {uid}, another player took the item first.");
				var item = inventory.GetItem(uid);
				if (item != null) inventory.Delete(item);
				else if (inventory.GetGroup(uid) != null) inventory.DeleteGroup(uid);
			}
		}
		InventoryHandlers.RefreshInventoryWindow();
	}

	// Inventory neutrality (D3.3)

	public static IDisposable ApplyingRemote(ModToolId tool, IEnumerable<long> neutralUids = null) => new Scope(tool, neutralUids?.ToList() ?? new List<long>());

	private sealed class Scope : IDisposable
	{
		private readonly ModToolId tool;
		private readonly List<long> uids;

		public Scope(ModToolId tool, List<long> uids)
		{
			this.tool = tool;
			this.uids = uids;
			Enter(tool, uids);
		}

		public void Dispose() => Exit(tool, uids);
	}

	private static void Enter(ModToolId tool, IEnumerable<long> uids)
	{
		applying[tool] = applying.TryGetValue(tool, out int count) ? count + 1 : 1;
		foreach (long uid in uids) neutral[uid] = neutral.TryGetValue(uid, out int n) ? n + 1 : 1;
	}

	private static void Exit(ModToolId tool, IEnumerable<long> uids)
	{
		if (applying.TryGetValue(tool, out int count) && count > 1) applying[tool] = count - 1;
		else applying.Remove(tool);
		foreach (long uid in uids)
		{
			if (neutral.TryGetValue(uid, out int n) && n > 1) neutral[uid] = n - 1;
			else neutral.Remove(uid);
		}
		if (applying.Count == 0) seenByHooks.Clear();
	}

	public static bool BlockInventoryCall(long uid, string call, string id)
	{
		TraceEvent($"Inventory.{call} {id} {uid}{(InventoryHandlers.IgnoreInventoryHooks ? " (mod)" : "")}");
		if (applying.Count == 0) return false;
		seenByHooks.Add(uid);
		return !InventoryHandlers.IgnoreInventoryHooks && neutral.ContainsKey(uid);
	}

	private static void RemoveSilentAdditions(HashSet<long> before)
	{
		var inventory = Singleton<GameManager>.Instance?.Inventory;
		if (inventory == null) return;
		var items = new List<Item>();
		foreach (var item in inventory.items)
			if (!before.Contains(item.UID) && !seenByHooks.Contains(item.UID)) items.Add(item);
		var groups = new List<GroupItem>();
		foreach (var group in inventory.groups)
			if (!before.Contains(group.UID) && !seenByHooks.Contains(group.UID)) groups.Add(group);
		if (items.Count == 0 && groups.Count == 0) return;
		bool previous = InventoryHandlers.IgnoreInventoryHooks;
		InventoryHandlers.IgnoreInventoryHooks = true;
		try
		{
			foreach (var item in items)
			{
				Log.Warn($"[Tools] Removing {item.ID} {item.UID}, added by the game during a remote machine apply.");
				inventory.Delete(item);
			}
			foreach (var group in groups)
			{
				Log.Warn($"[Tools] Removing group {group.ID} {group.UID}, added by the game during a remote machine apply.");
				inventory.DeleteGroup(group.UID);
			}
		}
		finally
		{
			InventoryHandlers.IgnoreInventoryHooks = previous;
		}
	}

	public static HashSet<long> InventoryUids()
	{
		var uids = new HashSet<long>();
		var inventory = Singleton<GameManager>.Instance?.Inventory;
		if (inventory == null) return uids;
		foreach (var item in inventory.items) uids.Add(item.UID);
		foreach (var group in inventory.groups) uids.Add(group.UID);
		return uids;
	}

	public static IEnumerator Run(IEnumerator routine)
	{
		while (true)
		{
			bool more;
			try
			{
				more = routine.MoveNext();
			}
			catch (Exception ex)
			{
				Log.Error($"[Tools] {ex}");
				more = false;
			}
			if (!more) yield break;
			yield return routine.Current;
		}
	}

	public static string PlayerName(int playerId) =>
		PresenceManager.Roster.TryGetValue(playerId, out var player) && player.Record != null ? player.Record.Username : $"Player {playerId}";

	public static string Label(ModToolId tool) => tool switch
	{
		ModToolId.TireChanger => "tire changer",
		ModToolId.WheelBalancer => "wheel balancer",
		ModToolId.SpringClamp => "spring clamp",
		ModToolId.BrakeLathe => "brake lathe",
		ModToolId.BatteryCharger => "battery charger",
		_ => "engine stand",
	};
}
