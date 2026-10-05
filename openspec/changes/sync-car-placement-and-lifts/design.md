# Design

## Context

See proposal.md for motivation and specs/car-placement-sync/spec.md for the required behavior. Facts that
shape the approach (checked in the repo and the decompiled game):

- The client plays in a fresh profile (`ModGameManager.StartGame` creates `ProfileData` slot 4), so its own
  parking lot, lifts and car loaders start empty. Everything placement-related must come from the server.
- Car spawn/delete is "hybrid": the acting client runs the vanilla method and the hook (`CarSpawnHooks`) tells
  the server, which stores `CarState.LoadedCars[CarLoaderID]` (a `CarSpawnResponsePacket`) and relays it.
  Remote applies are wrapped in `CarSpawnHooks.Suppress/Release(loaderId)`.
- The vanilla lift restore at garage load is commented out in `LoaderAddition.VanillaLoad`, so lifts are
  currently never restored.
- Game API used (all present in the decompiled stubs; method bodies are not available):
  - `CarLifter`: `Action(int)`, `InstantSet(int, bool)`, `GetState()`, `currentState`, `isMoving`,
    `GetConnectedCarLoader()`, `IsBlocked()`, `IsPlayerInside()`, `HaveToResetPlayer()`;
    `CarLifterState { OnFloor, Middle, Up }`; `GarageLoader.Get().carLifter[]`.
  - `NotificationCenter`: `ChangeCarPos(CarLoader, CarPlace, bool)`, `MoveCarToParking(CarLoader)`,
    `MoveCarToGarage(NewHash, bool)`; `CarPlace { Entrance1..3, CarLifter1, CarLifter2, Paintshop, Dyno,
    DiagnosticPath, CarWash }`.
  - `CarLoader`: `placeNo`, `GetPlaceNo()`, `IsInPlace(CarPlace)`, `LoadCarFromFile(int, bool)`,
    `LoadCarFromFile(NewCarData)`, `DeleteCar()`, `IsCarLoaded()`, `PlaceAtPosition(bool, bool)`, `lifter`.
  - `CarLoaderPlaces`: `GetCarLoaderId`, `GetCarLoaderByIndex`, `ChangeGroundPosition(CarLoader, int, CarPlace)`,
    `carLoaderGroundPosition`.
  - Parking: `ProfileData.carsOnParking` (`NewCarData[]`), `NewCarData.Serialize(BinaryWriter, byte)` /
    `Deserialize(BinaryReader, byte)` / `IsDefault()`, `ProfileData.saveVersion`,
    `CMS.Managers.ParkingCarPlaceManager.MoveCar(int, int)` / `RemoveCar(int)` / `GetParkingFreePlaceIndex()`,
    `GlobalData.UnlockedParkingLevels` (a static field, not hookable), `NewGlobalDataWrapper.UnlockedParkingLevels`,
    `CMS.UI.Windows.ParkingManagementWindow` (`MoveCarFromParking()`, `ConfirmSwap(int, int)`,
    `UnlockParkingLevelAction()`, `Method_Private_Void_Boolean_PDM_0(bool)`, `parkingLevelPrice`,
    `RefreshPanels()`), `CMS.UI.Windows.ParkingWindow.MoveCarToGarageAction()` / `MoveCarToGarage()`.
- Lessons from 0.4.17 and TogetherFixer: relative lift steps drifted (TogetherFixer added an absolute
  `InstantSet` join sync and an F4 "emergency reset"); the parking hooks were commented out because they
  patched `CarLoader` instead of `GameDataManager`; TogetherFixer's `ParkingAdd` had to diff `carsOnParking`
  around `MoveCarToParking` because the slot index is not passed to any hookable method. We take the ideas
  (absolute state, diffing the parking array), not the code.

## Goals / Non-Goals

**Goals:**
- One authoritative value per lift, per car place and per parking slot on the server; every client converges
  to it, including after a refused action and on late join.
