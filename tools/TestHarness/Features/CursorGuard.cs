using HarmonyLib;

namespace TogetherTestHarness.Features;

// The game moves the Windows cursor through ProMouse when its 3D cursor is toggled (scene loads, mode changes); test
// games must not take the user's mouse. The public setters are inlined into their callers, so the coroutine that
// does the move is stopped too.
[HarmonyPatch]
public static class CursorGuard
{
    [HarmonyPatch(typeof(ProMouse), nameof(ProMouse.SetCursorPosition))]
    [HarmonyPrefix]
    private static bool BeforeSetCursorPosition() => false;

    [HarmonyPatch(typeof(ProMouse), nameof(ProMouse.SetGlobalCursorPosition))]
    [HarmonyPrefix]
    private static bool BeforeSetGlobalCursorPosition() => false;

    [HarmonyPatch(typeof(ProMouse.__SetCursorPosition_d__11), nameof(ProMouse.__SetCursorPosition_d__11.MoveNext))]
    [HarmonyPrefix]
    private static bool BeforeMoveStep(ref bool __result)
    {
        __result = false;
        return false;
    }
}
