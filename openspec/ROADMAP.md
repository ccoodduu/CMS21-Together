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
| 4 | `sync-car-details` | Fluids, wheels/tires/rims and alignment, tuning parts, paint/livery/tint, dirt/wash state, license plates, headlamp alignment, car info (mileage etc.) | 1 | L |
| 5 | `sync-workshop-tools` | Tire changer, wheel balancer, engine stand, engine crane, spring clamp, oil bin, welder, repair table, brake lathe, battery charger, car wash, paint shop, interior detailing, tool positions | 1, 4 | XL |
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

Backlog (not planned): import a single-player save as server start state; multiplayer tutorial (tutorial is
disabled in multiplayer games); shared non-garage worlds (same junkyard/barn for everyone); driving sync
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
| M4 | **The full workshop** — every tool, junkyard/barn/auction trips with car purchases landing in the shared parking, consistent money; host and join from the in-game menu | 5, 6 part 2 (seat/engine, purchases outside), 10, 8 part 2 |
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
5. M4: `sync-workshop-tools` (common tools first: tire changer, balancer, oil bin, engine crane/stand, spring
   clamp; then the rest) → rest of `sync-players-and-scenes` → `economy-audit` → `hosting-and-join-ui` part 2.
6. M5, M6 as in the table.

### Early spikes (de-risk before the row starts)

| Spike | Why | When |
|-------|-----|------|
| Part identity: load every car model on both clients, dump part keys, diff | Row 1 assumes every client builds the same part hierarchy for a car (DLC, mod parts, config-dependent `AddPartIfShould`); everything after row 1 sits on it | M0, background |
| Steam join over the internet with one real friend: does the anonymous game-server SteamID change per start, does relay (SDR) work without port forwarding, can a friend join from the Steam friends list | Cannot be tested in the local harness; upstream #93, #97 | M1, needs the user + a friend |
| In-game UI technology on IL2CPP (IMGUI vs. cloning the game's UI like 0.4.17's `NewUI`) | Row 8 and the guard messages need it | M1 |
| Hook trace per change (logging-only Harmony patches, verify each hook fires once per action) | IL2CPP-inlined methods never hit their patch; coroutine methods only fire on start | First task of every change |
| Test drive round trip: what the game saves before leaving and loads on return | Row 13 and row 6's "return = late join" | Start of M3 |

### Integration notes (cross-change decisions to keep consistent)

- Late-join sends go through `ISnapshotProvider` in `SyncOrder` order, not straight into `OnAskForSync`
  (row 1 draft says otherwise; row 7's contract wins).
- Shared server state is guarded by one lock (`GameDataManager.StateLock` in row 1 = row 7's state lock).
- `ItemActionType.Update` and UID-idempotent inventory ADD: implemented by whichever of rows 1/5 lands first.
- Parking API (`CarParkRequest`, `CarLoaderID = -1` for cars arriving from outside) comes from row 2 and is
  used by rows 3 and 6.
- Car detail updates from tools use row 4's `CarDetailsUpdatePacket` / `CarDetailsSync.MarkDirty`.
- Money, scrap, level, XP and skills are SHARED (user decision 2026-10-05). Per-player server data is only
  identity, name, position/scene, seat. Row 7's per-player progression must be removed.
- Tutorial is disabled in multiplayer games; story missions sync like orders; order expiry pauses while the
  server is empty; cars bought outside the garage go to shared parking; the balancer minigame is kept.
- Row 6's "return to the garage = late join" discards local garage state on return. Anything a player
  produced away from the garage for a garage car (test drive/test path results) must be sent before the
  snapshot is applied — row 13 owns that; dyno results are row 13's (answers row 4's open item A5).
- The order generator (row 3) must be a client whose scene is `Garage`; once row 6's roster exists the server
  elects among garage clients only.
- Claims/reservations from any row are released when their holder disconnects or leaves the garage (row 6
  publishes the event; rows 1, 5, 13 consume it).
- Every handler for garage-bound packets uses row 6's `ClientScene.IsGarageReady` check.

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
