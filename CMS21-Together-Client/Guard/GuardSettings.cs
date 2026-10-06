using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using MelonLoader;

namespace CMS21Together.Guard;

public static class GuardSettings
{
	private static MelonPreferences_Entry<string> mode;
	private static MelonPreferences_Entry<string> allow;
	private static MelonPreferences_Entry<string> deny;

	private static string parsedAllowRaw;
	private static string parsedDenyRaw;
	private static HashSet<string> allowSet = new HashSet<string>();
	private static HashSet<string> denySet = new HashSet<string>();

	public static void Initialize()
	{
		var category = MelonPreferences.CreateCategory("CMS21Together_Guard");
		mode = category.CreateEntry("Mode", nameof(GuardMode.Enforce),
			description: "Multiplayer feature guard: Enforce (refuse features that do not sync yet), LogOnly or Off.");
		allow = category.CreateEntry("Allow", "",
			description: "Extra allowed entries, ';'-separated Kind:Id (Window:Map;Scene:Junkyard;Mode:PhotoMode;Pie:map).");
		deny = category.CreateEntry("Deny", "", description: "Extra refused entries, same format; Deny wins over Allow.");
	}

	public static GuardMode Mode
	{
		get
		{
			string raw = mode?.Value;
			if (string.IsNullOrWhiteSpace(raw)) return GuardMode.Enforce;
			if (Enum.TryParse(raw.Trim(), true, out GuardMode parsed)) return parsed;
			Log.Warn($"[Guard] Unknown guard mode '{raw}', using Enforce.");
			return GuardMode.Enforce;
		}
	}

	public static string AllowRaw => allow?.Value ?? "";

	public static string DenyRaw => deny?.Value ?? "";

	public static bool Allowed(string key)
	{
		if (AllowRaw != parsedAllowRaw)
		{
			parsedAllowRaw = AllowRaw;
			allowSet = Parse(parsedAllowRaw);
		}
		return allowSet.Contains(key);
	}

	public static bool Denied(string key)
	{
		if (DenyRaw != parsedDenyRaw)
		{
			parsedDenyRaw = DenyRaw;
			denySet = Parse(parsedDenyRaw);
		}
		return denySet.Contains(key);
	}

	private static HashSet<string> Parse(string raw)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string key = part.Trim();
			if (key.Length > 0) set.Add(key);
		}
		return set;
	}
}
