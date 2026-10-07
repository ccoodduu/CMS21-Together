using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D1: every visual runs here. The only writes to existing game objects are
// Renderer.forceRenderingOff on renderers a ghost stands in for; they are counted per renderer and restored when the
// effect ends, is cancelled, throws, or the scope is reset.
public static class VisualScope
{
	public const int MaxEffects = 16;
	public const int MaxEffectsPerCar = 4;
	public const float MaxDistance = 40f;

	// Never called on a receiver by a visual; listed for review. The leak detector catches their state writes.
	public static readonly string[] Forbidden =
	{
		"PartScript.Hide", "PartScript.ShowMounted", "PartScript.DoMount", "PartScript.FastUnmount", "PartScript.FastMount",
		"PartScript.ActionUnMount", "PartScript.ActionMount", "PartScript.ShowMountAnimation", "CarLoader.TakeOffCarPart",
		"CarLoader.SwitchCarPart(instant: false)", "GarageTool.DoWorkAnim", "GarageTool.StartAnim", "MountObject.Action",
		"MountObject.SetPosition", "ToolsManager.Use"
	};

	private class HiddenRenderer
	{
		public Renderer Renderer;
		public int Count;
	}

	private static readonly List<VisualEffect> effects = new List<VisualEffect>();
	private static readonly Dictionary<int, HiddenRenderer> hidden = new Dictionary<int, HiddenRenderer>();
	private static readonly Dictionary<string, int> started = new Dictionary<string, int>();
	private static readonly Dictionary<string, int> finished = new Dictionary<string, int>();
	private static readonly Dictionary<string, int> skipped = new Dictionary<string, int>();
	private static int depth;
	private static bool subscribed;

	public static bool Hold { get; set; }
	public static int Leaks { get; private set; }
	public static bool Active => depth > 0;
	public static bool Enabled => PlayerSettings.RemoteVisuals;
	public static IReadOnlyList<VisualEffect> Effects => effects;
	public static int RenderersHidden => hidden.Count;
	public static IReadOnlyDictionary<string, int> Started => started;
	public static IReadOnlyDictionary<string, int> Finished => finished;
	public static IReadOnlyDictionary<string, int> Skipped => skipped;

	public static void Initialize()
	{
		if (subscribed) return;
		subscribed = true;
		ClientScene.LeavingScene += (from, to) => CancelAll($"left {from}");
	}

	public static IDisposable Enter() => new ScopeHandle();

	public static PartRegistry RegistryOf(int loader) => CarPartsSync.All.FirstOrDefault(s => s.Loader == loader)?.Registry;

	public static void CheckLeak(string what)
	{
		if (depth == 0) return;
		Leaks++;
		Log.Error($"[Visuals] state leak {what}");
	}

	// Why a live visual may not start, or null when it may. Counts the reason as skipped.
	public static string Admit(VisualKind kind, int loader, Vector3 position)
	{
		string reason = null;
		if (!Enabled) reason = "disabled";
		else if (!ClientScene.IsGarageReady) reason = "notInGarage";
		else if (!ClientData.IsInitialSyncFinished || SyncTracker.InSnapshot) reason = "beforeSync";
		else if (effects.Count(e => !e.Ended) >= MaxEffects) reason = "cap";
		else if (effects.Count(e => !e.Ended && e.Loader == loader) >= MaxEffectsPerCar) reason = "carCap";
		else if (Camera.main != null && Vector3.Distance(Camera.main.transform.position, position) > MaxDistance) reason = "distance";
		if (reason != null) Skip(kind, reason);
		return reason;
	}

	public static void Skip(VisualKind kind, string reason)
	{
		skipped[reason] = (skipped.TryGetValue(reason, out int n) ? n : 0) + 1;
		Log.Debug($"[Visuals] {kind} skipped ({reason}).");
	}

	public static void Start(VisualEffect effect)
	{
		effects.Add(effect);
		Count(started, effect.Kind);
		Log.Debug($"[Visuals] {effect.Kind} started on loader {effect.Loader} {effect.Key} for player {effect.PlayerId}.");
	}

