using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildInfo = CMS21_Together_Core.BuildInfo;
using CMS21_Together_Core.Data.Compatibility;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Diagnostics;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Compatibility;
using CMS21Together.Data;
using CMS21Together.Guard;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Reconciliation;
using CMS21Together.Managers;
using CMS21Together.Network;
using CMS21Together.Session;
using CMS21Together.UI;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CMS21Together.Diagnostics;

public static class BugReport
{
	private const float CooldownSeconds = 30f;
	private const int KeptRecords = 10;
	private const string IdentityFile = "player.json";

	public class Record
	{
		public string Id;
		public string Path;
		public string Origin;
		public bool Connected;
		public bool Done;
		public bool Written;
		public string Error;
		public long Bytes;
		public double CaptureMs;
		public double WriteMs;
		public double LongestFrameMs;
		public int Frames;
		public string ServerFile;
		public string ServerError;
		public string Refusal;
	}

	private class Capture
	{
		public Record Record;
		public Dictionary<string, object> Info;
		public List<string> Secrets;
		public string GameDirectory;
		public string UserDataDirectory;
		public object Mods;
		public List<string> Guard;
		public List<(string Key, string SubKey, Projection Projection)> State;
		public List<string> Errors = new List<string>();
	}

	public static readonly List<Record> Recent = new List<Record>();

	private static float lastReport = -CooldownSeconds;
	private static float lastUpdateAt = -1f;

	public static string Folder => System.IO.Path.Combine(MelonUtils.UserDataDirectory, "CMS21Together", "BugReports");

	public static Record Request()
	{
		float wait = lastReport + CooldownSeconds - Time.realtimeSinceStartup;
		if (wait > 0f)
		{
			string refusal = $"Wait {Mathf.CeilToInt(wait)} s before the next bug report.";
			Log.Info($"[BugReport] Refused: {refusal}");
			ModNotify.ShowToast(refusal);
			return new Record { Refusal = refusal };
		}

		lastReport = Time.realtimeSinceStartup;
		bool connected = Client.Instance != null && Client.Instance.IsConnectionValid;
		var record = Start(BugReportId.New(DateTime.UtcNow), "reporter", connected);
		if (connected) Client.Instance.Send(new BugReportRequestPacket { Id = record.Id });
		return record;
	}

	public static void OnCollect(BugReportCollectPacket packet)
	{
		if (!BugReportId.IsValid(packet.Id) || Recent.Any(r => r.Id == packet.Id))
		{
			Log.Warn($"[BugReport] Ignored a collect request with id '{packet.Id}'.");
			return;
		}
		Start(packet.Id, "collect", true);
	}

	public static void OnResult(BugReportResultPacket packet)
	{
		var record = Recent.FirstOrDefault(r => r.Id == packet.Id);
		if (record != null)
		{
			record.ServerFile = packet.ServerFile;
			record.ServerError = packet.Error;
		}
		if (!string.IsNullOrEmpty(packet.Error))
		{
			Log.Warn($"[BugReport] {packet.Id}: the server wrote no bundle: {packet.Error}");
			ModNotify.ShowToast($"Bug report {packet.Id}: the server wrote no bundle ({packet.Error}).");
			return;
		}
		Log.Info($"[BugReport] {packet.Id}: the server writes {packet.ServerFile}.");
		ModNotify.ShowToast($"Bug report {packet.Id}: the server saves its part as {packet.ServerFile} (the host sends it).");
	}

	public static void Update()
	{
		float now = Time.realtimeSinceStartup;
		float frameMs = lastUpdateAt < 0f ? 0f : (now - lastUpdateAt) * 1000f;
		lastUpdateAt = now;
		foreach (var record in Recent)
		{
			if (record.Done) continue;
			record.Frames++;
			if (frameMs > record.LongestFrameMs) record.LongestFrameMs = frameMs;
		}
	}

	private static Record Start(string id, string origin, bool connected)
	{
		var record = new Record { Id = id, Origin = origin, Connected = connected, Path = System.IO.Path.Combine(Folder, id + ".zip") };
		Recent.Add(record);
		if (Recent.Count > KeptRecords) Recent.RemoveAt(0);

		var watch = Stopwatch.StartNew();
		var capture = CaptureOnMainThread(record);
		record.CaptureMs = watch.Elapsed.TotalMilliseconds;
		Log.Info($"[BugReport] {id} ({origin}) captured in {record.CaptureMs:0} ms; writing {record.Path}.");

		Task.Run(() =>
		{
			var writeWatch = Stopwatch.StartNew();
			string error = null;
			long bytes = 0;
			try { bytes = Write(capture); }
			catch (Exception e) { error = e.Message; }
			double ms = writeWatch.Elapsed.TotalMilliseconds;
			ThreadManager.ExecuteOnMainThread<object>(_ => Finish(record, bytes, ms, error), null);
		});
		return record;
	}

