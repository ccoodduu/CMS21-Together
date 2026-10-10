using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21Together.Logic.Jobs;

namespace CMS21Together.Logic.Achievements;

// docs/spikes/shared-achievements.md: stats that follow from shared save state are counted on every client by the
// game's own check (or its condition) when that state arrives, never twice.
public static class SharedAchievements
{
	private const string Level = "stat_level";
	private const string UnlockParking = "stat_unlock_parking";
	private const string FinishAllMissions = "stat_finish_allmissions";
	private const int ParkingLevelsForStat = 10;

	private static readonly HashSet<string> awarded = new HashSet<string>();
	private static bool upgradesSeen;
	private static int raisedLevel;

	public static void Reset()
	{
		awarded.Clear();
		upgradesSeen = false;
		raisedLevel = 0;
	}

	public static void AfterUpgrades(UpgradeSystem system, bool garageUnlocked, bool skillUnlocked)
	{
		if (system == null) return;
		bool first = !upgradesSeen;
		upgradesSeen = true;
		if (garageUnlocked || first) system.CheckForAchievements(global::UpgradeType.Money);
		if (skillUnlocked || first) system.CheckForAchievements(global::UpgradeType.Points);
	}

	public static void AfterParking(int unlockedLevels)
	{
		if (unlockedLevels >= ParkingLevelsForStat) AwardOnce(UnlockParking);
	}

	public static void AfterMissions()
	{
		if (GlobalData.MissionsAmount <= 0 || GlobalData.MissionsFinished < GlobalData.MissionsAmount || JobEndContext.IsActive) return;
		AwardOnce(FinishAllMissions);
	}

	// In difficulty mode 2 the game counts into ValueSandbox, so GetStatValue stays put; raisedLevel keeps a world
	// state from adding the same levels again.
	public static void RaiseLevel(int level)
	{
		var platform = Singleton<GameManager>.Instance?.PlatformManager;
		if (platform == null) return;
		int have = Math.Max(platform.GetStatValue(Level), raisedLevel);
		if (level <= have) return;
		platform.IncrementStat(Level, level - have);
		raisedLevel = level;
	}

	private static void AwardOnce(string stat)
	{
		var platform = Singleton<GameManager>.Instance?.PlatformManager;
		if (platform == null || !awarded.Add(stat) || platform.GetStatValue(stat) != 0) return;
		platform.IncrementStat(stat, 1);
		Log.Info($"[Achievements] {stat} from the shared save state.");
	}
}
