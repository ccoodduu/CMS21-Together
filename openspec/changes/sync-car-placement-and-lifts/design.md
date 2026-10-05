# Design

## Context

See proposal.md for motivation and specs/car-placement-sync/spec.md for the required behavior. Facts that
shape the approach (checked in the repo and the decompiled game):

- The client plays in a fresh profile (`ModGameManager.StartGame` creates `ProfileData` slot 4), so its own
  parking lot, lifts and car loaders start empty. Everything placement-related must come from the server.
- Car spawn/delete is "hybrid": the acting client runs the vanilla method and the hook (`CarSpawnHooks`) tells
  the server, which stores `CarState.LoadedCars[CarLoaderID]` and relays it. Remote applies are wrapped in
  `CarSpawnHooks.Suppress/Release(loaderId)`. `sync-car-parts` (row 1) adds `SpawnSeq`, `SpawnedBy`,
  `CarSpawnAck`, a per-loader client state machine (`Empty → Loading → AwaitingBaseline → Ready`), a baseline
  upload by the spawner, and deletes a car whose spawner disconnects before its baseline.
- `session-persistence-and-rejoin` (row 7) groups 1–2 are the contract this change plugs into (they land first):
  server `ISaveSection` (versioned) + `ISnapshotProvider` (`int SendSnapshot(clientId)` returns the item count)
  discovered by `[SessionSection]`, key `car-placement`, `SyncOrder` 200 (after `cars` 100 and `car-details` 150);
  `SyncBegin{snapshotId}` … `SyncEnd{snapshotId, Items}` with per-key counts, a client `SyncTracker` that sends
  `SyncAck` when every count is met (30 s no-progress timeout), and `GameDataManager.StateLock` held around every
  `PacketRouter.Dispatch`, `Client.Disconnect`, main-loop ticks and the save build on the server. Rows count
  items instead of adding their own "synced" flags.
- Client packet handlers run on the Unity main thread (`ThreadManager.ExecuteOnMainThread`); server handlers run
  on transport threads, serialized by `StateLock`.
- The vanilla lift restore at garage load is commented out in `LoaderAddition.VanillaLoad`, so lifts are
  currently never restored. The commented code shows vanilla's rule: a lift without a connected car is put on
  the floor, and a car missing wheels is put on the middle position.
- Game API used (all present in the decompiled stubs with these signatures; bodies are not available):
  - `CarLifter` (firstpass): `void Action(int actionType)`, `void InstantSet(int _pos, bool switchIO = true)`,
    `GetState()`, `currentState`, `isMoving`, `GetConnectedCarLoader()`, `IsBlocked()`, `IsPlayerInside()`,
    `HaveToResetPlayer()`, coroutines `MoveUp`/`MoveMedFromFloor`/`MoveMiddleToFloor`, `void MoveDown`;
    `CarLifterState { OnFloor, Middle, Up }`; `LifterButtonTypes { Up, Down, All }`; `GarageLoader.Get().carLifter[]`.
  - `NotificationCenter`: `IEnumerator ChangeCarPos(CarLoader, CarPlace, bool movePlayerToCar = true)`,
    `IEnumerator MoveCarToParking(CarLoader)`; `CarPlace { Entrance1, Entrance2, Entrance3, CarLifter1, CarLifter2,
    Paintshop, Dyno, DiagnosticPath, CarWash }`. Both are coroutine factories: a prefix runs when the coroutine
    is created, not when it finishes.
  - `CarLoader`: `placeNo`, `GetPlaceNo()`, `IsInPlace(CarPlace)`, `IEnumerator LoadCarFromFile(int index, bool
    fromParking)`, `IEnumerator LoadCarFromFile(NewCarData)`, `DeleteCar()`, `DeleteCar(bool disconnectGroundPosition)`,
    `SaveCarToFile(int index, bool toParking)`, `IsCarLoaded()`, `PlaceAtPosition(bool, bool)`, `lifter`.
  - `CarLoaderPlaces`: `GetCarLoaderId`, `GetCarLoaderByIndex`, `GetOccupied(CarPlace)`,
    `GetCarLoaderForPlace(CarPlace)`, `ChangeGroundPosition(CarLoader, int, CarPlace)`, `carLoaderGroundPosition`.
  - Parking: `ProfileData.carsOnParking` (`NewCarData[]`), `ProfileData.saveVersion` (byte),
    `NewCarData.Serialize(Il2CppSystem.IO.BinaryWriter, byte)` / `Deserialize(BinaryReader, byte)` / `IsDefault()`,
    game `GameDataManager.SaveCarInParking(NewCarData, int)` / `SaveCar(NewCarData, int, bool toParking)`,
    `CMS.Managers.ParkingCarPlaceManager.MoveCar(int, int)`, `GlobalData.UnlockedParkingLevels` (static field),
    `GlobalData.Cost_BaseParkingLevel`, `GlobalData.GetMaxParkingPlacesAmount()`,
    `NewGlobalDataWrapper.UnlockedParkingLevels`, `CMS.UI.Windows.ParkingManagementWindow` (`MoveCarFromParking()`,
    `ConfirmSwap(int, int)`, `UnlockParkingLevelAction()`, `Method_Private_Void_Boolean_PDM_0(bool wasAccepted)`,
    `parkingLevelPrice`, `RefreshPanels()`), `CMS.UI.Windows.ParkingWindow.MoveCarToGarageAction()`.
  - Unhollower `CallerCount`: `ParkingCarPlaceManager.RemoveCar` and `GetParkingFreePlaceIndex` are 0 (dead or
    inlined; not used as hooks). `ChangeCarPos`, `UnlockParkingLevelAction`, `PDM_0` and the `*Action()` UI methods
    are 0 because they are called through delegates, which still hit a detour. `SaveCarInParking` is 10.
