using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Outdoor
{
	public class LootEntry
	{
		public long Uid;
		public int PileIndex;
		public string PileKey;
		public ModItem Item;
		public LootItemStatus Status;
		public int HolderId;
	}

	public class OutdoorInstance
	{
		public int InstanceId;
		public GameScene Scene;
		public int Seed;
		public int GeneratorId;
		public int OpenerId;
		public bool OpenerFeePending;
		public DateTime OpenedUtc;
		public float EmptySince = -1f;
		public readonly List<OutdoorCarPick> Picks = new List<OutdoorCarPick>();
		public readonly List<int> Members = new List<int>();
		public readonly HashSet<int> Sold = new HashSet<int>();

		public List<LootPile> Piles;
		public List<bool> RandomShowActive;
		public readonly Dictionary<long, LootEntry> Loot = new Dictionary<long, LootEntry>();

		public bool LotsPending;
		public readonly List<AuctionLot> Lots = new List<AuctionLot>();
		public readonly Dictionary<int, AuctionLotState> LotStates = new Dictionary<int, AuctionLotState>();

		public List<string> ReferenceDigest;
		public readonly Dictionary<int, List<string>> Digests = new Dictionary<int, List<string>>();
		public readonly HashSet<int> DigestCompared = new HashSet<int>();
		public int DigestMismatches;
		public readonly List<string> MismatchLines = new List<string>();

		public bool LootRecorded => Piles != null;

		public bool HasMember(int playerId) => Members.Contains(playerId);

		public bool NeedsGeneratorUpload =>
			(OutdoorScenes.HasPiles(Scene) && !LootRecorded)
			|| ReferenceDigest == null
			|| (Scene == GameScene.Auction && (LotsPending || Lots.Any(l => !l.ValuesKnown)));

		public string Label => $"{Scene} #{InstanceId}";

		public OutdoorInstancePacket ToPacket(int forPlayer, bool fillAllSpawnPoints) => new OutdoorInstancePacket
		{
			InstanceId = InstanceId,
			Scene = Scene,
			Seed = Seed,
			Generator = forPlayer == GeneratorId && NeedsGeneratorUpload,
			FillAllSpawnPoints = fillAllSpawnPoints,
			Picks = Picks.ToList(),
			Sold = Sold.OrderBy(i => i).ToList(),
			Piles = Piles?.ToList(),
			RandomShowActive = RandomShowActive?.ToList(),
			ItemStates = Loot.Values.Where(e => e.Status != LootItemStatus.Available)
				.Select(e => new LootItemState { Uid = e.Uid, Status = e.Status, HolderId = e.HolderId }).ToList(),
			Lots = Lots.Where(l => IsLotOffered(l.Index)).ToList(),
			LotStates = LotStates.Values.Where(s => IsLotOffered(s.Lot)).ToList(),
			LotsPending = LotsPending,
		};

		public bool IsLotOffered(int lot) =>
			LotStates.TryGetValue(lot, out var state) && (state.Status == AuctionLotStatus.Open || state.Status == AuctionLotStatus.Bidding);
	}
}
