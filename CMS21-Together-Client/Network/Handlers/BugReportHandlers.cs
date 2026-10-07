using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Diagnostics;

namespace CMS21Together.Network.Handlers;

public static class BugReportHandlers
{
	[PacketHandler(PacketTypes.BugReportCollect)]
	public static void OnCollect(long clientId, BugReportCollectPacket packet) => BugReport.OnCollect(packet);

	[PacketHandler(PacketTypes.BugReportResult)]
	public static void OnResult(long clientId, BugReportResultPacket packet) => BugReport.OnResult(packet);
}
