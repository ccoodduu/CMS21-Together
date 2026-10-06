using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Hook;
using CMS21Together.Network;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

public enum LoaderSyncState
{
	Empty,
	Loading,
	AwaitingBaseline,
	Ready
}

public class LoaderSync
{
	public int Loader;
	public LoaderSyncState State;
	public int SpawnSeq;
	public int Revision;
	public string CarToLoad;
	public PartRegistry Registry;
	public readonly Dictionary<string, CarBodyPartUpdatePacket> Body = new Dictionary<string, CarBodyPartUpdatePacket>();
	public readonly Dictionary<string, CarSubPartUpdatePacket> Sub = new Dictionary<string, CarSubPartUpdatePacket>();
	public readonly Queue<Action> Pending = new Queue<Action>();
}

public static class CarPartsSync
{
	private const float BaselineSettleSeconds = 1f;
	private const float LoadTimeoutSeconds = 90f;
	private const int BatchSize = 100;

	private static readonly Dictionary<int, LoaderSync> loaders = new Dictionary<int, LoaderSync>();
	private static readonly Dictionary<string, List<CarPartsSnapshotPacket>> incoming = new Dictionary<string, List<CarPartsSnapshotPacket>>();

	public static float TestSnapshotDelaySeconds { get; set; }

	public static event Action<int> BaselineUploaded;
	public static event Action<int, IReadOnlyCollection<string>> LocalPartsCommitted;

	public static void MarkDirty(int loader, PartScript script) => PartChangeTracker.MarkDirty(loader);

	public static void MarkDirty(int loader, CarPart part) => PartChangeTracker.MarkDirty(loader);

