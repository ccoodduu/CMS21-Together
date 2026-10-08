using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Data.Outdoor;

[Serializable]
public enum OutdoorCatalogScene
{
	Junkyard = 0,
	Barn = 1,
	AuctionNormal = 2,
	AuctionSalvage = 3
}

[Serializable]
public enum AuctionKind
{
	Normal = 0,
	Salvage = 1
}

[Serializable]
public enum LootItemStatus
{
	Available = 0,
	Held = 1,
	Bought = 2
}

[Serializable]
public enum AuctionLotStatus
{
	Open = 0,
	Bidding = 1,
	Won = 2,
	Lost = 3
}

[Serializable]
public enum AuctionBidPhase
{
	Bidding = 0,
	TeamWon = 1,
	AiWon = 2
}

[Serializable]
public class CatalogEntry
{
	public const int UnknownRarity = -1;

	public string CarId;
	public int ConfigVersion;
	public int Dlc = -1;
	public int Rarity = UnknownRarity;

	public string Key => $"{CarId}|{ConfigVersion}";
}

[Serializable]
public class OutdoorCarPick
{
	public string CarId;
	public int ConfigVersion;
	public int Dlc = -1;

	public override string ToString() => $"{CarId}/{ConfigVersion}";
}

[Serializable]
public class LootPile
{
	public int Index;
	public string Key;
	public List<ModItem> Items = new List<ModItem>();
}

[Serializable]
public class LootItemState
{
	public long Uid;
	public LootItemStatus Status;
	public int HolderId;
}

[Serializable]
public class AuctionAmountRange
{
	public AuctionKind Kind;
	public float Min;
	public float Max;
}

[Serializable]
public class AuctionLot
{
	public int Index;
	public AuctionKind Kind;
	public string CarId;
	public int Version;
	public int Seed;
	public int Rating;
	public int Value;
	public int StartingPrice;
	public bool ValuesKnown;
}

[Serializable]
public class AuctionBidSnapshot
{
	public int Lot;
	public int OwnerId;
	public int CurrentBid;
	public int BidStep;
	public bool TeamLeads;
	public float SecondsLeft;
	public AuctionBidPhase Phase;
}

[Serializable]
public class AuctionLotState
{
	public int Lot;
	public AuctionLotStatus Status;
	public int OwnerId;
	public AuctionBidSnapshot LastBid;
}
