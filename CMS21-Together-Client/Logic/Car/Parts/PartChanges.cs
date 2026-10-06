using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network.Handlers;

namespace CMS21Together.Logic.Car.Parts;

public static class PartChanges
{
	private static readonly List<CarPartsChangePacket> heldForTest = new List<CarPartsChangePacket>();

	public static bool TestHoldRemote { get; set; }

	public static void TestReleaseRemote()
	{
		TestHoldRemote = false;
		var held = heldForTest.ToList();
		heldForTest.Clear();
		foreach (var change in held) OnRemoteChange(change);
	}

	public static void OnRemoteChange(CarPartsChangePacket change)
	{
		if (TestHoldRemote)
		{
			heldForTest.Add(change);
			return;
		}
		PartTransactions.AbortFor(change.CarLoaderID, change.BodyParts.Select(r => r.Key).Concat(change.SubParts.Select(r => r.Key)));
		ApplyInventory(change.InventoryDelta);
		change.InventoryDelta = null;
		var sync = CarPartsSync.Get(change.CarLoaderID);
		if (sync.SpawnSeq != 0 && change.SpawnSeq != sync.SpawnSeq) return;
		if (sync.State != LoaderSyncState.Ready)
		{
			sync.Pending.Enqueue(() => OnRemoteChange(change));
			return;
		}
		if (change.Revision <= sync.Revision) return;

		Apply(sync, change.BodyParts, change.SubParts);
		sync.Revision = change.Revision;
	}

	public static void OnResult(CarPartsChangeResultPacket result)
	{
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

	private static void ApplyInventory(InventoryDelta delta)
	{
		if (delta == null || delta.IsEmpty) return;
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

	private static void Apply(LoaderSync sync, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub, bool abortLocal = true)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(sync.Loader);
		if (carLoader == null || sync.Registry == null) return;
		if (abortLocal) PartTransactions.AbortFor(sync.Loader, body.Select(r => r.Key).Concat(sub.Select(r => r.Key)));

		int failed = 0;
		using (ApplyingRemote.Scope(sync.Loader))
		{
			foreach (var record in body)
				if (PartApplier.Apply(carLoader, sync.Registry, record)) sync.Body[record.Key] = record;
				else failed++;
			foreach (var record in sub)
				if (PartApplier.Apply(carLoader, sync.Registry, record)) sync.Sub[record.Key] = record;
				else failed++;
		}
		if (failed > 0) CarPartsSync.RequestResync(sync.Loader, $"{failed} records did not resolve");
	}
}
