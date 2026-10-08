# Tasks

Prerequisites (all merged): rows 6, 7, 13, 14a, 17 part 2, 21.

## 1. Spikes

- [x] 1.0 Static and runtime spike (2026-10-08): DLC table, travel gates, `SpeedTrack` reported as `TestTrack`, trips
      work with `guard-allow`, mileage returns, no claim, no drive relay on the race track
      (`docs/spikes/singleplayer-features.md` section 5; runs `20261008-230909_L2_sp-features-probe`,
      `20261008-231615_L2_sp-features-probe2`).
- [ ] 1.1 Classify every `TestTrack` reference in client, server and harness (driving scene vs. test-track feature);
      the list goes into design.md D1.
- [ ] 1.2 On the race track: trace `RaceTrackManager.LastTime`, `_Restart_d__20.MoveNext`, `TrackManager.ReturnToGarage`
      and the pause menu buttons; read the lap value and `ProfileData.BestRaceTime` after a lap (harness drives a lap
      with checkpoints `track-lap`, or `LastTime` called directly); on the speed track, check whether anything writes
      `ProfileData.TopSpeed`. Done when D6 names the hook and the field.

## 2. Core and server

- [ ] 2.1 `TrackScenes`; `CarAwayKind.RaceTrack`, `SpeedTrack` (appended); `RideUpdate.Scene`; packets `TrackRecord`,
      `TrackRecordUpdate`.
- [ ] 2.2 `CarAwayRegistry`, `Rides`, `CarDetailsStore` track-owner check and `DriveHandlers` use the track set (D3, D4).
- [ ] 2.3 `TrackRecords` (D6): per-player best and group record, `self`/`world` optional fields, broadcast rules,
      bounds; server command `records`.

## 3. Client

- [ ] 3.1 Scene identity (D2): `ClientScene.FromSceneName`, `SceneReady`.
- [ ] 3.2 `TrackDriveSync` (D3) replacing the test-track-only departure; `DriveCapture`, `RemoteCars` (D4, restart snap);
      `RideAlong` (D5).
- [ ] 3.3 Lap records (D6): `LastTime` postfix, `PlayerRestore` writes `BestRaceTime`, toast for the group record.
- [ ] 3.4 Guard (owner row 27): `Scene RaceTrack`, `Scene SpeedTrack` allowed; labels for `DragStrip`, `CustomTrack`,
      `FunTrack`, `OffroadTrack`.

## 4. Proof

- [ ] 4.1 Scenario `race-track` (two clients, guard on Enforce):
      1. A spawns a car, `track-go 0 RaceTrack` → A on `RaceTrack` in both rosters; B's `away` shows A's claim of kind
         `RaceTrack`; B `part-unmount` on that car → refused with "A has this car on the race track.";
      2. B spawns a second car and `track-go 1 RaceTrack` → B's `remoteCars` shows A's car moving while A drives
         (`drive-input`), and A's shows B's;
      3. A drives 2.5 km and returns → claim released, mileage +2 km on both;
      4. A records a lap (`track-lap 95000`) → server `records` shows A's best and the group record; B gets the toast;
         B joins again → `world` has the group record; A joins again → A's session profile `BestRaceTime` = 95000;
      5. B sits in A's car, A `track-go 0 SpeedTrack` → B rides along on the speed track (row 21 checks: camera at the
         passenger head, avatars seated), presence scene `SpeedTrack` on all clients (not `TestTrack`);
      6. `travel DragStrip` → refused by the guard with the DLC label.
      Fails on the old code at step 1 (guard refuses; with `guard-allow`, no claim: `away` empty, spike run 2). Verify:
      `Run-Session.ps1 -Scenario race-track` passes; `test-drive`, `drive-track`, `ride-along`, `test-drive-latejoin` and
      the smoke set pass (`Run-All -Changed`, area `driving`).

## 5. Hand checks

- [ ] 5.1 docs/playtest.md: race against a friend on the race track (ghost cars, group record toast), speed track ride
      along, the pause menu restart while the friend watches.

## 6. Docs

- [ ] 6.1 Rows 13, 17 and 21 documents: "test track" rules now apply to the track set (one line each); ROADMAP row 27
      status; STATUS entry with run ids.
