using System;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Server.Log;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Persistence.Sections
{
	[SessionSection]
	public class CarsSection : ISaveSection
	{
		public string Key => SyncOrder.CarsKey;
		public int Version => 3;

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.CarState);

		public void Load(JToken data)
		{
			var state = data.ToObject<CarState>() ?? new CarState();
			foreach (var loader in state.LoadedCars.Where(c => !c.Value.HasBaseline).Select(c => c.Key).ToList())
			{
				Logger.Warn($"[Cars] Dropping {state.LoadedCars[loader].Spawn?.CarToLoad} on loader {loader}: no part baseline, it cannot be replayed.");
				state.LoadedCars.Remove(loader);
			}
			GameDataManager.CurrentState.CarState = state;
		}

		public void Reset()
		{
			GameDataManager.CurrentState.CarState = new CarState();
		}

		public JToken Migrate(JToken data, int fromVersion)
		{
			if (fromVersion == 2) return data;
			if (fromVersion != 1) throw new NotSupportedException($"No migration from cars v{fromVersion}.");

			var v1 = (JObject)data;
			var loaders = new JObject();
			int nextSpawnSeq = 1;
			if (v1["LoadedCars"] is JObject loadedCars)
			{
				foreach (var car in loadedCars.Properties())
				{
					loaders[car.Name] = new JObject
					{
						["Spawn"] = car.Value.DeepClone(),
						["SpawnSeq"] = nextSpawnSeq++,
						["Revision"] = 0,
						["HasBaseline"] = false
					};
				}
			}

			var v2 = new JObject { ["NextSpawnSeq"] = nextSpawnSeq, ["LoadedCars"] = loaders };
			foreach (var property in v1.Properties())
			{
				if (property.Name == "LoadedCars" || property.Name == "BodyParts" || property.Name == "SubParts") continue;
				v2[property.Name] = property.Value.DeepClone();
			}
			return v2;
		}
	}
}
