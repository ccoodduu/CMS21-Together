# Design

## Context

- `NotificationCenter.<SelectSceneToLoad>d__34` treats scene types `{6, 7, 11, 13, 14, 17}` as tracks: it closes the
  selected car and saves the garage; the track's `PrepareCarPhysics` loads `GlobalData.SelectedCarLoader` from the
  in-memory profile, counts `mileage` and dirt, and `TrackManager.ReturnToGarage` writes `NewMileage` and the body dirt
  back (`docs/spikes/test-drive.md`). `RaceTrackManager` (laps, `LastTime()`, `NextCheckPoint()`,
  `AllCheckpointsDone()`, `timer` (`Stopwatch`), `lastBestTime` (long), a light sequence in `Prepare`,
  `stat_timeattack`) and `FreeTrackManager` (speed track, `topSpeed` float, `lastTopSpeed` int) both derive from
  `TrackManager`, which has `Instance` and `ReturnToGarage`; the pause menu offers return and (race track) restart.
  `SpeedTrackManager` exists in the dump but is not used by the `SpeedTrack` scene.
- Row 13: `TestDriveSync.HoldDeparture` asks the server for a `CarAwayKind.TestTrack` claim before the scene change
  (only for `SceneType.TestTrack`), flushes pending part and detail changes, then lets the coroutine run;
  `CarAwayRegistry` releases the claim when the owner is back in the garage for 60 s, goes elsewhere or leaves; its
  non-test-track branches release as soon as the owner leaves the garage and run a 15-minute watchdog. Results
  (mileage, dirt, examined parts) are sent on `ClientScene.LeavingScene` before the return snapshot; `CarDetailsStore`
  accepts results only from a `TestTrack` owner.
- Row 17 part 2: `DriveCapture` streams the local car (`ClientScene.LocalScene == TestTrack`, `CarDrive` mode) at 15 Hz
  and takes the loader from the own `CarAwayKind.TestTrack` claim; `DriveHandlers` relays only for
  `driveScenes = { TestTrack }` to players in the same scene; `RemoteCars` builds an observer copy without colliders;
  `DriveInterpolator` shows it 100 ms behind and snaps any state more than 5 m off the velocity prediction
  (`SnapDistance`, counted in `Snaps`).
- Row 21: `RideAlong` and the server's `Rides` start a ride when a test-track claim is granted to a driver with a seated
  passenger; the passenger loads `Test_track_1`; `RideAlong.Return` uses `FindObjectOfType<TestTrackManager>()` and
  otherwise loads `garage` directly.
- `SceneReady.WaitAndPublish` sets `ClientScene.LocalScene` from `GameScript.Get().CurrentSceneType`; on the speed track
  that value is `TestTrack` (spike run 2). `GameScene` already has `RaceTrack = 11` and `SpeedTrack = 16`;
  `ClientScene.FromSceneType` maps by name.
- Spike run 2 also showed: mileage from both tracks reaches the car (0 → 4 km, through the 1 Hz poll), the car has no
  claim while away, B sees no drive state, and `PrepareCarPhysics`, `CarDrive` mode and `RaceTrackManager`/
  `FreeTrackManager` load as on the test track. `RaceTrackManager.LastTime`, `NextCheckPoint` and `_Restart_d__20` are
  not decompiled yet (`spfeat_clean` has `Awake`, `Start`, `_Prepare_d__19`).

## Goals / Non-Goals

**Goals:** the three base-game tracks behave like the test track does today for claims, results, visibility and rides;
lap records survive sessions and server restarts.

**Non-Goals:** DLC and Workshop tracks; drag racing; a shared race start (27b `track-races`); collisions (27c
`track-collisions`).

## Decisions

### D1. `TrackScenes`

