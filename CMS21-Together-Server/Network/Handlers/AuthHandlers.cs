using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Compatibility;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Diagnostics.Perf;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network.Transport;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class AuthHandler
	{
		private static int lastSnapshotId;

		[PacketHandler(PacketTypes.Heartbeat)]
		[AllowBeforeSync]
		public static void OnHeartbeat(long clientId, HeartbeatPacket packet)
		{
			Server.Clients[(int)clientId].OnHeartbeatEcho(packet.sentTicks);
		}

		[PacketHandler(PacketTypes.Connect)]
		[AllowBeforeSync]
		public static void OnConnected(long clientId, ConnectPacket packet)
		{
			Logger.Debug($"Reiceved Connection callback from {packet.username}");
			Logger.Debug($"Received info: {packet.modVersion}, {packet.username}, {packet.playerID}");

			if (!CompatibilityPolicy.Evaluate((int)clientId, packet)) return;
			var client = Server.Clients[(int)clientId];
			if (!PasswordAccepted(client, packet.password))
			{
				Server.Refuse(client.ID, DisconnectReason.WrongPassword, "");
				return;
			}

			string identity = PlayerRecords.Resolve(client, packet.playerKey);
			if (identity == null)
			{
				Server.Refuse(client.ID, DisconnectReason.MissingIdentity, "");
				return;
			}
			var holder = PlayerRecords.ConnectedWith(identity, client.ID);
			if (holder != null)
			{
				Logger.Info($"{PlayerRecords.ShortKey(identity)} is already connected as Client[{holder.ID}].");
				Server.Refuse(client.ID, DisconnectReason.DuplicateIdentity, "");
				return;
			}
			client.Identity = identity;

			client.IsAdmin = !string.IsNullOrEmpty(Program.Config.AdminKey) && packet.adminKey == Program.Config.AdminKey;
			client.GameVersion = packet.gameVersion;
			client.ModVersion = packet.modVersion;
			client.Mods = (packet.mods ?? new List<ModReport>()).Select(m => m.ToString()).ToList();
			client.OnConnectedSuccessfully.Invoke();
			SharedDlc.Add(client.ID, packet.dlc);
			Server.SendToClient(BuildServerInfo(client.ID), client.ID);

			var record = PresenceRegistry.Add(client.ID, packet.username);
			PlayerRecords.OnJoined(identity, record.Username, out bool returning);
			Logger.Info($"Player {record.PlayerId} '{record.Username}' joined as {PlayerRecords.ShortKey(identity)} ({(returning ? "returning" : "new")}){(client.IsAdmin ? " (admin)" : "")}");
			Server.SendToClients(new PlayerPresencePacket { Record = record.Copy() }, record.PlayerId);
		}

		private static bool PasswordAccepted(Client client, string password)
		{
			if (string.IsNullOrEmpty(Program.Config.Password)) return true;
			if (client.ConnectionType == NetworkType.Steam &&!Program.Config.PasswordSteam) return true;
			return password == Program.Config.Password;
		}

		public static ServerInfoPacket BuildServerInfo(int clientId) => new ServerInfoPacket
		{
			ServerName = Program.Config.ServerName,
			ModVersion = Program.MOD_VERSION,
			Port = Program.Config.Port,
			MaxPlayers = Program.Config.MaxPlayers,
			SteamId = Program.Config.UseSteam ? SteamTransport.GetServerSteamID() : 0,
			PublicAddress = Program.Config.PublicAddress,
			Difficulty = GameDataManager.CurrentState.WorldState.Gamemode,
			SharedDlc = SharedDlc.Shared.ToList(),
			PasswordRequired = !string.IsNullOrEmpty(Program.Config.Password),
			IsAdmin = Server.Clients.TryGetValue(clientId, out var client) && client.IsAdmin,
			LockScope = Program.Config.LockScope,
			LockExpirySeconds = Program.Config.LockExpirySeconds,
			SharedOutdoorScenes = Data.Outdoor.OutdoorInstances.SharedScenes.OrderBy(s => (int)s).ToList()
		};

		public static void BroadcastServerInfo()
		{
			foreach (var client in Server.Clients.Values.Where(c => c.IsConnected && c.IsAccepted))
				Server.SendToClient(BuildServerInfo(client.ID), client.ID);
		}

		[PacketHandler(PacketTypes.AskForSync)]
		[AllowBeforeSync]
		public static void OnAskForSync(long clientId, AskForSync packet)
		{
			long waitStart = Stopwatch.GetTimestamp();
			lock (GameDataManager.StateLock)
			{
				long acquired = Stopwatch.GetTimestamp();
				var client = Server.Clients[(int)clientId];
				int snapshotId = ++lastSnapshotId;
				client.SnapshotId = snapshotId;
				long sentBefore = TrafficCounters.SlotBytes(client.ID, PerfDirection.Sent);

				Server.SendToClient(new SyncBegin { snapshotId = snapshotId }, client.ID);

				var items = new Dictionary<string, int>();
				foreach (var provider in SessionRegistry.SnapshotProviders)
					items[provider.Key] = provider.SendSnapshot(client.ID);

				Server.SendToClient(new SyncEnd { snapshotId = snapshotId, Items = items }, client.ID);
				client.SyncState = SyncState.Syncing;

				long built = Stopwatch.GetTimestamp();
				HandlerTimings.Record(HandlerTimings.SnapshotBuild, acquired - waitStart, built - acquired);
				client.SnapshotRequestedAt = waitStart;
				client.SnapshotBytes = TrafficCounters.SlotBytes(client.ID, PerfDirection.Sent) - sentBefore;
				client.SnapshotBuildMs = HandlerTimings.TicksToMs(built - acquired);
				client.SnapshotItems = string.Join(", ", items.Select(i => $"{i.Key}={i.Value}"));

				Logger.Info($"Client[{client.ID}] snapshot {snapshotId}: {client.SnapshotItems}");
			}
		}

		[PacketHandler(PacketTypes.SyncAck)]
		[AllowBeforeSync]
		public static void OnSyncAck(long clientId, SyncAck packet)
		{
			var client = Server.Clients[(int)clientId];
			if (packet.snapshotId != client.SnapshotId || client.SyncState != SyncState.Syncing)
			{
				Logger.Warn($"Client[{client.ID}] acked snapshot {packet.snapshotId}, expected {client.SnapshotId} ({client.SyncState}). Ignored.");
				return;
			}

			client.SyncState = SyncState.InSession;
			Data.Jobs.JobsService.OnInSession(client.ID);
			Logger.Info($"Client[{client.ID}] joined (snapshot {packet.snapshotId}).");
			Logger.Info(FormattableString.Invariant($"Client[{client.ID}] snapshot {packet.snapshotId} acked after {HandlerTimings.TicksToMs(Stopwatch.GetTimestamp() - client.SnapshotRequestedAt):0} ms, ") +
			            FormattableString.Invariant($"{client.SnapshotBytes} bytes, built in {client.SnapshotBuildMs:0.0} ms ({client.SnapshotItems})"));
		}
	}
}
