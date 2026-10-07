using System.Collections;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.UI;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

public enum OutdoorVisit
{
	None,
	Waiting,
	Shared,
	Local
}

public static class OutdoorSession
{
	public const float InstanceTimeoutSeconds = 15f;

	private static int waitToken;
	private static bool forceNextLocal;

	public static OutdoorVisit Visit { get; private set; } = OutdoorVisit.None;
	public static GameScene Scene { get; private set; } = GameScene.Unknown;
	public static OutdoorInstancePacket Instance { get; private set; }
	public static bool Applied { get; private set; }
	public static float EnterSentAt { get; private set; }

	public static bool IsShared => Visit == OutdoorVisit.Shared && Instance != null && Instance.Shared;
	public static int InstanceId => IsShared ? Instance.InstanceId : OutdoorInstancePacket.NotShared;
	public static int Seed => IsShared ? Instance.Seed : 0;
	public static bool IsGenerator => IsShared && Instance.Generator;

	public static bool IsSharedScene(GameScene scene)
	{
		var shared = ClientData.ServerInfo?.SharedOutdoorScenes;
		return shared != null && shared.Contains(scene);
	}

	public static bool ReadyToPublish => Visit != OutdoorVisit.Waiting && (Visit != OutdoorVisit.Shared || Applied);

	public static bool HoldsGenerator(GameScene scene) => Visit == OutdoorVisit.Waiting && Scene == scene;

	public static void Initialize()
	{
		ClientScene.LeavingScene += OnLeavingScene;
	}

	public static void Reset()
	{
		waitToken++;
		Visit = OutdoorVisit.None;
		Scene = GameScene.Unknown;
		Instance = null;
		Applied = false;
		LootSync.Reset();
		OutdoorCarSync.Reset();
		AuctionSync.Reset();
		GeneratorHooks.Reset();
		Reseed.Reset();
		OutdoorDigest.Reset();
	}

	public static void ForceNextLocal() => forceNextLocal = true;

	private static void OnLeavingScene(GameScene from, GameScene to)
	{
		if (Visit != OutdoorVisit.None) Log.Info($"[Outdoor] Leaving {Scene} ({Visit}{(IsShared ? $" #{Instance.InstanceId}" : "")}).");
		Reset();
		if (!OutdoorScenes.IsOutdoor(to) || Client.Instance == null || !Client.Instance.IsConnectionValid) return;

		Scene = to;
		if (forceNextLocal || !IsSharedScene(to))
		{
			Log.Info($"[Outdoor] {to} is {(forceNextLocal ? "forced local" : "not shared by the server")}; this visit is local.");
			forceNextLocal = false;
			Visit = OutdoorVisit.Local;
			return;
		}

		Visit = OutdoorVisit.Waiting;
		EnterSentAt = Time.realtimeSinceStartup;
		Client.Instance.Send(new OutdoorEnterPacket { Scene = to });
		Log.Info($"[Outdoor] OutdoorEnter {to} sent.");
		MelonCoroutines.Start(TimeOut(++waitToken, to));
	}

	private static IEnumerator TimeOut(int token, GameScene scene)
	{
		float deadline = Time.realtimeSinceStartup + InstanceTimeoutSeconds;
		while (Time.realtimeSinceStartup < deadline && token == waitToken && Visit == OutdoorVisit.Waiting) yield return null;
		if (token != waitToken || Visit != OutdoorVisit.Waiting) yield break;
		GoLocal($"no instance within {InstanceTimeoutSeconds:0} s");
	}

	private static void GoLocal(string why)
	{
		Visit = OutdoorVisit.Local;
		Instance = null;
		Log.Warn($"[Outdoor] {Scene}: {why}; this visit is generated locally and not shared.");
		ModNotify.ShowToast($"This {Scene.ToString().ToLowerInvariant()} visit is not shared with the other players.");
	}

	public static void OnInstance(OutdoorInstancePacket packet)
	{
		if (packet == null || packet.Scene != Scene || Visit == OutdoorVisit.None || Visit == OutdoorVisit.Local)
		{
			Log.Info($"[Outdoor] OutdoorInstance for {packet?.Scene} #{packet?.InstanceId} ignored ({Visit} in {Scene}).");
			return;
		}
		if (!packet.Shared)
		{
			GoLocal("the server does not share this scene");
			return;
		}
		if (Visit == OutdoorVisit.Shared && Instance != null && Instance.InstanceId != packet.InstanceId)
		{
			Log.Warn($"[Outdoor] OutdoorInstance #{packet.InstanceId} ignored, this visit is #{Instance.InstanceId}.");
			return;
		}

		bool first = Visit == OutdoorVisit.Waiting;
		var previous = Instance;
		Instance = packet;
		Visit = OutdoorVisit.Shared;
		if (first)
		{
			Log.Info($"[Outdoor] OutdoorInstance {packet.Scene} #{packet.InstanceId} after {Time.realtimeSinceStartup - EnterSentAt:0.00} s: seed {packet.Seed}, {(packet.Generator ? "generator" : "joining")}, " +
			         $"{packet.Picks.Count} picks, {packet.Sold.Count} sold, piles {(packet.Piles == null ? "not recorded" : packet.Piles.Count.ToString())}, {packet.Lots.Count} lots.");
			OutdoorCarSync.OnInstance(packet);
			return;
		}

		Log.Info($"[Outdoor] Instance #{packet.InstanceId} update: generator {packet.Generator}, piles {(packet.Piles == null ? "not recorded" : packet.Piles.Count.ToString())}, {packet.Lots.Count} lots{(packet.LotsPending ? " (pending)" : "")}.");
		OutdoorCarSync.OnInstance(packet);
		if (Applied && previous?.Piles == null && packet.Piles != null) LootSync.ReplayIfNeeded();
		if (Applied && packet.Generator && !previous.Generator) MelonCoroutines.Start(OutdoorArrival.UploadAsGenerator());
		AuctionSync.OnInstance(packet);
	}

	public static void MarkApplied()
	{
		if (Applied) return;
		Applied = true;
		Log.Info($"[Outdoor] {Scene} #{InstanceId} applied{(IsGenerator ? " as generator" : "")}.");
	}

	public static string Describe() =>
		$"{Visit} {Scene}{(IsShared ? $" #{Instance.InstanceId} seed {Instance.Seed}{(IsGenerator ? " generator" : "")}, picks {Instance.Picks.Count}, sold [{string.Join(",", Instance.Sold)}]" : "")}, applied {Applied}";
}
