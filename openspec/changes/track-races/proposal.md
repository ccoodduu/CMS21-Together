# Proposal

## Why

The user wants to race each other on the race track: a shared start with a countdown and results kept by the server
(decision 2026-10-08, ROADMAP row 27b). Row 27a (`shared-race-tracks`) lets players drive on the race track together and
keeps lap records, but each player's race (lights, timer, laps) still runs on their own client, so two players cannot
start at the same moment or see who won. The review of row 27 (`shared-race-tracks/review.md`, "U1") split this into
its own change.

Races are on the **race track only** (`Race_track_1`, `RaceTrackManager`). The speed track (`FreeTrackManager`) has no
race, laps or lights; a speed-track rule (first to a distance, best top speed in N seconds) is not part of this change.

## What Changes

- **Start.** A player on the race track with an active drive (not a ride-along passenger) chooses "Start race" with a
  lap count. The client sends `RaceStartRequest { Scene, Laps }`. The server's new `TrackRaces` (under `StateLock`)
  creates a race whose participants are the players whose presence scene is the race track and who have an
  `ActiveDrives` entry. It refuses with a D16 answer (`RaceRefused { Reason }`: a race already runs on the track, no
  drive, laps out of range 1–20).
- **Countdown.** The server broadcasts `RaceCountdown { RaceId, Scene, Laps, Participants, StartInMs }` to the players on
  the race track. Each client converts `StartInMs` to local time minus half its measured ping (`PingMs`) and runs the
  game's own sequence so the green light falls on the shared start: `RaceTrackManager.Restart` (teleport to the start,
  `timer`/`laps` reset, lights in `Prepare` with `WaitForSeconds`), started early by the measured length of the light
  sequence. If the sequence is not deterministic (spike 1.1), our own overlay counts down and starts the game's timer at
  zero.
- **Results.** Row 27a's `RaceTrackManager.LastTime` postfix also sends `RaceLap { RaceId, Lap, LapMs }` while a race
  runs for the player. The server marks a racer finished after `Laps` laps and records DNF on leaving the scene,
  disconnecting, a pause-menu restart (`RaceQuit { RaceId }` from a `_Restart_d__20` hook, except for the restart our
  countdown starts itself) or a timeout (10 minutes after the start). When every racer is finished or DNF, it broadcasts
  `RaceResult { RaceId, Order[{ PlayerId, TotalMs, BestLapMs, Dnf }] }`; the clients show it. Every lap still feeds 27a's
  personal best and group record. The server keeps the last 10 results in `world` (`[OptionalField] RaceResults`).
- **Late join and restart.** A player arriving on the race track during a race watches and is not a participant. A
  server restart ends running races (DNF for all, logged); stored results survive.
- **Start grid** (revision 2026-10-10, D7). Each racer starts on a box of the race track's painted grid (20 boxes):
  the starter on pole, the others in their order of arrival on the track. 27c (`track-collisions`) keeps collisions
  off during the countdown; after the green, only racers who share a box (more than 20) wait until they are 10 m
  apart.

Hooks: `RaceTrackManager.LastTime` (27a's postfix, extended), `RaceTrackManager._Restart_d__20.MoveNext` (first step:
quit marker), `RaceTrackManager.Restart` (called by the countdown). Packets (new, appended): `RaceStartRequest`,
`RaceRefused`, `RaceCountdown`, `RaceLap`, `RaceQuit`, `RaceResult`; `WorldState.RaceResults` (`[OptionalField]`).

## Capabilities

### New Capabilities
- `track-races`: a shared race on the race track with one countdown for every racer, lap times collected by the
  server, a result with the finishing order and DNFs, kept by the server.

### Modified Capabilities
- None in `openspec/specs/` (27a is not archived; this change extends its lap hook).

## Impact

- Core: packets above, `ModRaceResult`, `WorldState.RaceResults`.
- Server: `TrackRaces` (new; participants, laps, DNF rules, timeout, result), `world` section optional field, server
  command `races`, a `RaceCheck` self-check (style of `CarLocksCheck`).
- Client: `Logic/Driving/TrackRaceSync.cs` (new: request, countdown timing, `Restart` call, quit marker, result view),
  27a's `TrackRecords` (lap hook sends `RaceLap`), a "Start race" entry (open question 1), toasts for countdown and
  result.
- Harness: `race-start <laps>`, `race-state` (race id, participants, local green-light time as wall clock, laps,
  result); dump section `race`; scenario `race-start`.
- Depends on: 27a `shared-race-tracks` (track set, drives on the race track, `LastTime` hook, `track-lap`), rows 7, 8
  (session panel, ping), 17 part 2 (`ActiveDrives`), 19 part 3 (D16).

## Open questions

Each has the default the draft works with.

1. **Where the player starts a race.** **Default:** a "Start race" button with a lap count in our session panel (F9,
   row 8) while on the race track with a drive. Alternative: an entry in the game's pause menu (more natural, more UI
   work).
2. **Speed track.** **Default:** no race there in this change. A top-speed challenge (best `topSpeed` within N seconds)
   or a distance race can follow if the user picks a rule.
3. **Who may start.** **Default:** any participant; a running race blocks a new one until its result.
