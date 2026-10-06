using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Network;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

// Hooks only mark loaders dirty; the tracker sends a change once the part state has been stable for a few polls
// and nothing is in progress, so a change made inside a game coroutine is caught when it has finished.
public static class PartChangeTracker
{
	private const float PollSeconds = 0.1f;
	private const int StablePolls = 3;
	private const float BodyScanSeconds = 0.5f;

	private static readonly HashSet<int> dirty = new HashSet<int>();
	private static readonly HashSet<int> mechanicalDirty = new HashSet<int>();
	private static readonly Dictionary<int, (string Hash, int Polls)> stability = new Dictionary<int, (string, int)>();
	private static int nextTxId = 1;
	private static float nextBodyScan;
	private static bool running;

	public static void MarkDirty(int loader)
	{
		if (ApplyingRemote.IsActive(loader)) return;
		if (!mechanicalDirty.Contains(loader)) Log.Debug($"[Parts] Loader {loader} marked dirty.");
		mechanicalDirty.Add(loader);
		dirty.Add(loader);
		if (!running)
		{
			running = true;
			MelonCoroutines.Start(Run());
		}
	}

	public static void Reset()
	{
		dirty.Clear();
		mechanicalDirty.Clear();
		stability.Clear();
	}

	private static IEnumerator Run()
	{
		while (true)
		{
			yield return new WaitForSeconds(PollSeconds);
			if (Client.Instance == null || !Client.Instance.IsConnectionValid) continue;

			if (Time.realtimeSinceStartup >= nextBodyScan)
			{
				nextBodyScan = Time.realtimeSinceStartup + BodyScanSeconds;
				foreach (var sync in CarPartsSync.All.Where(s => s.State == LoaderSyncState.Ready)) dirty.Add(sync.Loader);
			}

			foreach (int loader in dirty.ToList()) Poll(loader);
		}
	}

	private static void Poll(int loader)
	{
		var sync = CarPartsSync.Get(loader);
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (sync.State != LoaderSyncState.Ready || sync.Registry == null || carLoader == null || ApplyingRemote.IsActive(loader))
		{
			dirty.Remove(loader);
			stability.Remove(loader);
			return;
		}
		if (InProgress(carLoader))
		{
			Log.Debug($"[Parts] Loader {loader}: change in progress.");
			return;
		}

		var body = new List<CarBodyPartUpdatePacket>();
		var sub = new List<CarSubPartUpdatePacket>();
		bool mechanical = mechanicalDirty.Contains(loader);
		CarPartsSync.CaptureAll(carLoader, sync.Registry, body, mechanical ? sub : null);
		var changedBody = body.Where(r => !sync.Body.TryGetValue(r.Key, out var last) || !PartRecords.SameState(r, last)).ToList();
		var changedSub = sub.Where(r => !sync.Sub.TryGetValue(r.Key, out var last) || !PartRecords.SameState(r, last)).ToList();
		if (changedBody.Count == 0 && changedSub.Count == 0)
		{
			dirty.Remove(loader);
			mechanicalDirty.Remove(loader);
			stability.Remove(loader);
			return;
		}

		string hash = string.Join(";", changedBody.Select(r => $"{r.Key}{r.Unmounted}{r.Switched}{r.State?.Condition:F3}")
			.Concat(changedSub.Select(r => $"{r.Key}{r.Unmounted}{r.Condition:F3}{r.IsExamined}")));
		if (!stability.TryGetValue(loader, out var seen) || seen.Hash != hash)
		{
			stability[loader] = (hash, 1);
			return;
		}
		if (seen.Polls + 1 < StablePolls)
		{
			stability[loader] = (hash, seen.Polls + 1);
			return;
		}

		stability.Remove(loader);
		dirty.Remove(loader);
		mechanicalDirty.Remove(loader);
		Send(sync, changedBody, changedSub);
	}

	private static void Send(LoaderSync sync, List<CarBodyPartUpdatePacket> body, List<CarSubPartUpdatePacket> sub)
	{
		var change = new CarPartsChangePacket
		{
			CarLoaderID = sync.Loader,
			SpawnSeq = sync.SpawnSeq,
			TxId = nextTxId++,
			BodyParts = body,
			SubParts = sub
		};
		foreach (var record in body)
			if (sync.Body.TryGetValue(record.Key, out var last) && last.Unmounted != record.Unmounted)
				change.Preconditions.Add(new PartPrecondition { Key = record.Key, WasUnmounted = last.Unmounted });
		foreach (var record in sub)
			if (sync.Sub.TryGetValue(record.Key, out var last) && last.Unmounted != record.Unmounted)
				change.Preconditions.Add(new PartPrecondition { Key = record.Key, WasUnmounted = last.Unmounted });

		foreach (var record in body) sync.Body[record.Key] = record;
		foreach (var record in sub) sync.Sub[record.Key] = record;
		Client.Instance.Send(change);
		Log.Debug($"[Parts] Loader {sync.Loader}: change {change.TxId} sent ({body.Count} body, {sub.Count} mechanical, {change.Preconditions.Count} preconditions).");
	}

	private static bool InProgress(CarLoader carLoader)
	{
		var parts = carLoader.carParts;
		for (int i = 0; parts != null && i < parts.Count; i++)
			if (parts[i].TakeOnOffInProgress || parts[i].InProgress) return true;
		return false;
	}
}
