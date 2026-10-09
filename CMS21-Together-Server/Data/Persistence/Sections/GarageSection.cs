using System;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Garage;
using CMS21_Together_Server.Network;
using CMS21_Together_Server.Network.Handlers;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Persistence.Sections
{
	[SessionSection]
	public class GarageSection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.GarageKey;
		public int Version => 2;
		int ISnapshotProvider.SyncOrder => SyncOrder.Garage;

		public JToken Save()
		{
			var data = JObject.FromObject(GameDataManager.CurrentState.GarageState);
			data.Remove(nameof(GarageState.AvailablePoints));
			return data;
		}

		public void Load(JToken data)
		{
			var state = data.ToObject<GarageState>();
			state.Look = (state.Look ?? new ModGarageLook()).Clamped();
			GameDataManager.CurrentState.GarageState = state;
			GarageLookService.ResetRuntime();
		}

		public void Reset()
		{
			var state = new GarageState();
			foreach (var upgrade in GameDatabase.PlayerUpgrades.MoneyUpgrades)
				state.GarageUpgradeLevels[upgrade.ID] = upgrade.UnlockedLevels.ToArray();
			foreach (var upgrade in GameDatabase.PlayerUpgrades.PointUpgrades)
				state.PlayerUpgradeLevels[upgrade.ID] = upgrade.UnlockedLevels.ToArray();
			GameDataManager.CurrentState.GarageState = state;
			GarageLookService.ResetRuntime();
		}

		public JToken Migrate(JToken data, int fromVersion)
		{
			if (fromVersion != 1) throw new NotSupportedException($"No migration from garage v{fromVersion}.");
			var garage = (JObject)data;
			garage[nameof(GarageState.Look)] = JObject.FromObject(new ModGarageLook());
			return garage;
		}

		public int SendSnapshot(int clientId)
		{
			var state = GameDataManager.CurrentState;
			state.GarageState.AvailablePoints = GarageUpgradeHandler.ComputeAvailablePoints(state.WorldState, state.GarageState);
			Server.SendToClient(state.GarageState, clientId);
			return 1;
		}
	}
}
