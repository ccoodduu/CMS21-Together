# Tasks

> **Read first:** `docs/spikes/test-drive.md` (static decompile, 2026-10-06). Section 10 lists the runtime checks group 1
> answers; section 8 lists the hooks.

Prerequisites (merged): `session-persistence-and-rejoin` groups 1–2 (contract, `StateLock`, `Send-ServerCommand`,
`Stop-TestServer`/`Start-TestServer`, `to-menu`), `sync-players-and-scenes` part 1 (`ClientScene.LeavingScene`,
`PresenceEvents`, `travel`, `Wait-HarnessDump`), `sync-car-parts` (`car-spawn`, `car-ready`, `part-unmount`,
`part-state`, `PartHooks.AfterExamine`, `CarClaims`, `CarsSnapshotProvider.SendCar`, `LoaderCleared`),
`sync-car-placement-and-lifts` (`car-move`, `lift`, `park`, `net-hold`), `sync-orders-and-jobs` (`orders-accept`,
`job-finish`), `sync-car-details` (`CarDetailsStore`, `cardetails-*`), `multiplayer-guard` (`guard-set`, `guard-allow`,
`guard-log`) and `desync-detection-and-resync` (`resync`). Work on branch `change/sync-test-drive-and-diagnostics`.

## 1. Spike: runtime trace of the test track, test path and dyno (harness only, no behaviour change)

