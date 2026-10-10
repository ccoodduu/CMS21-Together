using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Tracks;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class RaceHandlers
	{
		[PacketHandler(PacketTypes.RaceStartRequest)]
		public static void OnStart(long clientId, RaceStartRequestPacket packet) => TrackRaces.OnStart((int)clientId, packet, ServerTime.Time);

		[PacketHandler(PacketTypes.RaceLap)]
		public static void OnLap(long clientId, RaceLapPacket packet) => TrackRaces.OnLap((int)clientId, packet, ServerTime.Time);

		[PacketHandler(PacketTypes.RaceQuit)]
		public static void OnQuit(long clientId, RaceQuitPacket packet) => TrackRaces.OnQuit((int)clientId, packet);
	}
}
