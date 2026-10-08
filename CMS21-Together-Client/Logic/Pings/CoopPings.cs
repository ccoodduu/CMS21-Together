using System;
using System.Collections.Generic;
using CMS.UI;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Locks;
using CMS21Together.Logic.Outdoor;
using CMS21Together.Logic.Player;
using CMS21Together.Logic.Visuals;
using CMS21Together.Network;
using CMS21Together.UI;
using UnityEngine;

namespace CMS21Together.Logic.Pings;

public enum PingOutcome
{
	Sent,
	Throttled,
	NotAllowed,
	NothingThere
}

public static class CoopPings
{
	public const float MinIntervalSeconds = 0.5f;
	public const float RayDistance = 60f;
	private const int AllButIgnoreRaycastLayer = ~(1 << 2);

	private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
	private static float lastSent = float.MinValue;
	private static bool subscribed;

	public static IReadOnlyDictionary<string, int> Counters => counters;
	public static CoopPingPacket LastSent { get; private set; }

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		ClientScene.LeavingScene += (from, to) => PingMarkers.Clear();
		PresenceManager.PlayerRemoved += (record, reason) => PingMarkers.RemovePlayer(record.PlayerId);
	}

	public static void Reset()
	{
		counters.Clear();
		lastSent = float.MinValue;
		LastSent = null;
		PingMarkers.Clear();
	}

	public static void Update()
	{
		var key = PlayerSettings.PingKey;
		if (key == KeyCode.None || !Input.GetKeyDown(key) || !InputFree()) return;
		PingUnderCursor();
	}

	public static PingOutcome PingUnderCursor()
	{
		if (!Allowed()) return Count(PingOutcome.NotAllowed);
		var packet = TargetUnderCursor();
		if (packet == null) return Count(PingOutcome.NothingThere);
		return Send(packet);
	}

	public static PingOutcome PingSpot(Vector3 position)
	{
		if (!Allowed()) return Count(PingOutcome.NotAllowed);
		return Send(Spot(position));
	}

	public static bool Allowed()
	{
		var client = Client.Instance;
		if (client == null || !client.IsConnectionValid || !ClientData.IsInitialSyncFinished) return false;
		return GameSceneInfo.ShowsAvatars(ClientScene.LocalScene, OutdoorSession.IsShared);
	}

	private static bool InputFree()
	{
		if (GUIUtility.keyboardControl != 0 || MultiplayerMenuModel.SessionPanelOpen) return false;
		var windows = WindowManager.Instance;
		return windows == null || !windows.IsAnyWindowOpened();
	}

	public static CoopPingPacket TargetUnderCursor()
	{
		var game = GameScript.Get();
		var part = game == null ? null : game.partMouseOver;
		if (part != null && LockHooks.TryResolve(part, out int loader, out string key)) return Part(loader, key, PingMarkers.Center(PartGhosts.PartRenderers(part), part.transform.position));

		var body = BodyUnderCursor(game);
		if (body != null) return body;

		var camera = Camera.main;
		if (camera == null) return null;
		var ray = Cursor.visible ? camera.ScreenPointToRay(Input.mousePosition) : camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
		if (!Physics.Raycast(ray, out RaycastHit hit, RayDistance, AllButIgnoreRaycastLayer, QueryTriggerInteraction.Ignore)) return null;
		var hitPart = hit.collider == null ? null : hit.collider.GetComponentInParent<PartScript>();
		if (hitPart != null && !hitPart.IsUnmounted && LockHooks.TryResolve(hitPart, out loader, out key))
			return Part(loader, key, PingMarkers.Center(PartGhosts.PartRenderers(hitPart), hitPart.transform.position));
		return Spot(hit.point);
	}

	private static CoopPingPacket BodyUnderCursor(GameScript game)
	{
		string type = game?.IOMouseOverType;
		var carLoader = game?.IOMouseOverCarLoader;
		if (string.IsNullOrEmpty(type) || type.Length < 2 || carLoader == null || game.IOMouseOverIO == null) return null;
		var part = carLoader.GetCarPart(type.Substring(1));
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		var registry = VisualScope.RegistryOf(loader);
		if (part?.handle == null || registry == null || !registry.TryGetBodyIndex(part, out int index)) return null;
		var handle = part.handle.transform;
		return Part(loader, PartKeys.Body(index), PingMarkers.Center(PartGhosts.VisibleRenderers(handle), handle.position));
	}

	private static CoopPingPacket Part(int loader, string key, Vector3 position) => new CoopPingPacket
	{
		Scene = ClientScene.LocalScene, CarLoaderID = loader, PartKey = key, Position = new Vector3Serializable(position.x, position.y, position.z),
	};

	private static CoopPingPacket Spot(Vector3 position) => new CoopPingPacket
	{
		Scene = ClientScene.LocalScene, Position = new Vector3Serializable(position.x, position.y, position.z),
	};

	private static PingOutcome Send(CoopPingPacket packet)
	{
		float now = Time.realtimeSinceStartup;
		if (now - lastSent < MinIntervalSeconds) return Count(PingOutcome.Throttled);
		lastSent = now;
		packet.PlayerId = Client.Instance.ID;
		LastSent = packet;
		Client.Instance.Send(packet);
		PingMarkers.Show(packet.PlayerId, "You", own: true, packet);
		Log.Info($"[Ping] Pinged {packet}.");
		return Count(PingOutcome.Sent);
	}

	public static void OnPacket(CoopPingPacket packet)
	{
		if (packet?.Position == null) return;
		if (packet.Scene != ClientScene.LocalScene)
		{
			Count("droppedScene");
			return;
		}
		string name = PresenceManager.Roster.TryGetValue(packet.PlayerId, out var player) && !string.IsNullOrEmpty(player.Record?.Username)
			? player.Record.Username
			: $"Player {packet.PlayerId}";
		Count("received");
		var marker = PingMarkers.Show(packet.PlayerId, name, own: false, packet);
		Count(marker.Resolved ? "shownOnPart" : packet.IsPart ? "partFallbackToSpot" : "shownSpot");
		Log.Info($"[Ping] {name} pinged {packet}{(packet.IsPart && !marker.Resolved ? " (part not found here, shown at its position)" : "")}.");
	}

	private static PingOutcome Count(PingOutcome outcome)
	{
		Count(char.ToLowerInvariant(outcome.ToString()[0]) + outcome.ToString().Substring(1));
		return outcome;
	}

	internal static void Count(string name) => counters[name] = counters.TryGetValue(name, out int value) ? value + 1 : 1;
}
