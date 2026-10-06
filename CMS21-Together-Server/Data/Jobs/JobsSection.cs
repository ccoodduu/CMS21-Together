using System;
using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Persistence;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Jobs
{
	[SessionSection]
	public class JobsSection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.JobsKey;
		public int Version => 1;
		int ISnapshotProvider.SyncOrder => SyncOrder.Jobs;

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.JobsState);

		public void Load(JToken data)
		{
			GameDataManager.CurrentState.JobsState = data.ToObject<JobsState>() ?? new JobsState();
			JobsService.Reset();
		}

		public void Reset()
		{
			GameDataManager.CurrentState.JobsState = new JobsState();
			JobsService.Reset();
		}

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from jobs v{fromVersion}.");

		public int SendSnapshot(int clientId) => JobsService.SendSnapshot(clientId);
	}
}
