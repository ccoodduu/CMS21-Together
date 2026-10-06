using System;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Persistence;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Placement
{
	[SessionSection]
	public class PlacementSection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.CarPlacementKey;
		public int Version => 1;
		int ISnapshotProvider.SyncOrder => SyncOrder.CarPlacement;

		public PlacementSection()
		{
			CarPartsStore.LoaderCleared += PlacementRules.OnLoaderCleared;
		}

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.PlacementState);

		public void Load(JToken data)
		{
			GameDataManager.CurrentState.PlacementState = data.ToObject<PlacementState>() ?? NewState();
		}

		public void Reset()
		{
			GameDataManager.CurrentState.PlacementState = NewState();
		}

		public JToken Migrate(JToken data, int fromVersion) =>
			throw new NotSupportedException($"No migration from car-placement v{fromVersion}.");

		public int SendSnapshot(int clientId)
		{
			PlacementRules.ResetLiftsWithoutCars();
			int items = ParkingService.SendState(clientId);
			foreach (int lifter in GameDataManager.CurrentState.PlacementState.Lifters.Keys.ToList())
			{
				if (PlacementRules.LifterState(lifter) == PlacementRules.OnFloor) continue;
				PlacementRules.SendLifter(lifter, instant: true, only: clientId);
				items++;
			}
			return items;
		}

		private static PlacementState NewState() =>
			new PlacementState { Parking = new ParkingLot { UnlockedLevels = ParkingLayout.DefaultUnlockedLevels } };
	}
}
