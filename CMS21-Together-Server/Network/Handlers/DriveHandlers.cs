using System.Collections.Generic;
using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	// remote-visual-feedback D9: the driver is the physics authority; the server keeps each player's running drive and
	// relays it to the players in the driver's scene. Garage driving does not exist in the game (spike 8.1), so only
	// track scenes are accepted.
	public static class DriveHandlers
	{
		public const int MaxStatesPerSecond = 20;
		public const int MaxBlobBytes = 512 * 1024;
		private const float WarnIntervalSeconds = 60f;

		private static readonly HashSet<GameScene> driveScenes = new HashSet<GameScene> { GameScene.TestTrack };

		private class Budget
		{
			public float WindowStart;
			public int Count;
			public int Dropped;
			public float LastWarn = float.MinValue;
		}

		private static readonly Dictionary<int, Budget> budgets = new Dictionary<int, Budget>();

		public static void Initialize()
		{
			PresenceEvents.SceneChanged += OnSceneChanged;
			PresenceEvents.Left += OnLeft;
		}

		[PacketHandler(PacketTypes.CarDriveStart)]
		public static void OnStart(long clientId, CarDriveStartPacket packet)
		{
			int id = (int)clientId;
			var record = PresenceRegistry.Get(id);
			if (record == null || packet == null) return;
			if (!driveScenes.Contains(packet.Scene) || record.Scene != packet.Scene)
			{
				Logger.Warn($"[Drive] Player {id}: drive start in {packet.Scene} dropped (player is in {record.Scene}).");
				return;
			}
			if (packet.CarBlob != null && packet.CarBlob.Length > MaxBlobBytes)
			{
				Logger.Warn($"[Drive] Player {id}: drive start dropped, car data of {packet.CarBlob.Length} bytes.");
				return;
			}
			if (packet.CarLoaderID >= 0 && CarAwayRegistry.IsAwayFrom(packet.CarLoaderID, id))
			{
				Logger.Warn($"[Drive] Player {id}: drive start dropped, car {packet.CarLoaderID} is away with player {CarAwayRegistry.OwnerOf(packet.CarLoaderID)}.");
				return;
			}

			if (ActiveDrives.End(id, out var previous)) Relay(previous.Scene, id, new CarDriveStopPacket { PlayerId = id, DriveId = previous.Start.DriveId }, true);
			packet.PlayerId = id;
			ActiveDrives.Begin(packet);
			int sent = Relay(packet.Scene, id, packet, true);
			Logger.Info($"[Drive] Player {id} drives {packet.CarToLoad} (car {packet.CarLoaderID}, drive {packet.DriveId}) in {packet.Scene}; start relayed to {sent}, {packet.CarBlob?.Length ?? 0} bytes of car data.");
		}

		[PacketHandler(PacketTypes.CarDriveState)]
		public static void OnState(long clientId, CarDriveStatePacket packet)
		{
			int id = (int)clientId;
			var drive = ActiveDrives.Get(id);
			if (drive == null || packet == null || packet.DriveId != drive.Start.DriveId) return;
			if (packet.Payload == null || packet.Payload.Length != DriveStateCodec.Size) return;
			if (!WithinBudget(id)) return;
			packet.PlayerId = id;
			if (drive.Latest == null || packet.Seq > drive.Latest.Seq) drive.Latest = packet;
			Relay(drive.Scene, id, packet, false);
		}

		[PacketHandler(PacketTypes.CarDriveStop)]
		public static void OnStop(long clientId, CarDriveStopPacket packet)
		{
			int id = (int)clientId;
			var drive = ActiveDrives.Get(id);
			if (drive == null || packet == null || packet.DriveId != drive.Start.DriveId) return;
			Stop(id, packet.FinalPose, "stopped");
		}

		private static void Stop(int playerId, byte[] finalPose, string why)
		{
			if (!ActiveDrives.End(playerId, out var drive)) return;
			int sent = Relay(drive.Scene, playerId, new CarDriveStopPacket { PlayerId = playerId, DriveId = drive.Start.DriveId, FinalPose = finalPose }, true);
			Logger.Info($"[Drive] Player {playerId}'s drive {drive.Start.DriveId} in {drive.Scene} {why}; stop relayed to {sent}.");
		}

		private static int Relay<T>(GameScene scene, int driverId, T packet, bool reliable) where T : INetworkData
		{
			int sent = 0;
			foreach (var other in PresenceRegistry.All)
			{
				if (other.PlayerId == driverId || other.Scene != scene) continue;
				if (!Server.Clients.TryGetValue(other.PlayerId, out var client) || !client.IsConnected || client.SyncState == SyncState.Connected) continue;
				Server.SendToClient(packet, other.PlayerId, reliable);
				sent++;
			}
			return sent;
		}

		private static bool WithinBudget(int clientId)
		{
			if (!budgets.TryGetValue(clientId, out var budget))
			{
				budget = new Budget { WindowStart = ServerTime.Time };
				budgets[clientId] = budget;
			}
			float now = ServerTime.Time;
			if (now - budget.WindowStart >= 1f)
			{
				budget.WindowStart = now;
				budget.Count = 0;
			}
			if (++budget.Count <= MaxStatesPerSecond) return true;

			budget.Dropped++;
			if (now - budget.LastWarn >= WarnIntervalSeconds)
			{
				budget.LastWarn = now;
				Logger.Warn($"[Drive] Client {clientId} sends more than {MaxStatesPerSecond} drive states per second; dropped {budget.Dropped} so far.");
			}
			return false;
		}

		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			var own = ActiveDrives.Get(clientId);
			if (own != null && own.Scene != to) Stop(clientId, null, $"ended by the scene change to {to}");

			if (!driveScenes.Contains(to) || !Server.Clients.TryGetValue(clientId, out var client) || !client.IsConnected || client.SyncState == SyncState.Connected) return;
			foreach (var pair in ActiveDrives.All)
			{
				if (pair.Key == clientId || pair.Value.Scene != to) continue;
				Server.SendToClient(pair.Value.Start, clientId);
				if (pair.Value.Latest != null) Server.SendToClient(pair.Value.Latest, clientId);
				Logger.Info($"[Drive] Player {clientId} entered {to}: sent player {pair.Key}'s running drive {pair.Value.Start.DriveId}.");
			}
		}

		private static void OnLeft(int clientId)
		{
			budgets.Remove(clientId);
			Stop(clientId, null, "ended by the driver leaving");
		}
	}
}
