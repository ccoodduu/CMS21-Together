# Spike: start grid on the race track

Roadmap row 27b (`track-races`): every racer now starts on the one spot the game uses. The user remembered a start grid
painted on the race track and asked us to look around. Date: 2026-10-10. Branch `spike/race-grid`.

**Result:** the race track (`Race_track_1`) has a painted grid of **20 boxes: 10 rows of 2**, behind the chequered
start/finish line. The game's own start spot is the left box of the front row (pole). No game code knows the other 19
boxes; they are only paint. If we move the scene's spawn transform onto box N before the game's restart, the restart
puts the car on box N. We checked this for boxes 1, 3 and 19.

## Screenshots

These are rendered with a harness camera into a texture, in a graphics-enabled test game (lane 1). Full-size PNGs are
in `C:\Users\ccoodduu\Desktop\CMS21-racetrack-start`. The JPG copies are in `docs/spikes/race-grid/`:

| File | View |
|------|------|
| `top-start-15m.jpg` | straight down, 30 m × 30 m around the start spot; the car is on pole, with the chequered line ahead |
| `top-grid-all.jpg` | straight down, 58 m × 140 m: the line, all 10 rows, and the curve after the grid |
| `chase-behind.jpg` | 14 m behind the start spot, 5 m up, under the start light gantry |
| `oblique-left.jpg` | from above the stand on the left |
| `driver-back.jpg`, `driver-forward.jpg` | driver height (1.2 m) on the start spot, looking back over the grid and ahead |
| `driver-row2-right.jpg` | driver height, row 2 right box |
| `spot-3.jpg`, `spot-19.jpg` | straight down on box 3 (row 2 right) and box 19 (row 10 right), with the car that the game's restart put there |

## Where the game starts a car

- `PrepareCarPhysics.StartPosition` is **null** on the race track. The restart's `LoadCar` uses
  `PrepareCarPhysics.carSpawnPosition`. That is the scene transform `!!Logic/CarSpawnPosition` at world
  (84.27, 0.12, 584.80), rotation (0, 180, 0), facing −z. Its right is −x. After the game's restart the car sits
  0.04 m ahead of it and 0.18 m above it. The note "one `StartPosition`" in `track-races/design.md` D2 meant this spot.
- No managed write to `StartPosition` (field 0x110) exists in the track code (byte scan of `GameAssembly.dll`). It is
  set only on scenes that serialize it.
- `RaceTrackManager`, `RaceTrackBase`, `TrackManager` and `PrepareCarPhysics` have no grid, slot or spawn array (see
  `dump.cs`). The only arrays of start spots in the game are the drag strip DLC's (`OpponentCarPhysics.StartPosition`)
  and the junkyard's `CarSpawnPositions`.
- The scene has no object per box. The scan covered 53,413 transforms by name, mesh, material and decal projector,
  plus every renderer within 40 m. The boxes, the chequered line and the lane lines are part of one combined mesh,
  `!RaceMap/Lines` (material `Ground_decals`). That mesh covers the whole track (416 × 1330 m) and is not readable, so
  its triangles cannot be listed. Other things near the start: the start light gantry `!RaceMap/Start_Lights` at
  forward +14.6 m, the finish trigger `Meta` (the last checkpoint) at forward +4.4 m, and the pit wall
  `Race_Track_Wall_Start` at right −5.8 m.

## Grid geometry

These were measured from orthographic top views at 53.3 px/m (rows 1–2) and 17.1 px/m (all rows), to ±0.05 m.
Coordinates are relative to `CarSpawnPosition`: **right** is the car's right (world −x), **forward** is the driving
direction (world −z).

- Box: a white U, open at the back, 2.57 m wide outside (2.33 m inside) and 4.26 m long. Lines are 0.12–0.19 m.
- Columns: box centres at right **0.00** (left column, holds the spawn) and **+6.15** (right column).
- Rows: the front edges are at forward **+2.87 − 10.0·row**, with row = 0…9. The last front edge is at −87.13 m.
  Rows are exactly 10.0 m apart.
- The spawn sits 0.74 m behind the centre of its box. The car's nose (Bolt Atlanta, 5.08 m) is 0.36 m behind the
  box's front line, so a car placed at the same offset in any box sits the way the game places it on pole.
- The chequered line is at forward +4.0 to +5.4 m. The track between the white edge lines runs from right −3.7 to
  +9.3 m (13 m). The pit wall is at −5.8 m.
- The ground rises toward the back: +0.04 m in rows 0–1 and +0.25 m at row 9, compared with the spawn's ground.

Spot n (0-based; n = 0 is the game's spot): column = n mod 2, row = ⌊n / 2⌋.

| n | right, forward (m) | world x, z |
|---|--------------------|------------|
| 0, 1 | (0, 0), (6.15, 0) | (84.27, 584.80), (78.12, 584.80) |
| 2, 3 | (0, −10), (6.15, −10) | z 594.80 |
| 4, 5 | (0, −20), (6.15, −20) | z 604.80 |
| … | forward −10·row | z 584.80 + 10·row |
| 18, 19 | (0, −90), (6.15, −90) | (84.27, 674.80), (78.12, 674.80) |

The world y is the spawn's y (0.12) plus the ground difference below the box (a downward raycast that ignores
rigidbodies).

