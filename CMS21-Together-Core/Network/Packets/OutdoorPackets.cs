using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Data.Outdoor;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.OutdoorCatalog)]
public class OutdoorCatalogPacket : INetworkData
{
	public Dictionary<OutdoorCatalogScene, List<CatalogEntry>> Scenes = new Dictionary<OutdoorCatalogScene, List<CatalogEntry>>();
}

[Serializable]
[NetworkPacket(PacketTypes.OutdoorEnter)]
public class OutdoorEnterPacket : INetworkData
{
	public GameScene Scene;
}

[Serializable]
[NetworkPacket(PacketTypes.OutdoorInstance)]
public class OutdoorInstancePacket : INetworkData
{
	public const int NotShared = 0;

	public int InstanceId;
	public GameScene Scene;
	public int Seed;
	public bool Generator;
	public bool FillAllSpawnPoints;
	public List<OutdoorCarPick> Picks = new List<OutdoorCarPick>();
	public List<int> Sold = new List<int>();
	public List<LootPile> Piles;
	public List<bool> RandomShowActive;
	public List<LootItemState> ItemStates = new List<LootItemState>();
	public List<AuctionLot> Lots = new List<AuctionLot>();
	public List<AuctionLotState> LotStates = new List<AuctionLotState>();
	public bool LotsPending;

	public bool Shared => InstanceId != NotShared;
}

[Serializable]
[NetworkPacket(PacketTypes.OutdoorLootRecord)]
public class OutdoorLootRecordPacket : INetworkData
{
	public int InstanceId;
	public List<LootPile> Piles = new List<LootPile>();
	public List<bool> RandomShowActive = new List<bool>();
}

[Serializable]
[NetworkPacket(PacketTypes.OutdoorDigest)]
public class OutdoorDigestPacket : INetworkData
{
	public int InstanceId;
	public List<string> Rows = new List<string>();
	public List<AuctionAmountRange> AuctionAmounts = new List<AuctionAmountRange>();
	public List<AuctionLot> LotValues = new List<AuctionLot>();
}

[Serializable]
[NetworkPacket(PacketTypes.LootTake)]
public class LootTakePacket : INetworkData
{
	public int InstanceId;
	public string PileKey;
	public int PileIndex = -1;
	public long Uid;
}

[Serializable]
[NetworkPacket(PacketTypes.LootPutBack)]
public class LootPutBackPacket : INetworkData
{
	public int InstanceId;
	public long Uid;
}

[Serializable]
[NetworkPacket(PacketTypes.LootUpdate)]
public class LootUpdatePacket : INetworkData
{
	public int InstanceId;
	public long Uid;
	public LootItemStatus Status;
	public int By;
	public int PileIndex = -1;
	public string PileKey;
	public ModItem Item;
}

[Serializable]
[NetworkPacket(PacketTypes.LootTakeRefused)]
public class LootTakeRefusedPacket : INetworkData
{
	public int InstanceId;
	public long Uid;
	public int By;
	public string ByName;
}

[Serializable]
[NetworkPacket(PacketTypes.OutdoorCarRemoved)]
public class OutdoorCarRemovedPacket : INetworkData
{
	public int InstanceId;
	public int Index;
	public int By;
}

[Serializable]
[NetworkPacket(PacketTypes.AuctionLotClaim)]
public class AuctionLotClaimPacket : INetworkData
{
	public int InstanceId;
	public int Lot;
	public bool Granted;
	public int OwnerId;
	public string OwnerName;
}

[Serializable]
[NetworkPacket(PacketTypes.AuctionBidState)]
public class AuctionBidStatePacket : INetworkData
{
	public int InstanceId;
	public AuctionBidSnapshot State;
}

[Serializable]
[NetworkPacket(PacketTypes.AuctionBidRequest)]
public class AuctionBidRequestPacket : INetworkData
{
	public int InstanceId;
	public int Lot;
	public int RequesterId;
	public int SeenBid;
}

[Serializable]
[NetworkPacket(PacketTypes.AuctionLotClosed)]
public class AuctionLotClosedPacket : INetworkData
{
	public int InstanceId;
	public int Lot;
	public AuctionLotStatus Status;
	public int By;
}
