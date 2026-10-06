using MelonLoader;
using Steamworks;

namespace CMS21Together.Data;

public static class PlayerSettings
{
	private static MelonPreferences_Category category;
	private static MelonPreferences_Entry<string> playerName;

	public static string NameOverride { get; set; }

	public static void Initialize()
	{
		category = MelonPreferences.CreateCategory("CMS21Together");
		playerName = category.CreateEntry("PlayerName", "", description: "Name shown to other players. Empty = Steam name.");
	}

	public static string PlayerName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(NameOverride)) return NameOverride;
			if (playerName != null && !string.IsNullOrWhiteSpace(playerName.Value)) return playerName.Value;
			return MainMod.IsSteamAvailable ? SteamClient.Name : "";
		}
	}
}
