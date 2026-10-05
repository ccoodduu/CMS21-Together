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
  of a race. Actions that only change a car trigger the senders of `sync-car-parts` / `sync-car-details` plus a
  relay-only **action event** so other players see the effect.
- Slot machines: tire changer, wheel balancer (the minigame stays, played only by the acting player), spring clamp,
  engine stand 1 and 2 (engine, rotation angle, parts removed from the engine on the stand), brake lathe, battery charger.
- Car-effect tools: welder, car wash, interior detailing (portable and stationary), oil bin, engine crane (effect and
  engine swap trigger), paint shop (car paint), dyno (result trigger).
- Item processing: repair table and paint shop part painting update the item in the shared inventory in place.
- Positions of movable tools (`ToolsMoveManager`): welder, interior detailing kit, oil bin, engine crane, headlamp
  aligner, window tinting kit.
- Late join: the server sends all slot states and tool positions as the `workshop-tools` snapshot section
  (SyncOrder 300). A joining client first clears what its own save put on the machines.
- Game hooks (all verified in the decompiled stubs): `TireChangerLogic.SetGroupOnTireChanger(GroupItem,bool,bool)`/`Clear()`,
  `WheelBalancerLogic.SetGroupOnWheelBalancer`/`FinishBalance` (fallback `FinishBalanceInternal`)/`Clear()`,
  `WheelBalanceWindow.CancelAction` (called, not hooked),
  `SpringClampLogic.SetGroupOnSpringClamp`/`ClearSpringClamp`,
  `EngineStandLogic.SetGroupOnEngineStand`/`SetEngineOnEngineStand`/`ClearEngineStand`/`IncreaseEngineStandAngle`/`SetEngineStandAngle`,
  `BrakeLatheLogic.SetItem`/`Clear`, `BatteryChargerLogic.SetItemOnBatteryCharger`/`ClearBatteryCharger`/`BatteryChargerActivate`,
  `WelderLogic`/`CarWashLogic`/`InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)`,
  `ToolsMoveManager.MoveTo`/`SetOnDefaultPosition`/`UseInteriorDetailingToolkitStationary`,
  `CarLoader.UseOilbin`/`UseEngineCrane`/`SwapEngine`/`MeasurePower`, `DynoManager.CloseDyno`,
  `NotificationCenter.InsertEngineToCar`/`TakeOffEngineFromStand`, `PaintshopManager.MakeCarPaintEffects`/`MakePartPaintEffects`,
  `RepairPartWindow.UpdateItemCondition(PartInfo,bool)`.
- New packets: `ToolSlotUpdate`, `ToolSlotRejected`, `ToolSlotProperty`, `ToolPartChange`, `ToolPosition`,
  `ToolAction`, `ToolsState`. Changed: `ItemActionType` gains `Update` (replace an inventory item by UID), and
  inventory ADDs become idempotent by UID on server and client (both owned here; `sync-car-parts` does not add them).

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
- Server: new `Network/Handlers/ToolHandlers.cs` (runs under `GameDataManager.StateLock`), new
  `Data/Persistence/WorkshopToolsSection.cs` (`ISaveSection` + `ISnapshotProvider`, key `workshop-tools`);
  inventory handlers ignore duplicate UIDs and handle `Update`.
- Client: new `Logic/Tools/*` (one small file per machine on a shared `ToolSync` base), new
  `Network/Handlers/ToolHandlers.cs`, a UID-scoped inventory guard in `InventoryHook`.
- Harness: `tools/TestHarness/Features/ToolsCommands.cs`, `tools`/`toolPositions` sections in `StateDump`,
  `Wait-HarnessDumpsEqual` in `HarnessClient.psm1`, scenarios `tools-slots`, `tools-race`, `tools-car-effects`,
  `tools-latejoin`.
- Depends on `session-persistence-and-rejoin` task groups 1–2 (contract, state lock, sections, `SyncTracker`, harness server commands), `sync-car-parts` (part keys,
  `PartTransaction`, `CarPartsSync.MarkDirty`/`UploadBaseline`, engine crane group transactions, engine swap field),
  `sync-car-details` (`CarDetailsSync.MarkDirty`/`FlushNow`), roadmap row 13 `sync-test-drive-and-diagnostics` (stores dyno results), `sync-players-and-scenes`
  (`ClientScene.IsGarageReady`), and `sync-car-placement-and-lifts` only for tools attached to a car place whose car moves.
