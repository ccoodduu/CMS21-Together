using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Diagnostics;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Diagnostics
{
	// --check-redaction: secret names are redacted in config text, preferences, JSON and save copies; hotkeys are not.
	public static class RedactionCheck
	{
		public static int Run()
		{
			var failures = new List<string>();
			void Expect(bool condition, string what)
			{
				Console.WriteLine($"{(condition ? "ok" : "FAILED")}: {what}");
				if (!condition) failures.Add(what);
			}

			foreach (string name in new[] { "admin_key", "GSLT_Token", "password", "CMS21Together.AdminKey", "client_secret" })
				Expect(Redaction.IsSecretName(name), $"{name} is secret");
			foreach (string name in new[] { "CMS21Together.ResyncHotkey", "BugReportHotkey", "DevHotkeys", "server_name", "LastJoinTarget" })
				Expect(!Redaction.IsSecretName(name), $"{name} is not secret");

			string ini = "server_name = \"Garage\"\nGSLT_Token = \"gslt-5d1e\"\npassword = pw-7f3a\nadmin_key = adm-91c2 # admin\npassword_steam = False\nempty_token = \"\"\n";
			string redactedIni = Redaction.ConfigText(ini);
			Expect(!new[] { "gslt-5d1e", "pw-7f3a", "adm-91c2" }.Any(redactedIni.Contains), "no secret value left in the config");
			Expect(redactedIni.Contains("GSLT_Token = \"<redacted>\"") && redactedIni.Contains("password = <redacted>") && redactedIni.Contains("admin_key = <redacted>"), "secrets show as <redacted>");
			Expect(redactedIni.Contains("server_name = \"Garage\"") && redactedIni.Contains("empty_token = \"\""), "other and empty entries keep their value");

			string toml = "Autosave = { IntervalMinutes = 5.0 }\n[OtherMod]\nApiKey = \"x1x1x1\"\n[CMS21Together]\nResyncHotkey = \"F7\"\nAdminKey = \"adm-91c2\"\n[CMS21Together_Guard]\nMode = \"Enforce\"\n";
			string prefs = Redaction.ConfigText(Redaction.PreferenceCategories(toml, "CMS21Together"));
			Expect(!prefs.Contains("OtherMod") && !prefs.Contains("Autosave") && prefs.Contains("[CMS21Together_Guard]"), "preferences reduced to the CMS21Together categories");
			Expect(prefs.Contains("ResyncHotkey = \"F7\"") && prefs.Contains("AdminKey = \"<redacted>\"") && !prefs.Contains("adm-91c2"), "AdminKey redacted, ResyncHotkey kept");

			var save = JObject.Parse("{\"Sections\":{\"players\":{\"Version\":1,\"Data\":{\"Records\":[{\"Key\":\"guid:k1k1k1\",\"Name\":\"A\"}]}},\"world\":{\"Data\":{\"Money\":5}}}}");
			Redaction.DropPlayerKeys(save);
			string saveText = save.ToString();
			Expect(!saveText.Contains("k1k1k1") && saveText.Contains("\"Name\": \"A\"") && saveText.Contains("Money"), "save copy drops players[].Key and keeps the rest");

			string identity = "guid:5e4d3c2b1a09f8e7d6c5";
			var jobs = new CMS21_Together_Core.Data.JobsState();
			jobs.ActiveJobs.Add(new CMS21_Together_Core.Data.ActiveJobEntry { Contributors = { Presence.PlayerRecords.ShortKey(identity) } });
			var withJobs = JObject.Parse($"{{\"Sections\":{{\"players\":{{\"Data\":{{\"Records\":[{{\"Key\":\"{identity}\"}}]}}}}}}}}");
			withJobs["Sections"]["jobs"] = new JObject { ["Data"] = JObject.FromObject(jobs) };
			var jobSecrets = Redaction.SecretValues(withJobs["Sections"]?[Redaction.PlayersSection]).ToList();
			Redaction.DropPlayerKeys(withJobs);
			string jobsText = Redaction.Scrub(withJobs.ToString(), jobSecrets);
			Expect(!jobsText.Contains(identity) && !jobsText.Contains("1a09f8e7d6c5") && jobsText.Contains("guid:5e4d3c2b"), "a jobs section names contributors by short key, never by identity key");

			var json = JObject.Parse("{\"PlayerKey\":\"p9p9p9\",\"Hotkey\":\"F8\",\"Nested\":{\"token\":\"t1t1t1\"}}");
			Redaction.RedactJson(json);
			Expect(!json.ToString().Contains("p9p9p9") && !json.ToString().Contains("t1t1t1") && json.ToString().Contains("F8"), "JSON secrets redacted");

			string log = Redaction.Scrub("joined with adm-91c2 and pw-7f3a, key k", new[] { "adm-91c2", "pw-7f3a", "k" });
			Expect(log == "joined with <redacted> and <redacted>, key k", $"secret values scrubbed from text, short ones left ({log})");

			Expect(BugReportId.IsValid(BugReportId.New(DateTime.UtcNow)) && !BugReportId.IsValid("..\\x") && !BugReportId.IsValid("20261006-120000-ABCD"), "report ids");

			Console.WriteLine(failures.Count == 0 ? "OK" : $"FAILED ({failures.Count})");
			return failures.Count == 0 ? 0 : 1;
		}
	}
}
