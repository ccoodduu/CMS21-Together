using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
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
	public const string OccupiedMessage = "Take the engine off the stand first.";
	private const float BuildSeconds = 30f;

	private static readonly HashSet<ModToolId> pendingPuts = new HashSet<ModToolId>();
	private static bool creating;
	private static long builtUid;
	private static long builtExpected;
	private static float builtAt;
	private static bool subscribed;

	public static bool Building => builtUid != 0 && Time.realtimeSinceStartup - builtAt < BuildSeconds;

	public static void ForgetBuild()
	{
		creating = false;
		builtUid = 0;
	}

	[HarmonyPatch(typeof(CMS.UI.Windows.CreateEngineWindow), nameof(CMS.UI.Windows.CreateEngineWindow.CreateEngineAction))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool BeforeCreateEngine()
	{
		if (!ToolSync.CanSend) return true;
		if (!subscribed)
		{
			subscribed = true;
			ClientScene.LeavingScene += (from, to) => ForgetBuild();
		}
		var logic = ToolSync.Machine(ModToolId.EngineStand1) is EngineStandSync stand ? stand.Logic : null;
		if (logic != null && (HoldsEngine(logic) || !ToolSync.Mirror(ModToolId.EngineStand1).IsEmpty || ToolSync.IsApplyPending(ModToolId.EngineStand1) || Building))
		{
			Log.Info("[Tools] EngineStand1: building a new engine refused, the stand is not empty.");
			Car.Locks.LockMessages.Refuse(OccupiedMessage);
			return false;
		}
		creating = true;
		builtExpected = ToolSync.Mirror(ModToolId.EngineStand1).Uid;
		return true;
	}

	private static bool HoldsEngine(EngineStandLogic logic) => logic.engineGameObject != null || logic.GroupOnEngineStand != null && logic.GroupOnEngineStand.UID != 0;

	private static long EngineItemUid(GroupItem group) => group?.ItemList != null && group.ItemList.Count > 0 ? group.ItemList[0].UID : 0;

	[HarmonyPatch(typeof(CMS.UI.Windows.CreateEngineWindow), nameof(CMS.UI.Windows.CreateEngineWindow.CreateEngineAction))]
	[HarmonyPostfix]
	private static void AfterCreateEngine() => creating = false;

	[HarmonyPatch(typeof(EngineStandLogic), nameof(EngineStandLogic.SetGroupOnEngineStand))]
	[HarmonyPostfix]
	private static void AfterPutStarted(EngineStandLogic __instance, GroupItem groupItem)
	{
		var stand = EngineStandSync.For(__instance);
		ToolSync.TraceEvent($"{stand?.Tool} SetGroupOnEngineStand");
		if (stand == null || ToolSync.IsApplyingRemote(stand.Tool) || !ToolSync.CanSend) return;
		pendingPuts.Add(stand.Tool);
		if (!creating || stand.Tool != ModToolId.EngineStand1 || groupItem == null) return;
		builtUid = EngineItemUid(groupItem);
		builtAt = Time.realtimeSinceStartup;
		Log.Info($"[Tools] EngineStand1: building a new {groupItem.ID} (group {groupItem.UID}, engine {builtUid}).");
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
			bool created = builtUid != 0 && tool == ModToolId.EngineStand1 && EngineItemUid(stand.Logic.GroupOnEngineStand) == builtUid;
			if (builtUid != 0) Log.Debug($"[Tools] EngineStand1: built group {stand.AppliedUid}, engine {EngineItemUid(stand.Logic.GroupOnEngineStand)} (expected engine {builtUid}).");
			if (created) builtUid = 0;
			ToolSync.SendLocal(stand.CaptureForSend(), expectedUid: created ? builtExpected : (long?)null, created: created);
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
		if (builtUid != 0 && EngineItemUid(group) == builtUid) builtUid = 0;
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
