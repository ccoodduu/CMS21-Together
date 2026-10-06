using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Jobs;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class JobHandlers
	{
		[PacketHandler(PacketTypes.OrderGenerated)]
		public static void OnOrderGenerated(long clientId, OrderGeneratedPacket packet) => JobsService.OnOrderGenerated((int)clientId, packet);

		[PacketHandler(PacketTypes.OrderAction)]
		public static void OnOrderAction(long clientId, OrderActionPacket packet) => JobsService.OnOrderAction((int)clientId, packet, ServerTime.Time);

		[PacketHandler(PacketTypes.JobStarted)]
		public static void OnJobStarted(long clientId, JobStartedPacket packet) => JobsService.OnJobStarted((int)clientId, packet);

		[PacketHandler(PacketTypes.JobEndRequest)]
		public static void OnJobEnd(long clientId, JobEndRequestPacket packet) => JobsService.OnJobEnd((int)clientId, packet);
	}
}
