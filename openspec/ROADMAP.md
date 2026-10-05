# Roadmap

Goal: a playable co-op session on the dedicated server (Dev architecture) for 2–4 players: shared garage,
shared cars, shared jobs and tools, with state that survives a server restart and a late join — installable
by a friend from a release zip and joinable without typing IDs by hand.

## Status (2026-10-05)

Working on `main` (= upstream `Dev` a490cd5): DirectIP/Steam transport, connect + initial sync
(WorldState, GarageState, inventory), player movement/animation, money/exp/level/scrap/skills,
garage upgrades, server-authoritative inventory/shop/warehouse/exchange, car spawn/delete in the garage.
DTOs for body-part and sub-part updates exist (`CarBodyPartUpdatePacket`, `CarSubPartUpdatePacket`,
`ModGameState.CarState`) but nothing sends or handles them. Cars and jobs are not part of initial sync.
The connect handshake checks the mod version only (`gameVersion` is sent but not checked). The client runs
the session in profile slot 4 with difficulty hardcoded to Normal.

Verified with the harness: two clients connect to the local server, reach the garage and dump identical
stats/inventory/cars, and see each other.

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

### Not yet drafted — needed for a finished product

| # | Change | Owns | Size |
|---|--------|------|------|
| 8 | `hosting-and-join-ui` | In-game multiplayer menu: host (start/stop the local dedicated server from the game), join by IP, join via Steam (friends list / invite / rich presence instead of a server ID), connection status and error messages, player name setting, version-mismatch message, new-session settings (difficulty), server password for DirectIP, in-game player list (name, scene, ping), join/leave notifications, kick. Today there is no UI at all: F5 = connect to 127.0.0.1, F6 = Steam to a hardcoded dev server ID. | L |
| 9 | `mod-compatibility` | Handshake checks: mod version, **game version** and **DLC set** (refuse or warn on mismatch); mod list + version exchange on connect; a game-data exporter that regenerates the server's `Database/*.json` after a game update (no exporter exists in the repo); modded items/parts from other mods (TK Aftermarket etc.; `RegisterModItem` exists for the shop), QoLmod settings that change game state, the user's own mods (LoadOptimizer, Lvx*). | M |
| 10 | `economy-audit` | Every money/scrap/XP path is server-authoritative: travel fees, selling cars, auction bids, barn/junkyard purchases, parking levels, paint/wash/welder costs, opening crates (upstream #94: money/level desync after crates). Rows 2/3/6 cover some; this row closes the rest (known gap from row 6: travel fees and car sales are undone by the next `WorldState`). | M |
| 11 | `multiplayer-soak-and-scale` | Harness with 3–4 instances, long scripted sessions (soak), bandwidth/CPU check, late-join time with a full garage + parking, disconnect storms. (Artificial latency/packet loss moves to M2, see below.) | M |
| 12 | `release-and-docs` | Release packaging (client zip: Mods/UserLibs; server zip), install + hosting guide (incl. moving a server save to another host), changelog, version bump policy, log collection for bug reports. | S (+S for the M1 dev build) |
| 13 | `sync-test-drive-and-diagnostics` | A car taken to the test track or test path (and dyno runs in the garage): the car is claimed by the driver while away, others see it as away and cannot edit it, and the results (examined/discovered parts, mileage, dyno measurements `EngineData.measured`) reach the server before the returning client applies the garage snapshot. Upstream's most reported car bug (#18, #83, #85, #95: repairs and job progress reset after a test drive). | M |
| 14 | `desync-detection-and-resync` | (a) Unsynced-action guard: while connected, actions whose sync has not landed yet are blocked with an on-screen message instead of silently desyncing (lets friends play between milestones). (b) Per-section state checksums from the server, client compares and logs/auto-requests a resync. (c) Manual resync key/button that reruns the late-join snapshot. (d) One-key bug-report bundle (client + server logs, dump). | M |
| 15 | `shared-outdoor-scenes` | Junkyard, barn and auction shared by everyone in them (user wants to scavenge together). Car selection runs on the server by adapting LvxBetterCarSpawns (LvxMagick; decompiled reference in `CMS21-TestInstalls\reference\LvxBetterCarSpawns-decompiled`): vehicle candidates per location, weighted selection, spawn history, all spawn points filled; clients load exactly the server's cars. Loose parts/items: the first visitor's generated layout is stored by the server and replayed to later visitors. Picking up parts and buying cars go through the server; remote players visible in the barn too. Needs the author's permission/credit before adapted code is published (user decides when to ask). Planned after M4. | L |
| 16 | `server-game-logic` | The game gets no more updates, so game logic moves to the server: prices/fees (feeds row 10), job payout/XP and order generation (replaces row 3's elected generator), random damage/colour of spawned cars (replaces row 1's spawner roll). Built from native code read with Cpp2IL/Il2CppDumper + Ghidra and game data exported to `Database/*.json` (shared exporter with row 9). Rows 1 and 3 keep their client-computed approach as the interim until this row lands. | L |

Backlog (not planned): import a single-player save as server start state; multiplayer tutorial (tutorial is
disabled in multiplayer games); driving sync
(test track / test path drives visible to others); garage decorations/customization; seasonal event garages
(Christmas/Easter/Halloween — multiplayer always loads the normal `garage`); sandbox mode; text chat (Steam/
Discord voice covers it); per-player inventory option (upstream #100); remote per-car engine sound beyond
row 6's simple loop; Linux/headless server.

## Milestones

Each milestone ends with something the user and friends can actually try. A milestone is done when its
definition of done (Working rules) holds. Rows listed as "part N" are split by their task groups.

| M | Name — what you can try | Rows |
|---|--------------------------|------|
| M0 | **Foundations** (no playtest) — harness infra, regression runner, sync contract, client save safety, atomic save + backups | 7 (groups 1, 2, 3, 6) |
| M1 | **Friends connect and see each other** — host + 1–2 friends join over Steam from a dev zip, walk around with name tags, see each other after a late join, shop/warehouse/garage upgrades with shared money; working on cars (and travel, until row 1 restores garage cars on return) is blocked by the guard; nobody's own saves are touched | 6 part 1 (spawn, roster, names, scene tracking + away/return), 8 part 1 (join without hardcoded ID, status/errors), 9 part 1 (mod + game version + DLC + mod list check), 12 part 1 (dev build zip), 14 (a) |
| M2 | **Work on one car together** — take a car from the parking onto a lift, strip and rebuild it together, travel to the junkyard (parts only) and come back to the same garage, restart the server, the car is still there; resync key fixes a broken car | 1, 2, 14 (b, c), harness latency/loss injection |
| M3 | **Run jobs together** — accept an order, diagnose (examine, test drive, test path), replace parts, fluids and tires, hand it back, payout shared; a late joiner mid-job sees the job | 3, 4, 13 |
| M4 | **The full workshop** — every tool, junkyard/barn/auction trips with car purchases landing in the shared parking, consistent money; host and join from the in-game menu | 5a, 5b, 6 part 2 (seat/engine, purchases outside), 10, 8 part 2 |
| M5 | **Robust sessions** — 3–4 players, multi-hour session, crashes and rejoins without loss | 7 rest (identity, rejoin end-to-end, server loss), 11, 14 (d) |
| M6 | **Release 1.0** — a friend installs from the zip and the guide alone, with the mods you play with | 9 part 2 (modded items, QoLmod), 12 part 2 |

### Implementation order

1. M0: `session-persistence-and-rejoin` groups 1–2 (the contract: harness server control, `ISaveSection`,
   `ISnapshotProvider`, `SyncOrder`, `SyncBegin/SyncEnd/SyncAck`, `GameDataManager.StateLock`) — every other
   change plugs into it; then group 6 (client save safety) and group 3 (atomic save, backups), because
   friends play from M1. Groups 4, 5, 7 (identity, join hardening, end-to-end scenarios) belong to M5.
2. M1: `sync-players-and-scenes` spawn fix + presence roster (fixes the idle late-join bug), names, scene
   tracking; `hosting-and-join-ui` part 1; `mod-compatibility` part 1; guard; dev zip.
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
| Test drive round trip: what the game saves before leaving and loads on return | Row 13 and row 6's "return = late join" | Start of M3 |
| Native decompile: set up Cpp2IL/Il2CppDumper + Ghidra, read `EndJob` payout and map the size of `OrderGenerator` | Decides how big row 16 is and whether row 3's elected generator can be skipped | M0, background |

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
- Claims/reservations (row 1 parts, row 3 order claims, row 5a balancer, row 13) are released on
  `PresenceEvents.Left`/`SceneChanged` away from the garage (row 6 publishes them).
- Harness: verbs are globally unique (`Commands.Discover` throws on duplicates); each helper has one owner — row 7:
  `Send-ServerCommand`, `Wait-ServerLog`, `Stop-/Start-TestServer`, `to-menu`, `stats-add`; row 6: `teleport`,
  `travel`, `Wait-HarnessDump`; row 1: `car-spawn`, `car-ready`, `car-hold`; row 2: `net-hold`, `park`; row 5a:
  `tool-hold`, `Wait-HarnessDumpsEqual`.

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
  row 14 guard or a config flag defaulting to off), split the rest into a follow-up change (`<name>-2`), add it
  to this roadmap and note it in `STATUS.md`. Do not switch design approach without the user (QUESTIONS.md).
- **Questions**: design questions that need the user go to `QUESTIONS.md` with a default; park the task and take
  the next one.
- **Session notes** go to `STATUS.md` (newest first): what was done, scenarios run, what is next.
- **After merge**: archive the OpenSpec change (`openspec archive <name>`) so `openspec/specs/` holds the
  current behaviour, and update the Status section above.
- **Game updates**: the test installs do not auto-update. When CMS21 updates, the first task is: rerun the
  regression, regenerate the server database (row 9 exporter), record the game version in `STATUS.md`.
- **Milestone definition of done**: all rows of the milestone merged; full regression green; a dev zip built;
  a short "what to try" checklist for the user in `STATUS.md`; known gaps listed. The user's playtest with
  friends closes the milestone; bugs found go to `QUESTIONS.md` under a "Playtest findings" heading (or a
  `BUGS.md` if they outgrow it) and are fixed before the next milestone's rows start.
