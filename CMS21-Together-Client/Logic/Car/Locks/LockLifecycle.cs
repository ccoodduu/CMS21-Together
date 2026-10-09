using System;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

public enum LockPhase
{
	Slot,
	Item,
	Work
}

public sealed class TrackedLock
{
	public int LockId;
	public CarLockKind Kind;
	public int Loader;
	public string MainKey;
	public PartScript Part;
	public CarPart Body;
	public LockPhase Phase;
	public float StartedAt;
	public float LastProgressAt;
	public float Progress;
	public float EndingSince = -1f;
	public Func<bool> Finished;
	public bool ItemPicked;
	public bool FlushFluids;
	public readonly Dictionary<string, bool> StartUnmounted = new Dictionary<string, bool>();
}

[HarmonyPatch]
public static class LockLifecycle
{
	public const float EndingFallbackSeconds = 8f;
	public const float CarLockSeconds = 30f;

	public static float ChooserIdleSeconds { get; set; } = 60f;
	public static float BoltIdleSeconds { get; set; } = 300f;
	public static float TuneIdleSeconds { get; set; } = 300f;

	private static readonly Dictionary<int, TrackedLock> tracked = new Dictionary<int, TrackedLock>();

	public static IEnumerable<TrackedLock> All => tracked.Values;

	public static void Reset() => tracked.Clear();

	public static void Initialize()
	{
		CarLockMirror.BusyRefused += (holder, refusal, what) => LockMessages.Refuse(LockMessages.Busy(holder));
		LockGate.Reported += (action, report) =>
		{
			if (!action.EndsSlotOnFailure || report.Result == "granted") return;
			var slot = Get(action.ExtendLockId);
			if (slot != null && slot.Phase == LockPhase.Slot) BackOut(slot, $"item step {report.Result}");
		};
	}

	public static TrackedLock Get(int lockId) => tracked.TryGetValue(lockId, out var t) ? t : null;

	public static IEnumerable<TrackedLock> OfKind(CarLockKind kind) => tracked.Values.Where(t => t.Kind == kind).ToList();

	public static TrackedLock ForPart(int loader, string key) =>
		tracked.Values.FirstOrDefault(t => t.Loader == loader && CarLockMirror.Get(t.LockId)?.X.Contains(key) == true);

	public static TrackedLock SlotFor(PartScript part) =>
		tracked.Values.FirstOrDefault(t => t.Kind == CarLockKind.PartMount && t.Part != null && part != null && t.Part.Pointer == part.Pointer && t.EndingSince < 0f);

	public static TrackedLock Track(int lockId, LockSet set, string mainKey, PartScript part = null, CarPart body = null, LockPhase phase = LockPhase.Work, Func<bool> finished = null)
	{
		var t = new TrackedLock
		{
			LockId = lockId, Kind = set.Kind, Loader = set.Loader, MainKey = mainKey, Part = part, Body = body, Phase = phase, Finished = finished,
			StartedAt = Time.realtimeSinceStartup, LastProgressAt = Time.realtimeSinceStartup, Progress = BoltProgress(part)
		};
		foreach (string key in set.X.Where(LockKeys.IsPart))
		{
			bool? unmounted = Unmounted(set.Loader, key);
			if (unmounted.HasValue) t.StartUnmounted[key] = unmounted.Value;
		}
		tracked[lockId] = t;
		return t;
	}

	private static bool? Unmounted(int loader, string key)
	{
		var registry = LockSets.Relations(loader)?.Registry;
		if (registry == null) return null;
		if (LockKeys.IsSub(key)) return registry.Sub(key)?.IsUnmounted;
		var body = registry.Body(key);
		return body == null ? (bool?)null : body.Unmounted;
	}

	public static void Release(TrackedLock t, string why, string counter = null)
	{
		if (!tracked.Remove(t.LockId)) return;
		if (counter != null) CarLockMirror.Count(counter);
		Log.Debug($"[Locks] Lock {t.LockId} ({t.Kind} {t.MainKey}) released: {why}.");
		CarLockMirror.Release(t.LockId);
	}

