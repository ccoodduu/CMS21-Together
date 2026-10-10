# Tasks

Prerequisites: 27a `shared-race-tracks` merged (track set, drives on the race track, `LastTime` hook, `track-lap`);
rows 7, 8, 17 part 2, 19 part 3 (merged).

## 1. Spike (S ≈ 0.5–1)

- [x] 1.1 On the race track: time `RaceTrackManager.Restart` from the call to the green light over 10 runs (fade,
      physics reload, `WaitForSeconds` in `_Prepare_d__19`); decide D2's path (game lights or our overlay) and the
      `LightsDuration`; check the start area (one car spot or room for a grid) for 27c. Done when D2 names the path.
      Done 2026-10-10 (`20261010-050051_L1_race-spike`, design.md D2 "Spike 1.1 result"): the restart takes 4.34 s to
      the throttle wait, the lights run only after the throttle and take 3 s plus a frame. Path: the game's lights,
      released by a throttle gate at `localStart − 3000 ms`; `StartInMs` 10 s; one start spot.

## 2. Core and server

- [x] 2.1 Packets `RaceStartRequest`, `RaceRefused`, `RaceCountdown`, `RaceLap`, `RaceQuit`, `RaceResult` (appended);
      `ModRaceResult`, `WorldState.RaceResults` (`[OptionalField]`).
- [x] 2.2 `TrackRaces` (D1, D3, D5): start rules, participants, laps, DNF on quit, scene change, disconnect and timeout,
      result broadcast and storage; server command `races`; `RaceCheck` self-check (D6). The self-check runs as
      `--check-races`; a server stop logs the running race as not finished and does not store it. A racer who left and
      comes back during the race watches it (the countdown lists only racers still racing).

## 3. Client

- [x] 3.1 `TrackRaceSync`: "Start race" entry (open question 1 default: session panel), request and refusal message,
      countdown timing and `Restart` call or overlay (D2), quit hook (D3), countdown and result toasts. The F9 panel
      shows the race block (laps −/+, "Start race", status, last result) only on the race track. The countdown is
      timed from the packet's receive time on the network thread (`PacketClock`), not from the frame that handles it,
      and the release leads by 1.5 frames (the release waits for a frame, the light waits end a frame late).
- [x] 3.2 27a's `LastTime` postfix sends `RaceLap` during a race.
- [x] 3.3 Harness `race-start <laps>`, `race-state`; dump section `race`. Also `race-restart` (the pause menu's
      restart) and the spike's `race-spike-*`.

## 4. Proof

- [x] 4.1 Scenario `race-start` (two clients, both drive to the race track with `track-go`, `guard-allow
      Mode:CarDrive`):
      1. A `race-start 1` → both `race-state` show the same race id with both players as participants;
      2. both clients' green-light wall-clock times within 150 ms;
      3. `track-lap 90000` on A and `track-lap 95000` on B (27a's real path) → `RaceResult` on both with A first, B
         second, no DNF; server `races` shows the stored result;
      4. A `race-start 2`; B leaves the race track mid-race → result with A finished (after two `track-lap`) and B DNF;
      5. B `race-start 1` while A's race runs → refused with the message;
      6. late join: B joins the session again → B's `world` has both results; a server restart → still both.
      Old-code failure: the old code has no `RaceCountdown` packet type, so step 1 fails. Verify: `Run-Session.ps1
      -Scenario race-start` passes; `race-track`, `drive-track` and the smoke set pass (`Run-All -Changed`, area
      `driving`).
      Built with step 5 done by B after coming back to the track during A's race (B watches it, so the server's
      refusal is tested, not the client's own check) and a third race in which B's pause-menu restart is a DNF.
      Fails on the old code (`20261010-091535_L1`, main `5debf9d`: no race verbs); passes after merging main
      (`20261010-092317_L1`, green lights 14 ms apart) in `20261010-091648_regression.json` with smoke, `race-track`,
      `drive-track`, `drive-latejoin`, `ride-along` and `guard` (10/10 and server-saves); `--check-races` OK.

## 5. Docs

- [x] 5.1 docs/playtest.md: a two-lap race against a friend with a visible game (start fairness by eye, result toast);
      docs/try-it.md: how to start a race; ROADMAP row 27b status; STATUS entry with run ids.
