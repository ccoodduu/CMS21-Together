using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;
using CMS21_Together_Server.Network.Handlers;

namespace CMS21_Together_Server.Data.Economy
{
	// economy-audit D3/D13: applies every EconomyRequest under StateLock (held by the packet dispatch) and keeps a
	// runtime ledger. Callers hold StateLock.
	public static class EconomyService
	{
		public const int MaxMoney = 900000000;
		private const int LedgerSize = 500;

		public class LedgerEntry
		{
			public DateTime UtcTime;
			public int ClientId;
			public EconomyReason Reason;
			public int Arg;
			public int RequestedMoney, RequestedScraps, RequestedExp;
			public int Money, Scraps, Exp, Barns;
			public EconomyRefusal Refusal;
			public int MoneyAfter;
			public string Note;
		}

		public class OpenedCase
		{
			public int Client;
			public DateTime Since;
			public bool Looted;
		}

		private static readonly Queue<LedgerEntry> ledger = new Queue<LedgerEntry>();
		private static readonly Dictionary<EconomyReason, int> applied = new Dictionary<EconomyReason, int>();
		private static readonly Dictionary<EconomyReason, int> refused = new Dictionary<EconomyReason, int>();
		private static readonly Dictionary<long, OpenedCase> recentCases = new Dictionary<long, OpenedCase>();

		public static IEnumerable<LedgerEntry> Ledger => ledger;

		[PacketHandler(PacketTypes.EconomyRequest)]
		public static void OnRequest(long clientId, EconomyRequestPacket request)
		{
			if (GameDataManager.CurrentState == null || request == null) return;
			Handle((int)clientId, request);
		}

		public static void Handle(int client, EconomyRequestPacket request)
		{
			var world = GameDataManager.CurrentState.WorldState;
			EconomyOutcome outcome;
			try
			{
				outcome = EconomyRules.Evaluate(client, request);
			}
			catch (Exception ex)
			{
				outcome = EconomyOutcome.Refused(EconomyRefusal.Invalid, $"rule error: {ex.Message}");
			}

			var entry = new LedgerEntry
			{
				UtcTime = DateTime.UtcNow, ClientId = client, Reason = request.Reason, Arg = request.Arg,
				RequestedMoney = request.Money, RequestedScraps = request.Scraps, RequestedExp = request.Exp,
				Refusal = outcome.Refusal, Note = outcome.Note,
			};

			if (outcome.Refusal != EconomyRefusal.None)
			{
				Count(refused, request.Reason);
				entry.MoneyAfter = world.Money;
				Remember(entry);
				Logger.Info($"[Economy] client {client} {Label(request)} refused {outcome.Refusal}: {outcome.Note} (client money {request.Money}, scraps {request.Scraps}, exp {request.Exp})");
				Server.SendToClient(Result(request, false, outcome.Refusal), client);
				world.updateGamemode = false;
				Server.SendToClient(world, client);
				outcome.Answer?.Invoke(client);
				return;
			}

			outcome.Effect?.Invoke(client);
			int moneyBefore = world.Money, scrapsBefore = world.Scraps, levelBefore = world.Level, expBefore = world.Exp, barnsBefore = world.Barns;
			world.Money = (int)Math.Max(0, Math.Min(MaxMoney, (long)world.Money + outcome.Money));
			world.Scraps = Math.Max(0, world.Scraps + outcome.Scraps);
			if (outcome.Exp > 0) StatsHandlers.ApplyExp(outcome.Exp);
			world.Barns = Math.Max(0, world.Barns + outcome.Barns);

			entry.Money = world.Money - moneyBefore;
			entry.Scraps = world.Scraps - scrapsBefore;
			entry.Exp = outcome.Exp;
			entry.Barns = world.Barns - barnsBefore;
			entry.MoneyAfter = world.Money;
			Count(applied, request.Reason);
			Remember(entry);

			string line = $"[Economy] client {client} {Label(request)} money {entry.Money:+0;-0;0} -> {world.Money}, scraps {entry.Scraps:+0;-0;0} -> {world.Scraps}, exp {outcome.Exp}, barns {entry.Barns:+0;-0;0} -> {world.Barns}{(outcome.Note != null ? $" ({outcome.Note})" : "")}";
			if (request.Reason == EconomyReason.Work) Logger.Debug(line);
			else Logger.Info(line);

			bool changed = world.Money != moneyBefore || world.Scraps != scrapsBefore || world.Level != levelBefore || world.Exp != expBefore || world.Barns != barnsBefore;
			if (changed || outcome.Money != request.Money || outcome.Scraps != request.Scraps)
			{
				world.updateGamemode = false;
				Server.SendToClients(world);
			}
			if (EconomyRules.Trades.Contains(request.Reason))
			{
				var result = Result(request, true, EconomyRefusal.None);
				Server.SendToClient(result, client);
				if (request.Reason == EconomyReason.SkillReset)
				{
					result.RequestId = 0;
					Server.SendToClients(result, client);
				}
			}
		}

