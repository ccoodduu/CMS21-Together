using HarmonyLib;

namespace TogetherTestHarness.Features;

// The game moves the Windows cursor through ProMouse when its 3D cursor is toggled (scene loads, mode changes); test
// games must not take the user's mouse.
[HarmonyPatch]
public static class CursorGuard
{
    [HarmonyPatch(typeof(ProMouse), nameof(ProMouse.SetCursorPosition))]
    [HarmonyPrefix]
    private static bool BeforeSetCursorPosition() => false;

    [HarmonyPatch(typeof(ProMouse), nameof(ProMouse.SetGlobalCursorPosition))]
    [HarmonyPrefix]
    private static bool BeforeSetGlobalCursorPosition() => false;
}
