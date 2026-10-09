# Status

Newest first. One entry per work session.

## 2026-10-09/10 (23:00–01:40) — playtest 3 (dev.1169/1170), findings

- New session start values (`e159fff`): `new_session_money` 4000 and `new_session_level` 1 by default (the game's
  `ProfileData.InitGlobalData`); test lanes keep 12500 / 8. Playtest builds `0.6.0-dev.1169`, then `dev.1170` from
  `fix/mount-replay-target`. Save, logs and bug reports: `Desktop\CMS21-Together-playtest-20261009`.
- **Mount into the wrong slot (fixed on `fix/mount-replay-target`, proof pending):** after the lock grant the item
  gate replayed `GameScript.SelectPartToMount`, which mounts into the part under the mouse at that moment; when the
  mouse moved during the round trip the item replaced a mounted part's identity (pads into the brake disc slot,
  rollers into the engine block and head as `TunedID`). The save was repaired by hand (`13.2`, `13.6` of
  `car_griffintyro` from backup `bak5`).
- **Remote-mounted part shows the old part's wear until F7 (fixed, proof pending):** shader values are now updated
  when `ShowMounted` shows the part.
- **No buy popup or sound in the shop (fixed, proof pending):** the buy hook now shows `PopUp_NewItem` and plays `Popup`.
- **Open:** tires missing and rims hollow on both players although cars, details and server agree (wheel shape is not
  rebuilt after our wheel apply; mounting again and F7 do not help); a wheel ghost stuck in the air at the friend's
  name tag (only on the receiving player); magenta wheels (only on the receiving player; car sold before a test);
  no remote oil drain animation; the bonus parts (row 25 part 2) and `fix/place-same` branches are paused.

## 2026-10-09 (20:30–23:00) — fix/place-same: locks-select-2 checked; soak not run (paused for a playtest)