		private static EconomyResultPacket Result(EconomyRequestPacket request, bool accepted, EconomyRefusal refusal)
		{
			var world = GameDataManager.CurrentState.WorldState;
			return new EconomyResultPacket
			{
				RequestId = request.RequestId, Reason = request.Reason, Accepted = accepted, Refusal = refusal,
				Money = world.Money, Scraps = world.Scraps,
			};
		}

		private static string Label(EconomyRequestPacket request) => $"{request.Reason}({request.Arg})";

		private static void Count(Dictionary<EconomyReason, int> counts, EconomyReason reason) =>
			counts[reason] = counts.TryGetValue(reason, out int count) ? count + 1 : 1;

		private static void Remember(LedgerEntry entry)
		{
			ledger.Enqueue(entry);
			while (ledger.Count > LedgerSize) ledger.Dequeue();
		}

		public static void RemoveItem(ModItem item, int remover)
		{
			GameDataManager.CurrentState.InventoryState.InventoryItems.RemoveAll(i => i.UID == item.UID);
			Cars.InventoryChanges.NoteRemoved(item, remover);
			Server.SendToClients(new InventoryItemActionPacket { Action = ItemActionType.Remove, Item = item });
		}

		public static void ReplaceItem(ModItem item)
		{
			Server.SendToClients(new InventoryItemActionPacket { Action = ItemActionType.Remove, Item = item });
			Server.SendToClients(new InventoryItemActionPacket { Action = ItemActionType.Add, Item = item });
		}

		public static void OnInventoryRemoved(int client, ModItem item)
		{
			if (item == null || !EconomyRules.IsCase(item.ID)) return;
			PruneCases();
			recentCases[item.UID] = new OpenedCase { Client = client, Since = DateTime.UtcNow };
			Logger.Debug($"[Economy] Case {item.UID} opened by client {client}.");
		}

		public static void OnInventoryAdded(ModItem item)
		{
			if (item != null && recentCases.Remove(item.UID))
				Logger.Debug($"[Economy] Case {item.UID} is back in the inventory.");
		}

		public static bool TryGetCase(long uid, out OpenedCase opened)
		{
			PruneCases();
			return recentCases.TryGetValue(uid, out opened);
		}

		private static void PruneCases()
		{
			var cutoff = DateTime.UtcNow.AddSeconds(-EconomyRules.CaseLifetimeSeconds);
			foreach (long uid in recentCases.Where(c => c.Value.Since < cutoff).Select(c => c.Key).ToList())
				recentCases.Remove(uid);
		}

		public static IEnumerable<string> Describe(int count)
		{
			var entries = ledger.Skip(Math.Max(0, ledger.Count - count)).ToList();
			if (entries.Count == 0) yield return "  no economy requests yet";
			foreach (var e in entries)
			{
				string outcome = e.Refusal == EconomyRefusal.None
					? $"money {e.Money:+0;-0;0} scraps {e.Scraps:+0;-0;0} exp {e.Exp} barns {e.Barns:+0;-0;0}"
					: $"refused {e.Refusal}";
				yield return $"  {e.UtcTime:HH:mm:ss} client {e.ClientId} {e.Reason}({e.Arg}) requested money {e.RequestedMoney} scraps {e.RequestedScraps} exp {e.RequestedExp}: {outcome}, money after {e.MoneyAfter}{(e.Note != null ? $" ({e.Note})" : "")}";
			}
			foreach (string line in DescribeReasons()) yield return line;
		}

		public static IEnumerable<string> DescribeReasons()
		{
			var reasons = applied.Keys.Union(refused.Keys).OrderBy(r => r).ToList();
			if (reasons.Count == 0) yield return "  per reason: none";
			foreach (var reason in reasons)
				yield return $"  {reason}: applied {(applied.TryGetValue(reason, out int a) ? a : 0)}, refused {(refused.TryGetValue(reason, out int r) ? r : 0)}";
		}

		public static IEnumerable<string> DescribeCases()
		{
			PruneCases();
			if (recentCases.Count == 0) yield return "  no open cases";
			foreach (var pair in recentCases)
				yield return $"  case {pair.Key}: client {pair.Value.Client}, since {pair.Value.Since:HH:mm:ss}, looted {pair.Value.Looted}";
		}
	}
}
