using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Car.Details;

// sync-car-details D4/D7/D9/D10 and state-merges-and-contention D4-D7: change detection per entry (1 Hz poll for values
// without previews, MarkDirty from commit hooks), the spawn snapshot after the part baseline, the queued apply of
// remote details once a car is Ready, and the own echo decided per entry.
public static class CarDetailsSync
{
	private const float PollSeconds = 1f;
	private const float FlushDelaySeconds = 0.5f;
	private const int MaxSendCopies = 16;
	private const float SendCopySeconds = 10f;

	private class SendCopy
	{
		public int Seq;
		public float At;
		public readonly Dictionary<string, string> Signatures = new Dictionary<string, string>();
		public readonly HashSet<string> ForeignSince = new HashSet<string>();
	}

	private static readonly Dictionary<int, Dictionary<string, string>> lastKnown = new Dictionary<int, Dictionary<string, string>>();
	private static readonly Dictionary<int, List<SendCopy>> sendCopies = new Dictionary<int, List<SendCopy>>();
	private static readonly Dictionary<int, CarDetailSection> dirty = new Dictionary<int, CarDetailSection>();
	private static readonly Dictionary<int, float> dirtySince = new Dictionary<int, float>();
	private static readonly HashSet<int> awaiting = new HashSet<int>();
	private static readonly HashSet<int> applying = new HashSet<int>();
	private static int nextSeq = 1;
	private static float nextPoll;
	private static bool subscribed;

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid && SyncTracker.Acked;

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		CarPartsSync.BaselineUploaded += OnBaselineUploaded;
	}

	public static void Reset()
	{
		lastKnown.Clear();
		sendCopies.Clear();
		dirty.Clear();
		dirtySince.Clear();
		awaiting.Clear();
		applying.Clear();
	}

	public static bool HoldSpawnSnapshots { get; set; }

	public static bool IsApplying(int loader) => applying.Contains(loader);

	public static bool IsDirty(int loader) => dirty.ContainsKey(loader);

	public static int KeptSends(int loader) => sendCopies.TryGetValue(loader, out var copies) ? copies.Count : 0;

	public static void OnCarLoading(int loader)
	{
		awaiting.Add(loader);
		lastKnown.Remove(loader);
		sendCopies.Remove(loader);
	}

	public static void MarkDirty(CarLoader carLoader, CarDetailSection sections)
	{
		Visuals.VisualScope.CheckLeak("CarDetailsSync.MarkDirty");
		var places = CarLoaderPlaces.Get();
		if (carLoader == null || places == null) return;
		int loader = places.GetCarLoaderId(carLoader);
		if (loader < 0 || applying.Contains(loader)) return;
		dirty[loader] = (dirty.TryGetValue(loader, out var current) ? current : CarDetailSection.None) | sections;
		dirtySince[loader] = Time.realtimeSinceStartup;
	}

	private static void OnBaselineUploaded(int loader)
	{
		if (!Active || HoldSpawnSnapshots) return;
		SendFull(loader);
	}

	public static void OnRequest(CarDetailsRequestPacket request)
	{
		if (!CarPartsSync.IsReady(request.CarLoaderID) || CarPartsSync.SpawnSeq(request.CarLoaderID) != request.SpawnSeq) return;
		SendFull(request.CarLoaderID);
	}

	private static void SendFull(int loader)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded()) return;
		var details = CarDetailsIO.Read(carLoader, CarDetailsIO.All);
		var signatures = CarDetailEntries.Signatures(details);
		lastKnown[loader] = new Dictionary<string, string>(signatures);
		awaiting.Remove(loader);
		int seq = Keep(loader, signatures);
		Log.Info($"[CarDetails] Loader {loader}: full snapshot sent.");
		Client.Instance.Send(new CarDetailsUpdatePacket { CarLoaderID = loader, SpawnSeq = CarPartsSync.SpawnSeq(loader), IsFull = true, ClientSeq = seq, Details = details });
	}

	public static void Update()
	{
		if (!Active) return;
		float now = Time.realtimeSinceStartup;
		if (now >= nextPoll)
		{
			nextPoll = now + PollSeconds;
			foreach (var sync in CarPartsSync.All.Where(s => s.State == LoaderSyncState.Ready).ToList())
				if (!awaiting.Contains(sync.Loader) && !applying.Contains(sync.Loader) && lastKnown.ContainsKey(sync.Loader) && !Away.CarAwaySync.LockedForMe(sync.Loader, out _, out _))
					MarkDirty(CarLoaderPlaces.Get().GetCarLoaderByIndex(sync.Loader), CarDetailsIO.Polled);
		}
		foreach (int loader in dirty.Keys.ToList())
		{
			if (now - dirtySince[loader] < FlushDelaySeconds) continue;
			var sections = dirty[loader];
			dirty.Remove(loader);
			Flush(loader, sections);
		}
	}

	public enum FlushResult
	{
		Sent,
		Unchanged,
		Deferred,
		NotReady
	}

	private const float FlushNowWaitSeconds = 1f;

	public static float TestApplyingUntil { get; set; }

	private static bool Busy(int loader) => awaiting.Contains(loader) || applying.Contains(loader) || Time.realtimeSinceStartup < TestApplyingUntil;

	public static FlushResult FlushNow(int loader, CarDetailSection sections, Action done = null)
	{
		if (!CarPartsSync.IsReady(loader) || !lastKnown.ContainsKey(loader))
		{
			done?.Invoke();
			return FlushResult.NotReady;
		}
		if (Busy(loader))
		{
			Log.Info($"[CarDetails] Loader {loader}: flush of {sections} deferred (awaiting or applying details).");
			MelonCoroutines.Start(FlushWhenFree(loader, sections, done));
			return FlushResult.Deferred;
		}
		var result = Flush(loader, sections) ? FlushResult.Sent : FlushResult.Unchanged;
		done?.Invoke();
		return result;
	}

	public static void FlushBeforeChange(int loader)
	{
		if (!CarPartsSync.IsReady(loader) || !lastKnown.ContainsKey(loader)) return;
		if (Busy(loader)) Log.Info($"[CarDetails] Loader {loader}: fluids flushed ahead of a part change although details are being applied.");
		Flush(loader, CarDetailSection.Fluids, force: true);
	}

	private static IEnumerator FlushWhenFree(int loader, CarDetailSection sections, Action done)
	{
		float deadline = Time.realtimeSinceStartup + FlushNowWaitSeconds;
		while (Busy(loader) && Time.realtimeSinceStartup < deadline) yield return null;
		bool forced = Busy(loader);
		bool sent = Flush(loader, sections, force: true);
		Log.Info($"[CarDetails] Loader {loader}: deferred flush of {sections} {(sent ? "sent" : "had no change")}{(forced ? " after 1 s although still busy" : "")}.");
		done?.Invoke();
	}

	private static bool Flush(int loader, CarDetailSection sections, bool force = false)
	{
		if (!force && Busy(loader) || !CarPartsSync.IsReady(loader) || !lastKnown.TryGetValue(loader, out var known)) return false;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded()) return false;
		var details = CarDetailsIO.Read(carLoader, sections);
		var signatures = CarDetailEntries.Signatures(details);
		var changed = new HashSet<string>(signatures.Where(p => !known.TryGetValue(p.Key, out string last) || last != p.Value).Select(p => p.Key));
		if (changed.Count == 0) return false;
		var (send, wheelMask, alignmentMask) = DetailsMerge.Only(details, DetailsMerge.AllWheels, DetailsMerge.AllAlignment, changed);
		var sent = signatures.Where(p => changed.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
		foreach (var pair in sent) known[pair.Key] = pair.Value;
		int seq = Keep(loader, sent);
		Log.Debug($"[CarDetails] Loader {loader}: update {seq} sent with {changed.Count} entries ({string.Join(", ", changed.OrderBy(e => e, StringComparer.Ordinal).Take(8))}).");
		Client.Instance.Send(new CarDetailsUpdatePacket
		{
			CarLoaderID = loader, SpawnSeq = CarPartsSync.SpawnSeq(loader), ClientSeq = seq, Details = send, WheelMask = wheelMask, AlignmentMask = alignmentMask
		});
		dirty.Remove(loader);
		return true;
	}

	private static int Keep(int loader, IDictionary<string, string> signatures)
	{
		int seq = nextSeq++;
		if (!sendCopies.TryGetValue(loader, out var copies)) sendCopies[loader] = copies = new List<SendCopy>();
		var copy = new SendCopy { Seq = seq, At = Time.realtimeSinceStartup };
		foreach (var pair in signatures) copy.Signatures[pair.Key] = pair.Value;
		copies.Add(copy);
		Prune(copies);
		return seq;
	}

	private static void Prune(List<SendCopy> copies)
	{
		float now = Time.realtimeSinceStartup;
		copies.RemoveAll(c => now - c.At > SendCopySeconds);
		while (copies.Count > MaxSendCopies) copies.RemoveAt(0);
	}

	public static void OnUpdate(CarDetailsUpdatePacket packet, int snapshotId) => MelonCoroutines.Start(ApplyWhenReady(packet, snapshotId));

	private static IEnumerator ApplyWhenReady(CarDetailsUpdatePacket packet, int snapshotId)
	{
		int loader = packet.CarLoaderID;
		float deadline = Time.realtimeSinceStartup + 90f;
		while (Time.realtimeSinceStartup < deadline && (!CarPartsSync.IsReady(loader) || CarPartsSync.SpawnSeq(loader) != packet.SpawnSeq))
		{
			if (CarPartsSync.SpawnSeq(loader) > packet.SpawnSeq) break;
			yield return new WaitForSeconds(0.25f);
		}
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader != null && carLoader.IsCarLoaded() && CarPartsSync.SpawnSeq(loader) == packet.SpawnSeq)
			Apply(loader, carLoader, packet);
		Count(packet, snapshotId);
	}

	private static void Apply(int loader, CarLoader carLoader, CarDetailsUpdatePacket packet)
	{
		var details = packet.Details;
		int wheelMask = packet.WheelMask;
		var alignmentMask = packet.AlignmentMask;
		var carried = DetailsMerge.CarriedSignatures(details, wheelMask, alignmentMask);
		bool own = Client.Instance != null && packet.SourceClientId == Client.Instance.ID;
		if (own)
		{
			var copy = TakeCopy(loader, packet.ClientSeq);
			if (copy != null)
			{
				var apply = new HashSet<string>(carried.Where(p => copy.ForeignSince.Contains(p.Key) || !copy.Signatures.TryGetValue(p.Key, out string sent) || sent != p.Value).Select(p => p.Key));
				if (apply.Count == 0)
				{
					if (packet.IsFull) awaiting.Remove(loader);
					return;
				}
				Log.Debug($"[CarDetails] Loader {loader}: own update {packet.ClientSeq} came back with {apply.Count} entries the server changed ({string.Join(", ", apply.Take(8))}).");
				(details, wheelMask, alignmentMask) = DetailsMerge.Only(details, wheelMask, alignmentMask, apply);
				carried = DetailsMerge.CarriedSignatures(details, wheelMask, alignmentMask);
			}
		}
		else if (sendCopies.TryGetValue(loader, out var copies))
		{
			foreach (var copy in copies)
				foreach (string entry in carried.Keys)
					if (copy.Signatures.ContainsKey(entry)) copy.ForeignSince.Add(entry);
		}

		applying.Add(loader);
		try { CarDetailsIO.Apply(carLoader, details, wheelMask, alignmentMask); }
		finally { applying.Remove(loader); }
		var now = CarDetailEntries.Signatures(CarDetailsIO.Read(carLoader, Present(details)));
		if (!lastKnown.TryGetValue(loader, out var known)) lastKnown[loader] = known = new Dictionary<string, string>();
		foreach (string entry in carried.Keys)
			if (now.TryGetValue(entry, out string signature)) known[entry] = signature;
		if (packet.IsFull) awaiting.Remove(loader);
		if (packet.IsFull) Log.Info($"[CarDetails] Loader {loader}: details applied ({Present(details)}).");
	}

	private static SendCopy TakeCopy(int loader, int seq)
	{
		if (!sendCopies.TryGetValue(loader, out var copies)) return null;
		Prune(copies);
		var copy = copies.FirstOrDefault(c => c.Seq == seq);
		if (copy != null) copies.Remove(copy);
		return copy;
	}

	private static void Count(CarDetailsUpdatePacket packet, int snapshotId)
	{
		if (packet.SourceClientId == -1) SyncTracker.Applied(SyncOrder.CarDetailsKey, snapshotId);
	}

	private static CarDetailSection Present(ModCarDetails details)
	{
		var sections = CarDetailSection.None;
		if (details.Fluids != null) sections |= CarDetailSection.Fluids;
		if (details.Wheels != null) sections |= CarDetailSection.Wheels;
		if (details.Alignment != null) sections |= CarDetailSection.Alignment;
		if (details.Tuning != null) sections |= CarDetailSection.Tuning;
		if (details.Paint != null) sections |= CarDetailSection.Paint;
		if (details.BodyCosmetics != null) sections |= CarDetailSection.BodyCosmetics;
		if (details.Plates != null) sections |= CarDetailSection.Plates;
		if (details.Info != null) sections |= CarDetailSection.Info;
		if (details.Dyno != null) sections |= CarDetailSection.Dyno;
		return sections;
	}

	public static string Signature(ModCarDetails details, CarDetailSection section)
	{
		object value = section switch
		{
			CarDetailSection.Fluids => details.Fluids,
			CarDetailSection.Wheels => details.Wheels,
			CarDetailSection.Alignment => details.Alignment,
			CarDetailSection.Tuning => details.Tuning,
			CarDetailSection.Paint => details.Paint,
			CarDetailSection.BodyCosmetics => details.BodyCosmetics,
			CarDetailSection.Plates => details.Plates,
			CarDetailSection.Info => details.Info,
			CarDetailSection.Dyno => details.Dyno,
			_ => null,
		};
		return CarDetailEntries.Signature(value);
	}
}
