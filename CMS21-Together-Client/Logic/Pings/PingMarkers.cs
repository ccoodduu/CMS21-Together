using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Visuals;
using UnityEngine;

namespace CMS21Together.Logic.Pings;

public class PingMarker
{
	public int PlayerId;
	public string Name;
	public bool Own;
	public int Loader = CoopPingPacket.NoCar;
	public string Key;
	public bool Resolved;
	public Vector3 Fallback;
	public Transform Anchor;
	public List<Renderer> Renderers = new List<Renderer>();
	public PartScript Flashed;
	public float ShownAt;
	public float ExpiresAt;

	public bool IsPart => Key != null;
	public float Remaining => ExpiresAt - Time.realtimeSinceStartup;
}

public static class PingMarkers
{
	public const float Seconds = 5f;
	public const float FadeSeconds = 1f;
	public const string Sound = "Popup";
	private const float MinBoxPixels = 28f;
	private const float BoxPadding = 6f;
	private const float SpotPixels = 22f;
	private const float EdgeMargin = 40f;
	private const float LabelWidth = 240f;
	private const float LabelHeight = 22f;
	private const float Line = 2f;

	private static readonly List<PingMarker> markers = new List<PingMarker>();
	private static GUIStyle style;
	private static GUIStyle shadowStyle;

	public static IReadOnlyList<PingMarker> All => markers;

	public static readonly Color OwnColor = new Color(0.35f, 0.85f, 1f);
	public static readonly Color OtherColor = new Color(1f, 0.6f, 0.1f);

	public static void Clear()
	{
		foreach (var marker in markers) StopFlash(marker);
		markers.Clear();
	}

	public static void RemovePlayer(int playerId)
	{
		foreach (var marker in markers.Where(m => m.PlayerId == playerId)) StopFlash(marker);
		markers.RemoveAll(m => m.PlayerId == playerId);
	}

	public static PingMarker Show(int playerId, string name, bool own, CoopPingPacket packet)
	{
		RemovePlayer(playerId);
		float now = Time.realtimeSinceStartup;
		var marker = new PingMarker
		{
			PlayerId = playerId, Name = name, Own = own, ShownAt = now, ExpiresAt = now + Seconds,
			Fallback = new Vector3(packet.Position.X, packet.Position.Y, packet.Position.Z),
		};
		if (packet.IsPart)
		{
			marker.Loader = packet.CarLoaderID;
			marker.Key = packet.PartKey;
			Resolve(marker);
		}
		markers.Add(marker);
		if (!own) PlaySound();
		return marker;
	}

	private static void Resolve(PingMarker marker)
	{
		var registry = VisualScope.RegistryOf(marker.Loader);
		if (registry == null) return;
		if (marker.Key.StartsWith("s:", StringComparison.Ordinal))
		{
			var script = registry.Sub(marker.Key);
			if (script == null || script.IsUnmounted) return;
			marker.Anchor = script.transform;
			marker.Renderers = PartGhosts.PartRenderers(script);
			StartFlash(marker, script);
		}
		else
		{
			var part = registry.Body(marker.Key);
			if (part?.handle == null || part.Unmounted) return;
			marker.Anchor = part.handle.transform;
			marker.Renderers = PartGhosts.VisibleRenderers(marker.Anchor);
		}
		marker.Resolved = true;
	}

	private static void StartFlash(PingMarker marker, PartScript script)
	{
		if (script.ho == null) return;
		try
		{
			script.ho.FlashingOn(marker.Own ? OwnColor : OtherColor);
			marker.Flashed = script;
			CoopPings.Count("flashes");
		}
		catch (Exception ex)
		{
			Log.Warn($"[Ping] Flashing {marker.Key} failed: {ex.Message}");
		}
	}

	private static void StopFlash(PingMarker marker)
	{
		var script = marker.Flashed;
		marker.Flashed = null;
		if (script == null || !script || script.ho == null) return;
		try
		{
			script.ho.FlashingOff();
		}
		catch (Exception ex)
		{
			Log.Warn($"[Ping] Stopping the flash of {marker.Key} failed: {ex.Message}");
		}
	}

	private static void PlaySound()
	{
		try
		{
			SoundManager.Get()?.PlaySFXOneShot(Sound);
			CoopPings.Count("sounds");
		}
		catch (Exception ex)
		{
			Log.Warn($"[Ping] Sound {Sound} failed: {ex.Message}");
		}
	}

	public static void Update()
	{
		if (markers.Count == 0) return;
		float now = Time.realtimeSinceStartup;
		foreach (var marker in markers.Where(m => m.ExpiresAt <= now)) StopFlash(marker);
		int removed = markers.RemoveAll(m => m.ExpiresAt <= now);
		for (int i = 0; i < removed; i++) CoopPings.Count("expired");
	}

	public static Vector3 Center(List<Renderer> renderers, Vector3 fallback) =>
		TryBounds(renderers, out var bounds) ? bounds.center : fallback;

	private static bool TryBounds(List<Renderer> renderers, out Bounds bounds)
	{
		bounds = default;
		bool any = false;
		foreach (var renderer in renderers)
		{
			if (renderer == null || !renderer || !TryMeshBounds(renderer, out var b)) continue;
			if (any) bounds.Encapsulate(b);
			else bounds = b;
			any = true;
		}
		return any;
	}

