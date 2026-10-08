using CMS21_Together_Core;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Outdoor;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class OutdoorHandlers
	{
		[PacketHandler(PacketTypes.OutdoorCatalog)]
		public static void OnCatalog(long clientId, OutdoorCatalogPacket packet) => CarCatalog.Report((int)clientId, packet);

		[PacketHandler(PacketTypes.OutdoorEnter)]
		public static void OnEnter(long clientId, OutdoorEnterPacket packet)
		{
			int client = (int)clientId;
			if (!OutdoorScenes.IsOutdoor(packet.Scene))
			{
				Logger.Info($"[Outdoor] Enter of {packet.Scene} from {client} ignored: not an outdoor scene.");
				return;
			}
			Server.SendToClient(OutdoorInstances.Enter(client, packet.Scene, ServerTime.Time), client);
		}

		[PacketHandler(PacketTypes.OutdoorLootRecord)]
		public static void OnLootRecord(long clientId, OutdoorLootRecordPacket packet) => OutdoorInstances.OnLootRecord((int)clientId, packet);

		[PacketHandler(PacketTypes.OutdoorDigest)]
		public static void OnDigest(long clientId, OutdoorDigestPacket packet) => OutdoorInstances.OnDigest((int)clientId, packet);

		[PacketHandler(PacketTypes.LootTake)]
		public static void OnLootTake(long clientId, LootTakePacket packet) => LootService.Take((int)clientId, packet);

		[PacketHandler(PacketTypes.LootPutBack)]
		public static void OnLootPutBack(long clientId, LootPutBackPacket packet) => LootService.PutBack((int)clientId, packet);

		[PacketHandler(PacketTypes.AuctionLotClaim)]
		public static void OnLotClaim(long clientId, AuctionLotClaimPacket packet) => AuctionService.Claim((int)clientId, packet);

		[PacketHandler(PacketTypes.AuctionBidState)]
		public static void OnBidState(long clientId, AuctionBidStatePacket packet) => AuctionService.BidState((int)clientId, packet);

		[PacketHandler(PacketTypes.AuctionBidRequest)]
		public static void OnBidRequest(long clientId, AuctionBidRequestPacket packet) => AuctionService.BidRequest((int)clientId, packet);
	}
}
