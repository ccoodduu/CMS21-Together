using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Diagnostics;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Data.Reconciliation;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Diagnostics
{
	// Callers hold StateLock.
	public static class BugReportWriter
	{
		private const float CooldownSeconds = 30f;
		private const int ListedBundles = 20;
		private static readonly TimeSpan DesyncWindow = TimeSpan.FromHours(1);

		private static readonly Dictionary<int, float> lastReport = new Dictionary<int, float>();

		private class Capture
		{
			public string Id;
			public DateTime Utc;
			public Dictionary<string, object> Info;
			public JObject Save;
			public List<(string Key, string SubKey, Projection Projection)> State;
			public List<Dictionary<string, object>> Players;
			public List<string> Secrets;
		}

		public static string Folder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BugReports");

		public static string FileName(string id) => $"BugReports/{id}.zip";

		public static void OnRequest(int clientId, BugReportRequestPacket request, float now)
		{
			string reporter = PresenceRegistry.Get(clientId)?.Username ?? $"player {clientId}";
			string refusal = Refusal(clientId, request.Id, now);
			if (refusal != null)
			{
				Logger.Warn($"[BugReport] Refused a report from {reporter} (client {clientId}): {refusal}");
				Server.SendToClient(new BugReportResultPacket { Id = request.Id, Error = refusal }, clientId);
				return;
			}
			lastReport[clientId] = now;

			var others = Server.Clients.Values.Where(c => c.IsConnected && c.SyncState == SyncState.InSession && c.ID != clientId).Select(c => c.ID).ToList();
			Capture capture;
			try
			{
				capture = CaptureState(request, clientId, reporter, others);
			}
			catch (Exception e)
			{
				Logger.Error($"[BugReport] {request.Id}: could not capture the server state: {e.Message}");
				Server.SendToClient(new BugReportResultPacket { Id = request.Id, Error = "the server could not capture its state" }, clientId);
				return;
			}
			Task.Run(() => Write(capture));

			foreach (int other in others)
				Server.SendToClient(new BugReportCollectPacket { Id = request.Id }, other);
			Server.SendToClient(new BugReportResultPacket { Id = request.Id, ServerFile = FileName(request.Id) }, clientId);
			Logger.Info($"[BugReport] {request.Id} requested by {reporter} (client {clientId}); writing {FileName(request.Id)}, asked {others.Count} other players for theirs.");
		}

		public static IEnumerable<string> Describe()
		{
			var files = Directory.Exists(Folder) ? new DirectoryInfo(Folder).GetFiles("*.zip").OrderByDescending(f => f.LastWriteTimeUtc).ToList() : new List<FileInfo>();
			yield return $"Bug reports in {Folder}: {files.Count}";
			foreach (var file in files.Take(ListedBundles))
				yield return $"  {file.Name}  {file.Length / 1024} KB  {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
		}

		private static string Refusal(int clientId, string id, float now)
		{
			if (!BugReportId.IsValid(id)) return "invalid report id";
			if (!Server.Clients.TryGetValue(clientId, out var client) || !client.IsAccepted) return "not accepted on this server";
			if (lastReport.TryGetValue(clientId, out float last) && now - last < CooldownSeconds) return $"wait {Math.Ceiling(CooldownSeconds - (now - last)):0} s before the next bug report";
			if (File.Exists(Path.Combine(Folder, id + ".zip"))) return "a report with this id exists";
			return null;
		}

		private static Capture CaptureState(BugReportRequestPacket request, int clientId, string reporter, List<int> others)
		{
			var config = Program.Config;
			var save = GameDataManager.BuildSaveCopy();
			var secrets = new List<string> { config.Password, config.AdminKey, config.GsltToken };
			secrets.AddRange(Redaction.SecretValues(save["Sections"]?[Redaction.PlayersSection]));
			Redaction.DropPlayerKeys(save);

			var state = new List<(string, string, Projection)>
			{
				(DigestMappers.WorldKey, "", ReconciliationService.Project(DigestMappers.WorldKey, "")),
				(DigestMappers.InventoryKey, "", ReconciliationService.Project(DigestMappers.InventoryKey, "")),
				(DigestMappers.PlacementKey, "", ReconciliationService.Project(DigestMappers.PlacementKey, "")),
			};
			foreach (int loader in GameDataManager.CurrentState.CarState.LoadedCars.Keys.OrderBy(k => k))
				state.Add((DigestMappers.CarsKey, loader.ToString(), ReconciliationService.Project(DigestMappers.CarsKey, loader.ToString())));

			var connected = Server.Clients.Values.Where(c => c.IsConnected).OrderBy(c => c.ID).ToList();
			var players = connected.Select(c =>
			{
				var record = PresenceRegistry.Get(c.ID);
				return new Dictionary<string, object>
				{
					["slot"] = c.ID,
					["name"] = record?.Username,
					["scene"] = record?.Scene.ToString(),
					["syncState"] = c.SyncState.ToString(),
					["connection"] = c.ConnectionType.ToString(),
					["gameVersion"] = c.GameVersion,
					["modVersion"] = c.ModVersion,
					["mods"] = c.Mods,
					["dlc"] = SharedDlc.Of(c.ID),
					["rttMs"] = Math.Round(c.RttMs),
					["admin"] = c.IsAdmin,
				};
			}).ToList();

			var utc = DateTime.UtcNow;
			return new Capture
			{
				Id = request.Id,
				Utc = utc,
				Save = save,
				State = state,
				Players = players,
				Secrets = secrets.Where(s => !string.IsNullOrEmpty(s)).ToList(),
				Info = new Dictionary<string, object>
				{
					["id"] = request.Id,
					["utc"] = utc.ToString("o"),
					["serverFullVersion"] = BuildInfo.FullVersion,
					["modVersion"] = Program.MOD_VERSION,
					["playerCount"] = connected.Count(c => c.IsAccepted),
					["reporter"] = new Dictionary<string, object> { ["slot"] = clientId, ["name"] = reporter },
					["note"] = request.Note ?? "",
					["askedSlots"] = others,
					["settings"] = config.Describe(),
					["desync"] = ReconciliationService.Describe().ToList(),
					["compat"] = CompatibilityPolicy.Describe().ToList(),
				},
			};
		}

		private static void Write(Capture capture)
		{
			var watch = Stopwatch.StartNew();
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string logDirectory = Path.Combine(baseDirectory, "Log");
			try
			{
				var bundle = new BugReportBundle("server", capture.Secrets);
				bundle.AddJson("info.json", capture.Info);
				bundle.AddFile("Log/Latest.txt", Path.Combine(logDirectory, "Latest.txt"));
				bundle.AddNewest("Log", logDirectory, "Log_*.txt", BugReportBundle.NewestLogs);
				bundle.AddFile("server_config.ini", Path.Combine(baseDirectory, "server_config.ini"), Redaction.ConfigText);
				bundle.AddText("save.json", capture.Save.ToString(Formatting.Indented));
				bundle.AddState(capture.State);
				AddDesyncRecords(bundle, Path.Combine(logDirectory, "desync"), capture.Utc - DesyncWindow);
				bundle.AddJson("players.json", capture.Players);
				long bytes = bundle.Write(Path.Combine(Folder, capture.Id + ".zip"));
				Logger.Info($"[BugReport] {capture.Id} written to {FileName(capture.Id)} ({bytes / 1024} KB in {watch.ElapsedMilliseconds} ms{(bundle.Errors.Count > 0 ? $", {bundle.Errors.Count} files missing: see errors.txt" : "")}).");
			}
			catch (Exception e)
			{
				Logger.Error($"[BugReport] {capture.Id}: could not write {FileName(capture.Id)}: {e.Message}");
			}
		}

		private static void AddDesyncRecords(BugReportBundle bundle, string directory, DateTime sinceUtc)
		{
			if (!Directory.Exists(directory)) return;
			foreach (var file in new DirectoryInfo(directory).GetFiles("*.json").Where(f => f.LastWriteTimeUtc >= sinceUtc).OrderBy(f => f.Name, StringComparer.Ordinal))
				bundle.AddFile($"Log/desync/{file.Name}", file.FullName);
		}
	}
}
