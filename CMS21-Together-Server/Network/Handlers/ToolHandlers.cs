using System;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Tools;
using CMS21_Together_Server.Log;

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

		[PacketHandler(PacketTypes.ToolAction)]
		public static void OnAction(long clientId, ToolActionPacket packet)
		{
			if (!Enum.IsDefined(typeof(ModToolId), packet.Tool) || ModTools.IsMachine(packet.Tool) || !Enum.IsDefined(typeof(ToolActionKind), packet.Kind)) return;
			if (CarPartsStore.Get(packet.CarLoaderID) == null) return;
			Server.SendToClients(packet, (int)clientId);
			Logger.Info($"[Tools] {packet.Tool}: {packet.Kind} on loader {packet.CarLoaderID} by client {clientId} relayed.");
		}
	}
}