Core table: `{ GameScene, SceneType, UnitySceneName, CarAwayKind }` for TestTrack (`Test_track_1`, manager
`TestTrackManager`), RaceTrack (`Race_track_1`, `RaceTrackManager`), SpeedTrack (`SpeedTrack`, map scene name
`Speedtrack`, both spellings map; manager `FreeTrackManager`). `IsTrack(GameScene)`, `ByUnityName(name)`,
`KindOf(GameScene)`, `IsTrackKind(CarAwayKind)`. Every former `== GameScene.TestTrack`/`CarAwayKind.TestTrack` check
that means "driving scene" uses `IsTrack`/`IsTrackKind`; checks that mean the test track's own features (examine
report, `TestToShow`, `TestTrackManager` tests) stay test-track only. Task 1.1 writes the classification of all 49
references (client: `GuardRules` 1, `CarAwaySync` 3, `TestDriveSync` 9, `DriveCapture` 3, `RideAlong` 9; core:
`GameScene` 1, `TestDrivePackets` 1; server: `CarAwayRegistry` 4, `CarDetailsStore` 1, `Rides` 7, `DriveHandlers` 1;
harness: 9) into a table below this decision; group 3 starts only after it.

Task 1.1 classification (2026-10-10, `origin/main` `4ec5c78`; lines as on that commit). **Track** = means "a driving
scene" and moves to the track set; **test track** = the test track's own feature, stays; **definition** = the value
itself.

