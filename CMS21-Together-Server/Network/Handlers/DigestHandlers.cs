using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Reconciliation;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class DigestHandlers
	{
		[PacketHandler(PacketTypes.StateDigest)]
		public static void OnDigest(long clientId, StateDigestPacket packet) => ReconciliationService.OnDigest((int)clientId, packet, ServerTime.Time);

		[PacketHandler(PacketTypes.StateDetail)]
		public static void OnDetail(long clientId, StateDetailPacket packet) => ReconciliationService.OnDetail((int)clientId, packet, ServerTime.Time);
	}
}
