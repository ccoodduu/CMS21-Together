# Design

## Context

- Row 17 part 2 (`remote-visual-feedback` D10) shows another player's track car as an inert copy: `RemoteCars.Build`
  waits until the player's own track car is ready (`LocalCarReady`: car loaded, `gameMode.CarDrive`, VPP initialized)
  and then `LocalCarSettleSeconds` = 3 s more, then steps `CarLoader.LoadCarFromFile(blob)` one `MoveNext` per frame
  on a cloned loader and calls `StopPhysics` after every step. Rows 21 (ride-along) and 27a/b (race and speed track)
  use the same copy.
- The 3 s settle came on 2026-10-07 (`72625e8`, `eaf82a5`) because a build that started while the own car loaded
  froze the game for over 10 s. The cause was found the next day (`5a9b036`): the copy's live colliders on the track's
  car spot stalled the driver's VPP wheel queries; `StopPhysics` after every load step fixed it. The settle stayed.
- The proposal's numbers (6.5 s load, ~10 s / ~7 s until the car shows) predate that fix. The spike below measured
  the current code.
- User decision (QUESTIONS.md, 2026-10-08): about 3 s until the car appears is fine, as long as loading it gives no
  frame over 0.13 s.

## Spike (2026-10-10, lane 2)

Harness tools added for it: `remote-build` (build trace per `BuildSteps` step with its `StopPhysics` share, settle
override `wait game|none|settle <s>`, extra copies `ghost`, `count`, `park`, `remove`, cheap `state` poll),
`frame-log <from> [minMs]` (every frame's real-clock duration from the harness's frame ring, now 32768 frames) and
`remote-stage` (the D2 prototype). Probe scenario `remote-car-spike` (`# run-all: skip`): round "a-first" (A drives,
B arrives: B builds A's car on arrival, A builds B's car while already there), round "b-first" with the roles swapped,
then extra copies on one client. All games headless (`-nographics`) with lane 1 busy at the same time, the same car
model on both sides (`car_boltatlanta`), frame cap 60 fps unless stated (the headless default cap is 15 fps, which
hides every cost under 66 ms).

