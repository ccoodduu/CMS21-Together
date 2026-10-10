using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.AskForSync)]
public class AskForSync : INetworkData { }

[Serializable]
[NetworkPacket(PacketTypes.WorldState)]
public class WorldState : INetworkData
{
	public bool updateGamemode;
	public Gamemode Gamemode;
	public int Money;
	public int Level;
	public int Exp;
	public int Scraps;
	[OptionalField] public int Barns;
	[OptionalField] public ModTrackRecord GroupBestLap;
	[OptionalField] public ModTrackRecord GroupTopSpeed;
}

[Serializable]
[NetworkPacket(PacketTypes.GarageState)]
public class GarageState : INetworkData
{
	public Dictionary<string, bool[]> GarageUpgradeLevels = new Dictionary<string, bool[]>();
	public Dictionary<string, bool[]> PlayerUpgradeLevels = new Dictionary<string, bool[]>();
	public int AvailablePoints;
	[OptionalField] public ModGarageLook Look = new ModGarageLook();
}

[Serializable]
[NetworkPacket(PacketTypes.SyncBegin)]
public class SyncBegin : INetworkData
{
	public int snapshotId;
}

[Serializable]
[NetworkPacket(PacketTypes.SyncEnd)]
public class SyncEnd : INetworkData
{
	public int snapshotId;
	public Dictionary<string, int> Items = new Dictionary<string, int>();
}

[Serializable]
[NetworkPacket(PacketTypes.SyncAck)]
public class SyncAck : INetworkData
{
	public int snapshotId;
}