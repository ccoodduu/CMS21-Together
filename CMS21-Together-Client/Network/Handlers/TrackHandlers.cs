using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Driving;

namespace CMS21Together.Network.Handlers;

public static class TrackHandlers
{
	[PacketHandler(PacketTypes.TrackRecordUpdate)]
	public static void OnUpdate(long senderId, TrackRecordUpdatePacket packet) => TrackRecords.OnUpdate(packet);
}
