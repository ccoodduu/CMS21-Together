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

	public static void Reset()
	{
		IsWorldStateSynced = false;
		IsGarageStateSynced = false;
		IsInventorySynced = false;
		IsInitialSyncFinished = false;
		IsServerUpdating = false;
		SyncTracker.Reset();
		PresenceManager.Clear();
		ClientScene.ClearPending();
		CarPartsSync.Reset();
		PartChangeTracker.Reset();
		PartClaims.Reset();
	}

	public static void Update()
	{
		if (!IsInitialSyncFinished) return;
		
		Movement.UpdateMovement();
	}
}
