# Tasks

Prerequisites: row 17 part 2, row 21 and 27a/b (merged). Client only; no packet or server change (design D5).

## 1. Spike

- [x] 1.1 Measure where the time goes and the frame times during the build, both arrival orders, on lane 2 (harness
      `remote-build`, `frame-log`, probe `remote-car-spike`); test the settle and a staged load. Done 2026-10-10
      (design "Spike"): arrival → shown 3.3–3.6 s today, all of it the 3 s settle plus a 0.22–0.5 s build; the first
      copy per scene visit has one 92–120 ms frame from the game's one-frame `LoadCar`; the staged prototype
      (`remote-stage`) brings it to 39–56 ms and the 0.5 s settle brings start → shown to 0.77–0.79 s.

## 2. Client

- [ ] 2.1 D1: `LocalCarSettleSeconds` 0.5 s; track "own car ready since" every frame on track scenes in
      `RemoteCars.Update`, reset on leaving the scene.
- [ ] 2.2 D2: register the loaders `CreateLoader` makes (`IntPtr` set, removed in `Destroy` and `Fail`); prefix and
      postfix on `CarLoader._LoadCar_d__215.MoveNext` (inside-LoadCar flag, hold while the queue is not empty);
      record-and-skip prefixes on the twelve deferred `CarLoader` methods; replay one group per frame from
      `RemoteCars.Update` with the groups of D2; a replay exception fails the copy; a removed copy drops its queue.
      A static switch (`RemoteCars.StagedLoad`, default on) lets the harness build a reference copy without staging.
- [ ] 2.3 D3: `RemoteCar.WaitSeconds`, `ShownAt`, staged group count; the ready log line names wait, build, longest
      frame and groups; dump fields `waitSeconds`, `shownAt`, `stagedGroups` and `remoteCars.readyAt`.
- [ ] 2.4 Remove the spike prototype `remote-stage` from the harness (the client does it now); keep `remote-build`
      (`state`, `count`, `ghost` gain a `staged on|off` argument for the reference copy) and `frame-log`.

## 3. Docs

- [ ] 3.1 docs/playtest.md: on arrival at the test track where a friend drives, the friend's car shows in about a
      second without a hitch; read `longest frame` in the `Observer car … ready` log line with a visible game (must be
      under 0.13 s). ROADMAP row 31 status; STATUS entry with the run ids; QUESTIONS.md row 17 #8 marked fixed.

## 4. Proof

- [ ] 4.1 Scenario `remote-car-timing` (design "Proof plan"): both clients `fps-cap 60`; A drives, B arrives: B shows
      A's car with `shownAfter` ≤ 1.5 s and `readyAt` → `shownAt` ≤ 2.0 s, A shows B's car with `shownAfter` ≤ 1.0 s;
      both copies `longestFrame` ≤ 0.075 s and no `frame-log` frame over 75 ms from build start to shown + 1 s; mode
      `ghost`, 4 wheels, engine on, kinematic, colliders off; a staged copy and a reference copy (staging off) have
      equal transform, renderer, collider, part script and car part counts; then B first and A arriving, the same.
      Write it first and run it on the old code: it must fail on `shownAfter` (3.2–3.6 s) and `longestFrame`
      (92–120 ms). Then passes with 2.1–2.3.
- [ ] 4.2 Tighten the existing checks: `drive-track` and `ride-along` "no frame over 1 s" become 0.13 s; the
      `drive-latejoin` note's "spec: 2 s" becomes a check (`shownAfter` ≤ 1.5 s).
- [ ] 4.3 Regression: `Run-All -Lanes 2 -Smoke -Scenarios remote-car-timing,drive-track,drive-latejoin,ride-along,race-track,race-start`
      (and `track-collisions` scenarios if 27c is merged by then).
