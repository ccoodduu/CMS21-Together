using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21Together.Data;
using CMS21Together.Logic.Driving;
using CMS21Together.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CMS21Together.UI;

public static class ImguiView
{
	private const float PanelWidth = 420f;
	private const float PanelHeight = 250f;
	private const float Margin = 12f;

	private static GUIStyle labelStyle;
	private static GUIStyle titleStyle;
	private static GUIStyle errorStyle;
	private static GUIStyle boxStyle;
	private static GUIStyle smallStyle;

	public static void Draw()
	{
		EnsureStyles();
		bool inMenu = SceneManager.GetActiveScene().name == "Menu";

		if (inMenu && !ConnectionStatus.IsBusy)
		{
			if (GUI.Button(MultiplayerButtonRect, "Multiplayer"))
			{
				if (MultiplayerMenuModel.Panel != MenuPanel.None) MultiplayerMenuModel.Close();
				else MultiplayerMenuModel.OpenJoinPanel();
			}
			if (MultiplayerMenuModel.JoinPanelOpen) DrawJoinPanel();
			else if (MultiplayerMenuModel.HostPanelOpen) DrawHostPanel();
			else if (MultiplayerMenuModel.FriendsPanelOpen) DrawFriendsPanel();
			if (ConnectionStatus.MessagePending) DrawMessage("Multiplayer", ConnectionStatus.Message, MultiplayerMenuModel.AcknowledgeMessage);
			else if (ModNotify.Messages.Count > 0)
			{
				var (title, text) = ModNotify.Messages.Peek();
				DrawMessage(title, text, () => ModNotify.Messages.Dequeue());
			}
		}

		if (MultiplayerMenuModel.SessionPanelOpen) DrawSessionPanel();
		if (JoinService.PendingConfirmation != null) DrawConfirm($"Leave this session and join {JoinService.PendingConfirmation}?");
		if (JoinService.PendingConfirmation != null || MultiplayerMenuModel.SessionPanelOpen) FreeCursor();
		else RestoreCursor();
		if (ConnectionStatus.IsBusy) DrawStatusOverlay();
		DrawToasts();
	}

	public static IEnumerable<Rect> Covered()
	{
		if (SceneManager.GetActiveScene().name == "Menu" && !ConnectionStatus.IsBusy)
		{
			yield return MultiplayerButtonRect;
			if (MultiplayerMenuModel.JoinPanelOpen) yield return JoinPanelRect;
			else if (MultiplayerMenuModel.HostPanelOpen) yield return HostPanelRect;
			else if (MultiplayerMenuModel.FriendsPanelOpen) yield return FriendsPanelRect;
			if (ConnectionStatus.MessagePending || ModNotify.Messages.Count > 0) yield return MessageRect;
		}
		if (MultiplayerMenuModel.SessionPanelOpen) yield return SessionPanelRect;
		if (JoinService.PendingConfirmation != null) yield return ConfirmRect;
		if (ConnectionStatus.IsBusy) yield return StatusRect;
	}

	private static Rect MultiplayerButtonRect => new Rect(Screen.width - 170f - Margin, Margin, 170f, 36f);
	private static Rect JoinPanelRect => new Rect(Screen.width - PanelWidth - Margin, Margin + 44f, PanelWidth, PanelHeight + TabsHeight + (MultiplayerMenuModel.PasswordFieldShown ? PasswordHeight : 0f));
	private static Rect FriendsPanelRect => new Rect(Screen.width - PanelWidth - Margin, Margin + 44f, PanelWidth, friendsPanelHeight);
	private static Rect HostPanelRect => new Rect(Screen.width - PanelWidth - Margin, Margin + 44f, PanelWidth, hostPanelHeight);
	private static Rect SessionPanelRect => new Rect(Margin, Margin + 44f, SessionPanelWidth, sessionPanelHeight);
	private static Rect StatusRect => new Rect((Screen.width - 460f) / 2f, Margin, 460f, 40f);
	private static Rect MessageRect => new Rect((Screen.width - 480f) / 2f, (Screen.height - 180f) / 2f, 480f, 180f);
	private static Rect ConfirmRect => new Rect((Screen.width - 480f) / 2f, (Screen.height - 160f) / 2f, 480f, 160f);

