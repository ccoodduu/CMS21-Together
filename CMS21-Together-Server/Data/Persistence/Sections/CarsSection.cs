using System;
using CMS21_Together_Core.Data;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Persistence.Sections
{
	[SessionSection]
	public class CarsSection : ISaveSection
	{
		public string Key => SyncOrder.CarsKey;
		public int Version => 1;

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.CarState);

		public void Load(JToken data)
		{
			GameDataManager.CurrentState.CarState = data.ToObject<CarState>() ?? new CarState();
		}

		public void Reset()
		{
			GameDataManager.CurrentState.CarState = new CarState();
		}

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from cars v{fromVersion}.");
	}
}