- Garage↔parking transfers are atomic on the server (a car is never in both places or in neither).
- The server stays free of game types: parked cars are opaque bytes.

**Non-Goals:**
- Part state of cars on loaders and the car replay itself (row 1, `sync-car-parts`).
- Money from selling cars, buying cars into parking (row 6), customer cars arriving or leaving (row 3). This
  change exposes the parking add path they can call.
- The separate Parking scene's "move to garage" (blocked while connected, see Decision 8) and scene tracking (row 6).
- Save versioning (row 7).

## Decisions

### 1. Absolute state + "expected previous" check, server wins

Every request carries what the client believed the state was and the state it wants. The server applies it
only if the belief matches its own value; otherwise it sends the requester the authoritative value. The acting
client still runs vanilla immediately (prediction, same as the existing spawn flow) so the game never waits on
the network. *Alternative:* relay relative actions (0.4.17) — drifts on any lost or concurrent action;
*alternative:* lock-before-act (ask server first) — adds a round-trip to every button press and needs the
vanilla call to be replayed after the reply, which is fragile for coroutines.

### 2. Lifts

- **Identity**: index in `GarageLoader.Get().carLifter[]` (the order vanilla saves `carLiftersData` in).
  0.4.17 parsed the loader's GameObject name, which breaks for loader 10+.
- **Detect**: prefix on `CarLifter.Action(int actionType)` records `GetState()` and `isMoving`; the postfix
  sends `LifterActionRequest { LifterIndex, CarLoaderID, FromState, ToState }` only if the call started a
  movement (`isMoving` went false→true), so refused presses (blocked, player under the lift, no car) send
  nothing. `ToState = FromState ± 1` (`actionType` 0 = up, 1 = down; confirmed by 0.4.17 and
  `LifterButtonTypes { Up, Down, All }`).
- **Server**: accepts if `FromState` equals its stored state, `ToState` is one step away, and
  `CarLoaderID` has a `LoadedCars` record; stores `LifterRecord { State, CarLoaderID }`; broadcasts
  `LifterState { LifterIndex, CarLoaderID, State, Instant=false }` to the *other* clients. On refusal it
  sends `LifterState` with the stored value to the requester only.
- **Apply** (per-lifter suppression set like `CarSpawnHooks`): wait until `!isMoving`; if the lift has no
  connected car, target 0; step with `Action(0/1)` and wait for each step; if a step does not start moving
  (local blocker, local player inside), `InstantSet(target, true)` and, if `HaveToResetPlayer()`, reset the
  local player position the way vanilla load does (`FPSInputController.ResetPosition()`). `Instant=true`
  (late join) goes straight to `InstantSet`.
- **Reset**: when the server removes or moves a loader (delete, park, place change), any lifter record
  pointing at that loader is set to `OnFloor` without a broadcast; clients' own vanilla code lowers/detaches
  the lift as part of the replicated delete/move.
- *Alternative considered:* hooking `MoveUp`/`MoveMedFromFloor`/`MoveMiddleToFloor`/`MoveDown` gives the
  direction directly, but which one maps to which transition is unverified; kept as fallback (task 3.1).

### 3. Car places

- **Detect**: prefix on `NotificationCenter.ChangeCarPos(CarLoader, CarPlace, bool)` (the player-intent
  method; it also tells us the actor). It sends `CarPlaceChangeRequest { CarLoaderID, FromPlace, ToPlace }`.
  *Alternative:* `CarLoaderPlaces.ChangeGroundPosition` is lower-level and probably also runs while loading,
  which would need extra filtering.
- **Store**: the place lives in the existing `LoadedCars[loader].PlaceNo` (working assumption: `placeNo`
  holds the `CarPlace` value; spike 3.2 confirms, otherwise a separate `Place` field is added). Keeping it in
  the spawn record means row 1's car replay already spawns a late joiner's car at the right place.
