using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using HarmonyLib;
using UnityEngine;

namespace CMS21Together.Logic.Tools;

// The stand builds its own GroupItem with a new UID on every client (spike), so the local engine is tracked by the
// UID the server holds (AppliedUid), not by the local group's UID.
public sealed class EngineStandSync : ToolMachine
{
	private const string SecondStandName = "Engine_stand_2";

	private readonly ModToolId tool;

	public EngineStandSync(ModToolId tool)
	{
		this.tool = tool;
	}

	public long AppliedUid { get; set; }

	public PartRegistry Registry { get; private set; }

	public Dictionary<string, CarSubPartUpdatePacket> Known { get; } = new Dictionary<string, CarSubPartUpdatePacket>();

	public override ModToolId Tool => tool;

	public EngineStandLogic Logic => tool == ModToolId.EngineStand1 ? ToolsManager.Get()?.EngineStandLogic : FindSecond();

	public override bool Present => Logic != null;

	public static EngineStandSync For(EngineStandLogic logic)
	{
		if (logic == null) return null;
		foreach (var machine in ToolSync.Machines)
			if (machine is EngineStandSync stand && stand.Logic != null && stand.Logic.Pointer == logic.Pointer) return stand;
		return null;
	}

	private static EngineStandLogic FindSecond()
	{
		var second = GameObject.Find(SecondStandName)?.GetComponent<EngineStandLogic>();
		var first = ToolsManager.Get()?.EngineStandLogic;
		return second != null && (first == null || first.Pointer != second.Pointer) ? second : null;
	}

	public override ToolSlotState ReadLocal()
	{
		var logic = Logic;
		var group = logic?.GroupOnEngineStand?.ToModGroupItem();
		if (group != null && AppliedUid != 0) group.UID = AppliedUid;
		return new ToolSlotState { Tool = tool, Group = group, Angle = logic?.EngineStandAngle ?? 0f };
	}

	public override IEnumerator Put(ToolSlotState state)
	{
		var logic = Logic;
		var build = Step(logic.SetGroupOnEngineStand(state.Group.ToGameGroupItem(), false));
		while (build.MoveNext()) yield return build.Current;
		AppliedUid = state.Uid;
		Rebuild();
		EngineStandParts.ApplyRecords(this, state.Parts.Values);
	}

	public override IEnumerator Clear()
	{
		Logic.ClearEngineStand();
		Forget();
		yield break;
	}

	public override void ApplyFlags(ToolSlotState state)
	{
		var logic = Logic;
		if (Mathf.Abs(Mathf.DeltaAngle(logic.EngineStandAngle, state.Angle)) > 0.5f) logic.SetEngineStandAngle(state.Angle);
		if (!state.IsEmpty && AppliedUid == state.Uid) EngineStandParts.ApplyRecords(this, state.Parts.Values);
	}

	public override void ApplyProperty(ToolProperty property, float value)
	{
		if (property == ToolProperty.Angle) Logic.SetEngineStandAngle(value);
	}

	public void Rebuild()
	{
		var engine = Logic?.engineGameObject;
		Registry = engine == null ? null : PartRegistry.Build(engine.transform);
		Known.Clear();
		if (Registry == null) return;
		foreach (string key in Registry.SubKeys) Known[key] = PartRecords.Capture(Registry.SubPath(key), Registry.Sub(key));
	}

	public void Forget()
	{
		AppliedUid = 0;
		Registry = null;
		Known.Clear();
	}

	public ToolSlotState CaptureForSend()
	{
		var state = ReadLocal();
		foreach (var pair in Known) state.Parts[pair.Key] = pair.Value;
		return state;
	}
}

[HarmonyPatch]
public static class EngineStandHooks
{
	private static readonly HashSet<ModToolId> pendingPuts = new HashSet<ModToolId>();

	[HarmonyPatch(typeof(EngineStandLogic), nameof(EngineStandLogic.SetGroupOnEngineStand))]
	[HarmonyPostfix]
	private static void AfterPutStarted(EngineStandLogic __instance)
	{
		var stand = EngineStandSync.For(__instance);
		ToolSync.TraceEvent($"{stand?.Tool} SetGroupOnEngineStand");
		if (stand == null || ToolSync.IsApplyingRemote(stand.Tool) || !ToolSync.CanSend) return;
		pendingPuts.Add(stand.Tool);
	}

	[HarmonyPatch(typeof(EngineStandLogic._SetGroupOnEngineStand_d__8), nameof(EngineStandLogic._SetGroupOnEngineStand_d__8.MoveNext))]
	[HarmonyPostfix]
	private static void AfterBuildStep(bool __result)
	{
		if (__result || pendingPuts.Count == 0) return;
		foreach (var tool in new List<ModToolId>(pendingPuts))
		{
			var stand = (EngineStandSync)ToolSync.Machine(tool);
			if (ToolSync.IsApplyingRemote(tool) || stand.Logic?.GroupOnEngineStand == null) continue;
			pendingPuts.Remove(tool);
			stand.AppliedUid = stand.Logic.GroupOnEngineStand.UID;
			stand.Rebuild();
			ToolSync.SendLocal(stand.CaptureForSend());
		}
	}

	[HarmonyPatch(typeof(EngineStandLogic), nameof(EngineStandLogic.ClearEngineStand))]
	[HarmonyPrefix]
	private static void BeforeClear(EngineStandLogic __instance)
	{
		var stand = EngineStandSync.For(__instance);
		ToolSync.TraceEvent($"{stand?.Tool} ClearEngineStand");
		if (stand == null || ToolSync.IsApplyingRemote(stand.Tool)) return;
		var group = __instance.GroupOnEngineStand;
		if (group == null || !ToolSync.CanSend || !InInventory(group))
		{
			stand.Forget();
			return;
		}
		Client.Instance.Send(new InventoryGroupItemActionPacket { Action = ItemActionType.Add, GroupItem = group.ToModGroupItem() });
		stand.Forget();
		ToolSync.SendLocal(ToolSlotState.Empty(stand.Tool), new[] { group.UID });
	}

	private static bool InInventory(GroupItem group)
	{
		foreach (var held in Singleton<GameManager>.Instance.Inventory.groups)
			if (held.Pointer == group.Pointer) return true;
		return false;
	}

	[HarmonyPatch(typeof(EngineStandLogic), nameof(EngineStandLogic.IncreaseEngineStandAngle))]
	[HarmonyPrefix]
	private static void BeforeRotate(EngineStandLogic __instance, out float __state) => __state = __instance.EngineStandAngle;

	[HarmonyPatch(typeof(EngineStandLogic), nameof(EngineStandLogic.IncreaseEngineStandAngle))]
	[HarmonyPostfix]
	private static void AfterRotate(EngineStandLogic __instance, float __state)
	{
		var stand = EngineStandSync.For(__instance);
		if (stand != null && Mathf.Abs(__instance.EngineStandAngle - __state) > 0.01f)
			ToolSync.SendProperty(stand.Tool, ToolProperty.Angle, __instance.EngineStandAngle);
	}
}
