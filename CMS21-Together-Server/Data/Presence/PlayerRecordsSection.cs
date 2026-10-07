using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Log;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Presence
{
	[SessionSection]
	public class PlayerRecordsSection : ISaveSection
	{
		public string Key => SyncOrder.PlayersKey;
		public int Version => 1;

		public JToken Save()
		{
			PlayerRecords.CaptureConnected();
			return JArray.FromObject(GameDataManager.CurrentState.PlayerRecords.Values.OrderBy(r => r.Key, StringComparer.Ordinal));
		}

		public void Load(JToken data)
		{
			var records = new Dictionary<string, PlayerRecord>();
			foreach (var record in data.ToObject<List<PlayerRecord>>() ?? new List<PlayerRecord>())
			{
				if (string.IsNullOrEmpty(record?.Key)) continue;
				records[record.Key] = record;
			}
			GameDataManager.CurrentState.PlayerRecords = records;
			Logger.Info($"[Players] Loaded {records.Count} player records.");
		}

		public void Reset()
		{
			GameDataManager.CurrentState.PlayerRecords = new Dictionary<string, PlayerRecord>();
		}

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from players v{fromVersion}.");
	}
}