	private static void Finish(Record record, long bytes, double writeMs, string error)
	{
		record.Done = true;
		record.Written = error == null;
		record.Error = error;
		record.Bytes = bytes;
		record.WriteMs = writeMs;
		if (error != null)
		{
			Log.Error($"[BugReport] {record.Id}: could not write {record.Path}: {error}");
			ModNotify.ShowToast($"Bug report {record.Id} could not be written: {error}");
			return;
		}

		Log.Info($"[BugReport] {record.Id} written to {record.Path}: {bytes / 1024} KB in {writeMs:0} ms on a worker thread " +
		         $"(capture {record.CaptureMs:0} ms on the main thread; longest frame while writing {record.LongestFrameMs:0} ms over {record.Frames} frames).");
		string where = record.Origin == "collect" ? "saved for another player's report" : "saved";
		string server = record.Origin == "reporter" && !record.Connected ? " Not connected, so there is no server part." : "";
		ModNotify.ShowToast($"Bug report {record.Id} {where}: {record.Path}.{server}");
	}

	private static Capture CaptureOnMainThread(Record record)
	{
		var capture = new Capture
		{
			Record = record,
			GameDirectory = MelonUtils.GameDirectory,
			UserDataDirectory = MelonUtils.UserDataDirectory,
		};
		capture.Info = Try(capture, "info", () => Info(record)) ?? new Dictionary<string, object> { ["id"] = record.Id };
		capture.Secrets = Try(capture, "secrets", Secrets) ?? new List<string>();
		capture.Mods = Try(capture, "mods", Mods);
		capture.Guard = Try(capture, "guard", GuardLog);
		capture.State = Try(capture, "state", State);
		return capture;
	}

	private static T Try<T>(Capture capture, string what, Func<T> read) where T : class
	{
		try { return read(); }
		catch (Exception e)
		{
			capture.Errors.Add($"{what}: {e.Message}");
			return null;
		}
	}

	private static Dictionary<string, object> Info(Record record) => new Dictionary<string, object>
	{
		["id"] = record.Id,
		["utc"] = DateTime.UtcNow.ToString("o"),
		["origin"] = record.Origin,
		["modVersion"] = BuildInfo.ModVersion,
		["fullVersion"] = BuildInfo.LoadedFullVersion,
		["gameVersion"] = LocalEnvironment.GameVersion,
		["connection"] = new Dictionary<string, object>
		{
			["state"] = ConnectionStatus.State.ToString(),
			["connected"] = record.Connected,
			["network"] = Client.Instance?.NetworkType.ToString(),
			["lastReason"] = ConnectionStatus.LastReason,
			["server"] = ClientData.ServerInfo?.ServerName,
			["serverModVersion"] = ClientData.ServerInfo?.ModVersion,
			["initialSyncFinished"] = ClientData.IsInitialSyncFinished,
			["inSnapshot"] = SyncTracker.InSnapshot,
			["serverPart"] = record.Connected,
		},
		["scene"] = ClientScene.LocalScene.ToString(),
		["unityScene"] = SceneManager.GetActiveScene().name,
		["player"] = new Dictionary<string, object>
		{
			["slot"] = record.Connected ? Client.Instance.ID : (int?)null,
			["name"] = PlayerSettings.PlayerName,
		},
	};

	private static List<string> Secrets()
	{
		var secrets = new List<string> { PlayerSettings.AdminKey, JoinService.CurrentPassword, JoinService.CurrentAdminKey, LocalServerHost.AdminKey };
		secrets.AddRange(JoinService.RememberedPasswords);
		return secrets.Where(s => !string.IsNullOrEmpty(s)).ToList();
	}

