using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

// Engine crane out/in as one part transaction (docs/spikes/engine-crane.md). ActionUnMountGroup adds the engine group
// with an inlined groups.Add, so the AddGroup hook never sees it; ActionInsertEngineToCar empties the group's
// ItemList before it calls DeleteGroup, so the group is copied when the insert starts.
[HarmonyPatch]
public static class EngineCraneHooks
{
	private static int insertingLoader = -1;
	private static ModGroupItem insertingGroup;

	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ActionUnMountGroup))]
	[HarmonyPrefix]
	private static bool BeforeUnMountGroup(InteractiveObject iO)
	{
		if (!Active || iO == null || !TryFindEngineOwner(iO.gameObject, out var sync)) return true;
		var keys = EngineKeys(sync, iO.gameObject);
		if (!Allowed(sync.Loader, keys)) return false;
		PartTransactions.Open(sync.Loader, keys, new[] { iO.gameObject.name });
		return true;
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ActionUnMountGroup))]
	[HarmonyPostfix]
	private static void AfterUnMountGroup(InteractiveObject iO)
	{
		if (!Active || iO == null || !TryFindEngineOwner(iO.gameObject, out var sync)) return;
		var groups = Singleton<GameManager>.Instance.Inventory.GetGroups();
		GroupItem engineGroup = null;
		for (int i = groups.Count - 1; i >= 0 && engineGroup == null; i--)
			if (groups[i].ID == iO.gameObject.name) engineGroup = groups[i];
		if (engineGroup != null && !PartTransactions.CaptureAddGroup(engineGroup.ToModGroupItem()))
			Client.Instance.Send(new InventoryGroupItemActionPacket { Action = ItemActionType.Add, GroupItem = engineGroup.ToModGroupItem() });
		Log.Info($"[Parts] Loader {sync.Loader}: engine {iO.gameObject.name} taken out with the crane (group {engineGroup?.UID}).");
		PartChangeTracker.MarkDirty(sync.Loader);
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.InsertEngineToCar))]
	[HarmonyPrefix]
	private static bool BeforeInsertEngine(GroupItem engine)
	{
		insertingGroup = null;
		if (!Active || engine == null) return true;
		var carLoader = ToolsMoveManager.Get()?.GetConnectedCarLoader(IOSpecialType.EngineCrane);
		if (carLoader == null || carLoader.e_engine_h == null) return true;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == loader);
		if (sync?.Registry == null) return true;
		if (engine.ID != carLoader.e_engine_h.name)
		{
			ModNotify.ShowToast("Engine swaps are not supported in multiplayer yet.");
			return false;
		}
		var keys = EngineKeys(sync, carLoader.e_engine_h);
		if (!Allowed(loader, keys)) return false;
		PartTransactions.Open(loader, keys, new[] { engine.ID });
		insertingLoader = loader;
		insertingGroup = engine.ToModGroupItem();
		return true;
	}

	public static ModGroupItem TakeInsertedGroup(long uid)
	{
		if (insertingGroup == null || insertingGroup.UID != uid) return null;
		var group = insertingGroup;
		insertingGroup = null;
		Log.Info($"[Parts] Loader {insertingLoader}: engine {group.ID} put back with the crane (group {uid}).");
		PartChangeTracker.MarkDirty(insertingLoader);
		return group;
	}

	private static bool Allowed(int loader, List<string> keys)
	{
		if (!CarPartsSync.IsReady(loader))
		{
			ModNotify.ShowToast("This car is still loading for multiplayer.");
			return false;
		}
		if (PartClaims.HeldByOther(loader, keys, out _))
		{
			ModNotify.ShowToast("Another player is working on this engine.");
			return false;
		}
		return true;
	}

	private static List<string> EngineKeys(LoaderSync sync, GameObject engine)
	{
		var keys = new List<string>();
		foreach (var script in engine.GetComponentsInChildren<PartScript>(true))
			if (sync.Registry.TryGetSubPath(script, out var path)) keys.Add(PartKeys.Sub(path));
		return keys;
	}

	private static bool TryFindEngineOwner(GameObject engine, out LoaderSync sync)
	{
		sync = null;
		var places = CarLoaderPlaces.Get();
		if (places == null) return false;
		for (int i = 0; i < places.GetCarLoadersCount(); i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader == null || carLoader.e_engine_h == null || carLoader.e_engine_h != engine) continue;
			int loader = i;
			sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == loader);
			return sync?.Registry != null;
		}
		return false;
	}
}