- Lessons from 0.4.17 and TogetherFixer: relative lift steps drifted (TogetherFixer added an absolute
  `InstantSet` join sync and an F4 "emergency reset"); the 0.4.17 parking hooks were commented out because they
  patched `CarLoader` instead of the game's `GameDataManager`; TogetherFixer's `ParkingAdd` diffs `carsOnParking`
  around `MoveCarToParking`. We take the ideas (absolute state, a diff as fallback), not the code.

## Goals / Non-Goals

**Goals:**
- One authoritative value per lift, per car place and per parking slot on the server; every client converges
  to it, including after a refused action and on late join.
- Garage↔parking transfers are atomic on the server (a car is never in both places or in neither).
- The server stays free of game types: parked cars are opaque bytes.

**Non-Goals:**
- Part state of cars on loaders and the car replay itself (row 1, `sync-car-parts`).
- Money from selling cars, buying cars into parking (row 6), customer cars arriving or leaving (row 3). This
  change provides the server parking API they call (Decision 8).
- The separate Parking scene's "move to garage" (blocked while connected, Decision 7) and scene tracking (row 6).
- Save file format, versioning and the snapshot pipeline (row 7); this change only implements its section.
- Parking actions added by other mods (QoLmod's parking extras): row 9.

## Decisions

### 1. Absolute state, "expected previous" check, server wins, answer to everyone

Every request carries what the client believed the state was and the state it wants. The server applies it
only if the belief matches its own value. The acting client still runs vanilla immediately (prediction, same as
the existing spawn flow) so the game never waits on the network. The server's result (`LifterState`,
`CarPlaceChanged`, `ParkingSlotUpdate`) goes to **all** clients including the requester: for the requester it
resolves its pending request (a no-op when its prediction already matches, a correction otherwise).

**Pending gate (client):** while the local client has an unanswered request for a loader, a lift or a parking
slot, remote applies that touch the same loader, lift, place or slot wait for that answer. The answer always
arrives next on the same ordered stream (TCP / Steam reliable), so this never waits long; timeout 10 s, then log
and apply. This is what keeps "both players take out the same car" from deleting the winner's copy and keeps a
refused move from blocking the winner's move into the same place.

*Alternatives:* relay relative actions (0.4.17) — drifts on any lost or concurrent action; lock-before-act (ask
the server first) — adds a round-trip to every button press and needs the vanilla coroutine replayed after the
reply.

### 2. Lifts

