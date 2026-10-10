using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Tracks;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class TrackHandlers
	{
		[PacketHandler(PacketTypes.TrackRecord)]
		public static void OnRecord(long clientId, TrackRecordPacket packet) => TrackRecords.OnRecord((int)clientId, packet);
	}
}
