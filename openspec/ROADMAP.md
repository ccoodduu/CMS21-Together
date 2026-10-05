# Roadmap

Goal: a playable co-op session on the dedicated server (Dev architecture) for 2–4 players: shared garage,
shared cars, shared jobs and tools, with state that survives a server restart and a late join.

## Status (2026-10-05)

Working on `main` (= upstream `Dev` a490cd5): DirectIP/Steam transport, connect + initial sync
(WorldState, GarageState, inventory), player movement/animation, money/exp/level/scrap/skills,
garage upgrades, server-authoritative inventory/shop/warehouse/exchange, car spawn/delete in the garage.
DTOs for body-part and sub-part updates exist (`CarBodyPartUpdatePacket`, `CarSubPartUpdatePacket`,
`ModGameState.CarState`) but nothing sends or handles them. Cars and jobs are not part of initial sync.

Verified with the harness: two clients connect to the local server, reach the garage and dump identical
stats/inventory/cars, and see each other.

## Changes, in implementation order

| # | Change | Owns | Depends on |
|---|--------|------|-----------|
| 1 | `sync-car-parts` | Mount/unmount and state (condition, examined, quality, dent, bolts in progress) of body parts (`CarPart`) and mechanical parts (`PartScript`) on cars in the garage, late-join replay of loaded cars + their parts | — |
| 2 | `sync-car-placement-and-lifts` | Lift states, moving a car between car loaders/places, garage parking lot, car transfer to/from parking | 1 |
| 3 | `sync-orders-and-jobs` | Order generation (server owns the order list), accept/decline, customer car spawn, task progress, ending a job (payout/exp), order expiry | 1, 2 |
| 4 | `sync-car-details` | Fluids, wheels/tires/rims and alignment, tuning parts, paint/livery/tint, dirt/wash state, license plates, headlamp alignment, car info (mileage etc.) | 1 |
| 5 | `sync-workshop-tools` | Tire changer, wheel balancer, engine stand, engine crane, spring clamp, oil bin, welder, repair table, brake lathe, battery charger, car wash, paint shop, interior detailing | 1, 4 |
| 6 | `sync-players-and-scenes` | Spawn positions, name tags, player in car seat, engine running/sound, scene tracking (who is where), visibility per scene, travel to junkyard/barn/auction/dealer and how purchases there flow into shared inventory/parking | — |
| 7 | `session-persistence-and-rejoin` | Server save format + versioning for all state above, autosave, identifying a returning player (per-player data), rejoin/late join end-to-end, client-side save safety (the client must never overwrite the player's own profiles) | all |

### Implementation order (revised after the drafts)

1. `session-persistence-and-rejoin` task group 2 (the contract: `ISaveSection`, `ISnapshotProvider`,
   `SyncOrder`, `SyncBegin/SyncEnd/SyncAck`, state lock) — every other change plugs into it.
2. `sync-players-and-scenes` spawn-position fix + presence roster (quick win, fixes the idle late-join bug).
3. `sync-car-parts` → `sync-car-placement-and-lifts` → `sync-orders-and-jobs` → `sync-car-details`
   → `sync-workshop-tools` → rest of `sync-players-and-scenes` → rest of `session-persistence-and-rejoin`.

### Integration notes (cross-change decisions to keep consistent)

- Late-join sends go through `ISnapshotProvider` in `SyncOrder` order, not straight into `OnAskForSync`
  (row 1 draft says otherwise; row 7's contract wins).
- Shared server state is guarded by one lock (`GameDataManager.StateLock` in row 1 = row 7's state lock).
- `ItemActionType.Update` and UID-idempotent inventory ADD: implemented by whichever of rows 1/5 lands first.
- Parking API (`CarParkRequest`, `CarLoaderID = -1` for cars arriving from outside) comes from row 2 and is
  used by rows 3 and 6.
- Car detail updates from tools use row 4's `CarDetailsUpdatePacket` / `CarDetailsSync.MarkDirty`.
- Exp ownership: see QUESTIONS.md #1 (row 3 assumed shared, row 7 per player).

Boundaries: a change only syncs what its row owns. When it needs something owned by another change,
it says so in its design as an assumption/dependency instead of implementing it.

## Working rules (for autonomous sessions)

- Work on a branch per change (`change/<name>`), merge to `main` when its harness scenario passes.
- A feature is done when its scenario in `tools/test-env/scenarios/` passes in two instances.
- Design questions that need the user go to `QUESTIONS.md`; park the task and take the next one.
- Session notes go to `STATUS.md` (newest first).
