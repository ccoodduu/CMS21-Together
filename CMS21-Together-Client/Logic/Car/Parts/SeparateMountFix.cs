using HarmonyLib;

namespace CMS21Together.Logic.Car.Parts;

// The game's ShowMounted wants a group item for a part with unmountWith members and stops in its first step when the
// part is mounted from a single item (members that come off as separate items, such as a radiator fan's blades): the
// item stays in the inventory and the parts it blocks are not blocked again. Without the members it takes the item's path.
[HarmonyPatch]
public static class SeparateMountFix
{
	[HarmonyPatch(typeof(PartScript._ShowMounted_d__155), nameof(PartScript._ShowMounted_d__155.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeShowMountedStep(PartScript._ShowMounted_d__155 __instance, out Il2CppSystem.Collections.Generic.List<PartScript> __state)
	{
		__state = null;
		var part = __instance.__4__this;
		if (__instance.__1__state != 0 || part == null) return;
		var members = part.unmountWith;
		if (members == null || members.Count == 0) return;
		var selected = GameScript.Get()?.SelectedToMount;
		if (selected == null || selected.TryCast<GroupItem>() != null || selected.TryCast<Item>() == null) return;
		__state = members;
		part.unmountWith = new Il2CppSystem.Collections.Generic.List<PartScript>();
		CMS21_Together_Core.Logging.Log.Debug($"[Parts] {part.id} mounted from a single item: its {members.Count} unmountWith members follow their own items.");
	}

	[HarmonyPatch(typeof(PartScript._ShowMounted_d__155), nameof(PartScript._ShowMounted_d__155.MoveNext))]
	[HarmonyPostfix]
	private static void AfterShowMountedStep(PartScript._ShowMounted_d__155 __instance, Il2CppSystem.Collections.Generic.List<PartScript> __state)
	{
		if (__state != null && __instance.__4__this != null) __instance.__4__this.unmountWith = __state;
	}
}
