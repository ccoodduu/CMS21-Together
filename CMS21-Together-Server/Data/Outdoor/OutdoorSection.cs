using System;
using CMS21_Together_Server.Data.Persistence;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Outdoor
{
	[SessionSection]
	public class OutdoorSection : ISaveSection
	{
		public string Key => "outdoor";
		public int Version => 1;

		public JToken Save() => new JObject { ["History"] = OutdoorInstances.History.Save() };

		public void Load(JToken data) => OutdoorInstances.History.Load(data?["History"]);

		public void Reset() => OutdoorInstances.History.Reset();

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from outdoor v{fromVersion}.");
	}
}
