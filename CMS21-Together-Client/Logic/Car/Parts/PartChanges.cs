using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Car.Parts;

public static class PartChanges
{
	public static void OnRemoteChange(CarPartsChangePacket change)
	{
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
		var sync = CarPartsSync.Get(result.CarLoaderID);
		if (result.SpawnSeq != sync.SpawnSeq) return;
		if (result.Accepted)
		{
			if (result.Revision > sync.Revision) sync.Revision = result.Revision;
			return;
		}

		Log.Warn($"[Parts] Loader {result.CarLoaderID}: change {result.TxId} rejected ({result.Reason}); restoring the server's state.");
		Apply(sync, result.BodyParts, result.SubParts);
		if (result.Revision > sync.Revision) sync.Revision = result.Revision;
	}

	private static void Apply(LoaderSync sync, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(sync.Loader);
		if (carLoader == null || sync.Registry == null) return;

		using (ApplyingRemote.Scope(sync.Loader))
		{
			foreach (var record in body)
				if (PartApplier.Apply(carLoader, sync.Registry, record)) sync.Body[record.Key] = record;
			foreach (var record in sub)
				if (PartApplier.Apply(carLoader, sync.Registry, record)) sync.Sub[record.Key] = record;
		}
	}
}
