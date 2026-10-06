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
}
