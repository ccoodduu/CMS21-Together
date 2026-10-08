using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Away;

// sync-test-drive-and-diagnostics D2/D7: the dyno takes an away claim when it starts and commits its result to the
// car details when it closes after a measurement; a cancelled run restores the game's backups and sends nothing.
[HarmonyPatch]
public static class DynoSync
{
	private static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid;

	public static bool IsOpenOn(CarLoader carLoader)
	{
		var manager = DynoManager.Get();
		return manager != null && carLoader != null && manager.CarLoader == carLoader && GameMode.Get()?.GetCurrentMode() == gameMode.Dyno;
	}

	public static void Commit(CarLoader carLoader)
	{
		if (carLoader == null) return;
		Log.Info($"[Dyno] {carLoader.carToLoad}: measured result committed.");
		CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Dyno);
	}

	[HarmonyPatch(typeof(DynoManager), nameof(DynoManager.RunDyno))]
	[HarmonyPrefix]
	private static bool BeforeRunDyno(DynoManager __instance)
	{
		if (!Active || __instance.CarLoader == null) return true;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance.CarLoader);
		return loader < 0 || !CarAwaySync.BlockIfLocked(loader, "dyno");
	}

	[HarmonyPatch(typeof(DynoManager), nameof(DynoManager.RunDyno))]
	[HarmonyPostfix]
	private static void AfterRunDyno(DynoManager __instance, bool __runOriginal)
	{
		if (!__runOriginal || !Active || __instance.CarLoader == null) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__instance.CarLoader);
		if (loader < 0) return;
		CarAwaySync.Request(loader, CarAwayKind.Dyno, null, (refusal, owner) =>
		{
			Log.Info($"[Dyno] Loader {loader}: claim refused ({refusal}), closing the dyno.");
			ModNotify.ShowToast(refusal == CarAwayRefusal.Busy ? $"{CarAwaySync.OwnerName(owner)} is using this car." : "Another player is working on this car.");
			UnityEngine.Object.FindObjectOfType<CMS.UI.Windows.DynoWindow>()?.HideAction();
		});
	}

	[HarmonyPatch(typeof(DynoManager), nameof(DynoManager.CloseDyno))]
	[HarmonyPrefix]
	private static void BeforeCloseDyno(DynoManager __instance, out (CarLoader Car, bool Measured) __state) =>
		__state = (__instance.CarLoader, __instance.DynoMeasured);

	[HarmonyPatch(typeof(DynoManager), nameof(DynoManager.CloseDyno))]
	[HarmonyPostfix]
	private static void AfterCloseDyno((CarLoader Car, bool Measured) __state)
	{
		if (!Active || __state.Car == null) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(__state.Car);
		if (loader < 0) return;
		if (__state.Measured) Commit(__state.Car);
		CarAwaySync.Release(loader);
	}
}
