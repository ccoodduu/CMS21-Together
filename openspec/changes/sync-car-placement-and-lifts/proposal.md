# Proposal

## Why

Cars can be spawned and deleted together, but where they stand is not shared: raising a lift, moving a car
to a lift or the paint shop, parking a car or taking one out only happens for the player who did it. The
old 0.4.17 mod synced lifts as relative "up/down" steps (`CarLifter.Action`), which drifted as soon as two
players pressed a button at the same time or someone joined late, and its parking sync was disabled (the
hooks targeted the wrong type). Jobs (row 3) and purchases (row 6) need cars to move between places and into
parking, so placement has to be server-owned before them.

## What Changes

- **Lifts**: the server stores the absolute state (`OnFloor`/`Middle`/`Up`) of each lift, indexed by
  `GarageLoader.carLifter[]`. Hook `CarLifter.Action(int)` (prefix + postfix) and send the requested
  *target* state together with the state the client saw; the server accepts it only if that state is still
  current, and sends the result to everyone. Remote clients step the lift with `Action` (animated) and fall back
  to `CarLifter.InstantSet(int, bool)` when the local game refuses the step.
- **Moving a car between places in the garage**: hook `NotificationCenter.ChangeCarPos(CarLoader, CarPlace,
  bool)`; the server checks that the target place is free and the car's lift is on the floor, stores the
  place in the loader's spawn record and sends the result to everyone. Remote clients replay `ChangeCarPos` with
  `movePlayerToCar: false` under suppression.
- **Parking lot**: the server owns the parking slots. A parked car is stored as an opaque blob made with the
  game's own `NewCarData.Serialize`/`Deserialize`, so the server never needs game types.
  - Garage → parking: hook `NotificationCenter.MoveCarToParking(CarLoader)` (marks the loader) and the game's
    `GameDataManager.SaveCarInParking(NewCarData, int)` (gives slot and car data); the existing `CarLoader.DeleteCar()`
    hook stays silent for a marked loader. One atomic `CarParkRequest` replaces the delete.
  - Parking → garage: hook `CarLoader.LoadCarFromFile(int, bool)` with `fromParking = true`; one atomic
    `CarUnparkRequest` replaces the spawn request and counts as a spawn for `sync-car-parts` (`SpawnSeq`,
    `CarSpawnAck`, baseline). Other clients load the car from the blob through `CarLoader.LoadCarFromFile(NewCarData)`.
  - Swapping slots: hook `ParkingCarPlaceManager.MoveCar(int, int)`.
  - Parking levels: hook the unlock confirmation in `ParkingManagementWindow`; the server charges the shared
    money and stores the unlocked levels.
  - Remote parking changes are written straight into `ProfileData.carsOnParking`; an open
    `ParkingManagementWindow` is refreshed.
- **Cars arriving from outside the garage** (rows 3 and 6): `CarParkRequest` with `CarLoaderID = -1`, `RequestId`
  and `Price`; the server rejects when the lot is full or money is short, else deducts the price once; the client
  gets `CarParkResult`.
- **Server API for other rows**: `ParkingService.TryAdd/TryRemove` (behind the -1 branch, later sell paths) and a
  lift reset in `sync-car-parts`' "loader cleared" function (delete, park, job end).
- **Save and late join** through `session-persistence-and-rejoin`'s contract: section `car-placement`
  (`ISaveSection` + `ISnapshotProvider`, `SyncOrder` 200) sends the parking lot and raised lifts after the cars;
  car places and parked-car base data travel inside the car records of `sync-car-parts`.
- Packets: **new** `LifterActionRequest`, `LifterState`, `CarPlaceChangeRequest`, `CarPlaceChanged`,
  `CarParkRequest`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingSlotUpdate`, `ParkingState`,
  `ParkingLevelUnlockRequest`, `ParkingResyncRequest`, `CarParkResult`; **changed** `CarSpawnResponsePacket` gains
  `CarData`/`CarDataVersion` (a car that left parking is replayed from its blob) and, if `placeNo` is not the
  `CarPlace`, a `Place` field. `ModGameState` gains a `PlacementState`.
- Out of scope for v1: taking a car out from the separate Parking *scene* (`ParkingWindow.MoveCarToGarageAction`)
  is blocked while connected; viewing parked cars there still works.

## Capabilities

### New Capabilities
- `car-placement-sync`: shared lift states, car places in the garage, the parking lot (slots, levels) and
  car transfers between parking and the garage, including conflict handling and late join.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (new values appended), new `Network/Packets/PlacementPackets.cs`, `Data/ParkingLayout.cs`,
  `CarPackets.cs` (`CarSpawnResponsePacket`), `Data/ModGameState.cs` (`PlacementState`).
- Server: new `Network/Handlers/PlacementHandlers.cs`, `ParkingHandlers.cs`, `Data/ParkingService.cs`,
  `Data/Persistence/PlacementSection.cs`; `CarHandlers.cs` (place on spawn, unpark as spawn, unpark-disconnect
  rule); `CommandSystem` (`placement` command).
- Client: new `Logic/Car/LifterSync.cs`, `CarPlacementSync.cs`, `ParkingSync.cs`, `NewCarDataCodec.cs` and
  hook classes; `CarSpawnHooks` (park/unpark interception); `Network/Handlers/CarHandlers.cs` (load from blob,
  place after load, pending gate).
- Test harness: `tools/TestHarness/Features/CarPlacementCommands.cs` (incl. `net-hold` to make races
  deterministic), `lifters`/`placement`/`parking` dump sections, scenarios `car-placement.ps1`,
  `car-placement-latejoin.ps1`, `car-parking-full.ps1`.
- Depends on `session-persistence-and-rejoin` task group 2 (contract, state lock) and `sync-car-parts` (car
  replay with spawn info, per-loader `Ready` state, `SpawnSeq`/`CarSpawnAck`, "loader cleared" function). Server
  save grows by the parked cars' blobs.
