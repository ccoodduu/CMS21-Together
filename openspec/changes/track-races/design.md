# Design

## Context

- 27a (`shared-race-tracks`) makes the race track a driving scene with claims, drive relay (`DriveHandlers` relays to
  players in the driver's scene; `ActiveDrives` holds one drive per player), ride-along and lap records through a
  `RaceTrackManager.LastTime` postfix (task 1.2 of 27a names the lap value) and the harness `track-lap <ms>`, which sets
  the game's timer and checkpoints and calls the real `LastTime`.
- `RaceTrackManager` has `LastTime()`, `NextCheckPoint()`, `AllCheckpointsDone()`, `timer` (`Stopwatch`), `laps`,
  `lastBestTime` and a light sequence in `Prepare` (`_Prepare_d__19`, `WaitForSeconds` between lights, mode 15);
  `Restart` (`_Restart_d__20`) teleports the car to the start and resets the timer and laps; it may re-run a fade and
  reload the car physics (not decompiled yet).
- Ping: the client measures it per player (`MultiplayerMenuModel.PingMs`). `DriveInterpolator`'s clock offset is per
  drive stream and does not give a server clock. Harness instances on one PC share the system clock, so a wall-clock
  log of the green light compares the two clients directly.

## Goals / Non-Goals

**Goals:** one countdown for every racer on the race track, green within ±150 ms across clients; finishing order and
DNFs decided by the server from the racers' lap times; the last results kept.

**Non-Goals:** races on the speed track; collisions (27c); server-measured lap times (the driver's client measures, as
in 27a); spectator UI beyond the result toast.

## Decisions

### D1. Server race state

`TrackRaces` (runtime, under `StateLock`): at most one race per scene. `Race { RaceId, Scene, Laps, StartAtUtc,
Participants[{ PlayerId, Laps, TotalMs, BestLapMs, Finished, Dnf }] }`. `RaceStartRequest` is refused (`RaceRefused`,
D16) when a race runs on that scene, the requester has no `ActiveDrives` entry on the race track, the requester is a
ride-along passenger, or `Laps` is outside 1–20. Otherwise the participants are every player whose presence scene is
the race track with an active drive; `StartAtUtc` = now + 10 s (the restart and the light sequence; spike 1.1); the
server broadcasts `RaceCountdown` with `StartInMs = 10000` to the players in that scene.

### D2. Countdown on the client

On `RaceCountdown`, a participant computes `localStart = now + StartInMs − PingMs / 2` and, at `localStart −
LightsDuration` (measured by spike 1.1), calls `RaceTrackManager.Restart`; the game's lights then turn green at
`localStart`. The client logs the green-light wall-clock time (harness `race-state`). A restart our countdown starts is
marked so the quit hook ignores it. If spike 1.1 shows the light sequence is not deterministic (fade, physics reload),
the client instead freezes the car at the start, shows our own countdown overlay and starts the game's timer at zero at
`localStart`. A non-participant on the track sees the countdown toast only.

**Spike 1.1 result (2026-10-10, `20261010-050051_L1_race-spike`, headless, 10 runs; static `native\out\tracks27a_clean`).**
`Restart` is not deterministic as a whole: fade in, `WaitForSeconds(1)`, `Prepare` (`PrepareCarPhysics.LoadCar`,
`WaitForSeconds(1.5)`, fade out), 4.34 s from `RunRestart` to the end in every run. Then `_Prepare_d__19` waits in
state 3/4 for the player's **throttle** (`carInput.throttle > 0`) and only then runs the lights: three
`YieldInstructions.WaitForSecond` (`WaitForSeconds(1)`), then the green sprites, `canMove`, `readySetGo`; the timer
starts in the next `Update`. Throttle to green was 3067–3076 ms at 15 fps (3 s plus one frame). So D2 keeps the
game's lights, with this timing: the racer calls `RunRestart` as soon as the countdown arrives, a prefix on
`_Prepare_d__19.MoveNext` holds the throttle at 0 in states 3/4 until `localStart − LightsMs` (`LightsMs` = 3000) and
then sets it to 1 for that step, so the game's own lights turn green at `localStart` plus at most a frame. The
overlay fallback is not needed. `StartInMs` is 10 s instead of 5 s (4.3 s restart + 3 s lights + margin); a restart
that reaches the throttle wait late releases at once and the log names the delay. `WaitForEndOfFrame` resumes in the
headless games (the arrival `Prepare` reached the throttle wait). Start area: `PrepareCarPhysics` has one
`StartPosition`, so every racer starts on the same spot (27c).

### D3. Laps, quit and DNF

- 27a's `LastTime` postfix sends `RaceLap { RaceId, Lap, LapMs }` while a race runs for the local player (and still
  sends `TrackRecord` for records). Laps before `localStart` are not sent.
- `_Restart_d__20.MoveNext` first step, not started by D2: send `RaceQuit { RaceId }`.
- Server: a racer is finished after `Laps` laps (`TotalMs` = sum, `BestLapMs` = min). DNF on `RaceQuit`, leaving the
  race track (presence scene change), disconnect, or 10 minutes after the start. A lap with a lap number out of order or
  below 27a's 10 s bound is ignored (logged).
- When every participant is finished or DNF, the server broadcasts `RaceResult { RaceId, Order }` (finished by
  `TotalMs`, then DNFs) to everyone in the session, stores it in `WorldState.RaceResults` (last 10) and ends the race.

### D4. What the server stores and relays

Stored: the last 10 `RaceResult`s in the `world` section (`[OptionalField]`). Runtime only: running races. Relayed:
nothing beyond the broadcasts above; drive states stay 27a's relay.

### D5. Late join and restart

A player who arrives on the race track or joins the session during a race gets the running race's `RaceCountdown` with
a negative `StartInMs` (shown as "race in progress") and is not a participant. A joiner gets the stored results in
`world`. A server restart loses running races (DNF for all, logged at shutdown when possible); stored results survive.

### D6. Self-check

`RaceCheck` (style of `CarLocksCheck`, run at server start in debug and by the harness): a race with two participants
finishes in lap-time order; a quit, a scene change and a disconnect each give DNF; a second start on a running track is
refused; the timeout ends a race.

## Risks / Trade-offs

- [Half the ping is an estimate] → ±50 ms expected; lap times are measured locally, so only the start fairness suffers.
- [`Restart` re-runs a fade or reloads physics, so its timing varies] → spike 1.1 measures it; D2's overlay fallback.
- [Everyone starts on the same spot] → harmless with ghost cars; 27c keeps collisions off until the racers are apart.
- [`LastTime`/`Restart` folded or inlined] → 27a's task 1.2 checks with `work\at.py`; the fallback poll of
  `lastBestTime` gives lap times, and the quit marker falls back to the presence scene change only.

## Migration Plan

Appended packets and an optional `world` field. Client and server update together (version check). Rollback: revert;
stored results are ignored by older servers.
