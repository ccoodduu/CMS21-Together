# Proposal

## Why

In a co-op session each player's garage machines are still local: a wheel put on the tire changer, an engine on
the stand or a moved welder exists only for the player who did it, and the item is lost or duplicated when someone
else uses the machine. The old 0.4.x mod synced the tools with fire-and-forget relays and `listen` flags, which
caused the reported duplicates and desyncs (upstream #87, the duplicate ADDs FixForTogether blocks). With the
shared inventory and parts (`sync-car-parts`) on the server, the machines are the next missing piece.

This change is the first half of the former `sync-workshop-tools` (split by user decision 2026-10-05). The second
half, `sync-workshop-car-tools`, covers the tools that act on a car and builds on this change's framework.

## What Changes

- One shared pattern for all machines: the server owns a **slot state** per item-holding machine (what it holds,
  plus a few machine flags), accepts changes with a compare-and-set on the held item's UID, and rejects the loser
  of a race.
- Slot machines: tire changer, wheel balancer (the minigame stays, played only by the acting player, and the
  balancer is **locked** for everyone else while it is open: a server reservation, user decision 2026-10-05),
  spring clamp, engine stand 1 and 2 (engine, rotation angle, parts removed from the engine on the stand), brake
  lathe, battery charger.
- Item processing: repair table and paint shop part painting update the item in the shared inventory in place.
- Positions of movable tools (`ToolsMoveManager`): welder, interior detailing kit, oil bin, engine crane, headlamp
  aligner, window tinting kit.
- Late join: the server sends all slot states, machine reservations and tool positions as the `workshop-tools`
  snapshot section (SyncOrder 300). A joining client first clears what its own save put on the machines.
- Game hooks (all verified in the decompiled stubs): `TireChangerLogic.SetGroupOnTireChanger(GroupItem,bool,bool)`/`Clear()`,
  `WheelBalancerLogic.SetGroupOnWheelBalancer`/`FinishBalance` (fallback `FinishBalanceInternal`)/`Clear()`,
  the method that opens `WheelBalanceWindow` (found by spike 1.3) and `WheelBalanceWindow.CancelAction` (hooked for
  the release, also called to close a window that lost the reservation),
  `SpringClampLogic.SetGroupOnSpringClamp`/`ClearSpringClamp`,
  `EngineStandLogic.SetGroupOnEngineStand`/`SetEngineOnEngineStand`/`ClearEngineStand`/`IncreaseEngineStandAngle`/`SetEngineStandAngle`,
  `BrakeLatheLogic.SetItem`/`Clear`, `BatteryChargerLogic.SetItemOnBatteryCharger`/`ClearBatteryCharger`/`BatteryChargerActivate`,
  `ToolsMoveManager.MoveTo`/`SetOnDefaultPosition`, `NotificationCenter.TakeOffEngineFromStand`,
  `PaintshopManager.MakePartPaintEffects`, `RepairPartWindow.UpdateItemCondition(PartInfo,bool)`.
- New packets: `ToolSlotUpdate`, `ToolSlotRejected`, `ToolSlotProperty`, `ToolPartChange`, `ToolPosition`,
  `ToolsState`, `ToolClaim`, `ToolClaimUpdate`. Changed: `ItemActionType` gains `Update` (replace an inventory item
  by UID). The UID-idempotent inventory ADD comes from `sync-car-parts`, which lands first.

## Capabilities

### New Capabilities
- `workshop-machines-sync`: shared, server-authoritative state of the garage machines (held item, machine flags,
  machine reservation, tool positions) and the results of machine work on items, including late join.

### Modified Capabilities
- None. There are no main specs yet.

## Impact

- Core: new `ToolPackets.cs`, `ModToolId`, `ToolSlotState`, `ToolsState` in `ModGameState`, new `PacketTypes`
  entries, `ItemActionType.Update`.
- Server: new `Network/Handlers/ToolHandlers.cs` (runs under `GameDataManager.StateLock`), new
  `Data/Persistence/WorkshopToolsSection.cs` (`ISaveSection` + `ISnapshotProvider`, key `workshop-tools`);
  inventory handlers handle `Update`.
- Client: new `Logic/Tools/*` (one small file per machine on a shared `ToolSync` base), new
  `Network/Handlers/ToolHandlers.cs`, a UID-scoped inventory guard in `InventoryHook`.
- Harness: `tools/TestHarness/Features/ToolsCommands.cs`, `tools`/`toolPositions` sections in `StateDump`,
  `Wait-HarnessDumpsEqual` in `HarnessClient.psm1` (built on `sync-players-and-scenes`' `Wait-HarnessDump`),
  scenarios `tools-slots`, `tools-race`, `tools-latejoin`.
- Depends on `session-persistence-and-rejoin` task groups 1–2 (contract, state lock, sections, `SyncTracker`,
  harness server commands), `sync-car-parts` (part keys, `PartTransaction`/`InventoryDelta` for a non-car root,
  idempotent ADD) and `sync-players-and-scenes` part 1 (`ClientScene.GarageBound`, `PresenceEvents`, `travel`,
  `Wait-HarnessDump`). It does not need `sync-car-details`, so it can land right after `sync-car-parts`.
