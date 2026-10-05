# Review: sync-car-details (2026-10-05)

Hook and field names were spot-checked against the stubs in `decomp/Assembly-CSharp-firstpass` (CarLoader,
FluidsData/FluidData/CarFluid, Wheel, WheelsAlignment, HeadlampAlignment, CarInfoData, LicensePlatesData, CarPart,
GearboxHandle, PartModule/EcuModule/CarbModule, TuningData, GearboxTab/EcuTuning/CarbTuning,
Wheels-/LampAlignmentWindow, TintingWindow, WindowTintManager, PaintshopManager, CarWashLogic, NewCarData, enums).
All exist. The findings below are about wrong types, the integration contract and design gaps.

## Findings

| # | Sev | Finding | Action |
|---|-----|---------|--------|
| 1 | blocker | Late join sent details "right after each car" from `OnAskForSync`, A1 assumed a per-car slot in `sync-car-parts`, and the client gated `IsInitialSyncFinished` on its own queue. This contradicts row 7's contract (provider at `SyncOrder` 150 after all cars, items counted by `SyncTracker`, `SyncAck`). | D10, spec and tasks now use `[SessionSection] CarDetailsSnapshot : ISnapshotProvider` (150, returns the item count), `SyncTracker.Applied("car-details")`, and no flags of our own. Done after the coordinator's contract update. |
| 2 | blocker | Persistence went "through the existing JSON". The row 7 table puts `CarState.Details` in the `cars` save section and gives `car-details` no save section. | Data stays in `CarState.Details`, saved in `cars`, with a +1 version step and a no-op `Migrate`. `car-details` is a snapshot provider only. All handlers and the tick run under `GameDataManager.StateLock`. |
| 3 | major | Fluids was one whole section, so two players pouring oil and coolant at once overwrote each other. The loser's pour was also reset by the incoming apply. | Keyed sections (Fluids per reservoir, BodyCosmetics per part, Tuning per module) are now merged per entry, and only changed entries are sent. Added spec scenario "Two fluids at once" and a scenario step. |
| 4 | major | Stale-update check used `CarToLoad`, so a same-model respawn, park or unpark slipped through. Reset depended on hooking `HandleCarSpawnRequest`, which unpark (row 2) and job spawns bypass. | Records and packets carry `sync-car-parts`' `SpawnSeq`. Stale records are purged lazily, with no spawn/delete hooks. Added spec scenario "Update for a replaced car". |
| 5 | major | The spawn snapshot waited for a "car settled" signal that `sync-car-parts` does not define. It also tied the spawner to the `LoadCarHook` path, which misses unpark and job prep. | The spawner now sends `IsFull` after every part-baseline upload (`BaselineUploaded`). Rows 2 and 3 already re-upload the baseline after their post-load steps. |
| 6 | major | The D7 request path had three ad-hoc triggers. | Replaced by one server tick rule: a car with a baseline but no valid record for 10 s triggers a request to the next `InSession` client. It covers a spawner disconnect and old saves. |
| 7 | major | Interior detailing ownership conflicts. This draft said it goes through `sync-car-parts`; `sync-workshop-tools` D7 says it goes through `sync-car-details`. | Probe 1.2 now records what `TweenInteriorConditionAndDust` changes. D6 splits the result: `CarPart` dust goes to `MarkDirty(BodyCosmetics)`, and condition plus `PartScript` dust go to `CarPartsSync.MarkDirty`. |
| 8 | major | `ModCarWheel` used ints for `Wheel.Width/Size/Profile`, which are floats in the game. Wheel hooks on small setters (`SetWheelSize`/`SetET`/`SetRim`/`SetTire`) risk IL2CPP inlining and miss the tire-mount path. | The DTO now holds `SetWheelSize`'s ints (as `NewCarData` does). Wheels moved to the 1 Hz poll and the four hooks were dropped. |
| 9 | major | A1 depended on four undefined `sync-car-parts` signals, including a server-side `OnBodyPartReplaced`. | Narrowed to client `IsReady`, `SpawnSeq`, `BaselineUploaded` and `LocalPartsCommitted`. Remount cosmetics now come from the mounting client's `MarkDirty` on `LocalPartsCommitted`, so the server API is gone. |
| 10 | minor | A5 (engine swap, dyno, `LightsOn`) was open. | Settled to match `sync-workshop-tools` assumption 3. Engine swap: `sync-car-parts` stores it, `sync-workshop-tools` triggers it. Dyno results and `LightsOn` go to the backlog explicitly. `sync-players-and-scenes` declines `LightsOn` too, because it is car state, not presence. If it is wanted later, it fits this change as one field in Info (hook `CarLoader.SwitchCarLights`). Note: `EngineData.measured` exists, but `NewCarData` has no `measured` field. |
| 11 | minor | Own echo with the latest seq was "no effect", so a server-clamped value stayed unclamped on the sender. | The latest echo is now applied, as a no-op unless the server clamped it. |
| 12 | minor | The tint hook on `TintAction` alone could miss restore/reset actions, and the cancel path was not tested. | Added a `HideAction` postfix (it runs after the backup restore, and the value diff filters). Added `cardetails-ui tint-cancel` to the scenario in place of the vacuous paint-shop preview step. |
| 13 | minor | Tasks were 179 lines and verified with commands defined in later tasks. Hook verification was manual, and the restart check was manual. | Tasks rewritten to 5 groups and about 95 lines, ordered so each verify uses existing commands. Added harness `cardetails-ui` (drives the commit methods), `-randomize`, `-hold` and `Wait-ServerLog`/`Send-ServerCommand` checks. The restart check is now in `car-details-latejoin` via row 7's server helpers. |
| 14 | minor | `SetRandomMileage` has no parameterless overload (it takes `SceneType`/`AuctionType`). Struct fields need copy-and-assign. Plates may preview while browsing textures. | Wording fixed. The plates preview is a risk with a one-time manual check in 4.6. |

