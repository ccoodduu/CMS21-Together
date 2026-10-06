using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Details;

namespace CMS21Together.Network.Handlers;

public static class CarDetailsHandlers
{
	[PacketHandler(PacketTypes.CarDetailsUpdate)]
	public static void OnUpdate(long clientId, CarDetailsUpdatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ClientScene.GarageBound(() => CarDetailsSync.OnUpdate(packet, snapshotId),
			() => { if (packet.SourceClientId == -1) SyncTracker.Applied(CMS21_Together_Core.Data.SyncOrder.CarDetailsKey, snapshotId); });
	}

	[PacketHandler(PacketTypes.CarDetailsRequest)]
	public static void OnRequest(long clientId, CarDetailsRequestPacket packet) => ClientScene.GarageBound(() => CarDetailsSync.OnRequest(packet));
}
