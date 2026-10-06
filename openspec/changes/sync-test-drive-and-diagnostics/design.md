# Design

## Context

See proposal.md for the why and specs/test-drive-sync/spec.md for the required behaviour. Facts that shape the
approach, from `docs/spikes/test-drive.md` (static decompile, 2026-10-06), `docs/spikes/workshop-car-tools.md`,
`docs/spikes/car-details.md` and the code on `main` (rows 1, 2, 3, 4, 6, 7, 14 built):

- **Only the test track changes scene.** The test path (`CarPlace.DiagnosticPath` 7) and the dyno (`CarPlace.Dyno` 6)
  run in the garage. The "benchmark" is the main-menu graphics benchmark (`BenchmarkManager`) and cannot be reached
  while connected.
- **Departure:** both UI paths (map while seated: `MapWindow.VerifyCarStateIfInterior`; map on foot:
  `SideCarsPanel.DriveAction`) set `GlobalData.SelectedCarLoader` (the loader's save name, `#CarLoaderN`; index =
  `Helper.GetIndexFromCarLoaderName`) and `GlobalData.TestToShow = "ExamineReport"`, then call
  `NotificationCenter.SelectSceneToLoad("Test_track_1", SceneType.TestTrack, true, true)`. That coroutine calls
  `CloseCar(true)` on the selected car, runs `GarageLoader.Save()` (in-memory profile; `SessionGuard` blocks only the
  disk write) and loads the track, which loads its own copy of the car from the in-memory profile.
- **On the track:** `PrepareCarPhysics.CalcDistance` counts metres and dirties the track's car every 500 m. No part
  wear (conditions are only read). `TestTrackManager.DoneTest` → `ReturnToGarage` (vtable) after the last test, or
  the pause menu's track button. `TrackManager.ReturnToGarage` calls `PrepareCarPhysics.SaveMileage(false)`
  (`GlobalData.NewMileage = max(1, metres / 1000)` km, track dust into the profile slot), sets `TestToShow = ""` when a
  test is unfinished, then `SelectSceneToLoad("garage", Garage, true, true)`.
- **Return:** vanilla's garage load adds `NewMileage` to the selected car inside the per-loader load loop and then,
  when `TestToShow == "ExamineReport"`, opens `ExamineReportWindow`, whose `GetExaminedParts` finds the car by
  `SelectedCarLoader`, clears both globals and calls `PartScript.Examine(true)` on the mounted parts of examine groups
  {2, 20, 24}. So the examined parts are produced **in the garage after the return load**, not on the track.
- **The mod's return load** (`LoaderAddition.CustomLoad`) is a full late join: `VanillaLoad` (without the per-loader
  loop: every loader is cleared and the cars come from the server snapshot), `ClientData.Reset()`, `AskForSync`,
  wait for `IsInitialSyncFinished`, then the copied ExamineReport block. The `Mileage += NewMileage` step is missing,
  so the drive's mileage is lost, and the leftover static `NewMileage` survives a disconnect into the player's next
  single-player garage load, where vanilla adds it to whichever car matches `SelectedCarLoader`.
- **Row 6:** `SceneHooks.SelectSceneToLoadPrefix` raises `ClientScene.LeavingScene(from, to)` while the scene being
  left is still loaded, before it publishes `Loading` (only when connected and `IsInitialSyncFinished`, which stays
  true on the track: only `CustomLoad` resets it). Server `PresenceEvents.SceneChanged(id, from, to)` and `Left(id)`
  exist. Row 14a's guard prefixes on the same method run at `Priority.First`; other prefixes take `__runOriginal`.
  `GuardHooks` treats `CarDrive`, `PathTest`, `Dyno` as log-only modes; `TestTrack` (scene), `PathTest`/`Dyno`
  (windows, modes), `ExamineTools` (mode) and the pies `move_pathTest`, `move_dyno`, `obd`, `cylinder`, `fuel`,
  `electronic`, `tires` are `Planned(..., "row 13")`. `Window ExamineReport` is already allowed (row 3).