	// Renderer.bounds is empty while the game culls a renderer, which it does behind walls.
	private static bool TryMeshBounds(Renderer renderer, out Bounds bounds)
	{
		bounds = default;
		var filter = renderer.GetComponent<MeshFilter>();
		var mesh = filter == null ? null : filter.sharedMesh;
		if (mesh == null)
		{
			bounds = renderer.bounds;
			return bounds.extents != Vector3.zero;
		}
		var local = mesh.bounds;
		var transform = renderer.transform;
		var min = local.min;
		var max = local.max;
		for (int i = 0; i < 8; i++)
		{
			var corner = transform.TransformPoint(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
			if (i == 0) bounds = new Bounds(corner, Vector3.zero);
			else bounds.Encapsulate(corner);
		}
		return bounds.extents != Vector3.zero;
	}

	public static Vector3 WorldPoint(PingMarker marker) => WorldBounds(marker, out var bounds) ? bounds.center : marker.Fallback;

	private static bool WorldBounds(PingMarker marker, out Bounds bounds)
	{
		bounds = default;
		if (!marker.Resolved) return false;
		if (TryBounds(marker.Renderers, out bounds)) return true;
		if (marker.Anchor == null || !marker.Anchor) return false;
		bounds = new Bounds(marker.Anchor.position, Vector3.zero);
		return true;
	}

	public static bool TryScreenRect(PingMarker marker, Camera camera, out Rect rect, out bool onScreen)
	{
		rect = default;
		onScreen = false;
		if (camera == null) return false;
		bool part = WorldBounds(marker, out var bounds);
		Vector3 center = camera.WorldToScreenPoint(part ? bounds.center : marker.Fallback);

		if (center.z > 0f && part && bounds.extents != Vector3.zero && ProjectBounds(camera, bounds, out rect))
		{
			rect = Grow(rect, BoxPadding, MinBoxPixels);
		}
		else
		{
			if (center.z <= 0f) center = new Vector3(Screen.width - center.x, Screen.height - center.y, center.z);
			rect = new Rect(center.x - SpotPixels / 2f, Screen.height - center.y - SpotPixels / 2f, SpotPixels, SpotPixels);
		}

		onScreen = center.z > 0f && rect.xMax > 0f && rect.x < Screen.width && rect.yMax > 0f && rect.y < Screen.height;
		if (!onScreen)
		{
			float width = Math.Min(rect.width, SpotPixels), height = Math.Min(rect.height, SpotPixels);
			float x = Mathf.Clamp(rect.center.x, EdgeMargin, Screen.width - EdgeMargin);
			float y = center.z <= 0f ? Screen.height - EdgeMargin : Mathf.Clamp(rect.center.y, EdgeMargin, Screen.height - EdgeMargin);
			rect = new Rect(x - width / 2f, y - height / 2f, width, height);
		}
		return true;
	}

	private static bool ProjectBounds(Camera camera, Bounds bounds, out Rect rect)
	{
		rect = default;
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		var min = bounds.min;
		var max = bounds.max;
		for (int i = 0; i < 8; i++)
		{
			var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
			var screen = camera.WorldToScreenPoint(corner);
			if (screen.z <= 0f) return false;
			minX = Math.Min(minX, screen.x);
			maxX = Math.Max(maxX, screen.x);
			minY = Math.Min(minY, screen.y);
			maxY = Math.Max(maxY, screen.y);
		}
		rect = new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
		return true;
	}

	private static Rect Grow(Rect rect, float padding, float minSize)
	{
		float width = Math.Max(rect.width + padding * 2f, minSize), height = Math.Max(rect.height + padding * 2f, minSize);
		return new Rect(rect.center.x - width / 2f, rect.center.y - height / 2f, width, height);
	}

	public static void Draw()
	{
		if (markers.Count == 0) return;
		var camera = Camera.main;
		if (camera == null) return;
		EnsureStyles();
		var previous = GUI.color;
		float now = Time.realtimeSinceStartup;

		foreach (var marker in markers.ToList())
		{
			if (!TryScreenRect(marker, camera, out var rect, out bool onScreen)) continue;
			float fade = Mathf.Clamp01(marker.Remaining / FadeSeconds);
			float pulse = 0.75f + 0.25f * Mathf.Sin((now - marker.ShownAt) * 8f);
			var color = marker.Own ? OwnColor : OtherColor;
			color.a = fade * pulse;
			GUI.color = color;
			if (marker.Flashed == null || !onScreen) Outline(rect);

			float distance = Vector3.Distance(camera.transform.position, WorldPoint(marker));
			string text = $"{marker.Name} ({distance:0} m)";
			var label = new Rect(rect.center.x - LabelWidth / 2f, rect.y - LabelHeight - 2f, LabelWidth, LabelHeight);
			if (label.y < 0f) label.y = rect.yMax + 2f;
			GUI.color = new Color(1f, 1f, 1f, fade);
			GUI.Label(new Rect(label.x + 1f, label.y + 1f, label.width, label.height), text, shadowStyle);
			style.normal.textColor = new Color(color.r, color.g, color.b, 1f);
			GUI.Label(label, text, style);
		}
		GUI.color = previous;
	}

	private static void Outline(Rect rect)
	{
		var texture = Texture2D.whiteTexture;
		GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, Line), texture);
		GUI.DrawTexture(new Rect(rect.x, rect.yMax - Line, rect.width, Line), texture);
		GUI.DrawTexture(new Rect(rect.x, rect.y, Line, rect.height), texture);
		GUI.DrawTexture(new Rect(rect.xMax - Line, rect.y, Line, rect.height), texture);
	}

	private static void EnsureStyles()
	{
		if (style != null) return;
		style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
		shadowStyle = new GUIStyle(style);
		shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
	}
}
