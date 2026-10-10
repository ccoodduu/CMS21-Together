using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Driving;

namespace CMS21Together.Network.Handlers;

public static class TrackHandlers
{
	[PacketHandler(PacketTypes.TrackRecordUpdate)]
	public static void OnUpdate(long senderId, TrackRecordUpdatePacket packet) => TrackRecords.OnUpdate(packet);

	[PacketHandler(PacketTypes.RaceRefused)]
	public static void OnRaceRefused(long senderId, RaceRefusedPacket packet) => TrackRaceSync.OnRefused(packet);

	[PacketHandler(PacketTypes.RaceCountdown)]
	public static void OnRaceCountdown(long senderId, RaceCountdownPacket packet) => TrackRaceSync.OnCountdown(packet);

	[PacketHandler(PacketTypes.RaceResult)]
	public static void OnRaceResult(long senderId, RaceResultPacket packet) => TrackRaceSync.OnResult(packet);
}
