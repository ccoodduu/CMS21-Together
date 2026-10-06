using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Diagnostics;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class BugReportHandlers
	{
		[PacketHandler(PacketTypes.BugReportRequest)]
		public static void OnRequest(long clientId, BugReportRequestPacket packet) => BugReportWriter.OnRequest((int)clientId, packet, ServerTime.Time);
	}
}
