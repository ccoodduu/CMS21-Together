using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Visuals;
using CMS21Together.Network.Handlers;

namespace CMS21Together.Logic.Car.Parts;

public static class PartChanges
{
	private static readonly List<CarPartsChangePacket> heldForTest = new List<CarPartsChangePacket>();
	private static bool releasingHeld;

	// Live changes of other players only: snapshots, resyncs, released test holds and own results never animate.
	public static event Action<int, List<CarBodyPartUpdatePacket>, List<CarSubPartUpdatePacket>> RemoteChangeApplying;
	public static event Action<int, List<CarBodyPartUpdatePacket>, List<CarSubPartUpdatePacket>> RemoteChangeApplied;

	public static bool TestHoldRemote { get; set; }

	public static void TestReleaseRemote()
	{
		TestHoldRemote = false;
		var held = heldForTest.ToList();
		heldForTest.Clear();
		releasingHeld = true;
		try
		{
			foreach (var change in held) OnRemoteChange(change);
		}
		finally
		{
			releasingHeld = false;
		}
	}

	public static void OnRemoteChange(CarPartsChangePacket change)
	{
		if (TestHoldRemote)
		{
			heldForTest.Add(change);
			return;
		}
		var sync = CarPartsSync.Get(change.CarLoaderID);
		bool sameCar = sync.SpawnSeq == 0 || change.SpawnSeq == sync.SpawnSeq;
		if (sameCar && !(sync.State == LoaderSyncState.Ready && change.Revision <= sync.Revision))
			PartTransactions.AbortFor(change.CarLoaderID, AbortKeys(change.BodyParts, change.SubParts));
		ApplyInventory(change.InventoryDelta);
		change.InventoryDelta = null;
		if (!sameCar) return;
		if (sync.State != LoaderSyncState.Ready)
		{
			sync.Pending.Enqueue(() => OnRemoteChange(change));
			return;
		}
		if (change.Revision <= sync.Revision) return;

		Raise(RemoteChangeApplying, change);
		Apply(sync, change.BodyParts, change.SubParts, abortLocal: false);
		sync.Revision = change.Revision;
		Raise(RemoteChangeApplied, change);
	}

	public static void OnResult(CarPartsChangeResultPacket result)
	{
		VisualScope.CancelFor(result.CarLoaderID, result.BodyParts.Select(r => r.Key).Concat(result.SubParts.Select(r => r.Key)), "own result");
		PartTransactions.OnResult(result);
		var sync = CarPartsSync.Get(result.CarLoaderID);
		if (result.SpawnSeq != sync.SpawnSeq) return;
		if (result.Accepted)
		{
			if (result.Revision > sync.Revision) sync.Revision = result.Revision;
			if (result.BodyParts.Count > 0 || result.SubParts.Count > 0) Apply(sync, result.BodyParts, result.SubParts, abortLocal: false);
			CarPartsSync.RaiseLocalPartsCommitted(result.CarLoaderID, PartChangeTracker.TakeSentKeys(result.TxId));
			return;
		}

		Log.Warn($"[Parts] Loader {result.CarLoaderID}: change {result.TxId} rejected ({result.Reason}); restoring the server's state.");
		Apply(sync, result.BodyParts, result.SubParts);
		if (result.Revision > sync.Revision) sync.Revision = result.Revision;
	}

	private static void Raise(Action<int, List<CarBodyPartUpdatePacket>, List<CarSubPartUpdatePacket>> handler, CarPartsChangePacket change)
	{
		if (handler == null || releasingHeld) return;
		try
		{
			handler(change.CarLoaderID, change.BodyParts, change.SubParts);
		}
		catch (Exception e)
		{
			Log.Error($"[Visuals] Part change visual failed on loader {change.CarLoaderID}: {e.Message}");
		}
	}

	public static void ApplyInventory(InventoryDelta delta)
	{
		if (delta == null || delta.IsEmpty) return;
		if (InventoryHandlers.HoldUntilReady(() => ApplyInventory(delta))) return;
		var inventory = Singleton<GameManager>.Instance.Inventory;
		bool previous = InventoryHandlers.IgnoreInventoryHooks;
		InventoryHandlers.IgnoreInventoryHooks = true;
		try
		{
			foreach (long uid in delta.RemovedItemUids)
			{
				var item = inventory.GetItem(uid);
				if (item != null) inventory.Delete(item);
			}
			foreach (long uid in delta.RemovedGroupUids) inventory.DeleteGroup(uid);
			foreach (var item in delta.AddedItems)
				if (inventory.GetItem(item.UID) == null) inventory.Add(item.ToGameItem());
			foreach (var group in delta.AddedGroups)
				if (inventory.GetGroup(group.UID) == null) inventory.AddGroup(group.ToGameGroupItem());
		}
		finally
		{
			InventoryHandlers.IgnoreInventoryHooks = previous;
		}
		InventoryHandlers.RefreshInventoryWindow();
	}

	private const PartFields AbortGroups = PartFields.Mount | PartFields.Identity | PartFields.Switched | PartFields.All;

	private static PartFields Groups(PartFields changed) => changed == PartFields.None ? PartFields.All : changed;

	public static IEnumerable<string> AbortKeys(IEnumerable<CarBodyPartUpdatePacket> body, IEnumerable<CarSubPartUpdatePacket> sub) =>
		body.Where(r => (Groups(r.Changed) & AbortGroups) != 0).Select(r => r.Key)
			.Concat(sub.Where(r => (Groups(r.Changed) & AbortGroups) != 0).Select(r => r.Key)).ToList();

	private static void Apply(LoaderSync sync, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub, bool abortLocal = true)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(sync.Loader);
		if (carLoader == null || sync.Registry == null) return;
		if (abortLocal) PartTransactions.AbortFor(sync.Loader, AbortKeys(body, sub));

		int failed = 0;
		using (ApplyingRemote.Scope(sync.Loader))
		{
			foreach (var record in body)
			{
				var groups = Groups(record.Changed);
				if (PartApplier.Apply(carLoader, sync.Registry, record, groups))
					sync.Body[record.Key] = PartRecordMerge.WithGroups(sync.Body.TryGetValue(record.Key, out var known) ? known : null, record, groups);
				else failed++;
			}
			foreach (var record in sub)
			{
				var groups = Groups(record.Changed);
				if (PartApplier.Apply(carLoader, sync.Registry, record, groups))
					sync.Sub[record.Key] = PartRecordMerge.WithGroups(sync.Sub.TryGetValue(record.Key, out var known) ? known : null, record, groups);
				else failed++;
			}
		}
		if (failed > 0) CarPartsSync.RequestResync(sync.Loader, $"{failed} records did not resolve");
	}
}
