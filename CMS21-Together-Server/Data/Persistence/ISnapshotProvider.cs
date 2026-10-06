namespace CMS21_Together_Server.Data.Persistence
{
	public interface ISnapshotProvider
	{
		string Key { get; }
		int SyncOrder { get; }
		int SendSnapshot(int clientId);
	}
}
