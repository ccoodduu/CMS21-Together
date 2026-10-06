using CMS21Together.Logic.Player;
using UnityEngine;

namespace CMS21Together.Logic.Car.Away;

public static class AwayLabels
{
	private const float RoofHeight = 1.8f;
	private const float MaxDistance = 25f;
	private const float LabelWidth = 320f;
	private const float LabelHeight = 24f;

	private static GUIStyle style;
	private static GUIStyle shadowStyle;

	public static void Draw()
	{
		if (CarAwaySync.All.Count == 0) return;
		var camera = Camera.main;
		var places = CarLoaderPlaces.Get();
		if (camera == null || places == null) return;
		EnsureStyles();

		foreach (var pair in CarAwaySync.All)
		{
			if (!CarAwaySync.LockedForMe(pair.Key, out int owner, out var kind)) continue;
			var carLoader = places.GetCarLoaderByIndex(pair.Key);
			if (carLoader == null || !carLoader.IsCarLoaded()) continue;
			Vector3 roof = carLoader.transform.position + Vector3.up * RoofHeight;
			if (Vector3.Distance(camera.transform.position, roof) > MaxDistance) continue;
			Vector3 screen = camera.WorldToScreenPoint(roof);
			if (screen.z <= 0f) continue;

			string text = $"{CarAwaySync.OwnerName(owner)} — {CarAwaySync.Label(kind)}";
			var rect = new Rect(screen.x - LabelWidth / 2f, Screen.height - screen.y - LabelHeight, LabelWidth, LabelHeight);
			GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, shadowStyle);
			GUI.Label(rect, text, style);
		}
	}

	private static void EnsureStyles()
	{
		if (style != null) return;
		style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
		style.normal.textColor = new Color(1f, 0.85f, 0.4f);
		shadowStyle = new GUIStyle(style);
		shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
	}
}
