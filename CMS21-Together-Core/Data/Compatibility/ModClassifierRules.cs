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

	public static ModClassifierRules Default => new ModClassifierRules
	{
		GameAssemblies = { "Assembly-CSharp", "Assembly-CSharp-firstpass" },
		VisualTargets = { "StringExtension.Localize", "Resources.Load" },
		VisualTypeSuffixes = { "Camera", "CameraController", "Flycam", "AudioController", "SaveIcon" },
		UiNamespaces = { "CMS.UI" },
		UiTypeSuffixes = { "Window", "Tab", "TabData", "Page", "PageManager", "Panel", "Bar" },
		UiVisualMethods = { "Draw*", "Redraw*", "Refresh*", "Prepare*Description*", "*Icon*", "Setup", "Activate", "OnCategoryChange", "FillItem" },
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
		};
	}

	public int Count =>
		GameAssemblies.Count + GameplayTargets.Count + VisualTargets.Count + VisualTypeSuffixes.Count
		+ UiNamespaces.Count + UiTypeSuffixes.Count + UiVisualMethods.Count;

	private static List<string> Union(List<string> a, List<string> b) =>
		(a ?? new List<string>()).Concat(b ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
