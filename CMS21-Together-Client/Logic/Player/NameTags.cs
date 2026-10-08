using CMS21_Together_Core.Data;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public static class NameTags
{
	private const float HeadHeight = 2.0f;
	private const float SeatedHeadHeight = 1.2f;
	private const float MaxDistance = 25f;
	private const float LabelWidth = 200f;
	private const float LabelHeight = 24f;

	private static GUIStyle style;
	private static GUIStyle shadowStyle;

	public static bool IsDrawn(RemotePlayer player)
	{
		return TryGetScreenPoint(player, out Vector3 screen)
		       && screen.x >= 0f && screen.x <= Screen.width
		       && screen.y >= LabelHeight && screen.y <= Screen.height;
	}

	public static void Draw()
	{
		if (PresenceManager.Roster.Count == 0) return;
		EnsureStyles();

		foreach (var player in PresenceManager.Roster.Values)
		{
			if (!TryGetScreenPoint(player, out Vector3 screen)) continue;

			var rect = new Rect(screen.x - LabelWidth / 2f, Screen.height - screen.y - LabelHeight, LabelWidth, LabelHeight);
			var shadow = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
			GUI.Label(shadow, player.Record.Username, shadowStyle);
			GUI.Label(rect, player.Record.Username, style);
		}
	}

	private static bool TryGetScreenPoint(RemotePlayer player, out Vector3 screen)
	{
		screen = Vector3.zero;
		var camera = Camera.main;
		if (camera == null || !player.HasAvatar || !player.Avatar.gameObject.activeSelf) return false;

		float height = player.Record.SeatCarLoaderId != PlayerPresenceRecord.NoCar ? SeatPoses.SeatedAvatarDrop + SeatedHeadHeight : HeadHeight;
		Vector3 head = player.Avatar.transform.position + Vector3.up * height;
		if (Vector3.Distance(camera.transform.position, head) > MaxDistance) return false;

		screen = camera.WorldToScreenPoint(head);
		return screen.z > 0f;
	}

	private static void EnsureStyles()
	{
		if (style != null) return;
		style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
		style.normal.textColor = Color.white;
		shadowStyle = new GUIStyle(style);
		shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
	}
}
