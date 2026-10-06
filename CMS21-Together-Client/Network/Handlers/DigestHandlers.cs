using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Reconciliation;

namespace CMS21Together.Network.Handlers;

public static class DigestHandlers
{
	[PacketHandler(PacketTypes.StateDigestRequest)]
	public static void OnDigestRequest(long clientId, StateDigestRequestPacket packet) => ClientDigests.OnRequest(packet);

	[PacketHandler(PacketTypes.StateDetailRequest)]
	public static void OnDetailRequest(long clientId, StateDetailRequestPacket packet) => ClientDigests.OnDetailRequest(packet);

	[PacketHandler(PacketTypes.DesyncNotice)]
	public static void OnDesyncNotice(long clientId, DesyncNoticePacket packet) => ClientDigests.OnNotice(packet);
}
