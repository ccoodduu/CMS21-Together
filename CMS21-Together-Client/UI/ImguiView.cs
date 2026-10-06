using CMS21Together.Data;
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

	public static void Draw()
	{
		EnsureStyles();
		bool inMenu = SceneManager.GetActiveScene().name == "Menu";

		if (inMenu && !ConnectionStatus.IsBusy)
		{
			if (GUI.Button(new Rect(Screen.width - 170f - Margin, Margin, 170f, 36f), "Multiplayer"))
			{
				if (MultiplayerMenuModel.JoinPanelOpen) MultiplayerMenuModel.Close();
				else MultiplayerMenuModel.OpenJoinPanel();
			}
			if (MultiplayerMenuModel.JoinPanelOpen) DrawJoinPanel();
			if (ConnectionStatus.MessagePending) DrawMessage("Multiplayer", ConnectionStatus.Message, MultiplayerMenuModel.AcknowledgeMessage);
			else if (ModNotify.Messages.Count > 0)
			{
				var (title, text) = ModNotify.Messages.Peek();
				DrawMessage(title, text, () => ModNotify.Messages.Dequeue());
			}
		}

		if (ConnectionStatus.IsBusy) DrawStatusOverlay();
		DrawToasts();
	}

	private static void DrawJoinPanel()
	{
		var area = new Rect(Screen.width - PanelWidth - Margin, Margin + 44f, PanelWidth, PanelHeight);
		GUI.Box(area, "", boxStyle);
		float x = area.x + 12f, y = area.y + 10f, w = area.width - 24f;

		GUI.Label(new Rect(x, y, w, 26f), "Join a server", titleStyle);
		y += 32f;
		GUI.Label(new Rect(x, y, w, 20f), "Address (IP or IP:port) or Steam server ID", labelStyle);
		y += 22f;
		MultiplayerMenuModel.TargetText = GUI.TextField(new Rect(x, y, w, 26f), MultiplayerMenuModel.TargetText ?? "");
		y += 34f;
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
		var rect = new Rect((Screen.width - 460f) / 2f, Margin, 460f, 40f);
		GUI.Box(rect, "", boxStyle);
		GUI.Label(rect, text, titleStyle);
	}

	private static void DrawMessage(string title, string text, System.Action onOk)
	{
		var rect = new Rect((Screen.width - 480f) / 2f, (Screen.height - 180f) / 2f, 480f, 180f);
		GUI.Box(rect, "", boxStyle);
		GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 28f), title, titleStyle);
		GUI.Label(new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 80f), text, labelStyle);
		if (GUI.Button(new Rect(rect.x + (rect.width - 100f) / 2f, rect.y + rect.height - 44f, 100f, 32f), "OK")) onOk();
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