	public static void RebuildRegistry(int loader)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader != null && carLoader.IsCarLoaded()) Get(loader).Registry = PartRegistry.Build(carLoader);
	}

	internal static void RaiseLocalPartsCommitted(int loader, IReadOnlyCollection<string> keys) => LocalPartsCommitted?.Invoke(loader, keys);

	public static IEnumerable<LoaderSync> All => loaders.Values;

	public static void Reset()
	{
		loaders.Clear();
		incoming.Clear();
	}

	public static LoaderSync Get(int loader)
	{
		if (!loaders.TryGetValue(loader, out var sync))
		{
			sync = new LoaderSync { Loader = loader };
			loaders[loader] = sync;
		}
		return sync;
	}

	public static bool IsReady(int loader) => loaders.TryGetValue(loader, out var sync) && sync.State == LoaderSyncState.Ready;

	public static int SpawnSeq(int loader) => loaders.TryGetValue(loader, out var sync) ? sync.SpawnSeq : 0;

	public static void OnSpawnAck(CarSpawnAckPacket packet)
	{
		var sync = Get(packet.CarLoaderID);
		sync.SpawnSeq = packet.SpawnSeq;
		sync.State = LoaderSyncState.Loading;
		sync.Registry = null;
		MelonCoroutines.Start(UploadWhenSettled(sync, packet.SpawnSeq));
	}

	public static void OnRemoteSpawn(CarSpawnResponsePacket spawn)
	{
		var sync = Get(spawn.CarLoaderID);
		sync.SpawnSeq = spawn.SpawnSeq;
		sync.CarToLoad = spawn.CarToLoad;
		sync.Registry = null;
		sync.State = LoaderSyncState.Loading;
	}

	public static void OnCarDeleted(int loader)
	{
		loaders.Remove(loader);
	}

	private static IEnumerator UploadWhenSettled(LoaderSync sync, int spawnSeq)
	{
		var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(sync.Loader);
		float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
		while (carLoader != null && !carLoader.IsCarLoaded())
		{
			if (Time.realtimeSinceStartup > deadline || sync.SpawnSeq != spawnSeq) yield break;
			yield return null;
		}
		yield return new WaitForSeconds(BaselineSettleSeconds);
		if (sync.SpawnSeq != spawnSeq || carLoader == null) yield break;

		sync.CarToLoad = carLoader.carToLoad;
		UploadBaseline(sync.Loader);
	}

	public static void UploadBaseline(int loader)
	{
		var sync = Get(loader);
		var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded()) return;

		sync.Registry = PartRegistry.Build(carLoader);
		var body = new List<CarBodyPartUpdatePacket>();
		var sub = new List<CarSubPartUpdatePacket>();
		CaptureAll(carLoader, sync.Registry, body, sub);

		SendSnapshot(loader, sync.SpawnSeq, carLoader.EngineParams?.EngineSwap, body, sub);
		Remember(sync, body, sub);
		sync.State = LoaderSyncState.Ready;
		Log.Info($"[Parts] Loader {loader}: baseline sent ({body.Count} body, {sub.Count} mechanical, SpawnSeq {sync.SpawnSeq}).");
		BaselineUploaded?.Invoke(loader);
		Drain(sync);
	}

	public static void OnSnapshot(CarPartsSnapshotPacket packet, int receivingSnapshotId)
	{
		string key = $"{packet.CarLoaderID}/{packet.SpawnSeq}/{packet.SnapshotId}";
		if (!incoming.TryGetValue(key, out var batches))
		{
			batches = new List<CarPartsSnapshotPacket>();
			incoming[key] = batches;
		}
		batches.Add(packet);
		SyncTracker.MarkProgress();
		if (!packet.IsLastBatch) return;

		incoming.Remove(key);
		int countedSnapshot = packet.SnapshotId == CarPartsSnapshotPacket.LiveSnapshot ? SyncTracker.NoSnapshot : receivingSnapshotId;
		MelonCoroutines.Start(ApplySnapshot(batches.OrderBy(b => b.BatchIndex).ToList(), countedSnapshot));
	}

	private static IEnumerator ApplySnapshot(List<CarPartsSnapshotPacket> batches, int countedSnapshot)
	{
		var first = batches[0];
		var spawn = first.Spawn;
		int loader = first.CarLoaderID;

		while (!ClientData.IsInventorySynced || !ClientData.IsGarageStateSynced)
			yield return new WaitForSeconds(0.25f);
		yield return new WaitForEndOfFrame();

		var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(loader);
		if (carLoader == null)
		{
			Log.Error($"[Parts] Loader {loader} not found, dropping snapshot.");
			yield break;
		}

		var sync = Get(loader);
		bool needsLoad = carLoader.carToLoad != spawn.CarToLoad || sync.SpawnSeq != first.SpawnSeq && sync.State != LoaderSyncState.Loading;
		sync.SpawnSeq = first.SpawnSeq;
		sync.CarToLoad = spawn.CarToLoad;

		if (needsLoad)
		{
			sync.State = LoaderSyncState.Loading;
			carLoader.placeNo = spawn.PlaceNo;
			carLoader.ConfigVersion = spawn.ConfigVersion;
			carLoader.customerCar = spawn.IsJob;
			CarSpawnHooks.Suppress(loader);
			try
			{
				carLoader.StartCoroutine(carLoader.LoadCar(spawn.CarToLoad));
				while (!carLoader.IsCarLoaded()) yield return new WaitForEndOfFrame();
				carLoader.PlaceAtPosition(true, true);
			}
			finally
			{
				CarSpawnHooks.Release(loader);
			}
		}
		else
		{
			float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
			while (!carLoader.IsCarLoaded() && Time.realtimeSinceStartup < deadline) yield return new WaitForEndOfFrame();
		}
		if (TestSnapshotDelaySeconds > 0f) yield return new WaitForSeconds(TestSnapshotDelaySeconds);
		if (sync.SpawnSeq != first.SpawnSeq) yield break;

		sync.Registry = PartRegistry.Build(carLoader);
		int failed = 0;
		var body = batches.SelectMany(b => b.BodyParts).ToList();
		var sub = batches.SelectMany(b => b.SubParts).ToList();
		using (ApplyingRemote.Scope(loader))
		{
			foreach (var record in body) if (!PartApplier.Apply(carLoader, sync.Registry, record)) failed++;
			foreach (var record in sub) if (!PartApplier.Apply(carLoader, sync.Registry, record)) failed++;
		}
		Remember(sync, body, sub);
		sync.Revision = first.Revision;
		sync.State = LoaderSyncState.Ready;
		Log.Info($"[Parts] Loader {loader}: {spawn.CarToLoad} ready from snapshot (revision {first.Revision}, {body.Count} body, {sub.Count} mechanical, {failed} unresolved).");

		if (countedSnapshot != SyncTracker.NoSnapshot) SyncTracker.Applied(SyncOrder.CarsKey, countedSnapshot);
		Drain(sync);
	}

	private const float ResyncCooldownSeconds = 10f;
	private static readonly Dictionary<int, float> lastResync = new Dictionary<int, float>();

	public static void RequestResync(int loader, string reason)
	{
		if (lastResync.TryGetValue(loader, out float at) && Time.realtimeSinceStartup - at < ResyncCooldownSeconds) return;
		lastResync[loader] = Time.realtimeSinceStartup;
		Log.Warn($"[Parts] Loader {loader}: asking the server for a resync ({reason}).");
		Client.Instance.Send(new CarPartsResyncRequestPacket { CarLoaderID = loader, SpawnSeq = SpawnSeq(loader), Reason = reason });
	}

	public static void CaptureAll(CarLoader carLoader, PartRegistry registry, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		var parts = carLoader.carParts;
		for (int i = 0; parts != null && i < parts.Count; i++)
			body.Add(PartRecords.Capture(i, parts[i]));
		if (sub == null) return;
		foreach (string key in registry.SubKeys)
			sub.Add(PartRecords.Capture(registry.SubPath(key), registry.Sub(key)));
	}

	private static void SendSnapshot(int loader, int spawnSeq, string engineSwap, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		var records = body.Cast<object>().Concat(sub).ToList();
		int batchCount = Math.Max(1, (records.Count + BatchSize - 1) / BatchSize);
		for (int i = 0; i < batchCount; i++)
		{
			var slice = records.Skip(i * BatchSize).Take(BatchSize).ToList();
			Client.Instance.Send(new CarPartsSnapshotPacket
			{
				SnapshotId = CarPartsSnapshotPacket.LiveSnapshot,
				CarLoaderID = loader,
				SpawnSeq = spawnSeq,
				BatchIndex = i,
				IsLastBatch = i == batchCount - 1,
				EngineSwap = engineSwap,
				BodyParts = slice.OfType<CarBodyPartUpdatePacket>().ToList(),
				SubParts = slice.OfType<CarSubPartUpdatePacket>().ToList()
			});
		}
	}

	private static void Remember(LoaderSync sync, IEnumerable<CarBodyPartUpdatePacket> body, IEnumerable<CarSubPartUpdatePacket> sub)
	{
		sync.Body.Clear();
		sync.Sub.Clear();
		foreach (var record in body) sync.Body[record.Key] = record;
		foreach (var record in sub) sync.Sub[record.Key] = record;
	}

	private static void Drain(LoaderSync sync)
	{
		while (sync.State == LoaderSyncState.Ready && sync.Pending.Count > 0)
			sync.Pending.Dequeue()();
	}
}
