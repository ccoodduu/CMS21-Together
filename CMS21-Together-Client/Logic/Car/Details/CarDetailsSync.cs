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
using Newtonsoft.Json;
using UnityEngine;

namespace CMS21Together.Logic.Car.Details;

// sync-car-details D4/D7/D9/D10: change detection (1 Hz poll for values without previews, MarkDirty from commit hooks),
// the spawn snapshot after the part baseline, and the queued apply of remote details once a car is Ready.
public static class CarDetailsSync
{
	private const float PollSeconds = 1f;
	private const float FlushDelaySeconds = 0.5f;

	private static readonly Dictionary<int, Dictionary<CarDetailSection, string>> lastKnown = new Dictionary<int, Dictionary<CarDetailSection, string>>();
	private static readonly Dictionary<int, CarDetailSection> dirty = new Dictionary<int, CarDetailSection>();
	private static readonly Dictionary<int, float> dirtySince = new Dictionary<int, float>();
	private static readonly HashSet<int> awaiting = new HashSet<int>();
	private static readonly HashSet<int> applying = new HashSet<int>();
	private static readonly Dictionary<int, int> latestSeq = new Dictionary<int, int>();
	private static int nextSeq = 1;
	private static float nextPoll;
	private static bool subscribed;

	private static readonly CarDetailSection[] Sections =
	{
		CarDetailSection.Fluids, CarDetailSection.Wheels, CarDetailSection.Alignment, CarDetailSection.Tuning, CarDetailSection.Paint,
		CarDetailSection.BodyCosmetics, CarDetailSection.Plates, CarDetailSection.Info, CarDetailSection.Dyno,
	};

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
		dirty.Clear();
		dirtySince.Clear();
		awaiting.Clear();
		applying.Clear();
		latestSeq.Clear();
	}

	public static bool HoldSpawnSnapshots { get; set; }

	public static bool IsApplying(int loader) => applying.Contains(loader);

	public static bool IsDirty(int loader) => dirty.ContainsKey(loader);

	public static void OnCarLoading(int loader)
	{
		awaiting.Add(loader);
		lastKnown.Remove(loader);
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
		Remember(loader, details, CarDetailsIO.All);
		awaiting.Remove(loader);
		int seq = nextSeq++;
		latestSeq[loader] = seq;
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

	private static void Flush(int loader, CarDetailSection sections)
	{
		if (awaiting.Contains(loader) || applying.Contains(loader) || !CarPartsSync.IsReady(loader) || !lastKnown.ContainsKey(loader)) return;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded()) return;
		var details = CarDetailsIO.Read(carLoader, sections);
		var changed = CarDetailSection.None;
		foreach (var section in Sections)
			if (sections.HasFlag(section) && Signature(details, section) != Known(loader, section)) changed |= section;
		if (changed == CarDetailSection.None) return;
		var send = CarDetailsIO.Read(carLoader, changed);
		Remember(loader, send, changed);
		int seq = nextSeq++;
		latestSeq[loader] = seq;
		Log.Debug($"[CarDetails] Loader {loader}: {changed} changed.");
		Client.Instance.Send(new CarDetailsUpdatePacket { CarLoaderID = loader, SpawnSeq = CarPartsSync.SpawnSeq(loader), ClientSeq = seq, Details = send });
	}

	public static void OnUpdate(CarDetailsUpdatePacket packet, int snapshotId)
	{
		if (packet.SourceClientId == Client.Instance.ID && latestSeq.TryGetValue(packet.CarLoaderID, out int latest) && packet.ClientSeq < latest)
		{
			Count(packet, snapshotId);
			return;
		}
		MelonCoroutines.Start(ApplyWhenReady(packet, snapshotId));
	}

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
		{
			applying.Add(loader);
			try { CarDetailsIO.Apply(carLoader, packet.Details); }
			finally { applying.Remove(loader); }
			var sections = Present(packet.Details);
			Remember(loader, CarDetailsIO.Read(carLoader, sections), sections);
			if (packet.IsFull) awaiting.Remove(loader);
			if (packet.IsFull) Log.Info($"[CarDetails] Loader {loader}: details applied ({sections}).");
		}
		Count(packet, snapshotId);
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
		return sections;
	}

	private static void Remember(int loader, ModCarDetails details, CarDetailSection sections)
	{
		if (!lastKnown.TryGetValue(loader, out var known)) lastKnown[loader] = known = new Dictionary<CarDetailSection, string>();
		foreach (var section in Sections)
			if (sections.HasFlag(section)) known[section] = Signature(details, section);
	}

	private static string Known(int loader, CarDetailSection section) =>
		lastKnown.TryGetValue(loader, out var known) && known.TryGetValue(section, out string value) ? value : null;

	private static readonly JsonSerializerSettings Rounded = new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String, Converters = { new RoundingConverter() } };

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
		return JsonConvert.SerializeObject(value, Rounded);
	}

	private sealed class RoundingConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType) => objectType == typeof(float) || objectType == typeof(double);
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(Math.Round(Convert.ToDouble(value), 3));
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotSupportedException();
		public override bool CanRead => false;
	}
}
