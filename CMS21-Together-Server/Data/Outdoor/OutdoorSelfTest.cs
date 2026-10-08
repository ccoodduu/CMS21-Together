using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using CMS21_Together_Core;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Economy;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Outdoor
{
	public static class OutdoorSelfTest
	{
		private static readonly List<(int To, INetworkData Packet)> sent = new List<(int, INetworkData)>();
		private static float now;
		private static int failures;
		private static int checks;

		public static int Run()
		{
			CMS21_Together_Core.Logging.Log.SetLogger(new Log.ServerLoggerAdapter());
			OutdoorNet.Send = (to, packet) => sent.Add((to, packet));
			OutdoorNet.NameOf = id => $"P{id}";
			OutdoorNet.MembershipChanged = (_, _) => { };
			OutdoorNet.Money = () => 100000;
			OutdoorNet.HistoryChanged = () => { };
			OutdoorNet.Now = () => now;
			CarCatalog.SharedDlcSet = () => new List<string> { "3" };

			Run("selector: 1,000 junkyard visits never repeat a model and skip the previous visit", SelectorVisits);
			Run("selector: small catalogs still fill the picks", SelectorSmallCatalog);
			Run("selector: unknown name falls back to basic", UnknownSelector);
			Run("history: save and load keep the last visits", HistoryRoundTrip);
			Run("catalog: intersection of the reports, DLC limited to the shared set", CatalogIntersection);
			Run("instances: join, leave, close, new seed, grace after a disconnect", InstanceLifecycle);
			Run("instances: a scene that is not shared gets no instance", NotShared);
			Run("instances: the generator leaving before its record promotes the next player", GeneratorPromotion);
			Run("loot: record, first take wins, put back, release on leave, purchase of held items", Loot);
			Run("cars: a sold car is Taken for the next buyer and removed for the others", CarPurchase);
			Run("auction: lots wait for the amounts, one bidder per lot, relay, raise, close on leave", Auction);
			Run("digest: equal digests pass, differences are counted per field", Digest);
			Run("economy: joining an open barn uses no barn", BarnCount);
			Run("packets: every outdoor packet survives a BinaryFormatter round trip", PacketRoundTrip);

			Console.WriteLine($"outdoor self-test: {checks - failures}/{checks} checks passed -> {(failures == 0 ? "OK" : "FAILED")}");
			return failures == 0 ? 0 : 1;
		}

		private static void Run(string name, Action test)
		{
			int before = failures;
			Fresh();
			try { test(); }
			catch (Exception ex) { Fail($"threw {ex.GetType().Name}: {ex.Message}"); }
			Console.WriteLine($"{(failures == before ? "ok  " : "FAIL")} {name}");
		}

		private static void Check(bool condition, string what)
		{
			checks++;
			if (condition) return;
			failures++;
			Console.WriteLine($"     - {what}");
		}

		private static void Fail(string what) => Check(false, what);

		private static void Fresh()
		{
			sent.Clear();
			now = 0f;
			OutdoorInstances.Reset();
			OutdoorInstances.Configure(OutdoorScenes.All, "basic", 60, false);
			OutdoorInstances.Rng = new Random(1234);
			OutdoorInstances.History.Reset();
			CarCatalog.Reset();
		}

		private static List<CatalogEntry> Models(int count, string prefix = "car_", int configs = 2) =>
			Enumerable.Range(0, count).SelectMany(m => Enumerable.Range(0, configs).Select(c => new CatalogEntry { CarId = $"{prefix}{m:00}", ConfigVersion = c })).ToList();

		private static void Report(int client, params (OutdoorCatalogScene Scene, List<CatalogEntry> Entries)[] scenes)
		{
			var packet = new OutdoorCatalogPacket();
			foreach (var scene in scenes) packet.Scenes[scene.Scene] = scene.Entries;
			CarCatalog.Report(client, packet);
		}

		private static void ReportAll(int client, int models = 40)
		{
			Report(client, (OutdoorCatalogScene.Junkyard, Models(models)), (OutdoorCatalogScene.Barn, Models(models)),
				(OutdoorCatalogScene.AuctionNormal, Models(models)), (OutdoorCatalogScene.AuctionSalvage, Models(models)));
		}

		private static List<T> Sent<T>(int to) where T : INetworkData => sent.Where(s => s.To == to).Select(s => s.Packet).OfType<T>().ToList();

		private static void SelectorVisits()
		{
			var selector = new BasicCarSelector();
			var history = new SelectionHistory();
			var random = new Random(7);
			var catalog = Models(40);
			List<string> previous = null;
			bool repeated = false, reused = false, wrongCount = false;
			for (int visit = 0; visit < 1000; visit++)
			{
				var picks = selector.Select(new SelectionRequest { Scene = OutdoorCatalogScene.Junkyard, Count = 15, Candidates = catalog, History = history, Random = random });
				var models = picks.Select(p => p.CarId).ToList();
				wrongCount |= models.Count != 15;
				repeated |= models.Distinct().Count() != models.Count;
				reused |= previous != null && models.Intersect(previous).Any();
				history.Record(OutdoorCatalogScene.Junkyard, models);
				previous = models;
			}
			Check(!wrongCount, "every visit has 15 picks");
			Check(!repeated, "no model twice in one visit");
			Check(!reused, "no model of the previous visit");
			Check(history.Visits(OutdoorCatalogScene.Junkyard).Count == SelectionHistory.VisitsKept, "the history keeps three visits");

			var all = selector.Select(new SelectionRequest { Scene = OutdoorCatalogScene.Junkyard, Count = 40, Candidates = catalog, History = history, Random = random });
			Check(all.Select(p => p.CarId).Distinct().Count() == 40, "40 picks from 40 models are all different");
		}

		private static void SelectorSmallCatalog()
		{
			var selector = new BasicCarSelector();
			var history = new SelectionHistory();
			var random = new Random(3);
			var catalog = Models(5, configs: 1);
			var first = selector.Select(new SelectionRequest { Scene = OutdoorCatalogScene.Barn, Count = 3, Candidates = catalog, History = history, Random = random });
			Check(first.Count == 3 && first.Select(p => p.CarId).Distinct().Count() == 3, "3 different picks from 5 models");
			history.Record(OutdoorCatalogScene.Barn, first.Select(p => p.CarId));
			var second = selector.Select(new SelectionRequest { Scene = OutdoorCatalogScene.Barn, Count = 3, Candidates = catalog, History = history, Random = random });
			Check(second.Count == 3 && second.Select(p => p.CarId).Distinct().Count() == 3, "the next visit still fills 3 different picks");
			Check(second.Count(p => !first.Any(f => f.CarId == p.CarId)) == 2, "the 2 models not in the previous visit come first");
			var tiny = selector.Select(new SelectionRequest { Scene = OutdoorCatalogScene.Barn, Count = 3, Candidates = Models(2, configs: 1), History = null, Random = random });
			Check(tiny.Count == 3, "2 models still give 3 picks");
			var none = selector.Select(new SelectionRequest { Scene = OutdoorCatalogScene.Barn, Count = 3, Candidates = new List<CatalogEntry>(), History = null, Random = random });
			Check(none.Count == 0, "an empty catalog gives no picks");
		}

		private static void UnknownSelector()
		{
			Check(CarSelectors.Create("lvx").Name == "basic", "lvx is not built in and falls back to basic");
			Check(CarSelectors.Create("BASIC").Name == "basic", "names ignore case");
		}

		private static void HistoryRoundTrip()
		{
			OutdoorInstances.History.Record(OutdoorCatalogScene.Junkyard, new[] { "a", "b" });
			OutdoorInstances.History.Record(OutdoorCatalogScene.Junkyard, new[] { "c" });
			OutdoorInstances.History.Record(OutdoorCatalogScene.Barn, new[] { "d", "e", "f" });
			var section = new OutdoorSection();
			string saved = section.Save().ToString();
			section.Reset();
			Check(OutdoorInstances.History.LastVisit(OutdoorCatalogScene.Junkyard).Count == 0, "reset empties the history");
			section.Load(JToken.Parse(saved));
			Check(string.Join(",", OutdoorInstances.History.LastVisit(OutdoorCatalogScene.Junkyard)) == "c", "the last junkyard visit survives a save and load");
			Check(OutdoorInstances.History.Visits(OutdoorCatalogScene.Junkyard).Count == 2, "both junkyard visits survive");
			Check(string.Join(",", OutdoorInstances.History.LastVisit(OutdoorCatalogScene.Barn)) == "d,e,f", "the barn visit survives");
			section.Load(null);
			Check(OutdoorInstances.History.LastVisit(OutdoorCatalogScene.Barn).Count == 0, "a save without the section starts empty");
		}

		private static void CatalogIntersection()
		{
			var a = Models(10);
			a.Add(new CatalogEntry { CarId = "dlc_shared", ConfigVersion = 0, Dlc = 3 });
			a.Add(new CatalogEntry { CarId = "dlc_other", ConfigVersion = 0, Dlc = 5 });
			var b = Models(8);
			b.Add(new CatalogEntry { CarId = "dlc_shared", ConfigVersion = 0, Dlc = 3 });
			b.Add(new CatalogEntry { CarId = "dlc_other", ConfigVersion = 0, Dlc = 5 });
			Report(1, (OutdoorCatalogScene.Junkyard, a));
			Check(CarCatalog.For(OutdoorCatalogScene.Junkyard).Count == 21, "one report: its cars minus DLC outside the shared set");
			Report(2, (OutdoorCatalogScene.Junkyard, b));
			var shared = CarCatalog.For(OutdoorCatalogScene.Junkyard);
			Check(shared.Count == 17, $"two reports: 8 models x 2 configs + the shared DLC car ({shared.Count})");
			Check(shared.All(e => e.CarId != "dlc_other"), "a DLC car outside the shared DLC set is left out");
			Check(CarCatalog.For(OutdoorCatalogScene.Barn).Count == 0, "a scene one player did not report is empty");
			CarCatalog.Remove(2);
			Check(CarCatalog.For(OutdoorCatalogScene.Junkyard).Count == 21, "the intersection grows again when a player leaves");
		}

		private static void InstanceLifecycle()
		{
			ReportAll(1);
			ReportAll(2);
			var a = OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			Check(a.Shared && a.Generator, "the first player opens the junkyard and is its generator");
			Check(a.Picks.Count == OutdoorInstances.JunkyardPickCount, $"the junkyard has {OutdoorInstances.JunkyardPickCount} picks ({a.Picks.Count})");
			var b = OutdoorInstances.Enter(2, GameScene.Junkyard, now);
			Check(b.InstanceId == a.InstanceId && b.Seed == a.Seed && !b.Generator, "the second player joins the same instance, not as generator");
			Check(string.Join(",", b.Picks) == string.Join(",", a.Picks), "both get the same picks");

			OutdoorInstances.OnSceneChanged(1, GameScene.Junkyard, GameScene.Loading);
			Check(OutdoorInstances.Of(GameScene.Junkyard)?.Members.SequenceEqual(new[] { 2 }) == true, "leaving the junkyard for the loading screen leaves the instance");
			OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			OutdoorInstances.OnSceneChanged(1, GameScene.Garage, GameScene.Loading);
			Check(OutdoorInstances.Of(GameScene.Junkyard)?.HasMember(1) == true, "the loading screen on the way in keeps the member");
			OutdoorInstances.OnSceneChanged(1, GameScene.Loading, GameScene.Junkyard);
			Check(OutdoorInstances.Of(GameScene.Junkyard)?.HasMember(1) == true, "arriving keeps the member");

			OutdoorInstances.OnSceneChanged(1, GameScene.Junkyard, GameScene.Loading);
			OutdoorInstances.OnSceneChanged(2, GameScene.Junkyard, GameScene.Loading);
			Check(!OutdoorInstances.IsOpen(GameScene.Junkyard), "the instance closes when the last player travels away");
			var again = OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			Check(again.InstanceId != a.InstanceId && again.Seed != a.Seed, "the next visit is a new instance with a new seed");

			OutdoorInstances.OnLeft(1);
			Check(OutdoorInstances.IsOpen(GameScene.Junkyard), "a disconnect keeps the instance for the grace period");
			now = 30f;
			OutdoorInstances.Tick(now);
			var back = OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			Check(back.InstanceId == again.InstanceId, "returning within the grace finds the same instance");
			OutdoorInstances.OnLeft(1);
			now = 100f;
			OutdoorInstances.Tick(now);
			Check(!OutdoorInstances.IsOpen(GameScene.Junkyard), "nobody back after the grace closes it");

			OutdoorInstances.Enter(2, GameScene.Barn, now);
			OutdoorInstances.Enter(2, GameScene.Junkyard, now);
			Check(!OutdoorInstances.IsOpen(GameScene.Barn) && OutdoorInstances.IsOpen(GameScene.Junkyard), "entering another scene leaves the first instance");
		}

		private static void NotShared()
		{
			OutdoorInstances.Configure(new[] { GameScene.Junkyard, GameScene.Auction }, "basic", 60, false);
			var barn = OutdoorInstances.Enter(1, GameScene.Barn, now);
			Check(!barn.Shared && !OutdoorInstances.IsOpen(GameScene.Barn), "the barn is local when shared_outdoor_scenes leaves it out");
			Check(OutdoorScenes.Parse("junkyard, AUCTION,foo").SequenceEqual(new[] { GameScene.Junkyard, GameScene.Auction }), "the setting is parsed case-insensitively and skips unknown names");
			Check(OutdoorScenes.Parse("").Count == 0, "an empty setting shares nothing");
		}

		private static void GeneratorPromotion()
		{
			ReportAll(1);
			OutdoorInstances.Enter(1, GameScene.Barn, now);
			OutdoorInstances.Enter(2, GameScene.Barn, now);
			sent.Clear();
			OutdoorInstances.OnSceneChanged(1, GameScene.Barn, GameScene.Loading);
			var promoted = Sent<OutdoorInstancePacket>(2);
			Check(promoted.Count == 1 && promoted[0].Generator, "the next player is told it is the generator");
			OutdoorInstances.OnLootRecord(1, new OutdoorLootRecordPacket { InstanceId = promoted[0].InstanceId });
			Check(!OutdoorInstances.Of(GameScene.Barn).LootRecorded, "a record from a player who left is ignored");
			OutdoorInstances.OnLootRecord(2, Record(promoted[0].InstanceId));
			Check(OutdoorInstances.Of(GameScene.Barn).LootRecorded, "the new generator's record is stored");
		}

		private static OutdoorLootRecordPacket Record(int instanceId) => new OutdoorLootRecordPacket
		{
			InstanceId = instanceId,
			Piles = new List<LootPile>
			{
				new LootPile { Index = 0, Key = "1.0,0.0,2.0", Items = new List<ModItem> { new ModItem { ID = "tuleja_1", UID = 11 }, new ModItem { ID = "felga_1", UID = 12 } } },
				new LootPile { Index = 1, Key = "5.0,0.0,2.0", Items = new List<ModItem> { new ModItem { ID = "tarcza_1", UID = 21 } } },
			},
			RandomShowActive = new List<bool> { true, false },
		};

		private static void Loot()
		{
			var a = OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			OutdoorInstances.Enter(2, GameScene.Junkyard, now);
			OutdoorInstances.OnLootRecord(2, Record(a.InstanceId));
			Check(!OutdoorInstances.Of(GameScene.Junkyard).LootRecorded, "a record from a non-generator is ignored");
			sent.Clear();
			OutdoorInstances.OnLootRecord(1, Record(a.InstanceId));
			var update = Sent<OutdoorInstancePacket>(2);
			Check(update.Count == 1 && update[0].Piles?.Count == 2, "the record reaches the other player");
			Check(Sent<OutdoorInstancePacket>(1).Count == 0, "the generator is not sent its own record");

			sent.Clear();
			LootService.Take(2, new LootTakePacket { InstanceId = a.InstanceId, Uid = 11 });
			var taken = Sent<LootUpdatePacket>(1);
			Check(taken.Count == 1 && taken[0].Status == LootItemStatus.Held && taken[0].By == 2, "the other player is told the item is gone");
			LootService.Take(1, new LootTakePacket { InstanceId = a.InstanceId, Uid = 11 });
			var refused = Sent<LootTakeRefusedPacket>(1);
			Check(refused.Count == 1 && refused[0].By == 2 && refused[0].ByName == "P2", "the second take is refused and names the first taker");
			LootService.Take(1, new LootTakePacket { InstanceId = a.InstanceId, Uid = 999 });
			Check(Sent<LootTakeRefusedPacket>(1).Count == 2, "an unknown item is refused");

			var late = OutdoorInstances.Enter(3, GameScene.Junkyard, now);
			Check(late.ItemStates.Any(s => s.Uid == 11 && s.Status == LootItemStatus.Held), "a late arrival gets the held item");

			sent.Clear();
			LootService.PutBack(1, new LootPutBackPacket { InstanceId = a.InstanceId, Uid = 11 });
			Check(sent.Count == 0, "a put back by a player who does not hold it is ignored");
			LootService.PutBack(2, new LootPutBackPacket { InstanceId = a.InstanceId, Uid = 11 });
			var back = Sent<LootUpdatePacket>(1);
			Check(back.Count == 1 && back[0].Status == LootItemStatus.Available && back[0].Item?.ID == "tuleja_1" && back[0].PileIndex == 0, "a put back returns the item with its pile");

			LootService.Take(2, new LootTakePacket { InstanceId = a.InstanceId, Uid = 12 });
			LootService.Take(2, new LootTakePacket { InstanceId = a.InstanceId, Uid = 21 });
			var accepted = LootService.HeldBy(2, a.InstanceId, new List<ModItem> { new ModItem { ID = "felga_1", UID = 12 }, new ModItem { ID = "tuleja_1", UID = 11 }, new ModItem { ID = "fake", UID = 77 } }, out var dropped);
			Check(accepted.Count == 1 && accepted[0].UID == 12 && dropped.Count == 2, "only the buyer's held items are accepted");
			LootService.MarkBought(2, a.InstanceId, accepted.Select(i => i.UID));

			sent.Clear();
			OutdoorInstances.OnLeft(2);
			var released = Sent<LootUpdatePacket>(1);
			Check(released.Count == 1 && released[0].Uid == 21 && released[0].Status == LootItemStatus.Available, "the unpaid item returns when its taker disconnects, the bought one does not");
			Check(OutdoorInstances.Of(GameScene.Junkyard).Loot[12].Status == LootItemStatus.Bought, "the bought item stays bought");
		}

		private static void CarPurchase()
		{
			var a = OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			OutdoorInstances.Enter(2, GameScene.Junkyard, now);
			var request = new CarParkRequestPacket { SourceInstanceId = a.InstanceId, SourceCarIndex = 3 };
			Check(OutdoorInstances.CheckCarPurchase(1, request) == ParkRefusal.None, "an unsold car can be bought");
			sent.Clear();
			OutdoorInstances.CarPurchaseAccepted(1, request);
			var removed = Sent<OutdoorCarRemovedPacket>(2);
			Check(removed.Count == 1 && removed[0].Index == 3 && removed[0].By == 1, "the other player is told to remove the car");
			Check(OutdoorInstances.CheckCarPurchase(2, new CarParkRequestPacket { SourceInstanceId = a.InstanceId, SourceCarIndex = 3 }) == ParkRefusal.Taken, "the same car is Taken for the next buyer");
			Check(OutdoorInstances.CheckCarPurchase(2, new CarParkRequestPacket { SourceCarIndex = 3 }) == ParkRefusal.None, "a purchase without an instance is not checked");
			var late = OutdoorInstances.Enter(3, GameScene.Junkyard, now);
			Check(late.Sold.SequenceEqual(new[] { 3 }), "a late arrival gets the sold car");
		}

		private static void Auction()
		{
			ReportAll(1);
			ReportAll(2);
			var a = OutdoorInstances.Enter(1, GameScene.Auction, now);
			Check(a.LotsPending && a.Lots.Count == 0 && a.Generator, "without known amounts the lots wait for the generator");
			OutdoorInstances.Enter(2, GameScene.Auction, now);
			sent.Clear();
			OutdoorInstances.OnDigest(1, new OutdoorDigestPacket
			{
				InstanceId = a.InstanceId,
				AuctionAmounts = new List<AuctionAmountRange>
				{
					new AuctionAmountRange { Kind = AuctionKind.Normal, Min = 4, Max = 8 },
					new AuctionAmountRange { Kind = AuctionKind.Salvage, Min = 2, Max = 4 },
				},
			});
			var instance = OutdoorInstances.Of(GameScene.Auction);
			Check(!instance.LotsPending && instance.Lots.Count >= 6, $"the amounts build the lots ({instance.Lots.Count})");
			Check(Sent<OutdoorInstancePacket>(2).Count == 1 && Sent<OutdoorInstancePacket>(1).Count == 1, "both players get the lots");
			Check(instance.Lots.Select(l => l.Index).SequenceEqual(Enumerable.Range(0, instance.Lots.Count)), "lot indices run over both auction types");

			var values = instance.Lots.Select(l => new AuctionLot { Index = l.Index, CarId = l.CarId, Rating = 3, Value = 10000, StartingPrice = 5000 }).ToList();
			OutdoorInstances.OnDigest(2, new OutdoorDigestPacket { InstanceId = a.InstanceId, LotValues = values });
			Check(instance.Lots.All(l => !l.ValuesKnown), "lot values from a non-generator are ignored");
			OutdoorInstances.OnDigest(1, new OutdoorDigestPacket { InstanceId = a.InstanceId, LotValues = values });
			Check(instance.Lots.All(l => l.ValuesKnown && l.StartingPrice == 5000), "the generator's lot values are stored");

			sent.Clear();
			AuctionService.Claim(1, new AuctionLotClaimPacket { InstanceId = a.InstanceId, Lot = 1 });
			Check(Sent<AuctionLotClaimPacket>(1).Single().Granted && Sent<AuctionLotClaimPacket>(2).Single().OwnerId == 1, "the first claim is granted and shown to the other player");
			AuctionService.Claim(2, new AuctionLotClaimPacket { InstanceId = a.InstanceId, Lot = 1 });
			var refusal = Sent<AuctionLotClaimPacket>(2).Last();
			Check(!refusal.Granted && refusal.OwnerName == "P1", "a second claim is refused and names the bidder");

			sent.Clear();
			AuctionService.BidState(2, new AuctionBidStatePacket { InstanceId = a.InstanceId, State = new AuctionBidSnapshot { Lot = 1, CurrentBid = 999 } });
			Check(sent.Count == 0, "a bid state from a player who does not run the lot is ignored");
			AuctionService.BidState(1, new AuctionBidStatePacket { InstanceId = a.InstanceId, State = new AuctionBidSnapshot { Lot = 1, CurrentBid = 6000, BidStep = 500, TeamLeads = false, SecondsLeft = 9f } });
			Check(Sent<AuctionBidStatePacket>(2).Single().State.CurrentBid == 6000, "the bid state is relayed");
			var lateState = OutdoorInstances.Enter(3, GameScene.Auction, now);
			Check(lateState.LotStates.Single(s => s.Lot == 1).LastBid?.CurrentBid == 6000, "a late arrival gets the current bid");

			sent.Clear();
			AuctionService.BidRequest(2, new AuctionBidRequestPacket { InstanceId = a.InstanceId, Lot = 1, SeenBid = 6000 });
			var forwarded = Sent<AuctionBidRequestPacket>(1);
			Check(forwarded.Count == 1 && forwarded[0].RequesterId == 2, "a raise is forwarded to the bidding player");
			AuctionService.BidRequest(2, new AuctionBidRequestPacket { InstanceId = a.InstanceId, Lot = 2 });
			Check(Sent<AuctionBidRequestPacket>(1).Count == 1, "a raise on a lot nobody bids on is not forwarded");

			Check(OutdoorInstances.CheckCarPurchase(2, new CarParkRequestPacket { SourceInstanceId = a.InstanceId, SourceLot = 1 }) == ParkRefusal.Taken, "only the bidding player can buy the lot");
			Check(OutdoorInstances.CheckCarPurchase(1, new CarParkRequestPacket { SourceInstanceId = a.InstanceId, SourceLot = 1 }) == ParkRefusal.None, "the bidding player can buy the lot");
			sent.Clear();
			OutdoorInstances.CarPurchaseAccepted(1, new CarParkRequestPacket { SourceInstanceId = a.InstanceId, SourceLot = 1 });
			Check(Sent<AuctionLotClosedPacket>(2).Single().Status == AuctionLotStatus.Won, "a bought lot is closed as won for everyone");
			Check(OutdoorInstances.Enter(3, GameScene.Auction, now).Lots.All(l => l.Index != 1), "a won lot is not offered to later arrivals");

			AuctionService.Claim(1, new AuctionLotClaimPacket { InstanceId = a.InstanceId, Lot = 2 });
			sent.Clear();
			OutdoorInstances.OnSceneChanged(1, GameScene.Auction, GameScene.Loading);
			var closed = Sent<AuctionLotClosedPacket>(2);
			Check(closed.Count == 1 && closed[0].Lot == 2 && closed[0].Status == AuctionLotStatus.Lost, "the lot of a bidding player who leaves is lost for everyone");

			AuctionService.Claim(2, new AuctionLotClaimPacket { InstanceId = a.InstanceId, Lot = 3 });
			sent.Clear();
			AuctionService.BidState(2, new AuctionBidStatePacket { InstanceId = a.InstanceId, State = new AuctionBidSnapshot { Lot = 3, Phase = AuctionBidPhase.AiWon } });
			Check(Sent<AuctionLotClosedPacket>(3).Single().Status == AuctionLotStatus.Lost, "a lot another bidder won is closed");

			OutdoorInstances.OnSceneChanged(2, GameScene.Auction, GameScene.Loading);
			OutdoorInstances.OnSceneChanged(3, GameScene.Auction, GameScene.Loading);
			var reopened = OutdoorInstances.Enter(4, GameScene.Auction, now);
			Check(reopened.InstanceId != a.InstanceId && !reopened.LotsPending && reopened.Lots.Count > 0, "known amounts build the lots of a new auction at once");
		}

		private static void Digest()
		{
			var reference = new List<string>
			{
				OutdoorDigestRow.Car(0, new Dictionary<string, string> { ["id"] = "car_a", ["colour"] = "ff00", ["missing"] = "2" }),
				OutdoorDigestRow.Car(1, new Dictionary<string, string> { ["id"] = "car_b", ["colour"] = "00ff", ["missing"] = "0" }),
			};
			Check(OutdoorDigestRow.Compare(reference, reference.ToList()).Count == 0, "equal rows have no differences");
			var other = reference.ToList();
			other[1] = OutdoorDigestRow.Car(1, new Dictionary<string, string> { ["id"] = "car_b", ["colour"] = "0f0f", ["missing"] = "0" });
			var differences = OutdoorDigestRow.Compare(reference, other);
			Check(differences.Count == 1 && differences[0].StartsWith("car:1 colour 00ff vs 0f0f"), $"a different colour is named ({string.Join("; ", differences)})");

			ReportAll(1);
			var a = OutdoorInstances.Enter(1, GameScene.Junkyard, now);
			OutdoorInstances.Enter(2, GameScene.Junkyard, now);
			var used = a.Picks.Take(2).Select(p => p.CarId).ToList();
			var rows = used.Select((id, i) => OutdoorDigestRow.Car(i, new Dictionary<string, string> { ["id"] = id, ["colour"] = "ff00" })).ToList();
			var otherRows = rows.ToList();
			otherRows[1] = OutdoorDigestRow.Car(1, new Dictionary<string, string> { ["id"] = used[1], ["colour"] = "0f0f" });
			OutdoorInstances.OnDigest(2, new OutdoorDigestPacket { InstanceId = a.InstanceId, Rows = otherRows });
			Check(OutdoorInstances.Of(GameScene.Junkyard).ReferenceDigest == null, "a non-generator digest is not the reference");
			OutdoorInstances.OnDigest(1, new OutdoorDigestPacket { InstanceId = a.InstanceId, Rows = rows });
			Check(OutdoorInstances.Of(GameScene.Junkyard).DigestMismatches == 1, "the waiting digest is compared once the reference arrives");
			Check(OutdoorInstances.History.LastVisit(OutdoorCatalogScene.Junkyard).OrderBy(m => m).SequenceEqual(used.Distinct().OrderBy(m => m)), "the history keeps only the cars the junkyard used");
		}

		private static void BarnCount()
		{
			typeof(GameDataManager).GetProperty(nameof(GameDataManager.CurrentState)).SetValue(null, new ModGameState());
			var fee = new EconomyRequestPacket { Reason = EconomyReason.TravelFee, Arg = 7, Arg2 = 1, Money = -100 };
			Check(EconomyRules.Evaluate(1, fee).Barns == -1, "a fee before the instance opens uses one barn");
			OutdoorInstances.Enter(1, GameScene.Barn, now);
			Check(EconomyRules.Evaluate(1, fee).Barns == 0, "the opener's barn is not charged twice");
			var join = EconomyRules.Evaluate(2, fee);
			Check(join.Barns == 0 && join.Money == -100, "joining the open barn uses no barn and pays the fee");
			OutdoorInstances.OnSceneChanged(1, GameScene.Barn, GameScene.Loading);
			OutdoorInstances.OnSceneChanged(2, GameScene.Barn, GameScene.Loading);
			OutdoorInstances.Enter(3, GameScene.Barn, now);
			Check(EconomyRules.Evaluate(3, fee).Barns == -1, "a fee after OutdoorEnter (the order seen at runtime) uses one barn for the opener");
			Check(EconomyRules.Evaluate(3, fee).Barns == 0, "and only once");
		}

		private static void PacketRoundTrip()
		{
			var packets = new INetworkData[]
			{
				new OutdoorCatalogPacket { Scenes = { [OutdoorCatalogScene.Junkyard] = Models(2) } },
				new OutdoorEnterPacket { Scene = GameScene.Barn },
				new OutdoorInstancePacket { InstanceId = 3, Picks = { new OutdoorCarPick { CarId = "a" } }, Piles = Record(3).Piles, LotStates = { new AuctionLotState { Lot = 1, LastBid = new AuctionBidSnapshot() } } },
				Record(3),
				new OutdoorDigestPacket { Rows = { "car:0|id=a" }, AuctionAmounts = { new AuctionAmountRange() }, LotValues = { new AuctionLot() } },
				new LootTakePacket { Uid = 5 },
				new LootPutBackPacket { Uid = 5 },
				new LootUpdatePacket { Uid = 5, Item = new ModItem { ID = "x" } },
				new LootTakeRefusedPacket { Uid = 5, ByName = "Ann" },
				new OutdoorCarRemovedPacket { Index = 2 },
				new AuctionLotClaimPacket { Lot = 1, OwnerName = "Ann" },
				new AuctionBidStatePacket { State = new AuctionBidSnapshot { CurrentBid = 10 } },
				new AuctionBidRequestPacket { Lot = 1 },
				new AuctionLotClosedPacket { Lot = 1, Status = AuctionLotStatus.Won },
				new CarParkRequestPacket { SourceInstanceId = 2, SourceCarIndex = 4, SourceLot = -1 },
				new ItemsExchangePacket { InstanceId = 2, ItemsToBuy = new List<ModItem>() },
			};
			var formatter = new BinaryFormatter();
			foreach (var packet in packets)
			{
				object copy;
				using (var stream = new MemoryStream())
				{
					formatter.Serialize(stream, packet);
					stream.Position = 0;
					copy = formatter.Deserialize(stream);
				}
				Check(copy.GetType() == packet.GetType() && Newtonsoft.Json.JsonConvert.SerializeObject(copy) == Newtonsoft.Json.JsonConvert.SerializeObject(packet), $"{packet.GetType().Name} round trip");
			}
			var outdoorTypes = new[]
			{
				PacketTypes.OutdoorCatalog, PacketTypes.OutdoorEnter, PacketTypes.OutdoorInstance, PacketTypes.OutdoorLootRecord, PacketTypes.OutdoorDigest,
				PacketTypes.LootTake, PacketTypes.LootPutBack, PacketTypes.LootUpdate, PacketTypes.LootTakeRefused, PacketTypes.OutdoorCarRemoved,
				PacketTypes.AuctionLotClaim, PacketTypes.AuctionBidState, PacketTypes.AuctionBidRequest, PacketTypes.AuctionLotClosed,
			};
			var registered = typeof(OutdoorInstancePacket).Assembly.GetTypes()
				.Select(t => (NetworkPacket)Attribute.GetCustomAttribute(t, typeof(NetworkPacket)))
				.Where(a => a != null).Select(a => a.Type).ToList();
			Check(outdoorTypes.All(registered.Contains), $"each of the 14 outdoor packet types has a packet class ({outdoorTypes.Count(registered.Contains)})");
		}
	}
}