- **Server**: refuses if no record, `FromPlace` ≠ stored, another loader's record holds `ToPlace`, or a
  lifter record for this loader is not `OnFloor`. Accept → store, broadcast `CarPlaceChanged { CarLoaderID,
  Place }` to others; refuse → send `CarPlaceChanged` with the stored place to the requester, whose client
  moves the car back.
- **Apply**: under per-loader suppression, `StartCoroutine(NotificationCenter.ChangeCarPos(loader, place,
  movePlayerToCar: false))`. If spike 3.2 shows it fades the screen or locks input, use the decomposed calls
  it makes instead (expected: `CarLoaderPlaces.ChangeGroundPosition` + `CarLoader.PlaceAtPosition` + lift
  connect via `CarLifter.ConnectCar/DisconnectCar`).
- **Car moved while someone works on it**: allowed (spec). Parts are keyed by loader, not place, so row 1's
  updates still apply. If the local player stands inside the destination place, reset their position.

### 4. Parked cars are opaque `NewCarData` blobs

`NewCarDataCodec` serializes with `Il2CppSystem.IO.BinaryWriter` over a `MemoryStream` using the current
`ProfileData.saveVersion`, and copies the bytes into a managed `byte[]`. The server stores
`ParkedCar { Id (server Guid), CarToLoad, UId, SaveVersion, Data }` per slot. *Alternatives:* a mod DTO for
all of `NewCarData` (dozens of nested types, overlaps rows 1 and 4, server gains nothing from understanding
it); rebuilding from part state (loses fluids, wheels, paint, tuning). Identity for concurrency checks is the
server `Id`, because `NewCarData.UId` is not guaranteed to be set.

### 5. Transfers are single atomic requests

- **Garage → parking**: prefix on `NotificationCenter.MoveCarToParking(CarLoader)` marks the loader
  "parking" (expires after 10 s). The `CarSpawnHooks.DeleteCarHook` for a marked loader does not send
  `CarSpawnDelete`; instead a coroutine diffs `carsOnParking` against the client's mirror of the server slots
  (by blob presence, up to 5 s, since the save may happen just before or after `DeleteCar`) and sends
  `CarParkRequest { CarLoaderID, PreferredSlot, Car }`. Server: if a free slot exists (preferred, else the
  lowest free one within `UnlockedLevels`), remove `LoadedCars[loader]` and row 1's part state for it, reset
  its lift record, store the slot, send `CarSpawnDelete` to others and `ParkingSlotUpdate` to everyone; if the
  preferred slot was taken, also send the requester `ParkingSlotUpdate` for that slot. If the lot is full,
  send the requester `CarSpawnResponse` with the blob (car comes back) and a `ParkingSlotUpdate` clearing its
  local slot. If the diff times out, the client falls back to `CarSpawnDelete` and logs an error.
- **Parking → garage**: prefix on `CarLoader.LoadCarFromFile(int index, bool fromParking)` with
  `fromParking = true` suppresses the `LoadCar` spawn hook for that loader and, once `IsCarLoaded()`, sends
  `CarUnparkRequest { Slot, ParkedCarId, CarLoaderID, PlaceNo, ConfigVersion }`. Server: if the slot still holds
  that `Id` and the loader is free, clear the slot, set `LoadedCars[loader]` with `CarData` from the slot,
  broadcast `CarSpawnResponse` (others) and `ParkingSlotUpdate` (all). Otherwise `CarSpawnRejected` (existing:
  the client deletes the car locally) plus `ParkingSlotUpdate` for the slot to the requester.
- **Remote load from blob**: `ProcessCarSpawnResponse` uses `LoadCarFromFile(NewCarData)` when `CarData` is
  set, else `LoadCar(name)` as today. Row 1's baseline snapshot after spawn then gives the server the part state.
- **Swap**: postfix on `ParkingCarPlaceManager.MoveCar(int from, int to)` sends `ParkingMoveRequest { From,
  To, FromId, ToId }` (ids from the mirror). The server swaps if both ids match and broadcasts two
  `ParkingSlotUpdate`; otherwise it sends the requester a full `ParkingState`.
