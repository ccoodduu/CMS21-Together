using System;
using System.Collections.Generic;
using CMS.UI;
using HarmonyLib;
using UnhollowerBaseLib;
using UnityEngine;

namespace CMS21Together.Guard;

[HarmonyPatch]
public static class GuardHooks
{
	private static readonly HashSet<gameMode> logOnlyModes = new HashSet<gameMode>
	{
		gameMode.Interior, gameMode.CarDrive, gameMode.PathTest, gameMode.Dyno, gameMode.Benchmark
	};

	private static readonly Dictionary<string, bool> lockedPieOptions = new Dictionary<string, bool>();
	private static IntPtr lockedFor;

	[HarmonyPatch(typeof(WindowManager), nameof(WindowManager.Show), typeof(WindowID), typeof(bool))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool ShowWindow(WindowID windowID, ref bool __result) => AllowWindow(windowID, ref __result);

	[HarmonyPatch(typeof(WindowManager), nameof(WindowManager.Show), typeof(WindowID), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool ShowWindowWithArgs(WindowID windowID, ref bool __result) => AllowWindow(windowID, ref __result);

	private static bool AllowWindow(WindowID windowID, ref bool __result)
	{
		if (FeatureGuard.Decide(GuardKind.Window, windowID.ToString()) == GuardDecision.Allow) return true;
		__result = false;
		return false;
	}

	[HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.PrepareIcons))]
	[HarmonyPostfix]
	private static void LockPieOptions(PieMenuController __instance, Il2CppStringArray iconsToLoad)
	{
		var options = __instance.options;
		if (options == null || iconsToLoad == null) return;
		if (__instance.Pointer != lockedFor)
		{
			lockedFor = __instance.Pointer;
			lockedPieOptions.Clear();
		}
		foreach (string id in iconsToLoad)
		{
			if (string.IsNullOrEmpty(id) || !options.ContainsKey(id)) continue;
			bool blocked = FeatureGuard.WouldBlock(GuardKind.Pie, id);
			if (blocked && !lockedPieOptions.ContainsKey(id))
			{
				lockedPieOptions[id] = options[id].Enabled;
				__instance.SetEnableOption(id, false);
			}
			else if (!blocked && lockedPieOptions.TryGetValue(id, out bool wasEnabled))
			{
				lockedPieOptions.Remove(id);
				__instance.SetEnableOption(id, wasEnabled);
			}
		}
	}

	[HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.HandleInput))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void ReportLockedPieChoice(PieMenuController __instance)
	{
		if (!__instance.IsEnabledPieMenu || !(Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return))) return;
		string id = SelectedPieOption(__instance);
		if (id != null) FeatureGuard.Decide(GuardKind.Pie, id);
	}

	[HarmonyPatch(typeof(PieMenuController), nameof(PieMenuController.CheckSelectedOption))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool CheckSelectedOption(PieMenuController __instance)
	{
		string id = SelectedPieOption(__instance);
		if (id == null || FeatureGuard.Decide(GuardKind.Pie, id) == GuardDecision.Allow) return true;
		if (__instance.IsPieMenuOpen) __instance.Close();
		return false;
	}

	public static string SelectedPieOption(PieMenuController controller)
	{
		int option = controller.CurrOption;
		var names = controller.NameList;
		if (option < 0 || names == null || option >= names.Length) return null;
		return names[option];
	}

	[HarmonyPatch(typeof(GameMode), nameof(GameMode.SetCurrentMode))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool SetCurrentMode(GameMode __instance, gameMode newGameMode)
	{
		if (newGameMode == __instance.currentMode) return true;
		bool canBlock = !logOnlyModes.Contains(newGameMode);
		return FeatureGuard.Decide(GuardKind.Mode, newGameMode.ToString(), canBlock) == GuardDecision.Allow;
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.StartSelectSceneToLoad))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool StartSelectSceneToLoad(string newSceneName, SceneType sceneType) =>
		FeatureGuard.Decide(GuardKind.Scene, SceneId(newSceneName, sceneType)) == GuardDecision.Allow;

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.SelectSceneToLoad),
		typeof(string), typeof(SceneType), typeof(bool))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool SelectSceneToLoad(string newSceneName, SceneType sceneType) =>
		FeatureGuard.Decide(GuardKind.Scene, SceneId(newSceneName, sceneType)) == GuardDecision.Allow;

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.SelectSceneToLoad),
		typeof(string), typeof(SceneType), typeof(bool), typeof(bool))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool SelectSceneToLoadCoroutine(string newSceneName, SceneType sceneType, ref Il2CppSystem.Collections.IEnumerator __result)
	{
		if (FeatureGuard.Decide(GuardKind.Scene, SceneId(newSceneName, sceneType)) == GuardDecision.Allow) return true;
		__result = new Il2CppSystem.Collections.ArrayList().GetEnumerator();
		return false;
	}

	public static string SceneId(string sceneName, SceneType sceneType)
	{
		if (sceneType != SceneType.None) return sceneType.ToString();
		return string.IsNullOrEmpty(sceneName) ? "None" : sceneName;
	}
}
