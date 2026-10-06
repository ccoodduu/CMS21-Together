using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public class RemoteEngine
{
	public int PlayerId;
	public int CarLoaderId;
	public AudioSource Source;

	public bool IsAlive => Source != null && Source;
}

public static class RemoteEngines
{
	private const float MinPitch = 0.5f;
	private const float MaxPitch = 3f;
	private const float FallbackIdleRpm = 800f;
	private const float Volume = 0.6f;
	private const float MinDistance = 2f;
	private const float MaxDistance = 30f;

	private static readonly Dictionary<int, RemoteEngine> engines = new Dictionary<int, RemoteEngine>();
	private static bool warnedNoClip;

	public static IEnumerable<RemoteEngine> All => engines.Values;

	public static void Apply(RemotePlayer player)
	{
		var record = player.Record;
		bool wanted = record.EngineRunning
		              && record.EngineCarLoaderId != PlayerPresenceRecord.NoCar
		              && record.Scene == GameScene.Garage
		              && ClientScene.IsGarageReady;
		if (!wanted)
		{
			Remove(record.PlayerId);
			return;
		}

		engines.TryGetValue(record.PlayerId, out var engine);
		if (engine == null || !engine.IsAlive || engine.CarLoaderId != record.EngineCarLoaderId)
		{
			Remove(record.PlayerId);
			var anchor = Anchor(record.EngineCarLoaderId);
			if (anchor == null) return;
			var source = CreateSource(anchor, record.PlayerId);
			if (source == null) return;
			engine = new RemoteEngine { PlayerId = record.PlayerId, CarLoaderId = record.EngineCarLoaderId, Source = source };
			engines[record.PlayerId] = engine;
			Log.Info($"[Presence] Engine sound of player {record.PlayerId} on car {record.EngineCarLoaderId}.");
		}

		engine.Source.pitch = Mathf.Clamp(record.EngineRpm / IdleRpm(), MinPitch, MaxPitch);
		if (!engine.Source.isPlaying) engine.Source.Play();
	}

	public static void Refresh()
	{
		foreach (var player in PresenceManager.Roster.Values)
		{
			if (!player.Record.EngineRunning) continue;
			if (engines.TryGetValue(player.Record.PlayerId, out var engine) && engine.IsAlive) continue;
			Apply(player);
		}
	}

	public static void Remove(int playerId)
	{
		if (!engines.TryGetValue(playerId, out var engine)) return;
		if (engine.IsAlive) Object.Destroy(engine.Source.gameObject);
		engines.Remove(playerId);
	}

	public static void Clear()
	{
		foreach (int id in engines.Keys.ToList()) Remove(id);
	}

	private static Transform Anchor(int carLoaderId)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(carLoaderId);
		if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad) || !carLoader.IsCarLoaded()) return null;
		var handle = carLoader.GetLeftSeatHandle();
		return handle == null ? carLoader.transform : handle.transform;
	}

	private static AudioSource CreateSource(Transform anchor, int playerId)
	{
		var controller = Singleton<GameManager>.Instance?.EngineAudioController;
		var sound = controller == null ? null : controller.res;
		var clip = sound == null ? null : sound.idleClip;
		if (clip == null)
		{
			if (!warnedNoClip) Log.Warn("[Presence] No engine idle clip found, remote engines stay silent.");
			warnedNoClip = true;
			return null;
		}

		var holder = new GameObject($"RemoteEngine[{playerId}]");
		holder.transform.SetParent(anchor, false);
		var source = holder.AddComponent<AudioSource>();
		source.clip = clip;
		source.loop = true;
		source.spatialBlend = 1f;
		source.volume = Volume;
		source.minDistance = MinDistance;
		source.maxDistance = MaxDistance;
		var template = controller.ignitionSource;
		if (template != null) source.outputAudioMixerGroup = template.outputAudioMixerGroup;
		return source;
	}

	private static float IdleRpm()
	{
		var controller = Singleton<GameManager>.Instance?.EngineAudioController;
		return controller != null && controller.IdleRpm > 0f ? controller.IdleRpm : FallbackIdleRpm;
	}
}
