using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using CMS.Garage.Customization;
using CMS.UI;
using CMS.UI.Windows;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Tools;
using CMS21Together.Network;
using CMS21Together.UI;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Garage;

// shared-garage-look: the claim gate at the #garageLook click (D2), the commit when the window closes, and the
// per-section apply of the server's look through the game's own UpdateMaterials coroutine (D3-D5).
[HarmonyPatch]
public static class GarageLookSync
{
	private const string ClickType = "#garageLook";
	private const float ClaimTimeoutSeconds = 5f;

	private static bool claimHeld;
	private static int claimRequest;
	private static int pendingClaimRequest;
	private static string packAtOpen;
	private static ModGarageLook pending;
	private static bool applyRunning;
	private static int generation;
	private static string noticedPack;
	private static bool sectionCountLogged;

	public static ModGarageLook LastApplied { get; private set; }
	public static string LastRefusal { get; private set; }
	public static string SectionCountMismatch { get; private set; }
	public static long LastApplyMs { get; private set; } = -1;
	public static int LastApplyChanged { get; private set; }
	public static int Applies { get; private set; }

	public static bool HoldsClaim => claimHeld;

	public static bool ClaimPending => pendingClaimRequest != 0;

	public static bool IsApplying => applyRunning || pending != null;

	private static bool Deferred => claimHeld || WindowOpen;

	public static bool WindowOpen
	{
		get
		{
			var window = WindowManager.Instance?.GetWindowByID<GarageCustomizationWindow>(WindowID.GarageCustomization);
			return window != null && window.isActive;
		}
	}

