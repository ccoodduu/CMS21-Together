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
	public GameObject Sound;
	public RealisticEngineSound Res;

	public bool IsAlive => Sound != null && Sound && Res != null;

	public bool IsPlaying => IsAlive && Sound.GetComponentsInChildren<AudioSource>().Any(s => s.isPlaying);
}

// The game's EngineAudioController is one global object for the local player's car, so a remote engine gets its own
// copy of the engine's sound prefab, built the way EngineAudioController.Prepare builds it.
public static class RemoteEngines
{
	private const string EnginePrefabPath = "RealisticEngineSounds/Engines/";
	private const float FallbackLimiterRpm = 6000f;

	private static readonly Dictionary<int, RemoteEngine> engines = new Dictionary<int, RemoteEngine>();

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
			var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(record.EngineCarLoaderId);
			if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad) || !carLoader.IsCarLoaded()) return;
			engine = Create(carLoader, record.PlayerId, record.EngineCarLoaderId);
			if (engine == null) return;
			engines[record.PlayerId] = engine;
			Log.Info($"[Presence] Engine sound of player {record.PlayerId} on car {record.EngineCarLoaderId}.");
		}

		var data = CarLoaderPlaces.Get().GetCarLoaderByIndex(engine.CarLoaderId).EngineData;
		engine.Res.engineCurrentRPM = Mathf.Max(record.EngineRpm, data.idleRpm);
		if (engine.Res.mainCamera == null || !engine.Res.mainCamera) engine.Res.mainCamera = Camera.main;
	}

	public static void Refresh()
	{
		foreach (var player in PresenceManager.Roster.Values)
			if (player.Record.EngineRunning) Apply(player);
	}

	public static void Remove(int playerId)
	{
		if (!engines.TryGetValue(playerId, out var engine)) return;
		if (engine.Sound != null && engine.Sound) Object.Destroy(engine.Sound);
		engines.Remove(playerId);
	}

	public static void Clear()
	{
		foreach (int id in engines.Keys.ToList()) Remove(id);
	}

	public static RemoteEngine Create(CarLoader carLoader, int playerId, int carLoaderId)
	{
		var engineObject = carLoader.GetEngine();
		string soundName = engineObject == null ? null : carLoader.GetEngineSound();
		var prefab = string.IsNullOrEmpty(soundName) ? null : Resources.Load<GameObject>(EnginePrefabPath + soundName);
		if (prefab == null)
		{
			Log.Warn($"[Presence] No engine sound for car {carLoaderId} ('{soundName}'); player {playerId}'s engine stays silent.");
			return null;
		}

		var sound = Object.Instantiate(prefab, engineObject.transform);
		sound.name = $"RemoteEngine[{playerId}]";
		sound.transform.position = engineObject.transform.position;
		foreach (var behaviour in sound.GetComponentsInChildren<MonoBehaviour>(true))
			if (behaviour.GetIl2CppType().Name == "EdysToRes") behaviour.enabled = false;

		var res = sound.GetComponent<RealisticEngineSound>();
		if (res == null)
		{
			Object.Destroy(sound);
			return null;
		}
		var data = carLoader.EngineData;
		res.maxRPMLimit = data.limiterTriggerRpm > 0f ? data.limiterTriggerRpm : data.maxRpm > 0f ? data.maxRpm : FallbackLimiterRpm;
		res.masterVolume = 1f;
		res.mainCamera = Camera.main;
		return new RemoteEngine { PlayerId = playerId, CarLoaderId = carLoaderId, Sound = sound, Res = res };
	}
}
