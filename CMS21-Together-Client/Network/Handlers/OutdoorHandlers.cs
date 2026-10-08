using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Outdoor;

namespace CMS21Together.Network.Handlers;

public static class OutdoorHandlers
{
	[PacketHandler(PacketTypes.OutdoorInstance)]
	public static void OnInstance(long clientId, OutdoorInstancePacket packet) => OutdoorSession.OnInstance(packet);

	[PacketHandler(PacketTypes.LootUpdate)]
	public static void OnLootUpdate(long clientId, LootUpdatePacket packet) => LootSync.OnUpdate(packet);

	[PacketHandler(PacketTypes.LootTakeRefused)]
	public static void OnLootTakeRefused(long clientId, LootTakeRefusedPacket packet) => LootSync.OnRefused(packet);

	[PacketHandler(PacketTypes.OutdoorCarRemoved)]
	public static void OnCarRemoved(long clientId, OutdoorCarRemovedPacket packet) => OutdoorCarSync.OnRemoved(packet);

	[PacketHandler(PacketTypes.AuctionLotClaim)]
	public static void OnLotClaim(long clientId, AuctionLotClaimPacket packet) => AuctionSync.OnClaim(packet);

	[PacketHandler(PacketTypes.AuctionBidState)]
	public static void OnBidState(long clientId, AuctionBidStatePacket packet) => AuctionSync.OnBidState(packet);

	[PacketHandler(PacketTypes.AuctionBidRequest)]
	public static void OnBidRequest(long clientId, AuctionBidRequestPacket packet) => AuctionSync.OnBidRequest(packet);

	[PacketHandler(PacketTypes.AuctionLotClosed)]
	public static void OnLotClosed(long clientId, AuctionLotClosedPacket packet) => AuctionSync.OnLotClosed(packet);
}
