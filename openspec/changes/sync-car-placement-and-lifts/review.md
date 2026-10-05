# Review: sync-car-placement-and-lifts (2026-10-05)

All game types and methods named in the draft exist in the decompiled stubs with the stated signatures
(`CarLifter`, `CarLifterState`, `LifterButtonTypes`, `CarPlace`, `GarageLoader.carLifter`, `NotificationCenter`,
`CarLoader`, `CarLoaderPlaces`, `NewCarData`, `ProfileData`, `ParkingCarPlaceManager`, `ParkingManagementWindow`,
`ParkingWindow`, `GlobalData`, `NewGlobalDataWrapper`, `FPSInputController.ResetPosition`). Repo classes referenced
(`CarSpawnHooks`, `CarHandlers.ProcessCarSpawnResponse`, `CarSpawnRejected`, `OnAskForSync`, `GarageUpgradeHandler`)
exist. `openspec validate --strict` passes.

## Findings

### Blocker

1. **Save and late join bypassed row 7's contract.** The draft sent straight from `OnAskForSync`, added
   `ClientData.IsParkingSynced` + `WaitForSyncCompletion`, moved `SyncEnd` itself and relied on "the existing JSON
   save". *Changed* (also per the coordinator's contract update): `PlacementSection` is a versioned `ISaveSection` +
   `ISnapshotProvider` (key `car-placement`, `SyncOrder` 200, `SendSnapshot` returns the item count for
   `SyncEnd.Items`); the client counts `SyncTracker.Applied("car-placement")`; no own flag; the lift wait (25 s) stays
   under row 7's 30 s no-progress timeout; all server code runs under `GameDataManager.StateLock` (incl. the
   disconnect rule inside `Client.Disconnect`). Design Context/9/10, tasks 3.5, 6.1–6.2.
2. **Unpark ignored row 1's spawn lifecycle.** No `SpawnSeq`/`SpawnedBy`/`CarSpawnAck`, so no baseline would be
   accepted, and row 1 deletes a car whose spawner disconnects before its baseline — the unparked car would be lost.
   *Changed* (confirmed by the sync-car-details review): an accepted unpark is a row-1 spawn with a new `SpawnSeq`
   and `SpawnedBy` = unparker; on `CarSpawnAck` the unparker calls `CarPartsSync.UploadBaseline(loaderId)`, which
   also triggers row 4's details snapshot (its `LoadCarHook` is suppressed, so nothing else would). The server keeps
   the `ParkedCar` until the baseline and puts the car back in parking if the unparker disconnects first (new
   requirement + scenario, tasks 3.3, 3.4, 5.3, step in 7.3).
3. **Race bug in "both take out the same car".** The winner's `CarSpawnResponse` for the same loader arrives before
   the loser's `CarSpawnRejected`, so the loser would load the winner's car and then delete it. Same pattern for two
   moves into one place. *Changed:* client pending gate (Decision 1, task 4.1): remote applies touching a loader,
   place, lift or slot with an own unanswered request wait for that answer.

### Major

4. Accepted requests were broadcast "to others" only, so the requester could never resolve a pending request.
   *Changed:* results go to everyone; a no-op when the prediction matched.
5. Lift apply dropped a lift to the floor when its car was briefly elsewhere (refused move in flight), and the
   stored `LifterRecord.CarLoaderID` could disagree with car places. *Changed:* the car on a lift is derived from the
   stored places (`CarLifter1/2`); the server stores only the state; apply waits for the right car.
6. `ParkingState` was overloaded ("levels only" would have cleared every slot by the join rule) and the safety net
   needed a request packet that did not exist. *Changed:* `ParkingState` always carries full occupancy;
   new `ParkingResyncRequest`; a stale swap gets two slot updates.
7. Park detection by a 5 s diff although the game's `GameDataManager.SaveCarInParking(NewCarData, int)`
   (CallerCount 10) / `SaveCar(…, toParking)` pass slot and data. *Changed:* save hook is primary, diff is fallback;
   spike also covers `DeleteCar(bool)` (the existing hook only patches `DeleteCar()`).
8. The server had no source for slots per level, max levels or the starting level count. *Changed:*
   `Core/Data/ParkingLayout` filled by spike 1.3.
9. Cars arriving from outside the garage (rows 3 and 6) had no money or answer path, and the draft's lot-full
   "car comes back" rule cannot apply to a car bought elsewhere. *Changed* (per the sync-players-and-scenes review):
   `CarParkRequest` gains `RequestId` and `Price`; the `CarLoaderID = -1` branch rejects on full lot / short money
   with no give-back (the client undoes its purchase), else deducts `Price` once and broadcasts; new `CarParkResult`
   to the requester; new spec requirement, harness `park-incoming`, steps in 7.4. Behind it sits
   `ParkingService.TryAdd/TryRemove/BroadcastSlot`; lift reset hooks into row 1's "loader cleared" function so job end
   (row 3) also resets lifts.
