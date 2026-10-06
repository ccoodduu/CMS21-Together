using System;
using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Data.GameType;

public enum ModToolId
{
	TireChanger,
	WheelBalancer,
	SpringClamp,
	EngineStand1,
	EngineStand2,
	BrakeLathe,
	BatteryCharger
}

public enum ToolProperty
{
	Angle,
	Active
}

[Serializable]
public class ToolSlotState
{
	public ModToolId Tool;
	public ModItem Item;
	public ModGroupItem Group;
	public bool Mounting;
	public bool Active;
	public bool Balanced;
	public float Angle;
	public Dictionary<string, CarSubPartUpdatePacket> Parts = new Dictionary<string, CarSubPartUpdatePacket>();

	[JsonIgnore] public long Uid => Group?.UID ?? Item?.UID ?? 0;

	[JsonIgnore] public bool IsEmpty => Item == null && Group == null;

	public static ToolSlotState Empty(ModToolId tool) => new ToolSlotState { Tool = tool };

	public IEnumerable<long> Uids()
	{
		if (Item != null) yield return Item.UID;
		if (Group == null) yield break;
		yield return Group.UID;
		if (Group.ItemList == null) yield break;
		foreach (var item in Group.ItemList)
			if (item != null) yield return item.UID;
	}
}
