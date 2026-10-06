using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class TestDriveHandlers
	{
		[PacketHandler(PacketTypes.CarAwayRequest)]
		public static void OnRequest(long clientId, CarAwayRequestPacket packet) => CarAwayRegistry.OnRequest((int)clientId, packet, ServerTime.Time);

		[PacketHandler(PacketTypes.CarAwayRelease)]
		public static void OnRelease(long clientId, CarAwayReleasePacket packet) => CarAwayRegistry.OnRelease((int)clientId, packet);

		[PacketHandler(PacketTypes.TestDriveResult)]
		public static void OnResult(long clientId, TestDriveResultPacket packet)
		{
			bool applied = CarDetailsStore.FoldTestDrive((int)clientId, packet);
			Server.SendToClient(new TestDriveResultAckPacket { CarLoaderID = packet.CarLoaderID, Applied = applied }, (int)clientId);
		}
	}
}
