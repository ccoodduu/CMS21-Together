# Proposal

## Why

In a co-op session each player's garage machines are still local: a wheel put on the tire changer, an engine on
the stand or a moved welder exists only for the player who did it, and the item is lost or duplicated when someone
else uses the machine. The old 0.4.x mod synced the tools with fire-and-forget relays and `listen` flags, which
caused the reported duplicates and desyncs (upstream #87, the duplicate ADDs FixForTogether blocks). With parts
(`sync-car-parts`) and car details (`sync-car-details`) on the server, the tools are the next missing piece.

## What Changes

- One shared pattern for all machines: the server owns a **slot state** per item-holding machine (what it holds,
  plus a few machine flags), accepts changes with a compare-and-set on the held item's UID, and rejects the loser
  of a race. Actions that only change a car send their **result** through the existing car packets plus a
  relay-only **action event** so other players see the effect.
- Slot machines: tire changer, wheel balancer, spring clamp, engine stand 1 and 2 (engine, rotation angle, parts
  removed from the engine on the stand), brake lathe, battery charger.
- Car-effect tools: welder, car wash, interior detailing (portable and stationary), oil bin, engine crane
  (engine out / in / swap), paint shop (car paint). Results go through `sync-car-parts` / `sync-car-details`.
- Item processing: repair table and paint shop part painting update the item in the shared inventory in place.
- Positions of movable tools (`ToolsMoveManager`): welder, interior detailing kit, oil bin, engine crane, headlamp
  aligner, window tinting kit.
- Late join: the server sends all slot states and tool positions during initial sync. A joining client first
  clears what its own save put on the machines.
- Game hooks (all verified in the decompiled stubs): `TireChangerLogic.SetGroupOnTireChanger`/`Clear`,
  `WheelBalancerLogic.SetGroupOnWheelBalancer`/`FinishBalance`/`Clear`,
  `SpringClampLogic.SetGroupOnSpringClamp`/`ClearSpringClamp`,
  `EngineStandLogic.SetGroupOnEngineStand`/`ClearEngineStand`/`IncreaseEngineStandAngle`/`SetEngineStandAngle`,
  `BrakeLatheLogic.SetItem`/`Clear`, `BatteryChargerLogic.SetItemOnBatteryCharger`/`ClearBatteryCharger`/`BatteryChargerActivate`,
  `WelderLogic`/`CarWashLogic`/`InteriorDetailingToolkitLogic.DoWorkAnim`,
  `ToolsMoveManager.MoveTo`/`SetOnDefaultPosition`/`UseInteriorDetailingToolkitStationary`, `CarLoader.UseOilbin`/`UseEngineCrane`,
  `NotificationCenter.InsertEngineToCar`/`TakeOffEngineFromStand`, `PaintshopManager.MakeCarPaintEffects`/`MakePartPaintEffects`,
  `RepairPartWindow.UpdateItemCondition`.
- New packets: `ToolSlotUpdate`, `ToolSlotRejected`, `ToolSlotProperty`, `ToolPartUpdate`, `ToolPosition`,
  `ToolAction`, `ToolsState`. Changed: `ItemActionType` gains `Update` (replace an inventory item by UID) unless
  `sync-car-parts` already adds it. Inventory ADDs are made idempotent by UID on server and client.

## Capabilities

### New Capabilities
- `workshop-tools-sync`: shared, server-authoritative state of the garage machines (held item, machine flags,
  tool positions), visible machine actions, and the results of machine work on items and cars, including late join.

### Modified Capabilities
- None. There are no main specs yet. The car-state and inventory capabilities come from `sync-car-parts` and
  `sync-car-details`, which are being written alongside this change.

## Impact

- Core: new `ToolPackets.cs`, `ModToolId`, `ToolSlotState`, `ToolsState` in `ModGameState`, new `PacketTypes`
  entries, `ItemActionType.Update`.
- Server: new `Network/Handlers/ToolHandlers.cs`; `AuthHandlers.OnAskForSync` sends `ToolsState` before
  `SyncEnd`; inventory handlers ignore duplicate UIDs. Tool state is saved with `ModGameState`, so the save format
  grows (versioning belongs to `session-persistence-and-rejoin`).
- Client: new `Logic/Tools/*` (one small file per machine on a shared `ToolSync` base), new `Network/Handlers/ToolHandlers.cs`,
  a UID-scoped inventory guard in `InventoryHook`.
- Harness: `tools/TestHarness/Features/ToolsCommands.cs`, a `tools` section in `StateDump`, scenarios
  `tools-slots`, `tools-race`, `tools-car-effects`, `tools-latejoin`.
- Depends on `sync-car-parts` (part identity, sub-part DTO and resolver, shared inventory flow) and
  `sync-car-details` (wash/dirt, fluids, paint packets), and on `sync-car-placement-and-lifts` only for cars that
  are moved while a tool is attached to their place.
