using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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

		private const int StartCopiesKept = 3;
		private const int ReplaceAttempts = 3;

		private static readonly string SaveFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves");
		private static readonly string BackupFolderPath = Path.Combine(SaveFolderPath, "backups");
		private static readonly string CorruptFolderPath = Path.Combine(SaveFolderPath, "corrupt");
		private static readonly string DefaultSavePath = Path.Combine(SaveFolderPath, "server_save.json");
		private static readonly string TempSavePath = DefaultSavePath + ".tmp";

		public static readonly object StateLock = new object();
		private static readonly object saveGate = new object();

		private static volatile bool saveRequested;
		private static volatile bool forcedSaveRequested;
		private static float lastAutoSaveTime;
		private static string lastSavedHash;
		private static readonly Dictionary<string, JToken> unknownSections = new Dictionary<string, JToken>();

		public static ModGameState CurrentState { get; private set; }

		public static int BackupCount { get; set; } = 5;
		public static int AutosaveIntervalSeconds { get; set; } = 300;

		/// <summary>Loads the session before the server accepts connections. Throws when it must not start.</summary>
		public static void LoadOrCreateSession()
		{
			var candidates = GetLoadCandidates();
			if (candidates.Count == 0)
			{
				Logger.Info("No save found.");
				CreateNewSession();
				return;
			}

			byte[] mainBytesAtStart = File.Exists(DefaultSavePath) ? File.ReadAllBytes(DefaultSavePath) : null;
			var failures = new List<string>();

			foreach (string candidate in candidates)
			{
				try
				{
					var envelope = ReadEnvelope(JObject.Parse(File.ReadAllText(candidate)), candidate, true);
					LoadSections(envelope);
					Logger.Info($"Game session loaded from {candidate}.");

					if (candidate == DefaultSavePath)
					{
						WriteStartCopy(mainBytesAtStart);
						if (envelope.Value<int>("SaveVersion") < CurrentSaveVersion)
							SaveSession(true);
					}
					else
					{
						Logger.Warn($"Loaded the fallback {candidate}; saving it as the main save.");
						SaveSession(true);
					}
					return;
				}
				catch (SaveTooNewException ex)
				{
					throw new SessionLoadException($"{Path.GetFileName(candidate)}: {ex.Message}");
				}
				catch (Exception ex)
				{
					failures.Add($"{Path.GetFileName(candidate)}: {ex.Message}");
					Logger.Error($"Cannot load {candidate}: {ex.Message}");
					Quarantine(candidate);
				}
			}

			throw new SessionLoadException("Save files exist but none could be loaded (moved to Saves/corrupt): " + string.Join("; ", failures));
		}

		/// <summary>Loads a save file into memory without writing anything and returns a summary per section.</summary>
		public static List<string> CheckSave(string path)
		{
			var envelope = ReadEnvelope(JObject.Parse(File.ReadAllText(path)), path, false);
			LoadSections(envelope);

			var state = CurrentState;
			var inventory = state.InventoryState;
			var summary = new List<string>
			{
				$"file: {path}",
				$"save version: {envelope.Value<int?>("SaveVersion")} (sections: {string.Join(", ", ((JObject)envelope["Sections"]).Properties().Select(p => $"{p.Name} v{p.Value.Value<int?>("Version")}"))})",
				$"world: money {state.WorldState.Money}, level {state.WorldState.Level}, exp {state.WorldState.Exp}, scraps {state.WorldState.Scraps}, barns {state.WorldState.Barns}",
				$"garage: {state.GarageState.GarageUpgradeLevels.Count} garage upgrades, {state.GarageState.PlayerUpgradeLevels.Count} skills",
				$"inventory: {inventory.InventoryItems?.Count ?? 0} items, {inventory.InventoryGroupItems?.Count ?? 0} groups, warehouse {inventory.WarehouseItems?.Count ?? 0} items, {inventory.WarehouseGroupItems?.Count ?? 0} groups",
				$"cars: {state.CarState.LoadedCars.Count} loaded",
				$"players: {state.PlayerRecords.Count} records"
			};
			if (unknownSections.Count > 0)
				summary.Add($"unknown sections: {string.Join(", ", unknownSections.Keys)}");
			return summary;
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
			SaveSession(true);
		}

		public static void RequestSave(bool force = false)
		{
			if (force) forcedSaveRequested = true;
			saveRequested = true;
		}

		public static void Tick(float now)
		{
			if (AutosaveIntervalSeconds > 0 && now - lastAutoSaveTime >= AutosaveIntervalSeconds)
			{
				lastAutoSaveTime = now;
				saveRequested = true;
			}

			if (!saveRequested) return;
			bool force = forcedSaveRequested;
			saveRequested = false;
			forcedSaveRequested = false;
			SaveSession(force);
		}

		/// <summary>Writes the save if its sections changed (or when forced). Main loop or console-close handler only.</summary>
		public static bool SaveSession(bool force = false)
		{
			if (CurrentState == null) return false;

			lock (saveGate)
			{
				try
				{
					JObject envelope;
					lock (StateLock)
					{
						envelope = BuildEnvelope();
					}

					string hash = Hash(envelope["Sections"].ToString(Formatting.None));
					if (!force && hash == lastSavedHash && File.Exists(DefaultSavePath))
					{
						Logger.Debug("Session unchanged since the last save, nothing written.");
						return false;
					}

					Directory.CreateDirectory(BackupFolderPath);
					WriteDurably(TempSavePath, envelope.ToString(Formatting.Indented));

					if (File.Exists(DefaultSavePath))
					{
						RotateBackups();
						ReplaceWithRetry(TempSavePath, DefaultSavePath, GetBackupPath(1));
					}
					else
					{
						File.Move(TempSavePath, DefaultSavePath);
					}

					lastSavedHash = hash;
					Logger.Info($"Session successfully saved to: {DefaultSavePath}");
					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"Failed to save session: {ex.Message}");
					return false;
				}
			}
		}

		public static JObject BuildSaveCopy()
		{
			lock (StateLock)
			{
				return BuildEnvelope();
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

		private static JObject ReadEnvelope(JObject root, string sourcePath, bool writeMigrationCopy)
		{
			var saveVersion = root["SaveVersion"];
			if (saveVersion == null)
			{
				if (root["WorldState"] == null)
					throw new InvalidDataException("File has neither SaveVersion nor WorldState.");
				return MigrateFromV1(root, sourcePath, writeMigrationCopy);
			}

			int version = saveVersion.Value<int>();
			if (version > CurrentSaveVersion)
				throw new SaveTooNewException($"Save version {version} is newer than this server supports ({CurrentSaveVersion}). Update the server.");
			if (root["Sections"] is not JObject)
				throw new InvalidDataException("Save has no Sections object.");
			return root;
		}

		private static JObject MigrateFromV1(JObject root, string sourcePath, bool writeMigrationCopy)
		{
			if (writeMigrationCopy)
			{
				Directory.CreateDirectory(BackupFolderPath);
				string copyPath = Path.Combine(BackupFolderPath, $"premigration_v1_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
				File.Copy(sourcePath, copyPath, false);
				Logger.Warn($"Migrating v1 save to v{CurrentSaveVersion}. Original copied to {copyPath}");
			}

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
				["SaveVersion"] = 1,
				["Sections"] = sections
			};

			JObject Entry(JToken data) => new JObject { ["Version"] = 1, ["Data"] = data };
		}

		private static void LoadSections(JObject envelope)
		{
			var sections = (JObject)envelope["Sections"];
			var known = new HashSet<string>(SessionRegistry.SaveSections.Select(s => s.Key));

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
				CurrentState = new ModGameState();
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

				lastSavedHash = Hash(BuildEnvelope()["Sections"].ToString(Formatting.None));
			}
		}

		private static List<string> GetLoadCandidates()
		{
			var candidates = new List<string>();
			if (File.Exists(DefaultSavePath)) candidates.Add(DefaultSavePath);
			for (int i = 1; i <= BackupCount; i++)
			{
				if (File.Exists(GetBackupPath(i))) candidates.Add(GetBackupPath(i));
			}
			if (Directory.Exists(BackupFolderPath))
			{
				candidates.AddRange(Directory.GetFiles(BackupFolderPath, "start_*.json")
					.OrderByDescending(Path.GetFileName, StringComparer.Ordinal));
			}
			return candidates;
		}

		private static void WriteStartCopy(byte[] mainBytes)
		{
			if (mainBytes == null) return;
			try
			{
				Directory.CreateDirectory(BackupFolderPath);
				string path = Path.Combine(BackupFolderPath, $"start_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
				File.WriteAllBytes(path, mainBytes);

				foreach (string old in Directory.GetFiles(BackupFolderPath, "start_*.json")
					         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
					         .Skip(StartCopiesKept))
					File.Delete(old);
			}
			catch (Exception ex)
			{
				Logger.Warn($"Could not write the start copy of the save: {ex.Message}");
			}
		}

		private static void Quarantine(string path)
		{
			try
			{
				Directory.CreateDirectory(CorruptFolderPath);
				string target = Path.Combine(CorruptFolderPath, $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{Path.GetExtension(path)}");
				File.Move(path, target);
				Logger.Warn($"Moved {Path.GetFileName(path)} to {target}");
			}
			catch (Exception ex)
			{
				Logger.Error($"Could not move {path} to Saves/corrupt: {ex.Message}");
			}
		}

		private static void WriteDurably(string path, string content)
		{
			using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				byte[] bytes = new UTF8Encoding(false).GetBytes(content);
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush(true);
			}
		}

		private static void RotateBackups()
		{
			string oldest = GetBackupPath(BackupCount);
			if (File.Exists(oldest)) File.Delete(oldest);

			for (int i = BackupCount - 1; i >= 1; i--)
			{
				string from = GetBackupPath(i);
				if (File.Exists(from)) File.Move(from, GetBackupPath(i + 1));
			}
		}

		private static void ReplaceWithRetry(string source, string destination, string backup)
		{
			for (int attempt = 1; ; attempt++)
			{
				try
				{
					File.Replace(source, destination, backup);
					return;
				}
				catch (IOException) when (attempt < ReplaceAttempts)
				{
					Thread.Sleep(200);
				}
			}
		}

		private static string GetBackupPath(int index)
		{
			return Path.Combine(BackupFolderPath, $"server_save_bak{index}.json");
		}

		private static string Hash(string text)
		{
			using (var sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
		}
	}

	public class SaveTooNewException : Exception
	{
		public SaveTooNewException(string message) : base(message) { }
	}

	public class SessionLoadException : Exception
	{
		public SessionLoadException(string message) : base(message) { }
	}
}
