using System.Collections;
using CMS.Difficulty;
using CMS.UI.Logic.Upgrades;
using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Garage;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Network.Handlers;

public static class WorldStatesPackets
{
	[PacketHandler(PacketTypes.WorldState)]
	public static void HandleWorldState(long senderId, WorldState packet)
	{
		if (packet.updateGamemode)
		{
			DifficultyManager difficultyManager = Singleton<GameManager>.Instance.DifficultyManager;
			difficultyManager.SetDifficultyLevel((DifficultyLevel)packet.Gamemode);
			difficultyManager.ActivateDifficultyLevel();
		}
		
		Log.Info($"Received World State Sync :\nGamemode: {packet.Gamemode.ToString()}\nMoney: {packet.Money}\nLevel: {packet.Level}\n Exp:{packet.Exp}\n Scraps:{packet.Scraps}");
		
		ClientData.IsServerUpdating = true;
		GlobalData.PlayerMoney = packet.Money;
		GlobalData.PlayerLevel = packet.Level - 1;
		GlobalData.PlayerExp = packet.Exp;
		GlobalData.PlayerScraps = packet.Scraps;
		ClientData.IsServerUpdating = false;
		
		var profile = Singleton<GameManager>.Instance.GameDataManager.CurrentProfileData;
		if (profile != null)
		{
			profile.globalDataWrapper.PlayerLevel = GlobalData.PlayerLevel;
			profile.globalDataWrapper.PlayerExp = GlobalData.PlayerExp;
			profile.globalDataWrapper.PlayerMoney = GlobalData.PlayerMoney;
		}
		int currentPlatformLevel = Singleton<GameManager>.Instance.PlatformManager.GetStatValue("stat_level");
		int levelDifference = GlobalData.RealPlayerLevel - currentPlatformLevel;
		Singleton<GameManager>.Instance.PlatformManager.IncrementStat("stat_level", levelDifference);

		UIManager.Get().RefreshAllStats();
		UIManager.Get().RefreshStatsUICoroutine(StatType.Scraps, false);
		ClientData.IsWorldStateSynced = true;
		SyncTracker.Applied(SyncOrder.WorldKey, SyncTracker.ReceivingSnapshotId);
	}
	
	[PacketHandler(PacketTypes.GarageState)]
	public static void HandleGarageState(long senderId, GarageState packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ClientScene.GarageBound(() => ApplyGarageState(packet, snapshotId));
	}

	private static void ApplyGarageState(GarageState packet, int snapshotId)
	{
	    if (packet?.GarageUpgradeLevels == null) {
	       Log.Error("Packet or GarageUpgradeLevels is null!");
	       return;
	    }
	    if (GameData.Instance == null) {
	        Log.Error("CRITICAL: GameData.Instance is NULL. Are you in the main menu?");
	        return;
	    }
	    GarageAndToolsTab tools = GameData.Instance.GarageTools;
	    if (tools == null) {
	       Log.Error("CRITICAL: tools (GarageTools) is NULL!");
	       return;
	    }
	    if (tools.upgradeSystem == null || tools.upgradeItems == null) {
	       Log.Error($"Internal refs null: System={tools.upgradeSystem==null}, Items={tools.upgradeItems==null}");
	       return;
	    }
	    
	    tools.PrepareItems();
	    MelonCoroutines.Start( GarageUpgrades.SyncUpgrades(packet, tools, snapshotId));
	}
	
	[PacketHandler(PacketTypes.SyncBegin)]
	public static void HandleSyncBegin(long senderId, SyncBegin packet)
	{
		SyncTracker.OnSyncBegin(packet);
	}

	[PacketHandler(PacketTypes.SyncEnd)]
	public static void HandleSyncEnd(long senderId, SyncEnd packet)
	{
		SyncTracker.OnSyncEnd(packet);
	}
}