using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockBonusHooks
{
	private static bool fitting;

	public static bool RefuseFilledSlot(GameScript game)
	{
		var io = game.IOMouseOverIO;
		if (io == null || io.specialType != IOSpecialType.BonusPart || LockFluidHooks.LoaderOf(game.IOMouseOverCarLoader) < 0) return false;
		var part = game.IOMouseOverCarLoader.GetBonusPart(io);
		if (part == null || part.IsUnmounted || part.IsDummy()) return false;
		LockMessages.Refuse(LockMessages.SlotChanged);
		return true;
	}

	public static GatedAction FitAction(GameScript game, BaseItem item, InteractiveObject io)
	{
		var carLoader = game.IOMouseOverCarLoader;
		int loader = LockFluidHooks.LoaderOf(carLoader);
		int slot = CarDetailsIO.BonusSlotOf(carLoader, io);
		var uid = item.TryCast<Item>()?.UID ?? 0;
		if (loader < 0 || slot < 0 || uid == 0) return null;
		string before = CarDetailsIO.BonusSignature(carLoader, slot);
		var set = LockSets.ForBonus(loader, slot, before);
		set.Items.Add(uid);
		string type = game.IOMouseOverType;
		var go = game.IOMouseOverGO;
		var mode = GameMode.Get()?.currentMode ?? gameMode.None;
		int seq = CarPartsSync.SpawnSeq(loader);
		var inventory = Singleton<GameManager>.Instance.Inventory;
		return new GatedAction
		{
			Set = set, Target = item, TargetKey = LockKeys.Bonus(slot),
			Context = () => CarPartsSync.SpawnSeq(loader) == seq && GameMode.Get()?.currentMode == mode && inventory.GetItem(uid) != null
			                && CarDetailsIO.BonusSignature(carLoader, slot) == before,
			Run = () =>
			{
				var (pointedType, pointedLoader, pointedIo, pointedGo) = (game.IOMouseOverType, game.IOMouseOverCarLoader, game.IOMouseOverIO, game.IOMouseOverGO);
				(game.IOMouseOverType, game.IOMouseOverCarLoader, game.IOMouseOverIO, game.IOMouseOverGO) = (type, carLoader, io, go);
				fitting = true;
				try { game.SelectPartToMount(item); }
				finally
				{
					fitting = false;
					(game.IOMouseOverType, game.IOMouseOverCarLoader, game.IOMouseOverIO, game.IOMouseOverGO) = (pointedType, pointedLoader, pointedIo, pointedGo);
				}
			},
			Started = () => CarDetailsIO.BonusSignature(carLoader, slot) != before,
			OnStarted = lockId => Commit(loader, carLoader, lockId),
		};
	}

	public static GatedAction RemoveAction(CarLoader carLoader, InteractiveObject io, bool instant)
	{
		int loader = LockFluidHooks.LoaderOf(carLoader);
		int slot = CarDetailsIO.BonusSlotOf(carLoader, io);
		if (loader < 0 || slot < 0) return null;
		var part = carLoader.GetBonusParts()[slot];
		string before = CarDetailsIO.BonusSignature(carLoader, slot);
		int seq = CarPartsSync.SpawnSeq(loader);
		return new GatedAction
		{
			Set = LockSets.ForBonus(loader, slot, before), Target = io, TargetKey = LockKeys.Bonus(slot),
			Context = () => CarPartsSync.SpawnSeq(loader) == seq && CarDetailsIO.BonusSignature(carLoader, slot) == before,
			Run = () => carLoader.TakeOffBonusPart(io, instant),
			Started = () => part.IsUnmounted,
			OnStarted = lockId => Commit(loader, carLoader, lockId),
		};
	}

	private static void Commit(int loader, CarLoader carLoader, int lockId)
	{
		CarLockMirror.MarkEnding(lockId);
		CarDetailsSync.MarkDirty(carLoader, CarDetailSection.BonusParts);
		CarDetailsSync.FlushNow(loader, CarDetailSection.BonusParts, () => CarLockMirror.Release(lockId));
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffBonusPart))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeTakeOffBonusPart(CarLoader __instance, InteractiveObject io, bool instant)
	{
		if (fitting || !LockGate.Active) return true;
		var part = __instance.GetBonusPart(io);
		if (part == null || part.IsUnmounted || part.IsDummy()) return true;
		var action = RemoveAction(__instance, io, instant);
		return action == null || LockGate.Enter(action);
	}

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.TakeOffBonusPart))]
	[HarmonyPostfix]
	private static void AfterTakeOffBonusPart(CarLoader __instance, bool __runOriginal)
	{
		if (__runOriginal) CarDetailsSync.MarkDirty(__instance, CarDetailSection.BonusParts);
	}
}
