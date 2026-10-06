using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CMS21_Together_Core.Data.Compatibility;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data
{
	public static class CompatibilityPolicy
	{
		private const int RefusalHistory = 10;

		private static readonly Queue<string> refusals = new Queue<string>();
		private static ServerConfig config;
		private static ModClassifier classifier = new ModClassifier(ModClassifierRules.Default);
		private static string rulesSource = "default rules";

		public static string ReferenceGameVersion { get; private set; }
		public static string GameVersionSource { get; private set; } = "not set yet";

		public static void Initialize(ServerConfig serverConfig)
		{
			config = serverConfig;
			if (!string.Equals(config.GameVersion, "auto", StringComparison.OrdinalIgnoreCase))
			{
				ReferenceGameVersion = config.GameVersion;
				GameVersionSource = "configured";
			}
			else if (!string.IsNullOrWhiteSpace(GameDatabase.Meta?.GameVersion))
			{
				ReferenceGameVersion = GameDatabase.Meta.GameVersion;
				GameVersionSource = "database";
			}
			else
			{
				GameVersionSource = "pinned by the first client";
			}

			classifier = new ModClassifier(LoadRules());
			Logger.Info($"Compatibility: protocol {ProtocolHash.Value}, game version {ReferenceGameVersion ?? "(first client)"} ({GameVersionSource}), {rulesSource}.");
		}

		private static ModClassifierRules LoadRules()
		{
			var rules = ModClassifierRules.Default;
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database/mod_rules.json");
			if (!File.Exists(path)) return rules;

			try
			{
				var extra = ModClassifierRules.FromJson(File.ReadAllText(path));
				rulesSource = $"default rules + {extra.Count} entries from Database/mod_rules.json";
				return rules.Merge(extra);
			}
			catch (Exception ex)
			{
				Logger.Error($"Cannot read Database/mod_rules.json, using the default mod rules: {ex.Message}");
				return rules;
			}
		}

		public static bool Evaluate(int clientId, ConnectPacket packet)
		{
			var findings = new List<string>();
			var reason = DisconnectReason.None;

			void Fail(DisconnectReason failure, string finding)
			{
				if (reason == DisconnectReason.None) reason = failure;
				findings.Add(finding);
			}

			if (packet.modVersion != Program.MOD_VERSION)
				Fail(DisconnectReason.VersionMismatch, $"This server runs Together {Program.MOD_VERSION}; you have {packet.modVersion}.");
			else if (packet.protocolHash != ProtocolHash.Value)
				Fail(DisconnectReason.VersionMismatch,
					$"You and the server both run Together {Program.MOD_VERSION}, but the builds differ (server protocol {ProtocolHash.Value}, yours {packet.protocolHash ?? "none"}). Install the host's build.");

			if (ReferenceGameVersion != null && packet.gameVersion != ReferenceGameVersion)
				Fail(DisconnectReason.GameVersionMismatch,
					$"The server expects game version {ReferenceGameVersion} ({GameVersionSource}); you have {(string.IsNullOrEmpty(packet.gameVersion) ? "an unknown version" : packet.gameVersion)}.");

			var mods = packet.mods ?? new List<ModReport>();
			var verdicts = ApplyLists(clientId, classifier.ClassifyAll(mods));
			foreach (var verdict in verdicts.Where(v => v.Class >= ModClass.Unknown && !IsRequired(v.Mod.Name)))
			{
				string what = verdict.Class == ModClass.Gameplay ? "changes gameplay" : "may change gameplay";
				string targets = verdict.Reasons.Count > 0 ? $" ({string.Join(", ", verdict.Reasons)})" : "";
				Fail(DisconnectReason.ModMismatch, $"{verdict.Mod} {what}{targets}. Remove it or ask the host to allow it.");
			}
			foreach (string required in config?.ModsRequired ?? new List<string>())
			{
				SplitRequired(required, out string name, out string version);
				var mod = mods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
				if (mod == null)
					Fail(DisconnectReason.ModMismatch, $"The server requires the mod {required}; you do not have it.");
				else if (version != null && mod.Version != version)
					Fail(DisconnectReason.ModMismatch, $"The server requires {name} {version}; you have {mod.Version}.");
			}

			Logger.Info($"Client[{clientId}] '{packet.username}': Together {packet.modVersion} (protocol {packet.protocolHash ?? "none"}), " +
			            $"game {packet.gameVersion ?? "?"}, DLC {SharedDlc.Format(packet.dlc)}, mods: {(verdicts.Count == 0 ? "none" : string.Join("; ", verdicts))}");

			if (reason != DisconnectReason.None)
			{
				string message = string.Join("\n", findings);
				Remember($"{DateTime.Now:HH:mm:ss} client {clientId} '{packet.username}': {reason} - {string.Join(" | ", findings)}");
				Server.Refuse(clientId, reason, message);
				return false;
			}

			if (ReferenceGameVersion == null && !string.IsNullOrEmpty(packet.gameVersion))
			{
				ReferenceGameVersion = packet.gameVersion;
				Logger.Info($"Game version pinned to {ReferenceGameVersion} by client {clientId}.");
			}
			return true;
		}

		private static List<ModVerdict> ApplyLists(int clientId, List<ModVerdict> verdicts)
		{
			foreach (var verdict in verdicts)
			{
				if (Contains(config?.ModsIgnored, verdict.Mod.Name))
				{
					if (verdict.Class >= ModClass.Unknown)
						Logger.Info($"Client[{clientId}]: {verdict.Mod} ({verdict.Class}) ignored by configuration.");
					verdict.Class = ModClass.Visual;
					verdict.Reasons.Clear();
				}
				else if (Contains(config?.ModsGameplay, verdict.Mod.Name))
				{
					verdict.Class = ModClass.Gameplay;
					if (verdict.Reasons.Count == 0) verdict.Reasons.Add("listed in mods_gameplay");
				}
			}
			return verdicts;
		}

		private static bool IsRequired(string name) =>
			(config?.ModsRequired ?? new List<string>()).Any(r =>
			{
				SplitRequired(r, out string requiredName, out _);
				return string.Equals(requiredName, name, StringComparison.OrdinalIgnoreCase);
			});

		private static void SplitRequired(string entry, out string name, out string version)
		{
			int at = entry.LastIndexOf('@');
			name = at > 0 ? entry.Substring(0, at).Trim() : entry.Trim();
			version = at > 0 ? entry.Substring(at + 1).Trim() : null;
		}

		private static bool Contains(List<string> list, string name) =>
			list != null && list.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

		private static void Remember(string refusal)
		{
			refusals.Enqueue(refusal);
			while (refusals.Count > RefusalHistory) refusals.Dequeue();
		}

		public static IEnumerable<string> Describe()
		{
			yield return $"Together {Program.MOD_VERSION}, protocol {ProtocolHash.Value}";
			yield return $"Game version: {ReferenceGameVersion ?? "not set yet"} ({GameVersionSource})";
			foreach (string line in SharedDlc.Describe()) yield return line;
			yield return $"Mods required: {List(config?.ModsRequired)}; ignored: {List(config?.ModsIgnored)}; gameplay: {List(config?.ModsGameplay)} ({rulesSource})";
			yield return refusals.Count == 0 ? "Refusals: none" : $"Last {refusals.Count} refusals:";
			foreach (string refusal in refusals) yield return $"  {refusal}";
		}

		private static string List(List<string> list) => list == null || list.Count == 0 ? "none" : string.Join(", ", list);
	}
}
