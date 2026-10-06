using System.Collections;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using MelonLoader;
using UnhollowerBaseLib;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

// Applies remote part records with the game's "from save" methods: no animation, sound, inventory change, XP,
// money or game-mode switch (docs/spikes/native-decompile.md, section d).
public static class PartApplier
{
	private const float MountSettleSeconds = 0.5f;

	public static bool Apply(CarLoader carLoader, PartRegistry registry, CarBodyPartUpdatePacket record)
	{
		var part = registry.Body(record.Key);
		if (part == null || part.name != record.PartName)
		{
			Log.Warn($"[Parts] {carLoader.carToLoad}: body {record.Key} '{record.PartName}' does not resolve (found '{part?.name}').");
			return false;
		}

		if (part.Unmounted != record.Unmounted)
		{
			if (record.Unmounted) carLoader.TakeOffCarPartFromSave(part.name);
			else carLoader.TakeOnCarPartFromSave(part.name);
		}
		if (part.Switched != record.Switched) carLoader.SwitchCarPart(part, true, record.Switched);
		if (!string.IsNullOrEmpty(record.TunedID) && part.TunedID != record.TunedID) carLoader.TunePart(part.name, record.TunedID);
		if (record.State != null)
		{
			carLoader.SetCondition(part, record.State.Condition);
			carLoader.SetDent(part, record.State.Dent);
			part.Quality = record.State.Quality;
		}
		carLoader.UpdateCarBodyPart(part);
		return true;
	}

	public static bool Apply(CarLoader carLoader, PartRegistry registry, CarSubPartUpdatePacket record)
	{
		var script = registry.Sub(record.Key);
		if (script == null || script.id != record.PartId)
		{
			Log.Warn($"[Parts] {carLoader?.carToLoad ?? "engine stand"}: part {record.Key} '{record.PartId}' does not resolve (found '{script?.id}').");
			return false;
		}

		if (!string.IsNullOrEmpty(record.TunedID) && script.tunedID != record.TunedID) script.TunePart(record.TunedID);
		// SetConditionNormal throws after storing Condition while the part's highlighter is not set up yet.
		if (script.ho != null) script.SetConditionNormal(record.Condition);
		else script.Condition = Mathf.Clamp01(record.Condition);
		script.Quality = record.Quality;
		script.IsExamined = record.IsExamined;
		script.UpdateDust(record.Dust, true);
		if (record.MountObjectData != null) script.SetMountObjectData(ToGame(record.MountObjectData));

		if (script.IsUnmounted != record.Unmounted)
		{
			if (record.Unmounted)
			{
				script.HideBySavegame(false, carLoader);
			}
			else
			{
				script.ShowBySaveGame();
				MelonCoroutines.Start(ShowMounted(script));
			}
		}
		return true;
	}

	// The game's own ShowMounted also deletes the inventory item, adds XP and switches the game mode.
	private static IEnumerator ShowMounted(PartScript script)
	{
		script.IsUnmounted = false;
		if (script.ShouldUnmountWith())
			foreach (var member in script.unmountWith)
				member.MountByGroup(true);
		yield return new WaitForSeconds(MountSettleSeconds);
		if (script == null) yield break;

		script.UnblockBlockParts(false);
		foreach (var go in script.enableOnUnmount) go.SetActive(false);
		foreach (var go in script.disableOnUnmount)
		{
			var meshRenderer = go.GetComponent<MeshRenderer>();
			if (meshRenderer != null) meshRenderer.enabled = true;
			var meshCollider = go.GetComponent<MeshCollider>();
			if (meshCollider != null) meshCollider.enabled = true;
		}
		foreach (var go in script.hideWhenUnmontingMounting)
		{
			var renderer = go.GetComponent<Renderer>();
			if (renderer != null) renderer.enabled = true;
		}
		if (script.MountObjects != null)
			foreach (var mountObject in script.MountObjects)
				mountObject?.SetFullMountPosition();
	}

	private static MountObjectData ToGame(CMS21_Together_Core.Data.GameType.ModMountObjectData data)
	{
		var result = new MountObjectData { ParentPath = data.ParentPath };
		if (data.Condition != null) result.Condition = (Il2CppStructArray<float>)data.Condition;
		if (data.IsStuck != null) result.IsStuck = (Il2CppStructArray<bool>)data.IsStuck;
		return result;
	}
}