## Cross-change conflicts to fix elsewhere

- `sync-car-parts/design.md`:
  - Expose client `CarPartsSync.IsReady(loaderId)`, the loader's `SpawnSeq`, and the events `BaselineUploaded(loaderId)` and `LocalPartsCommitted(loaderId, keys)`.
  - Store `EngineSwap` in the per-loader entry and apply it before building the registry on late join. Nothing covers it today, and `sync-workshop-tools` assumes it.
  - D7 applies body colour/paint/livery on every record apply. It should only do this when the mount state changes, so that attribute-only changes do not overwrite BodyCosmetics.
- `sync-workshop-tools/design.md` D7 table: change "Interior detailing → interior condition/dust (`sync-car-details`)" to the split in this change's D6. Its welder row still names `CarBodyPartUpdate`, which `sync-car-parts` removes.
- `sync-car-placement-and-lifts`: unpark has to go through `sync-car-parts`' spawn bookkeeping (new `SpawnSeq`, `SpawnedBy` = unparking client, baseline upload). Without that, neither parts nor details get a baseline for unparked cars.
- `session-persistence-and-rejoin` D2 table: the `cars` section's version history must allow row 4's no-op step (`Details` added). It also needs to say whether rows 1 and 4 share one bump or take two.

## Open questions for the user

1. **Bonus (visual tuning) parts** (`CarLoader.bonusParts`: spoilers/bumpers, painted by the paint shop) are not covered by any draft. The roadmap gives row 4 "tuning parts". Recommended default: add a ninth `BonusParts` section here (state and paint, applied with `SwapBonusPart`/`TakeOffBonusPart`). The inventory side would use the normal inventory flow, and a rare double-take race is accepted.
2. **Dyno results and headlights on/off** are not synced, and are moved to the backlog. Recommended: accept, because they affect neither jobs nor money.

## Integration pass (2026-10-06)

- `LightsOn` is a feature here (user decision): `ModCarInfo.LightsOn`, Info poll + `SwitchCarLights` postfix/apply,
  probe and harness `cardetails-lights`, spec scenario; "backlog" wording removed.
- Bonus (visual tuning) parts accepted (user decision): ninth section `BonusParts` (D2, D4, D10 order, paint shop
  `MarkDirty`), probe, harness `cardetails-bonus`, spec scenario; A8 closed.
- `cars` section bump is explicit: v2 (`sync-car-parts`) → v3 with `Migrate(data, 2)` (D8, task 2.2).
- Prerequisites/Context name row 7 groups 1–2; tool references point to `sync-workshop-car-tools` /
  `sync-workshop-machines`.