	public static void Reset()
	{
		generation++;
		claimHeld = false;
		pendingClaimRequest = 0;
		pending = null;
		applyRunning = false;
		LastApplied = null;
		LastRefusal = null;
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.ClickIO))]
	[HarmonyPrefix]
	private static bool BeforeClickIO(GameScript __instance) => __instance.IOMouseOverType != ClickType || AllowOpen();

	private static bool AllowOpen()
	{
		if (Client.Instance == null || !Client.Instance.IsConnected || claimHeld) return true;
		if (ClaimPending) return false;
		if (IsApplying)
		{
			ModNotify.ShowToast("The garage look is still being updated.");
			return false;
		}
		int request = ++claimRequest;
		pendingClaimRequest = request;
		LastRefusal = null;
		Client.Instance.Send(new GarageLookClaimPacket());
		MelonCoroutines.Start(ClaimTimeout(request));
		return false;
	}

	private static IEnumerator ClaimTimeout(int request)
	{
		float deadline = Time.realtimeSinceStartup + ClaimTimeoutSeconds;
		while (Time.realtimeSinceStartup < deadline) yield return null;
		if (pendingClaimRequest != request) yield break;
		pendingClaimRequest = 0;
		LastRefusal = "no answer";
		ModNotify.ShowToast("No answer from the server.");
	}

	public static void OnClaimResult(GarageLookClaimResultPacket packet)
	{
		bool asked = ClaimPending;
		pendingClaimRequest = 0;
		if (packet.Granted && (!asked || !ClientScene.IsGarageReady || GameScript.Get() == null))
		{
			Client.Instance.Send(new GarageLookClaimPacket { Release = true });
			return;
		}
		if (!asked) return;
		if (!packet.Granted)
		{
			LastRefusal = $"{ToolSync.PlayerName(packet.HolderPlayerId)} is customising the garage.";
			ModNotify.ShowToast(LastRefusal);
			return;
		}
		claimHeld = true;
		packAtOpen = CurrentPackId();
		var game = GameScript.Get();
		game.StartCoroutine(game.ShowGarageCustomization());
	}

	[HarmonyPatch(typeof(GarageCustomizationWindow), nameof(GarageCustomizationWindow.Hide), typeof(bool))]
	[HarmonyPrefix]
	private static void BeforeHide(GarageCustomizationWindow __instance, out bool __state) => __state = __instance.isActive;

	[HarmonyPatch(typeof(GarageCustomizationWindow), nameof(GarageCustomizationWindow.Hide), typeof(bool))]
	[HarmonyPostfix]
	private static void AfterHide(bool __state)
	{
		if (__state) Commit();
	}

	private static void Commit()
	{
		bool held = claimHeld;
		claimHeld = false;
		if (!held || Client.Instance == null || !Client.Instance.IsConnected) return;
		var local = ReadLocal();
		if (local != null && !local.SameLook(LastApplied ?? new ModGarageLook()))
		{
			Log.Info($"[GarageLook] sending the look: {local.Describe()}.");
			Client.Instance.Send(new GarageLookUpdatePacket { Look = local });
		}
		Client.Instance.Send(new GarageLookClaimPacket { Release = true });
	}

	public static ModGarageLook ReadLocal()
	{
		var manager = GarageLookManager.Instance;
		var sections = manager?.GetSections();
		if (sections == null) return null;
		var last = LastApplied ?? new ModGarageLook();
		int count = sections.Length;
		var indexes = new int[Math.Max(count, last.MaterialIndexes?.Length ?? 0)];
		for (int i = 0; i < indexes.Length; i++)
			indexes[i] = i < count ? sections[i].SelectedMaterialIndex : last.MaterialAt(i);
		string pack = CurrentPackId();
		return new ModGarageLook { MaterialIndexes = indexes, TexturePack = pack == packAtOpen ? last.TexturePack : pack, SectionCount = count };
	}

	public static void Receive(ModGarageLook look)
	{
		if (look == null) return;
		pending = look;
		if (!applyRunning) MelonCoroutines.Start(RunApplies(generation));
	}

	private static IEnumerator RunApplies(int run)
	{
		applyRunning = true;
		while (pending != null && run == generation)
		{
			if (Deferred)
			{
				yield return null;
				continue;
			}
			var look = pending;
			pending = null;
			yield return Apply(look, run);
		}
		if (run == generation) applyRunning = false;
	}

	private static IEnumerator Apply(ModGarageLook look, int run)
	{
		var manager = GarageLookManager.Instance;
		var sections = manager?.GetSections();
		if (sections == null) yield break;
		int count = sections.Length;
		if (look.SectionCount > 0 && look.SectionCount != count)
		{
			SectionCountMismatch = $"stored {look.SectionCount}, local {count}";
			if (!sectionCountLogged) Log.Warn($"[GarageLook] the stored look has {look.SectionCount} sections, this garage {count}; applying the common sections.");
			sectionCountLogged = true;
		}
		int limit = look.SectionCount > 0 ? Math.Min(look.SectionCount, count) : count;
		var watch = Stopwatch.StartNew();
		int changed = 0;
		for (int i = 0; i < limit; i++)
		{
			var section = manager.GetSections()[i];
			int target = look.MaterialAt(i);
			if (section.SelectedMaterialIndex == target) continue;
			if (target >= (section.ProjectMaterials?.Length ?? 0))
			{
				Log.Warn($"[GarageLook] section {i} ({section.Name}) has no material {target}; left as it is.");
				continue;
			}
			Il2CppSystem.Collections.IEnumerator routine;
			try
			{
				routine = manager.UpdateMaterials(section.RendererData, i, target + 1, target == ModGarageLook.DefaultMaterial);
			}
			catch (Exception e)
			{
				Log.Warn($"[GarageLook] section {i}: UpdateMaterials threw {e.GetType().Name}: {e.Message.Split('\n')[0]}");
				continue;
			}
			yield return RunNative(routine, run);
			if (run != generation) yield break;
			changed++;
		}
		manager.Save();
		ApplyPack(look.TexturePack);
		LastApplied = look;
		LastApplyMs = watch.ElapsedMilliseconds;
		LastApplyChanged = changed;
		Applies++;
		Log.Info($"[GarageLook] applied {look.Describe()}: {changed} sections changed in {LastApplyMs} ms.");
	}

	private static IEnumerator RunNative(Il2CppSystem.Collections.IEnumerator root, int run)
	{
		var stack = new Stack<Il2CppSystem.Collections.IEnumerator>();
		stack.Push(root);
		while (stack.Count > 0 && run == generation)
		{
			bool more;
			Il2CppSystem.Collections.IEnumerator nested = null;
			try
			{
				more = stack.Peek().MoveNext();
				if (more) nested = stack.Peek().Current?.TryCast<Il2CppSystem.Collections.IEnumerator>();
			}
			catch (Exception e)
			{
				Log.Warn($"[GarageLook] the material coroutine threw {e.GetType().Name}: {e.Message.Split('\n')[0]}");
				yield break;
			}
			if (!more) stack.Pop();
			else if (nested != null) stack.Push(nested);
			else yield return null;
		}
	}

	private static void ApplyPack(string pack)
	{
		var packs = TexturePackManager.Instance;
		if (packs == null) return;
		if (pack == CurrentPackId()) return;
		if (pack == null) packs.SetDefaultTexturePack();
		else if (IsInstalled(packs, pack)) packs.SetActiveTexturePack(pack);
		else
		{
			if (CurrentPackId() != null) packs.SetDefaultTexturePack();
			Log.Info($"[GarageLook] texture pack {pack} is not installed; using the default textures.");
			if (noticedPack != pack) ModNotify.ShowToast($"{pack} is not installed; you see the default garage textures.");
			noticedPack = pack;
		}
		packs.Save();
	}

	private static bool IsInstalled(TexturePackManager packs, string id)
	{
		var list = packs.GetTexturePacks();
		for (int i = 0; list != null && i < list.Count; i++)
			if (list[i].ID == id) return true;
		return false;
	}

	public static string CurrentPackId()
	{
		var packs = TexturePackManager.Instance;
		if (packs == null) return null;
		string id = packs.GetCurrentTexturePack()?.ID;
		return string.IsNullOrEmpty(id) || id == packs.defaultTexturePack?.ID ? null : id;
	}
}