| Run (`tools\runs\…_L2_remote-car-spike`) | Setup | Result |
|---|---|---|
| `20261010-103952` | 15 fps (default), 3 s settle | Arrival → start packet 0.06–0.07 s; wait 3.0 s; build 0.49–0.50 s (8 steps, one per 66 ms frame); longest frame 91–102 ms |
| `20261010-104439` | 60 fps, 3 s settle | Wait 3.0 s; build 0.22–0.26 s; the first copy in a scene visit has one frame of 92–96 ms; later copies of the same model 25–40 ms; removing a copy: one frame of 45–75 ms; hiding / showing a built copy 0.3–0.5 / 1.4–2.6 ms |
| `20261010-105528` | 60 fps, 3 s, `method-time` on the `LoadCar` stages | The 92–96 ms frame is the game's `LoadCar` coroutine doing every stage in one frame: `CreateChassis` 31–33, `CreateEngine` 22–24, `CreateParts` 24–27, the rest < 3 ms. Later copies: 4 / 8 / 2 ms (assets loaded). The own track car pays 47 / 58 / 31 ms in one frame, hidden by the loading screen |
| `20261010-105122` | 60 fps, no wait at all | Start packet → shown 0.22–0.29 s; no freeze; frames 100–120 ms (the build lands on the game's own arrival frame) |
| `20261010-110209` | 60 fps, 3 s, D2 staged | Longest frame 41–56 ms; identical copies (627 transforms, 509 renderers, 509 colliders, 228 part scripts, 27 car parts, as unstaged); no replay errors |
| `20261010-110810` | 60 fps, no wait, D2 | Start → shown 0.25–0.28 s; the arriving client's build starts in the game's arrival frame and makes it 69–84 ms |
| `20261010-110523`, `20261010-110957` | 60 fps, 0.5 s settle, D2 | Start packet → shown 0.77–0.79 s; longest frame 39–48 ms |

Other findings:

- Without any build, the arriving client has one game frame of 63–79 ms at +0.06–0.08 s after "ready as TestTrack"
  (the scene's ready step). A second hitch at +0.6–1.6 s seen in some runs was the harness's own first `dump`; the
  probe now polls `remote-build state`.
- The server already sends the running drives on arrival (`DriveHandlers.OnSceneChanged`): the start packet reaches
  the arriving client 0.06–0.07 s after its scene is ready. Our per-frame stepping of `LoadCarFromFile` only polls the
  loader's `done`; the real work runs in the game's `LoadCar` coroutine (`StartCoroutine`), whose own staged path
  (`<asyncLoading>5__3`, yields between chassis, interior, engine, exterior and parts) is hard-wired off.
- `method-time` on all of `CarLoader` (374 methods) hung the arriving game in `20261010-103549`; patch named methods
  only.
- Today's arrival → shown is 3.3–3.6 s (drive-latejoin notes agree: 3.5 s), not 10 s. What remains to fix is the
  3 s settle and the 92–120 ms frame of the first copy, which has less than 40 ms margin to 0.13 s headless and
  none to spare for rendering in a visible game.

## Goals / Non-Goals

**Goals:** another player's track car shows within 2 s of arriving (expected ~0.85 s) and within 1 s of the other
player's drive start when already there; no frame over 0.13 s from building it (build work at most ~40 ms in any
frame on the test PC); the copy looks exactly as today.

**Non-Goals:** keeping copies between drives (option 2), loading before arrival (option 3), a lighter copy
(option 4) (see D4); the removal frame (45–75 ms, not loading); the own car's load (behind the loading screen).

## Decisions

### D1. Readiness signal: own car ready plus 0.5 s

`LocalCarReady` keeps its conditions; `LocalCarSettleSeconds` becomes 0.5 s. The "ready since" time is tracked
every frame in `RemoteCars.Update` while the scene is a track scene (not only while a build waits), so a player who
is already driving when another player arrives does not wait again; leaving the scene resets it, as today.

Why 0.5 s: the arriving client's own scene-ready frame (63–79 ms) comes 0.06–0.08 s after arrival; a build started
with no wait lands its first frames on it (69–84 ms with D2, 100–120 ms without). 0.5 s clears it with margin and
costs 0.5 s of the 3 s budget. The freeze the 3 s guarded against is fixed by `StopPhysics`; eight builds with no
wait at all (`105122`, `110810`) showed none.

### D2. Staged load: the game's own stages, one per frame

For loaders `RemoteCars` creates (registered by `IntPtr` in a set when `CreateLoader` makes them, removed in
`Destroy`/`Fail`), the client splits the one frame in which `LoadCar` builds the car:

1. A prefix on `CarLoader._LoadCar_d__215.MoveNext` sets "inside LoadCar of a registered loader" (a postfix clears
   it).
2. Prefixes on `CarLoader.CreateEngine(string)`, `SetEngine`, `CreateDriveshaft`, `SetDriveshaft`, `CreateExterior`,
   `SetExterior(bool)`, `CreateParts`, `CreateBonusParts(bool, bool)`, `SetLicensePlateNumber()`, `SetParts`,
   `SetBlockedBy`, `SetUnmountWithCarParts` record the call (method and arguments) in the loader's queue and skip it,
   only while inside `LoadCar` of a registered loader and not replaying. `CreateChassis`, `CreateWheels` and
   `CreateInterior` run in `LoadCar`'s frame as today (~35 ms the first time).
3. While a loader's queue is not empty, the `MoveNext` prefix returns `true` without running, so `LoadCar` stays at
   its next state (7: `PreparePartScriptCuller`, rust map, `done`).
4. `RemoteCars.Update` replays one group per frame through the original methods, in the recorded order, cut where
   the game's disabled `asyncLoading` path yields: [`CreateEngine`, `SetEngine`] (~23 ms), [`CreateDriveshaft` …
   `SetExterior`] (~5 ms), [`CreateParts`] (~25 ms), [`CreateBonusParts` … `SetUnmountWithCarParts`] (< 1 ms). Then
   `LoadCar` resumes and sets `done`; `LoadCarFromFile` (still stepped by `BuildSteps`) applies the car data as today.
5. An exception in a replayed call ends the build like a load exception today (`Fail`: warning, no copy). A copy
   removed while its queue is pending drops the queue.

Measured with the harness prototype (`remote-stage`): the first copy's worst frame drops from 92–96 ms to 41–56 ms,
the build takes three frames longer (0.26–0.28 s at 60 fps), and the copy is identical. The other remaining costs are
`LoadCarFromFile`'s data step (27–51 ms with its `StopPhysics`), the first `StopPhysics` after the parts appear
(15–20 ms) and `MakeInert` (4–24 ms), each in its own frame.

### D3. Measurement in the client

