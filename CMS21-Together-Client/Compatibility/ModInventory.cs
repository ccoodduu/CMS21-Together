using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CMS21_Together_Core.Data.Compatibility;
using HarmonyLib;
using MelonLoader;

namespace CMS21Together.Compatibility;

public class PatchRecord
{
	public string Target;
	public string TargetAssembly;
	public string Kind;
	public string Owner;
	public string PatchAssembly;
}

public static class ModInventory
{
	public const int MaxTargetsPerMod = 200;
	public const string UnattributedName = "(unattributed)";
	private const string HarnessAssembly = "TogetherTestHarness";

	private static readonly string[] LoaderAssemblies = { "MelonLoader", "0Harmony", "UnhollowerBaseLib", "UnhollowerRuntimeLib", "Il2CppAssemblyUnhollower" };

	public static List<ModReport> Collect()
	{
		var own = typeof(MainMod).Assembly;
		var reports = new List<ModReport>();
		var byAssembly = new Dictionary<Assembly, ModReport>();

		foreach (var melon in Melons())
		{
			var assembly = melon.Assembly;
			if (assembly == null || IsExcluded(assembly, own) || byAssembly.ContainsKey(assembly)) continue;
			var report = new ModReport
			{
				Name = melon.Info?.Name ?? assembly.GetName().Name,
				Version = melon.Info?.Version,
				Author = melon.Info?.Author,
				File = SafeFileName(melon.Location),
				Assembly = assembly.GetName().Name,
			};
			byAssembly[assembly] = report;
			reports.Add(report);
		}

		ModReport unattributed = null;
		var seen = new HashSet<string>();
		foreach (var patch in Patches())
		{
			if (patch.PatchAssemblyRef == null || IsExcluded(patch.PatchAssemblyRef, own) || IsLoader(patch.PatchAssemblyRef)) continue;
			if (!byAssembly.TryGetValue(patch.PatchAssemblyRef, out var report))
			{
				if (unattributed == null)
				{
					unattributed = new ModReport { Name = UnattributedName };
					reports.Add(unattributed);
				}
				report = unattributed;
			}

			var target = patch.Method;
			string type = target.DeclaringType?.FullName ?? "?";
			if (!seen.Add($"{report.Name}|{type}|{target.Name}") || report.Targets.Count >= MaxTargetsPerMod) continue;
			report.Targets.Add(new PatchTarget(target.DeclaringType?.Assembly.GetName().Name, type, target.Name));
		}
		return reports;
	}

	public static List<PatchRecord> DescribePatches() => Patches().Select(p => new PatchRecord
	{
		Target = $"{p.Method.DeclaringType?.FullName}.{p.Method.Name}",
		TargetAssembly = p.Method.DeclaringType?.Assembly.GetName().Name,
		Kind = p.Kind,
		Owner = p.Owner,
		PatchAssembly = p.PatchAssemblyRef?.GetName().Name,
	}).ToList();

	public static List<string> DescribeMelons() => Melons()
		.Select(m => $"{m.Info?.Name} {m.Info?.Version} by {m.Info?.Author} ({m.Assembly?.GetName().Name}, {SafeFileName(m.Location)})")
		.ToList();

	private static IEnumerable<MelonBase> Melons() =>
		MelonHandler.Plugins.Cast<MelonBase>().Concat(MelonHandler.Mods);

	private class PatchEntry
	{
		public MethodBase Method;
		public string Kind;
		public string Owner;
		public Assembly PatchAssemblyRef;
	}

	private static IEnumerable<PatchEntry> Patches()
	{
		foreach (var method in HarmonyLib.Harmony.GetAllPatchedMethods().ToList())
		{
			var info = HarmonyLib.Harmony.GetPatchInfo(method);
			if (info == null) continue;
			foreach (var entry in Entries(method, "prefix", info.Prefixes)) yield return entry;
			foreach (var entry in Entries(method, "postfix", info.Postfixes)) yield return entry;
			foreach (var entry in Entries(method, "transpiler", info.Transpilers)) yield return entry;
			foreach (var entry in Entries(method, "finalizer", info.Finalizers)) yield return entry;
		}
	}

	private static IEnumerable<PatchEntry> Entries(MethodBase method, string kind, IEnumerable<Patch> patches)
	{
		if (patches == null) yield break;
		foreach (var patch in patches)
			yield return new PatchEntry { Method = method, Kind = kind, Owner = patch.owner, PatchAssemblyRef = patch.PatchMethod?.DeclaringType?.Assembly };
	}

	private static bool IsExcluded(Assembly assembly, Assembly own) =>
		assembly == own || assembly.GetName().Name == HarnessAssembly;

	private static bool IsLoader(Assembly assembly)
	{
		string name = assembly.GetName().Name;
		return LoaderAssemblies.Any(l => name.StartsWith(l, StringComparison.OrdinalIgnoreCase));
	}

	private static string SafeFileName(string location)
	{
		try { return string.IsNullOrEmpty(location) ? null : Path.GetFileName(location); }
		catch (Exception) { return null; }
	}
}
