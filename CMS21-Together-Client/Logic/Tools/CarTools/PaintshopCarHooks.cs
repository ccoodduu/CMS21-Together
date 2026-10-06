using CMS21_Together_Core.Data.GameType;
using CMS.Managers;
using HarmonyLib;

namespace CMS21Together.Logic.Tools.CarTools;

// The paint is final at SubmitColor (row 4 sends it from its own postfix); MakeCarPaintEffects is built inline and never fires.
[HarmonyPatch]
public static class PaintshopCarHooks
{
	[HarmonyPatch(typeof(PaintshopManager), nameof(PaintshopManager.SubmitColor))]
	[HarmonyPostfix]
	private static void AfterSubmit(PaintshopManager __instance)
	{
		if (__instance.PaintshopType == CMS.UI.Logic.PaintshopType.Garage) CarToolActions.Send(ModToolId.Paintshop, __instance.carLoader, ToolActionKind.PaintCar);
	}
}
