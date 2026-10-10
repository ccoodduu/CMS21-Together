# Roadmap

Goal: a playable co-op session on the dedicated server (Dev architecture) for 2–4 players: shared garage,
shared cars, shared jobs and tools, with state that survives a server restart and a late join — installable
by a friend from a release zip and joinable without typing IDs by hand.

## Status (2026-10-06)

M0–M2 done; M3 done in code (rows 3, 4, 13 merged). M4: rows 5a, 5b, 6 part 2, 8 part 2 and 10 merged (2026-10-06 evening;
the engine stand of row 5a is a hand check). The M2 and M3 playtests are the user's. The guard is open for them (2026-10-07).
M4 is done in code (car purchases merged 2026-10-07; barn, salon and auction purchases are hand
checks). M5: rows 7 and 14d done; row 11 runs with four players (storms and full-garage late join green); the soak runs
and the long soak waits for the user. M6: row 12
part 2 docs merged, row 9 part 2 tuned on the user's mods (friends' lists later); the release itself waits for
the user. Two test lanes can run in parallel since the pagefile was
raised (commit limit 64 GB); until the user answers QUESTIONS.md, one lane at a time because of Claude Code's memory
guard. Rows 25–30 (single-player features the user wants in multiplayer, seated avatars, job achievements for contributors) drafted 2026-10-08 on `change/singleplayer-features` and revised after review the same day: row 26 cut to a salon purchase proof, row 27 split into 27a (tracks), 27b (races) and 27c (collisions); row 31 (faster remote cars) is a proposal only.

## Changes

Size: S ≈ 1–2 sessions, M ≈ 3–5, L ≈ 6–10, XL > 10 (one session = one autonomous work block).

### Drafted (`openspec/changes/<name>/`)

