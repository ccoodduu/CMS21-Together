using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Core.Diagnostics;

public static class Redaction
{
	public const string Redacted = "<redacted>";
	public const string PlayersSection = "players";
	private const int MinScrubLength = 4;

	private static readonly string[] SecretWords = { "token", "password", "secret", "key" };

	public static bool IsSecretName(string name)
	{
		if (string.IsNullOrEmpty(name) || name.IndexOf("Hotkey", StringComparison.OrdinalIgnoreCase) >= 0) return false;
		return SecretWords.Any(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	public static string ConfigText(string text) => MapLines(text, RedactLine);

	public static string PreferenceCategories(string toml, string categoryPrefix)
	{
		bool keep = false;
		var kept = new StringBuilder();
		foreach (string line in Lines(toml))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith("["))
				keep = trimmed.Trim('[', ']', ' ').Trim('"').StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase);
			if (keep) kept.Append(line).Append("\r\n");
		}
		return kept.ToString();
	}

	public static string Scrub(string text, IEnumerable<string> secrets)
	{
		if (string.IsNullOrEmpty(text) || secrets == null) return text;
		foreach (string secret in secrets.Where(s => s != null && s.Length >= MinScrubLength).Distinct().OrderByDescending(s => s.Length))
			text = text.Replace(secret, Redacted);
		return text;
	}

	public static void RedactJson(JToken token)
	{
		foreach (var property in Properties(token))
			if (IsSecretName(property.Name) && property.Value.Type != JTokenType.Null && !(property.Value.Type == JTokenType.String && (string)property.Value == ""))
				property.Value = Redacted;
	}

	public static void DropPlayerKeys(JObject saveEnvelope)
	{
		if (!(saveEnvelope?["Sections"]?[PlayersSection] is JObject players)) return;
		foreach (var property in Properties(players["Data"]).Where(p => IsSecretName(p.Name)).ToList())
			property.Remove();
	}

	public static IEnumerable<string> SecretValues(JToken token) =>
		Properties(token).Where(p => IsSecretName(p.Name) && p.Value.Type == JTokenType.String).Select(p => (string)p.Value);

	private static IEnumerable<JProperty> Properties(JToken token) =>
		token is JContainer container ? container.DescendantsAndSelf().OfType<JProperty>().ToList() : Enumerable.Empty<JProperty>();

	private static string RedactLine(string line)
	{
		string trimmed = line.TrimStart();
		if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[') return line;
		int equals = line.IndexOf('=');
		if (equals <= 0) return line;
		string name = line.Substring(0, equals).Trim().Trim('"');
		string value = line.Substring(equals + 1).Trim();
		if (!IsSecretName(name) || value.Length == 0 || value == "\"\"") return line;
		return $"{line.Substring(0, equals).TrimEnd()} = {(value.StartsWith("\"") ? $"\"{Redacted}\"" : Redacted)}";
	}

	private static string MapLines(string text, Func<string, string> map)
	{
		if (string.IsNullOrEmpty(text)) return text;
		var result = new StringBuilder(text.Length);
		foreach (string line in Lines(text)) result.Append(map(line)).Append("\r\n");
		return result.ToString();
	}

	private static IEnumerable<string> Lines(string text) =>
		(text ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}
