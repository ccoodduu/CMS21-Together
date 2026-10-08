using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Pings;

namespace CMS21Together.Network.Handlers;

public static class PingHandlers
{
	[PacketHandler(PacketTypes.CoopPing)]
	public static void OnCoopPing(long senderId, CoopPingPacket packet) => CoopPings.OnPacket(packet);
}
