using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Server.Data.Persistence;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Tools
{
	[SessionSection]
	public class WorkshopToolsSection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.WorkshopToolsKey;
		public int Version => 1;
		int ISnapshotProvider.SyncOrder => SyncOrder.WorkshopTools;

		public JToken Save() => JObject.FromObject(GameDataManager.CurrentState.ToolsState);

		public void Load(JToken data)
		{
			var state = data.ToObject<ToolsState>() ?? new ToolsState();
			state.Slots ??= new Dictionary<ModToolId, ToolSlotState>();
			state.Positions ??= new Dictionary<int, int>();
			foreach (var slot in state.Slots.Values)
				slot.Parts ??= new Dictionary<string, CMS21_Together_Core.Network.Packets.CarSubPartUpdatePacket>();
			GameDataManager.CurrentState.ToolsState = state;
			ToolsStore.ResetRuntime();
		}

		public void Reset()
		{
			GameDataManager.CurrentState.ToolsState = new ToolsState();
			ToolsStore.ResetRuntime();
		}

		public JToken Migrate(JToken data, int fromVersion) =>
			throw new NotSupportedException($"No migration from workshop-tools v{fromVersion}.");

		public int SendSnapshot(int clientId) => ToolsStore.SendSnapshot(clientId);
	}
}
