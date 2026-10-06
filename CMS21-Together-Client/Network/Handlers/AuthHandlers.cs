using System.Net;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Session;
using UnityEngine;

namespace CMS21Together.Network.Handlers;

public static class AuthHandler
{
	[PacketHandler(PacketTypes.Heartbeat)]
	public static void HandleHeartbeat(long senderId, HeartbeatPacket packet)
	{
		if (!Client.Instance.IsConnectionValid)
			Client.Instance.OnConnectionValidated.Invoke();
		Client.Instance.Send(new HeartbeatPacket(), false);
	}
	
	[PacketHandler(PacketTypes.Connect)]
	public static void HandleConnect(long senderId, ConnectPacket packet)
	{
		ConnectionStatus.Set(JoinStatus.Handshake);
		Log.Info($"Server compatible with mod version {packet.modVersion}");
		Log.Info($"Received message from server: {packet.message}");
		Client.Instance.ID = packet.playerID;
		if (Client.Instance.NetworkType == NetworkType.DirectIP)
		{
			Client.Instance.UDP.Connect();
			Client.Instance.Send(new ConnectPacket()
			{
				gameVersion = "",
				message = "",
				modVersion = ClientVersion.Current,
				playerID = Client.Instance.ID,
				username = PlayerSettings.PlayerName
			});
		}
	}
	
	[PacketHandler(PacketTypes.ServerInfo)]
	public static void HandleServerInfo(long senderId, ServerInfoPacket packet)
	{
		ClientData.ServerInfo = packet;
		Log.Info($"[Join] Server '{packet.ServerName}', Together {packet.ModVersion}, {packet.MaxPlayers} players, difficulty {packet.Difficulty}.");
	}

	[PacketHandler(PacketTypes.Disconnect)]
	public static void HandleDisconnect(long senderId, DisconnectPacket packet)
	{
		if (packet.playerID == Client.Instance.ID || packet.playerID == -1)
		{
			Log.Info($"[Received From Server] Disconnected from server ({packet.reason}): {packet.message}");
			if (!ConnectionStatus.Fail(JoinFailure.Server, packet.message, packet.reason))
				JoinService.ResetAfterFailure();
		}
		else
		{
			PresenceManager.Remove(packet.playerID);
		}
	}
}