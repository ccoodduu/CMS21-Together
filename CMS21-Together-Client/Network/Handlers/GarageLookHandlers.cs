using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Garage;

namespace CMS21Together.Network.Handlers;

public static class GarageLookHandlers
{
	[PacketHandler(PacketTypes.GarageLookUpdate)]
	public static void OnUpdate(long senderId, GarageLookUpdatePacket packet) => ClientScene.GarageBound(() => GarageLookSync.Receive(packet.Look));

	[PacketHandler(PacketTypes.GarageLookClaimResult)]
	public static void OnClaimResult(long senderId, GarageLookClaimResultPacket packet) => GarageLookSync.OnClaimResult(packet);
}
