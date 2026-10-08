using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.ShopList;

namespace CMS21Together.Network.Handlers;

public static class ShopListHandlers
{
	[PacketHandler(PacketTypes.ShopListState)]
	public static void OnState(long clientId, ShopListStatePacket packet)
	{
		int snapshotId = SyncTracker.ReceivingSnapshotId;
		ShopListSync.OnState(packet);
		SyncTracker.Applied(SyncOrder.ShopListKey, snapshotId);
	}
}
