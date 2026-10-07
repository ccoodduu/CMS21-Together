using System;
using System.IO;
using CMS21_Together_Core.Logging;
using MelonLoader;
using Newtonsoft.Json.Linq;

namespace CMS21Together.Persistence;

public static class PlayerIdentity
{
	private static string storedKey;

	public static string FilePath => Path.Combine(MelonUtils.UserDataDirectory, "CMS21Together", "player.json");

	public static string Override { get; set; }

	public static string Key => !string.IsNullOrEmpty(Override) ? Override : storedKey ??= LoadOrCreate();

	public static string Short(string key) => string.IsNullOrEmpty(key) ? "none" : key.Length <= 8 ? key : key.Substring(0, 8);

	private static string LoadOrCreate()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				string existing = ReadKey();
				if (existing != null) return existing;

				string aside = $"{FilePath}.unreadable_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
				File.Move(FilePath, aside);
				Log.Warn($"[Identity] {FilePath} has no player key; moved it to {aside} and creating a new key.");
			}

			string key = Guid.NewGuid().ToString("N");
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			File.WriteAllText(FilePath, new JObject { ["PlayerKey"] = key }.ToString());
			Log.Info($"[Identity] Created player key {Short(key)} in {FilePath}.");
			return key;
		}
		catch (Exception ex)
		{
			Log.Error($"[Identity] Cannot read or create {FilePath}: {ex.Message}");
			return null;
		}
	}

	private static string ReadKey()
	{
		try
		{
			string key = JObject.Parse(File.ReadAllText(FilePath)).Value<string>("PlayerKey")?.Trim();
			return string.IsNullOrEmpty(key) ? null : key;
		}
		catch (Exception)
		{
			return null;
		}
	}
}
