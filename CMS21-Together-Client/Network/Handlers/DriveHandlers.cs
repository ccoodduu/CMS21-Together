using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Driving;

namespace CMS21Together.Network.Handlers;

public static class DriveHandlers
{
	[PacketHandler(PacketTypes.CarDriveStart)]
	public static void OnStart(long senderId, CarDriveStartPacket packet) => RemoteCars.OnStart(packet);

	[PacketHandler(PacketTypes.CarDriveState)]
	public static void OnState(long senderId, CarDriveStatePacket packet) => RemoteCars.OnState(packet);

	[PacketHandler(PacketTypes.CarDriveStop)]
	public static void OnStop(long senderId, CarDriveStopPacket packet) => RemoteCars.OnStop(packet);
}
