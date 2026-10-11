using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Achievements
{
	// docs/spikes/shared-achievements.md, event stats: when the server accepts a purchase, sale or scrap, every connected
	// player's game counts the stat the actor's game counts. The actor is left out when its own game ran the method that
	// counts it; a method the mod skips (it asks the server instead) counted nothing, so the actor gets the award too.
	public static class EventStats
	{
		public const string BuyCarSalon = "stat_buy_carsalon";
		public const string BuyCarJunkyard = "stat_buy_carjunkyard";
		public const string BuyBarn = "stat_buy_barn";
		public const string WinCar = "stat_win_car";
		public const string SellCar = "stat_sell_car";
		public const string SellFixCar = "stat_sell_fix_car";
		public const string Scrap = "stat_scrap";
		public const string BuyParts = "stat_buy_parts";
		public const string SellJunk = "stat_sell_junk";

		private const int CarFromJunkyard = 1;
		private const int CarFromBarn = 2;
		private const int CarFromAuction = 5;
		private const int CarFromSalon = 6;

		public static string CarPurchaseStat(int carFrom) => carFrom switch
		{
			CarFromSalon => BuyCarSalon,
			CarFromJunkyard => BuyCarJunkyard,
			CarFromBarn => BuyBarn,
			CarFromAuction => WinCar,
			_ => null,
		};

		public static void CarBought(int actor, int carFrom)
		{
			string stat = CarPurchaseStat(carFrom);
			if (stat != null) Award(actor, true, "car purchase", (stat, 1));
		}

		public static void CarSold(int actor, int profit, bool fixedCar) =>
			Award(actor, true, "car sale", (SellCar, profit), (SellFixCar, fixedCar ? 1 : 0));

		public static void Scrapped(int actor, int scraps, bool actorCounted) => Award(actor, actorCounted, "scrap", (Scrap, scraps));

		public static void PartsBought(int actor, int amount) => Award(actor, false, "parts purchase", (BuyParts, amount));

		public static void JunkSold(int actor, int items, bool actorCounted) => Award(actor, actorCounted, "junk sale", (SellJunk, items));

		public static EventStatsAwardPacket Build(int actor, string what, params (string Id, int Amount)[] stats)
		{
			var award = new EventStatsAwardPacket { Event = what, Actor = actor };
			foreach (var (id, amount) in stats)
				if (amount != 0) award.Stats.Add(new StatIncrement { Id = id, Amount = amount });
			return award;
		}

		public static List<int> Receivers(int actor, bool actorCounted) =>
			Server.Clients.Values
				.Where(c => c.IsConnected && c.SyncState == SyncState.InSession && c.Identity != null && !(actorCounted && c.ID == actor))
				.Select(c => c.ID).ToList();

		private static void Award(int actor, bool actorCounted, string what, params (string Id, int Amount)[] stats)
		{
			var award = Build(actor, what, stats);
			if (award.Stats.Count == 0) return;
			var receivers = Receivers(actor, actorCounted);
			Logger.Info($"[Stats] {what} by client {actor}: [{string.Join(", ", award.Stats.Select(s => $"{s.Id} {s.Amount:+0;-0}"))}] " +
			            (receivers.Count == 0 ? "to nobody." : $"to clients {string.Join(", ", receivers)}{(actorCounted ? " (the actor counted its own)" : "")}."));
			foreach (int client in receivers) Server.SendToClient(award, client);
		}
	}
}
