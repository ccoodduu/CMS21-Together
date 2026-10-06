using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network.Transport;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class AuthHandler
	{
		private static int lastSnapshotId;

		[PacketHandler(PacketTypes.Heartbeat)]
		public static void OnHeartbeat(long clientId, HeartbeatPacket packet)
		{
			Server.Clients[(int)clientId].LastHeartbeatTime = ServerTime.Time;
		}
		
		[PacketHandler(PacketTypes.Connect)]
		public static void OnConnected(long clientId, ConnectPacket packet)
		{
			Logger.Debug($"Reiceved Connection callback from {packet.username}");
			Logger.Debug($"Received info: {packet.modVersion}, {packet.username}, {packet.playerID}");

			if (packet.modVersion != Program.MOD_VERSION)
			{
				Server.Refuse((int)clientId, DisconnectReason.VersionMismatch,
					$"This server runs Together {Program.MOD_VERSION}; you have {packet.modVersion}.");
				return;
			}
			Server.Clients[(int)clientId].OnConnectedSuccessfully.Invoke();
			Server.SendToClient(new ServerInfoPacket
			{
				ServerName = Program.Config.ServerName,
				ModVersion = Program.MOD_VERSION,
				Port = Program.Config.Port,
				MaxPlayers = Program.Config.MaxPlayers,
				SteamId = Program.Config.UseSteam ? SteamTransport.GetServerSteamID() : 0,
				PublicAddress = Program.Config.PublicAddress,
				Difficulty = GameDataManager.CurrentState.WorldState.Gamemode
			}, (int)clientId);

			var record = PresenceRegistry.Add((int)clientId, packet.username);
			Logger.Info($"Player {record.PlayerId} '{record.Username}' joined");
			Server.SendToClients(new PlayerPresencePacket { Record = record.Copy() }, record.PlayerId);
		}

		[PacketHandler(PacketTypes.AskForSync)]
		public static void OnAskForSync(long clientId, AskForSync packet)
		{
			lock (GameDataManager.StateLock)
			{
				var client = Server.Clients[(int)clientId];
				int snapshotId = ++lastSnapshotId;
				client.SnapshotId = snapshotId;

				Server.SendToClient(new SyncBegin { snapshotId = snapshotId }, client.ID);

				var items = new Dictionary<string, int>();
				foreach (var provider in SessionRegistry.SnapshotProviders)
					items[provider.Key] = provider.SendSnapshot(client.ID);

				Server.SendToClient(new SyncEnd { snapshotId = snapshotId, Items = items }, client.ID);
				client.SyncState = SyncState.Syncing;

				Logger.Info($"Client[{client.ID}] snapshot {snapshotId}: {string.Join(", ", items.Select(i => $"{i.Key}={i.Value}"))}");
			}
		}

		[PacketHandler(PacketTypes.SyncAck)]
		public static void OnSyncAck(long clientId, SyncAck packet)
		{
			var client = Server.Clients[(int)clientId];
			if (packet.snapshotId != client.SnapshotId || client.SyncState != SyncState.Syncing)
			{
				Logger.Warn($"Client[{client.ID}] acked snapshot {packet.snapshotId}, expected {client.SnapshotId} ({client.SyncState}). Ignored.");
				return;
			}

			client.SyncState = SyncState.InSession;
			Logger.Info($"Client[{client.ID}] joined (snapshot {packet.snapshotId}).");
		}
	}
}
