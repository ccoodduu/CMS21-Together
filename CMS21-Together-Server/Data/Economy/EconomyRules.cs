using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Placement;
using CMS21_Together_Server.Network;
using CMS21_Together_Server.Network.Handlers;

namespace CMS21_Together_Server.Data.Economy
{
	public class EconomyOutcome
	{
		public int Money;
		public int Scraps;
		public int Exp;
		public int Barns;
		public EconomyRefusal Refusal;
		public string Note;
		public Action Effect;

		public static EconomyOutcome Refused(EconomyRefusal refusal, string note) => new EconomyOutcome { Refusal = refusal, Note = note };
	}

	// economy-audit D3 (option C): the amount the server applies for each reason, and the checks of the Trades.
	public static class EconomyRules
	{
		public const int WorkExpLimit = 10000;
		public const int CaseLifetimeSeconds = 30 * 60;
		public const int SkillResetCostPerPoint = 1000;
		public const string CarWashUpgrade = "car_wash";
		public const string CheaperParkingSkill = "cheaper_parking";
		private const int AuctionDestination = 4;
		private const int JunkyardDestination = 5;
		private const int BarnDestination = 7;
		private const int MaxTintWindows = 16;
		private const int MaxPlates = 99;
		private const int PlatePrice = 100;
		private const int CustomPlatePrice = 1000;

		public static bool TravelFees { get; set; } = true;
		public static int MaxCarSalePrice { get; set; } = 5000000;
		public static int MaxCarPurchasePrice { get; set; } = 5000000;

		public static readonly HashSet<EconomyReason> Trades = new HashSet<EconomyReason>
		{
			EconomyReason.SkillReset, EconomyReason.CarSale, EconomyReason.ScrapItem, EconomyReason.ScrapPerCondition,
			EconomyReason.ScrapUpgrade, EconomyReason.LicensePlates, EconomyReason.BarnMap,
		};

		private static readonly Dictionary<int, int> TravelTable = new Dictionary<int, int>
		{
			[AuctionDestination] = -200,
			[JunkyardDestination] = -500,
			[BarnDestination] = -100,
		};

		private static ModGameState State => GameDataManager.CurrentState;

		private static bool ChargesTravel => TravelFees && State.WorldState.Gamemode != Gamemode.Sandbox;

		public static EconomyOutcome Evaluate(int client, EconomyRequestPacket r)
		{
			switch (r.Reason)
			{
				case EconomyReason.Work:
					return r.Exp > 0 && r.Exp < WorkExpLimit && r.Money == 0 && r.Scraps == 0
						? new EconomyOutcome { Exp = r.Exp }
						: Invalid($"work exp {r.Exp}");
				case EconomyReason.TravelFee: return TravelFee(r);
				case EconomyReason.TravelFeeLegacy:
					if (!TravelTable.Values.Contains(r.Money)) return Invalid($"legacy travel fee {r.Money}");
					return new EconomyOutcome { Money = ChargesTravel ? r.Money : 0 };
				case EconomyReason.FluidSpill: return Ranged(r.Money, -500, -1);
				case EconomyReason.FluidRefill: return Ranged(r.Money, -2000, -1);
				case EconomyReason.PaintCar:
					if (r.Arg != 0 && r.Arg != 1) return Invalid($"paint type {r.Arg}");
					return Fixed(r.Money, r.Arg == 0 ? -1000 : -100);
				case EconomyReason.WashBeforePaint:
				case EconomyReason.WashBeforeTint:
					return Fixed(r.Money, -100);
				case EconomyReason.Tint:
					if (r.Arg < 1 || r.Arg > MaxTintWindows) return Invalid($"{r.Arg} windows");
					return Fixed(r.Money, -50 * r.Arg);
				case EconomyReason.Welder: return Ranged(r.Money, -5000, -1);
				case EconomyReason.InteriorDetailing:
					if (HasLevel(State.GarageState.GarageUpgradeLevels, CarWashUpgrade)) return Invalid("the garage has the car wash, detailing is free");
					return Ranged(r.Money, -1000, -1);
				case EconomyReason.PartRepair: return PartRepair(r);
				case EconomyReason.CrateMoney:
				case EconomyReason.CrateScrap:
				case EconomyReason.CrateExp:
					return Crate(client, r);
				case EconomyReason.SkillReset: return SkillReset(r);
				case EconomyReason.CarSale: return CarSale(client, r);
				case EconomyReason.ScrapItem: return ScrapItem(r);
				case EconomyReason.ScrapPerCondition: return ScrapPerCondition(r);
				case EconomyReason.ScrapUpgrade: return ScrapUpgrade(r);
				case EconomyReason.LicensePlates: return LicensePlates(r);
				case EconomyReason.BarnMap: return BarnMap(r);
				default: return Invalid("unknown reason");
			}
		}