- **Identity**: index in `GarageLoader.Get().carLifter[]` (the order vanilla saves `carLiftersData` in).
  0.4.17 parsed the loader's GameObject name, which breaks for loader 10+.
- **Which car is on a lift** is not stored separately: it is the loader whose stored place is the lift's place
  (`LifterPlace`: index 0 → `CarLifter1`, 1 → `CarLifter2`; spike 1.2 confirms). The server stores only
  `Lifters[index] = CarLifterState`.
- **Detect**: prefix on `CarLifter.Action(int actionType)` records `GetState()` and `isMoving`; the postfix
  sends `LifterActionRequest { LifterIndex, FromState, ToState }` only if the call started a movement
  (`isMoving` went false→true), so presses vanilla refuses (blocked, player under the lift) send nothing.
  `ToState = FromState ± 1` (`actionType` 0 = up, 1 = down; from 0.4.17 and `LifterButtonTypes`). Spike 3.1
  confirms `isMoving` is set before `Action` returns; fallback is polling `currentState` until it settles.
- **Server**: accepts if `FromState` equals the stored state and `ToState` is one step away. If spike 1.1 shows
  vanilla refuses to move an empty lift, the server also requires a car at the lift's place. Stores and sends
  `LifterState { LifterIndex, State, Instant = false }` to everyone; on refusal it sends the stored value to the
  requester only.
- **Apply** (per-lifter suppression, like `CarSpawnHooks`): wait until `!isMoving`, no own pending request on
  this lift or the car's loader, and the locally connected loader equals the server's (≤ 30 s, then
  `InstantSet` and log). Then step with `Action(0/1)` and wait for each step; if a step does not start moving
  (local blocker, local player inside), `InstantSet(target, true)` and, if `HaveToResetPlayer()`, reset the local
  player like vanilla load does (`FPSInputController.ResetPosition()`). `Instant = true` (snapshot) goes straight
  to `InstantSet`.
- **Reset**: whenever no car stands at a lift's place any more (delete, park, job end, move away), the server
  sets that lift to `OnFloor` without a broadcast; clients' own vanilla code lowers/detaches the lift as part of
  the replicated delete/move. This runs in row 1's server "loader cleared" function (Decision 8), so every
  removal path, including row 3's job end, resets the lift.
- *Fallback:* hooking `MoveUp`/`MoveMedFromFloor`/`MoveMiddleToFloor`/`MoveDown` gives the direction directly,
  if spike 1.1 shows `Action` is not hit.

### 3. Car places

- **Detect**: prefix on `NotificationCenter.ChangeCarPos(CarLoader, CarPlace, bool)` sends
  `CarPlaceChangeRequest { CarLoaderID, FromPlace, ToPlace }`, only when the loader is `Ready` in row 1's state
  machine and has no own pending spawn/unpark (the game also calls `ChangeCarPos` while placing a freshly loaded
  car; those calls must not become requests). `FromPlace` comes from what spike 1.2 shows holds the place
  (`carLoaderGroundPosition[loader]`, `GetPlaceNo()` or `IsInPlace`).
  *Alternative:* `CarLoaderPlaces.ChangeGroundPosition` is lower-level and probably also runs while loading.
- **Store**: the place lives in the loader's spawn record (`LoadedCars[loader]`, row 1's entry) as `PlaceNo` if
  spike 1.2 shows `placeNo` holds the `CarPlace` value, else as a new `Place` field. Keeping it in the spawn
  record means row 1's car replay carries it to a late joiner. A place change never changes the loader id, so
  part and detail records (rows 1 and 4) stay keyed correctly.
- **Server**: refuses if `FromPlace` ≠ stored, another loader's record holds `ToPlace`, or the car stands on a
  lift that is not `OnFloor`. An unknown loader is logged and dropped (no correction: the car is mid-spawn).
  Accept → store, send `CarPlaceChanged { CarLoaderID, Place }` to everyone; refuse → send it with the stored
  place to the requester, whose client moves the car back.
