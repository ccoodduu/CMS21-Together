using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Tools;

// sync-workshop-machines D9. A position the local garage cannot take yet (CanMove false while a car is still being
// placed) stays in the mirror and is retried.
[HarmonyPatch]
public static class ToolPositionSync
{
	private const float RetrySeconds = 2f;

	public static readonly IOSpecialType[] Movable =
	{
		IOSpecialType.Welder, IOSpecialType.InteriorDetailingToolkit, IOSpecialType.Oilbin, IOSpecialType.EngineCrane,
		IOSpecialType.HeadlampAlignmentSystem, IOSpecialType.WindowTint,
	};

	private static readonly HashSet<int> waiting = new HashSet<int>();
	private static bool applying;
	private static bool retrying;

	public static void Reset() => waiting.Clear();

	[HarmonyPatch(typeof(ToolsMoveManager), nameof(ToolsMoveManager.MoveTo))]
	[HarmonyPrefix]
	private static void BeforeMove(ToolsMoveManager __instance, IOSpecialType tool, CarPlace place)
	{
		if (applying || !Movable.Contains(tool) || !__instance.CanMove(tool, place)) return;
		waiting.Remove((int)tool);
		ToolSync.SendPosition((int)tool, (int)place);
	}

	[HarmonyPatch(typeof(ToolsMoveManager), nameof(ToolsMoveManager.SetOnDefaultPosition))]
	[HarmonyPrefix]
	private static void BeforeDefault(IOSpecialType tool)
	{
		if (applying || !Movable.Contains(tool)) return;
		waiting.Remove((int)tool);
		ToolSync.SendPosition((int)tool, ToolPositionPacket.DefaultPosition);
	}

	public static void OnRemote(ToolPositionPacket packet)
	{
		ToolSync.MirrorPosition(packet);
		Apply(packet.IoSpecialType, packet.CarPlace);
	}

	public static void ApplyAll()
	{
		foreach (var tool in Movable)
			Apply((int)tool, ToolSync.Positions.TryGetValue((int)tool, out int place) ? place : ToolPositionPacket.DefaultPosition);
	}

	private static bool Apply(int type, int place)
	{
		var manager = ToolsMoveManager.Get();
		if (manager == null) return false;
		var tool = (IOSpecialType)type;
		applying = true;
		try
		{
			if (place == ToolPositionPacket.DefaultPosition)
			{
				if (!manager.IsOnDefaultPosition(tool)) manager.SetOnDefaultPosition(tool);
			}
			else if (manager.CanMove(tool, (CarPlace)place))
			{
				manager.MoveTo(tool, (CarPlace)place, false);
			}
			else
			{
				Log.Debug($"[Tools] Tool {tool} cannot move to {(CarPlace)place} yet.");
				waiting.Add(type);
				if (!retrying)
				{
					retrying = true;
					MelonCoroutines.Start(Retry());
				}
				return false;
			}
		}
		finally
		{
			applying = false;
		}
		waiting.Remove(type);
		return true;
	}

	private static IEnumerator Retry()
	{
		while (waiting.Count > 0)
		{
			yield return new WaitForSeconds(RetrySeconds);
			if (!ToolSync.CanSend) continue;
			foreach (int type in waiting.ToList())
				Apply(type, ToolSync.Positions.TryGetValue(type, out int place) ? place : ToolPositionPacket.DefaultPosition);
		}
		retrying = false;
	}
}