| # | File:line | What | Class | Change |
|---|---|---|---|---|
| 1 | `Guard/GuardRules.cs:44` | `Scene TestTrack` allowed | test track | stays; `RaceTrack`, `SpeedTrack` get their own rows (3.4) |
| 2 | `Driving/RideAlong.cs:164` | passenger loads `Test_track_1` | track | loads `RideUpdate.Scene`'s track (D5) |
| 3 | `RideAlong.cs:190` | `OnLeavingScene` only from the test track | track | `IsTrack(from)` |
| 4 | `RideAlong.cs:213` | travelling ends on arrival at the test track | track | arrival at the ride's track |
| 5 | `RideAlong.cs:222` | waiting/seated only on the test track | track | the ride's track |
| 6 | `RideAlong.cs:277` | `FindObjectOfType<TestTrackManager>()` return | track | `TrackManager.Instance` (D5) |
| 7 | `RideAlong.cs:376` | `OwnTrackCar` | track | `IsTrack` |
| 8 | `RideAlong.cs:384` | `CopyOf` | track | `IsTrack` |
| 9 | `RideAlong.cs:402` | `TryGetAvatarSeat` | track | `IsTrack` |
| 10 | `RideAlong.cs:420` | `LateUpdate` camera and avatars | track | `IsTrack` |
| 11 | `Driving/DriveCapture.cs:70` | capture starts | track | `IsTrack` |
| 12 | `DriveCapture.cs:74` | capture stops | track | `IsTrack` |
| 13 | `DriveCapture.cs:112` | loader from the own `TestTrack` claim | track | `IsTrackKind` (the speed track's "car -1") |
| 14 | `Car/Away/CarAwaySync.cs:112` | `LockedForMe` out-parameter default | definition | stays (unused when false) |
| 15 | `CarAwaySync.cs:133` | `Activity` "on the test track" | test track | stays; race and speed track texts added |
| 16 | `CarAwaySync.cs:140` | `Label` "test drive" | test track | stays; race and speed track labels added |
| 17 | `Car/Away/TestDriveSync.cs:56` | departure hold only for `SceneType.TestTrack` | track | `TrackDriveSync`, any track (D3) |
| 18 | `TestDriveSync.cs:59` | `IsMine(loader, TestTrack)` | track | `KindOf(scene)` |
| 19 | `TestDriveSync.cs:64` | request kind `TestTrack` | track | `KindOf(scene)` |
| 20 | `TestDriveSync.cs:75` | refusal text fallback kind | track | the requested kind |
| 21 | `TestDriveSync.cs:88` | `MoveNext` prefix scene type | track | the departing scene type |
| 22 | `TestDriveSync.cs:103` | `SceneHooks.Leave(…, TestTrack)` | track | the departing scene type |
| 23 | `TestDriveSync.cs:118` | result sent when leaving the test track | track | `IsTrack(from)` |
| 24 | `TestDriveSync.cs:121` | result for the own `TestTrack` claim | track | `IsTrackKind` |
| 25 | `TestDriveSync.cs:171` | release after the return | track | `IsTrackKind` |
| 26 | `Core/…/TestDrivePackets.cs:9` | `CarAwayKind.TestTrack` | definition | `RaceTrack`, `SpeedTrack` appended |
| 27 | `Core/Data/Enum/GameScene.cs:15` | `GameScene.TestTrack` | definition | stays (`RaceTrack`, `SpeedTrack` exist) |
| 28 | `Server/…/DriveHandlers.cs:23` | `driveScenes = { TestTrack }` | track | the track set (D4) |
| 29 | `Server/…/CarAwayRegistry.cs:107` | scene rules only for `TestTrack` claims | track | `IsTrackKind` |
| 30 | `CarAwayRegistry.cs:110` | released unless the owner goes to the test track | track | the claim kind's own track |
| 31 | `CarAwayRegistry.cs:124` | 60 s grace back in the garage | track | `IsTrackKind` |
| 32 | `CarAwayRegistry.cs:126` | 15-minute watchdog skips `TestTrack` | track | `IsTrackKind` |
| 33 | `Server/…/CarDetailsStore.cs:218` | result only from a `TestTrack` owner | track | owner of any track kind |
| 34 | `Server/…/Rides.cs:44` | ride starts on a `TestTrack` grant | track | `IsTrackKind`; the ride keeps its track |
| 35 | `Rides.cs:59` | ride ends on a `TestTrack` release | track | `IsTrackKind` |
| 36 | `Rides.cs:69` | driver left the test track | track | the ride's track |
| 37 | `Rides.cs:70` | driver arrived elsewhere | track | the ride's track |
| 38 | `Rides.cs:75` | passenger arrived | track | the ride's track |
| 39 | `Rides.cs:80` | passenger left | track | the ride's track |
| 40 | `Rides.cs:86` | running rides sent to a client entering the test track | track | rides on the track entered |
| 41 | `TestHarness/…/DriveCommands.cs:528` | `drive-start` goes to the test track | test track | stays (`track-go` covers the others) |
| 42 | `SpFeatureProbeCommands.cs:19` | `track-go` table entry | definition | stays |
| 43 | `SpFeatureProbeCommands.cs:26` | `track-go` usage text | definition | stays |
| 44 | `TestDriveCommands.cs:29` | trace target `TestTrackManager.ReturnToGarage` | test track | stays (row 13 spike trace) |
| 45 | `TestDriveCommands.cs:30` | trace target `TestTrackManager.DoneTest` | test track | stays |
| 46 | `TestDriveCommands.cs:153` | `testdrive-hold` holds only the test-track departure | test track | stays (`departure-hold` spike) |
| 47 | `TestDriveCommands.cs:180` | `testdrive-go` | test track | stays (sets the examine report) |
| 48 | `TestDriveCommands.cs:346` | `testdrive-finish` uses `TestTrackManager` | test track | stays (`track-return` for any track) |
| 49 | `TestDriveCommands.cs:348` | `DoneTest` for every listed test | test track | stays |

33 lines move to the track set, 11 stay test-track features, 5 are definitions.

### D2. Scene identity

`ClientScene.FromSceneName` maps the three Unity names; `SceneReady` uses the name mapping first and the game's scene type
only when the name is unknown. The departure remembers the requested `SceneType` (`SceneHooks.Leave` already gets it) and
`SceneReady` logs a mismatch between the two.

### D3. Claims and results

`TestDriveSync` becomes `TrackDriveSync`: `HoldDeparture` reacts to any track scene type, requests `KindOf(scene)`, and the
`MoveNext` prefix checks `TrackScenes.IsTrack(__instance.sceneType)`. `CarAwayKind.RaceTrack` and `SpeedTrack` are appended;
`CarAwayRegistry.OnSceneChanged`, `Tick`, `CarDetailsStore`'s track-owner check and `Rides` use `IsTrackKind`, so a new
kind never falls into the "released when the owner leaves the garage" branch. `CarAwaySync.Activity` gains "on the race
track" and "on the speed track". On return, the mileage and dirt path is the same; `TestToShow` stays empty for the race
and speed tracks, so no examine report opens. Server self-check `AwayCheck` (style of `CarLocksCheck`): for each of the
three kinds, a claim survives the owner entering the track, is released 60 s after the owner is back in the garage, on
the owner going elsewhere and on disconnect.

### D4. Driving and observers

`DriveHandlers.driveScenes` = the track set; relay to players whose presence scene equals the driver's. `DriveCapture`
captures in any track scene and takes the loader from the own claim of any track kind (`IsTrackKind`), which removes the
speed track's "car -1". `RemoteCars` builds copies in any track scene. A pause-menu restart moves the car far more than
5 m, so `DriveInterpolator` already snaps; no restart hook.

### D5. Ride-along

The ride starts on a grant of any track kind; `RideUpdate` gains `[OptionalField] Scene` so the passenger loads the
driver's track (`TrackScenes` name). `RideAlong.Return` uses `TrackManager.Instance` (the base class has
`ReturnToGarage`) instead of `FindObjectOfType<TestTrackManager>()`. Everything else is row 21 unchanged. A passenger on
the race track runs the game's own `RaceTrackManager.Prepare` on a frozen car; D6 never sends from a passenger.