| # | Change | Owns | Depends on | Size |
|---|--------|------|-----------|------|
| 1 | `sync-car-parts` | Mount/unmount and state (condition, examined, quality, dent, bolts in progress) of body parts (`CarPart`) and mechanical parts (`PartScript`) on cars in the garage, late-join replay of loaded cars + their parts | 7 (contract) | L |
| 2 | `sync-car-placement-and-lifts` | Lift states, moving a car between car loaders/places, garage parking lot, car transfer to/from parking | 1 | M |
| 3 | `sync-orders-and-jobs` | Order generation (server owns the order list), accept/decline, customer car spawn, task progress, ending a job (payout/exp), order expiry, story missions | 1, 2 | L |
| 4 | `sync-car-details` | Fluids, wheels/tires/rims and alignment, tuning parts, paint/livery/tint, dirt/wash state, license plates, headlamp alignment, car info (mileage, headlights on/off), visual tuning (bonus) parts | 1 | L |
| 5a | `sync-workshop-machines` | Tire changer, wheel balancer (locked while one player plays the minigame), spring clamp, engine stands 1/2 (incl. parts on the stand), brake lathe, battery charger, repair table and part painting, tool positions | 1 | L |
| 5b | `sync-workshop-car-tools` | Engine crane (out/in effect, engine swap trigger), car paint, car wash, interior detailing, oil bin, welder, dyno trigger | 1, 4, 5a | M |
| 6 | `sync-players-and-scenes` | Spawn positions, name tags, player in car seat, engine running/sound, scene tracking (who is where), visibility per scene, travel to junkyard/barn/auction/dealer and how purchases there flow into shared inventory/parking | 2 (purchases only) | L |
| 7 | `session-persistence-and-rejoin` | Server save format + versioning for all state above, autosave, identifying a returning player (per-player data), rejoin/late join end-to-end, client-side save safety (the client must never overwrite the player's own profiles) | — (contract first, rest last) | L |
| 8 | `hosting-and-join-ui` | Part 1 (M1): join by IP/Steam ID or Steam rich presence (Join Game, invites, cold start), one connection status with readable failure/disconnect messages, player name field, `ServerInfo`, refusals that reach the client (`Server.Refuse`). Part 2 (M4): host from the game (bundled `TogetherServer\`), new-session difficulty, DirectIP password, admin key and kick, session panel with ping, join/leave toasts, Steam friends panel | 7 (contract, group 3), 6 part 1; part 2: 12 part 1 | L (≈ 11: part 1 M ≈ 5–6, part 2 M ≈ 5) |
| 9 | `mod-compatibility` | Part 1 (M1): handshake checks — mod version + protocol hash, game version, gameplay mods; shared DLC set tracked (no DLC refusal) (Harmony patch-target classifier, server lists), one `ConnectPacket` send path, refusal reasons. Part 2: game-data exporter for `Database/*.json` (with row 16, M3 at the latest), classifier tuning on real mod lists (M6) | 7 (group 3), 8 part 1 | M (part 1 ≈ 4–5, exporter S, tuning S) |
| 12 | `release-and-docs` | Part 1 (M1): one version source (`BuildInfo`), `Build-Release.ps1` (client zip with `TogetherServer\`, server zip, manifest), `docs/try-it.md`, `release-smoke`. Part 2 (M6): install/hosting guides, changelog + version policy, offline `Collect-Logs.ps1` in the shared bug-report layout | 8 part 1, 9; part 2: 14 (d) | S + S (part 1 ≈ 2, part 2 ≈ 2–3) |
| 14 | `desync-detection-and-resync` | (b) Continuous state reconciliation: per-section/per-car digests compared by the server, automatic resend of a confirmed mismatch, diff log — M2. (c) Resync key F7 (garage reload = late-join snapshot) — M2. (d) One-key bug report F8 (client + server + other clients, shared layout and redaction) — M5 | 14a, 1, 2; (d): 7 | L (≈ 6–7: b M ≈ 4, c S ≈ 1, d S ≈ 1–2) |
| 14a | `multiplayer-guard` | Default-deny guard while connected (windows, pie options, game modes, scenes on an allow-list that each row grows in its merge commit; "not supported in multiplayer yet" message; player override) and the single-player assumption audit (pause, time scale, camera modes, autosave, modal windows) — M1. Split from row 14 | 7 (groups 1–2, 6), 8 group 1 (`ModNotify`) | M (≈ 4–5, one with the user) |
| 18 | `part-locks` | Strict locks granted by the server before work on a car starts (one round trip; the action runs only after the grant). A part locks its unmount group exclusively and, shared, the parts it is fixed to (ancestors, the game's `unblockOnUnmount` relation) and its fluids. Fluid fill, extractor and oil bin lock the fluid. A mount locks its inventory item. Lift, move and swap lock the car, and row 13's away claim counts as a car lock. Blocked at selection: hover highlight and label, click refused at once; park, delete and job end refused while another player works. Replaces row 1's optimistic claims (`CarClaims`, `CarPartClaim*`). Playtest fix (findings 1 and 4 of 2026-10-07), lands before the next Steam playtest. **Drafted and reviewed 2026-10-07**; open questions in its proposal.md. **Implemented 2026-10-08** on `change/part-locks` (groups 1–10 and the docs; the lane-3 `locks-scale` run, task 11.1, comes with the next soak). Question 7 (default yes) moves mount-mode previews, item-chooser filtering and pie greying to a follow-up `part-locks-2` (M ≈ 3) | 1, 2, 3, 4, 5b, 13, 14a, 17 part 1 (all merged) | L (≈ 9–10; XL ≈ 12–13 without the split) |
| 19 | `state-merges-and-contention` | The race and drift audit's gaps that row 18 does not close (`docs/audits/race-and-drift-audit.md`). Part 1, state merges: part records carry a changed-field mask and the server merges per field group, so a stale examine or condition change never undoes another player's mount (gap 3); car details travel as entries, merged per entry on the server, remembered per entry on the client, with the own echo skipped per `ClientSeq` (gap 6); a machine put of an item another player used is refused and says whether the item was returned or is gone (gap 9); a parked car keeps the server's part records and details, which the unpark applies (gap 10). Part 2, detection and contention: digest keys `car-details:<loader>`, `workshop-tools`, `warehouse`, `garage`, `jobs`; a "not ready" round no longer clears a pending mismatch; the forced check asks every car; a stall warning; `soak.ps1 -Contention` with conservation and outcome rules and a server order that a replay reproduces (gap 7 rest, the audit's soak contention mode). Part 3 (user decision 2026-10-07): the server answers every action it refuses, ignores or overrides with the authoritative result (no silent drops), and arbitrates seats (new packet `SeatRefused`). No new hooks. **Drafted 2026-10-07, reviewed and revised 2026-10-08**; open questions in its proposal.md. **Part 3 implemented 2026-10-08** on `change/server-answers` (groups 12–13: D16 answers, `SeatRefused`, scenario `server-answers`, the seat race in `seat-engine`; the dropped-transaction step comes with task 3.5). **Part 1 implemented 2026-10-08** on `change/state-merges-1` (groups 1–8: spikes, masked part records with a base check incl. quality, details per entry with the own echo per entry, machine puts that name the player who used the item, parked cars that keep the server's records, row 18 tie-ins; scenarios `car-stale-record`, `details-concurrent`, `tools-item-race`, `park-stale`). Audit rows I6, E5, C1, C5 go to row 20 | 1, 2, 4, 5a, 5b, 11, 13, 14 (merged), gap 5 fix (`d1dd908`); task 1.4 lands before 18's `locks-fluid`; groups 3–4, tasks 6.3 and 9.2's car keys start after 18 merges; group 8 and task 10.4 after its switch-over (task 5.1) | L + L + M (part 1 ≈ 9–11, part 2 ≈ 6–7, part 3 ≈ 3; XL ≈ 18–21 as one piece) |
| 25 | `sync-tuning-bonus-and-new-engines` | User wish 2026-10-08: single-player car features in multiplayer. Tuning at the dyno (`Window Tune`: gearbox commit hook `GearboxTab.ApplyAction`, ECU/carburettor already synced by row 4; one tuner per car through a row 18 lock kind `Tune` on a `tune` key plus the tunable parts, 5 min idle cap; tuning carried on items through the inventory), bonus (visual tuning) parts (per-slot detail entries `x:<slot>`; the fit gated in the `SelectPartToMount` prefix because the game removes the item after `TakeOffBonusPart`; lock kind `BonusPart` with the expected slot state, `Stale` refusal; modes `BonusAssemble`/`BonusDisassemble`), building a new engine on the engine stand (`Window CreateEngine`, pie `engine_new`: created puts marked per built group, refused on an occupied stand, a refused build discarded instead of returned, `ToolsCheck` self-check; the build itself is a hand check unless spike 1.1b drives it). "Car version" belongs to the car salon (row 26). Spike: `docs/spikes/singleplayer-features.md` sections 1–3. **Drafted 2026-10-08, review applied**; open questions in its proposal.md. **Part 1 (tuning) built 2026-10-09 on `feat/tuning-window`** (proof `car-tuning` fails on the old code `20261009-222538_L1`, passes `20261009-222319_L1`; smoke and touched scenarios `20261009-222727_regression.json`, `guard` rerun `20261009-224552_L1` after its blocked window example moved to `RevertBackup`); **part 2 (bonus parts) built 2026-10-10 on `feat/bonus-parts`** (proof `car-bonus` fails on the old code `20261010-024945_L1`, passes in `20261010-025058_regression.json` with smoke and touched scenarios; `guard` passes `20261010-031053_L1` after its mode example became log-only, no blockable mode is left); **part 3 (new engines) built 2026-10-10 on `feat/new-engines`** (spike 1.1b: the build can be driven headless when stepped; proof `engine-build` fails on the old code `20261010-032613_L1`, passes `20261010-032430_L1` and in `20261010-032836_regression.json` with smoke and the tools area; `--check-tools`). All three parts built | 4, 5a, 5b, 13, 18, 19, 24 (all merged) | M (≈ 5–6; tuning ≈ 1, bonus parts ≈ 3, new engine ≈ 1.5–2; each group merges on its own, tuning first) |
| 26 | `shared-salon` | User wish 2026-10-08, scope cut by the user the same day: the car salon (`Auto_salon`), not the main menu's Showroom; no shared display lineup (purchases come from the configurator's full catalog). Proves a salon purchase in multiplayer (configurator car with a non-default version and rim lands once in the shared parking for everyone, money taken once) and the server's `NoMoney` refusal with an answer to the buyer after the game's local check passed; guard clean-up (`Window CarVersion` → "Car version (car salon)", allowed; `Scene`/`Window Showroom` → main menu only). No packet. **Built 2026-10-09 on `feat/shared-salon`** (proof `salon-buy`: fails on the old guard `20261009-005105_L1`, passes `20261009-011548_L1`; smoke and guard area pass, `20261009-011825_regression.json`) | 2, 6 part 2, 10, 19 part 3 (all merged) | S (≈ 1) |
| 27a | `shared-race-tracks` | User wish 2026-10-08: drive on the race track like on the test track, base-game tracks only. Base game: race track (`Race_track_1`) and speed track (`SpeedTrack`, `FreeTrackManager`); DLC: drag strip (Drag Racing DLC, index 22); Workshop: custom tracks; FunTrack/OffroadTrack have no scene. Away claims (`CarAwayKind.RaceTrack`, `SpeedTrack`, `AwayCheck` self-check), visible driving (row 17 part 2), ride-along (row 21, return through `TrackManager.Instance`) and returned mileage/dirt for the track set; scene identity by name (the game reports the speed track as `TestTrack`); all 49 `TestTrack` references classified first; best lap per player and group record kept by the server across restarts (`LastTime` decompiled before the hook). **Drafted 2026-10-08, review applied; split into 27a/27b/27c. Built 2026-10-10 on `feat/shared-race-tracks`** (also the speed track's best top speed, which the game writes to `ProfileData.TopSpeed`; proof `race-track` fails on the old code `20261010-041057_L1`, passes `20261010-040634_L1` incl. a server restart; smoke and touched scenarios `20261010-041514_regression.json` 14/14; hand check 5.1 open) | 6, 7, 13, 14a, 17 part 2, 21 (all merged) | M (≈ 4–5) |
| 27b | `track-races` | User decision 2026-10-08: a shared race on the race track only (not the speed track): "Start race" with a lap count, server `TrackRaces`, `RaceCountdown` timed by half the ping so the game's own lights turn green together (overlay fallback), laps from 27a's `LastTime` hook, DNF on leaving, disconnect, pause-menu restart or timeout, `RaceResult` broadcast and the last 10 kept in `world`. **Drafted 2026-10-08. Built 2026-10-10 on `feat/track-races`** (spike 1.1: the game's lights released by a throttle gate 3 s before the shared start, 10 s countdown, no overlay; start from the F9 panel on the race track; proof `race-start` fails on the old code `20261010-091535_L1`, passes `20261010-092317_L1` with the green lights 14 ms apart; smoke and touched scenarios `20261010-091648_regression.json` 10/10; `--check-races`; hand check in playtest 5 open) | 27a; 7, 8, 17 part 2, 19 part 3 (merged) | M (≈ 4–5 incl. spike) |
| 27c | `track-collisions` | User decision 2026-10-08 (collisions wanted again): spike first (go/no-go), then one kinematic box collider per remote copy on a dedicated layer, moved with `MovePosition` in `FixedUpdate`, the copy's own colliders stay off (row 17's wheel freeze); enabled only without overlap and not for 1 s after a snap; off on a copy carrying a ride-along passenger and during a 27b race start; client setting `track_collisions` (default on) and a host switch in `ServerInfo`. Contacts are one-sided (the copy is never pushed). **Drafted 2026-10-08** | 27a; 27b for the start rule; 17 part 2, 21 (merged) | S + S–M (≈ 2.5–3) |
| 28 | `shared-garage-look` | User wish 2026-10-08: the garage's look shared and stored by the server (`Window GarageCustomization`: 41 sections with material variants, walls, floors, lifts, machines, lockers, gates, area floors and walls; texture packs; no placeable decorations exist in the game). Server stores `{ MaterialIndexes, TexturePack, SectionCount }` in the `garage` state (section v2); commit on window close, one editor at a time (claim gated at the `#garageLook` click), apply per section through the game's `UpdateMaterials` overload (restore for defaults; re-running `Load()` cannot reset), texture pack setters, look digest from the last applied server look. **Built 2026-10-09 on `feat/shared-garage-look`, ready for merge** (proof `garage-look`: fails `20261009-212616_L1`, passes `20261009-213011_L1`; spike `20261009-212145_L1_garage-look-probe`: gate in `ClickIO`, apply confirmed, the snapshot does not wait for the apply) | 7, 8, 14, 14a, 19 (all merged) | M (≈ 3) |
| 29 | `seated-avatars` | User wish 2026-10-08: a player seated in a car in the garage is shown seated (driver or passenger seat, following lifts, standing up on leave, late join), reusing row 21's seat pose through one `SeatPoses` helper that also places the name tag; seated records no longer move the avatar; replaces the accepted default "a seated player's avatar is hidden". Client only, no packet. **Built 2026-10-09 on `feat/seated-avatars`, ready for merge** (proof `seat-avatars`: fails `20261009-000552_L1`, passes `20261009-002305_L1`) | 2, 6, 19 part 3, 21 (all merged) | S (≈ 1) |
| 30 | `shared-job-achievements` | User wish 2026-10-08: Steam stats and achievements of a finished job (`stat_finish_order`, `stat_bonus_exp`, `stat_bonus_money`, `stat_finish_allmissions`) for every connected player who worked on the job (taker, finisher, accepted part/detail changes, examines, car locks; job found from the car's spawn record), once, never twice for the finisher; the server derives the stats in `OnJobEnd` (no client report); host setting `job_stats_to` (`contributors`, `garage`, `finisher`), also a runtime command. Row 3's spec promised stats for all connected players (`JobStatsAwarder`), never built. Level achievements are already shared. **Built 2026-10-09** (`feat/shared-job-achievements`; proof `job-stats` fails on the old code `20261009-010925_L1`, passes `20261009-203134_L1`) | 3, 7, 18, 19 part 1 (all merged) | S (≈ 1–2) |
| 31 | `faster-remote-cars` | User wish 2026-10-08 ("the long load when other cars join must be fixed at some point"): another player's car on a track appears ~10 s after arrival (7 s when already there; QUESTIONS.md row 17 #8: the load takes ~6.5 s and waits 3 s after your own car is ready because starting earlier froze the game). Options: a settle signal instead of the fixed 3 s, keep the copy between drives, load before you arrive (server sends running drives on arrival), a lighter copy; spike measures the load steps first. **Built 2026-10-10 on `change/faster-remote-cars`, ready for merge** (scope B: the wait after the own car is ready 3 s → 0.5 s; car shown ~0.65 s after arrival instead of 3.3–3.6 s; the first copy's one-frame load, 85–130 ms headless, unchanged; proof `remote-car-timing` fails `20261010-120734_L2`, passes `20261010-121219_L2`) | 17 part 2, 21, 27a | S |

### Not yet drafted — needed for a finished product

| # | Change | Owns | Size |
|---|--------|------|------|
| 10 | `economy-audit` | Every money/scrap/XP path is server-authoritative: travel fees, selling cars, auction bids, barn/junkyard purchases, parking levels, paint/wash/welder costs, opening crates (upstream #94: money/level desync after crates). Rows 2/3/6 cover some; this row closes the rest (known gap from row 6: travel fees and car sales are undone by the next `WorldState`). | M |
| 11 | `multiplayer-soak-and-scale` | Harness with 3–4 instances, long scripted sessions (soak), bandwidth/CPU check, late-join time with a full garage + parking, disconnect storms. (Artificial latency/packet loss moves to M2, see below.) | M |
| 13 | `sync-test-drive-and-diagnostics` | A car taken to the test track or test path (and dyno runs in the garage): the car is claimed by the driver while away, others see it as away and cannot edit it, and the results (examined/discovered parts, mileage, dyno measurements `EngineData.measured`) reach the server before the returning client applies the garage snapshot. Upstream's most reported car bug (#18, #83, #85, #95: repairs and job progress reset after a test drive). | M |
| 15 | `shared-outdoor-scenes` | Junkyard, barn and auction shared by everyone in them (user wants to scavenge together). Car selection runs on the server by adapting LvxBetterCarSpawns (LvxMagick; decompiled reference in `CMS21-TestInstalls\reference\LvxBetterCarSpawns-decompiled`): vehicle candidates per location, weighted selection, spawn history, all spawn points filled; clients load exactly the server's cars. Loose parts/items: the first visitor's generated layout is stored by the server and replayed to later visitors. Picking up parts and buying cars go through the server; remote players visible in the barn too. Needs the author's permission/credit before adapted code is published (user decides when to ask). Planned after M4. **Drafted 2026-10-07** (`openspec/changes/shared-outdoor-scenes/`): server-side selection sits behind `ICarSelector` with an own default (`BasicCarSelector`); the Lvx-based selector is an optional task gated on permission. | L (≈ 9–11) |
| 16 | `server-game-logic` | The game gets no more updates, so game logic moves to the server: prices/fees (feeds row 10), job payout/XP and order generation (replaces row 3's elected generator), random damage/colour of spawned cars (replaces row 1's spawner roll). Built from native code read with Cpp2IL/Il2CppDumper + Ghidra and game data exported to `Database/*.json` (shared exporter with row 9). Rows 1 and 3 keep their client-computed approach as the interim until this row lands. | L |
| 17 | `remote-visual-feedback` | Make it feel shared, not just consistent: visual-only replay of other players' actions (bolts turning, part moving off/on, tool in hand), simple work animations on the remote avatar, then driving sync (a car driven in the garage area / test drive visible to others). Polish after M4. | M (+L for driving) |
| 21 | `ride-along` | Two in one car on the test track (user idea 2026-10-08): players seated in a car (row 6 seats) when its driver starts a test drive or test path travel with it; the passenger sits in the passenger seat of the driver's car on their own client (row 17 part 2's driving stream and observer car), camera and avatar follow the car, the passenger cannot drive, and both come back to the garage when the drive ends. Depends on rows 6, 13 and 17 part 2; spike the in-car camera and the seat handle on the observer car first. **Implemented 2026-10-08 for the test track** on `feat/ride-along` (spike `docs/spikes/ride-along.md`, design note `docs/design/ride-along.md`, packet `RideUpdate`, scenario `ride-along`; the spike also fixed row 17's observer cars, which were shown turned 180°). The test path is not covered: its motion is not shared in the garage (options in the spike) | S–M |
| 22 | `coop-ping` | Ping a part (user wish 2026-10-08): look at a part (or a spot) and press a key; every other player sees it highlighted with the pinger's name for a few seconds, also through walls, plus a short sound. Reuses row 18's local highlight; one small relayed packet, no state. Depends on row 18. **Implemented 2026-10-08** on `feat/coop-ping` (design note `docs/design/coop-ping.md`; key: middle mouse button, `PingHotkey`; scenario `ping`) | S |
| 23 | `shared-shopping-list` | One shopping list for the whole group (user wish 2026-10-08): `ShopListWindow.AddToShopList`/`RemoveFromShopList`/`ClearShopList` go through the server, which stores the list in the session save, relays changes and sends it in the snapshot; today each client keeps its own list in its local profile | S |
| 24 | `part-locks-2` | Selection in mount mode, the item chooser and the pie for parts in use (row 18 open question 7, default split): mount-mode previews hidden on a locked slot, the chooser leaves out items in another player's lock and reopens after a denied pick, and one shared owner of pie option state for the guard and the locks. Clicks there are already refused by row 18. **Implemented 2026-10-08** on `feat/part-locks-2` (design note `docs/design/part-locks-2.md`, scenario `locks-select-2`): locked slots are hidden in mount mode, the chooser marks items in another player's lock with the game's lock overlay instead of leaving them out, a refused pick opens the chooser again, and `Guard/PieOptionState` (a `PrepareIcons` prefix) greys the car move options and `car_drive` of a locked or moving car. Lifts have no pie option and stay click-refused only | 18 | M (≈ 3) |
| 20 | `race-hardening` | The race and drift audit rows that rows 18 and 19 leave (review of row 19, user decision 2026-10-07): reused UID ranges (I6), economy request deduplication by request id (E5), a spawn into an occupied car loader (C1), a second baseline replacing stored records (C5). Low risk today; none is made worse by rows 18 and 19. **Implemented 2026-10-08** on `fix/race-hardening` (design note `docs/design/race-hardening.md`, scenario `race-hardening`): I6 fixed (the inventory snapshot carries the highest stored UID of the player's range, warehouse and machines included, and new UIDs continue after it); C1 fixed (a plain spawn into an occupied loader is refused, the stored car stays and is sent to the refused client, which keeps it); E5 and C5 not reachable and not built (nothing resends an economy request; the only second baseline leaves the job taker 0.1–0.3 s after the first, before another player can act on the car) | S–M |

Backlog (not planned): import a single-player save as server start state; multiplayer tutorial (tutorial is
disabled in multiplayer games); gameplay-mod support (modded items/parts, QoLmod); seasonal event garages
(Christmas/Easter/Halloween — multiplayer always loads the normal `garage`); sandbox mode; text chat (Steam/
Discord voice covers it); per-player inventory option (upstream #100); remote per-car engine sound beyond
row 6's simple loop; Linux/headless server.

## Milestones

Each milestone ends with something the user and friends can actually try. A milestone is done when its
definition of done (Working rules) holds. Rows listed as "part N" are split by their task groups.

| M | Name — what you can try | Rows |
|---|--------------------------|------|
| M0 | **Foundations** (no playtest) — harness infra, regression runner, sync contract, client save safety, atomic save + backups | 7 (groups 1, 2, 3, 6) |
| M1 | **Friends connect and see each other** — host + 1–2 friends join over Steam from a dev zip, walk around with name tags, see each other after a late join, shop/warehouse/garage upgrades with shared money; working on cars (and travel, until row 1 restores garage cars on return) is blocked by the guard; nobody's own saves are touched | 6 part 1 (spawn, roster, names, scene tracking + away/return), 8 part 1 (join without hardcoded ID, status/errors), 9 part 1 (mod + game version + mod list check, shared DLC set), 12 part 1 (dev build zip), 14a (guard) |
| M2 | **Work on one car together** — take a car from the parking onto a lift, strip and rebuild it together, travel to the junkyard (parts only) and come back to the same garage, restart the server, the car is still there; resync key fixes a broken car | 1, 2, 14 (b, c), harness latency/loss injection |
| M3 | **Run jobs together** — accept an order, diagnose (examine, test drive, test path), replace parts, fluids and tires, hand it back, payout shared; a late joiner mid-job sees the job | 3, 4, 13 |
| M4 | **The full workshop** — every tool, junkyard/barn/auction trips with car purchases landing in the shared parking, consistent money; host and join from the in-game menu | 5a, 5b, 6 part 2 (seat/engine, purchases outside), 10, 8 part 2 |
| M5 | **Robust sessions** — 3–4 players, multi-hour session, crashes and rejoins without loss | 7 rest (identity, rejoin end-to-end, server loss), 11, 14 (d) |
| M6 | **Release 1.0** — a friend installs from the zip and the guide alone, with visual mods only | 9 part 2 (gameplay-mod heuristic tuned on real mod lists), 12 part 2 |
| M7 | **Release 1.1: scavenge together + feels shared** — shared junkyard/barn/auction, see each other's work, driving | 15, 17 |

Row 16 is split over milestones: prices/fees and job payout/XP land with M3 (rows 3 and 10 use them); order
generation and spawn damage land in M3 if the decompile spike shows they are reasonable, otherwise in M4 and row 3
ships with its interim elected generator. Row 9's game-data exporter (its task group 6) lands when row 16 starts, M3 at the
latest; only row 9's classifier tuning (group 7) is M6.

### Implementation order

1. M0: `session-persistence-and-rejoin` groups 1–2 (the contract: harness server control, `ISaveSection`,
   `ISnapshotProvider`, `SyncOrder`, `SyncBegin/SyncEnd/SyncAck`, `GameDataManager.StateLock`) — every other
   change plugs into it; then group 6 (client save safety) and group 3 (atomic save, backups), because
   friends play from M1. Groups 4, 5, 7 (identity, join hardening, end-to-end scenarios) belong to M5.
2. M1: `sync-players-and-scenes` spawn fix + presence roster (fixes the idle late-join bug), names, scene
   tracking; `hosting-and-join-ui` part 1; `mod-compatibility` part 1; `multiplayer-guard` (14a); dev zip (`release-and-docs` part 1).
3. M2: `sync-car-parts` → `sync-car-placement-and-lifts`; resync key + checksums.
4. M3: `sync-orders-and-jobs` → `sync-car-details` → `sync-test-drive-and-diagnostics` (or 4 before 3 if
   row 3's trace spike stalls; M3 needs both, because job completion checks fluids and wheels).
5. M4: `sync-workshop-machines` (common machines first: tire changer, balancer, engine stand, spring clamp; then
   the rest) → `sync-workshop-car-tools` → rest of `sync-players-and-scenes` → `economy-audit` →
   `hosting-and-join-ui` part 2.
6. M5, M6 as in the table.
7. `server-game-logic` (16): price/fee and payout parts as soon as the decompile spike confirms them (they feed
   rows 3 and 10); order generation and spawn damage before or right after M3, depending on the spike.
   `shared-outdoor-scenes` (15) after M4.

### Early spikes (de-risk before the row starts)

| Spike | Why | When |
|-------|-----|------|
| Part identity: load every car model on both clients, dump part keys, diff | Row 1 assumes every client builds the same part hierarchy for a car (DLC, mod parts, config-dependent `AddPartIfShould`); everything after row 1 sits on it | M0, background |
| Steam join over the internet with one real friend: does the anonymous game-server SteamID change per start, does relay (SDR) work without port forwarding, can a friend join from the Steam friends list | Cannot be tested in the local harness; upstream #93, #97 | M1, needs the user + a friend |
| In-game UI technology on IL2CPP (IMGUI vs. cloning the game's UI like 0.4.17's `NewUI`) | Row 8 and the guard messages need it | M1 |
| Hook trace per change (logging-only Harmony patches, verify each hook fires once per action) | IL2CPP-inlined methods never hit their patch; coroutine methods only fire on start | First task of every change |
| Test drive round trip: what the game saves before leaving and loads on return | Row 13 and row 6's "return = late join" | Done statically 2026-10-06: `docs/spikes/test-drive.md` (runtime checks listed there) |
| Server-hosted generator client (user's idea, 2026-10-06): the server starts one hidden game instance of its own (a harness-style client with a fixed generator role, no avatar) and uses it as the local source of truth for everything the game generates: orders, spawned-car damage/colour, junkyard/barn layouts, prices. Measure: does the game run headless (`-batchmode -nographics`) or in a small low-quality window with HDRP; RAM per instance (each game commits 8–9 GB while loading); can it generate orders in the garage and a junkyard layout without a player driving it; startup time; Steam behaviour (same account on the same PC works like the test lanes, another PC triggers `KickingOtherSession`). Decide with the user: generator instance as the highest-priority candidate of row 3's elected generator, and how much of row 16 it replaces | Could replace most of row 16 and the "nobody is in the garage" gap of row 3 without rewriting game logic; only works where the server runs on a PC with the game and Steam | Before row 3 starts (start of M3), decision before row 16 is planned |
| Native decompile: set up Cpp2IL/Il2CppDumper + Ghidra, read `EndJob` payout (and which Steam stats it increments), map the size of `OrderGenerator`, and find side-effect-free state setters for applying remote changes (part condition/mount, fluids, wheels, inventory) | Decides how big row 16 is, whether row 3's elected generator can be skipped, and lets rows 1/4/5 apply remote changes without the game's player-facing side effects (inventory, sounds, saves, achievements) | M0, background |

### Integration notes (cross-change decisions to keep consistent)

Final contracts after the integration pass (2026-10-06); the full matrix is in `openspec/INTEGRATION.md`.

- Contract (row 7 groups 1–2, lands first): every row plugs in as `[SessionSection]` `ISaveSection` and/or
  `ISnapshotProvider` in its `SyncOrder` slot (`world` 0, `garage` 10, `inventory` 20, `cars` 100, `car-details` 150,
  `car-placement` 200, `workshop-tools` 300, `jobs` 400, `self` 450, `players` 500); clients count items with
  `SyncTracker.Applied(key)`; no row sends from `OnAskForSync`, adds a "synced" flag or its own lock — all shared
  server state is under `GameDataManager.StateLock`.
- Section `cars` versions: v1 contract, v2 row 1, v3 row 4 (`Details`); row 2's `Place`/`CarData` are additive.
- Snapshot vs live on the client: snapshot packets (between `SyncBegin` and `SyncEnd`) apply as soon as the garage
  is loaded and never wait for `IsInitialSyncFinished`. Row 6's `ClientScene.IsGarageReady` is true from the start
  of `CustomLoad` (before `AskForSync`); its `ClientScene.GarageBound` drops (or mirror-only updates) garage-bound
  live packets while away, queues them between `SyncEnd` and `SyncAck`, and applies them otherwise.
- Car lifecycle on the server (row 1): every loader record is created by `CarPartsStore.RegisterSpawn` and removed
  by `ClearLoader(loader, reason)` (`Deleted`, `Parked`, `JobEnded`, `SpawnerLeft`), which raise `SpawnRegistered`
  / `LoaderCleared`. Later rows subscribe instead of being called: row 2 (lift reset, unparked car back to parking
  on `SpawnerLeft`), row 3 (lost job car reopens its order). Unpark = `RegisterSpawn`, park = `ClearLoader(Parked)`,
  job end = `ClearLoader(JobEnded)`; row 4 purges stale details lazily.
- Baselines: `CarPartsSync.UploadBaseline(loaderId)` after unpark (row 2), `PrepareJob` (row 3) and an engine swap
  (row 5b, after `RebuildRegistry`); each upload also triggers row 4's full details snapshot. Engine swap is stored
  and replayed by row 1 (blocked while connected if its spike finds no clean replay).
- Parking API (row 2): `CarParkRequest { RequestId, CarLoaderID, PreferredSlot, Car, Price }`; `CarLoaderID = -1` is
  for cars bought outside the garage (row 6 only) and is rejected (`ParkingFull`, `NoMoney`, `Invalid`) without a
  give-back; the answer is `CarParkResult`, raised on the client as `ParkingSync.ParkResultReceived`. Customer cars
  cannot be parked while connected.
- Inventory: UID-idempotent ADD is row 1's; `ItemActionType.Update` is row 5a's.
- Car-effect results: welder, interior condition and `PartScript` dust → `CarPartsSync.MarkDirty` (row 1); `CarPart`
  dust, wash, paint, bonus parts → `CarDetailsSync.MarkDirty`/`FlushNow` (row 4); dyno → row 13 (trigger in 5b).
  Headlights (`LightsOn`) and bonus parts are row 4 sections.
- Money, scrap, level, XP and skills are SHARED. Per-player server data is only identity, name, position/scene
  (seat/engine are live only). Steam stats/achievements of a finished job go to every connected player (row 3).
- Tutorial is disabled in multiplayer games; story missions sync like orders; order expiry pauses while the
  server is empty; no new orders while nobody is in the garage; a lost job car reopens its order with the
  original time; cars bought outside the garage go to shared parking; the balancer minigame is kept and locks the
  balancer for others while open.
- Random values (spawned car rolls, order generation, payout): one client computes, the server decides which
  result counts and stores it (interim until row 16's server-side logic).
- Row 6's "return to the garage = late join": results produced away (test drive/path) are sent on
  `ClientScene.LeavingScene` before the return snapshot — row 13 owns that and the dyno values.
- The order generator (row 3) is elected among `InSession` clients whose `PresenceRegistry` scene is `Garage`.
  Keep the election open for a server-hosted generator client as a candidate with the highest priority (see the
  generator-client spike).
- Claims/reservations (row 1 parts, row 3 order claims, row 5a balancer, row 13) are released on
  `PresenceEvents.Left`/`SceneChanged` away from the garage (row 6 publishes them).
- Harness: verbs are globally unique (`Commands.Discover` throws on duplicates); each helper has one owner — row 7:
  `Send-ServerCommand`, `Wait-ServerLog`, `Stop-/Start-TestServer`, `to-menu`, `stats-add`; row 6: `teleport`,
  `travel`, `Wait-HarnessDump`; row 1: `car-spawn`, `car-ready`, `car-hold`; row 2: `net-hold`, `park`; row 5a:
  `tool-hold`, `Wait-HarnessDumpsEqual`; row 8: `Start-TestServer -Arguments`, `*.launch.psd1`,
  `Status.lastDisconnect`; row 12: the `# run-all: skip` marker (`release-smoke` is not in `Run-All`); row 14:
  `resync [force]` (later rows use it instead of an in-place `AskForSync`).
- Guard (row 14a): default deny while connected; a row adds its `GuardRules` entries in its merge commit and its
  scenario runs with the guard on `Enforce` (`guard-allow` for features not yet allowed). Other prefixes of this mod
  on a guarded method take `__runOriginal` (row 6's `SceneHooks`).
- Handshake and refusals (M1): `DisconnectReason` is append-only — 7's base set, then 8's `ServerFull`,
  `WrongPassword`, then 9's `GameVersionMismatch`, `ModMismatch` (DLC never refuses: row 9 tracks the shared DLC
  set, rows 1, 2, 5a block DLC content outside it, see INTEGRATION.md); every refusal goes through row 8's
  `Server.Refuse` and is shown by row 8's `ConnectionStatus`/`ConnectionMessages`; the client builds its
  `ConnectPacket` only in row 9's `ConnectPacketFactory`; versions come from row 12's `BuildInfo`.
- Client preferences live in MelonPreferences category `CMS21Together` (guard: `CMS21Together_Guard`); hotkeys F7
  resync, F8 bug report (row 14), F9 session panel (row 8), F5 only as an opt-in dev key.
- Bug reports: one layout and redaction rule for row 14 (d)'s in-game bundles and row 12's `Collect-Logs.ps1`
  (INTEGRATION.md); secrets are `token`/`password`/`secret`/`key` entries (not `*Hotkey*`), `player.json` and
  `players[].Key`; the server writes logs to `Log\`.

Boundaries: a change only syncs what its row owns. When it needs something owned by another change,
it says so in its design as an assumption/dependency instead of implementing it.

## Working rules (for autonomous sessions)

- **One change in flight.** Work on a branch per change (`change/<name>`); rebase on `main` before merging.
  `PacketTypes` and `SyncOrder` are append-only, so parallel branches do not renumber each other.
- **Done = scenario + regression.** A feature is done when its scenario in `tools/test-env/scenarios/` passes in
  two instances *and* the full regression run (every existing scenario, `Run-All` — to be added in M0) passes.
  Record the run id and result in `STATUS.md`. A scenario that fails once and passes on rerun is logged as
  flaky in `STATUS.md`, not ignored.
- **Commits**: one conventional commit per finished task group; check off `tasks.md` items in the same commit.
- **Change too big?** If a change will not fit its size estimate by more than ~50 %, or a task group fails
  three sessions in a row: merge the task groups that already pass (unfinished behaviour stays blocked by the
  row 14a guard or a config flag defaulting to off), split the rest into a follow-up change (`<name>-2`), add it
  to this roadmap and note it in `STATUS.md`. Do not switch design approach without the user (QUESTIONS.md).
- **Questions**: design questions that need the user go to `QUESTIONS.md` with a default; park the task and take
  the next one.
- **Session notes** go to `STATUS.md` (newest first): what was done, scenarios run, what is next.
- **After merge**: archive the OpenSpec change (`openspec archive <name>`) so `openspec/specs/` holds the
  current behaviour, and update the Status section above.
- **Game version**: CMS21 gets no more updates (CMS 2026 replaces it); record the game version once in `STATUS.md`.
  If an update appears anyway: rerun the regression and regenerate the server database before anything else.
- **Draft ahead.** While a milestone is being implemented, draft the next milestone's undrafted rows (M1 needs 8
  part 1, 9 part 1, 12 part 1, 14a — all drafted and integrated 2026-10-06) with the same draft → review → integration pass; never start a row whose
  change is not drafted and reviewed.
- **Scale lane (row 11).** `Run-All -Lanes 3` (`scale-connect`, `soak` 10 min, `latejoin-full`, `storm`; about
  45 minutes, lanes 1 and 2 wait meanwhile) runs when server, Core or session code changed, and before each
  milestone from M5 on. A long soak (`Run-Soak.ps1 -Hours 2` or more, storms every 20 minutes) runs before M5 closes
  and before each release, only when the user starts it or says yes for that night, never while the user is at the
  PC (`Run-Soak.ps1` waits for 10 idle minutes). A failure is reproduced from its run folder (seed, `actions.jsonl`
  with `-Replay`, `deployed.json`) and fixed in the owning row's code.
- **Unattended-run safety.** `Run-Session.ps1` must abort (not wait) when Steam shows the "KickingOtherSession"
  prompt (the user forgot offline mode on another device) — port the check from LoadOptimizer's `testrun.sh`
  (M0 task). If the regression run is red and two attempts to fix it fail, stop feature work, log it in
  `STATUS.md` and send a push notification. Never leave game or server processes running at the end of a session.
- **Milestone definition of done**: all rows of the milestone merged; full regression green; a dev zip built with `tools/release/Build-Release.ps1`
  and `release-smoke` green on it, `docs/try-it.md` updated;
  a short "what to try" checklist for the user in `STATUS.md`; known gaps listed. The user's playtest with
  friends closes the milestone; bugs found go to `QUESTIONS.md` under a "Playtest findings" heading (or a
  `BUGS.md` if they outgrow it) and are fixed before the next milestone's rows start.
