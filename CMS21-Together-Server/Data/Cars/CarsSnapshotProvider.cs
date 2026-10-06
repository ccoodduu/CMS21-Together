using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Persistence;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Cars
{
	[SessionSection]
	public class CarsSnapshotProvider : ISnapshotProvider
	{
		public string Key => SyncOrder.CarsKey;
		int ISnapshotProvider.SyncOrder => SyncOrder.Cars;

		public CarsSnapshotProvider()
		{
			PresenceEvents.Left += CarPartsStore.OnPlayerLeft;
			PresenceEvents.Left += CarClaims.ReleaseOwner;
			PresenceEvents.SceneChanged += CarClaims.OnSceneChanged;
		}

		public int SendSnapshot(int clientId)
		{
			int cars = 0;
			int snapshotId = Server.Clients[clientId].SnapshotId;
			foreach (var pair in GameDataManager.CurrentState.CarState.LoadedCars)
			{
				if (!pair.Value.HasBaseline) continue;
				CarPartsStore.SendSnapshot(pair.Key, pair.Value, snapshotId, only: clientId);
				CarClaims.SendActive(pair.Key, clientId);
				CarAwayRegistry.SendActive(pair.Key, clientId);
				cars++;
			}
			return cars;
		}
	}
}
