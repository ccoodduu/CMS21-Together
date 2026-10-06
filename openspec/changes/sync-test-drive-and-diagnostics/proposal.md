# Proposal

## Why

Taking a car on a test drive is upstream's most reported car bug (#18, #83, #85, #95): repairs and job progress
reset after the drive. On `main` the test track, the test path, the dyno and the diagnostic tools are all blocked by
the row 14a guard ("row 13"), so a job cannot be diagnosed the way the game expects. The static spike
(`docs/spikes/test-drive.md`) shows why a naive unblock fails:

- the return to the garage is a full late join (row 6, user decision "return to the garage = late join"), so
  whatever the drive produced must be on the server before the return snapshot, or the snapshot erases it;
- the mod's `LoaderAddition.VanillaLoad` dropped the per-loader `LoadCarFromFile` loop, and vanilla's
  `CarInfoData.Mileage += GlobalData.NewMileage` step was inside it, so the drive's mileage is lost, and the
  leftover `NewMileage` can later be added to a car in the player's own single-player save;
- while one player drives or runs the path test or the dyno, the car changes only on that player's machine; another
  player working on it at the same time produces two diverging cars;
- dyno results (`CarLoader.EngineData` incl. `measured`, `MeasuredDragIndex`) are owned by this row (rows 4 and 5b
  defer to it) and are not synced at all.

M3 ("run jobs together — diagnose (examine, test drive, test path) …") needs this row.

## What Changes

- **Car claim while away**: a new per-car claim (`CarAway`, kinds `TestTrack`, `PathTest`, `Dyno`) held on the server,
  separate from row 1's part claims. The driver holds it from departure until the return snapshot has been applied
  and the examine report has run; the dyno and path-test claims last as long as those modes. Every client mirrors the
  claims, shows a label "<name>: test drive / test path / dyno" above the car and refuses edits to it; the server
  refuses part, detail, placement, delete and job-end requests for a claimed car from anyone but the owner and
  answers with a correction. Claims are runtime-only and are released on leave, on an unexpected scene change, by a
  watchdog, and when the car's loader is cleared.
- **Test track** (the only one of these that changes scene): the departure asks the server first — a prefix on
  `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` for `SceneType.TestTrack` holds the scene
  change, sends `CarAwayRequest` for the loader named by `GlobalData.SelectedCarLoader`, and replays the call when the
  claim is granted (refused → message, the player stays in the garage). Before leaving, the client flushes pending
  part and detail changes of that car.
- **Results before the return snapshot**: on `ClientScene.LeavingScene(TestTrack, Garage)` (row 6; by then
  `PrepareCarPhysics.SaveMileage` has run and the track scene is still loaded) the client sends `TestDriveResult`
  with the mileage delta (`GlobalData.NewMileage`, km) and the track car's dust and wash factor per body part. The
  server folds them into row 4's stored details (`Info.Mileage`, `BodyCosmetics`), relays them like a row 4 update
  and answers `TestDriveResultAck`. The return `AskForSync` follows on the same ordered stream, so the snapshot
  already has them.
- **Examined parts** are not results of the track: the game examines them in the garage after the return load
  (`ExamineReportWindow.GetExaminedParts` → `PartScript.Examine(true)`), which row 1's `PartHooks.AfterExamine`
  already syncs. This change makes that run on the snapshot car: `CustomLoad` waits until the selected car is
  `Ready` before it opens the report, and the claim keeps the car on the same loader until then.
