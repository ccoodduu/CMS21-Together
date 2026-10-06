using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;

namespace CMS21Together.Data;

public static class ClientData
{
	// Global Game States
	public static bool IsWorldStateSynced { get; set; }
	public static bool IsGarageStateSynced { get; set; }
	public static bool IsInventorySynced { get; set; }
	public static bool IsInitialSyncFinished { get; set; }
	public static bool IsServerUpdating { get; set; }
	public static ServerInfoPacket ServerInfo { get; set; }
	public static Dictionary<int, int> PlayerPings { get; set; } = new Dictionary<int, int>();

	public static void Reset()
	{
		PlayerPings = new Dictionary<int, int>();
		IsWorldStateSynced = false;
		IsGarageStateSynced = false;
		IsInventorySynced = false;
		IsInitialSyncFinished = false;
		IsServerUpdating = false;
		SyncTracker.Reset();
		PresenceManager.Clear();
		SpawnPlacement.ClearRestore();
		SeatEngine.Reset();
		ClientScene.ClearPending();
		CarPartsSync.Reset();
		Logic.Car.Placement.ParkingSync.Reset();
		Logic.Jobs.JobsSync.Reset();
		Logic.Car.Details.CarDetailsSync.Reset();
		PartChangeTracker.Reset();
		PartClaims.Reset();
		Logic.Car.Away.CarAwaySync.Reset();
		PartTransactions.Reset();
		Logic.Economy.EconomyScope.Reset();
		Logic.Economy.EconomyRequests.Reset();
		Logic.Tools.ToolSync.Reset();
	}

	public static void Update()
	{
		if (!IsInitialSyncFinished) return;
		
		Movement.UpdateMovement();
		SeatEngine.Update();
	}
}
