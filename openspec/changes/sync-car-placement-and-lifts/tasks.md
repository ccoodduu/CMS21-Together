# Tasks

Prerequisite: `sync-car-parts` is merged (car replay on join, per-loader part state, `SyncEnd` at the end of
`OnAskForSync`, `car-spawn` harness command). Work on branch `change/sync-car-placement-and-lifts`.

## 1. Core: packets and state

- [ ] 1.1 Append to `PacketTypes` (after the last existing value, never renumber): `LifterActionRequest`, `LifterState`, `CarPlaceChangeRequest`, `CarPlaceChanged`, `CarParkRequest`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingSlotUpdate`, `ParkingState`, `ParkingLevelUnlockRequest`; verify the solution builds
- [ ] 1.2 Add `Network/Packets/PlacementPackets.cs` with the packets and fields from design.md (Decisions 2, 3, 5, 7, 10) plus a `[Serializable] ParkedCar { Id, CarToLoad, UId, SaveVersion, Data }`, each with `[NetworkPacket]`; verify the build and that `PacketRouter.Initialize` logs 10 more packets
- [ ] 1.3 Add `byte[] CarData` and `byte CarDataVersion` to `CarSpawnResponsePacket` (null = spawn by name as today); verify a spawn without parking still works in the existing `connect` scenario
- [ ] 1.4 Add `PlacementState { Dictionary<int, LifterRecord> Lifters; ParkingLot Parking { int UnlockedLevels; int SlotCount; Dictionary<int, ParkedCar> Slots } }` to `ModGameState`; verify the server saves and reloads a state with one fake parked car (base64 blob) and one lifter record unchanged

## 2. Server: handlers

- [ ] 2.1 `PlacementHandlers.OnLifterActionRequest`: accept/refuse per Decision 2, store the record, broadcast or answer with `LifterState`; verify with a server log line per accept/refuse during task 3.3's two-instance check
- [ ] 2.2 `PlacementHandlers.OnCarPlaceChangeRequest`: refuse on unknown loader, stale `FromPlace`, occupied target or raised lift; store in `LoadedCars[loader].PlaceNo`; verify the refuse paths in task 3.4's check
- [ ] 2.3 In `CarHandlers`: on `CarSpawnDelete` reset any lifter record pointing at that loader to `OnFloor`; verify by deleting a car on a raised lift and reading the record with task 2.6's command
- [ ] 2.4 `ParkingHandlers`: `CarParkRequest` (slot allocation, lot full → give the car back, drop `LoadedCars`/part state/lift record), `CarUnparkRequest` (id + free loader check, `CarData` into the spawn record), `ParkingMoveRequest` (both ids must match, else full `ParkingState` to the requester), `ParkingLevelUnlockRequest` (next level only, money check, broadcast `WorldState` + levels); verify each accept/refuse branch is logged during group 4's checks
- [ ] 2.5 `AuthHandlers.OnAskForSync`: after the car replay send the `ParkingState` header, one `ParkingSlotUpdate` per occupied slot, `LifterState { Instant = true }` per raised lift, then `SyncEnd`; verify the order in the server log of a late join
- [ ] 2.6 Add a server console command `placement` that prints lifts, car places and parking slots (id, car, size); verify it prints the expected state after group 3 and 4 actions

## 3. Client: lifts and car places

- [ ] 3.1 Spike: temporary logging hooks on `CarLifter.Action`, `MoveUp`, `MoveMedFromFloor`, `MoveMiddleToFloor`, `MoveDown`, `InstantSet`; press the lift buttons in one instance and record in design.md whether `isMoving` is set before `Action` returns and which coroutine maps to which transition
- [ ] 3.2 Spike: log `NotificationCenter.ChangeCarPos`, `CarLoaderPlaces.ChangeGroundPosition`, `CarLoader.placeNo`/`GetPlaceNo()`/`carLoaderGroundPosition`, `CarLifter.ConnectCar/DisconnectCar` while moving a car with the vanilla UI; record in design.md whether `placeNo` equals the `CarPlace` value and whether `ChangeCarPos(…, false)` fades the screen or locks input
- [ ] 3.3 `Logic/Car/LifterSync.cs`: `Action` prefix/postfix sending `LifterActionRequest`, per-lifter suppression, apply coroutine (step with `Action`, fall back to `InstantSet` + player reset), handler for `LifterState`; verify in two instances that raising, lowering and both players pressing "up" at once end in the same state (`lifters` dump section from 3.6)
- [ ] 3.4 `Logic/Car/CarPlacementSync.cs`: `ChangeCarPos` prefix sending `CarPlaceChangeRequest`, per-loader suppression, apply via `ChangeCarPos(…, false)` (or the decomposed calls from 3.2), handler for `CarPlaceChanged` that moves the car back on refusal; verify a move, a move onto an occupied place and a move of a car on a raised lift in two instances
- [ ] 3.5 Gate all hooks on connected + garage scene + `IsInitialSyncFinished` + not suppressed, like `CarSpawnHooks`; verify a single-player session (not connected) still moves cars and lifts without errors in the log
- [ ] 3.6 Harness: `tools/TestHarness/Features/CarPlacementCommands.cs` with `lift <index> up|down` (calls `CarLifter.Action`) and `car-move <loaderId> <CarPlace>` (calls `NotificationCenter.ChangeCarPos`); `StateDump` sections `lifters` (`index`, `state`, `isMoving`, `connectedLoader`) and `placement` (`loader`, `carToLoad`, `placeNo`, `place` from `IsInPlace`); verify both commands and sections against a manual run

## 4. Client: parking

- [ ] 4.1 Spike: `Logic/Car/NewCarDataCodec.cs` (serialize/deserialize with `ProfileData.saveVersion`); round-trip a parked car in one instance and compare `carToLoad`, body part count and condition before/after; confirm `new NewCarData().IsDefault()` is true; record the result in design.md
- [ ] 4.2 Spike: log whether `NotificationCenter.MoveCarToParking`, `CarLoader.DeleteCar`, `CarLoader.LoadCarFromFile(int, bool)`, `ParkingCarPlaceManager.MoveCar/RemoveCar` fire on the real UI paths (`ParkingManagementWindow` take-out and swap, garage "move to parking"), and whether the parking save happens before or after `DeleteCar`; record in design.md and switch to fallback hooks where one does not fire
- [ ] 4.3 Spike: confirm `ParkingManagementWindow.Method_Private_Void_Boolean_PDM_0(bool)` is the unlock confirmation callback; record in design.md, else use the fallback from design.md Risks
- [ ] 4.4 `Logic/Car/ParkingSync.cs`: mirror of server slots (slot → `ParkedCar.Id`), `ParkingSlotUpdate`/`ParkingState` handlers writing `ProfileData.carsOnParking` under an apply scope, `ParkingManagementWindow.RefreshPanels()` when open, hide-time safety-net diff requesting `ParkingState`; verify that a server-side `ParkingSlotUpdate` shows up in an open parking window
- [ ] 4.5 Park: `MoveCarToParking` prefix marks the loader; `CarSpawnHooks.DeleteCarHook` defers to `ParkingSync` for marked loaders; diff coroutine sends `CarParkRequest` (timeout → `CarSpawnDelete` + error); verify in two instances that the car leaves B's garage and appears in B's slot
- [ ] 4.6 Unpark: `LoadCarFromFile(int, true)` prefix suppresses the spawn hook and sends `CarUnparkRequest` once loaded; `CarHandlers.ProcessCarSpawnResponse` loads from `CarData` via `LoadCarFromFile(NewCarData)`; verify in two instances that a car parked with a removed door comes back with the door removed for both
- [ ] 4.7 Swap: `ParkingCarPlaceManager.MoveCar` postfix sends `ParkingMoveRequest`; verify a swap in two instances and that a swap with a stale id resyncs the requester
- [ ] 4.8 Level unlock: confirm-callback prefix sends `ParkingLevelUnlockRequest` and skips vanilla; `ParkingState` (levels) handler sets `GlobalData.UnlockedParkingLevels` and `globalDataWrapper.UnlockedParkingLevels`; verify money drops once when both instances unlock together
- [ ] 4.9 Block `ParkingWindow.MoveCarToGarageAction` while connected and show a message; verify manually in the parking scene that nothing moves
- [ ] 4.10 Harness: add `park <loaderId>`, `unpark <slot>` (same entry the UI uses, per 4.2), `park-swap <from> <to>`, `parking-unlock` to `CarPlacementCommands.cs`; `StateDump` section `parking` (`levels`, `slots: [{ index, carToLoad, uid }]`); verify each command against a manual run

## 5. Client: initial sync and late join

- [ ] 5.1 Handle the `ParkingState` header during sync (clear unlisted slots, count slot packets, set `ClientData.IsParkingSynced`; reset it in `ClientData.Reset`); `WaitForSyncCompletion` waits for it; verify a join with 0 and with 3 parked cars finishes sync
- [ ] 5.2 Apply `LifterState { Instant = true }` only after the loader's car `IsCarLoaded()` and the lift reports it connected (30 s timeout → floor + error); verify a late join with a raised lift
- [ ] 5.3 Queue live placement packets that arrive before the initial sync finished behind the same gate as `ProcessCarSpawnResponse`; verify no "loader not found" errors when B joins while A moves a lift

## 6. Integration: harness scenarios in two instances

- [ ] 6.1 Add `lifters`, `placement`, `parking` to the default `-Sections` of `Compare-HarnessDumps` and a `Wait-HarnessDump` helper that polls `dump` until a condition holds (e.g. no lift `isMoving`); verify the `connect` scenario still passes
- [ ] 6.2 Scenario `tools/test-env/scenarios/car-placement.ps1`: both connect; A spawns cars on loaders 0 and 1; A moves car 0 to `CarLifter1`; B raises lift 0; A and B both send `lift 0 up` back to back; A moves car 1 to `CarLifter1` (refused); B moves car 0 to `Paintshop` (refused, lift raised); A lowers lift 0 to the floor; B parks car 1; A and B each park a car in the same frame (slot race); A swaps two slots; B unparks car 1; A unlocks a parking level; after each step compare `cars`, `lifters`, `placement`, `parking` and `stats`; passes when every comparison is equal and the expected refusals are in the server log. Run with `tools/test-env/Run-Session.ps1 -Scenario car-placement`
- [ ] 6.3 Scenario `tools/test-env/scenarios/car-placement-latejoin.ps1`: only A connects and does: spawn three cars, move car 0 to `CarLifter1`, lift 0 to `Up`, move car 1 to `Paintshop`, park car 2, swap it to another slot, unlock a level; then B connects; compare all sections; then B lowers lift 0 one step and both compare again; passes when all comparisons are equal. Run with `Run-Session.ps1 -Scenario car-placement-latejoin`
- [ ] 6.4 Manual restart check: after 6.3, save the session from the server window (`GameDataManager.SaveSession`), restart the server, connect one client and confirm the parked car, car places and lift position with `dump` and the `placement` server command; note the result in `STATUS.md`
