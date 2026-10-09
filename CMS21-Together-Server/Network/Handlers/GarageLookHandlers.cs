using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Garage;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class GarageLookHandlers
	{
		[PacketHandler(PacketTypes.GarageLookClaim)]
		public static void OnClaim(long clientId, GarageLookClaimPacket packet) => GarageLookService.OnClaim((int)clientId, packet);

		[PacketHandler(PacketTypes.GarageLookUpdate)]
		public static void OnUpdate(long clientId, GarageLookUpdatePacket packet) => GarageLookService.OnUpdate((int)clientId, packet);
	}
}
