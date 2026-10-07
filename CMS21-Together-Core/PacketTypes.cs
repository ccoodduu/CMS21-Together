using System;

namespace CMS21_Together_Core;

[Serializable]
public enum PacketTypes
{
	Connect,
	Heartbeat,
	Disconnect,
	
	// World State Sync
	AskForSync,
	WorldState, // Gamemode, Money, Lvl/Exp
	GarageState, // Garage Upgrade, Garage Customization
	CarData, // Individual carInfo (sent 1 time for every carLoader)
	InventoryData, // Item & GroupItem
	SyncEnd, // Signal that initial load is complete
	
	Movement, // Position/Velocity & Rotation
	UpgradeRequest, // Garage/Skill upgrade request
	StatsAction, // Exp and Scrap deltas
	
	InventoryItemAction,
	InventoryGroupItemAction,
	WarehouseAction,
	ShopAction,
	ItemsExchange,
	RegisterModItem,
	
	// Car Packets
	CarSpawnRequest,
	CarSpawnResponse,
	CarSpawnDelete,
	CarSpawnRejected,
	CarPartsChange,
	CarPartsChangeResult,

	SyncBegin,
	SyncAck,

	PlayerPresence,
	PlayerRoster,

	ServerInfo,

	CarPartClaim,
	CarPartClaimUpdate,
	CarPartsSnapshot,
	CarPartsResyncRequest,
	CarSpawnAck,

	LifterActionRequest,
	LifterState,
	CarPlaceChangeRequest,
	CarPlaceChanged,
	CarParkRequest,
	CarUnparkRequest,
	ParkingMoveRequest,
	ParkingSlotUpdate,
	ParkingState,
	ParkingLevelUnlockRequest,
	ParkingResyncRequest,
	CarParkResult,

	StateDigestRequest,
	StateDigest,
	StateDetailRequest,
	StateDetail,
	DesyncNotice,

	JobsState,
	OrderGeneratorRole,
	OrderGenerated,
	OrderAdded,
	OrderAction,
	OrderActionResult,
	JobStarted,
	JobProgress,
	JobEndRequest,
	JobRemoved,

	CarDetailsUpdate,
	CarDetailsRequest,

	CarAwayRequest,
	CarAwayUpdate,
	CarAwayRelease,
	TestDriveResult,
	TestDriveResultAck,
	PlayerPings,
	KickRequest,
	EconomyRequest,
	EconomyResult,
	ToolSlotUpdate,
	ToolSlotRejected,
	ToolSlotProperty,
	ToolPartChange,
	ToolPosition,
	ToolsState,
	ToolClaim,
	ToolClaimUpdate,
	ToolPartChangeResult,

	ToolAction,
	PlayerRestore,
	BugReportRequest,
	BugReportCollect,
	BugReportResult,
	PlayerActivity,
	CarDriveStart,
	CarDriveState,
	CarDriveStop
}