	private static void DrawJoinPanel()
	{
		bool password = MultiplayerMenuModel.PasswordFieldShown;
		var area = JoinPanelRect;
		GUI.Box(area, "", boxStyle);
		float x = area.x + 12f, y = area.y + 10f, w = area.width - 24f;

		y = DrawTabs(x, y, w);
		GUI.Label(new Rect(x, y, w, 26f), "Join a server", titleStyle);
		y += 32f;
		GUI.Label(new Rect(x, y, w, 20f), "Address (IP or IP:port) or Steam server ID", labelStyle);
		y += 22f;
		MultiplayerMenuModel.TargetText = GUI.TextField(new Rect(x, y, w, 26f), MultiplayerMenuModel.TargetText ?? "");
		y += 34f;
		if (password)
		{
			GUI.Label(new Rect(x, y, w, 20f), "Server password", labelStyle);
			y += 22f;
			MultiplayerMenuModel.PasswordText = GUI.TextField(new Rect(x, y, w, 26f), MultiplayerMenuModel.PasswordText ?? "");
			y += 34f;
		}
		GUI.Label(new Rect(x, y, w, 20f), "Your name", labelStyle);
		y += 22f;
		if (MultiplayerMenuModel.NameEditable)
			MultiplayerMenuModel.NameText = GUI.TextField(new Rect(x, y, w, 26f), MultiplayerMenuModel.NameText ?? "");
		else
			GUI.Label(new Rect(x, y, w, 26f), MultiplayerMenuModel.NameText, labelStyle);
		y += 36f;
		if (GUI.Button(new Rect(x, y, 120f, 32f), "Join")) MultiplayerMenuModel.Join();
		if (GUI.Button(new Rect(x + 130f, y, 120f, 32f), "Close")) MultiplayerMenuModel.Close();
		y += 38f;
		if (!string.IsNullOrEmpty(MultiplayerMenuModel.PanelError))
			GUI.Label(new Rect(x, y, w, 40f), MultiplayerMenuModel.PanelError, errorStyle);
	}

	private const float TabsHeight = 38f;
	private const float PasswordHeight = 56f;

	private static float DrawTabs(float x, float y, float w)
	{
		float third = (w - 16f) / 3f;
		if (GUI.Button(new Rect(x, y, third, 28f), MultiplayerMenuModel.JoinPanelOpen ? "> Join <" : "Join") && !MultiplayerMenuModel.JoinPanelOpen)
			MultiplayerMenuModel.OpenJoinPanel();
		if (GUI.Button(new Rect(x + third + 8f, y, third, 28f), MultiplayerMenuModel.HostPanelOpen ? "> Host <" : "Host") && !MultiplayerMenuModel.HostPanelOpen)
			MultiplayerMenuModel.OpenHostPanel();
		if (GUI.Button(new Rect(x + 2f * (third + 8f), y, third, 28f), MultiplayerMenuModel.FriendsPanelOpen ? "> Friends <" : "Friends") && !MultiplayerMenuModel.FriendsPanelOpen)
			MultiplayerMenuModel.OpenFriendsPanel();
		return y + TabsHeight;
	}

	private static float friendsPanelHeight = 300f;

	private static void DrawFriendsPanel()
	{
		var area = FriendsPanelRect;
		GUI.Box(area, "", boxStyle);
		float x = area.x + 12f, y = area.y + 10f, w = area.width - 24f;

		y = DrawTabs(x, y, w);
		y = DrawFriends(x, y, w);
		if (!string.IsNullOrEmpty(MultiplayerMenuModel.ActionMessage))
		{
			GUI.Label(new Rect(x, y, w, 40f), MultiplayerMenuModel.ActionMessage, errorStyle);
			y += 44f;
		}
		if (GUI.Button(new Rect(x, y, 120f, 32f), "Close")) MultiplayerMenuModel.Close();
		y += 40f;
		friendsPanelHeight = y - area.y + 6f;
	}

	private const int MaxFriendRows = 12;

	private static float DrawFriends(float x, float y, float w)
	{
		SteamFriendsList.RefreshIfDue();
		GUI.Label(new Rect(x, y, w, 26f), "Steam friends", titleStyle);
		y += 30f;
		if (!string.IsNullOrEmpty(SteamFriendsList.Status))
		{
			GUI.Label(new Rect(x, y, w, 40f), SteamFriendsList.Status, labelStyle);
			return y + 44f;
		}

		foreach (var friend in SteamFriendsList.Rows.Take(MaxFriendRows))
		{
			string status = friend.CanJoin ? "in a Together session" : friend.PlayingThisGame ? "playing CMS21" : "online";
			GUI.Label(new Rect(x, y, w - 100f, 26f), $"{friend.Name}  ({status})", labelStyle);
			if (friend.CanJoin && GUI.Button(new Rect(x + w - 90f, y, 90f, 26f), "Join")) MultiplayerMenuModel.JoinFriend(friend.Id);
			else if (!friend.CanJoin && SteamFriendsList.CanInvite && GUI.Button(new Rect(x + w - 90f, y, 90f, 26f), "Invite")) MultiplayerMenuModel.InviteFriend(friend.Id);
			y += 30f;
		}
		if (SteamFriendsList.Rows.Count > MaxFriendRows)
		{
			GUI.Label(new Rect(x, y, w, 22f), $"... and {SteamFriendsList.Rows.Count - MaxFriendRows} more", smallStyle);
			y += 24f;
		}
		return y + 4f;
	}