- **Apply**: under per-loader suppression, raise `CarPlacementSync.BeforeRemoteCarMove(loaderId)` (row 6
  subscribes to get a seated player out), then `StartCoroutine(NotificationCenter.ChangeCarPos(loader, place,
  movePlayerToCar: false))`. If spike 1.2 shows it fades the screen or locks input, use the calls it makes
  instead (expected: `CarLoaderPlaces.ChangeGroundPosition` + `CarLoader.PlaceAtPosition` +
  `CarLifter.ConnectCar/DisconnectCar`). If the local player stands inside the destination place, reset their
  position.
- **After any replayed spawn** (live, unpark or snapshot), once `IsCarLoaded()`, the client applies the record's
  place the same way if the car is not already there. This covers the case where `placeNo` alone does not put
  the car at its place.
- **Car moved while someone works on it**: allowed (spec). Parts are keyed by loader, not place.

### 4. Parked cars are opaque `NewCarData` blobs

`NewCarDataCodec` serializes with `Il2CppSystem.IO.BinaryWriter` over an `Il2CppSystem.IO.MemoryStream` using the
current `ProfileData.saveVersion`, and copies `ToArray()` into a managed `byte[]`. The server stores
`ParkedCar { Id (server Guid), CarToLoad, UId, SaveVersion, Data }` per slot. *Alternatives:* a mod DTO for all
of `NewCarData` (dozens of nested types, overlaps rows 1 and 4, the server gains nothing from understanding it);
rebuilding from part state (loses fluids, wheels, paint, tuning). Identity for concurrency checks is the server
`Id`, because `NewCarData.UId` is not guaranteed to be set. `ParkedCar` and the codec are the "car DTO" row 6
uses for purchases.

**Layout** (server has no game code): `Core/Data/ParkingLayout` holds `SlotsPerLevel`, `MaxLevels` and the
new-profile `DefaultUnlockedLevels`, recorded by spike 1.3 from `GlobalData.GetMaxParkingPlacesAmount()`,
`carsOnParking.Length` and a fresh profile. A slot is usable when `slot < UnlockedLevels * SlotsPerLevel`.

### 5. Transfers are single atomic requests

