using System.Collections.Generic;
using System.Linq;

namespace CMS21_Together_Core.Data.Outdoor;

public static class OutdoorDigestRow
{
	public const string CarPrefix = "car:";
	public const string LayoutPrefix = "layout:";
	public const string CarIdField = "id";

	public static string Build(string key, IEnumerable<KeyValuePair<string, string>> fields) =>
		key + string.Concat(fields.Select(f => $"|{f.Key}={Clean(f.Value)}"));

	public static string Car(int index, IEnumerable<KeyValuePair<string, string>> fields) => Build(CarPrefix + index, fields);

	public static (string Key, Dictionary<string, string> Fields) Parse(string row)
	{
		var parts = (row ?? "").Split('|');
		var fields = new Dictionary<string, string>();
		foreach (string part in parts.Skip(1))
		{
			int eq = part.IndexOf('=');
			if (eq > 0) fields[part.Substring(0, eq)] = part.Substring(eq + 1);
		}
		return (parts[0], fields);
	}

	public static List<string> Compare(IEnumerable<string> reference, IEnumerable<string> other)
	{
		var expected = (reference ?? Enumerable.Empty<string>()).Select(Parse).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First().Fields);
		var actual = (other ?? Enumerable.Empty<string>()).Select(Parse).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First().Fields);
		var differences = new List<string>();
		foreach (var key in expected.Keys.Union(actual.Keys).OrderBy(k => k, System.StringComparer.Ordinal))
		{
			if (!expected.TryGetValue(key, out var want)) { differences.Add($"{key} extra"); continue; }
			if (!actual.TryGetValue(key, out var got)) { differences.Add($"{key} missing"); continue; }
			var fields = want.Keys.Union(got.Keys).OrderBy(f => f, System.StringComparer.Ordinal)
				.Where(f => !want.TryGetValue(f, out var a) || !got.TryGetValue(f, out var b) || a != b).ToList();
			if (fields.Count > 0)
				differences.Add($"{key} {string.Join(", ", fields.Select(f => $"{f} {Value(want, f)} vs {Value(got, f)}"))}");
		}
		return differences;
	}

	private static string Value(Dictionary<string, string> fields, string name) => fields.TryGetValue(name, out var value) ? value : "-";

	private static string Clean(string value) => (value ?? "").Replace("|", "/").Replace("=", ":");
}
