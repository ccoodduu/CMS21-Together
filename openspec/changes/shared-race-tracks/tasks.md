# Tasks

Prerequisites (all merged): rows 6, 7, 13, 14a, 17 part 2, 21. Followed by 27b `track-races` and 27c
`track-collisions`.

## 1. Spikes

- [x] 1.0 Static and runtime spike (2026-10-08): DLC table, travel gates, `SpeedTrack` reported as `TestTrack`, trips
      work with `guard-allow`, mileage returns, no claim, no drive relay on the race track
      (`docs/spikes/singleplayer-features.md` section 5; runs `20261008-230909_L2_sp-features-probe`,
      `20261008-231615_L2_sp-features-probe2`).
- [ ] 1.1 Classify all 49 `TestTrack` references (40 in client, core and server, 9 in the harness) as driving scene or
      test-track feature; the table goes into design.md D1. Gate for group 3.
- [ ] 1.2 Decompile `RaceTrackManager.LastTime`, `NextCheckPoint` and `_Restart_d__20` and check them with
      `work\at.py`; on the race track, trace them and `TrackManager.ReturnToGarage` and the pause menu buttons; name the
      lap value, per-lap or per-finish, and the type and unit of `ProfileData.BestRaceTime`; check whether a restart can
      produce a `LastTime` without all checkpoints. On the speed track, check whether `FreeTrackManager.topSpeed`/
      `lastTopSpeed` reach `ProfileData.TopSpeed`. Done when D6 names the hook and the fields.

## 2. Core and server

- [ ] 2.1 `TrackScenes`; `CarAwayKind.RaceTrack`, `SpeedTrack` (appended); `RideUpdate.Scene`; packets `TrackRecord`,
      `TrackRecordUpdate`.
- [ ] 2.2 `CarAwayRegistry`, `Rides`, `CarDetailsStore` track-owner check and `DriveHandlers` use the track set (D3, D4);
      `AwayCheck` self-check for the three kinds.
- [ ] 2.3 `TrackRecords` (D6): per-player best and group record, `self`/`world` optional fields, broadcast rules,
      bounds; server command `records`.

## 3. Client

- [ ] 3.1 Scene identity (D2): `ClientScene.FromSceneName`, `SceneReady`.
- [ ] 3.2 `TrackDriveSync` (D3) replacing the test-track-only departure; `CarAwaySync` texts; `DriveCapture` loader
      lookup and `RemoteCars` (D4); `RideAlong` track set and `TrackManager.Instance` return (D5).
- [ ] 3.3 Lap records (D6): `LastTime` postfix (driver only, complete laps), `PlayerRestore` writes `BestRaceTime`,
      toast for the group record; harness `track-lap` (real path) and `track-record-send`.
- [ ] 3.4 Guard (owner row 27): `Scene RaceTrack`, `Scene SpeedTrack` allowed; labels for `DragStrip`, `CustomTrack`,
      `FunTrack`, `OffroadTrack`.

## 4. Proof

- [ ] 4.1 Scenario `race-track` (two clients, guard on Enforce, `guard-allow Mode:CarDrive` as in `test-drive` and
      `drive-track`):
      1. A spawns a car, `track-go 0 RaceTrack` → A on `RaceTrack` in both rosters; B's `away` shows A's claim of kind
         `RaceTrack`; B `part-unmount` on that car → refused with "A has this car on the race track.";
      2. B spawns a second car and `track-go 1 RaceTrack` → B's `remoteCars` shows A's car moving while A drives
         (`drive-input`), and A's shows B's;
      3. A drives 2.5 km and returns → claim released, mileage +2 km on both;
      4. A records a lap (`track-lap 95000`, the real `LastTime` path) → server `records` shows A's best and the group
         record; B gets the toast; B joins again → `world` has the group record; A joins again → A's session profile
         `BestRaceTime` = 95000; server restart (row 7 `server-restart` helpers) → `records`, B's `world` group record
         and A's `BestRaceTime` unchanged; `track-record-send 5000` → ignored and logged;
      5. B sits in A's car, A `track-go 0 SpeedTrack` → B rides along on the speed track (row 21 checks: camera at the
         passenger head, avatars seated), presence scene `SpeedTrack` on all clients (not `TestTrack`), A's drive
         stream names A's loader (not -1); on the race track the passenger sends no `TrackRecord`;
      6. `travel DragStrip` → refused by the guard with the DLC label (the harness bypasses the map, so this checks the
         guard label, not the game's DLC gate).
      Old-code failure: run with `guard-allow Scene:RaceTrack`; it fails at step 1, "B's `away` shows A's claim of kind
      `RaceTrack`" (spike run 2: `away` empty). Verify: `Run-Session.ps1 -Scenario race-track` passes; `test-drive`,
      `drive-track`, `ride-along`, `test-drive-latejoin` and the smoke set pass (`Run-All -Changed`, area `driving`).

## 5. Hand checks

- [ ] 5.1 docs/playtest.md: drive with a friend on the race track (cars pass through each other until 27c, group record
      toast), speed track ride along, the speed track's pause menu and return, the race track's pause menu restart while
      the friend watches.

## 6. Docs

- [ ] 6.1 Rows 13, 17 and 21 documents: "test track" rules now apply to the track set (one line each); ROADMAP row 27a
      status; STATUS entry with run ids.
