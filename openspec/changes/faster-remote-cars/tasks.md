# Tasks

Prerequisites: row 17 part 2, row 21 and 27a/b (merged). Client only; no packet or server change (design D5). Scope
B (user, 2026-10-10): D1 only.

## 1. Spike

- [x] 1.1 Measure where the time goes and the frame times during the build, both arrival orders, on lane 2 (harness
      `remote-build`, `frame-log`, probe `remote-car-spike`); test the settle and a staged load. Done 2026-10-10
      (design "Spike"): arrival → shown 3.3–3.6 s, all of it the 3 s settle plus a 0.22–0.5 s build; the first copy
      per scene visit has one 92–120 ms frame from the game's one-frame `LoadCar`; the staged prototype brought it to
      39–56 ms (not built, design D2).

## 2. Client

- [x] 2.1 D1: `LocalCarSettleSeconds` 0.5 s; "own car ready since" tracked every frame on track scenes in
      `RemoteCars.Update`, reset on leaving the scene.
- [x] 2.2 D3: `RemoteCar.WaitSeconds`, `ShownAt`, `RemoteCars.SceneReadyAt`; the ready log line names the wait; dump
      fields `waitSeconds`, `shownAt`, `remoteCars.readyAt`; harness `remote-build state`.
- [x] 2.3 Remove the D2 prototype `remote-stage` from the harness; `remote-build` patches `RemoteCars` only for
      `wait` and `trace` (patching during a build made a 0.22 s frame).

## 3. Docs

- [x] 3.1 QUESTIONS.md row 31 scope answered (B) and row 17 #8 marked; ROADMAP row 31 status; STATUS entry with the
      run ids.
- [ ] 3.2 docs/playtest.md: on arrival at the test track where a friend drives, the friend's car shows in about a
      second; read `longest frame` in the `Observer car … ready` log line with a visible game (target under 0.13 s).
      Waits for the next playtest checklist.

## 4. Proof

- [x] 4.1 Scenario `remote-car-timing` (design "Proof"): both clients `fps-cap 60`, both arrival orders; arrival →
      shown ≤ 1.5 s, drive start → shown ≤ 1 s for the player already driving, no frame over 0.2 s while the copy
      loads (WARN note over 0.13 s), own car drives. Fails on the old behaviour (`20261010-120734_L2`: 3.2–3.4 s),
      passes (`20261010-121219_L2`).
- [x] 4.2 `drive-latejoin` checks `shownAfter` ≤ 1.5 s.
- [x] 4.3 Regression on lane 2: smoke plus `remote-car-timing`, `drive-track`, `drive-latejoin`, `race-track`,
      `race-start`, `race-hardening`, `ride-along`: `20261010-121718_regression.json`, all passed (`guard` FLAKY: "no
      PieMenuController in this scene" in the batch, passed alone; not a driving path).
