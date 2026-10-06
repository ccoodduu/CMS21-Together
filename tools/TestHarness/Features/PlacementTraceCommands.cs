using System;
using HarmonyLib;
using MelonLoader;

namespace TogetherTestHarness.Features;

// Spike for sync-car-placement-and-lifts group 1 (runtime part); removed when the spike is written up.
[HarmonyPatch]
public static class PlacementTraceCommands
{
    private static bool tracing;

    private static void Trace(string text)
    {
        if (tracing) MelonLogger.Msg($"[Harness] placement-trace {text}");
    }

    [HarnessCommand("placement-trace")]
    private static object PlacementTrace(string args)
    {
        tracing = (args ?? "").Trim() == "on";
        return tracing ? "tracing" : "not tracing";
    }

    private static string Loader(CarLoader carLoader) => carLoader == null ? "null" : $"{CarLoaderPlaces.Get()?.GetCarLoaderId(carLoader)}({carLoader.carToLoad}, placeNo {carLoader.GetPlaceNo()})";

    [HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
    [HarmonyPrefix]
    private static void BeforeLifterAction(CarLifter __instance, int actionType) =>
        Trace($"CarLifter.Action({actionType}) before: state {__instance.GetState()}, isMoving {__instance.isMoving}, car {Loader(__instance.GetConnectedCarLoader())}");

    [HarmonyPatch(typeof(CarLifter), nameof(CarLifter.Action))]
    [HarmonyPostfix]
    private static void AfterLifterAction(CarLifter __instance, int actionType) =>
        Trace($"CarLifter.Action({actionType}) after: state {__instance.GetState()}, isMoving {__instance.isMoving}");

    [HarmonyPatch(typeof(CarLifter), nameof(CarLifter.InstantSet))]
    [HarmonyPrefix]
    private static void BeforeInstantSet(CarLifter __instance, int _pos) => Trace($"CarLifter.InstantSet({_pos}) from {__instance.GetState()}");

    [HarmonyPatch(typeof(NotificationCenter._ChangeCarPos_d__20), nameof(NotificationCenter._ChangeCarPos_d__20.MoveNext))]
    [HarmonyPrefix]
    private static void BeforeChangeCarPosStep(NotificationCenter._ChangeCarPos_d__20 __instance)
    {
        try { Trace($"ChangeCarPos.MoveNext state {__instance.__1__state} car {Loader(__instance.carLoader)} pos {__instance.pos} movePlayer {__instance.movePlayerToCar}"); }
        catch (Exception e) { Trace($"ChangeCarPos.MoveNext (fields unreadable: {e.GetType().Name})"); }
    }

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.ChangeCarPos))]
    [HarmonyPrefix]
    private static void BeforeChangeCarPos(CarLoader carLoader, CarPlace pos) => Trace($"NotificationCenter.ChangeCarPos {Loader(carLoader)} -> {pos}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.ChangePosition), typeof(int))]
    [HarmonyPrefix]
    private static void BeforeChangePosition(CarLoader __instance, int no) => Trace($"CarLoader.ChangePosition({no}) {Loader(__instance)}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.ResetCarLifter))]
    [HarmonyPrefix]
    private static void BeforeResetCarLifter(CarLoader __instance) => Trace($"CarLoader.ResetCarLifter {Loader(__instance)}");

    [HarmonyPatch(typeof(NotificationCenter), nameof(NotificationCenter.MoveCarToParking))]
    [HarmonyPrefix]
    private static void BeforeMoveCarToParking(CarLoader carLoader) => Trace($"NotificationCenter.MoveCarToParking {Loader(carLoader)}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.SaveCarToFile), typeof(int), typeof(bool))]
    [HarmonyPostfix]
    private static void AfterSaveCarToFile(CarLoader __instance, int index, bool toParking) =>
        Trace($"CarLoader.SaveCarToFile({index}, toParking {toParking}) {Loader(__instance)}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.LoadCarFromFile), typeof(int), typeof(bool))]
    [HarmonyPrefix]
    private static void BeforeLoadCarFromFile(CarLoader __instance, int index, bool fromParking) =>
        Trace($"CarLoader.LoadCarFromFile({index}, fromParking {fromParking}) {Loader(__instance)}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.LoadCar))]
    [HarmonyPrefix]
    private static void BeforeLoadCar(CarLoader __instance, string name) => Trace($"CarLoader.LoadCar({name}) {Loader(__instance)}");

    [HarmonyPatch(typeof(CarLoader), nameof(CarLoader.DeleteCar), new Type[0])]
    [HarmonyPrefix]
    private static void BeforeDeleteCar(CarLoader __instance) => Trace($"CarLoader.DeleteCar() {Loader(__instance)}");

    [HarmonyPatch(typeof(CMS.Managers.ParkingCarPlaceManager), nameof(CMS.Managers.ParkingCarPlaceManager.MoveCar))]
    [HarmonyPostfix]
    private static void AfterParkingMoveCar(int from, int to) => Trace($"ParkingCarPlaceManager.MoveCar({from}, {to})");
}
