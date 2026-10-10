using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets;

[Serializable]
[NetworkPacket(PacketTypes.RaceStartRequest)]
public class RaceStartRequestPacket : INetworkData
{
	public GameScene Scene;
	public int Laps;
}

[Serializable]
[NetworkPacket(PacketTypes.RaceRefused)]
public class RaceRefusedPacket : INetworkData
{
	public GameScene Scene;
	public int Laps;
	public RaceRefusal Reason;
	public int RunningRaceId;
}

[Serializable]
[NetworkPacket(PacketTypes.RaceCountdown)]
public class RaceCountdownPacket : INetworkData
{
	public int RaceId;
	public GameScene Scene;
	public int Laps;
	public int StarterId;
	public List<int> Participants = new List<int>();
	public int StartInMs;
	// track-races D7: every racer of the race in grid order (index = box; 21+ share boxes from the back).
	[System.Runtime.Serialization.OptionalField] public List<int> Grid;
}

[Serializable]
[NetworkPacket(PacketTypes.RaceLap)]
public class RaceLapPacket : INetworkData
{
	public int RaceId;
	public int Lap;
	public long LapMs;
}

[Serializable]
[NetworkPacket(PacketTypes.RaceQuit)]
public class RaceQuitPacket : INetworkData
{
	public int RaceId;
}

[Serializable]
[NetworkPacket(PacketTypes.RaceResult)]
public class RaceResultPacket : INetworkData
{
	public ModRaceResult Result;
}
