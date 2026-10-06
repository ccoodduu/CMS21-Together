using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Tools;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class ToolHandlers
	{
		[PacketHandler(PacketTypes.ToolSlotUpdate)]
		public static void OnSlotUpdate(long clientId, ToolSlotUpdatePacket packet) => ToolsStore.OnSlotUpdate((int)clientId, packet);

		[PacketHandler(PacketTypes.ToolSlotProperty)]
		public static void OnProperty(long clientId, ToolSlotPropertyPacket packet) => ToolsStore.OnProperty((int)clientId, packet);

		[PacketHandler(PacketTypes.ToolPosition)]
		public static void OnPosition(long clientId, ToolPositionPacket packet) => ToolsStore.OnPosition((int)clientId, packet);

		[PacketHandler(PacketTypes.ToolPartChange)]
		public static void OnPartChange(long clientId, ToolPartChangePacket packet) => ToolsStore.OnPartChange((int)clientId, packet);

		[PacketHandler(PacketTypes.ToolClaim)]
		public static void OnClaim(long clientId, ToolClaimPacket packet) => ToolsStore.OnClaim((int)clientId, packet);
	}
}