### D6. Lap records

- Hook: `RaceTrackManager.LastTime` postfix, once task 1.2 has decompiled `LastTime`, `NextCheckPoint` and
  `_Restart_d__20` (checked with `work\at.py` for folded bodies) and named which value holds the lap time, whether the
  method runs per lap or per finish, and the type and unit of `ProfileData.BestRaceTime`. Fallback: poll
  `lastBestTime` at 2 Hz on the race track.
- Send `TrackRecord { Scene = RaceTrack, LapMs }` when connected, the local player is the driver
  (`!RideAlong.IsPassenger`), and the lap is complete: a `LastTime` that follows a restart without all checkpoints done
  is ignored (if task 1.2 shows it can happen).
- Server (under `StateLock`): keeps `BestLapMs` per player identity (row 7's per-player data, written with
  `PlayerRestore`) and `GroupBestLap { PlayerName, LapMs }` in the world state; a new group record is broadcast as
  `TrackRecordUpdate { IsGroupRecord = true }`, a personal best only to its player. On join, `PlayerRestore` writes
  `BestLapMs` into the session profile's `BestRaceTime`, so the race track's own display shows it. Laps over 30 minutes
  or under 10 s are ignored (logged). The lap time comes from the driver's client and is trusted within these bounds;
  this is co-op, not a competitive ladder.
- Harness `track-lap <ms>` exercises the real hook: it sets `timer`'s elapsed time, marks the checkpoints done
  (`numberOfCheckpoints` and the list) and calls `LastTime()`. `track-record-send <ms>` sends `TrackRecord` directly and
  is used only for the server's bounds check.
- Speed track: if task 1.2 shows `topSpeed`/`lastTopSpeed` reach `ProfileData.TopSpeed`, the same path keeps a best top
  speed (`TrackRecord.Scene = SpeedTrack`, value in km/h); otherwise nothing is kept.

Task 1.2 result (2026-10-10, static: `native\out\tracks27a_clean`, targets `work\targets\tracks27a.txt`; every body is
its own, `at.py` finds no folded method):

