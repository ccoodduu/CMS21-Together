using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS.Containers;
using CMS.Extensions;
using CMS.Helpers;
using CMS.SceneLoaders;
using CMS.UI.Logic;
using CMS.UI.Logic.Auction;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.Outdoor;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

[HarmonyPatch]
public static class AuctionSync
{
	private const float StateIntervalSeconds = 1f;

	private static readonly HashSet<int> owned = new HashSet<int>();
	private static readonly HashSet<int> closed = new HashSet<int>();
	private static readonly Dictionary<int, AuctionBidSnapshot> watched = new Dictionary<int, AuctionBidSnapshot>();
	private static readonly HashSet<AuctionKind> localKinds = new HashSet<AuctionKind>();
	private static int pendingClaim = -1;
	private static AuctionBidding pendingBidding;
	private static int tickerToken;
	private static GUIStyle style;

	private static bool Active => OutdoorSession.IsShared && OutdoorSession.Scene == GameScene.Auction;

	public static IReadOnlyDictionary<int, AuctionBidSnapshot> Watched => watched;

	public static bool IsClosed(int lot) => closed.Contains(lot);

	public static void Reset()
	{
		owned.Clear();
		closed.Clear();
		watched.Clear();
		localKinds.Clear();
		pendingClaim = -1;
		pendingBidding = null;
		tickerToken++;
	}

	public static void OnArrived()
	{
		if (!Active || !OutdoorSession.Instance.LotsPending) return;
		var manager = UnityEngine.Object.FindObjectOfType<AuctionManager>();
		if (manager == null)
		{
			Log.Warn("[Outdoor] No AuctionManager in the auction scene; the lots cannot be shared.");
			return;
		}
		var amounts = new List<AuctionAmountRange>
		{
			new AuctionAmountRange { Kind = AuctionKind.Normal, Min = manager.normalCarsAmountRange.x, Max = manager.normalCarsAmountRange.y },
			new AuctionAmountRange { Kind = AuctionKind.Salvage, Min = manager.salvageCarsAmountRange.x, Max = manager.salvageCarsAmountRange.y },
		};
		Client.Instance.Send(new OutdoorDigestPacket { InstanceId = OutdoorSession.InstanceId, AuctionAmounts = amounts });
		Log.Info($"[Outdoor] Auction amounts sent: normal {amounts[0].Min}-{amounts[0].Max}, salvage {amounts[1].Min}-{amounts[1].Max}.");
	}

	public static void OnInstance(OutdoorInstancePacket packet)
	{
		if (!Active) return;
		foreach (var state in packet.LotStates.Where(s => s.Status == AuctionLotStatus.Bidding && s.OwnerId != Client.Instance.ID))
			watched[state.Lot] = state.LastBid ?? new AuctionBidSnapshot { Lot = state.Lot, OwnerId = state.OwnerId };
		if (packet.LotsPending) return;
		var manager = UnityEngine.Object.FindObjectOfType<AuctionManager>();
		if (manager == null) return;
		if (localKinds.Remove(AuctionKind.Normal)) manager.normalCarsGenerated = false;
		if (localKinds.Remove(AuctionKind.Salvage)) manager.salvageCarsGenerated = false;
	}

	public static int LotOf(AuctionCarData data)
	{
		if (!Active || data == null) return -1;
		var lot = OutdoorSession.Instance.Lots.FirstOrDefault(l => l.Seed == data.Seed && l.CarId == data.Car);
		return lot?.Index ?? -1;
	}

	public static int CurrentLot(AuctionBidding bidding) => bidding == null ? -1 : LotOf(bidding.auctionManager?.CurrentCarData);

	[HarmonyPatch(typeof(AuctionManager), nameof(AuctionManager.GenerateCars))]
	[HarmonyPrefix]
	private static bool BeforeGenerateCars(AuctionManager __instance, AuctionType auctionType, ref Il2CppSystem.Collections.Generic.List<AuctionCarData> __result)
	{
		if (!Active) return true;
		var kind = (AuctionKind)(int)auctionType;
		var instance = OutdoorSession.Instance;
		if (instance.LotsPending)
		{
			localKinds.Add(kind);
			Log.Warn($"[Outdoor] {kind} lots are not decided yet; this list is local until the server sends them.");
			return true;
		}

		var list = new Il2CppSystem.Collections.Generic.List<AuctionCarData>();
		var computed = new List<AuctionLot>();
		foreach (var lot in instance.Lots.Where(l => l.Kind == kind && !closed.Contains(l.Index)))
		{
			var data = new AuctionCarData { Car = lot.CarId, Version = lot.Version, Seed = lot.Seed };
			if (lot.ValuesKnown)
			{
				data.Rating = lot.Rating;
				data.Value = lot.Value;
				data.StartingPrice = lot.StartingPrice;
			}
			else
			{
				ComputeValues(__instance, auctionType, data);
				computed.Add(new AuctionLot { Index = lot.Index, Kind = kind, CarId = lot.CarId, Version = lot.Version, Seed = lot.Seed, Rating = data.Rating, Value = data.Value, StartingPrice = data.StartingPrice, ValuesKnown = true });
			}
			list.Add(data);
		}
		if (computed.Count > 0 && OutdoorSession.IsGenerator)
			Client.Instance.Send(new OutdoorDigestPacket { InstanceId = OutdoorSession.InstanceId, LotValues = computed });
		Log.Info($"[Outdoor] {kind} lots from the server: {list.Count}{(computed.Count > 0 ? $", {computed.Count} valued here" : "")}.");
		__result = list;
		return false;
	}

