using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;

namespace CMS21Together.Logic.Achievements;

public static class EventStats
{
	public static void OnAward(EventStatsAwardPacket packet)
	{
		if (packet?.Stats == null || packet.Stats.Count == 0) return;
		var platform = Singleton<GameManager>.Instance?.PlatformManager;
		if (platform == null)
		{
			Log.Warn($"[Achievements] Stats of the {packet.Event} by client {packet.Actor} not applied: no platform manager.");
			return;
		}
		foreach (var stat in packet.Stats) platform.IncrementStat(stat.Id, stat.Amount);
		Log.Info($"[Achievements] Stats of the {packet.Event} by client {packet.Actor}: {string.Join(", ", packet.Stats.Select(s => $"{s.Id} {s.Amount:+0;-0}"))}.");
	}
}