Distances between boxes (centre to centre): 6.15 m within a row, 10.0 m along a column, and 11.7 m diagonally to the
neighbouring row. The gaps between cars: about 4.3 m side by side (car width 1.87 m) and about 4.9 m between rows.

## Placement test

Scenario `race-grid -TrySpots 1,3,19` (run `20261010-123709_L1_race-grid`, graphics, lane 1). Harness
`grid-spawn-move <right> <forward>` moves `CarSpawnPosition` onto the box and onto the ground. Then
`TrackManager.RunRestart` runs (the game's own restart, as 27b's D2 does), and the spawn is moved back.

| Box | Car after the restart (right, forward, up) |
|-----|---------------------------------------------|
| 1 | 6.15, 0.05, 0.22 |
| 3 | 6.14, −9.97, 0.23 |
| 19 | 6.09, −89.87, 0.43 |

The car lands within 0.13 m of the box, facing the track, and the game's lights and throttle wait run as usual.

## Proposal for row 27b: racer N on box N

1. **Server (D1).** `RaceCountdown` carries the grid: the participants' player ids in box order (`Grid[]`, index =
   box). Default order: arrival on the race track, with the requester first. Alternatives: random, or the last
   result's order (open question for the user). Up to 20 racers get their own box. Racers from 21 on share boxes from
   the back of the grid, and 27c's `race-start` rule stays in force for them.
2. **Client (D2).** When `RaceCountdown` arrives, the racer looks up its box n, moves `carSpawnPosition` to
   `spawn + right·6.15·(n mod 2) − forward·10·⌊n/2⌋`, adds the ground difference, and calls `RunRestart` as D2
   already does. When `_Prepare_d__19` reaches the throttle wait (state 3/4, which D2 already patches), the client
   puts the spawn back. The spawn also goes back on `RaceQuit`, on a scene change and at race end. The game's own
   placement, physics reload, fade and lights stay unchanged, so this is about 20 lines and no new teleport code. The
   pause-menu restart during a race is a quit (D3) and, with the spawn already back, returns the player to pole.
3. **Self-check / harness.** Add a `race-start` check for two racers: A on box 0 and B on box 1, each within 0.5 m of
   their box after the countdown. Run it headless; the headless restart already worked in spike 1.1.

### Interaction with 27c collisions

Rule 4 of 27c (`race-start`) switches the collider off during the countdown, and after it until the copy has once been
more than 10 m from the local car. That rule exists because everyone started on one spot. On the grid, two cars in the
same row are only 6.15 m apart. They can drive side by side down the straight for a long time without ever passing
10 m, so collisions would stay off between exactly the cars most likely to touch. Proposal:

- When the racers have their own boxes, `race-start` ends at the green light. It no longer waits for the 10 m
  separation.
- Rule 5 (`snap`) still covers the restart teleport, both of the local car and of each copy, in the first second.
  Rule 6 (`overlap`) still keeps the box off while a late copy would be inside the local car.
- The 10 m rule stays only for racers who share a box (more than 20 racers), or as the fallback if a racer's box
  lookup fails.
- Keep collisions off during the countdown itself. The cars are frozen until green, and copies arrive at their boxes
  at slightly different times.

## Harness changes on this branch

- `tools/TestHarness/Features/RaceGridCommands.cs`: `grid-start`, `grid-find [regex|path=..] [near=..]` (a full scan
  blocks the game for about 10 s, long enough for the server to drop the connection, so the scenario runs it last and
  only with `-Find`), `grid-shot` / `grid-shot-state` (a harness camera rendered into a RenderTexture and saved as
  PNG, with orthographic or perspective view, relative to the start spot), `grid-mesh` (triangles of a readable mesh
  near the start) and `grid-spawn-move`.
- Graphics test games kept in the background, with `CMS21_TEST_BACKGROUND=1`. `Start-HarnessInstance` launches
  through `cmd start /min` and passes `--harness.background --harness.returnfocus=<hwnd of the foreground window at
  launch>`. `Features/BackgroundWindow.cs` minimizes the game window every 0.25 s, gives the foreground back to that
  window, keeps the cursor unlocked and skips the window resize. Measured with a foreground watcher: Unity still
  activates its window at start-up, and the game held the foreground for about 10 s until the harness mod loaded,
  then gave it back (`handedBack 1`). The shots render normally while the window is minimized. The headless instance
  also took the foreground for a moment at launch (seen for B in every run). That is
  existing behaviour and is not addressed here.
- Scenario `tools/test-env/scenarios/race-grid.ps1` (`# needs: graphics`, `run-all: skip`), with `-ShotDir`,
  `-ExtraShots "label=w h start right up forward pitch yaw [fov|ortho=..]"`, `-TrySpots`, `-SpotsOnly` and `-Find`.
  Runs: `20261010-121541` (object scans), `20261010-122937` and `20261010-123155` (shots), `20261010-123709`
  (placement).
