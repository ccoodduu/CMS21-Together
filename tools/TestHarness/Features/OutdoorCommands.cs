using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS.Containers;
using CMS.SceneLoaders;
using CMS.UI;
using CMS.UI.Logic.Auction;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.Outdoor;
using CMS21Together.Logic.Outdoor;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TogetherTestHarness.Features;

[HarmonyPatch]
public static class OutdoorCommands
{
	private const int TraceLimit = 4000;
	private const float LoadWaitSeconds = 60f;

	private static readonly List<string> trace = new List<string>();
	private static readonly List<string> lastAuctionStart = new List<string>();
	private static bool tracing;

	public static Dictionary<string, object> Dump()
	{
		var instance = OutdoorSession.Instance;
		return new Dictionary<string, object>
		{
			["visit"] = OutdoorSession.Visit.ToString(),
			["scene"] = OutdoorSession.Scene.ToString(),
			["shared"] = OutdoorSession.IsShared,
			["instanceId"] = OutdoorSession.InstanceId,
			["generator"] = OutdoorSession.IsGenerator,
			["seed"] = OutdoorSession.Seed,
			["applied"] = OutdoorSession.Applied,
			["holdSeconds"] = Math.Round(GeneratorHooks.LastHoldSeconds, 2),
			["reseedSteps"] = Reseed.Steps,
			["picks"] = instance?.Picks.Select(p => p.ToString()).ToList(),
			["cars"] = OutdoorCarSync.Describe(),
			["piles"] = LootSync.SharedPiles || OutdoorSession.Visit != OutdoorVisit.None ? PileSummary() : null,
			["loot"] = LootSync.States(),
			["lots"] = instance?.Lots.Where(l => instance.LotStates.Any(s => s.Lot == l.Index) && !AuctionSync.IsClosed(l.Index))
				.Select(l => (object)new { l.Index, kind = l.Kind.ToString(), l.CarId, l.Version, l.Seed, l.StartingPrice, l.ValuesKnown }).ToList(),
			["auction"] = AuctionSync.Describe(),
			["digest"] = OutdoorDigest.LastRows,
		};
	}

	private static List<object> PileSummary() =>
		LootSync.Scan().Select(p => (object)new { p.Index, p.Key, uids = Items(p.Junk).Select(i => i.UID).OrderBy(u => u).ToList() }).ToList();

	private static List<Item> Items(Junk junk)
	{
		var items = new List<Item>();
		for (int i = 0; junk.ItemsInTrash != null && i < junk.ItemsInTrash.Count; i++)
		{
			var item = junk.ItemsInTrash[i]?.TryCast<Item>();
			if (item != null) items.Add(item);
		}
		return items;
	}

	[HarnessCommand("outdoor-state")]
	private static object State(string args) => Dump();

	[HarnessCommand("outdoor-local")]
	private static object Local(string args)
	{
		OutdoorSession.ForceNextLocal();
		return "the next outdoor visit is local";
	}

	[HarnessCommand("outdoor-catalog")]
	private static object Catalog(string args)
	{
		var packet = CatalogReporter.Build();
		return new
		{
			scene = SceneState.Current,
			sizes = packet.Scenes.ToDictionary(s => s.Key.ToString(), s => s.Value.Count),
			models = packet.Scenes.ToDictionary(s => s.Key.ToString(), s => s.Value.Select(e => e.CarId).Distinct().Count()),
			dlc = packet.Scenes.ToDictionary(s => s.Key.ToString(), s => s.Value.Where(e => e.Dlc >= 0).Select(e => $"{e.CarId}/{e.ConfigVersion} dlc {e.Dlc}").ToList()),
		};
	}

	[HarnessCommand("outdoor-trace")]
	private static object Trace(string args)
	{
		switch ((args ?? "").Trim().ToLowerInvariant())
		{
			case "on":
				trace.Clear();
				tracing = true;
				return "outdoor trace on";
			case "off":
				tracing = false;
				return $"outdoor trace off ({trace.Count} lines)";
			case "report":
			case "":
				return new List<string>(trace);
			default:
				throw new ArgumentException("usage: outdoor-trace on|off|report");
		}
	}

	private static void Note(string text)
	{
		if (!tracing || trace.Count >= TraceLimit) return;
		string line = $"{Time.realtimeSinceStartup:0.000} f{Time.frameCount} {text}";
		trace.Add(line);
		MelonLogger.Msg($"[Harness] outdoor-trace {line}");
	}

	private static string RandomState() => Reseed.Current.ToString();