- **Row 1:** part claims (`CarClaims` server, `PartClaims` client) are per part key, expire after 120 s and are
  released when the holder leaves the garage (`CarClaims.OnSceneChanged`). `PartHooks.AfterExamine` /
  `AfterExamineAll` mark a part dirty, so any `Examine(true)` on a `Ready` car reaches everyone. The server sends
  active claims after each car in the `cars` snapshot without counting them. `CarsSnapshotProvider.SendCar` resends one
  car. `CarPartsStore.LoaderCleared` fires on delete, park, job end and spawner-left.
- **Row 4:** `ModCarDetails` per loader with sections; `Info` (`Mileage`, `BuyPrice`, `CarFrom`) is polled at 1 Hz,
  `BodyCosmetics` (per `CarPart` index: paint, livery, tint, `Dust`, `WashFactor`) is commit-driven. The server
  (`CarDetailsStore`) clamps, merges (sections replace, keyed entries merge), relays to everyone including the sender
  and puts valid records in the `car-details` snapshot (`SyncOrder` 150). Row 4 left dyno values to this row.
- **Dyno:** `DynoManager.RunDyno` → `PrepareDyno` backs up `EngineData`/`MeasuredDragIndex`, writes preview values,
  `CloseCar(true)`, `GameMode.Dyno`, `DynoWindow`. `<StartDyno>d__32` sets `DynoMeasured = true`.
  `DynoManager.CloseDyno` (from `DynoWindow.HideAction`, `PlaceAtPosition`, `ChangeCarPos`) restores the backups when
  not measured, else sets `EngineData.measured = true`. `EngineData` is a plain struct (`isElectric`, rpm/torque
  curve floats, frictions, `limiterTriggerRpm`, `tuningValue`, `measured`), saved in `NewCarData` together with
  `measuredDragIndex`. `CarLoader.MeasurePower()` (map "measure power", only before a drag strip) computes the same
  without UI; row 5b hooks it later.
