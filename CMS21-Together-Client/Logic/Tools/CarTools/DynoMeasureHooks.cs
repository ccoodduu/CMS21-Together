// Needs row 13's DynoSync (sync-test-drive-and-diagnostics); drop the #if once this branch is rebased onto it.
#if SYNC_TEST_DRIVE
using HarmonyLib;

namespace CMS21Together.Logic.Tools.CarTools;

// The map's "measure power" (MapWindow.MeasurePowerForSelectedCarLoader) is MeasurePower's only caller; the garage dyno
// run does not call it and is committed by DynoSync's CloseDyno hook.
[HarmonyPatch]
public static class DynoMeasureHooks
{
	[HarmonyPatch(typeof(CarLoader), nameof(CarLoader.MeasurePower))]
	[HarmonyPostfix]
	private static void AfterMeasurePower(CarLoader __instance)
	{
		if (CarToolActions.LoaderOf(__instance) >= 0) Car.Away.DynoSync.Commit(__instance);
	}
}
#endif
