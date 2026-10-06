using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Network;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Persistence.Sections
{
	[SessionSection]
	public class InventorySection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.InventoryKey;
		public int Version => 1;
		int ISnapshotProvider.SyncOrder => SyncOrder.Inventory;

		private const int BatchSize = 50;

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.InventoryState);

		public void Load(JToken data)
		{
			GameDataManager.CurrentState.InventoryState = data.ToObject<InventoryState>() ?? new InventoryState();
		}

		public void Reset()
		{
			GameDataManager.CurrentState.InventoryState = new InventoryState();
		}

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from inventory v{fromVersion}.");

		public int SendSnapshot(int clientId)
		{
			var state = GameDataManager.CurrentState.InventoryState;
			var invItems = state.InventoryItems ?? new List<ModItem>();
			var invGroups = state.InventoryGroupItems ?? new List<ModGroupItem>();
			var whItems = state.WarehouseItems ?? new List<ModItem>();
			var whGroups = state.WarehouseGroupItems ?? new List<ModGroupItem>();

			int invItemIdx = 0, invGroupIdx = 0, whItemIdx = 0, whGroupIdx = 0;
			int batches = 0;

			do
			{
				var batch = new InventorySyncPacket
				{
					IsFirstBatch = batches == 0,
					InventoryItems = new List<ModItem>(),
					InventoryGroupItems = new List<ModGroupItem>(),
					WarehouseItems = new List<ModItem>(),
					WarehouseGroupItems = new List<ModGroupItem>()
				};

				int count = 0;
				while (count < BatchSize && invItemIdx < invItems.Count) { batch.InventoryItems.Add(invItems[invItemIdx++]); count++; }
				while (count < BatchSize && invGroupIdx < invGroups.Count) { batch.InventoryGroupItems.Add(invGroups[invGroupIdx++]); count++; }
				while (count < BatchSize && whItemIdx < whItems.Count) { batch.WarehouseItems.Add(whItems[whItemIdx++]); count++; }
				while (count < BatchSize && whGroupIdx < whGroups.Count) { batch.WarehouseGroupItems.Add(whGroups[whGroupIdx++]); count++; }

				batch.IsLastBatch = invItemIdx >= invItems.Count && invGroupIdx >= invGroups.Count
					&& whItemIdx >= whItems.Count && whGroupIdx >= whGroups.Count;

				Server.SendToClient(batch, clientId);
				batches++;

				if (batch.IsLastBatch) break;
			} while (true);

			return batches;
		}
	}
}