		private static EconomyOutcome Invalid(string note) => EconomyOutcome.Refused(EconomyRefusal.Invalid, note);

		private static EconomyOutcome Fixed(int clientAmount, int amount) =>
			clientAmount == amount ? new EconomyOutcome { Money = amount } : Invalid($"expected {amount}");

		private static EconomyOutcome Ranged(int clientAmount, int min, int max) =>
			clientAmount >= min && clientAmount <= max ? new EconomyOutcome { Money = clientAmount } : Invalid($"outside {min}..{max}");

		public static bool HasLevel(Dictionary<string, bool[]> levels, string id) =>
			levels != null && levels.TryGetValue(id, out var unlocked) && unlocked != null && unlocked.Any(u => u);

		private static EconomyOutcome TravelFee(EconomyRequestPacket r)
		{
			if (!TravelTable.TryGetValue(r.Arg, out int table)) return Invalid($"destination {r.Arg}");
			var outcome = new EconomyOutcome { Money = ChargesTravel ? table : 0 };
			if (r.Arg == BarnDestination && r.Arg2 == 1) outcome.Barns = -1;
			if (outcome.Money != r.Money) outcome.Note = $"client charged {r.Money}";
			return outcome;
		}

		private static EconomyOutcome PartRepair(EconomyRequestPacket r)
		{
			var item = FindItem(r.ItemUid);
			if (item == null) return Invalid($"item {r.ItemUid} not in the inventory");
			int price = Math.Max(1, PricingCalculator.GetPrice(item));
			return Ranged(r.Money, -price, -1);
		}

		private static ModItem FindItem(long uid)
		{
			var inventory = State.InventoryState;
			return inventory.InventoryItems.FirstOrDefault(i => i.UID == uid)
				?? inventory.InventoryGroupItems.Where(g => g.ItemList != null).SelectMany(g => g.ItemList).FirstOrDefault(i => i.UID == uid);
		}

		private static EconomyOutcome Crate(int client, EconomyRequestPacket r)
		{
			if (!EconomyService.TryGetCase(r.ItemUid, out var opened)) return Invalid($"case {r.ItemUid} was not opened");
			if (opened.Client != client) return Invalid($"case {r.ItemUid} was opened by client {opened.Client}");
			if (opened.Looted) return Invalid($"case {r.ItemUid} was already looted");

			int level = State.WorldState.Level;
			int amount = r.Reason == EconomyReason.CrateMoney ? r.Money : r.Reason == EconomyReason.CrateScrap ? r.Scraps : r.Exp;
			var (min, max) = CardRange(r.Reason, level);
			bool otherKinds = r.Reason == EconomyReason.CrateMoney ? r.Scraps != 0 || r.Exp != 0
				: r.Reason == EconomyReason.CrateScrap ? r.Money != 0 || r.Exp != 0
				: r.Money != 0 || r.Scraps != 0;
			if (otherKinds || amount < min || amount > max) return Invalid($"card {amount} outside {min}..{max} at level {level}");

			var outcome = new EconomyOutcome { Effect = () => opened.Looted = true };
			if (r.Reason == EconomyReason.CrateMoney) outcome.Money = amount;
			else if (r.Reason == EconomyReason.CrateScrap) outcome.Scraps = amount;
			else outcome.Exp = amount;
			return outcome;
		}

