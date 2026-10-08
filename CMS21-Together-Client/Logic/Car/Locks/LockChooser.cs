using System;
using System.Collections;
using System.Collections.Generic;
using CMS.UI;
using CMS.UI.Logic.ChoosePartDown;
using CMS.UI.Windows;
using CMS21_Together_Core.Logging;
using HarmonyLib;
using MelonLoader;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockChooser
{
	private static readonly HashSet<IntPtr> marked = new HashSet<IntPtr>();

	public static void Initialize()
	{
		CarLockMirror.Changed += (record, released, snapshot) =>
		{
			if (record.Items.Count > 0) Redraw();
		};
		LockGate.Reported += (action, report) =>
		{
			if (action.EndsSlotOnFailure && (report.Result == "refusedLocally" || report.Result == "denied"))
				MelonCoroutines.Start(Reopen(action));
		};
	}

	public static void Reset() => marked.Clear();

	private static ChoosePartUpWindow Window() => WindowManager.Instance?.GetWindowByID<ChoosePartUpWindow>(WindowID.ChoosePartUp);

	private static bool IsOpen() => WindowManager.Instance != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp);

	public static void Redraw()
	{
		try
		{
			if (!IsOpen()) return;
			Window()?.choosePartDownWindow?.DrawPage();
		}
		catch (Exception e)
		{
			Log.Error($"[Locks] Item chooser redraw failed: {e.Message}");
		}
	}

	[HarmonyPatch(typeof(ChoosePartPageManager), nameof(ChoosePartPageManager.DrawPage))]
	[HarmonyPostfix]
	private static void AfterDrawPage(ChoosePartPageManager __instance) => MarkRows(__instance);

	[HarmonyPatch(typeof(ChoosePartPageManager), nameof(ChoosePartPageManager.RedrawCurrentPage))]
	[HarmonyPostfix]
	private static void AfterRedrawCurrentPage(ChoosePartPageManager __instance) => MarkRows(__instance);

	private static void MarkRows(ChoosePartPageManager manager)
	{
		var down = Window()?.choosePartDownWindow;
		if (down == null || manager.Pointer != down.Pointer) return;
		var rows = manager.itemObjects;
		bool active = LockGate.Active;
		for (int i = 0; rows != null && i < rows.Length; i++)
		{
			var row = rows[i];
			if (row == null || row.Locked == null) continue;
			int holder = active && row.gameObject.activeSelf ? CarLockMirror.ItemHolder(row.ID) : -1;
			if (holder >= 0)
			{
				row.Locked.SetActive(true);
				if (row.LockedText != null) row.LockedText.text = LockMessages.ItemTag(holder);
				if (marked.Add(row.Pointer)) CarLockMirror.Count("blockedAtSelection.chooserItem");
			}
			else if (marked.Remove(row.Pointer))
			{
				row.Locked.SetActive(row.IsLocked);
			}
		}
	}

	public static IEnumerable<(long Uid, bool Locked, string Text)> Rows()
	{
		var rows = Window()?.choosePartDownWindow?.itemObjects;
		for (int i = 0; rows != null && i < rows.Length; i++)
		{
			var row = rows[i];
			if (row == null || !row.gameObject.activeSelf) continue;
			yield return (row.ID, row.Locked != null && row.Locked.activeSelf, row.LockedText?.text);
		}
	}

	private static IEnumerator Reopen(GatedAction refused)
	{
		yield return null;
		yield return null;
		yield return null;
		var part = LockSets.Relations(refused.Set.Loader)?.Registry?.Sub(refused.TargetKey);
		if (part == null || !part.IsUnmounted || IsOpen() || GameMode.Get()?.currentMode != gameMode.PartSelectMount) yield break;
		var action = LockHooks.MountAction(part);
		if (action == null) yield break;
		CarLockMirror.Count("chooserReopened");
		Log.Debug($"[Locks] Item step for {refused.TargetKey} refused; opening the item chooser again.");
		LockGate.Enter(action);
	}
}