	private static void ComputeValues(AuctionManager manager, AuctionType auctionType, AuctionCarData data)
	{
		var saved = UnityEngine.Random.state;
		try
		{
			UnityEngine.Random.InitState(data.Seed);
			data.Rating = AuctionHelper.GetRatingForCar(auctionType);
			data.Value = CarBundleLoaderExtension.GetCarValue(Singleton<GameManager>.Instance.CarBundleLoader, data.Car, data.Version);
			var range = auctionType == AuctionType.Normal ? manager.rangeNormalStartPriceMod : manager.rangeSalvageStartPriceMod;
			data.StartingPrice = AuctionHelper.GetStartingPrice(data.Value, range, data.Rating);
		}
		finally
		{
			UnityEngine.Random.state = saved;
		}
	}

	[HarmonyPatch(typeof(AuctionBidding), nameof(AuctionBidding.StartAuction))]
	[HarmonyPrefix]
	private static bool BeforeStartAuction(AuctionBidding __instance)
	{
		if (!Active) return true;
		int lot = CurrentLot(__instance);
		if (lot < 0 || owned.Contains(lot)) return true;
		if (pendingClaim == lot) return false;
		pendingClaim = lot;
		pendingBidding = __instance;
		Client.Instance.Send(new AuctionLotClaimPacket { InstanceId = OutdoorSession.InstanceId, Lot = lot });
		Log.Info($"[Outdoor] Claim of lot {lot} sent.");
		return false;
	}

	public static void OnClaim(AuctionLotClaimPacket packet)
	{
		if (!Active || packet.InstanceId != OutdoorSession.InstanceId) return;
		bool mine = packet.OwnerId == Client.Instance.ID;
		if (packet.Granted && mine)
		{
			owned.Add(packet.Lot);
			watched.Remove(packet.Lot);
			if (pendingClaim != packet.Lot) return;
			pendingClaim = -1;
			var bidding = pendingBidding;
			pendingBidding = null;
			Log.Info($"[Outdoor] Lot {packet.Lot}: bidding granted.");
			if (bidding != null) bidding.StartAuction();
			MelonCoroutines.Start(Ticker(++tickerToken, bidding, packet.Lot));
			return;
		}
		if (packet.Granted)
		{
			watched[packet.Lot] = new AuctionBidSnapshot { Lot = packet.Lot, OwnerId = packet.OwnerId };
			ModNotify.ShowToast($"{packet.OwnerName} is bidding on lot {packet.Lot + 1}.");
			return;
		}
		if (pendingClaim == packet.Lot)
		{
			pendingClaim = -1;
			pendingBidding = null;
			Log.Info($"[Outdoor] Lot {packet.Lot}: claim refused, {packet.OwnerName} is bidding.");
			ModNotify.ShowMessage("Already bidding", packet.OwnerId > 0 ? $"{packet.OwnerName} is bidding on this car." : "This car is no longer offered.");
		}
	}

	private static IEnumerator Ticker(int token, AuctionBidding bidding, int lot)
	{
		while (token == tickerToken && Active && bidding != null && owned.Contains(lot))
		{
			float next = Time.realtimeSinceStartup + StateIntervalSeconds;
			while (Time.realtimeSinceStartup < next) yield return null;
			if (token != tickerToken || bidding == null || !bidding.startedAuction) yield break;
			SendState(bidding, lot);
		}
	}

	[HarmonyPatch(typeof(AuctionBidding), nameof(AuctionBidding.OnStateChange))]
	[HarmonyPostfix]
	private static void AfterStateChange(AuctionBidding __instance)
	{
		if (!Active) return;
		int lot = CurrentLot(__instance);
		if (lot >= 0 && owned.Contains(lot)) SendState(__instance, lot);
	}