10. Race scenarios were not deterministic (harness command polling is slower than localhost RTT, so "both press up"
    usually becomes two sequential presses). *Changed:* harness `net-hold on|off` buffers incoming packets; races in
    7.2/7.4 use it.
11. `placeNo` may not be the `CarPlace`; a replayed car would then stand at the spawn place. Also the game calls
    `ChangeCarPos` itself while placing new cars. *Changed:* place applied after every replayed spawn; the hook only
    fires for `Ready` loaders without pending spawn; unknown loaders are dropped without a correction.
12. Unpark did not check the garage place, only the loader. *Changed:* server checks both; new spec scenario.
13. Spikes came after the code that depends on them. *Changed:* tasks reordered, spikes are group 1.

### Minor

14. Row 6 expects row 2 to call `EnsureNotSeatedIn`; row 6 lands later. *Changed:* `BeforeRemoteCarMove` event.
15. Empty-lift movement unknown; draft spec forbade it. *Changed:* spec requirement reworded, spike 1.1 decides.
16. Level unlock skips vanilla, so the window would not refresh. *Changed:* handler refreshes it (task 5.5).
17. Restart check was manual. *Changed:* automated in 7.3 with row 7's `Send-ServerCommand save`/`Stop-TestServer`/
    `Start-TestServer`.
18. Lot-full path had no scenario; stale swap, refused unlock had no spec scenario. *Changed:* `car-parking-full.ps1`
    and two scenarios.
19. Customer cars being parked would orphan row 3's job. *Changed:* server refuses (A5, question below).
20. `RemoveCar`/`GetParkingFreePlaceIndex` have CallerCount 0 (dead or inlined): not used as hooks. `ChangeCarPos`,
    `PDM_0`, `*Action()` are 0 because they are delegate targets, which still hit a detour. Noted in design.
21. QoLmod parking extras bypass these hooks. *Left* for row 9 (non-goal).
22. `Wait-HarnessDump` is also task 6.3 of `sync-orders-and-jobs`. *Changed:* "unless row 3 already added it".

## Needs fixing in other changes

- `sync-car-parts/design.md` D6/D9 + tasks 2.2–2.6: a server "loader cleared" function used by delete, park, job end
  and spawner disconnect (rows 2 and 4 hook into it); the spawner-disconnect delete must let row 2 return an unparked
  car to parking; snapshot spawn info must carry `CarData` and the place; an unpark acceptance sends `CarSpawnAck`.
- `sync-car-details/design.md` A2 is moot (a car never changes loader; place changes keep the loader id). D7/D8: also
  drop `Details[loader]` on park (via the "loader cleared" function) and treat the unparking client (`CarSpawnAck`) as
  the spawner, since its `LoadCarHook` is suppressed.
- `sync-players-and-scenes/design.md` D7: subscribe to `CarPlacementSync.BeforeRemoteCarMove` instead of "row 2
  calls it". D8 / task 2.3, 5.6: replace `CarPurchasePacket`/`CarPurchaseResultPacket` with `CarParkRequest`
  (`CarLoaderID = -1`, `RequestId`, `Price`) + `CarParkResult`, built with `NewCarDataCodec.ToParkedCar`; reasons
  are `ParkingFull`/`NoMoney`/`Invalid` (its `Unavailable` only applies while row 2 has not landed).
- `sync-orders-and-jobs/design.md`: job end removes the loader through row 1's "loader cleared" function so the lift
  resets; any car it delivers to parking uses the same `CarParkRequest` -1 branch.
- `openspec/ROADMAP.md` integration note "Parking API": add `RequestId`/`Price`, `CarParkResult`, reject-on-full
  for -1, and the server API `ParkingService.TryAdd/TryRemove`.

## Open questions for the user

1. Customer (job) cars cannot be moved to parking while connected. Default: yes, refused (vanilla probably does
   not offer it anyway).
2. The parking level price is taken from the client (server checks money and "next level only"). Default: yes until
   an economy audit (row 10) adds a server price table.
3. Part changes another player makes in the last moment before a car is parked are lost. Default: accept for v1.

## Integration pass (2026-10-06)

- D2/D5/D8, tasks 3.1, 3.3, 3.4: the "loader cleared function" is row 1's `ClearLoader(loader, reason)` + event
  `LoaderCleared`; park calls `ClearLoader(Parked)`, unpark `RegisterSpawn`; the lift reset and the unparked-car
  return subscribe to the event (row 1 deletes, this change puts the `ParkedCar` back). Row 4 needs no call (lazy purge).
- D5/D8: the `-1` branch is used by row 6 only (customer cars cannot be parked while connected — user decision —
  so row 3 aborts the take instead). Client `CarParkResult` handler raises `ParkingSync.ParkResultReceived` for row 6
  (task 5.1). A5 and open question 1 marked as accepted.
- D8 / task 4.4: live handlers go through row 6's `ClientScene.GarageBound` (parking `mirrorOnly` while away).
- Task 7.1: `Wait-HarnessDump` is row 6's (no "unless row 3" fallback). Prerequisites name row 6 part 1 and the
  contract as groups 1–2. D10 reference fixed (row 7 D4).
