using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Economy;

namespace CMS21Together.Network.Handlers;

public static class EconomyHandlers
{
	[PacketHandler(PacketTypes.EconomyResult)]
	public static void OnResult(long clientId, EconomyResultPacket packet) => EconomyRequests.OnResult(packet);
}