	public static AuctionBidSnapshot Snapshot(AuctionBidding bidding, int lot) => new AuctionBidSnapshot
	{
		Lot = lot,
		OwnerId = Client.Instance.ID,
		CurrentBid = bidding.currentBid,
		BidStep = bidding.bidAmount,
		TeamLeads = bidding.lastBidByPlayer,
		SecondsLeft = bidding.auctionTimer,
		Phase = bidding.State == AuctionState.Win ? AuctionBidPhase.TeamWon : bidding.State == AuctionState.Loss ? AuctionBidPhase.AiWon : AuctionBidPhase.Bidding,
	};

	private static void SendState(AuctionBidding bidding, int lot)
	{
		var snapshot = Snapshot(bidding, lot);
		Client.Instance.Send(new AuctionBidStatePacket { InstanceId = OutdoorSession.InstanceId, State = snapshot });
		if (snapshot.Phase == AuctionBidPhase.AiWon) owned.Remove(lot);
	}

	public static void OnBidState(AuctionBidStatePacket packet)
	{
		if (!Active || packet.InstanceId != OutdoorSession.InstanceId || packet.State == null) return;
		watched[packet.State.Lot] = packet.State;
	}

	public static void OnBidRequest(AuctionBidRequestPacket packet)
	{
		if (!Active || packet.InstanceId != OutdoorSession.InstanceId || !owned.Contains(packet.Lot)) return;
		var bidding = UnityEngine.Object.FindObjectOfType<AuctionBidding>();
		if (bidding == null || CurrentLot(bidding) != packet.Lot || !bidding.startedAuction || bidding.lastBidByPlayer)
		{
			Log.Info($"[Outdoor] Raise on lot {packet.Lot} from {packet.RequesterId} not applied (bidding {(bidding == null ? "closed" : bidding.startedAuction ? "team leads" : "not started")}).");
			return;
		}
		int before = bidding.currentBid;
		bidding.PlayerBid();
		Log.Info($"[Outdoor] Raise on lot {packet.Lot} from {packet.RequesterId}: {before} -> {bidding.currentBid}.");
		SendState(bidding, packet.Lot);
	}

	public static void OnLotClosed(AuctionLotClosedPacket packet)
	{
		if (!Active || packet.InstanceId != OutdoorSession.InstanceId) return;
		closed.Add(packet.Lot);
		owned.Remove(packet.Lot);
		watched.Remove(packet.Lot);
		var lot = OutdoorSession.Instance.Lots.FirstOrDefault(l => l.Index == packet.Lot);
		var manager = UnityEngine.Object.FindObjectOfType<AuctionManager>();
		if (lot != null && manager != null)
		{
			MarkSold(manager.normalCars, lot);
			MarkSold(manager.salvageCars, lot);
		}
		Log.Info($"[Outdoor] Lot {packet.Lot} closed: {packet.Status} ({packet.By}).");
	}

	private static void MarkSold(Il2CppSystem.Collections.Generic.List<AuctionCarData> cars, AuctionLot lot)
	{
		for (int i = 0; cars != null && i < cars.Count; i++)
			if (cars[i] != null && cars[i].Seed == lot.Seed && cars[i].Car == lot.CarId) cars[i].Sold = true;
	}

	public static bool Raise(int lot)
	{
		if (!Active || !watched.TryGetValue(lot, out var state)) return false;
		Client.Instance.Send(new AuctionBidRequestPacket { InstanceId = OutdoorSession.InstanceId, Lot = lot, SeenBid = state.CurrentBid });
		return true;
	}

	public static void Draw()
	{
		if (!Active || watched.Count == 0) return;
		if (style == null)
		{
			style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
			style.normal.textColor = new Color(1f, 0.85f, 0.4f);
		}
		float y = 80f;
		foreach (var state in watched.Values.OrderBy(s => s.Lot))
		{
			string owner = PresenceManager.Roster.TryGetValue(state.OwnerId, out var player) ? player.Record.Username : $"Player {state.OwnerId}";
			GUI.Label(new Rect(20f, y, 600f, 24f), $"{owner} is bidding on lot {state.Lot + 1}: {state.CurrentBid:N0} ({(state.TeamLeads ? "team leads" : "another bidder leads")}), {state.SecondsLeft:0} s", style);
			y += 26f;
		}
	}

	public static object Describe() => new
	{
		shared = Active,
		owned = owned.OrderBy(l => l).ToList(),
		closed = closed.OrderBy(l => l).ToList(),
		pendingClaim,
		localKinds = localKinds.Select(k => k.ToString()).ToList(),
		watched = watched.Values.OrderBy(s => s.Lot).Select(s => new { lot = s.Lot, owner = s.OwnerId, bid = s.CurrentBid, step = s.BidStep, teamLeads = s.TeamLeads, seconds = Math.Round(s.SecondsLeft, 1), phase = s.Phase.ToString() }).ToList(),
	};
}
