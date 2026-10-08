using System.Collections.Generic;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class GarageUpgradeHandler
	{
		[PacketHandler(PacketTypes.UpgradeRequest)]
		public static void OnUpgradeRequest(long clientId, UpgradeRequest packet)
		{
			var state = GameDataManager.CurrentState;
			string refusal = packet.type switch
			{
				UpgradeType.Money => BuyUpgrade(packet, state.GarageState, state.WorldState),
				UpgradeType.Points => UnlockSkill(packet, state.GarageState, state.WorldState),
				_ => "unknown type",
			};
			state.GarageState.AvailablePoints = ComputeAvailablePoints(state.WorldState, state.GarageState);
			state.WorldState.updateGamemode = false;
			if (refusal == null)
			{
				Logger.Info($"[Upgrades] {packet.type} {packet.id} level {packet.level} unlocked by client {clientId}.");
				Server.SendToClients(state.WorldState);
				Server.SendToClients(state.GarageState);
				return;
			}
			Logger.Info($"[Upgrades] {packet.type} {packet.id} level {packet.level} from client {clientId} refused: {refusal}; answering with the garage state.");
			Server.SendToClient(state.WorldState, (int)clientId);
			Server.SendToClient(state.GarageState, (int)clientId);
		}

		private static string BuyUpgrade(UpgradeRequest packet, GarageState garageState, WorldState worldState)
		{
			var upgradeData = GameDatabase.PlayerUpgrades.MoneyUpgrades.Find(s => s.ID == packet.id);
			if (upgradeData == null) return "unknown id";
			if (packet.level < 0 || packet.level >= upgradeData.Costs.Count) return "level out of range";
			if (!garageState.GarageUpgradeLevels.TryGetValue(packet.id, out bool[] levels) || packet.level >= levels.Length) return "not in the garage state";
			if (levels[packet.level]) return "already unlocked";
			int cost = upgradeData.Costs[packet.level];
			if (worldState.Money < cost) return $"not enough money ({worldState.Money} < {cost})";
			worldState.Money -= cost;
			levels[packet.level] = true;
			return null;
		}

		private static string UnlockSkill(UpgradeRequest packet, GarageState garageState, WorldState worldState)
		{
			var skillData = GameDatabase.PlayerUpgrades.PointUpgrades.Find(s => s.ID == packet.id);
			if (skillData == null) return "unknown id";
			if (packet.level < 0 || packet.level >= skillData.Costs.Count) return "level out of range";
			if (!garageState.PlayerUpgradeLevels.TryGetValue(packet.id, out bool[] skillLevels) || packet.level >= skillLevels.Length) return "not in the garage state";
			if (skillLevels[packet.level]) return "already unlocked";
			int availablePoints = ComputeAvailablePoints(worldState, garageState);
			int cost = skillData.Costs[packet.level];
			if (availablePoints < cost) return $"not enough points ({availablePoints} < {cost})";
			skillLevels[packet.level] = true;
			return null;
		}

		public static int ComputeAvailablePoints(WorldState worldState, GarageState garageState)
		{
			int totalPointsEarned = 0;
			var pointsList = GameDatabase.PlayerUpgrades.PointsPerLevel;
			for (int i = 0; i < worldState.Level && i < pointsList.Count; i++)
			{
				totalPointsEarned += pointsList[i];
			}
			int totalPointsSpent = CalculateSpentPoints(garageState.PlayerUpgradeLevels);
			return totalPointsEarned - totalPointsSpent;
		}

		public static void ResetPointSkills()
		{
			var garageState = GameDataManager.CurrentState.GarageState;
			foreach (var levels in garageState.PlayerUpgradeLevels.Values)
				for (int i = 0; levels != null && i < levels.Length; i++) levels[i] = false;
			garageState.AvailablePoints = ComputeAvailablePoints(GameDataManager.CurrentState.WorldState, garageState);
			Logger.Info($"[Server] Point skills reset, available points {garageState.AvailablePoints}.");
			Server.SendToClients(garageState);
		}

		public static int CalculateSpentPoints(Dictionary<string, bool[]> playerSkills)
		{
			int spent = 0;
			foreach (var skill in playerSkills)
			{
				var dbSkill = GameDatabase.PlayerUpgrades.PointUpgrades.Find(s => s.ID == skill.Key);
				if (dbSkill == null) continue;

				for (int i = 0; i < skill.Value.Length; i++)
				{
					if (skill.Value[i]) spent += dbSkill.Costs[i];
				}
			}
			return spent;
		}
	}
	
}