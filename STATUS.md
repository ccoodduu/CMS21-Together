# Status

Newest first. One entry per work session.

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
