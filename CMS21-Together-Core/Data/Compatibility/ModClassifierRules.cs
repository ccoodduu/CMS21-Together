using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Data.Compatibility;

[Serializable]
public class ModClassifierRules
{
	public List<string> GameAssemblies = new List<string>();
	public List<string> GameplayTargets = new List<string>();
	public List<string> VisualTargets = new List<string>();
	public List<string> VisualTypeSuffixes = new List<string>();
	public List<string> UiNamespaces = new List<string>();
	public List<string> UiTypeSuffixes = new List<string>();
	public List<string> UiVisualMethods = new List<string>();
	public Dictionary<string, KnownMod> KnownMods = new Dictionary<string, KnownMod>(StringComparer.OrdinalIgnoreCase);

	public static ModClassifierRules Default => new ModClassifierRules
	{
		GameAssemblies = { "Assembly-CSharp", "Assembly-CSharp-firstpass" },
		VisualTargets = { "StringExtension.Localize", "Resources.Load" },
		VisualTypeSuffixes = { "Camera", "CameraController", "Flycam", "AudioController", "SaveIcon" },
		UiNamespaces = { "CMS.UI" },
		UiTypeSuffixes = { "Window", "Tab", "TabData", "Page", "PageManager", "Panel", "Bar" },
		UiVisualMethods = { "Draw*", "Redraw*", "Refresh*", "Prepare*Description*", "*Icon*", "Setup", "Activate", "OnCategoryChange", "FillItem" },
		KnownMods =
		{
			["Autosave Mod"] = new KnownMod(ModClass.Gameplay, "runs the game's save on a timer and after each job"),
			["CMS21 Load Optimizer"] = new KnownMod(ModClass.Visual, "speeds up loading"),
			["Lvx Better Car Spawns"] = new KnownMod(ModClass.Gameplay, "changes which cars the junkyard, barn and auctions offer"),
			["Lvx Owned Cars Only"] = new KnownMod(ModClass.Gameplay, "changes which cars the junkyard, barn and auctions offer"),
			["QoLmod"] = new KnownMod(ModClass.Gameplay, "changes inventory, repairs, minigames, shops and more"),
			["QuickShop"] = new KnownMod(ModClass.Gameplay, "buys parts by hotkey"),
			["TK Aftermarket"] = new KnownMod(ModClass.Gameplay, "adds aftermarket parts"),
			["TK Basics"] = new KnownMod(ModClass.Gameplay, "changes inventory, parts and repairs"),
		},
	};

	public static ModClassifierRules FromJson(string json) =>
		JsonConvert.DeserializeObject<ModClassifierRules>(json) ?? new ModClassifierRules();

	public ModClassifierRules Merge(ModClassifierRules extra)
	{
		if (extra == null) return this;
		return new ModClassifierRules
		{
			GameAssemblies = Union(GameAssemblies, extra.GameAssemblies),
			GameplayTargets = Union(GameplayTargets, extra.GameplayTargets),
			VisualTargets = Union(VisualTargets, extra.VisualTargets),
			VisualTypeSuffixes = Union(VisualTypeSuffixes, extra.VisualTypeSuffixes),
			UiNamespaces = Union(UiNamespaces, extra.UiNamespaces),
			UiTypeSuffixes = Union(UiTypeSuffixes, extra.UiTypeSuffixes),
			UiVisualMethods = Union(UiVisualMethods, extra.UiVisualMethods),
			KnownMods = Overlay(KnownMods, extra.KnownMods),
		};
	}

	public int Count =>
		GameAssemblies.Count + GameplayTargets.Count + VisualTargets.Count + VisualTypeSuffixes.Count
		+ UiNamespaces.Count + UiTypeSuffixes.Count + UiVisualMethods.Count + (KnownMods?.Count ?? 0);

	private static List<string> Union(List<string> a, List<string> b) =>
		(a ?? new List<string>()).Concat(b ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

	private static Dictionary<string, KnownMod> Overlay(Dictionary<string, KnownMod> a, Dictionary<string, KnownMod> b)
	{
		var result = new Dictionary<string, KnownMod>(StringComparer.OrdinalIgnoreCase);
		foreach (var source in new[] { a, b })
			if (source != null)
				foreach (var entry in source)
					result[entry.Key] = entry.Value;
		return result;
	}
}

[Serializable]
public class KnownMod
{
	public ModClass Class;
	public string Reason;

	public KnownMod() { }

	public KnownMod(ModClass modClass, string reason)
	{
		Class = modClass;
		Reason = reason;
	}
}