- [x] 1.1 **Done (2026-10-06):** trace, `testdrive-go/-drive/-finish/-partnames`, `dyno-run`, `pathtest-run` (prepare/end/exit/state), `diag-examine`. Add `tools/TestHarness/Features/TestDriveCommands.cs` with native-only verbs: `testdrive-trace on|off|report`
      (logging-only prefix/postfix with fire counters on `NotificationCenter.SelectSceneToLoad(string, SceneType, bool,
      bool)`, `SideCarsPanel.DriveAction`, `MapWindow.VerifyCarStateIfInterior`, `CarLoader.CloseCar`,
      `GarageLoader.Save`, `TrackManager.ReturnToGarage`, `PrepareCarPhysics.SaveMileage`, `TestTrackManager.DoneTest`,
      `ExamineReportWindow.GetExaminedParts`, `PartScript.Examine`, `DynoManager.{RunDyno, PrepareDyno, CloseDyno}`,
      `CarLoader.MeasurePower`, `PathTestManager.{Prepare, SetCarPositionAfterLoad}`,
      `PathTestManager._EndAllTests_d__50.MoveNext`, `_ExitFromCar_d__47.MoveNext`, `GameScript.ExitFromInterior`;
      each log line carries `GlobalData.NewMileage`, `SelectedCarLoader`, `TestToShow`, `GameMode` current mode);
      `testdrive-go <loader>` (sets `SelectedCarLoader` = that loader's save name and `TestToShow = "ExamineReport"`,
      then starts `SelectSceneToLoad("Test_track_1", TestTrack, true, true)` like the on-foot map path);
      `testdrive-drive <metres>` (adds to the track's `PrepareCarPhysics.mileage`); `testdrive-finish all|abort`
      (`DoneTest` for every active test, or the pause menu's `ReturnToGarage` without finishing); `pathtest-run
      <loader> [abort]` (car must stand at `DiagnosticPath`; runs the `RunPathTest` entry, drives `EndAllTests` and
      the exit, or leaves early); `dyno-run <loader> measure|cancel` (car at `Dyno`; `RunDyno`, then `StartDyno` +
      `HideAction`, or `HideAction` only); `diag-examine <loader> <ToolType>` (`CarHelper.GetPartsToExamine` +
      `Examine(true)` per part, the body of the tools' coroutines). Verify: the harness builds with
      `tools/test-env/Deploy-Mod.ps1` and `testdrive-trace report` lists every hook with count 0 (unpatchable targets
      reported, not thrown).
- [x] 1.2 **Done (2026-10-06):** results in design.md "Runtime trace results". Scenario `tools/test-env/scenarios/test-drive-trace.ps1` (instance A only, connected, guard `Enforce` with
      `guard-allow` for the row 13 entries): `car-spawn`, damage a few parts, `testdrive-trace on`, `testdrive-go`,
      `testdrive-drive 5000`, `testdrive-finish all`, wait for the garage, `testdrive-trace report`; repeat with
      `testdrive-finish abort`; then `guard-log`. Record in design.md ("Runtime trace results"): call order; whether
      `GlobalData.Load` on return keeps `NewMileage`, `SelectedCarLoader`, `TestToShow`; whether `LeavingScene(TestTrack,
      Garage)` fires after `SaveMileage` with the track still loaded; whether `CloseCar(true)` changes synced part or
      detail state; whether the track car's `carParts` order equals the garage car's (compare `CarDetailsIO.Read`
      indexes); the save name ↔ loader index mapping; whether the snapshot car is `Ready` when `CustomLoad` reaches the
      report and `AfterExamine` fires once per examined part; every window/mode the guard blocked. Verify: each item of
      the spike doc's section 10 (1–5) is answered in design.md or moved to Risks.
- [ ] 1.3 **Partly done (2026-10-06):** `departure-hold`, results in design.md (hold in `MoveNext`, no replay; cancel leaves `LocalScene = Loading`); the map UI paths need a hand check. Departure replay: in the same scenario, a harness-only prefix holds the first `SelectSceneToLoad(…, TestTrack,
      …)` for 1 s and replays it with `StartCoroutine`; repeat from the map UI path while seated (`MapWindow`) and on
      foot (`SideCarsPanel`); and a held call that is never replayed (cancel). Record whether the replay reaches the
      track with the right car and what the cancel leaves behind (map window, input mode, pie menu, `loadingScene`).
      Verify: design.md D2 names the replay call and the cancel cleanup, or switches to the UI-entry fallback from Risks
      (user asked first if the approach changes).
- [ ] 1.4 **Partly done (2026-10-06):** `diag-trace`, results in design.md; path test exit, tuned car and job car on the dyno still to check by hand. Path test and dyno: `car-move` to `DiagnosticPath`, `pathtest-run` (full and `abort`), `car-move` to `Dyno`,
      `dyno-run measure` and `dyno-run cancel`, with the trace on. Record: `specialState` before/after; whether
      `SetCarPositionAfterLoad(true)` only moves the car (no input, camera or mode change); an early exit that examines
      nothing; `EngineData`/`MeasuredDragIndex` before, during (preview) and after both dyno runs; whether
      `DynoManager.job/haveJob` changes any `Job`. Verify: D7 and D8 in design.md are confirmed or corrected, and a job
      effect, if any, is in QUESTIONS.md for row 3.

## 2. Core: packets and DTOs

- [x] 2.1 **Done (2026-10-06):** 62 → 67 packets on client and server (`connect`). Append `CarAwayRequest`, `CarAwayUpdate`, `CarAwayRelease`, `TestDriveResult`, `TestDriveResultAck` to the end
      of `PacketTypes`; add `Network/Packets/TestDrivePackets.cs` with the fields of design.md D14 and enums
      `CarAwayKind`, `CarAwayRefusal`. Verify: the solution builds and `PacketRouter.Initialize` logs 5 more packets on
      client and server start.
- [ ] 2.2 **In code (2026-10-06):** round trip checked with the dyno scenario later. `ModCarDetails.cs`: `ModEngineData` (every `EngineData` field from the decompiled struct), `ModDynoResult
      { Engine, MeasuredDragIndex }`, `ModCarDetails.Dyno`, `CarDetailSection.Dyno = 512`; `CarPackets.cs`:
      `CarSpawnResponsePacket.SpecialState` (default 0). Verify: a Newtonsoft and a BinaryFormatter round trip keep every
      field (server debug command or a small console check), and a `cars` section saved by the previous build loads
      with `Dyno = null`, `SpecialState = 0`.

## 3. Server

- [ ] 3.1 `Data/Cars/CarAwayRegistry.cs` + `Network/Handlers/TestDriveHandlers.cs`: grant/refuse per D1 (record with
      baseline and `SpawnSeq`, no other away claim, no other player's part claims; release the owner's own part claims on
      grant), `CarAwayUpdate` to everyone on grant/release and to the requester on refusal, `CarAwayRelease` (owner only;
      stores `SpecialState` in the spawn record when ≥ 0), release rules of D10 (`PresenceEvents.Left`, `SceneChanged`,
      `LoaderCleared`, tick watchdog from `Server.Update` under `StateLock`). Verify: grant, refusal (`Busy`, `InUse`)
      and each release reason appear in the server log during groups 4–6 and section 8.
- [ ] 3.2 Away enforcement per D3 in `CarClaims.Handle`, `CarHandlers` (`CarPartsChange`, `CarSpawnDelete` → `SendCar`
      to the sender), `CarDetailsStore.OnUpdate` (non-owner → resend the stored record to the sender), `PlacementHandlers`
      (place change, lift of the car's place), `ParkingHandlers` (park), `JobsService.OnJobEnd` (job of a claimed car).
      Row 2's accepted place change resets the record's `SpecialState` to 0. Verify: in 8.1, B's harness changes to A's
      away car are refused in the server log and B's `cars`/`cardetails` dumps return to the server's state.
- [ ] 3.3 `TestDriveResult` fold per D4: owner's `TestTrack` claim + same `SpawnSeq` + valid details → `Info.Mileage +=
      clamp(delta, 0, 2000)`, merge only `Dust`/`WashFactor` per part index, relay one `CarDetailsUpdate` through
      `CarDetailsStore`, `TestDriveResultAck { Applied = true }`; else `Applied = false` + log. Verify: the `cardetails`
      server command shows the new mileage right after the result and before A's `AskForSync` in the log order.
- [ ] 3.4 `CarDetailsStore`: merge `Dyno` as a whole section, clamp it (finite floats, `MeasuredDragIndex ≥ 0`), keep it
      in the snapshot and the save. Verify: `Send-ServerCommand save` writes `Dyno` for a measured car and a restart
      (`Stop-TestServer`, `Start-TestServer`) loads it unchanged (`cardetails` command).
- [ ] 3.5 `cars` snapshot: after each car (next to `CarClaims.SendActive`) send its `CarAwayUpdate` when claimed,
      uncounted; server command `away` (loader, kind, owner, age). Verify: a late join while a car is claimed shows the
      update in the server log after that car's `CarPartsSnapshot` and the `SyncEnd` count for `cars` is unchanged.

## 4. Client: away mirror and locks

- [ ] 4.1 `Logic/Car/Away/CarAwaySync.cs`: mirror from `CarAwayUpdate` (snapshot and live, applied at once, cleared by
      `ClientData.Reset` before the snapshot refills it), `Request(loader, kind, onGranted, onRefused)` with `RequestId`
      and a 5 s timeout, `Release(loader, specialState)`, `LockedForMe(loader, out owner, out kind)`. Verify: harness dump
      section `away` (7.1) equals on A and B after a grant and a release.
- [ ] 4.2 Lock checks per D3 in `PartClaims.AllowAction`/`AfterCanTakeOffCarPart`, `EngineCraneHooks`, row 2's car
      move/lift/park hooks, row 3's job-end hook, and the dyno/path/track starts; row 4's poll skips locked loaders; toast
      names the owner and the activity. Verify: with A holding a claim, B's `part-unmount`, `car-move`, `park` and
      `job-finish` on that car are blocked locally (`PartClaims.LastBlocked` / log) and send nothing.
- [ ] 4.3 Away label above claimed cars ("<name> — test drive / test path / dyno") drawn next to row 6's `NameTags`
      within its distance limit. Verify: `screenshot` on B shows the label while A holds a claim and not after release.

## 5. Client: test track round trip and the mileage fix

- [ ] 5.1 `Logic/Car/Away/TestDriveSync.cs` departure: prefix (after the guard, `__runOriginal`) on
      `SelectSceneToLoad(string, SceneType, bool, bool)` for `TestTrack` → hold, `Request(TestTrack)`, replay on grant
      (call from 1.3), cancel cleanup and message on refusal/timeout; on `LeavingScene(Garage, TestTrack)` flush the
      loader's pending part and detail changes (add `CarDetailsSync.FlushNow(loader)` if missing). Gated on connected.
      Verify: `testdrive-go` reaches the track with the claim granted; with B holding a part claim on the car A stays in
      the garage with a message and no fade; a single-player (not connected) test drive still works.
- [ ] 5.2 Results on `LeavingScene(TestTrack, Garage)`: `TestDriveResult` with `NewMileage` and the track car's
      cosmetics (D4, index check from 1.2); `TestDriveResultAck` handler zeroes `NewMileage` when applied. Verify: after
      `testdrive-drive 5000` + `testdrive-finish all`, the server log shows the result before A's `AskForSync` and A's
      log shows `NewMileage` 0 after the ack.
- [ ] 5.3 `LoaderAddition.CustomLoad` per D5 and D6: after the sync, apply a leftover `NewMileage` to the
      `SelectedCarLoader` car once it is `Ready` (20 s), then `NewMileage = 0`; wait for that car to be `Ready` before
      the ExamineReport block; clear `NewMileage` on disconnect. Postfix on `ExamineReportWindow.GetExaminedParts`
      (and the no-report end of `CustomLoad`) releases the claim after the part tracker flushed. Verify: with
      `testdrive-skip-result` (7.1) the mileage still rises once on A and B; after a return + `disconnect`,
      `GlobalData.NewMileage` is 0 (harness `dump`).

## 6. Client: dyno, test path, diagnostics, guard

- [ ] 6.1 `DynoSync.cs`: postfix `DynoManager.RunDyno` → `Request(Dyno)` (refusal → `DynoWindow.HideAction`); prefix +
      postfix `CloseDyno` → `Commit(carLoader)` when measured, then release; `CarDetailsIO` reads/applies `Dyno` (in
      `All`, not in `Polled`, skipped while the local dyno is open on that car). Verify: `dyno-run measure` on A gives B
      equal `dyno` fields; `dyno-run cancel` leaves both unchanged.
- [ ] 6.2 `PathTestSync.cs`: postfix `PathTestManager.Prepare` → `Request(PathTest)` (refusal → abort from 1.4); note
      `specialState = 1` at the end of `EndAllTests`; release with it after the report's `GetExaminedParts`; client
      watchdog (5 s outside the mode, no report open); remote apply of `SpecialState` (field + `SetCarPositionAfterLoad`
      if 1.4 allows) on `CarAwayUpdate` and after a snapshot spawn. Verify: `pathtest-run` on A → B's
      `cars` dump shows the same examined flags and `specialState` 1; `car-move` away resets it to 0 on both.
- [ ] 6.3 `GuardRules`: `Allow` the entries of D11 (plus any window/mode from 1.2), move `Benchmark` (window, mode) to
      the backlog owner; drop the scenarios' `guard-allow` for them. Verify: `guard-rules` lists them as allowed and
      `guard-try Scene DragStrip` is still denied.

## 7. Harness

- [ ] 7.1 Add to `TestDriveCommands.cs`: `testdrive-skip-result on|off` (the next `TestDriveResult` is not sent, for
      the D5 fallback); `StateDump` section `away` (`loader`, `kind`, `owner`, `mine`) and fields `dyno` (engine fields,
      `measuredDragIndex`) and `specialState` per car in the existing car dump; add `away` to the sections
      `Compare-HarnessDumps` knows. Verify: each verb and section answers in a `-KeepRunning` session and the `connect`
      scenario still passes.

## 8. Integration: harness scenarios in two instances (guard on `Enforce`)

- [ ] 8.1 Scenario `tools/test-env/scenarios/test-drive.ps1`: both connect; A `car-spawn`s a car, A and B unmount and
      remount different parts and change a fluid; A `testdrive-go` → B's `away` shows A/`TestTrack`; B `part-unmount`,
      `car-move`, `park` on it → blocked, B's dumps unchanged; A `testdrive-drive 5000`, `testdrive-finish all` →
      A back in the garage, report ran; compare `cars`, `cardetails`, `away`: mileage +5 on both, examined flags of
      groups {2, 20, 24} on both, every earlier repair present, claim released. Then: B `part-claim` a part, A
      `testdrive-go` → refused, A stays in the garage. Race: `net-hold on` on both, A and B `testdrive-go` the same car,
      `net-hold off` → one on the track, one in the garage with a message. Abort: A `testdrive-go`, `testdrive-drive
      1500`, `testdrive-finish abort` → mileage +1 (or +2 by rounding, same on both), no report. Fallback: A
      `testdrive-skip-result on`, drive and return → mileage still increased once, equal on both. Pass = every
      `Compare-HarnessDumps -Sections cars,cardetails,away` empty and the expected refusals in the logs. Verify with
      `Run-Session.ps1 -Scenario test-drive`.
- [ ] 8.2 Scenario `tools/test-env/scenarios/test-drive-latejoin.ps1`: only A connects; A `car-spawn`, `orders-accept`
      a job car as well; A `testdrive-go` with the job car; B connects → B's `away` shows the claim and B
      `job-finish` on that job is blocked; A `testdrive-drive 3000`, `finish all` → B has the mileage and examined flags;
      A `testdrive-go` again and `disconnect` on the track → claim released on B, car unchanged; A reconnects. Restart:
      dump A, `Send-ServerCommand save`, `Stop-TestServer`, A `to-menu`, `Start-TestServer`, A reconnects → `cars` and
      `cardetails` equal the pre-restart dump. Verify with `Run-Session.ps1 -Scenario test-drive-latejoin`.
- [ ] 8.3 Scenario `tools/test-env/scenarios/diagnostics.ps1`: both connect; A spawns a car, `car-move` to `Dyno`; A
      `dyno-run measure` → B's `dyno` equal and B's `part-unmount` blocked during the run; `dyno-run cancel` → nothing
      changes; with `net-hold`, A and B `dyno-run` the same car → one runs; `car-move` to `DiagnosticPath`, A
      `pathtest-run` → B's examined flags of groups {4, 16, 28, 40} and `specialState` equal; A `pathtest-run abort` →
      no `specialState` change; A `diag-examine <loader> OBD` and B `diag-examine <loader> Compression` at once → union
      of examined flags on both; B late-joins once more (`disconnect`/`connect`) → `dyno`, `specialState`, examined
      flags equal; restart keeps `dyno`. Pass = all comparisons empty. Verify with `Run-Session.ps1 -Scenario
      diagnostics`.
- [ ] 8.4 Full regression `tools/test-env/Run-All.ps1` with the three scenarios added; record run ids and results in
      `STATUS.md`, update ROADMAP status, INTEGRATION.md (packets, APIs `CarAwaySync`/`DynoSync.Commit`, harness verbs,
      scenarios, `cars`/`car-details` additive fields) and row 6 D6's note on examined parts. Verify: `Run-All` green.
