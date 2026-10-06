using MelonLoader;
using Steamworks;

namespace CMS21Together.Data;

public static class PlayerSettings
{
	private static MelonPreferences_Category category;
	private static MelonPreferences_Entry<string> playerName;
	private static MelonPreferences_Entry<string> lastJoinTarget;
	private static MelonPreferences_Entry<bool> devHotkeys;

	public static string NameOverride { get; set; }

	public static void Initialize()
	{
		category = MelonPreferences.CreateCategory("CMS21Together");
		playerName = category.CreateEntry("PlayerName", "", description: "Name shown to other players. Empty = Steam name.");
		lastJoinTarget = category.CreateEntry("LastJoinTarget", "", description: "Last server joined (IP:port or Steam server ID).");
		devHotkeys = category.CreateEntry("DevHotkeys", false, description: "F5 joins the last server (developer shortcut).");
	}

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

	public static bool DevHotkeys => devHotkeys != null && devHotkeys.Value;
}
