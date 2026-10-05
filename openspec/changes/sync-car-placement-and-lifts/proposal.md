# Proposal

## Why

Cars can be spawned and deleted together, but where they stand is not shared: raising a lift, moving a car
to a lift or the paint shop, parking a car or taking one out only happens for the player who did it. The
old 0.4.17 mod synced lifts as relative "up/down" steps (`CarLifter.Action`), which drifted as soon as two
players pressed a button at the same time or someone joined late, and its parking sync was disabled (the
hooks targeted the wrong type). Jobs (row 3) need customer cars to move between places and to/from parking,
so placement has to be server-owned before them.

## What Changes

- **Lifts**: the server stores the absolute state (`OnFloor`/`Middle`/`Up`) of each lift, indexed by
  `GarageLoader.carLifter[]`. Hook `CarLifter.Action(int)` (prefix + postfix) and send the requested
  *target* state together with the state the client saw; the server accepts it only if that state is still
  current, and broadcasts the result. Remote clients step the lift with `Action` (animated) and fall back to
  `CarLifter.InstantSet(int, bool)` when the local game refuses the step.
- **Moving a car between places in the garage**: hook `NotificationCenter.ChangeCarPos(CarLoader, CarPlace,
  bool)`; the server checks that the target place is free and the car's lift is on the floor, stores the
  place on the loader record and relays it. Remote clients replay `ChangeCarPos` with `movePlayerToCar:
  false` under suppression.
- **Parking lot**: the server owns the parking slots. A parked car is stored as an opaque blob made with the
  game's own `NewCarData.Serialize`/`Deserialize`, so the server never needs game types.
  - Garage → parking: hook `NotificationCenter.MoveCarToParking(CarLoader)` and the existing
    `CarLoader.DeleteCar()` hook; one atomic `CarParkRequest` replaces the delete.
  - Parking → garage: hook `CarLoader.LoadCarFromFile(int, bool)` with `fromParking = true`; one atomic
    `CarUnparkRequest` replaces the spawn request, and other clients load the car from the blob through
    `CarLoader.LoadCarFromFile(NewCarData)`.
  - Swapping slots: hook `ParkingCarPlaceManager.MoveCar(int, int)`.
  - Parking levels: hook the unlock confirmation in `ParkingManagementWindow`; the server charges the money
    and stores `UnlockedParkingLevels`.
  - Remote parking changes are written straight into `ProfileData.carsOnParking`; an open
    `ParkingManagementWindow` is refreshed.
- **Late join**: initial sync sends the parking lot, and after the cars are replayed (row 1) the lift states;
  car places travel inside the existing spawn record.
- Packets: **new** `LifterActionRequest`, `LifterState`, `CarPlaceChangeRequest`, `CarPlaceChanged`,
  `CarParkRequest`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingSlotUpdate`, `ParkingState`,
  `ParkingLevelUnlockRequest`; **changed** `CarSpawnResponsePacket` gains `CarData`/`CarDataVersion` (a
  car that left parking is replayed from its blob). `ModGameState` gains a `PlacementState`.
- Out of scope for v1: taking a car out from the separate Parking *scene* (`ParkingWindow.MoveCarToGarage`)
  is blocked while connected; viewing parked cars there still works.

## Capabilities

### New Capabilities
- `car-placement-sync`: shared lift states, car places in the garage, the parking lot (slots, levels) and
  car transfers between parking and the garage, including conflict handling and late join.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (new values appended), new `Network/Packets/PlacementPackets.cs`,
  `CarPackets.cs` (`CarSpawnResponsePacket`), `Data/ModGameState.cs` (`PlacementState`).
- Server: new `Network/Handlers/PlacementHandlers.cs` and `ParkingHandlers.cs`; `CarHandlers.cs` (place on
  spawn, lift reset on delete); `AuthHandlers.OnAskForSync`.
- Client: new `Logic/Car/LifterSync.cs`, `CarPlacementSync.cs`, `ParkingSync.cs`, `NewCarDataCodec.cs` and
  hook classes; `CarSpawnHooks` (park/unpark interception); `Network/Handlers/CarHandlers.cs` (load from
  blob); `WorldStatesPackets.WaitForSyncCompletion` waits for the parking lot.
- Test harness: `tools/TestHarness/Features/CarPlacementCommands.cs`, `lifters`/`placement`/`parking` dump
  sections, scenarios `car-placement.ps1` and `car-placement-latejoin.ps1`.
- Depends on `sync-car-parts` (car replay on join, per-loader part state that must be dropped when a car is
  parked). Server save grows by the parked cars' blobs; save format and versioning stay with
  `session-persistence-and-rejoin`.
