using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Jobs;

// shared-job-achievements D3: the server sends a finished job's stats to the players who worked on it, never to the
// finisher, whose own game counted them already. A finished last mission is shared state: SharedAchievements counts it.
public static class JobStats
{
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
		var stats = packet.Stats ?? new List<string>();
		foreach (string stat in stats) platform.IncrementStat(stat, 1);
		Log.Info($"[Jobs] Stats of job {packet.JobId} for working on it: {(stats.Count == 0 ? "none" : string.Join(", ", stats))}.");
	}
}
