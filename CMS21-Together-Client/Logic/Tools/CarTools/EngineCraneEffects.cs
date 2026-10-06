using CMS21_Together_Core.Data.GameType;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Tools.CarTools;

// Engine out/in is row 1's part transaction (EngineCraneHooks); this only announces it. Row 1's prefixes may refuse.
[HarmonyPatch]
public static class EngineCraneEffects
{
	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ActionUnMountGroup))]
	[HarmonyPostfix]
	private static void AfterUnMountGroup(InteractiveObject iO, bool __runOriginal)
	{
		if (__runOriginal && iO != null) CarToolActions.Send(ModToolId.EngineCrane, EngineOwner(iO.gameObject), ToolActionKind.EngineOut);
	}

	[HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.InsertEngineToCar))]
	[HarmonyPostfix]
	private static void AfterInsertEngine(GroupItem engine, bool __runOriginal)
	{
		if (__runOriginal && engine != null)
			CarToolActions.Send(ModToolId.EngineCrane, ToolsMoveManager.Get()?.GetConnectedCarLoader(IOSpecialType.EngineCrane), ToolActionKind.EngineIn);
	}

	private static CarLoader EngineOwner(GameObject engine)
	{
		var places = CarLoaderPlaces.Get();
		for (int i = 0; places != null && i < places.GetCarLoadersCount(); i++)
		{
			var carLoader = places.GetCarLoaderByIndex(i);
			if (carLoader != null && carLoader.e_engine_h != null && carLoader.e_engine_h == engine) return carLoader;
		}
		return null;
	}
}