	private static object Mods()
	{
		var mods = ConnectPacketFactory.CollectMods();
		return new ModClassifier(ModClassifierRules.Default).ClassifyAll(mods).Select(v => new Dictionary<string, object>
		{
			["name"] = v.Mod.Name,
			["version"] = v.Mod.Version,
			["author"] = v.Mod.Author,
			["file"] = v.Mod.File,
			["assembly"] = v.Mod.Assembly,
			["class"] = v.Class.ToString(),
			["knownReason"] = v.KnownReason,
			["reasons"] = v.Reasons,
			["targets"] = v.Mod.Targets.Select(t => $"{t.Assembly}:{t.Type}.{t.Method}").ToList(),
		}).ToList();
	}

	private static List<string> GuardLog()
	{
		var lines = new List<string> { $"mode {FeatureGuard.Mode}; allow '{GuardSettings.AllowRaw}'; deny '{GuardSettings.DenyRaw}'; {FeatureGuard.Blocks.Count} entries (ring of {FeatureGuard.RingSize})" };
		lines.AddRange(FeatureGuard.Blocks.Select(b => b.ToString()));
		return lines;
	}

	private static List<(string Key, string SubKey, Projection Projection)> State()
	{
		var state = new List<(string, string, Projection)>();
		bool inGarage = Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished
		                && ClientScene.LocalScene == GameScene.Garage && !SyncTracker.InSnapshot;
		if (!inGarage) return state;
		var keys = new List<(string Key, string SubKey)> { (DigestMappers.WorldKey, ""), (DigestMappers.InventoryKey, ""), (DigestMappers.PlacementKey, "") };
		keys.AddRange(CarPartsSync.All.Where(s => s.Registry != null).Select(s => (DigestMappers.CarsKey, s.Loader.ToString())));
		foreach (var (key, subKey) in keys) state.Add((key, subKey, ClientDigests.Project(key, subKey)));
		return state;
	}

	private static long Write(Capture capture)
	{
		string userData = capture.UserDataDirectory;
		string melonLoader = System.IO.Path.Combine(capture.GameDirectory, "MelonLoader");
		string modData = System.IO.Path.Combine(userData, "CMS21Together");
		var secrets = capture.Secrets.Concat(IdentitySecrets(modData)).ToList();

		var bundle = new BugReportBundle("client", secrets);
		bundle.Errors.AddRange(capture.Errors);
		bundle.AddJson("info.json", capture.Info);
		bundle.AddFile("MelonLoader/Latest.log", System.IO.Path.Combine(melonLoader, "Latest.log"));
		bundle.AddNewest("MelonLoader/Logs", System.IO.Path.Combine(melonLoader, "Logs"), "*.log", BugReportBundle.NewestLogs);
		bundle.AddFile("MelonPreferences.cfg", System.IO.Path.Combine(userData, "MelonPreferences.cfg"),
			text => Redaction.ConfigText(Redaction.PreferenceCategories(text, "CMS21Together")));
		if (Directory.Exists(modData))
		{
			foreach (string file in Directory.GetFiles(modData, "*.json", SearchOption.TopDirectoryOnly)
				         .Where(f => !string.Equals(System.IO.Path.GetFileName(f), IdentityFile, StringComparison.OrdinalIgnoreCase)))
				bundle.AddFile($"UserData/CMS21Together/{System.IO.Path.GetFileName(file)}", file, RedactJson);
		}
		bundle.AddText("files.txt", string.Join("\r\n", BugReportBundle.FileList(capture.GameDirectory, "Mods", "UserLibs")));
		if (capture.Mods != null) bundle.AddJson("mods.json", capture.Mods);
		if (capture.Guard != null) bundle.AddText("guard.log", string.Join("\r\n", capture.Guard));
		if (capture.State != null) bundle.AddState(capture.State);
		return bundle.Write(capture.Record.Path);
	}

	private static string RedactJson(string text)
	{
		try
		{
			var token = JToken.Parse(text);
			Redaction.RedactJson(token);
			return token.ToString();
		}
		catch (Exception)
		{
			return Redaction.ConfigText(text);
		}
	}

	private static IEnumerable<string> IdentitySecrets(string modData)
	{
		string path = System.IO.Path.Combine(modData, IdentityFile);
		try
		{
			if (!File.Exists(path)) return Enumerable.Empty<string>();
			return JToken.Parse(BugReportBundle.ReadShared(path)).SelectTokens("$..*").OfType<JValue>()
				.Where(v => v.Type == JTokenType.String).Select(v => (string)v).ToList();
		}
		catch (Exception)
		{
			return Enumerable.Empty<string>();
		}
	}
}