- *Alternative:* keep `CarSpawnDelete`/`CarSpawnRequest` and send a separate parking packet — two packets can
  be split by a disconnect or a refusal, leaving the car in both places or neither.

### 6. Remote parking changes are written straight into the profile

Apply `ParkingSlotUpdate` by assigning `ProfileData.carsOnParking[slot]` (deserialized blob, or
`new NewCarData()` for empty; spike 4.1 checks `IsDefault()` is true for it), then refresh an open
`ParkingManagementWindow` (`RefreshPanels()`, and deselect the slot if it was selected). Vanilla
`MoveCar`/`RemoveCar` are never replayed, so no hook re-fires and no slot is picked locally. A safety net
compares `carsOnParking` with the mirror when `ParkingManagementWindow` hides; a difference not explained by a
pending request is logged and answered with a `ParkingState` request (server wins).

### 7. Parking levels

Prefix on `ParkingManagementWindow.Method_Private_Void_Boolean_PDM_0(bool wasAccepted)` (the confirm
callback of the unlock dialog; the obfuscated name is verified in spike 4.3): when accepted and connected,
send `ParkingLevelUnlockRequest { TargetLevels = current + 1, Price = parkingLevelPrice }` and skip vanilla.
The server (like `GarageUpgradeHandler`) unlocks only if `TargetLevels == stored + 1` and money ≥ price,
deducts money, and broadcasts `WorldState` and `ParkingState` (levels only, no slots). Clients set
`GlobalData.UnlockedParkingLevels` and `profile.globalDataWrapper.UnlockedParkingLevels`. The price comes from
the client because the server has no price table (assumption A4).

### 8. Parking scene take-out is blocked while connected

Prefix on `ParkingWindow.MoveCarToGarageAction()` returns false with a UI message while connected. That path
loads the garage scene with the car (via `ParkingManager.MoveCarToGarage`) while the player is outside the
garage, which needs row 6's scene tracking to be safe.

### 9. Server stores vs. relays

| Data | Server | Packets |
|---|---|---|
| Lift position + connected loader | **stores** `PlacementState.Lifters[index]` | `LifterActionRequest` → `LifterState` |
| Car place | **stores** in `CarState.LoadedCars[loader].PlaceNo` | `CarPlaceChangeRequest` → `CarPlaceChanged` |
| Parked cars | **stores** `PlacementState.Parking.Slots[slot]` (blob) | `CarParkRequest`, `CarUnparkRequest`, `ParkingMoveRequest` → `ParkingSlotUpdate`, `ParkingState` |
| Parking levels | **stores** `PlacementState.Parking.UnlockedLevels`, money in `WorldState` | `ParkingLevelUnlockRequest` → `ParkingState`, `WorldState` |
| Base data of a car that left parking | **stores** `LoadedCars[loader].CarData` | `CarSpawnResponse` |
| Lift animation, player reposition, UI refresh | relays nothing | client-local |

All state lives in `ModGameState` and is therefore written by the existing JSON save (`byte[]` as base64).

### 10. Late-join path

1. Client loads the garage with an empty profile and sends `AskForSync`.
2. Server sends `WorldState`, `GarageState`, inventory, then (row 1) the loaded cars — their `PlaceNo` and
   `CarData` put each car at the right place with the right base data.
3. Server sends `ParkingState { UnlockedLevels, SlotCount, OccupiedSlots[] }` then one `ParkingSlotUpdate`
   per occupied slot; the client clears every slot not listed and sets `ClientData.IsParkingSynced` once all
   listed slots arrived.
4. Server sends `LifterState { Instant = true }` for every lift record not on the floor.
5. `SyncEnd` (moved to the end of `OnAskForSync`, shared with row 1). `WaitForSyncCompletion` also waits
   for `IsParkingSynced`.