	private static float DrawRace(float x, float y, float w)
	{
		GUI.Label(new Rect(x, y, w, 26f), "Race", titleStyle);
		y += 30f;
		int laps = TrackRaceSync.LapsChoice;
		if (GUI.Button(new Rect(x, y, 30f, 26f), "-")) TrackRaceSync.LapsChoice = Mathf.Max(TrackRaceSync.MinLaps, laps - 1);
		GUI.Label(new Rect(x + 36f, y, 70f, 26f), laps == 1 ? "1 lap" : $"{laps} laps", labelStyle);
		if (GUI.Button(new Rect(x + 106f, y, 30f, 26f), "+")) TrackRaceSync.LapsChoice = Mathf.Min(TrackRaceSync.MaxLaps, laps + 1);
		if (GUI.Button(new Rect(x + 146f, y, 120f, 26f), "Start race")) TrackRaceSync.RequestStart(TrackRaceSync.LapsChoice);
		y += 30f;
		string status = TrackRaceSync.Status();
		if (!string.IsNullOrEmpty(status))
		{
			GUI.Label(new Rect(x, y, w, 40f), status, labelStyle);
			y += 44f;
		}
		var last = TrackRaceSync.LastResult;
		if (last != null)
		{
			GUI.Label(new Rect(x, y, w, 40f), $"Last race: {TrackRaceSync.Summary(last)}", smallStyle);
			y += 44f;
		}
		return y + 4f;
	}

	private const float SessionPanelWidth = 480f;
	private static float sessionPanelHeight = 300f;

	private static void DrawSessionPanel()
	{
		var area = SessionPanelRect;
		GUI.Box(area, "", boxStyle);
		float x = area.x + 12f, y = area.y + 10f, w = area.width - 24f;

		var rows = MultiplayerMenuModel.PlayerRows();
		string title = rows.Count == 0 ? "Not in a session" : $"Session - {ClientData.ServerInfo?.ServerName ?? JoinService.CurrentTarget?.ToString() ?? "server"}";
		GUI.Label(new Rect(x, y, w, 26f), title, titleStyle);
		y += 32f;

		bool admin = MultiplayerMenuModel.IsAdmin;
		foreach (var row in rows)
		{
			string ping = row.PingMs.HasValue ? $"{row.PingMs} ms" : "- ms";
			GUI.Label(new Rect(x, y, 190f, 26f), row.IsLocal ? $"{row.Name} (you)" : row.Name, labelStyle);
			GUI.Label(new Rect(x + 195f, y, 110f, 26f), row.Scene, labelStyle);
			GUI.Label(new Rect(x + 310f, y, 60f, 26f), ping, labelStyle);
			if (admin && !row.IsLocal && GUI.Button(new Rect(x + w - 70f, y, 70f, 26f), "Kick")) MultiplayerMenuModel.Kick(row.Id);
			y += 30f;
		}
		if (rows.Count > 0) y += 6f;

		if (TrackRaceSync.PanelShown) y = DrawRace(x, y, w);
		y = DrawFriends(x, y, w);
		if (!string.IsNullOrEmpty(MultiplayerMenuModel.ActionMessage))
		{
			GUI.Label(new Rect(x, y, w, 40f), MultiplayerMenuModel.ActionMessage, labelStyle);
			y += 44f;
		}

		if (GUI.Button(new Rect(x, y, 120f, 32f), $"Close ({PlayerSettings.SessionPanelKey})")) MultiplayerMenuModel.CloseSessionPanel();
		if (LocalServerHost.State == HostState.Running && GUI.Button(new Rect(x + 130f, y, 140f, 32f), "Stop server")) MultiplayerMenuModel.HostStop();
		y += 40f;
		sessionPanelHeight = y - area.y + 6f;
	}

	private static float hostPanelHeight = 420f;

