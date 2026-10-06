using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Managers;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public class RemotePlayer
{
	public PlayerPresenceRecord Record;
	public PlayerInstance Avatar;

	public bool HasAvatar => Avatar != null && Avatar;
}

public static class PresenceManager
{
	public static readonly Dictionary<int, RemotePlayer> Roster = new Dictionary<int, RemotePlayer>();

	public static CharacterMotor LocalMotor { get; private set; }

	public static int VisibleAvatarCount => Roster.Values.Count(p => p.HasAvatar && p.Avatar.gameObject.activeSelf);

	public static void FindLocalMotor()
	{
		LocalMotor = Object.FindObjectOfType<CharacterMotor>();
	}

	public static bool HasLocalMotor => LocalMotor != null && LocalMotor;

	public static void ApplyRoster(PlayerRosterPacket packet, int snapshotId)
	{
		Clear();
		foreach (var record in packet.Records ?? new List<PlayerPresenceRecord>())
		{
			if (record.PlayerId == Client.Instance.ID) continue;
			Roster[record.PlayerId] = new RemotePlayer { Record = record };
		}
		ReconcileAll();
		Log.Info($"[Presence] Roster: {string.Join(", ", Roster.Values.Select(p => $"{p.Record.PlayerId} '{p.Record.Username}' in {p.Record.Scene}"))}");
		SyncTracker.Applied(SyncOrder.PlayersKey, snapshotId);
	}

	public static void ApplyRecord(PlayerPresenceRecord record)
	{
		if (record == null || record.PlayerId == Client.Instance.ID) return;

		if (!Roster.TryGetValue(record.PlayerId, out var player))
		{
			player = new RemotePlayer();
			Roster[record.PlayerId] = player;
			Log.Info($"[Presence] Player {record.PlayerId} '{record.Username}' joined ({record.Scene}).");
		}
		player.Record = record;
		if (player.HasAvatar && record.LastMovement != null) ApplyMovement(player.Avatar, record.LastMovement);
		Reconcile(record.PlayerId);
	}

	public static void ApplyMovement(MovementPacket packet)
	{
		if (!Roster.TryGetValue(packet.SenderId, out var player)) return;
		if (packet.Scene != player.Record.Scene) return;

		player.Record.LastMovement = packet;
		if (player.HasAvatar) ApplyMovement(player.Avatar, packet);
		else Reconcile(packet.SenderId);
	}

	public static void Remove(int playerId)
	{
		if (!Roster.TryGetValue(playerId, out var player)) return;
		DestroyAvatar(player);
		Roster.Remove(playerId);
		Log.Info($"[Presence] Player {playerId} left.");
	}

	public static void Clear()
	{
		foreach (var player in Roster.Values) DestroyAvatar(player);
		Roster.Clear();
	}

	public static void ReconcileAll()
	{
		foreach (int id in Roster.Keys.ToList()) Reconcile(id);
	}

	public static void Reconcile(int playerId)
	{
		if (!Roster.TryGetValue(playerId, out var player)) return;
		var record = player.Record;

		bool visible = record.Scene == ClientScene.LocalScene
		               && GameSceneInfo.ShowsAvatars(record.Scene)
		               && record.LastMovement != null
		               && record.LastMovement.Scene == record.Scene;
		if (!visible)
		{
			DestroyAvatar(player);
			return;
		}

		if (!player.HasAvatar)
		{
			player.Avatar = CreateAvatar(record);
			if (player.Avatar == null) return;
		}
		player.Avatar.gameObject.SetActive(record.SeatCarLoaderId == PlayerPresenceRecord.NoCar);
	}

	public static PlayerPresenceRecord CaptureLocalRecord()
	{
		return new PlayerPresenceRecord
		{
			PlayerId = Client.Instance.ID,
			Scene = ClientScene.LocalScene,
			LastMovement = ClientScene.LocalScene == GameScene.Loading ? null : Movement.CaptureLocal()
		};
	}

	public static void PublishLocal()
	{
		if (Client.Instance == null || !Client.Instance.IsConnectionValid) return;
		Client.Instance.Send(new PlayerPresencePacket { Record = CaptureLocalRecord() });
	}

	private static PlayerInstance CreateAvatar(PlayerPresenceRecord record)
	{
		if (!ModGameManager.PlayerPrefab)
		{
			Log.Warn("Cannot create an avatar, player prefab is null.");
			return null;
		}

		var movement = record.LastMovement;
		var position = new Vector3(movement.Position.X, movement.Position.Y, movement.Position.Z);
		var rotation = new Quaternion(movement.Rotation.X, movement.Rotation.Y, movement.Rotation.Z, movement.Rotation.W);
		GameObject avatar = Object.Instantiate(ModGameManager.PlayerPrefab, position, rotation);
		avatar.SetActive(true);
		avatar.name = $"Player[{record.PlayerId}]";
		var instance = avatar.AddComponent<PlayerInstance>();
		ApplyMovement(instance, movement);
		Log.Debug($"[Presence] Avatar for player {record.PlayerId} at ({position.x:F2},{position.y:F2},{position.z:F2}).");
		return instance;
	}

	private static void ApplyMovement(PlayerInstance avatar, MovementPacket packet)
	{
		var position = new Vector3(packet.Position.X, packet.Position.Y, packet.Position.Z);
		var velocity = new Vector3(packet.Velocity.X, packet.Velocity.Y, packet.Velocity.Z);
		var rotation = new Quaternion(packet.Rotation.X, packet.Rotation.Y, packet.Rotation.Z, packet.Rotation.W);
		avatar.UpdateNetworkState(position, rotation, velocity, packet.CameraPitch, packet.IsGrounded, packet.IsCrouching, packet.IsRunning);
	}

	private static void DestroyAvatar(RemotePlayer player)
	{
		if (player.HasAvatar) Object.Destroy(player.Avatar.gameObject);
		player.Avatar = null;
	}
}