6. Client applies each `LifterState` once its loader `IsCarLoaded()` and the lift reports it as connected
   (timeout 30 s, then lift to floor and log). Live packets that arrive during sync queue behind the same
   gates as `ProcessCarSpawnResponse`.

### 11. Packet types

New `PacketTypes` values are appended at the end of the enum (other rows append too; resolve by merge order,
never renumber existing values). Direction: `*Request` client→server; `LifterState`, `CarPlaceChanged`,
`ParkingSlotUpdate`, `ParkingState` server→client.

## Risks / Trade-offs

- [IL2CPP inlining: a hooked method called from native code may never hit the detour — especially small
  ones like `ParkingCarPlaceManager.MoveCar`] → each hook gets a spike that logs whether it fires on the real
  UI path; the hide-time parking diff (Decision 6) catches what slips through and resyncs.
- [`NewCarData` serialization through Unhollower streams fails or differs between game builds] → spike 4.1
  round-trips a car before anything else is built; the blob carries `SaveVersion` and a client refuses a blob
  whose version differs from its own (logs, keeps slot empty, requests no retry).
- [`placeNo` is not the `CarPlace` value] → spike 3.2; fallback is a separate `Place` field on the spawn record.
- [Order of save vs. `DeleteCar` inside `MoveCarToParking` is unknown] → the park detection waits up to 5 s
  for the diff in either order; timeout falls back to a plain delete (car lost to parking but state consistent).
- [Blob size: a car may be tens of KB; `ParkingState` for a full lot is sent as one packet per slot] → no
  single packet holds more than one car; Steam's reliable message limit (512 KB) is far above one car.
- [Parts changed by another player in the last moments before parking are lost: the blob is the parking
  client's view] → accepted for v1; the server logs part updates that arrive for a loader that was just parked.
- [Remote lift lowered onto the local player] → vanilla blocks it locally; we fall back to `InstantSet` and
  reset the player like vanilla load does. Visible pop, but no desync.
- [Trusting the client's parking level price] → co-op only; the server checks money and that the level is the
  next one, so the worst case is a wrong price, not a free or double unlock.
- [`Method_Private_Void_Boolean_PDM_0` is an obfuscated name] → spike 4.3 verifies it; fallback is a postfix on
  `UnlockParkingLevelAction` plus a before/after compare of `GlobalData.UnlockedParkingLevels` on window hide.

## Migration Plan

Clients and server must run the same build (existing mod version check). Old server saves load with an empty
`PlacementState` (Newtonsoft defaults) and `CarData = null` (cars spawn from their name as today). Rollback =
previous build; the extra JSON fields are ignored by older builds.

## Open Questions / Assumptions

Decided without the user (the user can override):

- **A1** Parking scene take-out is blocked while connected (Decision 8) rather than synced. Revisit with row 6.
- **A2** Moving a car is never blocked because someone works on it; only an occupied target or a raised lift
  refuses it.
- **A3** `sync-car-parts` provides: the car replay on join using `CarSpawnResponse` (so `PlaceNo` and the new
  `CarData` are honored), a "car on loader X is loaded" signal or `IsCarLoaded()` polling, a baseline upload
  after every spawn (including unpark), dropping part state when this change removes a loader record, and
  `SyncEnd` at the end of `OnAskForSync`.
- **A4** The parking level price comes from the client; adding a server price table can come later.
- **A5** Rows 3 and 6 add cars to parking through `CarParkRequest` with `CarLoaderID = -1` (no garage loader)
  and sell/remove through a future `ParkingSlotUpdate` request; this change only provides the server-side
  slot allocation they reuse.

Deferrable unknowns (answered by the spikes in tasks.md, they do not change the approach): which lift
coroutine maps to which transition; whether `ChangeCarPos` fades the screen; whether `LoadCarFromFile(NewCarData)`
internally calls `LoadCar(string)` (either way it is suppressed).
