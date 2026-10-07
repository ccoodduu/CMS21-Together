using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class PlayerHandlers
	{
		[PacketHandler(PacketTypes.Movement)]
		[AllowBeforeSync]
		public static void OnMovementUpdate(long clientId, MovementPacket packet)
		{
			var record = PresenceRegistry.Get((int)clientId);
			if (record == null) return;

			packet.SenderId = (int)clientId;
			record.LastMovement = packet;
			if (packet.Scene != record.Scene || !GameSceneInfo.ShowsAvatars(packet.Scene)) return;

			foreach (var other in PresenceRegistry.All)
			{
				if (other.PlayerId == record.PlayerId || other.Scene != packet.Scene) continue;
				var client = Server.Clients[other.PlayerId];
				if (client.IsConnected && client.SyncState != SyncState.Connected)
					Server.SendToClient(packet, other.PlayerId, false);
			}
		}

		[PacketHandler(PacketTypes.PlayerPresence)]
		[AllowBeforeSync]
		public static void OnPlayerPresence(long clientId, PlayerPresencePacket packet)
		{
			var record = PresenceRegistry.Get((int)clientId);
			if (record == null || packet.Record == null) return;

			var sent = packet.Record;
			bool inGarage = sent.Scene == GameScene.Garage;
			record.SeatCarLoaderId = inGarage ? sent.SeatCarLoaderId : PlayerPresenceRecord.NoCar;
			record.SeatLeft = inGarage && sent.SeatLeft;
			record.EngineCarLoaderId = inGarage ? sent.EngineCarLoaderId : PlayerPresenceRecord.NoCar;
			record.EngineRunning = inGarage && sent.EngineRunning;
			record.EngineRpm = inGarage ? sent.EngineRpm : 0f;
			if (sent.LastMovement != null)
			{
				sent.LastMovement.SenderId = record.PlayerId;
				record.LastMovement = sent.LastMovement;
			}

			var previous = record.Scene;
			PresenceRegistry.SetScene(record, sent.Scene);
			if (previous != record.Scene)
				Logger.Info($"Player {record.PlayerId} '{record.Username}' scene {previous} -> {record.Scene}");

			Server.SendToClients(new PlayerPresencePacket { Record = record.Copy() }, record.PlayerId);
		}
	}
}
