using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Achievements;

namespace CMS21Together.Network.Handlers;

public static class AchievementHandlers
{
	[PacketHandler(PacketTypes.EventStatsAward)]
	public static void OnEventStatsAward(long clientId, EventStatsAwardPacket packet) => EventStats.OnAward(packet);
}
