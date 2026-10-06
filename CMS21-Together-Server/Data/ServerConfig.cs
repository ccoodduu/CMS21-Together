using System;
using System.IO;
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
						case "backup_count":
							if (int.TryParse(value, out int backups) && backups >= 1) config.BackupCount = backups;
							break;
					}
				}

				Logger.Success("Configuration loaded successfully.");
			}
			catch (Exception ex)
			{
				Logger.Error($"Error reading configuration file: {ex.Message}. Using defaults.");
			}

			return config;
		}
	}
}