- `RaceTrackManager.LastTime` (`0x18098DC00`) runs **once per lap**. Its only caller is `CheckPoints.OnTriggerEnter`
  (`xref.py`), on the `Meta` trigger with the `Player` tag and only when `numberOfCheckpoints == checkPointsList.Count
  - 1` (= `AllCheckpointsDone()`); afterwards the caller counts `laps++`, sets `numberOfCheckpoints = 0` and arms the
  first checkpoint again.
- The lap is `timer.ElapsedMilliseconds` (`long`, ms) read at entry. Under 110 000 ms it counts `stat_timeattack`. If
  (`CurrentSceneType == RaceTrack` and `lastBestTime == 0`) or the lap is below `lastBestTime`, it writes
  `lastBestTime` and `ProfileData.BestRaceTime` (`long`, ms, offset 0x30). Then it shows the times and **restarts the
  timer** (elapsed 0), so the hook is a prefix that reads the timer and a postfix that sends.
- `Prepare` (`<Prepare>d__19`) loads `lastBestTime` from `ProfileData.BestRaceTime` on the race track, resets
  `laps = 1`, `numberOfCheckpoints = 0` and the timer, and starts the lights on the first throttle; `Update` starts the
  timer at `readySetGo`. `<Restart>d__20` fades and runs `Prepare` again. So a restart resets the checkpoint count and
  the timer before any lap can end: **a partial lap after a restart cannot reach `LastTime`** (m6 needs no rule; the
  prefix still checks `AllCheckpointsDone()`).
- `RaceTrackManager.ReturnToGarage` sets `TestToShow = ""` (no examine report), saves the profile and loads `garage`.
- Speed track: `FreeTrackManager.Update` keeps `topSpeed` (`float`, km/h also with imperial units, which only change
  the text); `FreeTrackManager.ReturnToGarage` writes `lastTopSpeed = (int)topSpeed` and **`ProfileData.TopSpeed`
  (`int`, km/h, offset 0x38)** when higher, and `<Prepare>d__9` loads `lastTopSpeed` from it. So the speed track keeps
  a best top speed through the same path: a prefix on `FreeTrackManager.ReturnToGarage` reads `topSpeed`.
- The packets carry `Value` instead of `LapMs` (lap ms on the race track, km/h on the speed track):
  `TrackRecord { Scene, Value }`, `TrackRecordUpdate { Scene, PlayerId, PlayerName, Value, IsGroupRecord,
  IsPersonalBest }`. The world state keeps `GroupBestLap` and `GroupTopSpeed` (`ModTrackRecord { PlayerName, Value }`),
  the player record `BestLapMs` and `TopSpeedKmh`. An ignored value (out of bounds, or not a race or speed track) is
  answered to its sender with the stored personal best (`IsPersonalBest = false`), which the client writes back into
  the profile (D16).
- Harness: `track-lap <ms>` sets the stopped `timer`'s `elapsed` (in `Stopwatch` ticks), `numberOfCheckpoints =
  checkPointsList.Count - 1` and calls `LastTime()`; `track-topspeed <kmh>` sets `FreeTrackManager.topSpeed` before
  `track-return`.

### D7. Late join

A joiner gets the active claims (row 13 `SendActive`, now including track kinds), the running rides, the group record in
`world` and the personal best in `self`. Drive streams start for a joiner who travels to the same track (as row 17).

## Risks / Trade-offs

- [One of the 49 `TestTrack` checks is missed, leaving a track without a claim release or a relay] → D1's table, the
  group 3 review checks each line, `AwayCheck` covers each kind, and `test-drive`, `drive-track`, `ride-along` and
  `test-drive-latejoin` run unchanged.
- [`LastTime` is inlined or shares a native body] → `work\at.py`; fallback poll of `lastBestTime`.
- [The speed track's game scene type stays `TestTrack` in other game code paths (pause menu, `TestToShow`)] → our code
  uses D2's identity; the hand check 5.1 covers the speed track's pause menu and return.

## Migration Plan

Appended enum values and packets; optional fields in `self`, `world` and `RideUpdate`. Client and server update together
(version check). Rollback: revert; stored records are ignored by older servers.
