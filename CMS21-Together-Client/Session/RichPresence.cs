using System;
using System.Net;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using Steamworks;

namespace CMS21Together.Session;

public static class RichPresence
{
	public static string JoinString { get; private set; }
	public static string Reason { get; private set; } = "not in a session";
	public static string StatusText { get; private set; }

	public static (string JoinString, string Reason) Build(ServerInfoPacket info, JoinTarget target)
	{
		if (info != null && info.SteamId != 0)
			return (JoinTarget.Steam(info.SteamId).ToJoinString(), null);

		int port = info?.Port ?? target?.Port ?? MainMod.PORT;
		if (info != null && !string.IsNullOrWhiteSpace(info.PublicAddress))
			return (JoinTarget.Ip(info.PublicAddress.Trim(), port).ToJoinString(), null);

		if (target == null) return (null, "unknown server address");
		if (target.Kind == JoinTargetKind.Steam) return (target.ToJoinString(), null);
		if (IsLoopback(target.Host)) return (null, "loopback");
		if (IsPrivate(target.Host)) return (null, "private address");
		return (JoinTarget.Ip(target.Host, port).ToJoinString(), null);
	}

	public static void Publish()
	{
		if (ConnectionStatus.State != JoinStatus.InSession) return;

		(JoinString, Reason) = Build(ClientData.ServerInfo, JoinService.CurrentTarget);
		int players = PresenceManager.Roster.Count + 1;
		string server = ClientData.ServerInfo?.ServerName ?? JoinService.CurrentTarget?.ToString() ?? "a server";
		StatusText = $"Together - {server} ({players}/{ClientData.ServerInfo?.MaxPlayers ?? MainMod.MAX_PLAYER})";
		Log.Info($"[Presence] Rich presence: {StatusText}, join {JoinString ?? $"none ({Reason})"}");

		if (!MainMod.IsSteamAvailable || !PlayerSettings.AdvertisePresence) return;
		try
		{
			SteamFriends.SetRichPresence("status", StatusText);
			if (JoinString != null) SteamFriends.SetRichPresence("connect", JoinString);
			else SteamFriends.SetRichPresence("connect", null);
		}
		catch (Exception ex)
		{
			Log.Warn($"[Presence] Could not set rich presence: {ex.Message}");
		}
	}

	public static void Clear()
	{
		JoinString = null;
		StatusText = null;
		Reason = "not in a session";
		if (!MainMod.IsSteamAvailable) return;
		try
		{
			SteamFriends.ClearRichPresence();
		}
		catch (Exception ex)
		{
			Log.Warn($"[Presence] Could not clear rich presence: {ex.Message}");
		}
	}

	private static bool IsLoopback(string host) =>
		host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

	private static bool IsPrivate(string host)
	{
		if (!IPAddress.TryParse(host, out var ip)) return false;
		byte[] b = ip.GetAddressBytes();
		return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
	}
}
