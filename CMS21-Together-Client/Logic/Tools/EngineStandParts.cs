using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Tools;

// sync-workshop-machines D7: part work on the engine on a stand goes through sync-car-parts' PartTransactions under a
// pseudo loader id per stand, and the server checks it against the stand's part records like a CarPartsChange.
public static class EngineStandParts
{
	private const float PollSeconds = 0.1f;
	private const int StablePolls = 3;
	private const int FirstTxId = 1_000_000;

	private static readonly HashSet<ModToolId> dirty = new HashSet<ModToolId>();
	private static readonly Dictionary<ModToolId, (string Hash, int Polls)> stability = new Dictionary<ModToolId, (string, int)>();
	private static int nextTxId = FirstTxId;
	private static bool running;

	public static int PseudoLoader(ModToolId tool) => -100 - (int)tool;

	public static void Reset()
	{
		dirty.Clear();
		stability.Clear();
	}

	public static bool MarkPart(PartScript script)
	{
		foreach (var machine in ToolSync.Machines)
		{
			if (!(machine is EngineStandSync stand) || stand.Registry == null || !stand.Registry.TryGetSubPath(script, out _)) continue;
			if (ToolSync.IsApplyingRemote(stand.Tool)) return true;
			PartTransactions.OpenForPart(PseudoLoader(stand.Tool), stand.Registry, script);
			dirty.Add(stand.Tool);
			if (!running)
			{
				running = true;
				MelonCoroutines.Start(Run());
			}
			return true;
		}
		return false;
	}

	private static IEnumerator Run()
	{
		while (dirty.Count > 0)
		{
			yield return new WaitForSeconds(PollSeconds);
			foreach (var tool in dirty.ToList()) Poll((EngineStandSync)ToolSync.Machine(tool));
			PartTransactions.FlushIdle();
		}
		running = false;
	}

	private static void Poll(EngineStandSync stand)
	{
		if (stand.Registry == null || !ToolSync.CanSend || ToolSync.Mirror(stand.Tool).Uid != stand.AppliedUid)
		{
			dirty.Remove(stand.Tool);
			stability.Remove(stand.Tool);
			return;
		}
		var changed = new List<CarSubPartUpdatePacket>();
		foreach (string key in stand.Registry.SubKeys)
		{
			var record = PartRecords.Capture(stand.Registry.SubPath(key), stand.Registry.Sub(key));
			if (!stand.Known.TryGetValue(key, out var last) || !PartRecords.SameState(record, last)) changed.Add(record);
		}
		if (changed.Count == 0)
		{
			dirty.Remove(stand.Tool);
			stability.Remove(stand.Tool);
			return;
		}
		string hash = string.Join(";", changed.Select(r => $"{r.Key}{r.Unmounted}{r.Condition:F3}{r.IsExamined}"));
		if (!stability.TryGetValue(stand.Tool, out var seen) || seen.Hash != hash)
		{
			stability[stand.Tool] = (hash, 1);
			return;
		}
		if (seen.Polls + 1 < StablePolls)
		{
			stability[stand.Tool] = (hash, seen.Polls + 1);
			return;
		}
		stability.Remove(stand.Tool);
		dirty.Remove(stand.Tool);
		Send(stand, changed);
	}

	private static void Send(EngineStandSync stand, List<CarSubPartUpdatePacket> changed)
	{
		var change = new ToolPartChangePacket { Tool = stand.Tool, EngineUid = stand.AppliedUid, TxId = nextTxId++, SubParts = changed };
		foreach (var record in changed)
		{
			stand.Known.TryGetValue(record.Key, out var last);
			record.Changed = PartMasks.For(record, last);
			if (last != null && last.Unmounted != record.Unmounted)
				change.Preconditions.Add(new PartPrecondition { Key = record.Key, WasUnmounted = last.Unmounted });
		}
		change.Delta = PartTransactions.TakeFor(PseudoLoader(stand.Tool), changed.Select(r => r.Key), change.TxId);
		var mirror = ToolSync.Mirror(stand.Tool);
		foreach (var record in changed)
		{
			stand.Known[record.Key] = record;
			mirror.Parts[record.Key] = record;
		}
		Client.Instance.Send(change);
		Log.Info($"[Tools] {stand.Tool}: part change {change.TxId} sent ({changed.Count} parts, {change.Preconditions.Count} preconditions; Changed = {PartMasks.Describe(new CarBodyPartUpdatePacket[0], changed)}).");
	}

	public static void OnRemoteChange(ToolPartChangePacket change)
	{
		var stand = ToolSync.Machine(change.Tool) as EngineStandSync;
		if (stand == null) return;
		PartTransactions.AbortFor(PseudoLoader(change.Tool), PartChanges.AbortKeys(new CarBodyPartUpdatePacket[0], change.SubParts));
		PartChanges.ApplyInventory(change.Delta);
		var mirror = ToolSync.Mirror(change.Tool);
		if (mirror.Uid != change.EngineUid) return;
		foreach (var record in change.SubParts) mirror.Parts[record.Key] = Merge(mirror.Parts, record);
		if (stand.AppliedUid == change.EngineUid) ApplyRecords(stand, change.SubParts);
	}

	public static void OnResult(ToolPartChangeResultPacket result)
	{
		PartTransactions.OnResult(new CarPartsChangeResultPacket { TxId = result.TxId, Accepted = result.Accepted, RestoreUids = result.RestoreUids });
		if (result.Accepted && result.SubParts.Count == 0) return;
		if (!result.Accepted) Log.Warn($"[Tools] {result.Tool}: part change {result.TxId} rejected ({result.Reason}); restoring the server's state.");
		var stand = ToolSync.Machine(result.Tool) as EngineStandSync;
		var mirror = ToolSync.Mirror(result.Tool);
		foreach (var record in result.SubParts) mirror.Parts[record.Key] = Merge(mirror.Parts, record);
		if (stand != null && stand.AppliedUid == result.EngineUid) ApplyRecords(stand, result.SubParts);
	}

	private static CarSubPartUpdatePacket Merge(Dictionary<string, CarSubPartUpdatePacket> known, CarSubPartUpdatePacket record) =>
		PartRecordMerge.WithGroups(known.TryGetValue(record.Key, out var last) ? last : null, record, record.Changed == PartFields.None ? PartFields.All : record.Changed);

	public static void ApplyRecords(EngineStandSync stand, IEnumerable<CarSubPartUpdatePacket> records)
	{
		if (stand.Registry == null) return;
		using (ToolSync.ApplyingRemote(stand.Tool))
		{
			foreach (var record in records)
			{
				var groups = record.Changed == PartFields.None ? PartFields.All : record.Changed;
				if (groups.HasFlag(PartFields.All) && stand.Known.TryGetValue(record.Key, out var local) && PartRecords.SameState(record, local)) continue;
				if (PartApplier.Apply(null, stand.Registry, record, groups)) stand.Known[record.Key] = Merge(stand.Known, record);
			}
		}
	}
}
