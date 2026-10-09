using CMS21_Together_Core.Data.GameType;
using HarmonyLib;

namespace CMS21Together.Logic.Car.Details;

// Commit points of details that are not polled (docs/spikes/car-details.md "Recommended hooks").
[HarmonyPatch]
public static class CarDetailsHooks
{
	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SetNewLicensePlateNumber))]
	[HarmonyPostfix]
	private static void AfterPlateNumber(CarLoader __instance) => CarDetailsSync.MarkDirty(__instance, CarDetailSection.Plates);

	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.ChangeLicencePlateTexture), typeof(CarPart), typeof(string))]
	[HarmonyPostfix]
	private static void AfterPlateTexture(CarLoader __instance) => CarDetailsSync.MarkDirty(__instance, CarDetailSection.Plates);

	[HarmonyPatch(typeof(CMS.UI.Windows.TintingWindow), "TintAction")]
	[HarmonyPostfix]
	private static void AfterTint(CMS.UI.Windows.TintingWindow __instance) =>
		CarDetailsSync.MarkDirty(__instance.tintManager?.carLoader, CarDetailSection.BodyCosmetics);

	[HarmonyPatch(typeof(CMS.Managers.PaintshopManager), nameof(CMS.Managers.PaintshopManager.SubmitColor))]
	[HarmonyPostfix]
	private static void AfterPaint(CMS.Managers.PaintshopManager __instance) =>
		CarDetailsSync.MarkDirty(__instance.carLoader, CarDetailSection.Paint | CarDetailSection.BodyCosmetics);

	[HarmonyPatch(typeof(CMS.UI.Logic.Tune.GearboxTab), nameof(CMS.UI.Logic.Tune.GearboxTab.ApplyAction))]
	[HarmonyPostfix]
	private static void AfterGearboxApply(CMS.UI.Logic.Tune.GearboxTab __instance)
	{
		Locks.LockTuneHooks.Touch();
		CarDetailsSync.MarkDirty(__instance.carLoader, CarDetailSection.Tuning);
	}

	[HarmonyPatch(typeof(CMS.PartModules.PartModule), nameof(CMS.PartModules.PartModule.Tune))]
	[HarmonyPostfix]
	private static void AfterTune(CMS.PartModules.PartModule __instance)
	{
		Locks.LockTuneHooks.Touch();
		var places = CarLoaderPlaces.Get();
		for (int i = 0; places != null && i < places.carLoaders.Length; i++)
		{
			var root = places.carLoaders[i]?.GetRoot();
			if (root != null && __instance.transform.IsChildOf(root.transform))
			{
				CarDetailsSync.MarkDirty(places.carLoaders[i], CarDetailSection.Tuning);
				return;
			}
		}
	}
}
