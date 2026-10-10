using MelonLoader;
using Steamworks;

namespace CMS21Together.Data;

public static class PlayerSettings
{
	private static MelonPreferences_Category category;
	private static MelonPreferences_Entry<string> playerName;
	private static MelonPreferences_Entry<string> lastJoinTarget;
	private static MelonPreferences_Entry<bool> devHotkeys;
	private static MelonPreferences_Entry<bool> advertisePresence;
	private static MelonPreferences_Entry<string> resyncHotkey;
	private static MelonPreferences_Entry<string> adminKey;
	private static MelonPreferences_Entry<string> serverPath;
	private static MelonPreferences_Entry<string> sessionPanelHotkey;
	private static MelonPreferences_Entry<string> bugReportHotkey;
	private static MelonPreferences_Entry<bool> remoteVisuals;
	private static MelonPreferences_Entry<string> pingHotkey;

	public const string DefaultPingKey = "Mouse2";

	public static string NameOverride { get; set; }

	public static void Initialize()
	{
		category = MelonPreferences.CreateCategory("CMS21Together");
		playerName = category.CreateEntry("PlayerName", "", description: "Name shown to other players. Empty = Steam name.");
		lastJoinTarget = category.CreateEntry("LastJoinTarget", "", description: "Last server joined (IP:port or Steam server ID).");
		devHotkeys = category.CreateEntry("DevHotkeys", false, description: "F5 joins the last server, F6 spawns a random base-game car in a connected garage (developer shortcuts).");
		advertisePresence = category.CreateEntry("AdvertisePresence", true, description: "Show the server in Steam rich presence so friends can join.");
		resyncHotkey = category.CreateEntry("ResyncHotkey", "F7", description: "Key that reloads the garage from the server when something looks out of sync.");
		adminKey = category.CreateEntry("AdminKey", "", description: "Admin key of a dedicated server you run (its admin_key); lets you kick players there. Keep it secret.");
		serverPath = category.CreateEntry("ServerPath", "", description: "Server program started by Host. Empty = TogetherServer\\CMS21_Together_Server.exe in the game folder.");
		sessionPanelHotkey = category.CreateEntry("SessionPanelHotkey", "F9", description: "Key that opens the session panel (players, ping, kick, Steam friends).");
		bugReportHotkey = category.CreateEntry("BugReportHotkey", "F8", description: "Key that saves a bug report (logs, settings, mod list, shared state) to UserData\\CMS21Together\\BugReports; when connected the server and the other players save theirs with the same id.");
		remoteVisuals = category.CreateEntry("RemoteVisuals", true, description: "Show other players' work as it happens (parts moving off and on, bolts turning, the avatar working with a tool). Off: their changes still apply, without animation.");
		pingHotkey = category.CreateEntry("PingHotkey", DefaultPingKey, description: "Key that pings the part or spot you look at: the other players in your scene see it marked with your name for a few seconds, also through walls. Mouse2 = middle mouse button; None = off.");
	}

	public static UnityEngine.KeyCode PingKey =>
		System.Enum.TryParse(pingHotkey?.Value, true, out UnityEngine.KeyCode key) ? key : UnityEngine.KeyCode.Mouse2;

	public static UnityEngine.KeyCode BugReportKey =>
		System.Enum.TryParse(bugReportHotkey?.Value, true, out UnityEngine.KeyCode key) ? key : UnityEngine.KeyCode.F8;

	public static UnityEngine.KeyCode SessionPanelKey =>
		System.Enum.TryParse(sessionPanelHotkey?.Value, true, out UnityEngine.KeyCode key) ? key : UnityEngine.KeyCode.F9;

	public static string AdminKey => adminKey?.Value ?? "";

	public static string ServerPath => serverPath?.Value ?? "";

	public static string PlayerName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(NameOverride)) return NameOverride;
			if (playerName != null && !string.IsNullOrWhiteSpace(playerName.Value)) return playerName.Value;
			return MainMod.IsSteamAvailable ? SteamClient.Name : "";
		}
		set
		{
			if (playerName == null) return;
			playerName.Value = value ?? "";
			category.SaveToFile(false);
		}
	}

	public static string LastJoinTarget
	{
		get => lastJoinTarget?.Value ?? "";
		set
		{
			if (lastJoinTarget == null) return;
			lastJoinTarget.Value = value ?? "";
			category.SaveToFile(false);
		}
	}

	public static bool RemoteVisuals
	{
		get => remoteVisuals == null || remoteVisuals.Value;
		set
		{
			if (remoteVisuals != null) remoteVisuals.Value = value;
		}
	}

	public static bool DevHotkeys => devHotkeys != null && devHotkeys.Value;

	public static bool AdvertisePresence => advertisePresence == null || advertisePresence.Value;

	public static UnityEngine.KeyCode ResyncKey =>
		System.Enum.TryParse(resyncHotkey?.Value, true, out UnityEngine.KeyCode key) ? key : UnityEngine.KeyCode.F7;
}
