using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using CMS21Together.Logic.Car.Away;

namespace CMS21Together.Logic.Car.Parts;

[HarmonyPatch]
public static class PartClaims
{
	private static readonly Dictionary<int, Dictionary<string, int>> owners = new Dictionary<int, Dictionary<string, int>>();

	public static string LastBlocked { get; private set; }

	public static void Reset()
	{
		owners.Clear();
		LastBlocked = null;
	}

	public static IReadOnlyDictionary<string, int> Held(int loader) =>
		owners.TryGetValue(loader, out var keys) ? keys : new Dictionary<string, int>();

	public static int OwnerOf(int loader, string key) =>
		owners.TryGetValue(loader, out var keys) && keys.TryGetValue(key, out int owner) ? owner : CarPartClaimUpdatePacket.Released;

	public static void OnUpdate(CarPartClaimUpdatePacket packet)
	{
		if (!owners.TryGetValue(packet.CarLoaderID, out var keys))
		{
			keys = new Dictionary<string, int>();
			owners[packet.CarLoaderID] = keys;
		}
		foreach (string key in packet.Keys)
		{
			if (packet.OwnerPlayerId == CarPartClaimUpdatePacket.Released) keys.Remove(key);
			else keys[key] = packet.OwnerPlayerId;
		}
	}

	public static void Claim(int loader, IEnumerable<string> keys, bool release = false)
	{
		var list = keys.ToList();
		if (list.Count == 0) return;
		Client.Instance.Send(new CarPartClaimPacket { CarLoaderID = loader, SpawnSeq = CarPartsSync.SpawnSeq(loader), Keys = list, Release = release });
	}

	public static bool HeldByOther(int loader, IEnumerable<string> keys, out int owner)
	{
		owner = CarPartClaimUpdatePacket.Released;
		foreach (string key in keys)
		{
			int holder = OwnerOf(loader, key);
			if (holder != CarPartClaimUpdatePacket.Released && holder != Client.Instance.ID)
			{
				owner = holder;
				return true;
			}
		}
		return false;
	}

	private static bool TryResolve(PartScript script, out int loader, out List<string> keys)
	{
		loader = -1;
		keys = null;
		foreach (var sync in CarPartsSync.All)
		{
			if (sync.Registry == null || !sync.Registry.TryGetSubPath(script, out var path)) continue;
			loader = sync.Loader;
			keys = new List<string> { PartKeys.Sub(path) };
			foreach (var member in script.GetUnmountWith())
				if (sync.Registry.TryGetSubPath(member, out var memberPath)) keys.Add(PartKeys.Sub(memberPath));
			return true;
		}
		return false;
	}

	private static bool TryResolve(CarLoader carLoader, string partName, out int loader, out List<string> keys)
	{
		int id = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		loader = id;
		keys = null;
		var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == id);
		var part = carLoader.GetCarPart(partName);
		if (sync?.Registry == null || part == null || !sync.Registry.TryGetBodyIndex(part, out int index)) return false;
		keys = new List<string> { PartKeys.Body(index) };
		return true;
	}

	private static bool AllowAction(int loader, List<string> keys)
	{
		if (!CarPartsSync.IsReady(loader))
		{
			Block("This car is still loading for multiplayer.", keys);
			return false;
		}
		if (CarAwaySync.BlockIfLocked(loader, "part edit"))
		{
			LastBlocked = keys.FirstOrDefault();
			return false;
		}
		if (HeldByOther(loader, keys, out int owner))
		{
			string name = PresenceManager.Roster.TryGetValue(owner, out var player) ? player.Record.Username : $"Player {owner}";
			Block($"{name} is working on this part.", keys);
			return false;
		}
		Claim(loader, keys);
		return true;
	}

	private static void Block(string message, List<string> keys)
	{
		LastBlocked = keys.FirstOrDefault();
		ModNotify.ShowToast(message);
	}

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionUnMount))]
	[HarmonyPrefix]
	private static bool BeforeActionUnMount(PartScript __instance) =>
		!Active || !TryResolve(__instance, out int loader, out var keys) || AllowAction(loader, keys);

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionMount))]
	[HarmonyPrefix]
	private static bool BeforeActionMount(PartScript __instance) =>
		!Active || !TryResolve(__instance, out int loader, out var keys) || AllowAction(loader, keys);

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), typeof(string))]
	[HarmonyPrefix]
	private static bool BeforeTakeOffCarPart(CarLoader __instance, string partName)
	{
		if (!Active || !TryResolve(__instance, partName, out int loader, out var keys)) return true;
		if (!AllowAction(loader, keys)) return false;
		var part = __instance.GetCarPart(partName);
		var sync = CarPartsSync.Get(loader);
		if (part != null && sync.Registry != null && sync.Registry.TryGetBodyIndex(part, out int index)) PartTransactions.OpenForBody(loader, index, part);
		return true;
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.CanTakeOffCarPart))]
	[HarmonyPostfix]
	private static void AfterCanTakeOffCarPart(CarLoader __instance, string name, ref bool __result)
	{
		if (!__result || !Active || !TryResolve(__instance, name, out int loader, out var keys)) return;
		if (!CarPartsSync.IsReady(loader) || HeldByOther(loader, keys, out _) || CarAwaySync.LockedForMe(loader, out _, out _)) __result = false;
	}

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.UndoMounting))]
	[HarmonyPostfix]
	private static void AfterUndoMounting(PartScript __instance) => ReleaseFor(__instance);

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.UndoUnMounting))]
	[HarmonyPostfix]
	private static void AfterUndoUnMounting(PartScript __instance) => ReleaseFor(__instance);

	private static void ReleaseFor(PartScript script)
	{
		if (Active && TryResolve(script, out int loader, out var keys)) Claim(loader, keys, release: true);
	}
}