- **Garage → parking**: prefix on `NotificationCenter.MoveCarToParking(CarLoader)` marks the loader "parking"
  (expires after 10 s). While marked:
  - `CarSpawnHooks.DeleteCarHook` (and a hook on `DeleteCar(bool)` if spike 1.4 shows vanilla calls that
    overload) sends no `CarSpawnDelete`.
  - A postfix on the game's `GameDataManager.SaveCarInParking(NewCarData carData, int index)` (or `SaveCar(…,
    toParking: true)`, whichever spike 1.4 shows firing) serializes `carData` and sends
    `CarParkRequest { RequestId, CarLoaderID, PreferredSlot = index, Car, Price = 0 }`.
  - Fallback if no save hook fires within 5 s: diff `carsOnParking` against the mirror (TogetherFixer's idea);
    if that also finds nothing, send `CarSpawnDelete` and log an error.

  Server: refuse if the record `IsJob` (customer cars belong to row 3's job) or the lot is full. Otherwise take
  the preferred slot if free, else the lowest free usable slot; run row 1's "loader cleared" function (drops
  `LoadedCars`, part state, details, resets the lift); store the slot; send `CarSpawnDelete` to the others and
  `ParkingSlotUpdate` to everyone; if the preferred slot was taken, also send the requester `ParkingSlotUpdate`
  for that slot. On refusal (a rare race: the requester's mirror said there was room) send the requester
  `ParkingSlotUpdate` clearing its local slot and row 1's single-car snapshot of the unchanged record with
  `CarData` = the sent blob, so the car comes back with its server records.
- **Car arriving from outside the garage** (`CarParkRequest { RequestId, CarLoaderID = -1, PreferredSlot = -1,
  Car, Price }`, sent by rows 3 and 6 after the client bought/received the car locally with vanilla's money
  deduction blocked): the server **rejects** if no usable slot is free or `Price > Money`; there is no give-back,
  because the client's purchase is simply undone. On success it takes the lowest free slot (or the preferred one),
  deducts `Price` once from the shared money, sends `ParkingSlotUpdate` to everyone and, when `Price > 0`,
  `WorldState`. Either way the requester gets `CarParkResult { RequestId, Accepted, Reason (ParkingFull, NoMoney,
  Invalid), Slot }`. A `CarParkRequest` with a loader id must have `Price = 0` (else `Invalid`).
- **Parking → garage**: prefix on `CarLoader.LoadCarFromFile(int index, bool fromParking)` with
  `fromParking = true` suppresses the `LoadCar` spawn hook for that loader, marks an own pending unpark and, once
  `IsCarLoaded()`, sends `CarUnparkRequest { Slot, ParkedCarId, CarLoaderID, Place, ConfigVersion }`.
  Server: accept if the slot still holds that `Id`, the loader has no record and no other record holds the
  place. Accept = an accepted spawn in row 1's terms: clear the slot, create `LoadedCars[loader]` with
  `CarData`/`CarDataVersion` from the slot, a new `SpawnSeq` and `SpawnedBy` = requester; keep the `ParkedCar`
  in the record until the baseline arrives; send `CarSpawnAck` to the requester, `CarSpawnResponse` to the others
  and `ParkingSlotUpdate` to everyone. On `CarSpawnAck` the requester calls row 1's
  `CarPartsSync.UploadBaseline(loaderId)` (its `LoadCarHook` was suppressed, so nothing else would), which also
  starts row 4's full details snapshot for the new `SpawnSeq`; without it other clients get neither parts nor details. Refuse → `CarSpawnRejected` (the client deletes its copy) plus `ParkingSlotUpdate` for that
  slot to the requester.
- **Unparker disconnects before the baseline**: instead of row 1's delete, the server puts the kept `ParkedCar`
  back into its slot (or the lowest free one), broadcasts `CarSpawnDelete` and `ParkingSlotUpdate`. Only if no
  slot is free does row 1's delete apply (logged as an error).
- **Remote load from blob**: `ProcessCarSpawnResponse` uses `LoadCarFromFile(NewCarData)` when `CarData` is
  set, else `LoadCar(name)` as today, then applies the place (Decision 3).
- **Swap**: postfix on `ParkingCarPlaceManager.MoveCar(int from, int to)` (fallback
  `ParkingManagementWindow.ConfirmSwap(int, int)`) sends `ParkingMoveRequest { From, To, FromId, ToId }` (ids from
  the mirror). The server swaps if both ids match and sends two `ParkingSlotUpdate` to everyone; otherwise it
  sends the requester `ParkingSlotUpdate` for both slots.
- *Alternative:* keep `CarSpawnDelete`/`CarSpawnRequest` and send a separate parking packet — two packets can
  be split by a disconnect or a refusal, leaving the car in both places or neither.

### 6. Remote parking changes are written straight into the profile

Apply `ParkingSlotUpdate { Slot, Car }` by assigning `ProfileData.carsOnParking[slot]` (deserialized blob, or
`new NewCarData()` for empty; spike 1.3 checks `IsDefault()` is true for it), update the mirror (slot → `Id`),
then refresh an open `ParkingManagementWindow` (`RefreshPanels()`, deselect the slot if it was selected).
Vanilla `MoveCar`/`RemoveCar` are never replayed, so no hook re-fires and no slot is picked locally. A blob whose
`SaveVersion` differs from the local `saveVersion` is not loaded (slot stays empty, logged).

`ParkingState { UnlockedLevels, Occupied: slot → Id }` is always complete: the client sets the levels and clears
every slot not listed; listed slots whose `Id` differs from the mirror are filled by the `ParkingSlotUpdate`s that
follow it (snapshot and resync only). A safety net compares `carsOnParking` with the mirror when
`ParkingManagementWindow` hides; a difference not explained by a pending request is logged and answered by a
`ParkingResyncRequest` (server sends `ParkingState` + one `ParkingSlotUpdate` per occupied slot).

### 7. Parking levels; parking scene take-out blocked

- Prefix on `ParkingManagementWindow.Method_Private_Void_Boolean_PDM_0(bool wasAccepted)` (the unlock dialog's
  confirm callback; verified in spike 1.5): when accepted and connected, send `ParkingLevelUnlockRequest
  { TargetLevels = current + 1, Price = parkingLevelPrice }` and skip vanilla. The server (like
  `GarageUpgradeHandler`) unlocks only if `TargetLevels == stored + 1 ≤ MaxLevels` and money ≥ price, deducts
  money, and sends `WorldState` and `ParkingState` to everyone; on refusal it sends `ParkingState` to the
  requester. Clients set `GlobalData.UnlockedParkingLevels` and `profile.globalDataWrapper.UnlockedParkingLevels`
  and refresh an open window (vanilla's own refresh was skipped). The price comes from the client because the
  server has no price table (A4).
- Prefix on `ParkingWindow.MoveCarToGarageAction()` returns false with `UIManager.ShowInfoWindow` while connected.
  That path loads the garage scene with the car while the player is outside the garage, which needs row 6's scene
  tracking to be safe.

### 8. Contract with other rows

Server (all called with `StateLock` held, i.e. from a handler):
- `CarParkRequest` with `CarLoaderID = -1`, `RequestId` and `Price`, answered by `CarParkResult` (Decision 5), is
  the one path for cars arriving from outside the garage; rows 3 and 6 share it instead of their own purchase
  packets. Server-side it uses `ParkingService.TryAdd(ParkedCar car, int preferredSlot, out int slot)` +
  `BroadcastSlot(slot)`, which other server code may call directly.
- `ParkingService.TryRemove(int slot, Guid id)`: for a later sell path (row 10); no client packet in this change.
- Row 1's "loader cleared" function (delete, park, job end, spawner disconnect) calls
  `PlacementState.OnLoaderCleared(loader)` (lift reset). Row 4 drops `CarState.Details[loader]` in the same
  function.
Client:
- `CarPlacementSync.BeforeRemoteCarMove(loaderId)` event for row 6's `EnsureNotSeatedIn`.
- `NewCarDataCodec.ToParkedCar(NewCarData)` for row 6's purchase capture.
- The unparking client is the spawner for rows 1 and 4 (`CarSpawnAck`), so their "own spawn" uploads run.

### 9. Server stores vs. relays

| Data | Server | Packets |
|---|---|---|
| Lift position | **stores** `PlacementState.Lifters[index]` | `LifterActionRequest` → `LifterState` |
| Car place | **stores** in the loader's spawn record (`PlaceNo` or `Place`) | `CarPlaceChangeRequest` → `CarPlaceChanged` |
| Parked cars | **stores** `PlacementState.Parking.Slots[slot]` (blob) | `CarParkRequest`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingResyncRequest` → `ParkingSlotUpdate`, `ParkingState`, `CarParkResult`, `WorldState` (priced arrivals) |
| Parking levels | **stores** `PlacementState.Parking.UnlockedLevels`, money in `WorldState` | `ParkingLevelUnlockRequest` → `ParkingState`, `WorldState` |
| Base data of a car that left parking | **stores** `LoadedCars[loader].CarData` | `CarSpawnResponse`, row 1's snapshot |
| Lift animation, player reposition, UI refresh | relays nothing | client-local |