- **Test path:** pie `move_pathTest` moves the car to `DiagnosticPath` (row 2's place sync) and sets `specialState =
  0`; `RunPathTest` → `PathTestManager.Prepare` (player inside, `GameMode.PathTest`); `<EndAllTests>d__50` sets
  `specialState = 1`; `<ExitFromCar>d__47` → `GameScript.ExitFromInterior` shows the ExamineReport → examine groups
  {4, 16, 28, 40}. `specialState` is saved per loader by `CarLoaderPlaces.Save`; on load, `1` puts the car at
  `PathTestManager.places[1]` (the "after the test" spot).
- **OBD and the other examine tools** (`ObdScanner.<UseAnim>d__2`, compression, multimeter, tire tread, compound
  meter): `AddPlayerExp(1)` for a newly examined part (row 7's `StatsHooks` makes XP shared), then
  `PartScript.Examine(true)`. Nothing else is car state.
- **Jobs (row 3):** nothing writes `JobPart.Found`; task progress is recomputed from synced car state, so examined
  flags synced by row 1 are the job progress. A finished job clears its loader (`ClearLoader(JobEnded)`).

## Goals / Non-Goals

**Goals:**
- A car on the test track, the test path or the dyno belongs to one player until the activity is over; others see
  that and cannot change the car, so nothing diverges.
- Everything the activity produces that is car state (mileage, dirt, examined flags, dyno values, `specialState`)
  reaches the server and every player, and survives the driver's return snapshot, a late join and a server restart.
- Vanilla's mileage step works again while connected and can never apply twice or leak into single-player.
- The guard entries of these features are opened by this change's merge commit.

**Non-Goals:**
- Seeing the car being driven (on the track or in the garage): row 17 (`remote-visual-feedback`).
- Other tracks (race, fun, off-road, drag strip, custom, speed): stay blocked (backlog). So does the map's
  "measure power" path, which only serves the drag strip; row 5b hooks `MeasurePower` and calls this row's
  `DynoSync.Commit`.
- Money and XP of the tools (OBD XP is already shared through row 7's `StatsHooks`; fees are row 10's).
- Steam stats `stat_finish_testtrack` / `stat_finish_testpath`: local to the driver, like vanilla.
- Part state, details storage, places and the snapshot pipeline themselves (rows 1, 4, 2, 7); this change adds
  checks, one details section and one spawn-record field.
- Two players on the track together: each drives their own car; avatars follow row 6's presence rules.

## Decisions

### D1. One server-held "away" claim per car, separate from part claims

The server keeps `CarAwayRegistry` (runtime only, under `StateLock`): `loader → CarAway { SpawnSeq, Owner, Kind
(TestTrack, PathTest, Dyno), SinceUtc }`. A request is granted when the loader has a record with a baseline and the
same `SpawnSeq`, no away claim of another player, and no part claims of another player (`CarClaims.Held`); the
owner's own part claims are released on grant. Grants and releases go to everyone as `CarAwayUpdate`; a refusal goes
to the requester only, with `CarAwayRefusal` (`Busy` = someone else's away claim, `InUse` = part claims, `NotReady`,
`Unknown`). Clients keep a mirror (`CarAwaySync`), which is all a lock check reads.

*Alternatives:* (a) a "whole car" key inside row 1's `CarClaims` — its lifetime is wrong: row 1 releases claims when
the holder leaves the garage and after 120 s, which is exactly when a test drive starts; changing that rule for one
key mixes two lifetimes in one table. (b) No claim, only result sync — other players' changes during the drive would
be silently overwritten by, or race with, the results (upstream's bug class).

### D2. How a claim is taken: ask first for the track, optimistic in the garage

- **Test track (ask first):** a prefix on `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` (after
  the guard, `__runOriginal`) with `sceneType == TestTrack` and connected: if the loader of `SelectedCarLoader` has no
  granted claim of ours, return an empty enumerator (as the guard does), store the call's arguments, send
  `CarAwayRequest { RequestId, CarLoaderID, SpawnSeq, Kind = TestTrack }`. On grant, re-run the stored call with
  `NotificationCenter.m_instance.StartCoroutine(...)`; the prefix lets it through because the claim is now held. On
  refusal or no answer within 5 s: an info message ("<name> is test-driving / working on this car"), `GlobalData.
  SelectedCarLoader`/`TestToShow` cleared, input and pie menu restored the way a cancelled map selection leaves them
  (spike 1.3 records what that is). The prefix runs before the coroutine body, so nothing (fade, `loadingScene`, input
  lock, save) has happened yet.
- **Dyno and test path (optimistic):** the client checks its mirror before the activity (a locked car refuses with a
  toast, D3) and sends the request when the activity starts (postfix on `DynoManager.RunDyno`, on
  `PathTestManager.Prepare`). A refusal only happens when two requests race; the client then aborts: dyno →
  `DynoWindow.HideAction` (nothing measured, so `CloseDyno` restores the backups); path test → the exit path spike 1.4
  finds that leaves without examining (fallback: let it run and drop the result — the release carries no
  `specialState` and the client resyncs the car with row 14's `resync`).

*Alternatives:* ask first everywhere — adds a round trip and a replay of `RunDyno`/`RunPathTest` (UI local functions
on display classes) for a race the mirror already makes rare; optimistic for the track — a refusal would arrive
during the loading screen, with no way back except a second scene change.

### D3. Others see the car as away and cannot edit it

- **Client lock checks** (`CarAwaySync.LockedForMe(loader, out owner, out kind)`), added in front of the existing
  claim checks: `PartClaims.AllowAction` / `AfterCanTakeOffCarPart`, `EngineCraneHooks`, row 2's car move, lift action
  on the lift the car stands on and park hooks, row 3's job-end hook for the job whose car it is, and the dyno/path/
  examine-tool starts. A blocked action shows a toast ("<name> has this car on the test track"). Row 4's poll skips a
  locked loader (a local change made through a tool row 4 has no commit hook for is not sent; the server would refuse
  it anyway).
- **Server enforcement** (the backstop for paths without a client check): for a claimed loader, from anyone but the
  owner, the server drops `CarPartClaim` (answering with `CarPartClaimUpdate` naming the owner), `CarPartsChange`
  (answering with `CarsSnapshotProvider.SendCar(sender, loader)`), `CarDetailsUpdate` (answering with the stored
  record), `CarPlaceChangeRequest`, `LifterActionRequest` for its lift, `CarParkRequest` (their existing refusals),
  `CarSpawnDelete` (answering with `SendCar`) and `JobEndRequest` for its job (dropped and logged). Examining is not
  an edit and stays allowed (examined flags only ever become true).
- **Visible as away:** the car stays where it is on everyone's screen (the track loads its own copy; the garage copy
  of other players is untouched). A label above the car ("<name> — test drive / test path / dyno") is drawn with the
  same IMGUI path as row 6's `NameTags`, within the same distance limit.

*Alternative:* hide the car while it is on the track — needs a despawn and a respawn from the snapshot on return for
every other player, which is row 1 work for a cosmetic gain; a visible locked car also tells others why they cannot
use the place.

### D4. Test drive results are sent on `LeavingScene(TestTrack, Garage)`; examined parts are not

- **Before leaving the garage** (`LeavingScene(Garage, TestTrack)`, claim already held): flush pending part changes
  of that loader (row 1's tracker) and pending detail changes (row 4's dirty set; add `CarDetailsSync.FlushNow(loader)`
  if it is not on `main`), so the server's record is complete before the scene change. Changes produced by
  `CloseCar(true)` inside the departure coroutine come after `LocalScene = Loading` and are not sent (spike 1.2
  confirms nothing leaks; the server's record is the truth anyway).
- **Leaving the track** (`LeavingScene(TestTrack, Garage)`): `SaveMileage` has run and the track scene is still
  loaded. Send `TestDriveResult { CarLoaderID, SpawnSeq, MileageDeltaKm = GlobalData.NewMileage, Cosmetics }` where
  `Cosmetics` = row 4's `CarDetailsIO.Read(trackCar, BodyCosmetics)` on `TrackManager.Instance.carPhysics.carLoader`
  (the same data and part order as the garage car, spike 1.2 checks the order; on a mismatch only `Dust`/`WashFactor`
  of matching indexes are sent, else none). Sent also when the player leaves through the pause menu; `MileageDeltaKm`
  is then whatever `SaveMileage` wrote.
- **Server:** if the claim is the sender's `TestTrack` claim with the same `SpawnSeq` and the details are valid:
  `Info.Mileage += clamp(MileageDeltaKm, 0, 2000)`, merge `Dust`/`WashFactor` of the cosmetics (other cosmetic fields
  keep the stored values, so a stale paint value can never ride along), relay one `CarDetailsUpdate { Info,
  BodyCosmetics }` to everyone through `CarDetailsStore`'s normal path, answer `TestDriveResultAck { Applied = true }`.
  Otherwise `Applied = false` with a log line (car gone, claim lost, details not valid yet).
- **Client on the ack:** `Applied` → `GlobalData.NewMileage = 0`; not applied → keep it for D5's fallback.
- **Examined parts** come from the return examine report (D6), not from the track. This corrects row 6 D6's note
  ("results (examined parts, mileage, …)").

*Alternatives:* send the whole track car (`CarDetailsIO.All` + parts) — the track only changes mileage and dirt, and
anything else read from the track's copy would be stale for any change other players made before the departure
flush; merge on return on the client — rejected by row 6 D6 (stale merges, per-field rules in every row).

### D5. The mileage-loop fix in `LoaderAddition.CustomLoad`

After `IsInitialSyncFinished` and before the ExamineReport block:

1. If `GlobalData.NewMileage != 0` (no result was sent, or the server did not apply it): find the loader whose
   `GetSaveName() == SelectedCarLoader`, wait until `CarPartsSync.IsReady(loader)` and row 4 is not applying to it
   (max 20 s), then vanilla's step: copy `CarInfoData`, `Mileage += NewMileage`, assign. Row 4's `Info` poll uploads it
   within a second. If no such car exists, drop the value with a log line.
2. `GlobalData.NewMileage = 0` in every case.

Also `NewMileage = 0` when the client disconnects (row 8's `ResetAfterFailure` / disconnect path), so a value can never
reach the player's own single-player garage. Vanilla's single-player load is untouched (`GarageStartOverride` runs
vanilla when not connected).

*Alternative:* restore the original loop position (inside `VanillaLoad`, before the snapshot) — the snapshot replaces
the car afterwards, so the value would be lost again; apply locally always and never on the server — the server's
record and other players would lag until the poll, and the value would be lost if the driver disconnects during the
return load.

### D6. The return stays a late join; the claim carries the car through it

The return runs `CustomLoad` unchanged up to the sync (user decision "return to the garage = late join"). The
snapshot contains the folded results (D4: the result is sent before `AskForSync` on the same ordered stream and the
server handles both in order under `StateLock`) and the driver's own claim (D10), so the driver is not locked by it.
Then:

- `CustomLoad` waits (max 20 s) until the car of `SelectedCarLoader` is `Ready` before opening the ExamineReport, so
  `GetExaminedParts` examines the snapshot car and row 1's `AfterExamine` sends the flags. The claim guarantees that
  the car is still on that loader (no move, park, delete or job end by others while away).
- A postfix on `ExamineReportWindow.GetExaminedParts` (or, when `TestToShow` was empty because a test was
  unfinished, the end of `CustomLoad`) sends `CarAwayRelease { CarLoaderID, SpawnSeq, SpecialState = -1 }` after the
  part tracker has flushed the examined flags (same stream, so the flags reach the server first).
- If the car is not `Ready` within 20 s, the report opens anyway (vanilla behaviour on whatever it finds), the claim
  is released and the timeout is logged.

### D7. Dyno: commit on `CloseDyno`, stored as a row 4 details section

- Prefix on `DynoManager.CloseDyno` records `DynoMeasured` and the car; postfix, when it was measured, calls
  `DynoSync.Commit(carLoader)` and then releases the claim. `Commit` marks row 4's new section `Dyno` dirty and
  flushes it now. Not measured → release only.
- `CarDetailSection.Dyno = 512`, `ModCarDetails.Dyno = ModDynoResult { ModEngineData Engine; int MeasuredDragIndex }`
  with every `EngineData` field. `CarDetailsIO`: read copies the struct; apply copy-assigns `CarLoader.EngineData` and
  sets `MeasuredDragIndex` (plain fields, no side effects; `DynoWindow` reads them on open). `Dyno` is in `All` (spawn
  baseline, snapshots) but not in `Polled`, and `Read` skips it while the local dyno is open on that car (preview
  values).
- Server: `CarDetailsStore` merges `Dyno` as a whole section and clamps it (finite floats, `MeasuredDragIndex` ≥ 0).
  It is saved with row 4's details in the `cars` section as an additive field (no version step; null = nothing to
  apply), reaches late joiners through the `car-details` snapshot and is purged with the car.
- `DynoSync.Commit(carLoader)` is public for row 5b's `MeasurePower` postfix (its task 10.1).

*Alternatives:* a separate `DynoStore` and packet — duplicates row 4's per-loader storage, `SpawnSeq` validation,
relay, snapshot and purge; polling `EngineData` — leaks the dyno's preview values and the restore on cancel.

### D8. Test path: claim for the run, `specialState` with the release

- Moving the car to `DiagnosticPath` is row 2's place change (no claim; a claimed car cannot be moved by others).
- Postfix on `PathTestManager.Prepare` → claim `PathTest` (D2). The examined flags come from the ExamineReport that
  `ExitFromInterior` opens (row 1, as in D6). Postfix on `<EndAllTests>d__50.MoveNext` when it finishes notes
  `specialState = 1`; the `GetExaminedParts` postfix (examine type `PathTest`) then sends `CarAwayRelease {
  SpecialState = 1 }`. A run left before the end sends `SpecialState = -1` (unchanged).
- Server: stores `SpecialState` in the loader's spawn record (`CarSpawnResponsePacket.SpecialState`, additive like
  row 2's `Place`), sets it back to 0 when row 2 accepts a place change for that car (vanilla's `ConnectCarLoader`
  does the same), and relays it in the `CarAwayUpdate` of the release.
- Clients: set `carLoader.specialState` and, when 1 and the car is at `DiagnosticPath`, call
  `PathTestManager.SetCarPositionAfterLoad(true)` if spike 1.4 shows it only moves the car; else they only set the field
  (the car keeps its spot until its next move, a cosmetic difference, listed in STATUS.md). Snapshot spawns apply the
  field after the car is loaded, like row 2's place.

### D9. OBD and the other examine tools need no claim

Their only car effect is `PartScript.Examine(true)`, which row 1 already sends and which can only turn a flag on, so
two players examining at once converge. This change only verifies them in two instances and opens their guard
entries. The test-track examine report, the path-test report and the tools all share the same hook.

### D10. Release and failure rules

| Event | Effect |
|---|---|
| `CarAwayRelease` from the owner | released, `CarAwayUpdate { Released }` to everyone (with `SpecialState` when set) |
| `PresenceEvents.Left(owner)` (disconnect, quit to menu, kick) | all its claims released; nothing else changes (a test drive's results are lost, the car stays as it was) |
| `PresenceEvents.SceneChanged(owner, from, to)` | `TestTrack` claim: kept for `Garage → Loading → TestTrack → Loading → Garage`, released for any other target; `PathTest`/`Dyno`: released when `from == Garage` |
| `CarPartsStore.LoaderCleared(loader)` | claim dropped (cannot happen while the checks hold; covers a server-side clear) |
| Watchdog (server tick) | a `TestTrack` claim whose owner is back in the garage and `InSession` for 60 s, or a `PathTest`/`Dyno` claim older than 15 min, is released and logged |
| Client watchdog | `PathTest`/`Dyno`: when `GameMode` has been neither that mode nor `UI` for 5 s and no report is open, release |
| Server restart | claims are runtime only; everyone is disconnected anyway |

A `TestTrack` claim has no time limit while the owner is on the track.

### D11. Guard entries

In the merge commit (`GuardRules`): `Allow` with owner `"row 13"` for `Scene TestTrack`, `Window PathTest`, `Window
Dyno`, `Mode PathTest`, `Mode Dyno`, `Mode ExamineTools`, `Pie move_pathTest`, `move_dyno`, `obd`, `cylinder`, `fuel`,
`electronic`, `tires`, and any window or mode of the track flow that the trace (task 1.2, `guard-log` on `Enforce`)
shows as blocked. `Window`/`Mode Benchmark` move to the backlog owner (still denied). Other tracks stay denied. The
scenarios run with the guard on `Enforce` and never use `guard-allow` for these entries after group 6.

### D12. Server stores vs. relays

| Data | Server | Packets |
|---|---|---|
| Away claims | **stores** runtime-only `CarAwayRegistry` | `CarAwayRequest` → `CarAwayUpdate`; `CarAwayRelease` → `CarAwayUpdate` |
| Mileage, track dirt | **stores** in row 4's `Details[loader].Info` / `BodyCosmetics` | `TestDriveResult` → `CarDetailsUpdate` (everyone), `TestDriveResultAck` (driver) |
| Examined flags (track, path, tools) | **stores** in row 1's part records | row 1's `CarPartsChange` |
| Dyno values | **stores** in row 4's `Details[loader].Dyno` | `CarDetailsUpdate` |
| `specialState` | **stores** in the spawn record | `CarAwayRelease` → `CarAwayUpdate` |
| Driving, the dyno run, the path animation, labels, toasts | relays nothing | client-local |

### D13. Late-join path

1. A client joins (or returns from the track) and sends `AskForSync`; the server builds the snapshot under
   `StateLock`.
2. `cars` (100, row 1): each car's spawn info carries `SpecialState` (applied after load, D8). After each car, next to
   `CarClaims.SendActive`, the server sends that car's `CarAwayUpdate` if it is claimed. Not counted as an item (like
   part claims); the client applies it to the mirror at once (mirror only, no scene work, so no `GarageBound`).
3. `car-details` (150, row 4): `Info.Mileage`, `BodyCosmetics` and `Dyno` arrive with the record; `Dyno` is applied
   with the other sections once the car is `Ready`.
4. A late joiner therefore sees a car that is on the track as claimed and labelled, with the server's mileage. When
   the driver returns, the joiner gets the fold as a live `CarDetailsUpdate` and the release as `CarAwayUpdate`.
5. No new `SyncOrder` slot and no new save section.

### D14. Packets

Appended to the end of `PacketTypes` (never renumbered): `CarAwayRequest { RequestId, CarLoaderID, SpawnSeq, Kind }`
C→S; `CarAwayUpdate { CarLoaderID, SpawnSeq, Kind, OwnerPlayerId (−1 = released), RequestId (0 unless it answers that
client's request), Refusal, SpecialState (−1 = none) }` S→C; `CarAwayRelease { CarLoaderID, SpawnSeq, SpecialState }`
C→S; `TestDriveResult { CarLoaderID, SpawnSeq, MileageDeltaKm, Cosmetics }` C→S; `TestDriveResultAck { CarLoaderID,
Applied }` S→C. Enums `CarAwayKind { TestTrack, PathTest, Dyno }`, `CarAwayRefusal { None, Busy, InUse, NotReady,
Unknown }`. Changed: `ModCarDetails.Dyno`, `CarDetailSection.Dyno = 512`, `CarSpawnResponsePacket.SpecialState`.

## Risks / Trade-offs

- [IL2CPP inlining: a hook never fires on the real path] → task 1.1 traces every hook on the real UI path with a fire
  counter. Known: `TestTrackManager.ReturnToGarage` is vtable-only and `PrepareCarPhysics.LoadCarFromSave` is inlined,
  so neither is hooked; results ride on `SelectSceneToLoad`, which both exits call.
- [Replaying `SelectSceneToLoad` after the grant fails, or the cancelled departure leaves the map/input in a broken
  state] → spike 1.3; fallback: ask first at the two UI entry points (`SideCarsPanel.DriveAction`,
  `MapWindow.SubmitPanelAction`/`SubmitItemAction` case 2) and replay those instead.
- [`GlobalData.Load` on return resets `NewMileage`, `SelectedCarLoader` or `TestToShow`] → spike 1.2 (runtime check 1
  of the spike doc); vanilla depends on them surviving, so this is unlikely; fallback: copy them in the
  `LeavingScene(TestTrack, Garage)` handler and restore them after `VanillaLoad`.
- [The track car's `carParts` order differs from the garage car's] → spike 1.2 compares; the result then carries no
  cosmetics (dirt from the drive is lost, mileage still syncs).
- [The return snapshot car is not `Ready` when the report opens, or `GetExaminedParts` picks another car] → D6 waits
  for `Ready`; spike 1.2 checks the save name ↔ loader index mapping and that `AfterExamine` fires once per part.
- [The dyno's preview leaks into a full details snapshot taken while the window is open] → `Read` skips `Dyno` while
  the local dyno is open on that car, and only the owner can be asked for that car's snapshot while it is claimed.
- [`DynoManager.job/haveJob` changes job state] → spike 1.4 reads it; if it does, that state goes to row 3 as a
  finding (QUESTIONS.md), not into this change.
- [A claim outlives a stuck client] → D10's watchdogs and `Left`; a stuck claim is visible (label) and logged.
- [Locked car blocks a job hand-back while the driver is away] → intended; the label says who has it.

## Migration Plan

Clients and server must run the same build (row 9's protocol hash). Old saves have no `Dyno` (null → nothing applied,
the car keeps its loaded `EngineData`) and no `SpecialState` (0). No section version changes. Rollback = previous build;
it ignores the extra fields when it loads the save and drops them on its next save (dyno values and `specialState` are
lost, nothing else).

## Open Questions / Assumptions

Decided without the user (each can be overridden; listed in QUESTIONS.md under row 13 when the change starts):

- **A1** The car stays visible and locked (with a label) for others while it is on the track, rather than disappearing.
- **A2** Examining a claimed car (looking at parts, examine tools) stays allowed for others; only edits are blocked.
- **A3** Track dirt is synced with the mileage (vanilla puts it on the car too).
- **A4** `specialState` is synced; if no side-effect-free apply exists, other players see the car at the path start
  until its next move (cosmetic).
- **A5** A driver who disconnects or quits on the track loses that drive's results; the car stays as it was and is
  free again.
- **A6** Watchdog times: none on the track, 60 s after the return, 15 min for the dyno/path; client release after 5 s
  outside the mode.
- **A7** The map's "measure power" (−500, drag strip only) stays blocked with the drag strip; row 5b's later
  `MeasurePower` hook uses `DynoSync.Commit`.
- **A8** Ending a job whose car is away is refused for others (the driver hands it back after the return).

Deferrable unknowns (answered by the spikes in tasks.md, they do not change the approach): whether `GlobalData.Load`
keeps the three globals; whether `CloseCar(true)` changes synced part state; the track car's part order; whether the
`SelectSceneToLoad` replay leaves the UI clean; the path-test abort and `SetCarPositionAfterLoad` side effects;
`DynoManager.job/haveJob`; the windows/modes the track flow needs from the guard.

## Runtime trace results

Spike 1.2, `test-drive-trace` on lane 1 (2026-10-06, run `20261006-154452_L1_test-drive-trace` and the two before it),
one connected client, `car_boltatlanta` on loader 0, guard `Enforce` with `Scene:TestTrack` allowed.

- **Departure:** `LeavingScene(Garage, TestTrack)` → `SelectSceneToLoad(Test_track_1, TestTrack, true, true)` →
  `CloseCar(true)` → `GarageLoader.Save(false)` (into the session's profile slot). `CloseCar(true)` sends no part or
  detail change: nothing between it and the track load in the client log.
- **On the track:** the track car is loaded from that save. Its `carParts` order equals the garage car's (27 names,
  same order), so D4's cosmetics can be read by index. Loading it calls `PartScript.Examine(true)` once per examined
  part (52); there is no `CarLoaderPlaces` on the track, so row 1's hooks stay quiet. The guard reports
  `Mode:CarDrive` (D11 needs that entry).
- **Return, all tests done:** the last `DoneTest` calls `TestTrackManager.ReturnToGarage` itself →
  `PrepareCarPhysics.SaveMileage(false)` (sets `GlobalData.NewMileage`: 5000 m → 5) → `LeavingScene(TestTrack, Garage)`
  → `SelectSceneToLoad(garage, Garage, true, true)`. D4's order holds: the result can be read in `LeavingScene`
  with the track still loaded.
- **Return, aborted:** the pause menu's `ReturnToGarage` → `SaveMileage` → `LeavingScene`; `TestToShow` is cleared
  (`''`), so no examine report opens.
- **`GlobalData.Load` on return keeps** `NewMileage`, `SelectedCarLoader` and `TestToShow`.
- **The mileage is lost today:** after both returns the car's `Info.Mileage` is still 0, because the garage loads
  the car from the server snapshot. `NewMileage` is never reset either, so it is still 5 at the next departure
  (the loop D5 fixes).
- **Examine report:** `GetExaminedParts` runs once after the snapshot car is `Ready`; its 52 examine calls reach the
  server as one row 1 change (`0 body, 52 mechanical`).
- Bugs found and fixed on `main`: row 4's detail hooks threw on the track (no `CarLoaderPlaces`), and presence threw
  there (no `CharacterMotor` while driving).
- Not covered yet: the path test, the dyno, `diag-examine` (the verbs are still to write) and the map entry
  (`SideCarsPanel.DriveAction`, `VerifyCarStateIfInterior`), which the harness skips by calling `SelectSceneToLoad`.
