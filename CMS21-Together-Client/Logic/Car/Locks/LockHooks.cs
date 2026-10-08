using System;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockHooks
{
	private static readonly HashSet<gameMode> BodyModes = new HashSet<gameMode>
	{
		gameMode.GarageDisassemble, gameMode.GarageAssemble, gameMode.InteriorDisassemble, gameMode.InteriorAssemble,
		gameMode.BonusAssemble, gameMode.BonusDisassemble
	};

	public static bool TryResolve(PartScript script, out int loader, out string key)
	{
		loader = -1;
		key = null;
		if (script == null) return false;
		foreach (var sync in CarPartsSync.All)
		{
			if (sync.Registry == null || !sync.Registry.TryGetSubPath(script, out var path)) continue;
			loader = sync.Loader;
			key = PartKeys.Sub(path);
			return true;
		}
		return false;
	}

	private static bool TryResolveBody(CarLoader carLoader, string partName, out int loader, out int index, out CarPart part)
	{
		int id = carLoader == null ? -1 : CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		loader = id;
		index = -1;
		part = carLoader?.GetCarPart(partName);
		var sync = CarPartsSync.All.FirstOrDefault(s => s.Loader == id);
		return part != null && sync?.Registry != null && sync.Registry.TryGetBodyIndex(part, out index);
	}

	private static gameMode Mode => GameMode.Get()?.currentMode ?? gameMode.None;

	private static PartScript MainOf(PartScript script)
	{
		var main = script.unmountWithMainObject;
		return main != null ? main : script;
	}

	public static GatedAction UnmountAction(PartScript script)
	{
		if (!TryResolve(script, out int loader, out string key)) return null;
		var main = MainOf(script);
		TryResolve(main, out _, out string mainKey);
		var set = LockSets.ForPart(loader, key, CarLockKind.PartUnmount);
		if (set == null) return null;
		int seq = CarPartsSync.SpawnSeq(loader);
		var mode = Mode;
		return new GatedAction
		{
			Set = set, Target = script, TargetKey = key,
			Context = () => script != null && !script.IsUnmounted && CarPartsSync.SpawnSeq(loader) == seq && Mode == mode,
			Run = () => script.ActionUnMount(),
			Started = () => main.oneClickUnmount ? main.IsUnmounted : Mode == gameMode.PartUnMount && GameScript.Get()?.SelectedPart?.Pointer == main.Pointer,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, mainKey ?? key, main),
			PrefetchedLockId = LockPrefetch.Take(loader, set),
		};
	}

	public static GatedAction MountAction(PartScript script)
	{
		if (!TryResolve(script, out int loader, out string key)) return null;
		var main = MainOf(script);
		TryResolve(main, out _, out string mainKey);
		var set = LockSets.ForPart(loader, key, CarLockKind.PartMount);
		if (set == null) return null;
		int seq = CarPartsSync.SpawnSeq(loader);
		var center = NotificationCenter.Get();
		var mountGroup = center?.mountGroup;
		return new GatedAction
		{
			Set = set, Target = script, TargetKey = key,
			Context = () => script != null && script.IsUnmounted && CarPartsSync.SpawnSeq(loader) == seq,
			Run = () =>
			{
				if (center != null) center.SetMountGroup(mountGroup);
				script.ActionMount(true);
			},
			Started = () => WindowManager.Instance != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp),
			OnStarted = lockId => LockLifecycle.Track(lockId, set, mainKey ?? key, main, phase: LockPhase.Slot),
		};
	}

	public static GatedAction ItemAction(GameScript game, BaseItem item)
	{
		if (item == null) return null;
		var mode = Mode;
		if (BodyModes.Contains(mode)) return BodyMountAction(game, item);
		var part = game.partMouseOver;
		if (!TryResolve(part, out int loader, out string key)) return null;
		var main = MainOf(part);
		TryResolve(main, out _, out string mainKey);
		var slot = LockLifecycle.SlotFor(main) ?? LockLifecycle.SlotFor(part);
		var set = LockSets.ForPart(loader, key, CarLockKind.PartMount);
		if (set == null) return null;
		var group = item.TryCast<GroupItem>();
		if (group != null)
		{
			set.Items.Add(group.UID);
			for (int i = 0; group.ItemList != null && i < group.ItemList.Count; i++) set.Items.Add(group.ItemList[i].UID);
		}
		else set.Items.Add(item.TryCast<Item>()?.UID ?? 0);
		set.Items.RemoveAll(uid => uid == 0);
		if (slot != null) slot.ItemPicked = true;
		return new GatedAction
		{
			Set = set, Target = item, TargetKey = key, ExtendLockId = slot?.LockId ?? 0,
			EndsSlotOnFailure = slot != null,
			Context = () => part != null && part.IsUnmounted,
			Run = () => game.SelectPartToMount(item),
			Started = () => Mode == gameMode.PartMount || !main.IsUnmounted,
			OnStarted = lockId =>
			{
				var tracked = LockLifecycle.Get(lockId) ?? LockLifecycle.Track(lockId, set, mainKey ?? key, main);
				tracked.Phase = LockPhase.Work;
				tracked.ItemPicked = true;
				tracked.LastProgressAt = Time.realtimeSinceStartup;
			},
		};
	}

	private static GatedAction BodyMountAction(GameScript game, BaseItem item)
	{
		string type = game.IOMouseOverType;
		var carLoader = game.IOMouseOverCarLoader;
		if (string.IsNullOrEmpty(type) || carLoader == null || !TryResolveBody(carLoader, type.Substring(1), out int loader, out int index, out var part)) return null;
		var set = LockSets.ForBody(loader, index);
		if (set == null) return null;
		long uid = item.TryCast<Item>()?.UID ?? 0;
		if (uid != 0) set.Items.Add(uid);
		bool before = part.Unmounted;
		return new GatedAction
		{
			Set = set, Target = item, TargetKey = PartKeys.Body(index),
			Context = () => part.Unmounted == before,
			Run = () => game.SelectPartToMount(item),
			Started = () => part.Unmounted != before || part.TakeOnOffInProgress,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, PartKeys.Body(index), body: part),
		};
	}

	public static GatedAction GroupPickAction(ChoosePartUpWindow window, Item item)
	{
		var slot = LockLifecycle.All.FirstOrDefault(t => t.Phase == LockPhase.Slot && t.EndingSince < 0f);
		if (slot == null || item == null) return null;
		var set = new LockSet { Loader = slot.Loader, Kind = CarLockKind.PartMount };
		var record = CarLockMirror.Get(slot.LockId);
		if (record != null)
		{
			set.X.AddRange(record.X);
			set.S.AddRange(record.S);
		}
		else set.X.Add(slot.MainKey);
		set.Items.Add(item.UID);
		long uid = item.UID;
		var inventory = Singleton<GameManager>.Instance.Inventory;
		return new GatedAction
		{
			Set = set, Target = item, TargetKey = slot.MainKey, ExtendLockId = slot.LockId,
			Context = () => WindowManager.Instance != null && WindowManager.Instance.IsWindowActive(WindowID.ChoosePartUp) && inventory.GetItem(uid) != null,
			Run = () => window.SelectItemInCreateGroup(item),
			Started = () => inventory.GetItem(uid) == null,
			OnStarted = _ => slot.ItemPicked = true,
		};
	}

	public static GatedAction BodyTakeOffAction(CarLoader carLoader, string partName)
	{
		if (!TryResolveBody(carLoader, partName, out int loader, out int index, out var part)) return null;
		var set = LockSets.ForBody(loader, index);
		if (set == null) return null;
		bool before = part.Unmounted;
		return new GatedAction
		{
			Set = set, Target = part.handle, TargetKey = PartKeys.Body(index),
			Context = () => part.Unmounted == before,
			Run = () =>
			{
				PartTransactions.OpenForBody(loader, index, part);
				carLoader.TakeOffCarPart(partName);
			},
			Started = () => part.TakeOnOffInProgress || part.Unmounted != before,
			OnStarted = lockId => LockLifecycle.Track(lockId, set, PartKeys.Body(index), body: part),
		};
	}

	public static GatedAction GroupOutAction(NotificationCenter center, InteractiveObject io)
	{
		if (io == null) return null;
		var go = io.gameObject;
		var places = CarLoaderPlaces.Get();
		for (int i = 0; places != null && i < places.GetCarLoadersCount(); i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader == null || carLoader.e_engine_h == null || carLoader.e_engine_h != go) continue;
			int loader = i;
			var set = LockSets.ForCrane(loader);
			if (set == null) return null;
			int groupsBefore = 0;
			return new GatedAction
			{
				Set = set, Target = io, TargetKey = LockKeys.Engine,
				Context = () => io != null && carLoader.e_engine_h == go,
				Run = () =>
				{
					groupsBefore = EngineGroups(go.name);
					center.ActionUnMountGroup(io);
				},
				Started = () => EngineGroups(go.name) > groupsBefore,
				OnStarted = lockId => TrackCrane(lockId, set),
			};
		}
		return null;
	}

	public static GatedAction EngineInAction(NotificationCenter center, GroupItem engine)
	{
		if (engine == null) return null;
		var carLoader = ToolsMoveManager.Get()?.GetConnectedCarLoader(IOSpecialType.EngineCrane);
		if (carLoader == null || carLoader.e_engine_h == null) return null;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		var set = LockSets.ForCrane(loader);
		if (set == null) return null;
		set.Items.Add(engine.UID);
		long uid = engine.UID;
		var inventory = Singleton<GameManager>.Instance.Inventory;
		return new GatedAction
		{
			Set = set, Target = engine, TargetKey = LockKeys.Engine,
			Context = () => inventory.GetGroup(uid) != null,
			Run = () => center.InsertEngineToCar(engine),
			Started = () => inventory.GetGroup(uid) == null,
			OnStarted = lockId => TrackCrane(lockId, set),
		};
	}

	private static void TrackCrane(int lockId, LockSet set)
	{
		float start = Time.realtimeSinceStartup;
		int loader = set.Loader;
		LockLifecycle.Track(lockId, set, LockKeys.Engine,
			finished: () => Time.realtimeSinceStartup - start > 1.5f && !PartChangeTracker.IsPending(loader) && !PartTransactions.HasOpen(loader));
	}

	private static int EngineGroups(string name)
	{
		var groups = Singleton<GameManager>.Instance.Inventory.GetGroups();
		int count = 0;
		for (int i = 0; groups != null && i < groups.Count; i++)
			if (groups[i].ID == name) count++;
		return count;
	}

	private static bool Gate(GatedAction action, string key)
	{
		if (action == null) return true;
		return LockGate.Enter(action);
	}

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionUnMount))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeActionUnMount(PartScript __instance) => !LockGate.Active || Gate(UnmountAction(__instance), null);

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.ActionMount))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeActionMount(PartScript __instance, bool showMenu) => !showMenu || !LockGate.Active || Gate(MountAction(__instance), null);

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.SelectPartToMount))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeSelectPartToMount(GameScript __instance, BaseItem selectToMount)
	{
		if (!LockGate.Active || selectToMount == null) return true;
		var group = selectToMount.TryCast<GroupItem>();
		if (group != null && !BodyModes.Contains(Mode))
		{
			var slot = LockLifecycle.SlotFor(__instance.partMouseOver != null ? MainOf(__instance.partMouseOver) : null);
			if (slot != null && slot.ItemPicked)
			{
				slot.Phase = LockPhase.Work;
				slot.LastProgressAt = Time.realtimeSinceStartup;
				return true;
			}
		}
		return Gate(ItemAction(__instance, selectToMount), null);
	}

	[HarmonyPatch(typeof(ChoosePartUpWindow), nameof(ChoosePartUpWindow.SelectItemInCreateGroup))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeSelectItemInCreateGroup(ChoosePartUpWindow __instance, Item item) =>
		!LockGate.Active || Gate(GroupPickAction(__instance, item), null);

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffCarPart), typeof(string))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeTakeOffCarPart(CarLoader __instance, string partName) =>
		!LockGate.Active || Gate(BodyTakeOffAction(__instance, partName), null);

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.CanTakeOffCarPart))]
	[HarmonyPostfix]
	private static void AfterCanTakeOffCarPart(CarLoader __instance, string name, ref bool __result)
	{
		if (!__result || !LockGate.Active || !TryResolveBody(__instance, name, out int loader, out int index, out _)) return;
		if (!CarPartsSync.IsReady(loader) || CarAwaySync.LockedForMe(loader, out _, out _) || CarLockMirror.Conflict(LockSets.ForBody(loader, index)) != null) __result = false;
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ActionUnMountGroup))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeActionUnMountGroup(NotificationCenter __instance, InteractiveObject iO) =>
		!LockGate.Active || Gate(GroupOutAction(__instance, iO), null);

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.InsertEngineToCar))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeInsertEngineToCar(NotificationCenter __instance, GroupItem engine) =>
		!LockGate.Active || Gate(EngineInAction(__instance, engine), null);
}