	private static void DrawHostPanel()
	{
		var area = HostPanelRect;
		GUI.Box(area, "", boxStyle);
		float x = area.x + 12f, y = area.y + 10f, w = area.width - 24f;

		y = DrawTabs(x, y, w);
		GUI.Label(new Rect(x, y, w, 26f), "Host a session", titleStyle);
		y += 32f;
		GUI.Label(new Rect(x, y, w, 36f), $"Server: {LocalServerHost.ServerPath}", smallStyle);
		y += 40f;

		var state = LocalServerHost.State;
		if (state == HostState.Starting || state == HostState.Running || state == HostState.Stopping)
		{
			GUI.Label(new Rect(x, y, w, 40f), LocalServerHost.Message, labelStyle);
			y += 44f;
			if (state == HostState.Running)
			{
				if (GUI.Button(new Rect(x, y, 120f, 32f), "Join")) MultiplayerMenuModel.HostRejoin();
				if (GUI.Button(new Rect(x + 130f, y, 120f, 32f), "Stop")) MultiplayerMenuModel.HostStop();
			}
			else if (state == HostState.Starting && GUI.Button(new Rect(x, y, 120f, 32f), "Cancel"))
			{
				MultiplayerMenuModel.HostStop();
			}
			if (GUI.Button(new Rect(x + 260f, y, 120f, 32f), "Close")) MultiplayerMenuModel.Close();
			y += 40f;
		}
		else
		{
			y = DrawHostSettings(x, y, w);
		}

		if (!string.IsNullOrEmpty(MultiplayerMenuModel.PanelError))
		{
			GUI.Label(new Rect(x, y, w, 40f), MultiplayerMenuModel.PanelError, errorStyle);
			y += 44f;
		}
		if (state == HostState.Failed && LocalServerHost.LogTail.Count > 0)
		{
			float tailHeight = LocalServerHost.LogTail.Count * 15f + 8f;
			GUI.Box(new Rect(x, y, w, tailHeight), "", boxStyle);
			GUI.Label(new Rect(x + 4f, y + 4f, w - 8f, tailHeight - 8f), string.Join("\n", LocalServerHost.LogTail), smallStyle);
			y += tailHeight + 6f;
		}
		hostPanelHeight = y - area.y + 6f;
	}

	private static float DrawHostSettings(float x, float y, float w)
	{
		if (LocalServerHost.State == HostState.Failed || LocalServerHost.State == HostState.Idle && LocalServerHost.Message.Length > 0)
		{
			GUI.Label(new Rect(x, y, w, 40f), LocalServerHost.Message, LocalServerHost.State == HostState.Failed ? errorStyle : labelStyle);
			y += 44f;
		}

		if (MultiplayerMenuModel.StartOverPending)
		{
			GUI.Label(new Rect(x, y, w, 60f), "Start a new session? The saved session is moved to Saves_old_<date> in the server folder, not deleted.", labelStyle);
			y += 64f;
			if (GUI.Button(new Rect(x, y, 120f, 32f), "Start over")) MultiplayerMenuModel.AnswerStartOver(true);
			if (GUI.Button(new Rect(x + 130f, y, 120f, 32f), "Keep it")) MultiplayerMenuModel.AnswerStartOver(false);
			return y + 40f;
		}

		if (MultiplayerMenuModel.HostHasSave && !MultiplayerMenuModel.StartOver)
		{
			GUI.Label(new Rect(x, y, w - 130f, 32f), "Continue the saved session", labelStyle);
			if (GUI.Button(new Rect(x + w - 120f, y, 120f, 32f), "Start over")) MultiplayerMenuModel.RequestStartOver();
			y += 40f;
		}
		else
		{
			GUI.Label(new Rect(x, y, w, 22f), MultiplayerMenuModel.StartOver ? "New session (the old one is kept aside)" : "New session", labelStyle);
			y += 24f;
			float bw = (w - 16f) / 3f;
			var choices = new[] { Gamemode.Easy, Gamemode.Normal, Gamemode.Expert };
			for (int i = 0; i < choices.Length; i++)
			{
				string text = choices[i] == MultiplayerMenuModel.HostDifficulty ? $"> {choices[i]} <" : choices[i].ToString();
				if (GUI.Button(new Rect(x + i * (bw + 8f), y, bw, 30f), text)) MultiplayerMenuModel.SetHostDifficulty(choices[i]);
			}
			y += 38f;
		}

		float half = (w - 8f) / 2f;
		GUI.Label(new Rect(x, y, half, 20f), "Port", labelStyle);
		GUI.Label(new Rect(x + half + 8f, y, half, 20f), "Max players", labelStyle);
		y += 22f;
		MultiplayerMenuModel.HostPortText = GUI.TextField(new Rect(x, y, half, 26f), MultiplayerMenuModel.HostPortText ?? "");
		MultiplayerMenuModel.HostMaxPlayersText = GUI.TextField(new Rect(x + half + 8f, y, half, 26f), MultiplayerMenuModel.HostMaxPlayersText ?? "");
		y += 34f;
		GUI.Label(new Rect(x, y, w, 20f), "Password for IP joins (empty = none)", labelStyle);
		y += 22f;
		MultiplayerMenuModel.HostPasswordText = GUI.TextField(new Rect(x, y, w, 26f), MultiplayerMenuModel.HostPasswordText ?? "");
		y += 34f;
		if (GUI.Button(new Rect(x, y, w, 30f), MultiplayerMenuModel.HostSteam ? "Steam joins: on" : "Steam joins: off"))
			MultiplayerMenuModel.HostSteam = !MultiplayerMenuModel.HostSteam;
		y += 38f;

		if (GUI.Button(new Rect(x, y, 120f, 32f), "Start")) MultiplayerMenuModel.HostStart();
		if (LocalServerHost.LeftoverRunning && GUI.Button(new Rect(x + 130f, y, 120f, 32f), "Stop it")) MultiplayerMenuModel.HostStopLeftover();
		if (GUI.Button(new Rect(x + 260f, y, 120f, 32f), "Close")) MultiplayerMenuModel.Close();
		return y + 40f;
	}

