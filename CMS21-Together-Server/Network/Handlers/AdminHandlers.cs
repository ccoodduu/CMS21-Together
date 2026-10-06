using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers;

public static class AdminHandlers
{
	[PacketHandler(PacketTypes.KickRequest)]
	public static void OnKickRequest(long clientId, KickRequestPacket packet)
	{
		var sender = Server.Clients[(int)clientId];
		string refusal = null;
		if (!sender.IsAdmin) refusal = "not an admin";
		else if (sender.SyncState != SyncState.InSession) refusal = "not in session";
		else if (packet.PlayerId == sender.ID) refusal = "cannot kick itself";

		if (refusal != null)
		{
			Logger.Warn($"Kick request from client {sender.ID} for player {packet.PlayerId} refused: {refusal}.");
			return;
		}
		Server.Kick(packet.PlayerId, $"requested by admin client {sender.ID}");
	}
}
