using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.Enum;

namespace CMS21_Together_Core.Data.GameType;

[Serializable]
public class ModRaceResult
{
	public int RaceId;
	public GameScene Scene;
	public int Laps;
	public long StartedUtcMs;
	public List<ModRaceEntry> Order = new List<ModRaceEntry>();
}

[Serializable]
public class ModRaceEntry
{
	public int PlayerId;
	public string PlayerName;
	public int Laps;
	public long TotalMs;
	public long BestLapMs;
	public bool Dnf;
	public RaceDnfReason DnfReason;
}

public enum RaceDnfReason
{
	None,
	Quit,
	LeftTrack,
	Disconnected,
	Timeout,
	ServerStopped
}

public enum RaceRefusal
{
	None,
	NotRaceTrack,
	RaceRunning,
	NotDriving,
	Passenger,
	LapsOutOfRange
}
