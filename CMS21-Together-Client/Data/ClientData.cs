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
		Logic.Car.Placement.CarPlacementSync.Reset();
		Logic.Car.CarSpawnManager.Reset();
		Logic.Jobs.JobsSync.Reset();
		Logic.Jobs.JobStats.Reset();
		Logic.Achievements.SharedAchievements.Reset();
		Logic.ShopList.ShopListSync.Reset();
		Logic.Car.Details.CarDetailsSync.Reset();
		PartChangeTracker.Reset();
		PartBlockCheck.Reset();
		PartClaims.Reset();
		Logic.Car.Locks.CarLockMirror.Reset();
		Logic.Car.Locks.LockGate.Reset();
		Logic.Car.Locks.LockLifecycle.Reset();
		Logic.Car.Locks.LockPrefetch.Reset();
		Logic.Car.Locks.LockSelection.Reset();
		Logic.Car.Locks.LockChooser.Reset();
		Logic.Car.Locks.LockFluidHooks.Reset();
		Logic.Tools.CarTools.OilBinHooks.Reset();
		Logic.Car.Away.CarAwaySync.Reset();
		PartTransactions.Reset();
		Network.Handlers.InventoryHandlers.ResetHeld();
		Logic.Economy.EconomyScope.Reset();
		Logic.Economy.EconomyRequests.Reset();
		Logic.Economy.CarPurchaseSync.Reset();
		Logic.Tools.ToolSync.Reset();
		Logic.Garage.GarageLookSync.Reset();
		Logic.Outdoor.OutdoorSession.Reset();
		Logic.Outdoor.CatalogReporter.Reset();
		Logic.Visuals.VisualScope.Reset();
		Logic.Visuals.FluidReplay.Reset();
		Logic.Visuals.PartGhosts.Reset();
		Logic.Visuals.ActivityCapture.Reset();
		Logic.Visuals.RemoteActivity.Reset();
		Logic.Pings.CoopPings.Reset();
		Logic.Driving.DriveCapture.Reset();
		Logic.Driving.RemoteCars.Reset();
	}

	public static void Update()
	{
		if (!IsInitialSyncFinished) return;
		
		Movement.UpdateMovement();
		SeatEngine.Update();
	}
}