Saving: `PlacementSection : ISaveSection, ISnapshotProvider` (`[SessionSection]`, key `car-placement`,
`Version = 1`, `Migrate` has no steps yet) serializes `PlacementState` (`byte[]` as base64); `Reset()` = all lifts
`OnFloor`, empty slots, `ParkingLayout.DefaultUnlockedLevels`. On `Load`, a lift whose place holds no car is reset to
`OnFloor`. Places and `CarData` are fields of row 1's `cars` section (additive, no version bump; a later rename
needs a `cars` version step owned by row 1). Every handler, the unpark-disconnect rule (runs inside
`Client.Disconnect`) and `ParkingService` run with `StateLock` held; this change adds no lock of its own.

### 10. Late-join path

1. Client loads the garage with an empty profile and sends `AskForSync`; the server builds the snapshot under
   `StateLock` (row 7 D8).
2. `cars` (100, row 1): each car's spawn info carries its place and `CarData`, so it loads from the right base
   data and is moved to its place after load (Decision 3).
3. `car-placement` (200, this change): `SendSnapshot` sends `ParkingState`, one `ParkingSlotUpdate` per occupied
   slot, then `LifterState { Instant = true }` per lift not on the floor, and returns 1 + occupied slots + raised
   lifts; row 7 puts that count into `SyncEnd.Items["car-placement"]`.
