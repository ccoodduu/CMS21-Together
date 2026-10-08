using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Data.Outdoor
{
	public static class AuctionService
	{
		private static readonly AuctionKind[] Kinds = { AuctionKind.Normal, AuctionKind.Salvage };

		public static bool BuildLots(OutdoorInstance instance)
		{
			if (Kinds.Any(k => !OutdoorInstances.AuctionAmounts.ContainsKey(k)))
			{
				instance.LotsPending = true;
				return false;
			}

			instance.Lots.Clear();
			instance.LotStates.Clear();
			foreach (var kind in Kinds)
			{
				var range = OutdoorInstances.AuctionAmounts[kind];
				int count = LotCount(range, OutdoorInstances.Rng);
				var picks = OutdoorInstances.SelectAndRecord(OutdoorScenes.CatalogSceneFor(kind), count);
				foreach (var pick in picks)
				{
					var lot = new AuctionLot
					{
						Index = instance.Lots.Count, Kind = kind, CarId = pick.CarId, Version = pick.ConfigVersion,
						Seed = OutdoorInstances.Rng.Next(1, int.MaxValue),
					};
					instance.Lots.Add(lot);
					instance.LotStates[lot.Index] = new AuctionLotState { Lot = lot.Index, Status = AuctionLotStatus.Open };
				}
			}
			instance.LotsPending = false;
			Logger.Info($"[Outdoor] {instance.Label}: {instance.Lots.Count(l => l.Kind == AuctionKind.Normal)} normal and {instance.Lots.Count(l => l.Kind == AuctionKind.Salvage)} salvage lots.");
			return true;
		}

		public static int LotCount(AuctionAmountRange range, Random random)
		{
			float min = Math.Min(range.Min, range.Max), max = Math.Max(range.Min, range.Max);
			return Math.Max(0, (int)(min + random.NextDouble() * (max - min)));
		}

		public static bool ApplyValues(OutdoorInstance instance, int playerId, List<AuctionLot> values)
		{
			if (playerId != instance.GeneratorId)
			{
				Logger.Info($"[Outdoor] {instance.Label}: lot values from {playerId} ignored, the generator is {instance.GeneratorId}.");
				return false;
			}
			int applied = 0;
			foreach (var value in values)
			{
				var lot = instance.Lots.FirstOrDefault(l => l.Index == value.Index);
				if (lot == null || lot.ValuesKnown || lot.CarId != value.CarId) continue;
				lot.Rating = value.Rating;
				lot.Value = value.Value;
				lot.StartingPrice = value.StartingPrice;
				lot.ValuesKnown = true;
				applied++;
			}
			if (applied > 0) Logger.Info($"[Outdoor] {instance.Label}: values of {applied} lots from {playerId}.");
			return applied > 0;
		}

		public static void Claim(int playerId, AuctionLotClaimPacket packet)
		{
			var instance = OutdoorInstances.ForMember(playerId, packet.InstanceId);
			AuctionLotState state = null;
			if (instance == null || !instance.LotStates.TryGetValue(packet.Lot, out state))
			{
				OutdoorNet.Send(playerId, new AuctionLotClaimPacket { InstanceId = packet.InstanceId, Lot = packet.Lot, Granted = false });
				return;
			}

			bool granted = state.Status == AuctionLotStatus.Open || (state.Status == AuctionLotStatus.Bidding && state.OwnerId == playerId);
			if (granted && state.Status == AuctionLotStatus.Open)
			{
				state.Status = AuctionLotStatus.Bidding;
				state.OwnerId = playerId;
				state.LastBid = null;
				Logger.Info($"[Outdoor] {instance.Label}: lot {packet.Lot} bidding run by {playerId}.");
			}
			else if (!granted)
			{
				Logger.Info($"[Outdoor] {instance.Label}: claim of lot {packet.Lot} by {playerId} refused, {state.Status} by {state.OwnerId}.");
			}

			var answer = new AuctionLotClaimPacket
			{
				InstanceId = instance.InstanceId, Lot = packet.Lot, Granted = granted, OwnerId = state.OwnerId, OwnerName = OutdoorNet.NameOf(state.OwnerId),
			};
			OutdoorNet.Send(playerId, answer);
			if (granted) OutdoorNet.SendTo(instance.Members, answer, except: playerId);
		}

		public static void BidState(int playerId, AuctionBidStatePacket packet)
		{
			var instance = OutdoorInstances.ForMember(playerId, packet.InstanceId);
			var snapshot = packet.State;
			if (instance == null || snapshot == null || !instance.LotStates.TryGetValue(snapshot.Lot, out var state)
			    || state.Status != AuctionLotStatus.Bidding || state.OwnerId != playerId)
			{
				Logger.Debug($"[Outdoor] Bid state from {playerId} for lot {snapshot?.Lot} ignored.");
				return;
			}

			snapshot.OwnerId = playerId;
			state.LastBid = snapshot;
			OutdoorNet.SendTo(instance.Members, new AuctionBidStatePacket { InstanceId = instance.InstanceId, State = snapshot }, except: playerId);
			if (snapshot.Phase == AuctionBidPhase.AiWon) Close(instance, snapshot.Lot, AuctionLotStatus.Lost, playerId, "another bidder won");
		}

		public static void BidRequest(int playerId, AuctionBidRequestPacket packet)
		{
			var instance = OutdoorInstances.ForMember(playerId, packet.InstanceId);
			if (instance == null || !instance.LotStates.TryGetValue(packet.Lot, out var state) || state.Status != AuctionLotStatus.Bidding)
			{
				Logger.Info($"[Outdoor] Raise on lot {packet.Lot} by {playerId} refused: nobody is bidding on it.");
				return;
			}
			var last = state.LastBid;
			if (last != null && (last.Phase != AuctionBidPhase.Bidding || last.TeamLeads))
			{
				Logger.Info($"[Outdoor] {instance.Label}: raise on lot {packet.Lot} by {playerId} not forwarded ({(last.TeamLeads ? "the team already leads" : last.Phase.ToString())}).");
				OutdoorNet.Send(playerId, new AuctionBidStatePacket { InstanceId = instance.InstanceId, State = last });
				return;
			}
			int next = last == null ? 0 : last.CurrentBid + last.BidStep;
			if (next > OutdoorNet.Money())
			{
				Logger.Info($"[Outdoor] {instance.Label}: raise on lot {packet.Lot} by {playerId} refused, {next} is more than the shared money {OutdoorNet.Money()}.");
				return;
			}
			Logger.Info($"[Outdoor] {instance.Label}: raise on lot {packet.Lot} by {playerId} forwarded to {state.OwnerId}.");
			OutdoorNet.Send(state.OwnerId, new AuctionBidRequestPacket { InstanceId = instance.InstanceId, Lot = packet.Lot, RequesterId = playerId, SeenBid = packet.SeenBid });
		}

		public static ParkRefusal CheckPurchase(OutdoorInstance instance, int playerId, int lot)
		{
			if (!instance.LotStates.TryGetValue(lot, out var state) || state.Status != AuctionLotStatus.Bidding || state.OwnerId != playerId)
			{
				Logger.Info($"[Outdoor] {instance.Label}: purchase of lot {lot} by {playerId} refused ({state?.Status.ToString() ?? "unknown lot"}{(state != null ? $" by {state.OwnerId}" : "")}).");
				return ParkRefusal.Taken;
			}
			return ParkRefusal.None;
		}

		public static void ReleaseOwner(OutdoorInstance instance, int playerId, string why)
		{
			foreach (var state in instance.LotStates.Values.Where(s => s.Status == AuctionLotStatus.Bidding && s.OwnerId == playerId).ToList())
				Close(instance, state.Lot, AuctionLotStatus.Lost, playerId, why);
		}

		public static void Close(OutdoorInstance instance, int lot, AuctionLotStatus status, int by, string why)
		{
			if (!instance.LotStates.TryGetValue(lot, out var state) || state.Status == AuctionLotStatus.Won || state.Status == AuctionLotStatus.Lost) return;
			state.Status = status;
			Logger.Info($"[Outdoor] {instance.Label}: lot {lot} {status} ({why}, {by}).");
			OutdoorNet.SendTo(instance.Members, new AuctionLotClosedPacket { InstanceId = instance.InstanceId, Lot = lot, Status = status, By = by });
		}
	}
}
