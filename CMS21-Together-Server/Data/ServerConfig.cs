using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data
{
	public class ServerConfig
	{
		private const string ConfigFileName = "server_config.ini";

		public int MaxPlayers { get; private set; } = 4;
		public bool UseSteam { get; private set; } = true;
		public string GsltToken { get; private set; } = string.Empty;
		public int LogLevel { get; private set; }
		public int Port { get; private set; } = NetworkConstants.DEFAULT_PORT;
		public int AutosaveIntervalSeconds { get; private set; } = 300;
		public int BackupCount { get; private set; } = 5;
		public int DesyncCheckIntervalSeconds { get; private set; } = 5;
		public bool DesyncAutofix { get; private set; } = true;
		public string ServerName { get; private set; } = "CMS21 Together Server";
		public string PublicAddress { get; private set; } = string.Empty;
		public string GameVersion { get; private set; } = "auto";
		public List<string> ModsRequired { get; private set; } = new List<string>();
		public List<string> ModsIgnored { get; private set; } = new List<string>();
		public List<string> ModsGameplay { get; private set; } = new List<string>();
		public bool TravelFees { get; private set; } = true;
		public int MaxCarSalePrice { get; private set; } = 5000000;
		public int MaxCarPurchasePrice { get; private set; } = 5000000;

		public string Password { get; private set; } = string.Empty;
		public bool PasswordSteam { get; private set; }
		public string AdminKey { get; private set; } = string.Empty;
		public Gamemode NewSessionDifficulty { get; private set; } = Gamemode.Normal;

		private static readonly string[][] HostingKeyLines =
		{
			new[] { "password", "# Password for DirectIP joins. Empty = none. Sent as plain text, do not reuse an important password", "password = \"\"" },
			new[] { "password_steam", "# Ask Steam joins for the password too (True/False)", "password_steam = False" },
			new[] { "admin_key", "# Players whose game sends this key (CMS21Together.AdminKey) may kick others. Empty = no admin", "admin_key = \"\"" },
			new[] { "new_session_difficulty", "# Difficulty of a new session when no save exists: Easy, Normal or Expert", "new_session_difficulty = Normal" },
		};

		private static readonly string[][] CompatibilityKeyLines =
		{
			new[] { "game_version", "# Game version clients must run: auto (database version, else the first client) or e.g. 1.0.40", "game_version = auto" },
			new[] { "mods_required", "# Gameplay mods every client must have and may use, comma separated: Name or Name@Version", "mods_required =" },
			new[] { "mods_ignored", "# Mods treated as visual (allowed) whatever the mod check finds, comma separated", "mods_ignored =" },
			new[] { "mods_gameplay", "# Mods treated as gameplay (refused unless required) whatever the mod check finds, comma separated", "mods_gameplay =" },
		};

		private static readonly string[][] EconomyKeyLines =
		{
			new[] { "travel_fees", "# Charge the travel fee for trips to the auction, junkyard and barns, for every player (True/False)", "travel_fees = True" },
			new[] { "max_car_sale_price", "# Highest price a player may sell a car for", "max_car_sale_price = 5000000" },
			new[] { "max_car_purchase_price", "# Highest price a player may pay for a car", "max_car_purchase_price = 5000000" },
		};

		public void ApplyArguments(string[] args)
		{
			for (int i = 0; i + 1 < args.Length; i++)
			{
				string value = args[i + 1];
				switch (args[i].ToLowerInvariant())
				{
					case "--port":
						if (int.TryParse(value, out int port)) Port = port;
						break;
					case "--max-players":
						if (int.TryParse(value, out int maxPlayers) && maxPlayers >= 1) MaxPlayers = maxPlayers;
						break;
					case "--use-steam":
						if (bool.TryParse(value, out bool useSteam)) UseSteam = useSteam;
						break;
					case "--server-name":
						ServerName = value;
						break;
					case "--public-address":
						PublicAddress = value;
						break;
					case "--password":
						Password = value;
						break;
					case "--admin-key":
						AdminKey = value;
						break;
					case "--new-difficulty":
						if (TryParseDifficulty(value, out var difficulty)) NewSessionDifficulty = difficulty;
						break;
				}
			}
		}

		public void SetPassword(string password) => Password = password ?? string.Empty;

		public static bool TryParseDifficulty(string value, out Gamemode difficulty)
		{
			if (!Enum.TryParse(value?.Replace("\"", "").Trim(), true, out difficulty) || !Enum.IsDefined(typeof(Gamemode), difficulty))
			{
				Logger.Warn($"Unknown difficulty '{value}' for new sessions; use Easy, Normal or Expert.");
				return false;
			}
			if (difficulty == Gamemode.Sandbox)
			{
				Logger.Warn("Sandbox is not supported for new sessions; use Easy, Normal or Expert.");
				return false;
			}
			return true;
		}

		private static string Masked(string secret) => string.IsNullOrEmpty(secret) ? "none" : "set";

		public string Describe() =>
			$"name '{ServerName}', port {Port}, max players {MaxPlayers}, steam {UseSteam}, public address '{PublicAddress}', autosave {AutosaveIntervalSeconds}s, backups {BackupCount}, " +
			$"password {Masked(Password)}{(PasswordSteam ? " (also Steam)" : "")}, admin key {Masked(AdminKey)}, new sessions {NewSessionDifficulty}, " +
			$"travel fees {TravelFees}, max car sale {MaxCarSalePrice}, max car purchase {MaxCarPurchasePrice}, game version {GameVersion}, mods required [{string.Join(", ", ModsRequired)}], ignored [{string.Join(", ", ModsIgnored)}], gameplay [{string.Join(", ", ModsGameplay)}]";

		public static ServerConfig LoadOrCreate()
		{
			string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

			if (!File.Exists(filePath))
			{
				Logger.Warn($"Configuration file '{ConfigFileName}' not found.");
				CreateDefaultConfig(filePath);
				return new ServerConfig();
			}

			Logger.Info($"Loading configuration from '{ConfigFileName}'...");
			return ParseConfig(filePath);
		}

		private static void CreateDefaultConfig(string path)
		{
			try
			{
				using (StreamWriter sw = new StreamWriter(path))
				{
					sw.WriteLine("# Maximum number of players allowed (mod is designed with 4 players in mind, higher might cause issue)");
					sw.WriteLine("max_players = 4");
					sw.WriteLine("");
					sw.WriteLine("# Name shown to joining players");
					sw.WriteLine("server_name = \"CMS21 Together Server\"");
					sw.WriteLine("");
					sw.WriteLine("# Address friends type to join (your public IP or host name), used for Steam join strings. Empty = none");
					sw.WriteLine("public_address = \"\"");
					sw.WriteLine("");
					sw.WriteLine("# TCP/UDP port for DirectIP connections");
					sw.WriteLine($"port = {NetworkConstants.DEFAULT_PORT}");
					sw.WriteLine("");
					sw.WriteLine("# Enable Steam Transport (True/False)");
					sw.WriteLine("use_steam = True");
					sw.WriteLine("");
					sw.WriteLine("# Game Server Login Token (GSLT)");
					sw.WriteLine("# Have in mind that if you dont own the game you cant use this (only anonymous will work)");
					sw.WriteLine("# Required for persistent ServerID. Leave empty \"\" for anonymous login.");
					sw.WriteLine("# Generate one here: https://steamcommunity.com/dev/managegameservers");
					sw.WriteLine("GSLT_Token = \"\"");
					sw.WriteLine("");
					sw.WriteLine("# Seconds between autosaves (only written when something changed). 0 = off");
					sw.WriteLine("autosave_interval_seconds = 300");
					sw.WriteLine("");
					sw.WriteLine("# Number of rotating backups of the save in Saves/backups");
					sw.WriteLine("backup_count = 5");
					sw.WriteLine("");
					sw.WriteLine("# Seconds between state comparisons with each player (desync detection)");
					sw.WriteLine("desync_check_interval_seconds = 5");
					sw.WriteLine("");
					sw.WriteLine("# Resend a section automatically when a player's state is confirmed out of sync");
					sw.WriteLine("desync_autofix = True");
					sw.WriteLine("");
					foreach (var lines in HostingKeyLines.Concat(CompatibilityKeyLines).Concat(EconomyKeyLines))
					{
						sw.WriteLine(lines[1]);
						sw.WriteLine(lines[2]);
						sw.WriteLine("");
					}
					sw.WriteLine("# Log Level Configuration");
					sw.WriteLine("# 0 = Base (Info, Warn, Error, Success)");
					sw.WriteLine("# 1 = Debug (Show all internal messages)");
					sw.WriteLine("log_level = 1");
				}
				Logger.Info($"Created default configuration file at: {path}");
				Logger.Warn("Please edit the config file to add your GSLT token if needed.");
			}
			catch (Exception ex)
			{
				Logger.Error($"Failed to create config file: {ex.Message}");
			}
		}

		private static ServerConfig ParseConfig(string path)
		{
			var config = new ServerConfig();
			var seenKeys = new HashSet<string>();

			try
			{
				foreach (string line in File.ReadAllLines(path))
				{
					string trimmedLine = line.Trim();
					if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
						continue;
					if (trimmedLine.Contains("#"))
						trimmedLine = trimmedLine.Split('#')[0].Trim();

					string[] parts = trimmedLine.Split('=');
					if (parts.Length != 2) continue;

					string key = parts[0].Trim().ToLowerInvariant();
					string value = parts[1].Trim();
					seenKeys.Add(key);

					switch (key)
					{
						case "use_steam":
							if (bool.TryParse(value, out bool useSteam)) config.UseSteam = useSteam;
							break;
						case "max_players":
							if (int.TryParse(value, out int maxPlayers)) config.MaxPlayers = maxPlayers;
							break;
						case "gslt_token":
							config.GsltToken = value.Replace("\"", "");
							break;
						case "log_level":
							if (int.TryParse(value, out int logLevel)) config.LogLevel = logLevel;
							break;
						case "port":
							if (int.TryParse(value, out int port)) config.Port = port;
							break;
						case "autosave_interval_seconds":
							if (int.TryParse(value, out int autosave) && autosave >= 0) config.AutosaveIntervalSeconds = autosave;
							break;
						case "desync_check_interval_seconds":
							if (int.TryParse(value, out int desyncInterval) && desyncInterval > 0) config.DesyncCheckIntervalSeconds = desyncInterval;
							break;
						case "desync_autofix":
							if (bool.TryParse(value, out bool autofix)) config.DesyncAutofix = autofix;
							break;
						case "server_name":
							config.ServerName = value.Replace("\"", "");
							break;
						case "public_address":
							config.PublicAddress = value.Replace("\"", "");
							break;
						case "backup_count":
							if (int.TryParse(value, out int backups) && backups >= 1) config.BackupCount = backups;
							break;
						case "game_version":
							string gameVersion = value.Replace("\"", "").Trim();
							config.GameVersion = gameVersion.Length == 0 ? "auto" : gameVersion;
							break;
						case "mods_required":
							config.ModsRequired = ParseList(value);
							break;
						case "mods_ignored":
							config.ModsIgnored = ParseList(value);
							break;
						case "travel_fees":
							if (bool.TryParse(value, out bool travelFees)) config.TravelFees = travelFees;
							break;
						case "max_car_sale_price":
							if (int.TryParse(value, out int maxSale) && maxSale > 0) config.MaxCarSalePrice = maxSale;
							break;
						case "max_car_purchase_price":
							if (int.TryParse(value, out int maxPurchase) && maxPurchase > 0) config.MaxCarPurchasePrice = maxPurchase;
							break;
						case "mods_gameplay":
							config.ModsGameplay = ParseList(value);
							break;
						case "password":
							config.Password = Unquote(value);
							break;
						case "password_steam":
							if (bool.TryParse(value, out bool passwordSteam)) config.PasswordSteam = passwordSteam;
							break;
						case "admin_key":
							config.AdminKey = Unquote(value);
							break;
						case "new_session_difficulty":
							if (TryParseDifficulty(value, out var difficulty)) config.NewSessionDifficulty = difficulty;
							break;
					}
				}

				Logger.Success("Configuration loaded successfully.");
				AppendMissingKeys(path, seenKeys);
			}
			catch (Exception ex)
			{
				Logger.Error($"Error reading configuration file: {ex.Message}. Using defaults.");
			}

			return config;
		}

		private static string Unquote(string value)
		{
			string trimmed = value.Trim();
			return trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\"") ? trimmed.Substring(1, trimmed.Length - 2) : trimmed;
		}

		private static List<string> ParseList(string value) =>
			value.Replace("\"", "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

		private static void AppendMissingKeys(string path, HashSet<string> seenKeys)
		{
			var missing = HostingKeyLines.Concat(CompatibilityKeyLines).Concat(EconomyKeyLines).Where(k => !seenKeys.Contains(k[0])).ToList();
			if (missing.Count == 0) return;

			var lines = new List<string>();
			foreach (var key in missing)
			{
				lines.Add("");
				lines.Add(key[1]);
				lines.Add(key[2]);
			}
			File.AppendAllLines(path, lines);
			Logger.Info($"Added missing settings to '{ConfigFileName}' with defaults: {string.Join(", ", missing.Select(k => k[0]))}");
		}
	}
}
