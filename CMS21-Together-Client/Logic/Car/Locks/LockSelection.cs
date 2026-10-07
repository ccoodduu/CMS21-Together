using System;
using System.Collections.Generic;
using CMS.UI.Logic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

[HarmonyPatch]
public static class LockSelection
{
	private const float CacheSeconds = 0.1f;

	private static readonly Dictionary<IntPtr, (float At, string Message)> cache = new Dictionary<IntPtr, (float, string)>();

	public static void Reset() => cache.Clear();

	public static string BlockedMessage(PartScript part)
	{
		if (part == null || !LockGate.Active) return null;
		float now = Time.realtimeSinceStartup;
		if (cache.TryGetValue(part.Pointer, out var cached) && now - cached.At < CacheSeconds) return cached.Message;
		string message = Compute(part);
		cache[part.Pointer] = (now, message);
		return message;
	}

	private static string Compute(PartScript part)
	{
		if (!LockHooks.TryResolve(part, out int loader, out string key)) return null;
		if (CarMotion.IsMoving(loader, out int mover)) return LockMessages.Moving(loader, mover);
		var kind = part.IsUnmounted ? CarLockKind.PartMount : CarLockKind.PartUnmount;
		var conflict = CarLockMirror.Conflict(LockSets.ForPart(loader, key, kind));
		return conflict == null ? null : LockMessages.ForConflict(loader, conflict, key);
	}

	public static string BlockedMessage(CarLoader carLoader, string partName)
	{
		if (carLoader == null || string.IsNullOrEmpty(partName) || !LockGate.Active) return null;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		var part = carLoader.GetCarPart(partName);
		var registry = LockSets.Relations(loader)?.Registry;
		if (part == null || registry == null || !registry.TryGetBodyIndex(part, out int index)) return null;
		if (CarMotion.IsMoving(loader, out int mover)) return LockMessages.Moving(loader, mover);
		var conflict = CarLockMirror.Conflict(LockSets.ForBody(loader, index));
		return conflict == null ? null : LockMessages.ForConflict(loader, conflict, PartKeys.Body(index));
	}

	[HarmonyPatch(typeof(PartScript), nameof(PartScript.SetMouseOver), new Type[0])]
	[HarmonyPrefix]
	private static bool BeforeSetMouseOver(PartScript __instance)
	{
		if (BlockedMessage(__instance) == null) return true;
		CarLockMirror.Count("blockedAtSelection.highlight");
		return false;
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.SetPartMouseOver))]
	[HarmonyPostfix]
	private static void AfterSetPartMouseOver(PartScript part)
	{
		string message = BlockedMessage(part);
		if (message == null) return;
		UIManager.Get()?.SetIODescription(message, AlternativeDescriptionID.None);
	}

	[HarmonyPatch(typeof(InteractiveObject), nameof(InteractiveObject.SetMouseOver), typeof(bool))]
	[HarmonyPrefix]
	private static void BeforeIOSetMouseOver(InteractiveObject __instance, ref bool b) => SuppressBody(__instance, ref b);

	[HarmonyPatch(typeof(InteractiveObject), nameof(InteractiveObject.SetMouseOver), typeof(bool), typeof(Color))]
	[HarmonyPrefix]
	private static void BeforeIOSetMouseOverColor(InteractiveObject __instance, ref bool b) => SuppressBody(__instance, ref b);

	private static void SuppressBody(InteractiveObject io, ref bool b)
	{
		if (!b || io == null || !LockGate.Active) return;
		var game = GameScript.Get();
		var carLoader = game?.IOMouseOverCarLoader;
		string type = game?.IOMouseOverType;
		if (carLoader == null || string.IsNullOrEmpty(type) || game.IOMouseOverIO == null || game.IOMouseOverIO.Pointer != io.Pointer) return;
		if (BlockedMessage(carLoader, type.Substring(1)) == null) return;
		CarLockMirror.Count("blockedAtSelection.bodyHighlight");
		b = false;
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.SetIOMouseOver))]
	[HarmonyPostfix]
	private static void AfterSetIOMouseOver(GameScript __instance)
	{
		string type = __instance.IOMouseOverType;
		if (string.IsNullOrEmpty(type)) return;
		string message = BlockedMessage(__instance.IOMouseOverCarLoader, type.Substring(1));
		if (message != null) UIManager.Get()?.SetIODescription(message, AlternativeDescriptionID.None);
	}
}
