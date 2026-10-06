using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data
{
	public static class GameDataManager
	{
		public const string SaveFormat = "cms21-together-server-save";
		public const int CurrentSaveVersion = 2;

		private static readonly string SaveFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves");
		private static readonly string BackupFolderPath = Path.Combine(SaveFolderPath, "backups");
		private static readonly string DefaultSavePath = Path.Combine(SaveFolderPath, "server_save.json");
		private const int MaxBackups = 3;

		public static float lastAutoSaveTime;
		public const float AutoSaveInterval = 300f;

		public static readonly object StateLock = new object();

		private static volatile bool saveRequested;
		private static readonly Dictionary<string, JToken> unknownSections = new Dictionary<string, JToken>();

		public static ModGameState CurrentState { get; private set; }

		public static void TryLoadSession(string path = null)
		{
			string targetPath = path ?? DefaultSavePath;

			if (File.Exists(targetPath))
			{
				try
				{
					var root = JObject.Parse(File.ReadAllText(targetPath));
					var envelope = ReadEnvelope(root, targetPath);
					LoadSections(envelope);
					Logger.Info($"Game session loaded from {targetPath} (save v{envelope.Value<int>("SaveVersion")}).");
					return;
				}
				catch (SaveTooNewException)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logger.Error($"Error loading save file: {ex.Message}. Creating new session instead.");
				}
			}
			CreateNewSession();
		}

		public static void CreateNewSession()
		{
			lock (StateLock)
			{
				CurrentState = new ModGameState();
				unknownSections.Clear();
				foreach (var section in SessionRegistry.SaveSections)
					section.Reset();
			}
			Logger.Info("Created a new game session.");
			SaveSession();
		}

		public static void RequestSave()
		{
			saveRequested = true;
		}

		public static void ProcessPendingSave()
		{
			if (!saveRequested) return;
			saveRequested = false;
			SaveSession();
		}

		public static void SaveSession()
		{
			if (CurrentState == null) return;

			try
			{
				string json;
				lock (StateLock)
				{
					json = BuildEnvelope().ToString(Formatting.Indented);
				}

				if (!Directory.Exists(SaveFolderPath))
					Directory.CreateDirectory(SaveFolderPath);

				RotateBackups();
				File.WriteAllText(DefaultSavePath, json);

				Logger.Info($"Session successfully saved to: {DefaultSavePath}");
			}
			catch (Exception ex)
			{
				Logger.Error($"Failed to save session: {ex.Message}");
			}
		}

		private static JObject BuildEnvelope()
		{
			var sections = new JObject();
			foreach (var section in SessionRegistry.SaveSections)
			{
				sections[section.Key] = new JObject
				{
					["Version"] = section.Version,
					["Data"] = section.Save()
				};
			}
			foreach (var unknown in unknownSections)
				sections[unknown.Key] = unknown.Value.DeepClone();

			return new JObject
			{
				["Format"] = SaveFormat,
				["SaveVersion"] = CurrentSaveVersion,
				["ModVersion"] = Program.MOD_VERSION,
				["SavedAtUtc"] = DateTime.UtcNow.ToString("o"),
				["Sections"] = sections
			};
		}

		private static JObject ReadEnvelope(JObject root, string sourcePath)
		{
			var saveVersion = root["SaveVersion"];
			if (saveVersion == null)
			{
				if (root["WorldState"] == null)
					throw new InvalidDataException("File has neither SaveVersion nor WorldState.");
				return MigrateFromV1(root, sourcePath);
			}

			int version = saveVersion.Value<int>();
			if (version > CurrentSaveVersion)
				throw new SaveTooNewException($"Save version {version} is newer than this server supports ({CurrentSaveVersion}). Update the server.");
			if (root["Sections"] is not JObject)
				throw new InvalidDataException("Save has no Sections object.");
			return root;
		}

		private static JObject MigrateFromV1(JObject root, string sourcePath)
		{
			Directory.CreateDirectory(BackupFolderPath);
			string copyPath = Path.Combine(BackupFolderPath, $"premigration_v1_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
			File.Copy(sourcePath, copyPath, false);
			Logger.Warn($"Migrating v1 save to v{CurrentSaveVersion}. Original copied to {copyPath}");

			var world = (JObject)root["WorldState"].DeepClone();
			world.Remove("updateGamemode");
			var garage = (root["GarageState"] as JObject ?? new JObject()).DeepClone() as JObject;
			garage.Remove("AvailablePoints");

			var sections = new JObject
			{
				[SyncOrder.WorldKey] = Entry(world),
				[SyncOrder.GarageKey] = Entry(garage)
			};
			if (root["InventoryState"] is JObject inventory)
				sections[SyncOrder.InventoryKey] = Entry(inventory.DeepClone());
			if (root["CarState"] is JObject cars)
				sections[SyncOrder.CarsKey] = Entry(cars.DeepClone());

			return new JObject
			{
				["Format"] = SaveFormat,
				["SaveVersion"] = CurrentSaveVersion,
				["Sections"] = sections
			};

			JObject Entry(JToken data) => new JObject { ["Version"] = 1, ["Data"] = data };
		}

		private static void LoadSections(JObject envelope)
		{
			var sections = (JObject)envelope["Sections"];
			var known = new HashSet<string>(SessionRegistry.SaveSections.Select(s => s.Key));

			var state = new ModGameState();
			var unknown = new Dictionary<string, JToken>();
			var loaded = new List<(ISaveSection Section, JToken Data)>();

			foreach (var section in SessionRegistry.SaveSections)
			{
				if (!(sections[section.Key] is JObject entry))
				{
					loaded.Add((section, null));
					continue;
				}

				int version = entry.Value<int?>("Version") ?? 1;
				if (version > section.Version)
					throw new SaveTooNewException($"Section '{section.Key}' is v{version}, this server supports v{section.Version}. Update the server.");

				JToken data = entry["Data"] ?? new JObject();
				for (int v = version; v < section.Version; v++)
				{
					data = section.Migrate(data, v);
					Logger.Info($"Migrated section '{section.Key}' v{v} -> v{v + 1}.");
				}
				loaded.Add((section, data));
			}

			foreach (var property in sections.Properties())
			{
				if (known.Contains(property.Name)) continue;
				unknown[property.Name] = property.Value.DeepClone();
				Logger.Warn($"Unknown save section '{property.Name}' kept as is and written back on save.");
			}

			lock (StateLock)
			{
				CurrentState = state;
				unknownSections.Clear();
				foreach (var pair in unknown)
					unknownSections[pair.Key] = pair.Value;

				foreach (var (section, data) in loaded)
				{
					if (data == null)
					{
						section.Reset();
						Logger.Info($"Section '{section.Key}' missing from save, reset to defaults.");
					}
					else
					{
						section.Load(data);
					}
				}
			}
		}

		private static void RotateBackups()
		{
			if (!File.Exists(DefaultSavePath)) return;

			for (int i = MaxBackups - 1; i >= 1; i--)
			{
				string oldPath = GetBackupPath(i);
				string newPath = GetBackupPath(i + 1);

				if (File.Exists(oldPath))
				{
					if (File.Exists(newPath)) File.Delete(newPath);
					File.Move(oldPath, newPath);
				}
			}

			string firstBackup = GetBackupPath(1);
			if (File.Exists(firstBackup)) File.Delete(firstBackup);
			File.Move(DefaultSavePath, firstBackup);
		}

		private static string GetBackupPath(int index)
		{
			return Path.Combine(SaveFolderPath, $"server_save_bak{index}.json");
		}
	}

	public class SaveTooNewException : Exception
	{
		public SaveTooNewException(string message) : base(message) { }
	}
}
