using System;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Network;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Persistence.Sections
{
	[SessionSection]
	public class WorldSection : ISaveSection, ISnapshotProvider
	{
		public string Key => SyncOrder.WorldKey;
		public int Version => 1;
		int ISnapshotProvider.SyncOrder => SyncOrder.World;

		private const int StartMoney = 12500;
		private const int StartLevel = 8;

		public JToken Save()
		{
			var data = JObject.FromObject(GameDataManager.CurrentState.WorldState);
			data.Remove(nameof(WorldState.updateGamemode));
			return data;
		}

		public void Load(JToken data)
		{
			var state = data.ToObject<WorldState>();
			state.updateGamemode = false;
			GameDataManager.CurrentState.WorldState = state;
		}

		public void Reset()
		{
			int internalLevel = StartLevel - 1;
			double capCurrent = Math.Floor(Math.Pow(internalLevel, 1.62221) * 2) * 75;
			double capNext = Math.Floor(Math.Pow(internalLevel + 1, 1.62221) * 2) * 75;
			int maxExpInThisLevel = (int)(capNext - capCurrent);

			GameDataManager.CurrentState.WorldState = new WorldState
			{
				Gamemode = Gamemode.Normal,
				Money = StartMoney,
				Level = StartLevel,
				Exp = Math.Min(480, maxExpInThisLevel - 1)
			};
		}

		public JToken Migrate(JToken data, int fromVersion) => throw new NotSupportedException($"No migration from world v{fromVersion}.");

		public int SendSnapshot(int clientId)
		{
			var state = GameDataManager.CurrentState.WorldState;
			state.updateGamemode = true;
			try
			{
				Server.SendToClient(state, clientId);
			}
			finally
			{
				state.updateGamemode = false;
			}
			return 1;
		}
	}
}