4. The client reports `SyncTracker.Applied("car-placement")` per item when applied: `ParkingState` and slot
   updates at once; a lift once its car is `Ready` and connected and `InstantSet` ran (own 25 s wait, then floor +
   error, still counted, so it never trips row 7's 30 s no-progress timeout). No `IsParkingSynced` flag.
5. Live packets after the snapshot are behind it on the same stream; per-loader gates (row 1's `Ready`, the
   pending gate) order them. A repeated `AskForSync` after a garage reload (row 6 travel) gets the same section.

### 11. Packet types

New `PacketTypes` values are appended at the end of the enum (other rows append too; resolve by merge order,
never renumber existing values). Direction: `*Request` client→server; `LifterState`, `CarPlaceChanged`, `CarParkResult`,
`ParkingSlotUpdate`, `ParkingState` server→client.

## Risks / Trade-offs

- [IL2CPP inlining: a hooked method called from native code may never hit the detour] → every hook gets a spike
  that logs whether it fires on the real UI path, with a named fallback; the hide-time parking diff (Decision 6)
  catches what slips through and resyncs.
- [`NewCarData` serialization through Unhollower streams fails or differs between game builds] → spike 1.3
  round-trips a car before anything else is built; the blob carries `SaveVersion`.
- [`placeNo` is not the `CarPlace` value] → spike 1.2; fallback is a separate `Place` field plus the post-load
  place apply.
- [Blob size: a car may be tens of KB] → one car per packet; Steam's reliable message limit (512 KB) is far above
  it; spike 1.3 logs the size.
- [Parts changed by another player in the last moments before parking are lost: the blob is the parking
  client's view] → accepted for v1; the server logs part changes that arrive for a loader that was just parked.
- [Remote lift lowered onto the local player] → vanilla blocks it locally; we fall back to `InstantSet` and
  reset the player like vanilla load does. Visible pop, but no desync.
- [Trusting the client's parking level price] → co-op only; the server checks money and that the level is the
  next one, so the worst case is a wrong price, not a free or double unlock.
- [`Method_Private_Void_Boolean_PDM_0` is an obfuscated name] → spike 1.5 verifies it; fallback is a postfix on
  `UnlockParkingLevelAction` plus a before/after compare of `GlobalData.UnlockedParkingLevels` on window hide.

## Migration Plan

Clients and server must run the same build (existing mod version check). A save without the `car-placement`
section gets `Reset()` (row 7 D2); old car records have no `CarData` (cars spawn from their name as today).
Rollback = previous build; row 7 keeps unknown sections as raw JSON.

## Open Questions / Assumptions

Decided without the user (the user can override):

- **A1** Parking scene take-out is blocked while connected (Decision 7) rather than synced. Revisit with row 6.
  (Listed in QUESTIONS.md.)
- **A2** Moving a car is never blocked because someone works on it; only an occupied target or a raised lift
  refuses it.
- **A3** `sync-car-parts` provides: spawn info in its snapshot (so place and `CarData` reach a late joiner), the
  per-loader `Ready` state, `CarSpawnAck`/`SpawnedBy`, the single-car snapshot, a server "loader cleared"
  function, and the spawner-disconnect rule that this change overrides for unparked cars.
- **A4** The parking level price comes from the client; a server price table can come later.
- **A5** Customer (job) cars cannot be parked while connected (the server refuses); vanilla probably does not
  offer it anyway (spike 1.4 checks). Cars from outside the garage (rows 3 and 6) use `CarParkRequest` with
  `CarLoaderID = -1`, `RequestId` and `Price`; a full lot or too little money rejects the request and the client
  undoes its purchase (no car comes back).

Deferrable unknowns (answered by the spikes in tasks.md, they do not change the approach): which lift
coroutine maps to which transition; whether an empty lift can move; whether `ChangeCarPos` fades the screen;
which save method fires on parking; whether `LoadCarFromFile(NewCarData)` internally calls `LoadCar(string)`
(either way it is suppressed).
