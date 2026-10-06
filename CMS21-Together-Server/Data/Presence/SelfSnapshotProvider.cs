using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Presence
{
	[SessionSection]
	public class SelfSnapshotProvider : ISnapshotProvider
	{
		public string Key => SyncOrder.SelfKey;
		int ISnapshotProvider.SyncOrder => SyncOrder.Self;

		public int SendSnapshot(int clientId)
		{
			var client = Server.Clients[clientId];
			if (client.RestoreOffered) return 0;
			client.RestoreOffered = true;

			var record = PlayerRecords.Get(client.Identity);
			if (record == null || record.Scene != GameScene.Garage || record.Position == null || record.Rotation == null) return 0;

			Server.SendToClient(new PlayerRestorePacket { Position = record.Position, Rotation = record.Rotation }, clientId);
			Logger.Info($"[Players] Client[{clientId}] restored to its last garage position ({record.Position.X:F2}, {record.Position.Y:F2}, {record.Position.Z:F2}).");
			return 1;
		}
	}
}
