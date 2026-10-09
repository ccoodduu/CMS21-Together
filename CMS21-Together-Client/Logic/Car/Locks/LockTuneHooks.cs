using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;
using UnhollowerBaseLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockTuneHooks
{
	private static TuneWindow Window => WindowManager.Instance?.GetWindowByID<TuneWindow>(WindowID.Tune);

	public static GatedAction OpenAction(TuneWindow window, Il2CppReferenceArray<Il2CppSystem.Object> args)
	{
		var carLoader = args != null && args.Length > 0 ? args[0]?.TryCast<CarLoader>() : null;
		int loader = LockFluidHooks.LoaderOf(carLoader);
		if (loader < 0) return null;
		var set = LockSets.ForTune(loader, carLoader);
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = set, Target = window, TargetKey = LockKeys.Tune,
			Context = () => carLoader != null && carLoader.IsCarLoaded() && CarPartsSync.SpawnSeq(loader) == seq && !window.isActive,
			Run = () => WindowManager.Instance.Show(WindowID.Tune, args),
			Started = () => window.isActive,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, LockKeys.Tune, finished: () => window == null || !window.isActive),
		};
	}

	public static void Touch()
	{
		float now = Time.realtimeSinceStartup;
		foreach (var t in LockLifecycle.OfKind(CarLockKind.Tune)) t.LastProgressAt = now;
	}

	public static void CloseIdle(TrackedLock t)
	{
		CarLockMirror.Count("idleCancelled");
		Log.Info($"[Locks] Tune window open for {LockLifecycle.TuneIdleSeconds:0} s without an applied change; closing it.");
		LockLifecycle.Release(t, "tune idle");
		var window = Window;
		if (window != null && window.isActive) window.HideAction();
	}

	[HarmonyPatch(typeof(TuneWindow), nameof(TuneWindow.Show))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeShow(TuneWindow __instance, Il2CppReferenceArray<Il2CppSystem.Object> args, ref bool __result)
	{
		if (!LockGate.Active) return true;
		var action = OpenAction(__instance, args);
		if (action == null || LockGate.Enter(action)) return true;
		__result = false;
		return false;
	}

	[HarmonyPatch(typeof(TuneWindow), nameof(TuneWindow.Hide))]
	[HarmonyPostfix]
	private static void AfterHide()
	{
		foreach (var t in LockLifecycle.OfKind(CarLockKind.Tune)) LockLifecycle.Release(t, "tune window closed");
	}
}
