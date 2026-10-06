using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Data;

public class ModGameState
{
	public WorldState WorldState = new WorldState();
	public GarageState GarageState = new GarageState();
	public InventoryState InventoryState = new InventoryState();
	public CarState CarState = new CarState();
	public PlacementState PlacementState = new PlacementState();
	public JobsState JobsState = new JobsState();
	public ToolsState ToolsState = new ToolsState();
	
	[NonSerialized] public PlayerState PlayerState = new PlayerState();
}

public class PlacementState
{
	public Dictionary<int, int> Lifters = new Dictionary<int, int>();
	public ParkingLot Parking = new ParkingLot();
}

public class ParkingLot
{
	public int UnlockedLevels;
	public Dictionary<int, ParkedCar> Slots = new Dictionary<int, ParkedCar>();
}

public class ToolsState
{
	public Dictionary<ModToolId, ToolSlotState> Slots = new Dictionary<ModToolId, ToolSlotState>();

	// Key: IOSpecialType; value: CarPlace or ToolPositionPacket.DefaultPosition.
	public Dictionary<int, int> Positions = new Dictionary<int, int>();
}

public class CarState
{
	public int NextSpawnSeq = 1;

	// Key: CarLoaderID (e.g. 0 to 4).
	public Dictionary<int, CarLoaderEntry> LoadedCars = new Dictionary<int, CarLoaderEntry>();
	public Dictionary<int, ModCarDetails> Details = new Dictionary<int, ModCarDetails>();
}

public class CarLoaderEntry
{
	public const int NoClient = 0;

	public CarSpawnResponsePacket Spawn;
	public int SpawnSeq;
	public int Revision;
	public bool HasBaseline;
	public string EngineSwap;
	public ParkedCar FromParking;

	[JsonIgnore] public int SpawnedBy = NoClient;

	// Last known state of each body part (key: PartIndex) and sub-part (key: CarSubPartIdentity.BuildKey),
	// used both to broadcast live updates and to replay the full car to a joining client.
	public Dictionary<int, CarBodyPartUpdatePacket> BodyParts = new Dictionary<int, CarBodyPartUpdatePacket>();
	public Dictionary<string, CarSubPartUpdatePacket> SubParts = new Dictionary<string, CarSubPartUpdatePacket>();
}