`RemoteCar` gets `WaitSeconds` (start packet → build start) besides `BuildSeconds`, `LongestFrame` (real clock, the
build's frames only) and `ShownAfter`; the ready log line names all four and the number of staged groups. The
`remoteCars` dump shows them, each car's `shownAt` and the scene's `readyAt` (real clock, from `ClientScene`). These
are what the proof checks and what a visible playtest reads from the log.

### D4. Options not taken

| Option | Measured | Why not now |
|---|---|---|
| 2. Keep the copy between drives | Re-show 1.4–2.6 ms, hide 0.3–0.5 ms; saves the 0.26 s build, the 0.5 s settle after a restart and the 45–75 ms removal frame | Not needed for the target; worth it if race restarts (27b) feel slow. Memory per copy not measured reliably (the process delta is noise) |
| 3. Load before you arrive | Start packet arrives 0.06–0.07 s after the scene is ready | Already the case; nothing to gain |
| 4. Lighter copy | Base model (no car data) 0.14 vs 0.20 s, worst frame 23–28 vs 37–40 ms on repeat builds; `CreateInterior` 0.4 ms | The first-copy cost is chassis, engine and parts asset loads, not hidden parts; a lighter copy looks different |
| Asset pre-warm (`Resources.LoadAsync`) | `CreateParts` loads with `Resources.Load` | The paths come from game logic per car config |

### D5. Server and late join

Nothing new on the server: it stores the running drives and relays starts, states and stops as today, and already
sends the running drives to a player who arrives (`OnSceneChanged`). A player who joins or reconnects mid-drive
arrives through the same path, so D1 and D2 apply unchanged. No packet changes.

### D6. Interaction with other rows

- Ride-along (21) and race/speed track copies (27a/b) use the same build, so they get D1 and D2.
- Collisions (27c): the copy's colliders stay off through every stage (`StopPhysics` after every step, unchanged);
  any collision proxy is added after `MakeInert`, as before.

## Proof plan

New scenario `remote-car-timing` (both clients `fps-cap 60`, two garage cars, `guard-allow Mode:CarDrive`, observers
polled with a cheap verb, not `dump`):

1. A drives on the test track; B travels there. B shows A's car with `ShownAfter` ≤ 1.5 s and B's scene-ready →
   shown ≤ 2.0 s; A shows B's car (A already there) with `ShownAfter` ≤ 1.0 s.
2. Both copies: `LongestFrame` ≤ 75 ms, and the harness `frame-log` shows no frame over 75 ms from build start to
   shown + 1 s; mode `ghost`, 4 wheels, engine on, kinematic, colliders off.
3. A staged copy and a reference copy built with staging off (harness toggle) have equal transform, renderer,
   collider, part script and car part counts.
4. Both return, B goes first and A arrives: steps 1–2 with the roles swapped.

The old code fails steps 1 and 2: `ShownAfter` 3.2–3.6 s (> 1.5) and `LongestFrame` 92–120 ms at 60 fps (> 75)
(`104439`, `105528`, `105122`). The 75 ms bound is the headless form of the 0.13 s limit: build work ≤ ~58 ms on a
16.7 ms frame leaves room for rendering in a visible game; the new code measured 39–56 ms.

## Risks / Trade-offs

- [The staged replay depends on `LoadCar`'s order] → the deferred calls are the ones the game itself separates with
  yields in its `asyncLoading` path; step 3 of the proof compares the copy with an unstaged one; a game update that
  changes `LoadCar` shows there.
- [Prefixes on `CarLoader` methods every car load uses] → the prefix checks one static flag first; only registered
  loaders inside their `LoadCar` frame are touched.
- [Holding an IL2CPP coroutine by skipping `MoveNext`] → `LoadCar` yields `WaitForEndOfFrame`, which Unity resumes
  every frame; the prototype held it 3–6 frames per copy without effect on the game.
- [Headless numbers] → no rendering or GPU upload is measured; the visible playtest reads `LongestFrame` from the log
  (docs/playtest.md task) and the 75 ms bound leaves ~55 ms for it.
- [A different car model per player] → the spike used one model for both; the first-copy costs are per model and per
  scene visit, so a second model costs the same as the first (measured: the first copy of each visit).

## Migration Plan

Client only. Rollback: revert; the 3 s settle and the one-frame load come back.

## Open Questions

1. Should option 2 (keep the copy between drives) follow for race restarts (27b), where each restart rebuilds the
   copy (0.5 s settle plus 0.26 s, and a 45–75 ms removal frame)? **Default:** not now; revisit after the races
   playtest.
2. The spec bound: the delta states 2 s (row 17's original bound) instead of the "about 3 s" the user accepted,
   since the expected time is ~0.85 s. **Default:** 2 s.