		public static (int Min, int Max) CardRange(EconomyReason reason, int level)
		{
			int low = Math.Max(1, level - 1);
			int high = level + 1;
			switch (reason)
			{
				case EconomyReason.CrateMoney:
					return ((low + (int)Math.Floor(low * 0.4)) * 15, (high + (int)Math.Ceiling(high * 0.4)) * 45);
				case EconomyReason.CrateScrap:
					return (Math.Max(1, low * 5), high * 15);
				default:
					int diff = Math.Max(StatsHandlers.GetDiffToNextLvl(level - 1), StatsHandlers.GetDiffToNextLvl(level));
					return (1, Math.Max(1, (int)Math.Ceiling(diff * 0.35)));
			}
		}

		public static int SpentPoints() => GarageUpgradeHandler.CalculateSpentPoints(State.GarageState.PlayerUpgradeLevels);

		private static EconomyOutcome SkillReset(EconomyRequestPacket r)
		{
			int points = SpentPoints();
			if (points == 0) return Invalid("no skill unlocked");
			int cost = points * SkillResetCostPerPoint;
			if (State.WorldState.Money < cost) return EconomyOutcome.Refused(EconomyRefusal.NoMoney, $"costs {cost}");
			return new EconomyOutcome
			{
				Money = -cost,
				Note = r.Money != -cost ? $"client asked {r.Money} for {points} points" : $"{points} points",
				Effect = GarageUpgradeHandler.ResetPointSkills,
			};
		}

		private static EconomyOutcome CarSale(int client, EconomyRequestPacket r)
		{
			if (r.Money <= 0 || r.Money > MaxCarSalePrice) return Invalid($"price {r.Money} outside 1..{MaxCarSalePrice}");
			if (r.CarLoaderId >= 0)
			{
				var entry = CarPartsStore.Get(r.CarLoaderId);
				if (entry == null || entry.SpawnSeq != r.SpawnSeq) return EconomyOutcome.Refused(EconomyRefusal.Gone, $"loader {r.CarLoaderId} SpawnSeq {r.SpawnSeq} is not there");
				if (entry.Spawn?.IsJob == true) return Invalid($"loader {r.CarLoaderId} is a job car");
				int awayOwner = CarAwayRegistry.OwnerOf(r.CarLoaderId);
				if (awayOwner >= 0) return EconomyOutcome.Refused(EconomyRefusal.Busy, $"loader {r.CarLoaderId} is away with client {awayOwner}");
				int lockOwner = CarLocks.OtherOwnerOn(r.CarLoaderId, client);
				if (lockOwner >= 0) return EconomyOutcome.Refused(EconomyRefusal.Busy, $"client {lockOwner} holds a lock on the car");
				int loader = r.CarLoaderId;
				return new EconomyOutcome
				{
					Money = r.Money,
					Note = $"loader {loader} ({entry.Spawn?.CarToLoad})",
					Effect = () =>
					{
						CarPartsStore.ClearLoader(loader, ClearReason.Sold);
						Server.SendToClients(new CarSpawnDeletePacket { CarLoaderID = loader });
					},
				};
			}
			if (r.ParkingSlot >= 0)
			{
				var car = ParkingService.Get(r.ParkingSlot);
				if (car == null || !Guid.TryParse(r.CarId, out var id) || car.Id != id) return EconomyOutcome.Refused(EconomyRefusal.Gone, $"slot {r.ParkingSlot} does not hold {r.CarId}");
				int slot = r.ParkingSlot;
				return new EconomyOutcome
				{
					Money = r.Money,
					Note = $"parking slot {slot} ({car.CarToLoad})",
					Effect = () =>
					{
						ParkingService.TryRemove(slot, id);
						ParkingService.BroadcastSlot(slot);
					},
				};
			}
			return Invalid("no car");
		}

		private static EconomyOutcome ScrapItem(EconomyRequestPacket r)
		{
			var item = State.InventoryState.InventoryItems.FirstOrDefault(i => i.UID == r.ItemUid);
			if (item == null) return EconomyOutcome.Refused(EconomyRefusal.Gone, $"item {r.ItemUid} is not in the inventory");
			if (r.Arg < 0 || r.Arg > 2) return Invalid($"grade {r.Arg}");
			int? computed = ScrapFormulas.ScrapFromItem(item, r.Arg);
			int scraps;
			string note = item.ID;
			if (computed.HasValue)
			{
				scraps = computed.Value;
				if (scraps != r.Scraps) note += $", client computed {r.Scraps}";
			}
			else
			{
				if (r.Scraps < 1 || r.Scraps > WorkExpLimit) return Invalid($"{item.ID} is not in the database and the client amount {r.Scraps} is out of range");
				scraps = r.Scraps;
				note += " (not in the database, client amount)";
			}
			return new EconomyOutcome { Scraps = scraps, Note = note, Effect = () => EconomyService.RemoveItem(item) };
		}