	public static void ReleaseAfterFlush(TrackedLock t, string why)
	{
		if (!tracked.Remove(t.LockId)) return;
		CarLockMirror.MarkEnding(t.LockId);
		Details.CarDetailsSync.FlushNow(t.Loader, CarDetailSection.Fluids, () =>
		{
			Log.Debug($"[Locks] Lock {t.LockId} ({t.Kind} {t.MainKey}) released after the fluid flush: {why}.");
			CarLockMirror.Release(t.LockId);
		});
	}

	public static void MarkEnding(TrackedLock t)
	{
		if (t.EndingSince >= 0f) return;
		t.EndingSince = Time.realtimeSinceStartup;
		CarLockMirror.MarkEnding(t.LockId);
	}

	private static float BoltProgress(PartScript part)
	{
		var bolts = part?.MountObjects;
		float sum = 0f;
		for (int i = 0; bolts != null && i < bolts.Length; i++)
			if (bolts[i] != null) sum += bolts[i].GetMountState();
		return sum;
	}

	public static void OnChangeSent(int loader)
	{
		foreach (var t in tracked.Values.Where(t => t.Loader == loader && t.EndingSince < 0f && t.StartUnmounted.Count > 0).ToList())
			if (t.StartUnmounted.All(p => Unmounted(loader, p.Key) is bool now && now != p.Value)) MarkEnding(t);
	}

	public static void Update()
	{
		if (tracked.Count == 0) return;
		float now = Time.realtimeSinceStartup;
		foreach (var t in tracked.Values.ToList())
		{
			if (CarLockMirror.Get(t.LockId) == null && now - t.StartedAt > 2f)
			{
				tracked.Remove(t.LockId);
				continue;
			}
			if (t.EndingSince >= 0f)
			{
				if (now - t.EndingSince > EndingFallbackSeconds) Release(t, "no release from the server after the change");
				continue;
			}
			if (t.Finished != null && SafeFinished(t))
			{
				if (t.FlushFluids) ReleaseAfterFlush(t, "the action ended");
				else Release(t, "the action ended");
				continue;
			}
			switch (t.Kind)
			{
				case CarLockKind.PartUnmount:
				case CarLockKind.PartMount:
				case CarLockKind.GroupUnmount:
				case CarLockKind.GroupMount:
					UpdatePartLock(t, now);
					break;
				case CarLockKind.Lift:
				case CarLockKind.Move:
					if (now - t.StartedAt > CarLockSeconds) Release(t, "car lock cap");
					break;
				case CarLockKind.Crane:
				case CarLockKind.BodyPart:
					if (now - t.StartedAt > CarLockSeconds && !PartChangeTracker.IsPending(t.Loader)) Release(t, "no commit in time");
					break;
				case CarLockKind.Tune:
					if (now - t.LastProgressAt > TuneIdleSeconds) LockTuneHooks.CloseIdle(t);
					break;
			}
		}
	}

	private static bool SafeFinished(TrackedLock t)
	{
		try { return t.Finished(); }
		catch (Exception) { return true; }
	}