- **Mileage-loop fix** in `LoaderAddition.CustomLoad`: after the snapshot, vanilla's step
  (`Mileage += NewMileage` on the loader whose save name equals `SelectedCarLoader`, then `NewMileage = 0`) is
  restored as the fallback for a result the server did not apply (row 4's 1 Hz `Info` poll uploads it);
  `NewMileage` is cleared once the server has applied it and on disconnect, so it is never counted twice and never
  leaks into a single-player load.
- **Dyno** (garage, `CarPlace.Dyno`): claim on `DynoManager.RunDyno`; prefix + postfix on `DynoManager.CloseDyno`
  commit the result only when `DynoMeasured` was true. The result is a new row 4 details section `Dyno`
  (`ModEngineData` + `MeasuredDragIndex`), committed by `MarkDirty`/flush only, never polled (the dyno window
  previews values and restores them on cancel). Remote apply copy-assigns `CarLoader.EngineData` and sets
  `MeasuredDragIndex`. `DynoSync.Commit(carLoader)` is the entry row 5b's `MeasurePower` hook calls.
- **Test path** (garage, `CarPlace.DiagnosticPath`): claim on `PathTestManager.Prepare`; the result is the examined
  flags of the report (row 1) plus `CarLoader.specialState = 1`, sent with the claim release and stored in the car's
  spawn record (`CarSpawnResponsePacket.SpecialState`, additive), applied on other clients and late joiners.
- **OBD and the other examine tools** (compression, multimeter, tire tread, compound meter) only call
  `PartScript.Examine(true)`, which row 1 syncs; this change verifies it in two instances and needs no claim.
- **Guard**: the merge commit opens `Scene TestTrack`, `Window PathTest`/`Dyno`, `Mode PathTest`/`Dyno`/
  `ExamineTools`, `Pie move_pathTest`/`move_dyno`/`obd`/`cylinder`/`fuel`/`electronic`/`tires`, plus any track
  window the trace finds; the `Benchmark` entries move to the backlog (the main-menu graphics benchmark, unreachable
  while connected).
- Packets: **new** `CarAwayRequest`, `CarAwayUpdate`, `CarAwayRelease`, `TestDriveResult`, `TestDriveResultAck`
  (appended to `PacketTypes`); **changed** `ModCarDetails` + `Dyno`, `CarDetailSection.Dyno`,
  `CarSpawnResponsePacket` + `SpecialState`.
- Game hooks (Harmony): `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` (prefix, test track
  departure), `ClientScene.LeavingScene` (event, results), `ExamineReportWindow.GetExaminedParts` (postfix,
  release), `DynoManager.RunDyno` (postfix, claim), `DynoManager.CloseDyno` (prefix + postfix, result and release),
  `PathTestManager.Prepare` (postfix, claim), `PathTestManager.<EndAllTests>d__50.MoveNext` (postfix, `specialState`);
  existing hooks of rows 1, 2, 3 and 4 gain an away check.

## Capabilities

### New Capabilities
- `test-drive-sync`: car claims while a car is on the test track, the test path or the dyno; results of the test
  drive, the test path, the dyno and the diagnostic tools reaching every player and the server before the driver's
  return snapshot; the mileage fix; late join and persistence of those results.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (5 values appended), new `Network/Packets/TestDrivePackets.cs` (+ `CarAwayKind`,
  `CarAwayRefusal`), `Data/GameType/ModCarDetails.cs` (`ModEngineData`, `ModDynoResult`, `Dyno` field,
  `CarDetailSection.Dyno = 512`), `CarPackets.cs` (`CarSpawnResponsePacket.SpecialState`).
- Server: new `Data/Cars/CarAwayRegistry.cs`, `Network/Handlers/TestDriveHandlers.cs`; away checks in `CarClaims`,
  `CarHandlers` (parts change, delete), `CarDetailsStore` (non-owner updates, `Dyno` merge/clamp, result fold),
  `PlacementHandlers`/`ParkingHandlers`, `JobsService.OnJobEnd`; `cars` snapshot sends active away claims; server
  command `away`.
- Client: new `Logic/Car/Away/CarAwaySync.cs` (mirror, request, lock checks, label), `TestDriveSync.cs`,
  `DynoSync.cs`, `PathTestSync.cs`; `LoaderAddition.CustomLoad` (wait for the car, mileage fallback, `NewMileage`
  cleanup); `CarDetailsIO` (`Dyno` read/apply, excluded from `Polled`); lock checks in `PartClaims`,
  `EngineCraneHooks`, `CarPlacementSync`/`LifterSync`/`ParkingSync`, `JobHooks`; `GuardRules`.
- Harness: `tools/TestHarness/Features/TestDriveCommands.cs` (`testdrive-trace`, `testdrive-go`, `testdrive-drive`,
  `testdrive-finish`, `testdrive-skip-result`, `pathtest-run`, `dyno-run`, `diag-examine`), dump section `away` and
  `dyno`/`specialState` fields in the car dumps; scenarios `test-drive-trace.ps1`, `test-drive.ps1`,
  `test-drive-latejoin.ps1`, `diagnostics.ps1`.
- Depends on (merged): rows 7 (contract, `StateLock`), 6 part 1 (`ClientScene.LeavingScene`, `PresenceEvents`,
  `travel`, `Wait-HarnessDump`), 1 (part sync, `PartHooks.AfterExamine`, `CarClaims`, `SendCar`, `LoaderCleared`),
  2 (places, `net-hold`, spawn record), 3 (job end path), 4 (details store, `Info`, `BodyCosmetics`), 14a (guard),
  14 (`resync`). Row 5b (not built) later calls `DynoSync.Commit` from its `MeasurePower` hook (its group 10).