		private static EconomyOutcome ScrapPerCondition(EconomyRequestPacket r)
		{
			if (r.Arg < 0 || r.Arg > 100) return Invalid($"slider {r.Arg}");
			var items = State.InventoryState.InventoryItems.Where(i => ScrapFormulas.ScrapsPerCondition(i, r.Arg)).ToList();
			int scraps = items.Sum(i => ScrapFormulas.ScrapFromItem(i, 0) ?? 0);
			return new EconomyOutcome
			{
				Scraps = scraps,
				Note = $"{items.Count} items at or below {r.Arg}%",
				Effect = () => { foreach (var item in items) EconomyService.RemoveItem(item); },
			};
		}

		private static EconomyOutcome ScrapUpgrade(EconomyRequestPacket r)
		{
			var item = State.InventoryState.InventoryItems.FirstOrDefault(i => i.UID == r.ItemUid);
			if (item == null) return EconomyOutcome.Refused(EconomyRefusal.Gone, $"item {r.ItemUid} is not in the inventory");
			int target = r.Arg;
			if (target != item.Quality + 1 || target > ScrapFormulas.MaxQuality) return Invalid($"quality {item.Quality} -> {target}");
			int cost = ScrapFormulas.UpgradeCost(target, ScrapFormulas.UpgradeItemValue(item, target));
			if (State.WorldState.Scraps < cost) return EconomyOutcome.Refused(EconomyRefusal.NoScraps, $"costs {cost}");
			return new EconomyOutcome
			{
				Scraps = -cost,
				Note = $"{item.ID} to quality {target}{(r.Scraps != -cost ? $", client cost {-r.Scraps}" : "")}",
				Effect = () =>
				{
					item.Quality = target;
					EconomyService.ReplaceItem(item);
				},
			};
		}

		private static EconomyOutcome LicensePlates(EconomyRequestPacket r)
		{
			var plates = r.Items;
			if (plates == null || plates.Count == 0 || plates.Count > MaxPlates || plates.Any(p => p == null || p.ID != "LicensePlate"))
				return Invalid("no plates");
			int price = -r.Money;
			int max = plates.Count * (r.Arg == 1 ? CustomPlatePrice : PlatePrice);
			if (price <= 0 || price > max) return Invalid($"price {price} outside 1..{max}");
			if (State.WorldState.Money < price) return EconomyOutcome.Refused(EconomyRefusal.NoMoney, $"costs {price}");
			return new EconomyOutcome
			{
				Money = -price,
				Note = $"{plates.Count} plates '{plates[0].LPData?.Custom}'",
				Effect = () =>
				{
					long uid = InventoryHandlers.GenerateNewUID();
					foreach (var plate in plates)
					{
						plate.UID = uid++;
						State.InventoryState.InventoryItems.Add(plate);
						Server.SendToClients(new InventoryItemActionPacket { Action = ItemActionType.Add, Item = plate });
					}
				},
			};
		}

		private static EconomyOutcome BarnMap(EconomyRequestPacket r)
		{
			var item = State.InventoryState.InventoryItems.FirstOrDefault(i => i.UID == r.ItemUid);
			if (item == null) return EconomyOutcome.Refused(EconomyRefusal.Gone, $"map {r.ItemUid} is not in the inventory");
			if (!IsBarnMap(item.ID)) return Invalid($"{item.ID} is not a barn map");
			return new EconomyOutcome { Barns = 1, Note = item.ID, Effect = () => EconomyService.RemoveItem(item) };
		}

		public static bool IsBarnMap(string id) =>
			id == "specialMap" || ScrapFormulas.TryGetProperty(id, out var property) && property.SpecialGroup == SpecialGroup.SpecialMap;

		public static bool IsCase(string id) =>
			id == "specialCase" || ScrapFormulas.TryGetProperty(id, out var property) && property.SpecialGroup == SpecialGroup.SpecialCase;
	}
}
