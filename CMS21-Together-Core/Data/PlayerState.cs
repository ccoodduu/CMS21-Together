using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Core.Data;

public class PlayerState
{
	public Dictionary<int, PlayerPresenceRecord> Records = new Dictionary<int, PlayerPresenceRecord>();
}

[Serializable]
public class PlayerPresenceRecord
{
	public const int NoCar = -1;

	public int PlayerId;
	public string Username;
	public GameScene Scene;
	public int SeatCarLoaderId = NoCar;
	public bool SeatLeft;
	public int EngineCarLoaderId = NoCar;
	public bool EngineRunning;
	public float EngineRpm;
	public MovementPacket LastMovement;

	public PlayerPresenceRecord Copy() => (PlayerPresenceRecord)MemberwiseClone();
}
