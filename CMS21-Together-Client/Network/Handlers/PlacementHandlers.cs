using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Placement;

namespace CMS21Together.Network.Handlers;

public static class PlacementHandlers
{
	[PacketHandler(PacketTypes.LifterState)]
	public static void OnLifterState(long clientId, LifterStatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ClientScene.GarageBound(() => LifterSync.OnLifterState(packet, snapshotId),
			() => SyncTracker.Applied(SyncOrder.CarPlacementKey, snapshotId));
	}

	[PacketHandler(PacketTypes.CarPlaceChanged)]
	public static void OnCarPlaceChanged(long clientId, CarPlaceChangedPacket packet)
	{
		ClientScene.GarageBound(() => CarPlacementSync.OnPlaceChanged(packet));
	}

	[PacketHandler(PacketTypes.CarParkResult)]
	public static void OnCarParkResult(long clientId, CarParkResultPacket packet)
	{
		ParkingSync.OnParkResult(packet);
	}

	[PacketHandler(PacketTypes.ParkingSlotUpdate)]
	public static void OnParkingSlotUpdate(long clientId, ParkingSlotUpdatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ClientScene.GarageBound(() => ParkingSync.OnSlotUpdate(packet, snapshotId), () => ParkingSync.OnSlotUpdate(packet, snapshotId));
	}

	[PacketHandler(PacketTypes.ParkingState)]
	public static void OnParkingState(long clientId, ParkingStatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ClientScene.GarageBound(() => ParkingSync.OnState(packet, snapshotId), () => ParkingSync.OnState(packet, snapshotId));
	}
}
