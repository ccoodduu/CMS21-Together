using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.Session;
using UnityEngine;

namespace CMS21Together.UI;

public enum MenuPanel
{
	None,
	Join,
	Host,
	Friends
}

public class PlayerRow
{
	public int Id;
	public string Name;
	public string Scene;
	public int? PingMs;
	public bool IsLocal;
}

public static class MultiplayerMenuModel
{
	private const float SaveCheckInterval = 1f;

	public static MenuPanel Panel { get; private set; }
	public static bool JoinPanelOpen => Panel == MenuPanel.Join;
	public static bool HostPanelOpen => Panel == MenuPanel.Host;
	public static bool FriendsPanelOpen => Panel == MenuPanel.Friends;
	public static bool SessionPanelOpen { get; private set; }
	public static string ActionMessage { get; private set; } = "";

	public static string TargetText { get; set; } = "";
	public static string NameText { get; set; } = "";
	public static string PanelError { get; private set; } = "";

	public static string HostPortText { get; set; } = MainMod.PORT.ToString();
	public static string HostMaxPlayersText { get; set; } = MainMod.MAX_PLAYER.ToString();
	public static string HostPasswordText { get; set; } = "";
	public static bool HostSteam { get; set; } = MainMod.IsSteamAvailable;
	public static Gamemode HostDifficulty { get; private set; } = Gamemode.Normal;
	public static bool StartOver { get; private set; }
	public static bool StartOverPending { get; private set; }

	private static bool hasSave;
	private static float nextSaveCheck;

	public static bool NameEditable => !Client.Instance.IsConnected;

	public static bool HostHasSave
	{
		get
		{
			if (Time.realtimeSinceStartup >= nextSaveCheck)
			{
				nextSaveCheck = Time.realtimeSinceStartup + SaveCheckInterval;
				hasSave = LocalServerHost.HasSave(LocalServerHost.ServerPath);
			}
			return hasSave;
		}
	}

	public static string PasswordText { get; set; } = "";

	public static bool PasswordFieldShown =>
		(ConnectionStatus.Failure == JoinFailure.Server && ConnectionStatus.ServerReason == DisconnectReason.WrongPassword) || !string.IsNullOrEmpty(PasswordText);

	public static void OpenJoinPanel(string target = null)
	{
		TargetText = target ?? PlayerSettings.LastJoinTarget;
		NameText = PlayerSettings.PlayerName;
		PasswordText = JoinTarget.TryParse(TargetText, MainMod.PORT, out var parsed, out _) ? JoinService.RememberedPassword(parsed) : "";
		PanelError = "";
		Panel = MenuPanel.Join;
	}

	public static void OpenFriendsPanel()
	{
		PanelError = "";
		SteamFriendsList.Refresh();
		Panel = MenuPanel.Friends;
	}

	public static void OpenHostPanel()
	{
		StartOver = false;
		StartOverPending = false;
		PanelError = "";
		nextSaveCheck = 0f;
		Panel = MenuPanel.Host;
	}

	public static void Close()
	{
		Panel = MenuPanel.None;
		StartOverPending = false;
	}

	public static void Join()
	{
		if (NameEditable && NameText != PlayerSettings.PlayerName) PlayerSettings.PlayerName = NameText.Trim();

		if (JoinService.Join(TargetText, out string error, PasswordFieldShown ? PasswordText ?? "" : null))
		{
			PanelError = "";
			Panel = MenuPanel.None;
		}
		else
		{
			PanelError = error;
		}
	}

	public static void SetHostDifficulty(Gamemode difficulty) => HostDifficulty = difficulty;

	public static void RequestStartOver() => StartOverPending = true;

	public static void AnswerStartOver(bool confirmed)
	{
		StartOverPending = false;
		StartOver = confirmed;
	}

	public static void HostStart()
	{
		if (!int.TryParse(HostPortText?.Trim(), out int port))
		{
			PanelError = $"'{HostPortText}' is not a valid port.";
			return;
		}
		if (!int.TryParse(HostMaxPlayersText?.Trim(), out int maxPlayers))
		{
			PanelError = $"'{HostMaxPlayersText}' is not a number of players.";
			return;
		}

		var settings = new HostSettings
		{
			Port = port,
			MaxPlayers = maxPlayers,
			Password = HostPasswordText ?? "",
			UseSteam = HostSteam,
			Difficulty = HostDifficulty,
			StartOver = StartOver
		};
		PanelError = "";
		LocalServerHost.Start(settings, out _);
		StartOver = false;
		nextSaveCheck = 0f;
	}

	public static void HostStop() => LocalServerHost.Stop();

	public static void HostStopLeftover() => LocalServerHost.StopLeftover();

	public static void HostRejoin() => LocalServerHost.Rejoin();

	public static void AcknowledgeMessage()
	{
		bool wrongPassword = ConnectionStatus.Failure == JoinFailure.Server && ConnectionStatus.ServerReason == DisconnectReason.WrongPassword;
		ConnectionStatus.MessagePending = false;
		ConnectionStatus.Set(JoinStatus.Idle);
		if (wrongPassword) OpenJoinPanel(JoinService.CurrentTarget?.ToString());
	}

	public static bool IsAdmin => ClientData.ServerInfo != null && ClientData.ServerInfo.IsAdmin && ConnectionStatus.State == JoinStatus.InSession;

	public static void ToggleSessionPanel()
	{
		if (SessionPanelOpen) CloseSessionPanel();
		else OpenSessionPanel();
	}

	public static void OpenSessionPanel()
	{
		ActionMessage = "";
		SteamFriendsList.Refresh();
		SessionPanelOpen = true;
	}

	public static void CloseSessionPanel() => SessionPanelOpen = false;

	public static List<PlayerRow> PlayerRows()
	{
		var rows = new List<PlayerRow>();
		if (Client.Instance == null || !Client.Instance.IsConnectionValid) return rows;

		rows.Add(new PlayerRow
		{
			Id = Client.Instance.ID,
			Name = PlayerSettings.PlayerName,
			Scene = ClientScene.LocalScene.ToString(),
			PingMs = Ping(Client.Instance.ID),
			IsLocal = true
		});
		foreach (var player in PresenceManager.Roster.Values.OrderBy(p => p.Record.PlayerId))
		{
			rows.Add(new PlayerRow
			{
				Id = player.Record.PlayerId,
				Name = player.Record.Username,
				Scene = player.Record.Scene.ToString(),
				PingMs = Ping(player.Record.PlayerId)
			});
		}
		return rows;
	}

	private static int? Ping(int playerId) => ClientData.PlayerPings.TryGetValue(playerId, out int ms) ? ms : (int?)null;

	public static void Kick(int playerId)
	{
		if (!IsAdmin || playerId == Client.Instance.ID) return;
		Client.Instance.Send(new KickRequestPacket { PlayerId = playerId });
		ActionMessage = $"Kick requested for player {playerId}.";
	}

	public static void JoinFriend(ulong friendId)
	{
		ActionMessage = SteamFriendsList.Join(friendId, out string error) ? "" : error;
		if (string.IsNullOrEmpty(ActionMessage)) CloseSessionPanel();
	}

	public static void InviteFriend(ulong friendId)
	{
		ActionMessage = SteamFriendsList.Invite(friendId, out string error) ? "Invite sent." : error;
	}
}