	private static void DrawStatusOverlay()
	{
		string target = JoinService.CurrentTarget?.ToString() ?? "server";
		string text = ConnectionStatus.State switch
		{
			JoinStatus.Connecting => $"Connecting to {target}...",
			JoinStatus.Handshake => $"Connected to {target}, checking...",
			JoinStatus.Loading => $"Loading the garage of {ClientData.ServerInfo?.ServerName ?? target}...",
			_ => "Loading the shared garage..."
		};
		var rect = StatusRect;
		GUI.Box(rect, "", boxStyle);
		GUI.Label(rect, text, titleStyle);
	}

	private static void DrawMessage(string title, string text, System.Action onOk)
	{
		var rect = MessageRect;
		GUI.Box(rect, "", boxStyle);
		GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 28f), title, titleStyle);
		GUI.Label(new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 80f), text, labelStyle);
		if (GUI.Button(new Rect(rect.x + (rect.width - 100f) / 2f, rect.y + rect.height - 44f, 100f, 32f), "OK")) onOk();
	}

	private static (CursorLockMode Lock, bool Visible)? savedCursor;

	private static void FreeCursor()
	{
		savedCursor ??= (Cursor.lockState, Cursor.visible);
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
	}

	private static void RestoreCursor()
	{
		if (savedCursor == null) return;
		Cursor.lockState = savedCursor.Value.Lock;
		Cursor.visible = savedCursor.Value.Visible;
		savedCursor = null;
	}

	private static void DrawConfirm(string text)
	{
		var rect = ConfirmRect;
		GUI.Box(rect, "", boxStyle);
		GUI.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 70f), text, labelStyle);
		if (GUI.Button(new Rect(rect.x + rect.width / 2f - 110f, rect.y + rect.height - 44f, 100f, 32f), "Join")) JoinService.Answer(true);
		if (GUI.Button(new Rect(rect.x + rect.width / 2f + 10f, rect.y + rect.height - 44f, 100f, 32f), "Stay")) JoinService.Answer(false);
	}

	private static void DrawToasts()
	{
		float y = Screen.height - Margin - 34f;
		foreach (var toast in ModNotify.Visible())
		{
			var rect = new Rect(Margin, y, 380f, 30f);
			GUI.Box(rect, "", boxStyle);
			GUI.Label(rect, toast.Text, labelStyle);
			y -= 34f;
		}
	}

	private static void EnsureStyles()
	{
		if (labelStyle != null) return;
		labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleLeft };
		labelStyle.normal.textColor = Color.white;
		titleStyle = new GUIStyle(labelStyle) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
		smallStyle = new GUIStyle(labelStyle) { fontSize = 11, alignment = TextAnchor.UpperLeft };
		errorStyle = new GUIStyle(labelStyle);
		errorStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
		var background = new Texture2D(1, 1);
		background.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.11f, 0.92f));
		background.Apply();
		background.hideFlags = HideFlags.HideAndDontSave;
		boxStyle = new GUIStyle(GUI.skin.box);
		boxStyle.normal.background = background;
	}
}