	[HarmonyPatch(typeof(JunkyardGenerator._Generate_d__18), nameof(JunkyardGenerator._Generate_d__18.MoveNext))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	private static void TraceJunkyardStep(JunkyardGenerator._Generate_d__18 __instance, out int __state)
	{
		__state = __instance.__1__state;
		Note($"junkyard d__18 enter state {__state} i {__instance._i_5__11} random {RandomState()}");
	}

	[HarmonyPatch(typeof(JunkyardGenerator._Generate_d__18), nameof(JunkyardGenerator._Generate_d__18.MoveNext))]
	[HarmonyPostfix]
	private static void TraceJunkyardStepDone(JunkyardGenerator._Generate_d__18 __instance, int __state, bool __result) =>
		Note($"junkyard d__18 exit {__state} -> {__instance.__1__state} result {__result} random {RandomState()}");

	[HarmonyPatch(typeof(JunkyardGenerator._CreateCar_d__19), nameof(JunkyardGenerator._CreateCar_d__19.MoveNext))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	private static void TraceJunkyardCar(JunkyardGenerator._CreateCar_d__19 __instance) =>
		Note($"junkyard d__19 car {__instance.index} state {__instance.__1__state} random {RandomState()}");

	[HarmonyPatch(typeof(ShedManager._Generate_d__29), nameof(ShedManager._Generate_d__29.MoveNext))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	private static void TraceBarnStep(ShedManager._Generate_d__29 __instance, out int __state)
	{
		__state = __instance.__1__state;
		Note($"barn d__29 enter state {__state} i {__instance._i_5__12} random {RandomState()}");
	}

	[HarmonyPatch(typeof(ShedManager._Generate_d__29), nameof(ShedManager._Generate_d__29.MoveNext))]
	[HarmonyPostfix]
	private static void TraceBarnStepDone(ShedManager._Generate_d__29 __instance, int __state, bool __result) =>
		Note($"barn d__29 exit {__state} -> {__instance.__1__state} result {__result} random {RandomState()}");

	[HarmonyPatch(typeof(ShedManager._CreateCar_d__30), nameof(ShedManager._CreateCar_d__30.MoveNext))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	private static void TraceBarnCar(ShedManager._CreateCar_d__30 __instance) =>
		Note($"barn d__30 car {__instance.index} state {__instance.__1__state} random {RandomState()}");

	[HarmonyPatch(typeof(JunkyardGenerator), nameof(JunkyardGenerator.CreateCar))]
	[HarmonyPostfix]
	private static void TraceJunkyardCreateCar(int index, CarsIdWithConfig randomCar) =>
		Note($"JunkyardGenerator.CreateCar {index} {randomCar?.CarID}/{randomCar?.ConfigVersion}");

	[HarmonyPatch(typeof(ShedManager), nameof(ShedManager.CreateCar))]
	[HarmonyPostfix]
	private static void TraceBarnCreateCar(int index, CarsIdWithConfig carIdWithConfig) =>
		Note($"ShedManager.CreateCar {index} {carIdWithConfig?.CarID}/{carIdWithConfig?.ConfigVersion}");

	[HarmonyPatch(typeof(AuctionManager), nameof(AuctionManager.GenerateCars))]
	[HarmonyPostfix]
	private static void TraceAuctionCars(AuctionType auctionType, Il2CppSystem.Collections.Generic.List<AuctionCarData> __result) =>
		Note($"AuctionManager.GenerateCars {auctionType}: {__result?.Count ?? 0} lots");

	[HarmonyPatch(typeof(AuctionManager), nameof(AuctionManager.LoadCar))]
	[HarmonyPrefix]
	private static void TraceAuctionLoad(AuctionCarData auctionCarData, AuctionType auctionType) =>
		Note($"AuctionManager.LoadCar {auctionType} {auctionCarData?.Car}/{auctionCarData?.Version} seed {auctionCarData?.Seed} random {RandomState()}");

	[HarmonyPatch(typeof(AuctionBidding), nameof(AuctionBidding.OnStateChange))]
	[HarmonyPostfix]
	private static void TraceBidding(AuctionBidding __instance, CMS.UI.Logic.AuctionState newState) =>
		Note($"AuctionBidding.OnStateChange {newState}: bid {__instance.currentBid} step {__instance.bidAmount} timer {__instance.auctionTimer:0.0} team leads {__instance.lastBidByPlayer}");

	[HarnessCommand("piles")]
	private static object Piles(string args) => new { piles = LootSync.Describe(), loot = LootSync.States() };

	[HarnessCommand("loot-take")]
	private static object LootTake(string args)
	{
		var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2) throw new ArgumentException("usage: loot-take <pileKey|pileIndex> <itemIndex|uid:N>");
		int pileIndex = int.TryParse(parts[0], out int index) ? index : -1;
		string pileKey = pileIndex >= 0 ? null : parts[0];
		long uid = parts[1].StartsWith("uid:") ? long.Parse(parts[1].Substring(4)) : 0;
		int itemIndex = uid == 0 ? int.Parse(parts[1]) : -1;
		var item = LootSync.HarnessTake(pileIndex, pileKey, uid, itemIndex);
		return new { item.ID, item.UID, held = LootSync.Held.Contains(item.UID), tempItems = Singleton<GameManager>.Instance.TempInventory.GetItemsCount() };
	}

	[HarnessCommand("loot-put")]
	private static object LootPut(string args)
	{
		long uid = long.Parse((args ?? "").Trim());
		var manager = Singleton<GameManager>.Instance;
		var items = manager.TempInventory.GetListOfItems();
		BaseItem found = null;
		for (int i = 0; items != null && i < items.Count; i++)
			if (items[i] != null && items[i].UID == uid) found = items[i];
		if (found == null) throw new InvalidOperationException($"item {uid} is not among the taken items");
		manager.TempInventory.RemoveItem(found);
		var inInventory = manager.Inventory.GetItem(uid);
		if (inInventory != null) manager.Inventory.Delete(inInventory);
		return new { uid, held = LootSync.Held.Contains(uid), tempItems = manager.TempInventory.GetItemsCount() };
	}

	[HarnessCommand("loot-quit")]
	private static object LootQuit(string args)
	{
		var windows = WindowManager.Instance ?? throw new InvalidOperationException("no WindowManager");
		if (!windows.IsWindowActive(WindowID.TakenItems)) windows.Show(WindowID.TakenItems, false);
		var window = windows.GetWindowByID<TakenItemsWindow>(WindowID.TakenItems) ?? throw new InvalidOperationException("no TakenItems window");
		int count = Singleton<GameManager>.Instance.TempInventory.GetItemsCount();
		window.QuitWithoutPartsAction();
		return new { leftUnpaid = count };
	}

	private static AuctionManager Manager() =>
		UnityEngine.Object.FindObjectOfType<AuctionManager>() ?? throw new InvalidOperationException("no AuctionManager: not in the auction");

	private static AuctionType ParseType(string text) =>
		(text ?? "").Trim().ToLowerInvariant() switch
		{
			"normal" => AuctionType.Normal,
			"salvage" => AuctionType.Salvage,
			_ => throw new ArgumentException("auction type: normal|salvage"),
		};

	[HarnessCommand("auction-open")]
	private static object AuctionOpen(string args)
	{
		var type = ParseType(args);
		var manager = Manager();
		string uiError = null;
		try
		{
			if (type == AuctionType.Normal) manager.OpenNormalAuctions();
			else manager.OpenSalvageAuctions();
		}
		catch (Exception ex)
		{
			uiError = ex.Message.Split(new[] { '\n' }, 2)[0];
		}
		var list = type == AuctionType.Normal ? manager.normalCars : manager.salvageCars;
		var lots = new List<object>();
		for (int i = 0; list != null && i < list.Count; i++)
			lots.Add(new { position = i, lot = AuctionSync.LotOf(list[i]), list[i].Car, list[i].Version, list[i].Seed, list[i].Rating, list[i].Value, list[i].StartingPrice, list[i].Sold });
		return new { type = type.ToString(), uiError, lots };
	}

	[HarnessCommand("auction-start")]
	private static object AuctionStart(string args)
	{
		int lot = int.Parse((args ?? "").Trim());
		var manager = Manager();
		AuctionCarData data = null;
		var type = AuctionType.Normal;
		foreach (var candidate in new[] { (AuctionType.Normal, manager.normalCars), (AuctionType.Salvage, manager.salvageCars) })
		{
			for (int i = 0; candidate.Item2 != null && i < candidate.Item2.Count && data == null; i++)
			{
				if (AuctionSync.LotOf(candidate.Item2[i]) != lot) continue;
				data = candidate.Item2[i];
				type = candidate.Item1;
			}
		}
		if (data == null) throw new InvalidOperationException($"lot {lot} is not in the opened auction lists (auction-open first)");
		lastAuctionStart.Clear();
		MelonCoroutines.Start(StartBidding(manager, data, type));
		return new { lot, data.Car, type = type.ToString(), path = "AuctionManager.LoadCar, AuctionBidding.Open, StartAuction" };
	}

	[HarnessCommand("auction-start-last")]
	private static object AuctionStartLast(string args) => new List<string>(lastAuctionStart);

	private static void StartStep(string text)
	{
		lastAuctionStart.Add(text);
		MelonLogger.Msg($"[Harness] auction-start: {text}");
	}

	private static IEnumerator StartBidding(AuctionManager manager, AuctionCarData data, AuctionType type)
	{
		manager.LoadCar(data, type);
		float deadline = Time.realtimeSinceStartup + LoadWaitSeconds;
		while (!manager.IsCarLoaded)
		{
			if (Time.realtimeSinceStartup > deadline)
			{
				StartStep("failed: the lot's car did not load");
				yield break;
			}
			yield return null;
		}
		StartStep($"car loaded: {manager.CarLoader?.carToLoad}");
		var bidding = AuctionSync.FindBidding();
		if (bidding == null)
		{
			StartStep("failed: no AuctionBidding");
			yield break;
		}
		bidding.Open(type);
		yield return null;
		StartStep($"bidding window open, state {bidding.State}");
		bidding.StartAuction();
		yield return null;
		StartStep($"StartAuction called: started {bidding.startedAuction}, bid {bidding.currentBid}");
	}

	[HarnessCommand("auction-raise")]
	private static object AuctionRaise(string args)
	{
		int lot = int.Parse((args ?? "").Trim());
		if (!AuctionSync.Raise(lot)) throw new InvalidOperationException($"lot {lot} is not being bid on by another player");
		return $"raise on lot {lot} sent";
	}

	[HarnessCommand("auction-state")]
	private static object AuctionStateCommand(string args)
	{
		var bidding = AuctionSync.FindBidding();
		int lot = AuctionSync.CurrentLot(bidding);
		return new
		{
			sync = AuctionSync.Describe(),
			local = bidding == null ? null : new { lot, started = bidding.startedAuction, snapshot = AuctionSync.Snapshot(bidding, lot), state = bidding.State.ToString() },
		};
	}

	[HarnessCommand("auction-time")]
	private static object AuctionTime(string args)
	{
		float scale = float.Parse((args ?? "").Trim(), System.Globalization.CultureInfo.InvariantCulture);
		Time.timeScale = Mathf.Clamp(scale, 0.1f, 20f);
		return $"time scale {Time.timeScale}";
	}

	[HarnessCommand("auction-finish")]
	private static object AuctionFinish(string args)
	{
		string who = (args ?? "").Trim().ToLowerInvariant();
		if (who != "team" && who != "ai") throw new ArgumentException("usage: auction-finish team|ai");
		var bidding = new[] { AuctionSync.FindBidding() }.FirstOrDefault(b => b != null && b.startedAuction)
		              ?? throw new InvalidOperationException("no bidding runs here");
		bidding.lastBidByPlayer = who == "team";
		bidding.AIMaxBid = bidding.currentBid;
		bidding.auctionTimer = 0f;
		if (!bidding.isActiveAndEnabled) bidding.Update();
		lastAuctionStart.Clear();
		if (who == "team") MelonCoroutines.Start(ReceiveWonCar(bidding));
		return new { who, bid = bidding.currentBid, lot = AuctionSync.CurrentLot(bidding) };
	}

	private static IEnumerator ReceiveWonCar(AuctionBidding bidding)
	{
		float deadline = Time.realtimeSinceStartup + 10f;
		while (bidding != null && bidding.State != CMS.UI.Logic.AuctionState.Win)
		{
			if (Time.realtimeSinceStartup > deadline)
			{
				StartStep("failed: the bidding did not end as a win");
				yield break;
			}
			yield return null;
		}
		bidding.ReceiveCarAction();
		StartStep("ReceiveCarAction called");
		var windows = WindowManager.Instance;
		deadline = Time.realtimeSinceStartup + 5f;
		while (!windows.IsWindowActive(WindowID.CarLocationWindow))
		{
			if (Time.realtimeSinceStartup > deadline)
			{
				StartStep("failed: the location window did not open");
				yield break;
			}
			yield return null;
		}
		float until = Time.realtimeSinceStartup + 0.5f;
		while (Time.realtimeSinceStartup < until) yield return null;
		var buttons = windows.GetWindowByID<CarLocationWindow>(WindowID.CarLocationWindow)?.buttons;
		if (buttons == null || buttons.Length < 2 || buttons[1] == null)
		{
			StartStep("failed: no parking button");
			yield break;
		}
		buttons[1].OnClick.Invoke();
		StartStep("pressed parking");
	}
}
