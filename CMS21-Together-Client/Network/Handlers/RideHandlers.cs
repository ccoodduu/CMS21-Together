using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Driving;

namespace CMS21Together.Network.Handlers;

public static class RideHandlers
{
	[PacketHandler(PacketTypes.RideUpdate)]
	public static void OnUpdate(long senderId, RideUpdatePacket packet) => RideAlong.OnUpdate(packet);
}
