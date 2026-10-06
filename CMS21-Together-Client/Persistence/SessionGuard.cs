using System.Collections.Generic;
using System.Diagnostics;
using CMS.Platforms;
using CMS21_Together_Core.Logging;
using CMS21Together.Utils;
using HarmonyLib;

namespace CMS21Together.Persistence;

[HarmonyPatch]
public static class SessionGuard
{
	public const int SessionProfileSlot = 4;
	private const string SelectedProfileKey = "selectedProfile";

	private static readonly HashSet<string> loggedBlocks = new HashSet<string>();
	private static int originalSelectedProfile;

	public static bool Active { get; private set; }

	public static void Begin(ProfileManager profileManager)
	{
		if (!Active) originalSelectedProfile = profileManager.selectedProfile;
		Active = true;
		loggedBlocks.Clear();
		Log.Info($"[SessionGuard] Multiplayer session started, game saves are blocked (selected profile was {originalSelectedProfile}).");
	}

	public static void End()
	{
		if (!Active) return;
		Active = false;

		var manager = Singleton<GameManager>.Instance;
		if (manager != null)
		{
			SaveUtils.RestoreProfileDataSize();
			manager.ProfileManager.selectedProfile = originalSelectedProfile;
		}
		Log.Info($"[SessionGuard] Multiplayer session ended, selected profile back to {originalSelectedProfile}.");
	}

	private static bool Allow(string what)
	{
		if (!Active) return true;
		if (loggedBlocks.Add(what))
			Log.Warn($"[SessionGuard] Blocked {what} during a multiplayer session.\n{new StackTrace(2, false)}");
		return false;
	}

	[HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.Save))]
	[HarmonyPrefix]
	private static bool GameDataManagerSave(int profileID) => Allow($"GameDataManager.Save({profileID})");

	[HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.Save))]
	[HarmonyPrefix]
	private static bool ProfileManagerSave() => Allow("ProfileManager.Save");

	[HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.BackupSave))]
	[HarmonyPrefix]
	private static bool ProfileManagerBackupSave() => Allow("ProfileManager.BackupSave");

	[HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.DeleteProfile))]
	[HarmonyPrefix]
	private static bool ProfileManagerDeleteProfile() => Allow("ProfileManager.DeleteProfile");

	[HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.DeleteSelectedProfile))]
	[HarmonyPrefix]
	private static bool ProfileManagerDeleteSelectedProfile() => Allow("ProfileManager.DeleteSelectedProfile");

	[HarmonyPatch(typeof(PlatformManager), nameof(PlatformManager.DeleteSave))]
	[HarmonyPrefix]
	private static bool PlatformManagerDeleteSave(string fileName) => Allow($"PlatformManager.DeleteSave({fileName})");

	// The game reads the selected profile from the PlayerPrefs value on every call, which would put a session on
	// the player's own profile; while the guard is on, the session slot is answered without touching the pref.
	[HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.GetSelectedProfile))]
	[HarmonyPrefix]
	private static bool GetSelectedProfile(ProfileManager __instance, ref int __result)
	{
		if (!Active) return true;
		__instance.selectedProfile = SessionProfileSlot;
		__result = SessionProfileSlot;
		return false;
	}

	[HarmonyPatch(typeof(GarageLoader), nameof(GarageLoader.Save))]
	[HarmonyPrefix]
	private static void GarageLoaderSave()
	{
		if (Active) Log.Debug($"[SessionGuard] GarageLoader.Save called during a session.\n{new StackTrace(1, false)}");
	}

	[HarmonyPatch(typeof(RDGPlayerPrefs), nameof(RDGPlayerPrefs.GetInt))]
	[HarmonyPostfix]
	private static void LogSelectedProfileRead(string key, int __result)
	{
		if (key == SelectedProfileKey) Log.Debug($"[SessionGuard] Game read pref {key} = {__result} (session active: {Active}).");
	}

	[HarmonyPatch(typeof(RDGPlayerPrefs), nameof(RDGPlayerPrefs.SetInt))]
	[HarmonyPrefix]
	private static void LogSelectedProfileWrite(string key, int value)
	{
		if (key == SelectedProfileKey) Log.Debug($"[SessionGuard] Pref {key} set to {value} (session active: {Active}).\n{new StackTrace(1, false)}");
	}
}
