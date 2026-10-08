# Design

## Context

- `NotificationCenter.<SelectSceneToLoad>d__34` treats scene types `{6, 7, 11, 13, 14, 17}` as tracks: it closes the
  selected car and saves the garage; the track's `PrepareCarPhysics` loads `GlobalData.SelectedCarLoader` from the
  in-memory profile, counts `mileage` and dirt, and `TrackManager.ReturnToGarage` writes `NewMileage` and the body dirt
  back (`docs/spikes/test-drive.md`). `RaceTrackManager` (laps, `LastTime`, `lastBestTime`, `stat_timeattack`) and
  `FreeTrackManager` (speed track) both derive from `TrackManager`; the pause menu offers return and (race track)
  restart.
- Row 13: `TestDriveSync.HoldDeparture` asks the server for a `CarAwayKind.TestTrack` claim before the scene change
  (only for `SceneType.TestTrack`), flushes pending part and detail changes, then lets the coroutine run;
  `CarAwayRegistry` releases the claim when the owner is back in the garage for 60 s, goes elsewhere or leaves; results
  (mileage, dirt, examined parts) are sent on `ClientScene.LeavingScene` before the return snapshot.
- Row 17 part 2: `DriveCapture` streams the local car (`ClientScene.LocalScene == TestTrack`, `CarDrive` mode);
  `DriveHandlers` relays only for `driveScenes = { TestTrack }` to players in the same scene; `RemoteCars` builds an
  observer copy without colliders.
- Row 21: `RideAlong` and the server's `Rides` start a ride when a test-track claim is granted to a driver with a seated
  passenger; the passenger loads `Test_track_1`.
- `SceneReady.WaitAndPublish` sets `ClientScene.LocalScene` from `GameScript.Get().CurrentSceneType`; on the speed track
  that value is `TestTrack` (spike run 2: "SpeedTrack ready as TestTrack", drive stream started for car -1).
- Spike run 2 also showed: mileage from both tracks reaches the car (0 → 4 km), the car has no claim while away, B sees
  no drive state, and `PrepareCarPhysics`, `CarDrive` mode and `RaceTrackManager`/`FreeTrackManager` load as on the test
  track.

## Goals / Non-Goals

**Goals:** the three base-game tracks behave like the test track does today for claims, results, visibility and rides;
lap records survive sessions.

**Non-Goals:** DLC and Workshop tracks; collisions; a shared race start; drag racing.

## Decisions

### D1. `TrackScenes`

Core table: `{ GameScene, SceneType, UnitySceneName, CarAwayKind }` for TestTrack (`Test_track_1`), RaceTrack
(`Race_track_1`, map scene name `Race_track_1`), SpeedTrack (`SpeedTrack`, map scene name `Speedtrack`; both spellings
map). `IsTrack(GameScene)`, `ByUnityName(name)`, `KindOf(GameScene)`. Every former `== GameScene.TestTrack` check that
means "driving scene" uses `IsTrack`; checks that mean the test track's own features (examine report, `TestToShow`,
`TestTrackManager` tests) stay test-track only.

### D2. Scene identity

`ClientScene.FromSceneName` maps the three Unity names; `SceneReady` uses the name mapping first and the game's scene type
only when the name is unknown. The departure remembers the requested `SceneType` (`SceneHooks.Leave` already gets it) and
`SceneReady` logs a mismatch between the two.

### D3. Claims and results

`TestDriveSync` becomes `TrackDriveSync`: `HoldDeparture` reacts to any track scene type, requests `KindOf(scene)`, and the
`MoveNext` prefix checks `TrackScenes.IsTrack(__instance.sceneType)`. `CarAwayKind.RaceTrack` and `SpeedTrack` are appended;
`CarAwayRegistry.OnSceneChanged`, `Tick`, `CarDetailsStore`'s track-owner check and `Rides` use `IsTrackKind`. On return,
the mileage and dirt path is the same; `TestToShow` stays empty for the race and speed tracks, so no examine report opens.

### D4. Driving and observers

`DriveHandlers.driveScenes` = the track set; relay to players whose presence scene equals the driver's. `DriveCapture`
captures in any track scene; `RemoteCars` builds copies in any track scene. A stream sample more than 20 m from the last
one (restart) resets the interpolation buffer, so the copy snaps.

### D5. Ride-along

The ride starts on a grant of any track kind; `RideUpdate` gains `[OptionalField] Scene` so the passenger loads the
driver's track (`TrackScenes` name). Everything else is row 21 unchanged.

### D6. Lap records

`RaceTrackManager.LastTime` postfix (it computes the finished lap and the `stat_timeattack` call) reads the lap time
(`lastBestTime`/the method's result, task 1.2) and sends `TrackRecord { Scene = RaceTrack, LapMs }` when connected. The
server (under `StateLock`) keeps `BestLapMs` per player identity (row 7's per-player data, written with `PlayerRestore`)
and `GroupBestLap { PlayerName, LapMs }` in the world state; a new group record is broadcast as
`TrackRecordUpdate { IsGroupRecord = true }`, a personal best only to its player. On join, `PlayerRestore` writes
`BestLapMs` into the session profile's `BestRaceTime`, so the race track's own display shows it. Laps over 30 minutes or
under 10 s are ignored (logged).

### D7. Late join

A joiner gets the active claims (row 13 `SendActive`, now including track kinds), the running rides, the group record in
`world` and the personal best in `self`. Drive streams start for a joiner who travels to the same track (as row 17).

## Risks / Trade-offs

- [Another track-specific rule hides in row 13/17/21 code that `IsTrack` misses] → task 1.1 lists every `TestTrack`
  reference (`rg "TestTrack"` gave 30 lines on 2026-10-08) and classifies it.
- [`LastTime` is inlined or shares a native body] → check with `work\at.py`; fallback: poll `lastBestTime` at 2 Hz on the
  race track.
- [Speed track's game scene type stays `TestTrack` in other game code paths (e.g. the pause menu's track buttons)] →
  the guard and our code use D2's identity; the game's own behaviour on that scene is unchanged.

## Migration Plan

Appended enum values and packets; optional fields in `self`, `world` and `RideUpdate`. Client and server update together
(version check). Rollback: revert; stored records are ignored by older servers.
