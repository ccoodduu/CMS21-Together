using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Jobs;

// shared-job-achievements D3: the server sends a finished job's stats to the players who worked on it, never to the
// finisher, whose own game counted them already.
public static class JobStats
{
	private const string FinishAllMissions = "stat_finish_allmissions";

	private static readonly HashSet<int> awarded = new HashSet<int>();

	public static void Reset() => awarded.Clear();

	public static void OnAward(JobStatsAwardPacket packet)
	{
		if (packet == null || !awarded.Add(packet.JobId)) return;
		var platform = Singleton<GameManager>.Instance?.PlatformManager;
		if (platform == null)
		{
			Log.Warn($"[Jobs] Stats of job {packet.JobId} not applied: no platform manager.");
			return;
		}
		var stats = new List<string>(packet.Stats ?? new List<string>());
		if (packet.MissionFinished && GlobalData.MissionsAmount <= GlobalData.MissionsFinished) stats.Add(FinishAllMissions);
		foreach (string stat in stats) platform.IncrementStat(stat, 1);
		Log.Info($"[Jobs] Stats of job {packet.JobId} for working on it: {(stats.Count == 0 ? "none" : string.Join(", ", stats))}.");
	}
}
