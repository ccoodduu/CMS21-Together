using System;
using CMS21_Together_Core.Logging;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockPreviews
{
	public const int PartLayer = 16;

	public static void Initialize()
	{
		CarLockMirror.Changed += (record, released, snapshot) => Refresh(record.Loader, released);
	}

	private static CarLoader MountModeCar(out int loader)
	{
		loader = -1;
		var game = GameScript.Get();
		if (!LockGate.Active || game == null || GameMode.Get()?.currentMode != gameMode.PartSelectMount) return null;
		var carLoader = game.GetIOMouseOverCarLoader2();
		loader = LockFluidHooks.LoaderOf(carLoader);
		return loader < 0 ? null : carLoader;
	}

	private static void Refresh(int loader, bool released)
	{
		try
		{
			if (MountModeCar(out int focused) == null || focused != loader) return;
			LockSelection.Reset();
			if (released) GameScript.Get().PrepareItemsToMount();
			else HideBlocked(loader);
		}
		catch (Exception e)
		{
			Log.Error($"[Locks] Mount-mode preview refresh on loader {loader} failed: {e.Message}");
		}
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.PrepareItemsToMount))]
	[HarmonyPostfix]
	private static void AfterPrepareItemsToMount()
	{
		if (MountModeCar(out int loader) != null) HideBlocked(loader);
	}

	public static int HideBlocked(int loader)
	{
		var registry = LockSets.Relations(loader)?.Registry;
		if (registry == null) return 0;
		int hidden = 0;
		foreach (string key in registry.SubKeys)
		{
			var part = registry.Sub(key);
			if (part == null || !part.IsUnmounted || part.gameObject.layer != PartLayer || LockSelection.BlockedMessage(part) == null) continue;
			part.HidePreview();
			CarLockMirror.Count("blockedAtSelection.preview");
			hidden++;
		}
		return hidden;
	}
}
