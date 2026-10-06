using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Persistence;

namespace CMS21_Together_Server.Data.Cars
{
	[SessionSection]
	public class CarDetailsSnapshot : ISnapshotProvider
	{
		public string Key => SyncOrder.CarDetailsKey;
		int ISnapshotProvider.SyncOrder => SyncOrder.CarDetails;

		public int SendSnapshot(int clientId) => CarDetailsStore.SendSnapshot(clientId);
	}
}
