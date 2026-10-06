using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;

namespace CMS21_Together_Core.Network.Packets;


[Serializable]
[NetworkPacket(PacketTypes.Movement)]
public class MovementPacket : INetworkData
{
	public int SenderId;
	public GameScene Scene;
	public Vector3Serializable Position;
	public Vector3Serializable Velocity;
	public QuaternionSerializable Rotation;
	
	public float CameraPitch;
	public bool IsGrounded;
	public bool IsCrouching;
	public bool IsRunning;
}

[Serializable]
[NetworkPacket(PacketTypes.PlayerPresence)]
public class PlayerPresencePacket : INetworkData
{
	public PlayerPresenceRecord Record;
}

[Serializable]
[NetworkPacket(PacketTypes.PlayerRoster)]
public class PlayerRosterPacket : INetworkData
{
	public List<PlayerPresenceRecord> Records = new List<PlayerPresenceRecord>();
}
