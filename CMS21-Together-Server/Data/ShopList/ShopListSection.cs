using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Server.Data.Persistence;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.ShopList
{
	[SessionSection]
	public class ShopListSection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.ShopListKey;
		public int Version => 1;
		int ISnapshotProvider.SyncOrder => SyncOrder.ShopList;

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.ShopListState);

		public void Load(JToken data)
		{
			var state = data?.ToObject<ShopListState>() ?? new ShopListState();
			state.Entries = (state.Entries ?? new List<ShopListEntry>()).Where(e => e != null && !string.IsNullOrEmpty(e.Id) && e.Amount > 0).ToList();
			GameDataManager.CurrentState.ShopListState = state;
		}

		public void Reset() => GameDataManager.CurrentState.ShopListState = new ShopListState();

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from shop-list v{fromVersion}.");

		public int SendSnapshot(int clientId) => ShopListService.SendSnapshot(clientId);
	}
}
