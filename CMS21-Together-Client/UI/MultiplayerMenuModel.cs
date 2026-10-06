using CMS21_Together_Core.Data.Enum;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.Session;
using UnityEngine;

namespace CMS21Together.UI;

public enum MenuPanel
{
	None,
	Join,
	Host
}

public static class MultiplayerMenuModel
{
	private const float SaveCheckInterval = 1f;

	public static MenuPanel Panel { get; private set; }
	public static bool JoinPanelOpen => Panel == MenuPanel.Join;
	public static bool HostPanelOpen => Panel == MenuPanel.Host;

	public static string TargetText { get; set; } = "";
	public static string NameText { get; set; } = "";
	public static string PanelError { get; private set; } = "";

	public static string HostPortText { get; set; } = MainMod.PORT.ToString();
	public static string HostMaxPlayersText { get; set; } = MainMod.MAX_PLAYER.ToString();
	public static string HostPasswordText { get; set; } = "";
	public static bool HostSteam { get; set; } = true;
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

	public static void OpenJoinPanel()
	{
		TargetText = PlayerSettings.LastJoinTarget;
		NameText = PlayerSettings.PlayerName;
		PanelError = "";
		Panel = MenuPanel.Join;
	}

	public static void OpenHostPanel()
	{
		HostSteam = MainMod.IsSteamAvailable;
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

		if (JoinService.Join(TargetText, out string error))
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
		ConnectionStatus.MessagePending = false;
		ConnectionStatus.Set(JoinStatus.Idle);
	}
}
