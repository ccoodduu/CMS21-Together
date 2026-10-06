using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CMS21_Together_Core.Data.Compatibility;

public enum ModClass
{
	Ignored,
	Visual,
	Unknown,
	Gameplay
}

public class ModVerdict
{
	public ModReport Mod;
	public ModClass Class;
	public List<string> Reasons = new List<string>();

	public override string ToString() =>
		Reasons.Count == 0 ? $"{Mod} [{Class}]" : $"{Mod} [{Class}: {string.Join(", ", Reasons)}]";
}

public class ModClassifier
{
	public const int MaxReasons = 5;

	private readonly ModClassifierRules rules;
	private readonly HashSet<string> gameAssemblies;
	private readonly Regex[] gameplayTargets;
	private readonly Regex[] visualTargets;
	private readonly Regex[] uiVisualMethods;

	public ModClassifier(ModClassifierRules rules)
	{
		this.rules = rules ?? ModClassifierRules.Default;
		gameAssemblies = new HashSet<string>(this.rules.GameAssemblies, StringComparer.OrdinalIgnoreCase);
		gameplayTargets = this.rules.GameplayTargets.Select(Wildcard).ToArray();
		visualTargets = this.rules.VisualTargets.Select(Wildcard).ToArray();
		uiVisualMethods = this.rules.UiVisualMethods.Select(Wildcard).ToArray();
	}

	public List<ModVerdict> ClassifyAll(IList<ModReport> mods)
	{
		var melonAssemblies = new HashSet<string>(
			mods.Where(m => !string.IsNullOrEmpty(m.Assembly)).Select(m => m.Assembly), StringComparer.OrdinalIgnoreCase);
		return mods.Select(m => Classify(m, melonAssemblies)).ToList();
	}

	public ModVerdict Classify(ModReport mod, ISet<string> melonAssemblies)
	{
		var verdict = new ModVerdict { Mod = mod, Class = ModClass.Visual };
		foreach (var target in mod.Targets ?? new List<PatchTarget>())
		{
			var targetClass = ClassifyTarget(target, melonAssemblies);
			if (targetClass > verdict.Class) verdict.Class = targetClass;
			if (targetClass >= ModClass.Unknown && verdict.Reasons.Count < MaxReasons)
				verdict.Reasons.Add(target.ToString());
		}
		return verdict;
	}

	public ModClass ClassifyTarget(PatchTarget target, ISet<string> melonAssemblies)
	{
		if (!gameAssemblies.Contains(target.Assembly ?? ""))
			return melonAssemblies != null && melonAssemblies.Contains(target.Assembly ?? "") ? ModClass.Ignored : ModClass.Visual;

		string simple = $"{target.SimpleTypeName}.{target.Method}";
		string full = $"{target.Type}.{target.Method}";
		if (gameplayTargets.Any(r => r.IsMatch(simple) || r.IsMatch(full))) return ModClass.Gameplay;
		if (visualTargets.Any(r => r.IsMatch(simple) || r.IsMatch(full))) return ModClass.Visual;

		string outerType = target.SimpleTypeName.Split('+')[0];
		if (rules.VisualTypeSuffixes.Any(s => outerType.EndsWith(s, StringComparison.Ordinal))) return ModClass.Visual;

		if (IsUiType(target.Type ?? "", outerType))
			return uiVisualMethods.Any(r => r.IsMatch(target.Method ?? "")) ? ModClass.Visual : ModClass.Unknown;

		return ModClass.Gameplay;
	}

	private bool IsUiType(string fullType, string outerType) =>
		rules.UiNamespaces.Any(ns => fullType.StartsWith(ns, StringComparison.Ordinal))
		|| rules.UiTypeSuffixes.Any(s => outerType.EndsWith(s, StringComparison.Ordinal));

	private static Regex Wildcard(string pattern) =>
		new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
