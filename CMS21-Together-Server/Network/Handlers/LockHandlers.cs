using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class LockHandlers
	{
		[PacketHandler(PacketTypes.CarLockRequest)]
		public static void OnRequest(long clientId, CarLockRequestPacket packet) => CarLocks.Handle((int)clientId, packet, ServerTime.Time);

		[PacketHandler(PacketTypes.CarLockRelease)]
		public static void OnRelease(long clientId, CarLockReleasePacket packet) => CarLocks.OnRelease((int)clientId, packet);

		[PacketHandler(PacketTypes.CarLockRenew)]
		public static void OnRenew(long clientId, CarLockRenewPacket packet) => CarLocks.OnRenew((int)clientId, packet, ServerTime.Time);
	}
}
