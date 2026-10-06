using System.Collections.Generic;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Compatibility;
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
		Log.Info($"Server runs Together {packet.modVersion}, protocol {packet.protocolHash ?? "none"}: {packet.message}");

		string mismatch = WelcomeMismatch(packet);
		if (mismatch != null)
		{
			Log.Warn($"[Compat] Not joining: {mismatch}");
			ConnectionStatus.Fail(JoinFailure.Server, mismatch, DisconnectReason.VersionMismatch);
			return;
		}

		Client.Instance.ID = packet.playerID;
		if (Client.Instance.NetworkType == NetworkType.DirectIP)
			Client.Instance.UDP.Connect();
		Client.Instance.Send(ConnectPacketFactory.Build());
	}

	private static string WelcomeMismatch(ConnectPacket welcome)
	{
		if (welcome.modVersion != ClientVersion.Current)
			return $"This server runs Together {welcome.modVersion}; you have {ClientVersion.Current}.";
		if (welcome.protocolHash != LocalEnvironment.ProtocolHashValue)
			return $"You and the server both run Together {welcome.modVersion}, but the builds differ (server protocol {welcome.protocolHash ?? "none"}, yours {LocalEnvironment.ProtocolHashValue}). Install the host's build.";
		return null;
	}

	[PacketHandler(PacketTypes.ServerInfo)]
	public static void HandleServerInfo(long senderId, ServerInfoPacket packet)
	{
		ClientData.ServerInfo = packet;
		Log.Info($"[Join] Server '{packet.ServerName}', Together {packet.ModVersion}, {packet.MaxPlayers} players, difficulty {packet.Difficulty}, " +
		         $"shared DLC [{string.Join(", ", packet.SharedDlc ?? new List<string>())}].");
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