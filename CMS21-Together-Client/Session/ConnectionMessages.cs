using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Session;

public static class ConnectionMessages
{
	public static string For(JoinFailure failure, DisconnectReason reason, string detail)
	{
		string text = failure switch
		{
			JoinFailure.Unreachable => "Could not reach the server. Check the address and that the server is running.",
			JoinFailure.Timeout => "The server did not answer in time.",
			JoinFailure.SteamUnavailable => "Steam is not available, so Steam joins do not work. Join by IP instead.",
			JoinFailure.SteamFailed => "The Steam connection failed.",
			_ => ForServerReason(reason)
		};
		return string.IsNullOrWhiteSpace(detail) ? text : $"{text} {detail}";
	}

	private static string ForServerReason(DisconnectReason reason)
	{
		switch (reason)
		{
			case DisconnectReason.ServerShutdown: return "The server was shut down.";
			case DisconnectReason.Kicked: return "You were kicked.";
			case DisconnectReason.VersionMismatch: return "The server runs another version of Together.";
			case DisconnectReason.DuplicateIdentity: return "You are already connected to this server from another game.";
			case DisconnectReason.MissingIdentity: return "The server could not identify you.";
			case DisconnectReason.SyncFailed: return "Loading the shared garage failed.";
			case DisconnectReason.ServerFull: return "The server is full.";
			case DisconnectReason.WrongPassword: return "Wrong server password.";
			default: return "Disconnected from the server.";
		}
	}
}
