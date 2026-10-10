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
			if (record == null) return 0;
			bool position = record.Scene == GameScene.Garage && record.Position != null && record.Rotation != null;
			bool records = record.BestLapMs > 0 || record.TopSpeedKmh > 0;
			if (!position && !records) return 0;

			Server.SendToClient(new PlayerRestorePacket
			{
				Position = position ? record.Position : null, Rotation = position ? record.Rotation : null,
				BestLapMs = record.BestLapMs, TopSpeedKmh = record.TopSpeedKmh,
			}, clientId);
			if (position) Logger.Info($"[Players] Client[{clientId}] restored to its last garage position {PlayerRecords.Format(record.Position)}.");
			if (records) Logger.Info($"[Players] Client[{clientId}] gets its track records: best lap {record.BestLapMs} ms, top speed {record.TopSpeedKmh} km/h.");
			return 1;
		}
	}
}
