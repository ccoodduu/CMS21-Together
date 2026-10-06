using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Data.Compatibility;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Data
{
	public static class ModCheck
	{
		private const string ExpectedFile = "expected.json";

		public static int Run(string path)
		{
			try
			{
				var files = Directory.Exists(path)
					? Directory.GetFiles(path, "*.json").Where(f => !string.Equals(Path.GetFileName(f), ExpectedFile, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList()
					: new List<string> { path };
				var mods = files.SelectMany(f => JsonConvert.DeserializeObject<List<ModReport>>(File.ReadAllText(f)) ?? new List<ModReport>()).ToList();
				var verdicts = new ModClassifier(ModClassifierRules.Default).ClassifyAll(mods);

				string expectedPath = Directory.Exists(path) ? Path.Combine(path, ExpectedFile) : null;
				var expected = expectedPath != null && File.Exists(expectedPath)
					? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(expectedPath))
					: new Dictionary<string, string>();

				int mismatches = 0;
				foreach (var verdict in verdicts)
				{
					string status = "";
					if (expected.TryGetValue(verdict.Mod.Name, out string want))
					{
						bool ok = string.Equals(want, verdict.Class.ToString(), StringComparison.OrdinalIgnoreCase);
						if (!ok) mismatches++;
						status = ok ? " ok" : $" EXPECTED {want}";
					}
					Console.WriteLine($"{verdict.Mod.Name,-24} {verdict.Class,-8} {verdict.Mod.Targets?.Count ?? 0,4} targets{status}  {string.Join(", ", verdict.Reasons)}");
				}
				foreach (string missing in expected.Keys.Where(k => verdicts.All(v => v.Mod.Name != k)))
				{
					mismatches++;
					Console.WriteLine($"{missing,-24} MISSING from the fixtures");
				}

				Console.WriteLine(mismatches == 0 ? "OK" : $"FAILED: {mismatches} mismatches");
				return mismatches == 0 ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"FAILED: {ex.Message}");
				return 1;
			}
		}
	}
}
