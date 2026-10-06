using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Away;

namespace CMS21Together.Network.Handlers;

public static class TestDriveHandlers
{
	[PacketHandler(PacketTypes.CarAwayUpdate)]
	public static void OnAwayUpdate(long clientId, CarAwayUpdatePacket packet) => CarAwaySync.OnUpdate(packet);

	[PacketHandler(PacketTypes.TestDriveResultAck)]
	public static void OnResultAck(long clientId, TestDriveResultAckPacket packet) => TestDriveSync.OnResultAck(packet);
}