	private static void UpdatePartLock(TrackedLock t, float now)
	{
		if (t.Phase == LockPhase.Slot)
		{
			if (now - t.StartedAt > ChooserIdleSeconds)
			{
				CarLockMirror.Count("idleCancelled");
				Log.Info($"[Locks] Item chooser for {t.MainKey} open for {ChooserIdleSeconds:0} s without a choice; closing it.");
				var window = WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);
				if (window != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp)) window.Hide(false);
				Release(t, "chooser idle");
			}
			return;
		}
		float progress = BoltProgress(t.Part);
		if (Mathf.Abs(progress - t.Progress) > 0.001f)
		{
			t.Progress = progress;
			t.LastProgressAt = now;
		}
		else if (now - t.LastProgressAt > BoltIdleSeconds)
		{
			CarLockMirror.Count("idleCancelled");
			Log.Info($"[Locks] No bolt progress on {t.MainKey} for {BoltIdleSeconds:0} s; cancelling.");
			var part = t.Part;
			Release(t, "bolt idle");
			try
			{
				if (part != null && t.Kind == CarLockKind.PartMount) part.UndoMounting();
				else if (part != null) part.UndoUnMounting();
				var mode = GameMode.Get();
				if (mode != null && (mode.currentMode == gameMode.PartUnMount || mode.currentMode == gameMode.PartMount))
					mode.SetCurrentMode(mode.currentMode == gameMode.PartMount ? gameMode.PartSelectMount : gameMode.PartSelect);
			}
			catch (Exception e)
			{
				Log.Warn($"[Locks] Idle cancel of {t.MainKey}: {e.Message}");
			}
		}
	}

	public static void BackOut(TrackedLock t, string why)
	{
		Release(t, why, "backedOut");
	}

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.UndoUnMounting))]
	[HarmonyPostfix]
	private static void AfterUndoUnMounting(PartScript __instance) => BackOutPart(__instance, "unmount undone");

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.UndoMounting))]
	[HarmonyPostfix]
	private static void AfterUndoMounting(PartScript __instance) => BackOutPart(__instance, "mount undone");

	private static void BackOutPart(PartScript part, string why)
	{
		if (part == null || tracked.Count == 0) return;
		foreach (var t in tracked.Values.Where(t => t.Part != null && (t.Part.Pointer == part.Pointer || Covers(t, part))).ToList())
			if (t.EndingSince < 0f) BackOut(t, why);
	}

	private static bool Covers(TrackedLock t, PartScript part)
	{
		var record = CarLockMirror.Get(t.LockId);
		var registry = LockSets.Relations(t.Loader)?.Registry;
		return record != null && registry != null && registry.TryGetSubPath(part, out var path) && record.X.Contains(PartKeys.Sub(path));
	}

	[HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.Hide))]
	[HarmonyPostfix]
	private static void AfterChooserHide()
	{
		if (!tracked.Values.Any(t => t.Phase == LockPhase.Slot)) return;
		MelonCoroutines.Start(CheckChooserBackOut());
	}

	private static System.Collections.IEnumerator CheckChooserBackOut()
	{
		yield return null;
		yield return null;
		foreach (var t in tracked.Values.Where(t => t.Phase == LockPhase.Slot && !t.ItemPicked).ToList())
			BackOut(t, "chooser closed without a choice");
	}

	[HarmonyPatch(typeof(GameMode), nameof(GameMode.SetCurrentMode))]
	[HarmonyPostfix]
	private static void AfterSetCurrentMode(GameMode __instance, gameMode newGameMode, bool __runOriginal)
	{
		if (!__runOriginal || tracked.Count == 0) return;
		foreach (var t in tracked.Values.ToList())
		{
			if (t.EndingSince >= 0f || SelfSet(t, newGameMode)) continue;
			if (t.Part != null && t.Phase == LockPhase.Work && WorkDone(t)) MarkEnding(t);
			else if (t.Kind == CarLockKind.PartUnmount || t.Kind == CarLockKind.PartMount) BackOut(t, $"mode changed to {newGameMode}");
		}
	}

	private static bool WorkDone(TrackedLock t) => t.Kind == CarLockKind.PartMount ? !t.Part.IsUnmounted : t.Part.IsUnmounted;

	private static bool SelfSet(TrackedLock t, gameMode mode)
	{
		switch (t.Kind)
		{
			case CarLockKind.PartUnmount:
				return mode == gameMode.PartUnMount || mode == gameMode.UI;
			case CarLockKind.PartMount:
				return mode == gameMode.UI || mode == gameMode.PartMount || mode == gameMode.PartSelectMount && t.Phase == LockPhase.Slot;
			default:
				return true;
		}
	}
}
