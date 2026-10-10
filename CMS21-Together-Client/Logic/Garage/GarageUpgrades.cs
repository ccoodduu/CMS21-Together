using System.Collections;
using CMS.UI.Logic.Upgrades;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Economy;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;
using UnityEngine;
using upgType = CMS21_Together_Core.Data.Enum.UpgradeType;

namespace CMS21Together.Logic.Garage;

[HarmonyPatch]
public static class GarageUpgrades
{
	public static bool IsSyncing = false;
	private const int SkillResetCostPerPoint = 1000;
	
	[HarmonyPatch(typeof(GarageAndToolsTab), nameof(GarageAndToolsTab.UnlockCurrentSelectedSkillAction))]
	[HarmonyPrefix]
	public static bool UnlockUpgradeActionHook(GarageAndToolsTab __instance)
	{
		if (!Client.Instance.IsConnected || IsSyncing) return true;


		var currentItem = __instance.currentUpgradeItem;
		if (currentItem == null)
		{
			Log.Warn($"[Client] Requesting invalid upgrade.");
			return false;
		}

		Log.Info($"[Client] Requesting upgrade: {currentItem.UpgradeID} Lvl {currentItem.UpgradeLevel}");
        
		Client.Instance.Send(new UpgradeRequest() {
			id = currentItem.UpgradeID,
			level = currentItem.UpgradeLevel,
			type = upgType.Money
		});
		
		return false; 
	}
	
	[HarmonyPatch(typeof(SkillsTab), nameof(SkillsTab.UnlockCurrentSelectedSkillAction))]
	[HarmonyPrefix]
	public static bool UnlockSkillActionHook(SkillsTab __instance)
	{
		if (!Client.Instance.IsConnected || IsSyncing) return true;

		var currentItem = __instance.currentUpgradeItem;
		if (currentItem == null)
		{
			Log.Warn($"[Client] Requesting invalid skill.");
			return false;
		}

		Log.Info($"[Client] Requesting Skill: {currentItem.UpgradeID} Lvl {currentItem.UpgradeLevel}");
		
		Client.Instance.Send(new UpgradeRequest()
		{
			id = currentItem.UpgradeID,
			level = currentItem.UpgradeLevel,
			type = upgType.Points 
		});
		
		return false; 
	}
	
	
	[HarmonyPatch(typeof(SkillsTab), nameof(SkillsTab.ResetSkillsAction))]
	[HarmonyPrefix]
	public static bool ResetSkillsHook(SkillsTab __instance)
	{
		if (!Client.Instance.IsConnected || IsSyncing) return true;
		if (!__instance.CanReset(out CMS.UI.Logic.ResetUpgradeLockReason _)) return true;

		int points = __instance.pointsToReset;
		Log.Info($"[Client] Requesting a skill reset ({points} points).");
		EconomyRequests.Send(new EconomyRequestPacket { Reason = EconomyReason.SkillReset, Money = -SkillResetCostPerPoint * points, Arg = points }, result =>
		{
			if (!result.Accepted) TradeHooks.ShowRefusal(result);
		});
		return false;
	}

	public static IEnumerator SyncUpgrades(GarageState packet, GarageAndToolsTab tools, int snapshotId)
	{
		yield return new WaitForEndOfFrame();
		float timeout = 10f;
		float timer = 0f;
		
		while (timer < timeout)
		{
			if (tools.upgradeSystem != null && tools.upgradeItems != null && tools.upgradeItems.Length > 0)
				break;

			timer += Time.deltaTime;
			yield return null;
		}
        
		if (tools.upgradeSystem == null)
		{
			Log.Error("Failed to sync: UpgradeSystem did not initialize in time.");
			yield break;
		}

		IsSyncing = true;
		bool garageUnlocked = false;
		bool skillUnlocked = false;

		foreach (var upgradeEntry in packet.GarageUpgradeLevels)
		{
			foreach (var upgradeData in tools.upgradeSystem.UpgradesForMoney)
			{
				if (upgradeData != null && upgradeData.ID == upgradeEntry.Key)
				{
					for (int i = 0; i < upgradeEntry.Value.Length; i++)
					{
						if (upgradeData.Unlocked.Length > i)
						{
							if (upgradeEntry.Value[i] && !upgradeData.Unlocked[i]) garageUnlocked = true;
							upgradeData.Unlocked[i] = upgradeEntry.Value[i];
						}
					}
					break;
				}
			}
		}

		foreach (var skillEntry in packet.PlayerUpgradeLevels)
		{
			foreach (var skillData in tools.upgradeSystem.UpgradesForPoints)
			{
				if (skillData != null && skillData.ID == skillEntry.Key)
				{
					for (int i = 0; i < skillEntry.Value.Length; i++)
					{
						if (skillData.Unlocked.Length > i)
						{
							if (skillEntry.Value[i] && !skillData.Unlocked[i]) skillUnlocked = true;
							skillData.Unlocked[i] = skillEntry.Value[i];
						}
					}
					break;
				}
			}
		}

		tools.SwitchIfUnlocked();
		tools.PrepareItems();

		tools.upgradeSystem.AvailablePoints = packet.AvailablePoints;
		var skillsTab = Object.FindObjectOfType<SkillsTab>();
		if (skillsTab != null && skillsTab.isActiveAndEnabled)
		{
			skillsTab.PrepareItems();
			skillsTab.Invoke(nameof(SkillsTab.RefreshGUI), 0f);
		}

		IsSyncing = false;
		Achievements.SharedAchievements.AfterUpgrades(tools.upgradeSystem, garageUnlocked, skillUnlocked);
		Log.Success("Garage and Skills synchronized successfully!");
		GarageLookSync.Receive(packet.Look);
		ClientData.IsGarageStateSynced = true;
		SyncTracker.Applied(SyncOrder.GarageKey, snapshotId);
	}
}