using System;
using HarmonyLib;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D1 leak detector: these prefixes only log while a visual runs; they never block. Every
// patched method is already patched by InventoryHook or GuardHooks, so their detours are known to fire.
[HarmonyPatch]
public static class VisualLeakHooks
{
	[HarmonyPatch(typeof(Inventory), nameof(Inventory.Add), new Type[] { typeof(Item), typeof(bool) })]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void BeforeAdd() => VisualScope.CheckLeak("Inventory.Add");

	[HarmonyPatch(typeof(Inventory), nameof(Inventory.Delete), new Type[] { typeof(Item) })]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void BeforeDelete() => VisualScope.CheckLeak("Inventory.Delete");

	[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddGroup), new Type[] { typeof(GroupItem) })]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void BeforeAddGroup() => VisualScope.CheckLeak("Inventory.AddGroup");

	[HarmonyPatch(typeof(Inventory), nameof(Inventory.DeleteGroup), new Type[] { typeof(long) })]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void BeforeDeleteGroup() => VisualScope.CheckLeak("Inventory.DeleteGroup");

	[HarmonyPatch(typeof(GameMode), nameof(GameMode.SetCurrentMode))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void BeforeSetCurrentMode() => VisualScope.CheckLeak("GameMode.SetCurrentMode");
}
