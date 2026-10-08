# Proposal

## Why

The user wants to drive on the race track in multiplayer like on the test track (2026-10-08, ROADMAP row 27): the car
claimed while away (row 13), other players see it driving (row 17 part 2), a second player can ride along (row 21).
DLC tracks are not wanted, only base-game ones.

Which tracks the game has (static spike `docs/spikes/singleplayer-features.md` section 5; the travel gate is
`CMS.Helpers.SceneHelper.CanGoToScene`, the DLC table `CMS.Platforms.Steam.SteamDLC.Init`):

| `SceneType` | Scene in the build | Map destination | Gate | Verdict |
|---|---|---|---|---|
| `RaceTrack` (7) | `Race_track_1` (`RaceTrackManager`: laps, lap times, best time) | `RaceTrack` | none | **base game** |
| `SpeedTrack` (17) | `SpeedTrack` (`FreeTrackManager`: free driving) | `SpeedTrack` | none | **base game** |
| `DragStrip` (13) | `Dragstrip` | `Dragstrip` | `IsDLCInstalled(22)` = **Drag Racing DLC** (Steam app 2112231); the map hides or locks it without the DLC | DLC, not wanted |
| `CustomTrack` (14) | `Custom_track` (loads a track bundle, `CustomTrackLoader`) | `WorkshopMaps` | needs Steam Workshop track items (reason 5) | not DLC but user-made Workshop maps; out by default (open question 3) |
| `FunTrack` (11), `OffroadTrack` (12) | none | none | — | enum values without a scene in this game version; unreachable |

So **the race track is not a DLC track**; neither is the speed track.

What happens today (runtime spike runs `20261008-230909_L2` and `20261008-231615_L2`, lane 2, two clients):

- With the guard on, both trips are refused (`[Guard] Blocked Scene:RaceTrack`).
- With `guard-allow`, the trips work and the driven distance reaches the car (mileage 0 → 4 km after two 2.5 km drives,
  stored by the server), but:
  - **the car is not claimed** while it is away: row 13's departure hold only reacts to `SceneType.TestTrack`, so the
    other player could take parts off, move, sell or delete the car while it is being driven (`away` empty on B);
  - **nobody sees the driver** on the race track: the server relays driving only for the test track
    (`DriveHandlers.driveScenes`), and the client captures only there;
  - **the speed track pretends to be the test track**: the game's `GameScript.CurrentSceneType` is `TestTrack` in the
    `SpeedTrack` scene, so our client reports "SpeedTrack ready as TestTrack", puts the player on the test track in the
    presence roster and starts a drive stream for car -1 (no claim);
  - ride-along (row 21) only works on the test track.
- Race track best time (`ProfileData.BestRaceTime`) lives in the session profile, which is new for every join, so a
  player's best lap is forgotten after each session.

## What Changes

- **Track set.** One definition of the base-game driving tracks: test track, race track, speed track
  (`TrackScenes`), used by the guard, scene tracking, away claims, driving and ride-along.
- **Scene identity by name.** A track scene is identified by the scene the player travelled to
  (`SelectSceneToLoad`'s `sceneType`, remembered at departure) and the Unity scene name (`Test_track_1`,
  `Race_track_1`, `SpeedTrack`), not by `GameScript.CurrentSceneType`, which is wrong on the speed track.
- **Away claim on every track.** `CarAwayKind` gains `RaceTrack` and `SpeedTrack` (appended). The departure hold, the
  server's release rules (back in the garage, 60 s grace, owner elsewhere, disconnect) and the results on return
  (mileage and dirt, row 13's `LeavingScene` path) apply to all three. No examine report for the race and speed
  tracks (the game sets none).
- **Driving is visible** on the race and speed tracks: the server relays drive states to players in the driver's track
  scene, and observers build the driver's car as on the test track (row 17 part 2, no collisions: ghost cars).
  A jump of more than 20 m in the stream (the race track's pause-menu restart) snaps the observer car instead of
  sliding it.
- **Ride-along** (row 21) works on all three tracks: a seated passenger follows the driver to the driver's track.
- **Best lap per player.** When the race track records a lap (`RaceTrackManager.LastTime`), the driver's client sends
  the lap time; the server keeps each player's best (per identity, in the `self` data) and the group's best (`world`),
  writes the player's best into the session profile on join (`BestRaceTime`), and tells everyone in the session when
  the group record falls ("New group record on the race track: Ann, 1:23.456").
- **Guard.** `Scene RaceTrack` and `Scene SpeedTrack` become allowed (owner row 27). `DragStrip` is labelled "Drag
  strip (Drag Racing DLC)", `CustomTrack` "Workshop tracks", `FunTrack`/`OffroadTrack` "not in this game version",
  all still refused.

Hooks: the existing departure hooks (`NotificationCenter._SelectSceneToLoad_d__34.MoveNext`, `SceneHooks`) widened to the
track set; `RaceTrackManager.LastTime` (postfix, lap time), `RaceTrackManager._Restart_d__20.MoveNext` (first step:
restart marker for observers). Packets: `CarAwayKind` values appended; new `TrackRecord { Scene, LapMs }` (client →
server) and `TrackRecordUpdate { Scene, PlayerId, LapMs, IsGroupRecord }` (server → clients).

## Capabilities

### New Capabilities
- `track-driving`: driving a garage car on the test, race and speed tracks with the car claimed, the result returned,
  the driver visible to others on the same track, ride-along, and lap records kept by the server.

### Modified Capabilities
- None in `openspec/specs/` (rows 13, 17 and 21 are not archived; this change widens their test-track rules to the track
  set; task 6.1 records that in their documents).

## Impact

- Core: `TrackScenes` (new: scene types, Unity scene names, `CarAwayKind` per track), `CarAwayKind` (+`RaceTrack`,
  `SpeedTrack`), `PacketTypes` (two packets appended), `PlayerState`/`self` data (`BestLapMs`), world state
  (`GroupBestLap`).
- Server: `CarAwayRegistry` (track kinds), `Rides` (track kinds), `DriveHandlers.driveScenes` (track set),
  `TrackRecords` (new), `self` and `world` section additions (optional fields).
- Client: `ClientScene` (track names), `SceneReady` (scene by name for tracks), `TestDriveSync` → `TrackDriveSync`
  (departure for every track), `DriveCapture`, `RemoteCars`, `RideAlong` (track set), `Logic/Driving/TrackRecords.cs`,
  `Guard/GuardRules.cs`.
- Harness: `track-go <loader> <RaceTrack|SpeedTrack|TestTrack>`, `track-state`, `track-return` (from the spike),
  `track-lap <ms>` (calls the lap path with a given time); scenario `race-track`.
- Depends on (merged): rows 6, 13, 17 part 2, 21, 14a; row 7 (`self` data).

## Open questions

Each has the default the draft works with.

1. **Collisions.** Observer cars have no colliders (row 17's freeze fix). **Default:** ghost cars, players drive
   through each other. Real collisions would need physics authority between clients (not planned).
2. **Racing each other.** Each player's race (countdown, laps) runs on their own client. **Default:** no shared start;
   the group record is the shared part. A shared countdown could follow later.
3. **Workshop tracks** (`CustomTrack`). Not DLC, but they need the same Workshop track installed on every client.
   **Default:** stay blocked. Alternative: allow when every connected player reports the same track id.
4. **Best lap storage.** **Default:** per player plus one group record, kept in the server save.
5. **Speed track top speed** (`ProfileData.TopSpeed`). The speed track runs `FreeTrackManager`; whether anything writes
   `TopSpeed` there is checked in task 1.2. **Default:** treat it like the best lap if the game sets it; ignore it
   otherwise.
