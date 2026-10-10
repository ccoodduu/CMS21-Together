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

The user chose scope B (2026-10-10, QUESTIONS.md): D1 only. D2 below is kept as a measured option, not built.

**Goals:** another player's track car shows within 1.5 s of arriving (measured ~0.7 s) and within 1 s of the other
player's drive start when already there (measured ~0.25 s); the frames of the build stay as they are today; the copy
is the same as today.

**Non-Goals:** splitting the first copy's one-frame load (D2); keeping copies between drives (option 2), loading
before arrival (option 3), a lighter copy (option 4) (see D4); the removal frame (45–75 ms, not loading); the own
car's load (behind the loading screen).

## Decisions

### D1. Readiness signal: own car ready plus 0.5 s (built)

`LocalCarReady` keeps its conditions; `LocalCarSettleSeconds` becomes 0.5 s. The "ready since" time is tracked
every frame in `RemoteCars.Update` while the scene is a track scene (not only while a build waits), so a player who
is already driving when another player arrives does not wait again; leaving the scene resets it, as today.

Why 0.5 s: the arriving client's own scene-ready frame (63–79 ms) comes 0.06–0.08 s after arrival; a build started
with no wait lands its first frames on it (100–120 ms). 0.5 s clears it. The freeze the 3 s guarded against is fixed
by `StopPhysics`; eight builds with no wait at all (`105122`, `110810`) showed none.

### D2. Staged load: the game's own stages, one per frame (not built)

Measured option for the first copy's 92–120 ms frame, if it ever matters in play: for loaders `RemoteCars` creates,
record and skip `CreateEngine`, `SetEngine`, `CreateDriveshaft`, `SetDriveshaft`, `CreateExterior`, `SetExterior`,
`CreateParts`, `CreateBonusParts`, `SetLicensePlateNumber`, `SetParts`, `SetBlockedBy` and `SetUnmountWithCarParts`
inside `CarLoader._LoadCar_d__215.MoveNext`, hold `LoadCar` (its `MoveNext` prefix returns `true`) and replay one
group per frame, cut where the game's disabled `asyncLoading` path yields: [engine] ~23 ms, [driveshaft, exterior]
~5 ms, [parts] ~25 ms, [bonus parts … unmount-with] < 1 ms. The harness prototype (`remote-stage`, removed again in
`43082bc`) brought the worst frame to 39–56 ms with identical copies (`110209`, `110523`, `110957`). Cost: twelve
prefixes on `CarLoader` methods every car load uses and a held IL2CPP coroutine.

### D3. Measurement in the client (built)

`RemoteCar` gets `WaitSeconds` (start packet → build start) and `ShownAt` besides `BuildSeconds`, `LongestFrame`
(real clock, the build's frames only) and `ShownAfter`; the ready log line names the wait. The `remoteCars` dump shows
`waitSeconds`, `shownAt` and the scene's `readyAt` (real clock, set when `RemoteCars` first sees the track scene);
the harness `remote-build state <playerId>` returns them without a full dump.

### D4. Options not taken

| Option | Measured | Why not now |
|---|---|---|
| 2. Keep the copy between drives | Re-show 1.4–2.6 ms, hide 0.3–0.5 ms; saves the ~0.25 s build, the 0.5 s settle after a restart and the 45–75 ms removal frame | Not needed for the target; worth it if race restarts (27b) feel slow. Memory per copy not measured reliably (the process delta is noise) |
| 3. Load before you arrive | Start packet arrives 0.06–0.07 s after the scene is ready | Already the case; nothing to gain |
| 4. Lighter copy | Base model (no car data) 0.14 vs 0.20 s, worst frame 23–28 vs 37–40 ms on repeat builds; `CreateInterior` 0.4 ms | The first-copy cost is chassis, engine and parts asset loads, not hidden parts; a lighter copy looks different |
| Asset pre-warm (`Resources.LoadAsync`) | `CreateParts` loads with `Resources.Load` | The paths come from game logic per car config |

### D5. Server and late join

Nothing new on the server: it stores the running drives and relays starts, states and stops as today, and already
sends the running drives to a player who arrives (`OnSceneChanged`). A player who joins or reconnects mid-drive
arrives through the same path, so D1 applies unchanged. No packet changes.

### D6. Interaction with other rows

- Ride-along (21) and race/speed track copies (27a/b) use the same build, so they get D1. A race restart (27b) that
  reloads the racer's own car resets "ready since", so the copy waits 0.5 s instead of 3 s.
- Collisions (27c): unchanged; the copy's colliders stay off (`StopPhysics` after every step).

## Proof

Scenario `remote-car-timing` (both clients `fps-cap 60`, two garage cars, `guard-allow Mode:CarDrive`, observers
polled with `remote-build state`, not `dump`), both arrival orders:

1. The arriving client shows the driver's car within 1.5 s of its track scene being ready (`readyAt` → `shownAt`).
2. The client already driving shows the arriving player's car within 1 s of the drive start (`shownAfter`).
3. No frame over 0.2 s while the copy loads (`longestFrame` and the harness `frame-log` up to 1 s after shown); a
   frame over the 0.13 s target is a WARN note in the result.
4. The arriving player's own car drives with the copy shown.

Old behaviour (settle 3 s, no per-frame tracking; `20261010-120734_L2`): fails 1 and 2 in both orders (3.2–3.4 s,
3.2–3.3 s). New code: `20261010-121219_L2` passed (arrival 0.67–0.69 s after the start, already driving 0.22–0.24 s,
frames 85–102 ms). `drive-latejoin` checks `shownAfter` ≤ 1.5 s.

The frame bound is 0.2 s, not 0.13 s: the first copy's one-frame `LoadCar` measured 85–130 ms with lane 1 busy, the
same as before the change (old run: 93–116 ms), and one run in three had a 130.4 ms frame (`20261010-121423_L2`), so a
0.13 s check would be flaky without D2. 0.2 s still catches a return of the old multi-second freeze.

## Risks / Trade-offs

- [The 0.13 s target is not guaranteed] → frames are unchanged from today (85–130 ms headless under load); D2 is the
  measured fix if a visible playtest shows a hitch.
- [The freeze returns] → the build still waits for the own car to be ready; `remote-car-timing` fails above 0.2 s.
- [Headless numbers] → no rendering or GPU upload is measured; the visible playtest reads `longest frame` from the
  `Observer car … ready` log line.

## Migration Plan

Client only. Rollback: revert; the 3 s settle comes back.

## Open Questions

1. Should option 2 (keep the copy between drives) follow for race restarts (27b)? **Default:** not now; revisit
   after the races playtest.