	public static void HideRenderer(VisualEffect effect, Renderer renderer)
	{
		if (renderer == null || !renderer) return;
		int id = renderer.GetInstanceID();
		if (!hidden.TryGetValue(id, out var entry))
		{
			if (renderer.forceRenderingOff) return;
			entry = new HiddenRenderer { Renderer = renderer };
			hidden[id] = entry;
			renderer.forceRenderingOff = true;
		}
		entry.Count++;
		effect.Hidden.Add(id);
	}

	public static void Update()
	{
		if (effects.Count == 0) return;
		float dt = Time.deltaTime;
		foreach (var effect in effects.ToList())
		{
			if (effect.Ended) continue;
			bool keep;
			try
			{
				if (!StillValid(effect))
				{
					End(effect, "car changed");
					continue;
				}
				float step = dt;
				if (Hold) step = Mathf.Clamp(effect.HoldPoint - effect.Elapsed, 0f, dt);
				effect.Elapsed += step;
				using (Enter()) keep = effect.Step(step);
			}
			catch (Exception e)
			{
				Log.Error($"[Visuals] {effect.Kind} on loader {effect.Loader} {effect.Key} failed: {e.Message}");
				keep = false;
			}
			if (!keep) End(effect, null);
		}
		effects.RemoveAll(e => e.Ended);
	}

	public static void CancelFor(int loader, IEnumerable<string> keys, string reason, bool keepBolts = false)
	{
		var set = new HashSet<string>(keys);
		foreach (var effect in effects.Where(e => !e.Ended && e.Loader == loader && e.Key != null && set.Contains(e.Key) && !(keepBolts && e.Kind == VisualKind.Bolts)).ToList())
			End(effect, reason);
	}

	public static void CancelPlayer(int playerId, string reason)
	{
		foreach (var effect in effects.Where(e => !e.Ended && e.PlayerId == playerId).ToList()) End(effect, reason);
	}

	public static void CancelLoader(int loader, string reason)
	{
		foreach (var effect in effects.Where(e => !e.Ended && e.Loader == loader).ToList()) End(effect, reason);
	}

	public static void CancelAll(string reason)
	{
		foreach (var effect in effects.Where(e => !e.Ended).ToList()) End(effect, reason);
		effects.Clear();
		RestoreAllRenderers();
	}

	public static void Reset()
	{
		CancelAll("reset");
		Hold = false;
		started.Clear();
		finished.Clear();
		skipped.Clear();
		Leaks = 0;
	}

	public static void End(VisualEffect effect, string cancelReason)
	{
		if (effect.Ended) return;
		effect.Ended = true;
		try
		{
			using (Enter()) effect.OnEnd();
		}
		catch (Exception e)
		{
			Log.Error($"[Visuals] {effect.Kind} end failed: {e.Message}");
		}
		finally
		{
			foreach (int id in effect.Hidden) Restore(id);
			effect.Hidden.Clear();
			effect.DestroyGhosts();
			Count(finished, effect.Kind);
			if (cancelReason != null) Log.Debug($"[Visuals] {effect.Kind} on loader {effect.Loader} {effect.Key} cancelled ({cancelReason}).");
		}
	}

	private static bool StillValid(VisualEffect effect)
	{
		if (effect.Loader < 0) return true;
		return CarPartsSync.IsReady(effect.Loader) && CarPartsSync.SpawnSeq(effect.Loader) == effect.SpawnSeq;
	}

	private static void Restore(int id)
	{
		if (!hidden.TryGetValue(id, out var entry)) return;
		if (--entry.Count > 0) return;
		hidden.Remove(id);
		if (entry.Renderer != null && entry.Renderer) entry.Renderer.forceRenderingOff = false;
	}

	private static void RestoreAllRenderers()
	{
		foreach (var entry in hidden.Values)
			if (entry.Renderer != null && entry.Renderer) entry.Renderer.forceRenderingOff = false;
		hidden.Clear();
	}

	private static void Count(Dictionary<string, int> counter, VisualKind kind)
	{
		string key = kind.ToString();
		counter[key] = (counter.TryGetValue(key, out int n) ? n : 0) + 1;
	}

	private sealed class ScopeHandle : IDisposable
	{
		private bool disposed;

		public ScopeHandle() => depth++;

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			depth--;
		}
	}
}