- `fix/place-same` (lane 2) has `main` merged in (`5d79fe8`, main at `c4bd1c3`). Commits: `348159e` (move lock covers the
  target place; a refused move answers with every car's place, then lift states), `a76e45f` (a car snapshot waits for
  `LoadCarFromFile` to end), `5a8fb16` (local moves tracked per coroutine; lift states kept with the places),
  `5153af3` (`locks-select-2` prints the pie after B's release).
- `locks-select-2` "after B's release the move options are as before (8 of 10)" is pre-existing, not the branch: the
  game's own pie input (`PieMenuController.HandleInput` -> `NotificationCenter.ButtonAccept`) accepts `move_carLift1`
  while the harness has the pie open, without any mouse button or Return the game sees, and A's car moves to
  CarLifter1. Alone runs on `c4bd1c3`: fails `20261009-220034_L2`, `20261009-220954_L2` (passed on rerun), spurious
  accepts also in the passing `20261009-220207_L2`, `20261009-221126_L2`. On the branch: fails `20261009-220342_L2`,
  passes `20261009-220513_L2`, `220819_L2`, `221300_L2`. The base runs are copied to
  `CMS21-Together-wt\place-same\tools\runs\base-c4bd1c3`; the `place-same-base` worktree is removed. A harness fix
  (keep the game's pie input from accepting while `lock-pie` holds it open) is not done.
- Not done: the 15-min soak `Run-Soak.ps1 -Lane 2 -ContentionKinds place-same -Hours 0.25 -Deploy` (it waited for the
  user to be idle and was stopped for the playtest). Resume with that command; then merge.
## 2026-10-09 (22:00–23:00) — row 25 part 1, tuning at the dyno (`feat/tuning-window`, lane 1)

- `GearboxTab.ApplyAction` marks the car's tuning dirty; one tuner per car through the new lock kind `Tune` (keys
  `tune`, the gearbox and every part with a `PartModule`), taken in a `TuneWindow.Show` prefix and released on `Hide`
  or after 5 min without an apply ("<name> is tuning this car.", from the new `CarLockResultPacket.HolderKind`);
  `ItemConverter` carries `TuningData`/`GearboxData`; a part change that mounts a mechanical part marks tuning dirty
  (the game's `DoMount` copies an item's tuning without `PartModule.Tune`). Guard: `Window Tune` allowed.
- Spike 1.2 (`20261009-220832_L1_tune-probe`, `20261009-221010_L1_tune-probe`): Bolt Atlanta racing parts
  `t_v8_gearbox_stary`, `t_v8_gaznik_1` (carburettor, no ECU); the gate re-invokes `WindowManager.Show`; D6 holds.
- Proof `car-tuning` fails on the old code (`20261009-222538_L1`, B's `t:gearbox` unchanged after 2.2 s) and passes
  (`20261009-222319_L1`). Smoke plus touched scenarios pass (`20261009-222727_regression.json`; `guard` moved its
  blocked window example from `Tune` to `RevertBackup`, `20261009-224552_L1`); `--check-locks`, `--check-merges` pass.

## 2026-10-09 (21:00–22:00) — row 28 shared garage look built (`feat/shared-garage-look`, lane 1)

- The server keeps the garage look (`ModGarageLook` in the `garage` state, section v2 with migration) and broadcasts
  every change; one player customises at a time (claim at the `#garageLook` click, "<name> is customising the
  garage."); every client applies the look per section with the game's own `UpdateMaterials` coroutine; texture
  packs by id (not installed: default textures and one notice); the `garage` digest uses the last applied look.
  Guard: `Window GarageCustomization` allowed. Server command `look`, self-check `--check-garage-look`.
- Spike 1.1 (`20261009-212145_L1_garage-look-probe`): gate confirmed in `ClickIO` (the coroutine fades in its first
  step), apply and restore confirmed; all 41 sections take 28.6 s (330-renderer decal section), so the `garage`
  snapshot no longer waits for the apply (D4 changed; digest "not ready" while it runs).
- Proof `garage-look` fails on the old code (`20261009-212616_L1`, worktree of `main` with only the harness and the
  scenario) and passes (`20261009-213011_L1`). Smoke plus `garage-look`, `desync-autofix`, `resync-key` and
  `server-saves` pass (`20261009-213313_regression.json`); `--check-garage-look`, `--check-merges`, `--check-digest`
  pass.
## 2026-10-09 (20:30–21:30) — rows 29, 26 and 30 merged; playtest build dev.1156

- `main` = `f4be6e4`. Seated avatars (row 29) and the car salon (row 26) merged through `integrate/seated-salon`
  (`20261009-203236_regression.json`: smoke, `seat-avatars`, `salon-buy`, `seat-engine`, `ride-along`, `purchases`;
  `seat-avatars` FLAKY once in the batch: B's car was rebuilt late after the move and A left the seat 3 s after the
  engine started; passed alone and in the next batch). Job achievements for every contributor (row 30) merged
  (`f4be6e4`; proof `job-stats` fails `20261009-010925_L1`, passes `20261009-203134_L1`;
  `20261009-205357_regression.json`: smoke, jobs area, `job-stats`, `seat-avatars`, `salon-buy`).
- Playtest build `0.6.0-dev.1156` on the Desktop with checklist items for the menu fix, seated players, the salon,
  achievements and the order clock.
- In progress: `fix/place-same` (lane 2: the `locks-select-2` batch failure, then a 15-min contention soak); row 28
  garage look (lane 1).

## 2026-10-09 (01:50) — paused for the night

- Paused at the user's request; agents finish their current run, push and stop.
- Not merged yet: `feat/seated-avatars` (row 29, built; proof `seat-avatars` fails on the old client
  `20261009-000552_L1` and passes `20261009-002305_L1`; smoke set still to confirm), `feat/shared-salon` (row 26, in
  progress), `fix/place-same` (lane 2, in progress). Row 30 not started.
- Lane 1 branches at the pause, all pushed, none merged:
  - `feat/seated-avatars` (row 29) done: proof `seat-avatars` fails `20261009-000552_L1`, passes `20261009-002305_L1`;
    smoke and touched areas pass. Hand check 3.2 (right-hand-drive car) open.
  - `feat/shared-salon` (row 26) done: proof `salon-buy` fails `20261009-005105_L1`, passes `20261009-011548_L1`;
    `20261009-011825_regression.json` passes. README "Planned" paragraph conflicts with seated-avatars.
  - `feat/shared-job-achievements` (row 30) built: old code fails `20261009-010925_L1_job-stats`; the new run
    `20261009-013204_L1` had 2 scenario-side failures fixed in `951a803`, not re-run. Resume with
    `Run-Session.ps1 -Lane 1 -Deploy -Scenario job-stats` in `CMS21-Together-wt\jobstats`. New question in QUESTIONS.md
    (upgrade achievements, default buyer only).
- `fix/place-same` (`5a8fb16`, paused): place-same fixed (move lock covers the target place, refused moves answer
  with every car's place then lift states; proof `car-place-same` fails on `84beea6` `20261008-231901_L2` and passes
  `20261008-232642_L2`); examined flags after an unpark fixed (snapshot waits for `LoadCarFromFile`; proof
  `park-return` fails `20261008-233419_L2`, passes `20261008-233709_L2`); smoke plus 20 touched scenarios pass
  (`20261009-011337_regression.json`). Open before merge: `locks-select-2` fails in batch on the branch (passes alone;
  base batch `20261009-014747` passes): check which 2 pie move options differ after B's release
  (`LockPie.CarBlocked`/`CarLockMirror.Conflict` with the new place check), then a 15-min soak
  `-ContentionKinds place-same`; remove the `place-same-base` worktree afterwards.
- To resume: read the agents' last reports (branch commit messages), run smoke on each branch with main merged in,
  merge; then row 30, the next soak with contention (row 19 task 10.5 still open), and rows 28, 25, 27a in that order.
## 2026-10-09 (00:45–02:00) — row 26 shared salon built (`feat/shared-salon`, lane 1)

- Harness `salon-buy` drives the car salon as a player does (configurator, version window, rims, summary tab,
  location window); `travel Salon` loads `Auto_salon`. The purchase path needed no change: `GameScript.BuyCar` gets
  the configurator's car, and after an unpark both players have the chosen version and rim.
- Guard: `Window CarVersion` allowed ("Car version (car salon)", it never passes `WindowManager.Show`, checked with the
  guard's decisions); `Scene`/`Window Showroom` are "The showroom (main menu only)".
- Proof `salon-buy`: fails on the old guard rules (`20261009-005105_L1_salon-buy`, guard step; the purchase steps of
  that run failed on a harness bug fixed since) and passes (`20261009-011548_L1_salon-buy`): configured purchase paid
  once and parked once for both, server `NoMoney` refusal answered, local refusal sends nothing.
- Smoke plus `salon-buy` (with the guard-decision check, `20261009-015035_L1_salon-buy`), `purchases`, `locks-select`,
  `locks-select-2` and `server-saves`: all passed (`20261009-011825_regression.json`).
uns\*_regression.json` on the branch's worktree.

## 2026-10-08/09 (23:15–00:45) — order clock, menu click fix, rows 25–31 drafted and reviewed

- `main` = `310f9c3`. Server-owned order clock merged (`6b5dccd`): the server runs the order timer only while a
  generator is elected, freezes order expiry with it, owns the open-order limit (small `GetMaxOrdersAmount` table)
  and asks the generator for an order with `OrderRequest`; the client's own timer no longer makes orders. Proof
  `jobs-clock` fails on the old code (`20261008-232825_L1_jobs-clock`) and passes (`20261008-232021_L1_jobs-clock`);
  `20261008-233426_regression.json` (smoke, jobs scenarios, every scenario using `orders-autogen`).
- Menu click-through fixed (`60ef3ce`): the game's EventSystem is off while the pointer is over a mod panel (playtest:
  Host opened the CMS 2026 news link). Proof `menu-click-through` fails on the old code (`20261009-002947`,
  `20261009-003214`) and passes (`20261009-003510`); the headless games have no game button under the panels, so the
  scenario checks the mechanism; the real click is a hand check.
- Test tools: parallel Run-All calls take turns on the shared server-saves test (`310f9c3`, named mutex).
- Rows 25–31 drafted, reviewed by two agents and revised (`92895cb`): 25 tuning/bonus parts/new engines, 26 salon (cut
  to a purchase proof), 27a race and speed track, 27b races (race track, start in the F9 panel), 27c collisions (spike
  first), 28 garage look, 29 seated avatars, 30 job achievements for every contributor, 31 faster remote cars (≈3 s
  without frame spikes). User decisions in QUESTIONS.md.
- In progress: rows 29, 26, 30 (lane 1); `place-same` and examined-flag drift (lane 2).
## 2026-10-09 (00:00–00:45) — row 29 seated avatars built (`feat/seated-avatars`, lane 1)

- A seated player is shown crouched in the right seat of the car in the garage (`SeatPoses`, `SeatedAvatars` in
  `OnLateUpdate`), follows the lift, stands up where the player stands (one more forced movement packet when the
  seated mode ends), and is posed after a late join once the car is Ready; seated records no longer move the avatar;
  name tags follow the posed head; ride-along uses the same helper.
- Spike 1.1: the lift moves the seat handle before `OnLateUpdate` (1.678 m before, 0 m after over a full travel;
  offset 0.000 m every frame); a player can sit in a car on a raised lift; `sit left` is the left handle on the Bolt
  Atlanta. No RHD car can be spawned (all RHD models are DLC); left the RHD case to the hand check.
- Proof `seat-avatars` fails on the old client (`20261009-000552_L1_seat-avatars`, 10 failures) and passes
  (`20261009-002305_L1_seat-avatars`). Smoke plus `seat-engine` (now expects the seated avatar shown), `ride-along`,
  `presence-latejoin`, `scenes`, `drive-latejoin`, `ping`: all pass (`guard` and `presence-latejoin` failed once in
  the batch and passed alone, both known flakes); `server-saves` passed on a rerun (the parallel run timed out
  starting its server).

## 2026-10-08 (22:00–23:15) — row 19 part 2 and seeded job cars merged; playtest build dev.1102

- `main` = `63c146e`. Row 19 part 2 merged (`23c46ef`; smoke plus touched scenarios `20261008-224024_regression.json`).
  Seeded job cars merged (`63c146e`, option (a): extra tasks, drained fluids and plate are not seeded;
  `20261008-225803_regression.json`).
- User decisions: row 16 reduced to seeded job cars plus the server-owned order clock and limit (small limit table
  allowed, no large decompiled logic). Order clock design reviewed ("ready after fixes",
  `docs/design/server-order-clock-review.md` on `feat/server-order-clock`); being built on lane 1.
- Features to open in multiplayer (user): race track (base game only), tuning window, bonus parts, new engines,
  showroom, garage customization, seated avatars in the garage, achievements for everyone who worked on a job.
  OpenSpec drafts in progress on `change/singleplayer-features`.
- Open from row 19 part 2: two cars moved to one free place (`place-same`) leaves a swap on one client; examined
  flags after an unpark; task 10.5 (row 18 contention kinds).
- Playtest build `0.6.0-dev.1102` on the Desktop.

## 2026-10-08 (21:00–22:00) — story missions fixed; refused delete restore fixed; playtest build dev.1086

- `main` = `04760bd`. Story missions merged (`f37602b`): only the order generator makes a mission, the first mission
  is no longer lost at start, missions sit outside the open-order limit, a mission decline is refused (`Mission`) with
  the jobs state, and the server counts a finished mission itself. Proof `jobs-missions` fails on the old code
  (`20261008-210859_L1_jobs-missions`) and passes (`20261008-211950_L1_jobs-missions`); `jobs` and four scenarios
  that take a new order now skip the mission.
- Regression from the snapshot-after-delete guard (`9fa5bf5`): after a refused delete or park the server sends the car
  back, but the client dropped its snapshot as deleted and the car never became Ready. Fixed (`04760bd`): a
  CarSpawnResponse clears the remembered delete. `locks-car` fails on `f37602b` and passes with the fix;
  `car-snapshot-after-delete`, `economy-trades` and the smoke set pass (`20261008-215000_regression.json`).
- Playtest build `0.6.0-dev.1086` on the Desktop; the checklist has a story mission section.
- Row 19 part 2 still in progress on lane 2. Row 16 waits for the scope answer in QUESTIONS.md.

## 2026-10-08 (20:30–21:00) — row 20 merged; row 16 reviewed; story mission bug found

- `main` = `fbebf7d`. Row 20 race-hardening merged (`6d732f4`); snapshot-after-delete guard merged (`f156303`); a job
  car refused because its place is taken now says "Another car is already in that place." (`fbebf7d`).
- Row 16 `server-game-logic` draft reviewed: "ready after fixes". The review found that the server refuses every
  story mission: the game never sets `MissionID`, so it stays 0 and `JobsService` treats it as the tutorial. No
  scenario covered it. Fix in progress on `fix/story-missions` (lane 1) with proof scenario `jobs-missions`.
- Scope question for row 16 in QUESTIONS.md (full port or smaller variant); the design waits for the answer.
- Row 19 part 2 (digests, stall warning, soak contention) still in progress on lane 2.

## 2026-10-08 (20:00–20:30) — row 20 race-hardening ready for merge

- `fix/race-hardening` (lane 1), design note `docs/design/race-hardening.md`. Audit rows I6, E5, C1, C5:
  - I6 (reachable: a player who joins again in a reused slot, or after a server restart): the inventory snapshot
    carries the highest stored UID of the player's range (inventory, warehouse, machines, items in groups); new UIDs
    continue after it. On the old code the new item got the warehouse item's UID and one of the two items was lost.
  - C1 (reachable through the plain spawn path: F6 developer spawn, harness): a spawn into an occupied loader is
    refused ("Another car is already in that place."), the stored car stays, the refused client gets its snapshot
    and keeps the winner's car.
  - E5 and C5 not reachable, not built: nothing resends an economy request (reliable transport, no retry, only the
    harness `econ-ledger resend`); the job taker's second baseline comes 86–280 ms after the first (10 job runs),
    before another player's car is Ready.
- Proof: scenario `race-hardening` fails on the old code (`20261008-201149_L1_race-hardening`, 8 failures) and passes
  (`20261008-201423_L1_race-hardening`); `Run-All -Lanes 1 -Smoke -Scenarios race-hardening,car-placement-race,car-dlc`
  → `20261008-201608_regression.json`, 9/9 passed.

## 2026-10-08 (17:30–20:00) — row 24 and row 19 part 1 merged; playtest build dev.1067

- `main` = `9ae63c7`. Row 24 `part-locks-2` merged (`62729ad`): mount-mode previews hide parts in another player's
  lock, the item chooser marks items being mounted, pie Move/Drive options are unavailable on a locked car; the guard's
  pie locks now apply on the first opening.
- Row 19 part 1 merged (`9ae63c7`): field-group masks and server merges for part records (gap 3), per-entry car
  details with echo handling (gap 6), the removed-by-other rule for machine puts, sales, scrap and warehouse (gap 9),
  server-side records for parked cars (gap 10); proofs `car-stale-record`, `details-concurrent`, `tools-item-race`,
  `park-stale` fail on the old code; 25 scenarios green after the merge.
- Soak report: rule 7 judged per server process (`ba1e757`; today's soak 1.08x, pass); soak inventory capped at 300.
- In progress: snapshot-after-delete guard (lane 1); row 19 part 2 (digests, stall warning, soak contention; lane 2).
- Playtest build `0.6.0-dev.1067` and checklist on the Desktop.
## 2026-10-08 (19:50–22:40) — row 19 part 2 (detection, soak contention) on lane 2

- `change/state-merges-2` (lane 2), groups 9 and 10.1-10.4 of `state-merges-and-contention`.
  - Digests for car details (per car), machines, warehouse, garage (skills, upgrades, barns) and jobs; "not ready"
    keeps a pending mismatch (expires after four asks); the forced round asks every car and key; a stall warning
    (`desync_stall_seconds`, log and bug report only); `desync_resend_keys` (log-only first; `desync-soak` was quiet,
    so all keys resend by default). Proof `desync-autofix` (`20261008-201238_L2`; the not-ready step fails without the
    change, `20261008-201632_L2`), `desync-soak` `20261008-202110_L2`.
  - Soak: `carDetails` in the checkpoint, every key in its forced round, a key not ready at two checkpoints in a row
    fails rule 2; `-Contention` (`SoakContention.ps1`, 14 first-pass kinds, rules 8 conservation and 9 outcome, the
    known list, replay with the server order). Lane-2 runs: `20261008-215819_L2_soak` 9 of 9 groups passed, its replay
    `20261008-220620_L2_soak` reproduced the server order of 8 of 9; gap 9 reverted fails rule 8
    (`20261008-221624_L2_soak`). Smoke and touched scenarios `20261008-222205_regression.json` (12 of 12).
- Real drift the new digests and contention found:
  - Plate textures were never applied on receivers (job car with "Monaco" plates; `car-details:4` persistent). Fixed
    (`CarDetailsIO.ApplyPlates`), proof `car-details` gains a plate texture step (fails without, `20261008-210336_L2`).
  - `place-same` (L4): a move whose lock was granted is refused because the other player's move lock still holds the
    car at the target place; the mover's game has swapped both cars and keeps them, and the placement resend does not
    always undo it (persistent). Not fixed, listed as known gap, kept out of the random draw.
  - A car unparked while the other player was at the junkyard came up on that player's return with 7 parts examined
    that the server has unexamined; one `cars` resend repaired it. Reproducible with the replay above. Not followed up.
- Harness fixes: the dump's `cars[].index` is now the loader id (it was the `carOnScene` index, so the soak's part keys
  pointed at the wrong car whenever several cars were loaded: the "no part" verb errors of the 4-hour soak);
  `part-fast-mount` of a rim without its group leaves the parts behind it unblocked on the actor only, so the soak
  keeps wheel parts out of item-less mounts.
- Not done: task 10.5 (row 18's contention kinds), the lane-3 runs (10.1, 10.3, 11.1: the user's).

## 2026-10-08 (17:00–19:45) — row 19 part 1 (state merges) ready for merge

- `change/state-merges-1` (lane 1), groups 1–8 of `state-merges-and-contention`, merged with main (row 24 included).
  - Spikes (design D1, D9, D11): examine tools change only `IsExamined`; a real mount writes the item's fields with
    the mount state; machines only ever put items the putter removed itself; no car detail entry drifts on its own.
  - Gap 3: part records carry a `Changed` mask; the server merges per field group after a base check (mount, switch,
    effective id, quality: user decision) and drops stale records; receivers write only the written groups; a local
    transaction is aborted only for mount or identity changes; `ShowMounted` rechecks (P11); committed transactions
    per loader (P7). Proof `car-stale-record` (fails on main in 4 of 5 steps).
  - Gap 6: car details travel, merge and are remembered per entry; the own echo is decided per entry, so a pour is
    never set back. Proof `details-concurrent`.
  - Gap 9: a machine put of an item another player used is refused ("<name> used this part."); the server puts the
    putter's own item back on a refused put. Proof `tools-item-race` (incl. mount against sale and the warehouse).
  - Gap 10: a parked car keeps the server's part records and details; the park waits up to 1 s for the parker's own
    change. Proof `park-stale` (incl. restart and the unparker leaving).
  - Row 18 tie-ins: `car-stale-record` step 6, `locks-fluid` two fills at once, item lock against a machine put.
- Verification: `20261008-190450_regression.json` (25 scenarios plus server-saves, all green; `economy-trades` FLAKY).
  The batch failure: B unmounted a part locally without sending it (harness `part-unmount`), the digest resent the
  car snapshot just as A sold the car, and B rebuilt the deleted car from that snapshot (a snapshot applied after the
  car's delete). Pre-existing race, not fixed here.
- Seen once, not followed up: in `tools-item-race`, A's `placeNo` read -1 after the tire changer race while B's read 0.
- Next: part 2 (digests, soak contention) after the user's merge.

## 2026-10-08 (15:00–17:30) — row 19 part 3, ride-along, freeze fix; playtest build dev.1049

- `main` = `9ac5e12`. Row 19 part 3 merged (`2a066ca`): every refused/ignored/overridden server action answers the
  acting client (D16 table), seats are arbitrated (`SeatRefused`). Row 21 `ride-along` merged (`7e44859`): a seated
  passenger travels along on a test drive; observer cars no longer face backwards. The Drive pie option opens the
  map (`e3397a9`). The test path is single-player and refused while another player sits in the car (`e89c962`).
  A blocked dyno start no longer sends a claim (`9ac5e12`).
- Freeze fix (`fix: merge the observer-car freeze fix`): building another player's car copy froze the game 6–8 s
  (its live colliders hit the driver's wheel physics) and could time the player out; now 0.5 s, no frame over
  0.13 s; frame times use the real clock. drive-track and ride-along check it.
- Regression: 76 passed on the pinned worktree; bug-report was a test bug (scenario helpers now record "FAIL: …").
- Decisions: test path stays single-player; LvxBetterCarSpawns selector dropped (mod hidden for permission issues);
  our selector limits DLC cars to the shared DLC.
- Playtest build `0.6.0-dev.1049` with checklist on the Desktop.

## 2026-10-08 (15:10–16:10) — row 21 ride-along (test track) ready for merge

- `feat/ride-along` (lane 1): spike `docs/spikes/ride-along.md` (runs `20261008-151452`/`-151836_L1_ride-probe`),
  design note `docs/design/ride-along.md`. A player seated in a car when its driver's test-track claim is granted
  travels along, sits in the passenger seat of the observer copy (own track car frozen and hidden, game camera off,
  camera placed after the copy each frame, mouse look), cannot drive, and returns when the driver drives back, leaves
  or disconnects. Packet `RideUpdate`; server `Rides`; the riders' avatars sit in the seats on every client.
- The spike found row 17's observer cars shown turned 180° (positions right, so no check saw it); fixed in
  `RemoteCars`, and `drive-track` now checks the facing (fails with 180° without the fix: `20261008-153915_L1_drive-track`).
- Proof: `ride-along` (`20261008-153528_L1_ride-along`, 57 checks: camera at the passenger head ≤ 5 cm while driving
  50 m, drift 0 over 213 frames, same seat and facing on both games, no drive from the passenger, avatars in the
  seats, only the driver's mileage, early return, driver disconnect); `Run-All -Lanes 1 -Smoke -Scenarios
  ride-along,drive-track,test-drive,seat-engine` → `20261008-155713_regression.json` 10/10 (the first batch
  `20261008-154150` had ride-along FLAKY on a test counter left from drive-track, fixed in the scenario).
- Not done: the test path (options in the spike); everything a visible game must judge (head height, the crouched
  avatar in the seat, mouse look, the copy's interior up close, wheels spinning the right way after the 180° fix).

## 2026-10-08 (14:00–15:10) — row 19 part 3 (server answers, seats) ready for merge

- `change/server-answers` (lane 1): groups 12–13 of `state-merges-and-contention`. Every server path that refuses,
  ignores or overrides an action now answers the acting client (design D16: inventory update/add, refused repair,
  upgrades, second park and delete, move of an unknown loader, dropped orders, expired accept with "This order is no
  longer available.", second or refused job end with the jobs snapshot); seats are arbitrated with the new packet
  `SeatRefused` ("<name> is sitting there."). Harness `inv-send`, server `jobs expire <id>`.
- Proof: `server-answers` and `seat-engine` (seat race, both orders) fail on main's code (run
  `20261008-142844_L1_seat-engine` batch); with the change the areas run `20261008-143432_regression.json` (16/16:
  smoke set, `server-answers`, `seat-engine`, `economy-latejoin`, `economy-trades`, `car-parking-full`,
  `car-placement-race`, `jobs`, `jobs-latejoin`, `presence-latejoin`, `tools-slots`, server saves) and after merging
  main `20261008-145330_regression.json` (smoke + both, `seat-engine` FLAKY on a timing check, fixed and passed in
  `20261008-150317_L1_seat-engine`).
- Not done here: the dropped-transaction step of `server-answers` (needs task 3.5, part 1).

## 2026-10-08 (12:15–15:00) — soak fixes, locks, ping and shared shopping list merged; playtest build dev.1021

- `main` = `a0b844a`. Soak fixes merged (`a5dd030`, `docs/soak/2026-10-08.md`): receivers no longer add an extra
  `blockedNo` on remote mounts; a wheel-less car's lift state travels with the move request; special group 1 parts
  (drain plugs, caps) unmount remotely; a shared `PacketFramer` for TCP; inventory updates held during scene loads.
  The soak's memory "growth" is a series of server restarts loading a bigger save (measure per process; to do).
- Row 18 `part-locks` merged (`ba31e91`; 18/18 smoke + lock scenarios; `locks-scale` runs with the next soak).
  Row 22 `coop-ping` merged (`83753ed`; middle mouse; the part flashes in the game's highlight colour, `a0b844a`).
  Row 23 `shared-shopping-list` merged (`a395139`).
- Harness: each real-profile fingerprint uses its own registry temp file (`2309f82`; two lanes at once gave a false
  "REAL SAVE FOLDER OR REGISTRY CHANGED").
- Daily regression: the first run is invalid (main changed under it mid-run); it runs again from a pinned worktree
  (`CMS21-Together-wt/regression`). Rule: regressions run from a pinned worktree, never from the main checkout.
- Playtest build `0.6.0-dev.1021` and an updated checklist on the Desktop (`CMS21-Together-playtest`).
- In progress: row 19 part 3 (server answers every refusal, seats) on lane 1.

## 2026-10-08 (12:00–13:30) — row 18 part locks ready for merge

- `change/part-locks` (lane 1): groups 1–10 done. Part work, the item chooser (also a caliper with its piston built as
  a group), body panels, the crane, fluid refill, extractor and oil bin, lifts, moves and swaps ask the server first;
  hover shows the holder, a click on a part in use is refused at once, holds prefetch their lock. Park, delete and
  job end are refused while another player works on the car. Proven by `locks-basic`, `-race`, `-leak`,
  `-connected`, `-fluid`, `-car`, `-select`, `-latency` (10 of 10 holds without a wait at 80 ms), `-latejoin`, the
  area scenarios of 5.1 and the smoke set (`20261008-130716_regression.json`, 18 of 18). Row 19 review items P8, P10 and X4 are scenario steps. Not done here:
  `locks-scale` on lane 3 (with the next soak) and part of 11.4. Row 19 D16 notes the job-end refusal gap.
- **Playtest checklist (row 18)**, also in `docs/try-it.md` with what to report: hover a friend's part (no highlight,
  label with the name); hold to unmount at normal ping (no wait after the ring); click a part in use (sound, message,
  nothing starts); open the item chooser and leave with ESC (the slot is free for the friend at once); fill a fluid
  with a friend at the car (no reservoir removal or second fill meanwhile; same levels after); raise the lift while a
  friend works (refused); park the car or end its job while a friend works (refused, the car stays).

## 2026-10-08 (07:15–12:15) — row 15 merged, row 19 documents, 4-hour soak

- `main`: row 15 shared outdoor scenes merged (`99f1888`; `outdoor-scale` passes on lane 3 after merging main in).
  Row 19 `state-merges-and-contention` reviewed and merged as documents (`e04dde3`; questions in QUESTIONS.md).
  Lane 3 waits for 36 GB commit / 12 GB RAM instead of 44/16 (`8bd718f`; headless games). ROADMAP rows 21
  `ride-along`, 22 `coop-ping`, 23 `shared-shopping-list` (user wishes); no car collisions for now.
- 4-hour soak on lane 3 (`tools/runs/20261008-075718_L3_soak`, seed 1008075710): **FAIL**. No errors or crashes,
  bandwidth 3.1/1.0 kB/s per client, server CPU 2.8 %, late join 2.6 s. Failed: 9 of 24 checkpoints (mostly B, C, D
  differ from A on cars), confirmed desyncs (`lift:0.state 1 vs 0` server OnFloor vs all clients Middle;
  `s:N.unmounted`), 4 of 11 storms (K5, K2, K8 not settling within 90 s), server memory ×1.53, game memory 280 MB/h.
  An agent analyses and fixes them on lane 2 (`fix/soak-2026-10-08`, `docs/soak/2026-10-08.md`); the lift
  broadcast on `fix/lift-reset-broadcast` is not proven (its `lift-reset` scenario passes without it).
- Row 18 `part-locks`: switch-over done on its branch; the rest of 5.1's proof and groups 6.2–9.2 run on lane 1.

## 2026-10-07 (22:00–23:45) — audit, row 19 drafted, harness input guard; paused for the night

- `main` = `6dcd638`. Race and drift audit (`docs/audits/race-and-drift-audit.md`, 80 scenarios). Gap 5 fixed
  (`d1dd908`, `car-gone-inflight`). Playtest finding 7 fixed (`6ac54e6`: builds of one version and build kind join
  when the protocol matches). New playtest build `dev.935` on the Desktop. Headless test games read no keyboard and
  open no web/Discord/store pages (`3db16a4`).
- **Resume here (2026-10-08):**
  1. Row 18 `part-locks` (branch `change/part-locks`, worktree `CMS21-Together-wt/part-locks`): see "Resume here" at
     the top of its tasks.md; the switch-over (5.1) was in progress on lane 1.
  2. Row 19 `state-merges-and-contention` (branch `change/state-merges`, worktree `CMS21-Together-wt/state-merges`):
     revision after its review (`review.md`, Resolution section) plus the user's decisions in QUESTIONS.md
     (Answered, late evening): fix seats (S1); every refused/ignored action reaches the acting client.
  3. Row 15 `change/shared-outdoor-scenes`: only `outdoor-scale` on lane 3 left, then merge.
  4. 4-hour soak on lane 3 (`Run-Soak.ps1 -Hours 4 -Lane 3 -Headless C,D`, storms every 20 min) when the PC can run
     unattended; row 18's lane-1 runs pause meanwhile. Daily regression on main afterwards.

## 2026-10-07 (21:00–22:00) — playtest findings 3, 4 and 5 fixed

- Lane 2. Each fix has a scenario that fails without it; with the fixes, the three scenarios and the smoke set pass.
  - `c369763` tire desync: the game stores a tire's tuned id in two ways and TunePart also rewrites a rim's or
    tire's id, so a wheel with another rim or tire made later records fail to resolve. Digests compare the effective
    id; the car-details wheel apply keeps the rim and tire ids (`car-wheel-swap`; harness `wheel-parts`,
    `wheel-mount`).
  - `f9557c0` a rejected mount ("item ... is gone") gave the loser back the other player's item; now only items the
    server still has (`car-mount-race`; harness `part-twins`, `part-fast-mount ... [itemUid]`).
  - `c81fd05` world-state packets during a garage reload threw in the UI refresh (`resync-key`). Packet handler
    errors now log their stack trace.

## 2026-10-07 (20:30–22:00) — driving merged, part-locks drafted and reviewed, harness guards

- `main` = `d85243c`. Row 17 part 2 (remote driving on the test track) merged (`310cb3a`): `drive-track`,
  `drive-latejoin` and the smoke set pass on lane 1; the other player's car appears after about 10 s (QUESTIONS row
  17 #8); garage driving does not exist in the game (#7).
- Row 18 `part-locks` (strict server-granted locks on connected parts, fluids and the car; refusal at click time and
  on hover) drafted from the playtest, reviewed by a second agent (3 blockers, 7 majors, all resolved in the
  documents), merged as documents (`b9bd13a`); implementation started on lane 1 with the defaults of QUESTIONS
  "Open — row 18".
- Harness: test games never open URLs and headless ones get no UI navigation (`b6779dc`; Enter in another window
  opened the CMS 2026 page); Steam stats and achievements are blocked in test games (`0ee1ba0`; four were unlocked
  on the user's account). `visual-lift` (playtest finding 1) passes apart from a lift state name, fixed (`07faf20`).
- Row 15 (shared outdoor scenes): all two-player scenarios and the smoke set pass on lane 2; `outdoor-scale` on lane
  3 waits for free lanes, then merge.
- Playtest findings 3–5 (tire tunedId desync, rollback after a rejected mount, WorldState error on F7) are fixed by
  an agent on lane 2. The user's game is restored (mods back, Together moved to `Desktop\CMS21-Together-removed`;
  the save was unchanged).

## 2026-10-07 (19:00–21:15) — row 17 part 2 (driving) on `change/remote-driving`

- Spike 8.1: the pie option `car_drive` only opens the map; there is no driving inside the garage, so garage driving
  (group 11) is dropped and `Pie:car_drive`/`Mode:CarDrive` stay `Planned` (QUESTIONS.md row 17 #7).
- Test track: the driver streams its car (`CarDriveStart/State/Stop`, 36-byte state, 15 Hz moving, 3 Hz parked); the
  server relays it to players on the track and gives a newcomer the running drive. Observers build an inert copy of
  the car (no physics, no colliders) and move it 100 ms behind the driver with turning wheels and engine sound.
- Runs (lane 1, headless): `drive-probe` (spike), `drive-track` and `drive-latejoin` pass (`20261007-210258`,
  `20261007-210430`); smoke set passes (`20261007-205411_regression`, `guard` flaky once in the batch: "Inventory did
  not open after the blocked mode change", passed alone). Measured: `CarDriveState` 276 B (4.1 kB/s per moving
  driver), `CarDriveStart` 15.6 kB.
- Open: the other car shows about 10 s after arriving (spec 2 s; QUESTIONS.md row 17 #8); the `testdrive`,
  `placement`, `guard` areas were not run; not merged.

## 2026-10-07 (19:00–20:30) — first playtest over Steam (2, then 3 players)

- Host from the game on the user's PC, friends joined over Steam with `dev.893`/`dev.894`. Five F8 bundles
  (`b3fd`, `be9b`, `20ec`, `475f`, `5b80`). Server-only fixes were shipped during the session as builds labelled
  with the clients' version (`-p:TogetherBuildLabel=dev.89x`), so friends did not reinstall.
- Fixed during the session (on `main`):
  - `83f93c1` Steam clients without a Steam ID (relay identity 0) are identified by their player key
    (was `MissingIdentity`).
  - `65a2f95` Steam login waits 20 s (was 5 s; it takes 3.6–5 s and timed out twice) and logs failures.
  - `035887c`, `e26d219` mounting a part with sub-parts (caliper + piston, piston + rings) was always rejected:
    the game builds a new inventory group while mounting that never reaches the server, and the rejection lost the
    items removed on their own. The server now rejects only when another client removed the entry.
  - `1efb71c` a remote condition change on a part whose highlighter is not set up yet skipped
    `UpdateShaderParams`: a replaced disc or ABS module looked worn until F7 (needs a new client).
- Open findings: see QUESTIONS.md "Playtest findings (2026-10-07)".

## 2026-10-07 (18:00–19:00) — row 17 part 1 merged (remote work visuals)

- `main` = `85b90ea`: other players' part work is now visible: a ghost copy of a part slides off or on, the bolts
  turn with the actor's progress, and the avatar shows a work pose and the tool prop (OBD scanner). Everything is
  visual only (ghosts; the real state is never touched or delayed; a leak detector logs state changes inside a
  visual). New `PlayerActivity` packet (≤ 4/s, latest value kept for late joiners). `visual-parts`,
  `visual-activity`, `visual-latejoin` pass, and the `parts`, `presence`, `tools`, `visuals` areas pass (23
  scenarios). Spike: one bolt takes about 0.9 s; the claim release arrives before the commit in the same frame; part
  materials have no dissolve, so ghosts shrink out.
- Left for row 17 part 1: `visual-screens` (needs a visible window: a night run), the lane-3 budget scenario, the
  README section. Part 2 (driving) has started on lane 1; row 15 (shared outdoor scenes) is tested on lane 2.

## 2026-10-07 (17:00–18:00) — headless tests, release smoke, M7 designs, branch cleanup

- Test games run headless by default (`-Visible` or `CMS21_TEST_VISIBLE=1` to see them; `# needs: graphics` for
  scenarios that need a window); test servers start hidden. The game moved the Windows cursor through `ProMouse`
  (its setters are inlined, so the harness stops the move coroutine) and Unity re-centred it on cursor lock; both are
  off in test games, so the user can use the PC during runs.
- Daily regression on `main` headless: all game scenarios pass (`car-live`, `economy-trades` flaky in the batch);
  `server-saves`' window-close check now starts its server minimized.
- `release-smoke` passes with a real release zip (`0.6.0-dev.878`) installed in lane 1; `Build-Release -Release`
  dry run passes (refuses without changelog section or tag). M6 now waits only for the user (friend install test,
  publishing).
- M7: OpenSpec changes `remote-visual-feedback` (row 17) and `shared-outdoor-scenes` (row 15) are on `main`; agents
  write their code (no game runs yet). Row 15's car selection uses our own selector; the LvxBetterCarSpawns-based
  one waits for the user to ask LvxMagick.
- GitHub cleanup (user): 33 merged branches deleted (kept `main`, `Dev`, `MainMod`, `dev-0.4.x`); 20 local worktrees
  and 30 local branches removed.

## 2026-10-07 (16:30–17:00) — row 9 part 2: mod check tuned on the user's mod list

- `main` = `d2b5f8a`: the user's eight mods were loaded in test install A (copied from the real game folder, read
  only, removed again afterwards). Six verdicts were right; `LvxOwnedCarsOnly` (filters which cars spawn) and
  `AutosaveMod` (runs the game's whole save routine every 5 minutes; our guard blocks the write, but it re-IDs
  engine-stand items) were wrongly visual and are now refused through a `KnownMods` table (extendable in
  `mod_rules.json`; a host can still allow a mod in `mods_ignored`). The refusal lists one plain line per mod and
  says what to do. `compat-mods-probe` proves it. Task 7.1 (friends' mod lists) waits for the friends.
- Note for the user's own playtest: QoLmod's "load last save on startup" skips the multiplayer menu.
- Playtest build refreshed: `0.6.0-dev.864`.

## 2026-10-07 (15:45–16:30) — last soak findings fixed; plans for the long soak and the playtest

- `main` = `f136eb9`: a car deleted on another client kept its loader's ground position, so the next car spawned
  into that loader took a lift from the car standing there and the lift buttons stopped working for the other
  players. Remote deletes now clear it like the game's own park/sell/job deletes (`car-placement-reuse` proves it).
  The `GameMode::SetCurrentMode` and `PartScript.Hide` errors were harness-only (no car under the mouse); fixed in
  the harness (`harness-mouse-over`).
- Playtest build refreshed: `0.6.0-dev.859` in `Desktop\CMS21-Together-playtest`.
- Test policy (user): a fix runs its proving scenario plus the smoke set; the full regression runs once a day on
  `main`; a soak only answers a concrete question.
- Plans (user): the 4-hour soak runs on 2026-10-08 while the user is at school; the Steam playtest is probably after
  the autumn holiday; row 9 part 2 uses the user's own mod list for now (an agent tests it on lane 1).

## 2026-10-07 (14:00–15:45) — first soak with four players; two more sync bugs fixed

- Four graphical games use about 6 GB RAM each and leave under 1 GB free, so the soak's watchdog stopped the first
  run. With C and D headless (`-Headless C,D`) the 15-minute soak runs: 216 actions, about 1.3–2.7 kB/s download per
  client, 4–5 % server CPU, lock wait under 50 ms.
- Fixed on `main` (`91fbdd6`), each with a scenario that fails without it:
  - a remote mount also mounted the part's `unmountWith` members (e.g. the bushings of a control arm), which the game
    only does for a group item (`car-live`);
  - window tint was applied visually to non-window parts and threw on a null material when another player unparked
    a car (`car-placement`).
  - The memory slope warning was warm-up; the 50 ms handler was a bug report zip and the periodic save.
- Playtest build refreshed: `0.6.0-dev.852` in `Desktop\CMS21-Together-playtest`.
- Still open, an agent is on it: one checkpoint where only the rejoined client D shows lift 1 connected to a car;
  `GameMode::SetCurrentMode` errors when a player sits in a car; a `PartScript.Hide` error on a headless client.

## 2026-10-07 (11:10–14:00) — playtest build; row 11 runs with four players; two inventory bugs fixed

- Playtest build for the user (Steam join): `0.6.0-dev.846` zips with a Danish quick start
  (`docs/playtest-quickstart-da.md`, also in the client zip) in `Desktop\CMS21-Together-playtest`.
- `main` = `4529315` after a full batch regression on both lanes (46 passed, about 30 min) and lane 3:
  - row 11: lane 3 (A–D against `Server3`) works. `scale-connect` (joins 5.5–17 s), `storm` K1–K8 and
    `latejoin-full` (about 7.5 s to playable with a full garage and parking, snapshot 0.75 MB) pass.
  - fixed, found by lane 3: (1) the inventory digest counted changes still held in an open part transaction, so the
    server resent the inventory, the client wiped its own new item and then sent it to everyone else; (2) live
    inventory packets that arrived during a full inventory sync were wiped by the queued snapshot (could hit every
    late join). Live inventory packets are now held during a full sync and replayed by UID afterwards.
  - fixed: our `Inventory.Delete` prefix threw on `Delete(null)` when the game mounts with nothing selected. The
    `DeleteCar` error at game quit is vanilla (`CarLoader.OnDestroy` after `GameManager` is gone) and harmless; the
    plate-texture error was already fixed on 2026-10-06.
  - harness: `car-spawn` takes a place (`auto` = first free), like the game's spawn; scenario errors name the script
    line.
- Running: a 15-minute soak with four players. Waits for the user: the long 4-hour soak (QUESTIONS.md), the Steam
  playtest.

## 2026-10-07 — row 11 (soak and scale) groups 1, 4–6 in code, waiting for game runs

- Branch `change/soak-and-scale`. In code, not run (both lanes were busy with the regression): lane 3 (A–D,
  `Server3`, port 7797) with lane locks held for the whole run and deploys inside them, `Start-HarnessInstance`,
  `-ScenarioArgs`/`-Deploy`/`-Headless` in `Run-Session`, `Run-All -Lanes 3`; scenarios `scale-connect`, `soak`
  (seeded action table, `actions.jsonl` + `-Replay`, checkpoints, watchdog, `Run-Soak.ps1`), `storm` (K1–K8,
  `net-hold out`), `latejoin-full` with the fixture maker `full-garage-fixture`. `Test-ServerSaves.ps1` moved to port
  7807 (7797 is lane 3's).
- Once before the first lane-3 run: `Setup-TestInstalls.ps1` (creates `Server3` and its config next to A–D) and
  `Deploy-Mod.ps1 -Lane 3`.
- Allow-list `tools/test-env/scenarios/soak-allow.txt` holds three client errors seen in most runs today
  (`CarLoader::DeleteCar`, `Inventory::Delete`, `ChangeLicencePlateTexture` hooks); each needs its owner's look.

### What to try (M5 hand checks, row 11)

1. The M5 playtest: 3–4 friends, two hours or more over Steam, the host's server with
   `perf_log_interval_seconds = 10`; one player quits the game hard once, one rejoins, the host restarts the server
   once. Send `Log/perf_*.jsonl`, the server log and F8 bundles of anything odd; compare bandwidth, CPU and join
   times with the harness numbers (`Show-SoakReport.ps1` on a run folder).
2. The host's upload: the summed server upload of that session (server `perf`) against the host's real upload speed.
3. One join from a friend over the internet into a full garage and full parking (late-join time with real latency).

## 2026-10-07 (09:40–11:10) — M4 done in code; 14d, test areas, release docs and row 11 metrics merged

- New test policy (user): merge after the change's own scenarios, its test areas and the smoke set
  (`Run-All -Changed`); the full regression runs on `main` in the background and failures are fixed forward.
  Scenarios carry `# areas:`; the smoke set is `latejoin`, `car-live`, `junkyard-trip`, `guard`, `tools-latejoin`.
- `main` = `2796c8a`:
  - car purchases outside the garage (row 6 part 2 group 6): a junkyard car goes to the shared parking, money once
    through the server, NoMoney refused. Found on the way: the server's item UIDs came from `DateTime.UtcNow.Ticks`,
    so parts bought in one tick shared a UID (now strictly increasing). Barn, salon and auction purchases are hand
    checks. **M4 is done in code.**
  - row 14d (F8 bug report bundles), row 12 part 2 (install and hosting guides, README for 1.0, CHANGELOG,
    versioning, `Collect-Logs`), test areas, and row 11 groups 2–3 (server traffic and lock-wait metrics, `perf`
    command and log, client `perf`/`fps-cap`; `perf-probe` passes).
  - scenario fixes: `economy-trades` compared dumps while B was in the newly opened barn; `bug-report` had two
    PowerShell traps (`$rootS` is `$roots`; `@(... | ConvertFrom-Json)` counts an array as one).
- Running: the full regression on `main` on both lanes. An agent writes the rest of row 11 (lane 3 with four
  instances, soak, full-garage late join, disconnect storms).

## 2026-10-07 (07:55–) — guard opened, batch mode, two lanes again

- Two lanes run in parallel again (Claude Code's memory guard is off; the second lane needs 10 GB free RAM).
- `main` = `8ae53d5`: the guard opens the synced workshop machines, part paint, window tint and starting the engine
  (regression `20261007-075825` plus the 14 scenarios from last night: all passed, `economy-trades` flaky once on
  "no SkillsTab"). Tint fee is a hand check (the harness cannot drive the tinting window).
- Batch mode merged (`ed3af35`): `Run-Session -Scenarios` keeps the games running, restarts only the server per
  scenario, `harness-reset` clears harness toggles, a batch failure is re-run fresh. Three scenarios in one batch
  took about 4 minutes. `Run-All` batches by default (`-Fresh` for the old way).
- Row 7 part 2 (`change/session-persistence-part2`): `rejoin`, `duplicate-identity`, `latejoin` (scenario fixed: it
  compared A's state before the part had settled) and `persistence-restart` pass; its full batch regression runs on
  lane 2. Once, under four games' load, the server timed out both clients at the same moment ~20 s into B's join and
  their UDP packets arrived 13 s late; not explained yet (row 11's lock-wait metrics should show it).
- Row 7 is done: part 2 merged (`151c606`) after the batch regression `20261007-082204` (all passed;
  `car-placement-race` and `economy-trades` failed once inside the batch with "cars differ" and passed fresh; a probe
  showed that a client does not keep cars from an earlier session in the same game process, so the cause is still
  open) and a short batch with the guard change.
- Car purchases outside the garage (row 6 part 2 group 6, `change/car-purchases`): first game runs found two bugs,
  both fixed: patching `GameDataManager.SaveCar` (it takes the `NewCarData` struct by value) crashed the game, and
  opening `CarLocationWindow` in the guard also opened the garage's car moves. Driving a junkyard purchase from the
  harness still fails (`BuyCar` needs the car info window flow); being worked on, on lane 1.
- Row 11 OpenSpec change written (`change/soak-and-scale`), questions in QUESTIONS.md.

## 2026-10-06 (23:40–00:05) — handoff for the night (PC off)

- Stopped on purpose for the night; nothing is running. The lane-1 server save was restored from the interrupted
  run's backup and the recovery marker removed.
- `change/guard-open` (opens 23 guard entries for rows 4, 5a, 6 part 2; repair and wash-before-tint fees through the
  game's calls; tint fee is a hand check): `guard`, `economy-fees`, `economy-trades`, `economy-trace`, `tools-slots`
  pass. Its full regression was stopped after 14 scenarios (all passed, `car-baseline` … `desync-autofix`). Next:
  finish it (or, with batch mode, the rest) and merge.
- `change/bug-report` (14d) and `change/session-persistence-part2` (row 7 part 2) contain `main` with 5a/5b and
  build; they wait for their scenarios (`bug-report`; `rejoin`, `latejoin`, `persistence-restart`,
  `duplicate-identity`) and a regression each.
- Batch mode for the harness (user's wish: reuse the running games across scenarios, re-run a failure alone in a
  fresh session): worktree `CMS21-Together-wt/harness-batch` exists, no code yet. Plan: `Run-Session -Scenarios`,
  server restart per scenario, clients back to the menu plus a `harness-reset` verb, per-scenario log slices,
  `# run-all: fresh` for scenarios that test game start or profiles, `Run-All` batches per lane with a fresh re-run
  of failures.
- Test budget (user): full regression only when client or server code changed; otherwise the affected scenarios.
- Two lanes again: `CLAUDE_CODE_DISABLE_BG_SHELL_PRESSURE_REAP=1` is in `~/.claude/settings.json` and takes effect at
  the next Claude Code start. The second lane only starts with at least 10 GB free RAM.
- After that: M4's car purchases outside (junkyard, barn, auction into the shared parking; row 6 part 2 group 6),
  then M5 (rest of row 7, row 11).

## 2026-10-06 (22:30–23:40) — row 5b merged; guard audit; a late-join fix

- `main` = `614cee9`: row 5b (car tools: engine crane effects, car paint, car wash, interior detailing, oil bin,
  welder, dyno from the map) merged. Single-lane regression `20261006-221425`: all passed except `economy-fees`
  (failed) and `test-drive-latejoin` (flaky); both are fixed and re-run green, as is `tools-car-effects`.
- `economy-fees` ran the paint, wash, welder and interior fee steps for the first time (5b opened them in the guard).
  The harness verbs skipped the game's payment calls; now they use them (`tool-use ... paid` runs the accept lambda,
  `wash-paint` runs `ShowCoroutine(clearCar: true)`, `tool-paint-car` pays with `TryGetMoneyForPaint`). The client
  was right: every fee goes through the server once.
- Late-join bug: `PartScript.SetConditionNormal` throws after storing `Condition` when the part's highlighter is not
  set up yet. That aborted the car snapshot and B's join failed (seen with a car on the test track). Fixed in
  `PartApplier`.
- Guard audit: rows 4, 5a and 6 part 2 were merged with their guard entries still `Planned`, so real players were
  refused the workshop machines, part paint, window tint and starting the engine (the harness runs with the guard
  off). Branch `change/guard-open` opens 23 entries and is being tested next. Still blocked on purpose: tuning
  (gearbox changes are not sent), car version, bonus parts, building a new engine on the stand, driving, buying
  cars, barn, auction, salon, parking scene.

## 2026-10-06 (20:10–22:30) — row 5a merged; 5b, 14d and row 7 part 2 ready to test

- `main` = `6859362`: row 5a (workshop machines: tire changer, wheel balancer with its lock, spring clamp, brake
  lathe, battery charger, repair table, part paint, tool positions) merged after a single-lane regression
  `20261006-212522` (all 40 PASSED).
- Bug found on the way: the game's `ToolsMoveManager.MoveTo` does nothing when the place has no loaded car
  (`CarLoader.root` is null). The scenarios had deleted the car first, so the welder never moved on either client and
  the old check (A equals B) did not notice. Now a position is only sent for a place with a loaded car, the receiver
  retries while its car there still loads (late join), `tool-move` reports whether the tool moved, and the scenarios
  keep a car on `CarLifter1` (with the synced `car-move`; the harness `car-place` is local only).
- The engine stand (groups 11/12) stays a hand check: the game's build coroutine throws a native exception when the
  harness drives it, also offline. The scenarios note it and skip those steps.
- Row 5b (`change/sync-workshop-car-tools`) now contains the new 5a and `main`; the `SYNC_TEST_DRIVE` guard on the dyno
  map hook is gone. `tools-car-effects` is running. 14d (`change/bug-report`) and row 7 part 2
  (`change/session-persistence-part2`) already contain `main` and wait for their test runs (one lane at a time).

## 2026-10-06 (15:40–) — row 4 continued; row 13 spike tooling

- Row 4 (car details), new and built but not yet run in the game: window tint, per-part paint (colour, paint type,
  custom paint, livery), the car's custom paint, and ECU/carburettor tuning per part (`PartModule.Tune`). The paint
  shop commit (`SubmitColor`) and every `Tune` call mark the car as changed. `cardetails-randomize` now also tints,
  paints a part and tunes the modules, so `car-details` covers them.
- Row 4 task 4.7: new scenario `car-details-request`. A holds back its spawn snapshot (`cardetails-hold on`); the
  server must ask for it after 10 s and store the answer.
- Lane-1 regression `20261006-151624` of the row 4 branch: all 26 PASSED. After it, `car-details` (now with tint,
  paint and tuning) and `car-details-request` passed. `main` fast-forwarded to row 4 (`48ad607`).
- Row 13 (test drive and diagnostics), branch `change/sync-test-drive-and-diagnostics`: the runtime trace
  (`test-drive-trace`) answers spike 1.2; results are in the change's design.md "Runtime trace results". In short:
  the order the design assumed holds, the track car has the same part order, and `GlobalData.Load` keeps the three
  globals. Today the driven kilometres are lost, because the garage reloads the car from the server snapshot.
- Two bugs found by the trace and fixed on `main`: row 4's detail hooks and the presence update threw exceptions on
  the test track.
- Next: spike 1.3 (hold and replay the departure) and 1.4 (path test, dyno; their harness verbs are still to write).

## 2026-10-06 (15:55–16:35) — row 13 implemented; handoff before a reboot (pagefile)

- Row 13 spikes 1.1–1.4 done as far as the harness reaches (results in the change's design.md). D2 now holds the
  departure coroutine in `MoveNext` instead of replaying it.
- Row 13 groups 2–7 are in code on `change/sync-test-drive-and-diagnostics`: server `CarAwayRegistry`, enforcement,
  test drive fold, dyno details section; client `CarAwaySync`, locks, labels, `TestDriveSync`, `DynoSync`,
  `PathTestSync`; guard entries allowed. `test-drive` PASSED (claim, locks, +5 km on both, refusal, abort, fallback).
- Not run yet: `diagnostics`, `test-drive-latejoin`, and the full regression (one was stopped for the reboot).
- Pagefile raised to 32–48 GB (was 8–16 GB, needs the reboot). The game commits 8–10 GB per instance but uses
  2–4.5 GB, so the commit limit, not RAM, kept the lanes taking turns.
- Background agents (stopped by the reboot): row 5a code on `change/sync-workshop-machines`, row 10 OpenSpec draft on
  `change/economy-audit`. Check what they pushed; their worktrees stay in `.claude/worktrees/`.
- Next after the reboot, in order:
  1. Check the commit limit is about 64 GB (`Win32_OperatingSystem.TotalVirtualMemorySize`).
  2. `Run-Session.ps1`: hold the lane mutex only while the games start and load, and start a lane only when the
     commit headroom (limit − committed) is at least about 22 GB; then run lanes 1 and 2 together (`Run-All -Lanes 1,2`).
  3. Run `diagnostics` and `test-drive-latejoin`, fix, then the full regression; merge row 13 into `main` if green.
  4. Continue M4: review and test row 5a's branch, then 5b, 6 part 2, 10, 8 part 2.

## 2026-10-06 (16:30–) — after the reboot: two lanes in parallel, row 13 tested

- The commit limit is 63.9 GB now. `Run-Session.ps1` holds the lane mutex only while the games start and waits for
  22 GB commit headroom, so lanes 1 and 2 run at the same time: four games used 37 of 63.9 GB. `Run-All -Lanes 1,2`
  halves the regression time.
- After a reboot Steam must run (offline mode is fine); without it the games hang in the `init` scene.
- `diagnostics` (lane 1) and `test-drive-latejoin` (lane 2) passed on the first run, in parallel. Row 13 is complete in
  code; the net-hold race of 8.1, the away-label screenshot (4.3) and the hand checks of spikes 1.3/1.4 are open.
- A run that is killed before its restore left a test car in lane 1's server save, and every later run restored that
  dirty save (`car-baseline` failed twice). The clean save is back, and `Run-Session` now leaves a marker with its
  backup path, so the next run on that lane restores an interrupted run's backup first.
- `car-placement` failed once on lane 2 (B's car from parking never finished loading, so no unpark request) and
  passed on the rerun; `ParkingSync` now logs that case.
- Your answers are in QUESTIONS.md: travel fees follow a server rule (`travel_fees`, added to row 10's design), all
  other questions take the defaults (generator client as opt-in, row 10 option C, …). Nothing is open.
- Branches from the agents: `change/sync-workshop-machines` (row 5a, Core + server store, agent still working),
  `change/economy-audit` (row 10 OpenSpec, complete), `change/sync-players-and-scenes-part2` (row 6 part 2 seat and
  engine, in code, needs a game run).
- Running: full regression of the row 13 branch on both lanes.

## 2026-10-06 (17:10–18:15) — row 13 merged; M4 branches tested in the game

- Row 13 merged into `main` after regression `20261006-165221` (30 scenarios on two lanes, green; one flaky run was a
  stall of the server loop that timed out every client, fixed: heartbeat deadlines move by the stall).
- Real bug found by row 5a's scenario and fixed on `main`: every client handed out item UIDs from its own profile's
  counter, so two players (or one player after a rejoin) created items with the same UID. Each player now uses its
  own UID range (player id × 10^12).
- Agents wrote rows 5a, 5b, 6 part 2, 8 part 2 and 10; I merged `main` into each and tested them in the game:
  - row 8 part 2 (host from the game, password, admin key, kick, ping, toasts): `session-admin` and
    `host-from-game` passed on the first run;
  - row 6 part 2 (seat and engine): `seat-engine` passes; the remote engine sound is now built like the game's own
    (a decompile showed `EngineAudioController` is one global object, so a remote car gets its own sound prefab);
  - row 5a (workshop machines): 44 checks of `tools-slots` pass (tire changer, balancer with its lock, spring clamp,
    brake lathe, battery charger, rejoin); the engine stand step is fixed in the scenario, not re-run yet;
  - rows 5b and 10 are in code, not run yet.
- `integration/m4-seat-host` (rows 6 part 2 + 8 part 2 on `main`) builds; its full regression on both lanes was
  stopped by Claude Code because free RAM ran critically low with four games. See QUESTIONS.md: until you answer,
  I run one lane at a time and do not restart that regression myself.

## 2026-10-06 (18:15–20:10) — rows 6 part 2, 8 part 2 and 10 merged; four fixes on `main`

- `main` = `43fca9a`: rows 6 part 2 (seat and engine), 8 part 2 (host from the game, password, admin key, kick,
  ping, toasts) and 10 (economy: every money, scrap and XP path through the server, travel fees by the server rule
  `travel_fees`) merged after a single-lane regression `20261006-184911` (36 of 37; the one failure was the job car's
  place, fixed below and re-run: `economy-trades`, `jobs`, `jobs-latejoin`, `car-placement`, `car-live`, `car-race`,
  `car-baseline` pass).
- Fixes found by the new scenarios, all on `main`:
  - items synced through the inventory keep valid mount (bolt) data and groups keep their size (an engine could be
    built at scale 0 or throw on the engine stand);
  - a job car's place reaches the other players (the game places it after loading, and a move that arrives while
    the other player's copy still loads is now kept until it has loaded);
  - the spawner's part revision follows its own baseline uploads;
  - the spill fine depends on the part (an oil pan costs 100), so the server accepts a bounded range.
- Still open in M4: row 5a (workshop machines: everything passes except the engine stand, whose build coroutine
  throws a native exception in the harness; a probe that logs each build step is ready) and row 5b (car tools, in
  code on `change/sync-workshop-car-tools`, not run yet). Several row 10 steps (paint, tint, welder, interior,
  repair, auction, barn) are skipped until rows 5a/5b open those windows in the guard.

## 2026-10-06 (15:10–15:40) — row 3 merged; row 4 car details (branch `change/sync-car-details`)

- Lane-1 regression `20261006-144952` of the row 3 branch: all 25 scenarios PASSED. `main` = `635b22b` (row 3).
- Row 4 (car details) first cut works: spawn snapshot after the part baseline, 1 Hz poll of fluids, wheels,
  alignment and info, commit hooks for plates, the `car-details` snapshot for late joiners, server store with
  per-entry merge, clamps and a request for missing snapshots. Scenario `car-details` passes (spawn snapshot, live
  changes of fluids/alignment/mileage/dust/plate, late join). Writing a whole `LicensePlatesData` struct back from
  the mod works (spike check 7).
- Guard: wheel and headlamp alignment and fluid draining are allowed now. Draining also unblocks the engine crane
  (row 1), which needs the oil drained first.
- Not yet in row 4: ECU/carburettor tuning, window tint, per-part paint, bonus parts (their windows and modes stay
  blocked), and the missing-snapshot request is not covered by a scenario.
- Running: full lane-1 regression of the row 4 branch.

## 2026-10-06 (14:00–15:10) — M3 started: generator spike, row 3 orders and jobs (branch `change/sync-orders-and-jobs`)

- Generator-client spike (your idea): a hidden headless instance (`-batchmode -nographics`, 15 fps) reaches the garage
  in 14 s, uses 2.6 GB RAM and 5 % of one CPU core, and generates orders with no player. Numbers for all variants in
  `docs/spikes/generator-client.md`. Your decision is in `QUESTIONS.md` (default: optional server feature later).
- Row 3 (orders and jobs) works end to end in the harness:
  - server: open → claimed → active → removed, elected order generator, expiry and claim timeout on the server,
    lost job cars reopen the order, `jobs` save section and snapshot, `jobs` command;
  - client: mirror and applier, only the generator generates, server-approved accept with a bypassed native re-run,
    decline/expiry through the server, take detected in `selectedJobs`, payout/XP captured once at the end;
  - scenarios `jobs` (generation, expiry, decline, accept race, customer car for both, payout once) and
    `jobs-latejoin` (late join, refused unclaimed job car, claim release, server restart) pass;
  - the runtime trace found that the game's own take calls `CancelJob`, and that tutorial missions are `MissionID 0`;
    both are handled. Orders, the examination report and job checks are allowed by the guard now.
- Limits: the vanilla end-of-job checks (bolts, body, fluids, other parts) need a really finished job, so the harness
  ends jobs through `job-end-direct`; please check a real job in the playtest. `JobProgress` was dropped (nothing in
  the game writes `JobPart.Found`). Steam stats of a finished job are not shared yet.
- Running: full lane-1 regression of the row 3 branch; then merge into `main`. Next in M3: rows 4 (car details) and 13
  (test drive), both with spike docs ready.

## 2026-10-06 (13:40–14:00) — M2 complete in code

- `desync-soak`: 78 work steps in 10 minutes, 9 first-round mismatches absorbed, 0 confirmed desyncs.
- Lane-1 regression `20261006-133757` of the row 14 branch: all 23 scenarios PASSED (incl. `desync-autofix`,
  `junkyard-trip`, `resync-key`). `main` fast-forwarded to `e81ae48`: M2 (rows 1, 2, 14 b+c, junkyard trips,
  latency injection `net-delay`) is complete in code; the M2 playtest is yours (checklist in the 12:40 entry).
- Running: the generator-client spike (`tools/test-env/Run-GeneratorSpike.ps1`); then row 3.

## 2026-10-06 (13:05–13:40) — M2 rest: desync repair, manual resync, junkyard (branch `change/desync-detection-and-resync`)

- Lane-1 regression of `main` (`20261006-130225`): all 21 scenarios PASSED, none flaky.
- Row 14 part b: the server compares digests (world, inventory, car placement, one car per round) every 5 s with
  every player in the garage, confirms a mismatch over two rounds with unchanged hashes, writes a field diff to
  `Log/desync/` and resends that section; a repair that does not hold becomes persistent (F7 hint, 5 min pause).
  Command `desync [check]`, config `desync_check_interval_seconds`, `desync_autofix`. `desync-autofix` passes
  (car part and inventory item repaired). Not covered yet: the garage section and the warehouse (the client does not
  read upgrade levels, and warehouse moves may not be synced live).
- The placement digest found a real bug: a spawn request carried the car's place before the game had set it, so the
  server and the other players disagreed on where the car stands. Fixed (request after load).
- F7 also sends the full digest; the server logs which sections differed.
- Junkyard trips (parts only) are allowed: inventory hooks send only from the garage, so parts picked up and left
  behind never reach the server; buying sends them once. `junkyard-trip` passes. The travel fee still only changes
  local money; the world digest puts it back (row 10).
- Running: `desync-soak` (10 minutes of work on one car, no desync may be confirmed). Next: full regression of the
  branch, merge to `main`, then the M2 milestone is complete except the user's playtest.

## 2026-10-06 (12:40–13:05) — row 2 merged; dev build 0.6.0-dev.611 with cars to play with

- `main` = `ba27389`: rows 1 and 2, the economy fixes and the resync key. Zips
  `tools\release\out\CMS21-Together-0.6.0-dev.611-client.zip` / `-server.zip`; `release-smoke` PASSED.
- Since the last entry: `car-parking-full` passes; economy fixes (sell price ×0.5, Expert XP ×2, scraps into the
  profile, car sale from `CarInfo` blocked while connected) checked in `car-race` and `guard`; F7 resync
  (`resync-key` passes; the digest part of row 14 is not built yet); F6 developer shortcut spawns a test car.
- The lane-1 regression `20261006-123223` ran before those fixes: everything passed except `car-race` and `guard`,
  which used verbs that were not deployed yet; both pass since. A full regression of `ba27389` is next.

### What to try (cars, on top of the M1 checklist)

1. In both clients' `UserData\MelonPreferences.cfg`, under `[CMS21Together]`, set `DevHotkeys = true`. In the garage,
   F6 spawns a random base-game car on a free place (there is no other car source yet: orders are row 3, buying cars
   is row 6).
2. Take parts off and put them back together, also on the same car at the same time; body parts (doors, hood) too.
   The item lands in the shared inventory once.
3. Move cars between places and onto a lift with the pie menu; raise and lower the lift from both clients.
4. Park a car (pie menu "move to parking"), take it out from the parking menu on the other client, swap two slots.
5. Leave and rejoin, restart the server: cars, parts, lifts and parking come back.
6. If something looks wrong, press F7: the garage reloads from the server.
7. Not yet: engine crane (needs oil draining, row 4), orders, buying cars, junkyard.
8. Report with both `MelonLoader\Latest.log` files and the server's `Log\Latest.txt`; server commands `cars` and
   `placement` print what the server has.

## 2026-10-06 (11:50–12:40) — eight research spikes; row 2 (lifts, places, parking) nearly done

- Eight parallel agents wrote static spikes into `docs/spikes/` (no game, no code): `orders-and-jobs`, `car-details`,
  `workshop-machines`, `workshop-car-tools`, `test-drive`, `outdoor-scenes`, `economy-paths`, `generator-client`.
  Each lists what changes the design of its row and the runtime checks still needed. Highlights:
  - generator client: headless looks plausible but unproven; Steam on another PC is the blocker; recommendation:
    use it for orders only (best candidate for row 3's generator), not as a replacement for row 16.
  - economy: every money/scrap/XP change goes through `GlobalData.AddPlayerMoney/Scraps/Exp`; the mod hooks money only
    per feature, 28 callers change money locally only (two reachable today: the fluid-spill fine, skill reset);
    server bugs found: Expert XP not doubled, item sales paid at full price, scraps not copied into the profile wrapper.
  - test drive: the mod's `LoaderAddition.VanillaLoad` dropped the per-car mileage loop, so test-track mileage is lost.
  - workshop machines / car tools / car details: many design hooks never fire (inlined or shared native bodies); each
    doc gives the replacement.
- Row 2 (`change/sync-car-placement-and-lifts`): static + runtime spike (`placement-spike`), packets, server
  (`Data/Placement`, handlers, `car-placement` save section and snapshot, `placement` command), client
  (`Logic/Car/Placement`: `LifterSync`, `CarPlacementSync`, `ParkingSync`, `CarLoading`, `NewCarDataCodec`), guard
  allows moving cars, lifts and parking management. Scenarios `car-placement`, `car-placement-race` (net-hold races:
  lift, park, unpark, swap, priced arrival, level unlock) and `car-placement-latejoin` (late join with a raised lift
  and a parked car, server restart) pass.
  - Found and fixed: when two players take the same parked car onto the same loader, the loser's refusal deleted the
    winner's car (`ParkingSync.KeepAfterRejection`).
  - Moving onto an occupied place swaps the cars (default for QUESTIONS.md row 2 question 1).
- Open in row 2: scenario `car-parking-full` (written, not run yet); 5.6 (the parking scene itself stays guarded).
- Running: full lane-1 regression of `2eb0d3c`.

## 2026-10-06 (11:25–11:45) — row 1 complete in code (branch `change/sync-car-parts`)

- Every task in `sync-car-parts/tasks.md` is checked. New since the last entry:
  - Engine crane out/in is one part transaction (`EngineCraneHooks`, scenario `car-crane`); engine swaps are refused
    while connected (a swap rebuilds the engine under `root` and shifts part keys). The crane needs the oil drained
    first, and draining is row 4 (`DrainTool` stays guarded), so players can use the crane only after row 4.
  - Server returns OR-merged `examined` flags in the accepted result (putting the engine back resets them).
  - `cars` server command; `car-race` round 2 forces a server rejection (`part-hold-remote`) and checks the inventory
    revert; `car-live` holds B in `Loading` (`car-hold-snapshot`) to exercise the D8 queue.
  - DLC rule: DLC ids are now positions in the game's DLC list; a DLC car's spawn is refused unless every connected
    player owns its DLC (scenario `car-dlc`; 91 DLC cars in the game).
  - Guard: row 1's modes, pie entries and windows are allowed while connected (engine swap and building engines stay
    blocked); the `guard` scenario uses the row 4 `BonusDisassemble` mode as its blocked example.
  - Harness fix: `Get-ServerLogMark` skipped the first new log line.
- Two checks need a person in the game (M2 playtest): event order and item IDs of a body-part unmount through the
  pie menu, and whether a second action can start before the first finishes.
- Lane-1 regression `20261006-113902` PASSED (all 16, none flaky). `main` fast-forwarded to `b282383` (row 1).
  Row 2 continues on `change/sync-car-placement-and-lifts`. An agent does row 2's static spikes
  (`docs/spikes/car-placement.md`).
- Zips `tools\release\out\CMS21-Together-0.6.0-dev.586-client.zip` / `-server.zip`; `release-smoke` PASSED on them.

### What to try (row 1 playtest, on top of the M1 checklist)

1. Not playable yet: while connected there is no way to get a car into the garage (orders are row 3, taking a car
   out of parking is row 2, both still blocked). This checklist applies once row 2 lands; until then row 1 is only
   tested by the harness scenarios.
2. Unmount and mount mechanical parts and body parts (doors, hood) at the same time on the same car: the other player
   sees the change within a second, the part's item lands in the shared inventory once.
3. Try the same part at the same moment: one of you gets "… is working on this part" or the part goes back.
4. Examine parts; leave and rejoin: the car comes back with the same parts off and examined.
5. Engine crane: needs the oil drained, which is still blocked (row 4), so expect the game's oil warning.
6. Report with both `MelonLoader\Latest.log` files and the server's `Log\Latest.txt`; the server command `cars`
   prints what the server thinks the car looks like.

## 2026-10-06 (11:00–11:25) — row 1: race fixed, API, dumps (branch `change/sync-car-parts`)

- `main` (M1) is merged into `change/sync-car-parts`. Lane-1 regression `20261006-105712` PASSED with every car
  scenario (`car-baseline`, `car-live`, `car-race`); `compat-refusal` and `join-presence` were FLAKY:
  - `compat-refusal`: the scenario read a stale `status.json` (B still `Failed` from the previous refusal); it now
    waits until B leaves `Failed` before the next join. Run `20261006-111147_L1_compat-refusal` PASSED.
  - `join-presence` (`20261006-110303`): B froze while loading the menu after "Join request → yes" (log and status
    stop at `SelectSceneToLoad(4) Menu`). First time in 5 runs; watch it.
- Race fix (`d6205a5`): the slower client aborts its own transaction before applying the winner's inventory.
- D11 API for other rows (`f318986`): `CarPartsSync.MarkDirty`, `RebuildRegistry`, `LocalPartsCommitted`.
- Dumps carry the full car part state (task 5.2) incl. `blocked`; `car-live` checks that A and B block the same
  parts after a remote unmount (task 3.3 done). `car-live` and `connect` PASSED with the larger dump.
- Your idea (server-hosted generator client) is a spike in `ROADMAP.md` at the start of M3, and under "Decide later"
  in `QUESTIONS.md`.
- Running: an agent maps the engine crane out/in path with the native decompile (`docs/spikes/engine-crane.md`).
  Next: engine crane group transaction (task 3.5), ordered change queue (3.2), then row 2.

## 2026-10-06 (11:00) — M1 done: dev build 0.6.0-dev.561

- All M1 rows merged to `main` (`58ad6e2`): presence/names/scenes (row 6 part 1), join menu with readable failures and
  Steam join (row 8 part 1), mod/version/DLC checks (row 9 part 1), release zips (row 12 part 1), feature guard
  (row 14a). Regressions: lane 1 `20261006-102833` (all M1 scenarios green), lane 2 `20261006-103205` from the merged
  tree (green; `join-coldstart` flaky because of a test-setup bug fixed during the run).
- Zips: `tools\release\out\CMS21-Together-0.6.0-dev.561-client.zip` and `-server.zip` (main checkout, not in git).
  Install and join: `docs/try-it.md` (also inside the zip).

### What to try (M1 playtest checklist)

1. Host: run `TogetherServer\CMS21_Together_Server.exe` (from the client zip). For friends over the internet: forward
   TCP+UDP 7777, or set `use_steam = True` and join with the Steam server ID / Steam "Join Game".
2. Everyone: install the client zip into a game install **without gameplay mods** (QoLmod, TK, LvxBetterCarSpawns,
   QuickShop are refused; LoadOptimizer is fine). Set your name in the Multiplayer panel.
3. Join from the main menu (Multiplayer → address → Join). Try a wrong address and see the message.
4. Walk around the garage together: names over heads, a late joiner sees players standing still.
5. Shared money/XP/scrap: buy something in the shop, upgrade the garage, open the warehouse together.
6. Travel to the junkyard alone, come back: the garage is reloaded from the server.
7. Try things that are not shared yet (orders, disassembly mode, tire changer): the game refuses them with
   "... is not supported in multiplayer yet".
8. Leave and rejoin; close the server with `/stop` and start it again: money, level, inventory are kept.
9. Report: `MelonLoader\Latest.log` and the server's `Log\Latest.txt`, with what you did.

Known gaps in M1: working on cars is blocked (M2 is in progress on `change/sync-car-parts`); no shared junkyard;
the Steam join with a real friend is untested (needs you and a friend).

## 2026-10-06 (09:50–10:35) — mod-compatibility part 1 (row 9, agent, branch `change/mod-compatibility`)

- DLC decision applied first: a different DLC set never refuses; the server tracks the shared DLC set (intersection
  of connected players), logs it, shows it in `compat` and sends `ServerInfo.SharedDlc`; rows 1, 2, 5a block DLC
  content outside it (INTEGRATION.md "DLC content"). `DlcMismatch` and the `dlc` config key dropped.
- Groups 1–5 done: protocol hash + client welcome check, one `ConnectPacket` send path (`ConnectPacketFactory`),
  game version pinned from the first client (or configured / `meta.json`), mod classifier (fixtures of 8 real mods
  pass `--check-mods`), `CompatibilityPolicy`, `/compat`, harness `compat-report`/`compat-override`.
- Runs: `20261006-101337_L2_compat-refusal` PASSED; regression `20261006-101633_regression` PASSED on lane 2.
- Open (review.md Implementation notes): DLC product ids are not unique (11 × "-1"; proposed: use the
  `GetDLCs()` index, as `PartProperty.DLC`/`IsDLCInstalled` do — needs the user); Steam send path verified by code only.
## 2026-10-06 (10:40) — in flight (superseded by the 11:00 entry)

## 2026-10-06 (09:45–10:15) — M1 rows done except row 9; M2 started (cars)

- Row 8 part 1 complete: Steam rich presence join strings, join requests with a leave-and-join dialog, cold start
  from `+connect`; `join-coldstart` and `join-presence` pass. Open: the real Steam test with a friend (user).
- Row 12 part 1 merged and verified: `release-smoke` passes from the zips (`0.6.0-dev.548`); regression
  `20261006-094659` green afterwards; `main` at that point.
- Row 14a: single-player audit outcomes in its design (pause menu keeps running, save buttons hidden, no
  `Time.timeScale` writers, `GarageLoader.Save` blocked by row 7).
- Row 9 part 1 is being built by an agent on lane 2 (DLC decision applied: no refusal, only tracking).
- **Part identity spike: all 163 car models build identical part hierarchies on two clients** (`part-identity`,
  `20261006-100013`). Row 1's planned hooks all have direct native callers (design D2).
- Row 1 (`sync-car-parts`, branch `change/sync-car-parts`): records/packets, per-loader car state, cars section v2,
  `CarPartsStore`, baselines (spawner uploads, server stores and relays), late join for cars from the snapshot,
  no cars from the client's own profile. `car-baseline` passes: a late joiner gets the same hierarchy and part
  state, and the car survives a server restart. Live changes (tracker + hooks, server arbitration) are written and
  being tested (`car-live`); transactions with inventory deltas and claims are next.

## 2026-10-06 (09:00–09:45) — scenes, join UI, guard; two real bugs

- **Bug: sessions ran on the player's own profile 0.** The game reads the selected profile from PlayerPrefs on
  every `GetSelectedProfile` call (native decompile); once the pref write was dropped, `ProfileManager.Load` picked
  "My Save". `SessionGuard` now answers slot 4 while active; the pref is never touched. `scenes` and
  `profile-safety` assert slot 4 during a session.
- **Bug: joins failed randomly with a socket access error.** The client bound UDP to the TCP local port; Windows
  reserves UDP ranges inside the dynamic port area (`netsh ... excludedportrange`). UDP now binds any free port.
  This also explains the "connected but never valid" hangs from the morning.
- Row 6 group 4: scene tracking, away/return as late join, `GarageBound` gating; `scenes` passes (car steps wait
  for `sync-car-parts`).
- Row 8 part 1: `JoinTarget`/`JoinService`/`ConnectionStatus`, readable failures (Unreachable, Timeout,
  VersionMismatch with both versions, ServerFull, Kicked, server lost), `Server.Refuse`, `ServerInfo`, server
  overrides, IMGUI join panel + overlay + message (spike 1.1 = IMGUI works). `join-ui` passes. Not done: Steam
  rich presence and join strings (group 4), the friend test.
- Row 14a `multiplayer-guard` (agent wrote it, I tested on lane 1): `guard` passes; pie options are locked in a
  `PrepareIcons` postfix. Open: runtime trace spike, the single-player audit (3.1).
- Row 12 part 1 is on `change/release-and-docs` (agent; `release-smoke` not run yet).
- Memory: two instances commit up to 31 of 40 GB (each game commits 8–9 GB while loading, working set 2–3.5 GB);
  four instances exceed the commit limit. A larger page file would allow two lanes (user's decision).

## 2026-10-06 (late morning) — M0 done, M1 started: presence

- **M0 complete** and on `main`: regression `20261006-083824_regression` PASSED on lane 1 (connect,
  presence-latejoin, profile-safety, server-restart, server-saves) in 3½ min.
- `sync-players-and-scenes` slice 1 + names (tasks 1.2–1.8, 2.1–2.6, 3.1–3.3): server presence records, roster
  snapshot (`players`), avatars driven by the roster, spawn slots, player names (preference, de-duplicated on the
  server), IMGUI name tags. **The idle late-join bug is fixed**: `presence-latejoin` passes, `connect` now requires
  both players to see each other.
- Seen in a log: on the way to the menu the game called `GameDataManager.Save(0)` (the player's real profile 0 in a
  normal install) during a session; `SessionGuard` blocked it.
- User allowed game tests while they are at the PC; answers to the M1 questions recorded (DLC: shared use only for
  content all players own; gameplay mods dropped in multiplayer for now; everything in English).
- `release-and-docs` part 1 is being built by an agent in its own worktree (no game runs there).
- Next: row 6 hook trace (1.1) and scene tracking (group 4: travel, away/return), then `multiplayer-guard`.

## 2026-10-06 (morning, autonomous loop) — M0 contract, test lanes, save robustness

- `session-persistence-and-rejoin` groups 1–3 done and merged/pushed to `main`:
  - Contract: versioned save sections, `SessionRegistry`, `SyncBegin/SyncEnd{Items}/SyncAck` + client
    `SyncTracker`, `GameDataManager.StateLock`, `RequestSave`, `--command-file`, 30 s no-progress sync timeout.
  - Group 3: crash-safe writes, rotating backups, start copies, fallback + quarantine + refusal, `--check-save`,
    `DisconnectReason`, save on `/stop` and window close.
- Harness: **isolated parallel test lanes** (taken as the first M0 harness task, as proposed on 2026-10-05).
  Each test install patches its own Unity company name (`RDGTogether-<X>`) into its own `globalgamemanagers`, so it
  has its own save folder and registry key; the real save and registry are never written (each run fingerprints
  them and fails if they changed). Lane 1 = A, B, `Server`, port 7777; lane 2 = C, D, `Server2`, port 7787.
  KickingOtherSession aborts a run. Server port is configurable; the client's UDP no longer always targets 127.0.0.1.
- Runs (all PASSED): `20261006-074033_L1_connect`, `074125_L1_server-restart` and `074125_L2_connect` (at the same
  time), `Test-ServerSaves.ps1` (14 server-only checks). `connect` notes the known idle late-join bug (row 6).
- Found: before group 6, a session wrote `profile4.cms21b` and left the `selectedProfile` pref at 4 (seen in lane A).
- Drafted M1 rows: `hosting-and-join-ui`, `release-and-docs`, `mod-compatibility`, `desync-detection-and-resync`
  (second reviews running). Native decompile spike running in the background.
- Unattended rule used: the game is only started after 10 min without keyboard/mouse input
  (`tools/test-env/Get-UserIdleSeconds.ps1`).
- Group 6 (client save safety) done: `profile-safety` passed twice (`080402_L2`, `080549_L2`). The session no
  longer writes the `selectedProfile` pref at all, so no crash recovery is needed. M0 is complete except a clean
  regression run. Native decompile spike done: `docs/spikes/native-decompile.md`.
- **Memory limit:** the first `Run-All` with both lanes (four game instances + server checks) ran the 32 GB PC low on
  memory; Claude Code stopped the run, and two clients had hung during the garage load (`connect` and
  `server-restart` each failed once and passed on rerun = flaky under memory pressure). Fix: lanes take turns with
  the game (global mutex in `Run-Session.ps1`, refuses below 12 GB free). Lanes still let a second agent work in
  its own worktree and deploy without overwriting the first lane's build. Game tests paused until the user OKs a
  rerun (Claude Code asks not to restart a run it stopped for memory on its own).
- Client logs now go to MelonLoader's `Latest.log` (they only reached the mod's own window before); not yet
  checked in game.
- Next: `Run-All` on lane 1 → merge to `main`; then M1: `sync-players-and-scenes` slice 1 (presence roster,
  fixes the idle late-join bug) here, and one M1 row per extra agent in its own worktree + lane 2.

## 2026-10-06 (night, with the user) — planning done

- Seven changes drafted, each reviewed, then one integration pass; workshop split into `sync-workshop-machines`
  and `sync-workshop-car-tools` → 8 changes, all `openspec validate --all --strict`. Cross-change matrix in
  `openspec/INTEGRATION.md`; each change has a `review.md`.
- Roadmap reviewed: milestones M0–M6, sizes, spikes, working rules; new rows 13–16.
- User decisions in `QUESTIONS.md` (four rounds). Key: shared progression, game logic moves server-side
  (row 16, game gets no more updates), outdoor scenes via adapted LvxBetterCarSpawns (row 15).
- Next: M0 — `session-persistence-and-rejoin` groups 1–2 (contract), then groups 6 and 3; spikes in the
  background (part identity, native decompile).

## 2026-10-05 (evening, with the user)

- Forked upstream `Dev` into our `main`; OpenSpec 1.14 set up with project context and `openspec/ROADMAP.md`.
- Test harness works end to end: two hard-linked test installs, local dedicated server, `TogetherTestHarness`
  mod (command file, status/dump JSON, screenshots, mute, skips intro + start screen), `Run-Session.ps1`
  with save/registry backup + restore. A connect run takes ~2 min.
- `connect` scenario: both clients reach the garage with identical stats/inventory/cars.
- Bug found: a late joiner never sees an idle player. `Movement.UpdateMovement` only sends on change and
  remote players spawn on the first `MovementPacket`. Belongs to `sync-players-and-scenes`.
- Seven OpenSpec changes being drafted (one per roadmap row).
