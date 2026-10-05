# Review: sync-workshop-machines

This change was split out of `sync-workshop-tools` by the integration pass (user decision 2026-10-05). The review
below was written for the combined change and is kept as history; findings about the car-effect tools (engine
crane, engine swap, welder, car wash, interior detailing, oil bin, car paint, dyno) now belong to
`sync-workshop-car-tools`.

## Review of the combined `sync-workshop-tools` (history)

Reviewed 2026-10-05 against ROADMAP integration notes, QUESTIONS.md answers, the decompiled stubs and the six other
drafts. All hooks named in proposal/design were checked with `members.sh` (all exist; signatures corrected where noted).
`openspec validate sync-workshop-tools --strict` passes.

### Findings

### Blocker

1. **Engine crane out/in bypassed the part transaction.** The draft synced engine out/in as "part state from row 1,
   engine group via the inventory flow". Row 1's hook table has no `NotificationCenter.ActionUnMountGroup` /
   `ActionInsertEngineToCar`, so the group ADD and the part change were separate, and two players craning at once
   produced two engine groups (new UIDs). *Changed:* D8 makes engine out/in a row-1 `CarPartsChange`; task 1.2
   checks for it and parks 16.x if missing. **Needs a fix in `sync-car-parts`** (see cross-change list).
2. **Engine stand part overlay had no arbitration.** `ToolPartUpdate` was last-write-wins while unmounting a part
   creates an inventory item, so a race duplicated it, and row 1's own hooks would also fire on stand parts.
   *Changed:* D7 reuses row 1's `PartTransaction`/`InventoryDelta`/precondition check rooted at `engineGameObject`
   (`ToolPartChangePacket`); spec scenario and task 11.3 added. Needs two small things from row 1.
3. **Engine swap stored nowhere / applied too late.** The draft assumed row 1 stores `EngineSwap`; it does not, and
   a swap changes part keys under the engine, so it must be applied before row 1's registry is built.
   *Changed:* D9 treats a swap as a re-baseline (`EngineSwap` in row 1's per-loader entry, `SpawnSeq` bump,
   `UploadBaseline`); this change only detects and triggers. Settles `sync-car-details` A5 for engine swap.
4. **Persistence/late join did not follow the row-7 contract** (coordinator update included). The draft added
   `ToolsState` straight into `OnAskForSync` and `SaveSession`, used `IsInventorySynced` and `CountItems`.
   *Changed:* `WorkshopToolsSection` (`[SessionSection]`, own `ISaveSection` key `workshop-tools` v1, thin adapter over
   `ModGameState.ToolsState`; `ISnapshotProvider` at SyncOrder 300 whose `SendSnapshot` returns 1), client reports
   `SyncTracker.Applied("workshop-tools")`, no own synced flag, handlers rely on `GameDataManager.StateLock` (D3 of
   row 7). Restart test uses row 7's `Send-ServerCommand save`/`Stop-TestServer`/`Start-TestServer` inside one run
   (the old "restart between two runs" could not work: `Run-Session.ps1` restores `Saves`).

### Major

5. **Loser compensation duplicated items.** Re-adding a rejected put's item duplicated it when both players put the
   *same* item; "take loser does nothing" duplicated new-UID take-offs (engine stand, confirmed by FixForTogether).
   *Changed:* D3.2 rules (re-add only if the item is nowhere; take loser deletes its own new UIDs) and a server check
   that a UID is not held by another machine; spec scenario "Same item put on two machines".
6. **Wrong owners for car-effect results.** Interior detailing was sent to row 4 (row 4 D6 says it is row 1's);
   welder used a "body-part sender" that row 1 removed. *Changed:* D8 table now names `CarPartsSync.MarkDirty(loaderId, part)`,
   `CarDetailsSync.MarkDirty(CarLoader, CarDetailSection, …)` + `FlushNow` with the sections row 4 D6 expects.
7. **Dyno was a non-goal** although row 4 A5 assigned it here. *Changed:* trigger hook `CarLoader.MeasurePower()`
   (fallback `DynoManager.CloseDyno()`); the values are stored by ROADMAP row 13 `sync-test-drive-and-diagnostics`
   (decision 2026-10-05), so group 21 waits for row 13. Requirement scenario added.
8. **Scene handling contradicted row 6.** The draft kept updating the mirror away from the garage; row 6 D6 drops
   garage packets while away and resyncs on return. *Changed:* D12 drops while away, queues (does not drop) during
   initial sync, return = full snapshot; spec scenario "Return from another scene" and task 22.2 step added.
9. **Local-save machine calls could reach the server.** The game's own `SetGroupOn…(…, instant: true)` during garage
   load would fire the hooks. *Changed:* D4 gates sends on sync finished and `ClientScene.IsGarageReady`.
10. **Race tests were not deterministic** ("within the same second" on localhost rarely races). *Changed:*
    `tool-hold on|off` buffers incoming tool packets so both clients act on a stale view; `Send-HarnessCommandPair` dropped.
11. **IL2CPP feasibility gaps.** IEnumerator hooks fire at iterator creation and the Il2Cpp iterator cannot simply be
    wrapped; small methods may be inlined. *Changed:* end-of-action watchers (D8), poll fallbacks for angle/charger
    (D5), `FinishBalanceInternal` fallback, explicit coroutine starts for `SetGroupOnEngineStand`/`SwapEngine`,
    and spike 1.4 for remote-apply primitives. `TakeOffEngineFromStand()` has no stand argument, so the stand now
    comes from the `ClearEngineStand` prefix.
12. **"Late join with own save" scenario was not drivable.** *Changed:* `tool-local-put` (hooks off) + `tool-resync`.

### Minor

13. `ModSubPartState` does not exist → `CarSubPartUpdatePacket` (row 1's nested record).
14. `SetGroupOnTireChanger`'s third parameter is `connect`, not `mounting`; balancer field is `groupOnWheelBalancer`.
15. `ItemActionType.Update` / idempotent ADD: row 1 does not add them, so the open question is closed — owned here.
16. Spec: added take-off by another player for the stand, minigame-window-closed case, part paint, swap, dyno,
    disconnect with an item on a machine; "no double cost/XP" made explicit.
17. Task 1.3 needs a human (normal UI play); marked as such. Harness commands call logic methods directly and cannot
    detect inlining, so this spike stays manual.
18. Server occupant check simplified to kind (Item/Group) + optional ID prefix table from `item_database.json`.

### Updates after the other reviews (2026-10-05)

- **Row 7 contract** (coordinator): `WorkshopToolsSection` is its own versioned `ISaveSection` and the
  `ISnapshotProvider` at slot 300 (`SendSnapshot` returns 1); no hand-made `OnAskForSync`/`SaveSession` calls; the
  client counts with `SyncTracker.Applied("workshop-tools")` instead of own synced flags; handlers rely on
  `GameDataManager.StateLock`. Tasks 2.4, 3.3, 4.4 and 22.2 changed.
- **Row 4 final names** (coordinator): interior detailing is split: `CarPart` dust → `CarDetailsSync.MarkDirty(…,
  BodyCosmetics)`, part condition and `PartScript` dust → `CarPartsSync.MarkDirty` (row 1 now carries dust as an
  attribute change, which closes the dust-ownership gap). Row 4's probe 1.2 decides the fields; task 1.2 copies them.
  Oil bin needs no `MarkDirty` (row 4 polls fluids), only an optional `FlushNow`. The welder row never used
  `CarBodyPartUpdate` after this review; it calls `CarPartsSync.MarkDirty`, which ends in row 1's `CarPartsChange`.
  Engine swap: stored by `sync-car-parts`, triggered by the engine crane (D9), as row 4 now says.

### Scope: recommend splitting into two changes

The change is large (14 tool ids + positions + item processing, 22 task groups) and its two halves have different
dependencies. Proposed split (not done here):

- **`sync-workshop-machines`** — framework (groups 1–5), slot machines (6–12), tool positions (13), repair table and
  part painting (14–15), `ItemActionType.Update`, idempotent ADD. Needs only the inventory and row 7's contract, so it
  can land right after row 1 and does not wait for row 4.
- **`sync-workshop-car-tools`** — engine crane and swap, car paint, car wash, interior detailing, oil bin, welder, dyno
  (16–21). Thin glue over rows 1/4 APIs; blocked by the row-1 items below.
  Tasks are already grouped so the split is a move of groups 16–21 plus the `tools-car-effects` scenario.

### Cross-change conflicts to fix elsewhere

- `sync-car-parts` design D2/tasks 3.6–3.7: add `NotificationCenter.ActionUnMountGroup(InteractiveObject)` and
  `ActionInsertEngineToCar(GroupItem)` (and probably `MountGroup(long)`) to the transaction hooks; ignore `PartScript`s
  outside a car registry; allow a `PartTransaction` for a non-car root (engine stand); add `EngineSwap` to the
  per-loader entry with re-baseline on swap; non-goals line "engine stand/crane … row 5" should say crane out/in is row 1.
- `sync-car-details` A5 is now settled there (engine swap: storage row 1, trigger here; dyno: row 13). Open point for
  the coordinator: the coordinator message says dyno results are "not yours", while this folder (edited 2026-10-05)
  keeps the `MeasurePower` trigger here with storage in row 13. Kept as on disk; if row 13 takes the trigger too,
  delete group 21, the dyno row in D1/D8 and the spec scenario "Dyno run".
- `sync-players-and-scenes` D6: `IsGarageReady` drops packets "while initial sync is unfinished", but row 7 sends live
  changes right after `SyncEnd`, before the client finishes applying — those would be lost for every row. Should be
  "queue during sync, drop only while away". Its D3 mentions "tool claims of sync-workshop-tools": there are none.

### Open questions for the user

1. **Taking a wheel off the balancer while someone plays the minigame** — allowed (their window closes, the wheel
   leaves unbalanced), or blocked while the minigame is open? *Default: allowed* (no extra lock state).
2. **Split into two changes as above?** *Default: yes.*
3. **Task 1.3 needs ~10 minutes of you using each machine with `tool-trace` on.** *Default: schedule it before
   group 6; groups 2–5 proceed without it.*

## Integration pass (2026-10-06)

- Split: groups 1–15 and the machine half of 22 of `sync-workshop-tools` became this change (spikes, framework,
  slot machines, engine stands, tool positions, repair table, part painting, verification); groups 16–21 moved to
  `sync-workshop-car-tools`. Capability renamed to `workshop-machines-sync`. Scenario `tools-car-effects` moved out.
- Idempotent inventory ADD: owned by `sync-car-parts` (lands first); this change owns only `ItemActionType.Update`
  (D3.1, task 3.2, 4.2, proposal). Answers the old open question in favour of row 1.
- Wheel balancer (user decision 2026-10-05): locked while one player has the minigame open. New `ToolClaim`/
  `ToolClaimUpdate` packets, server reservation released on finish/cancel/disconnect/leaving the garage/300 s,
  `claimedBy` in the dump, D2 rule 4, D6, D12, spec requirement rewritten (old "window closes when someone takes
  the wheel" removed), tasks 3.1, 7.2–7.3, harness `tool-balance-open`/`tool-balance-cancel`.
- Scene handling: `ClientData.LocalScene` → `ClientScene.IsGarageReady`; handlers go through row 6's
  `ClientScene.GarageBound` (mirror only while away, queue between `SyncEnd` and `SyncAck`) (D10, task 4.3).
- `Wait-HarnessDumpsEqual` is built on row 6's `Wait-HarnessDump` (task 5.3).
- Dependencies: row 1's non-car-root `PartTransaction` and "ignore `PartScript`s outside a registry" are now in
  `sync-car-parts` D3/D11 (task 1.2 only checks them). This change no longer depends on `sync-car-details`.
- Open question "split into two changes?": answered yes. "Taking a wheel off during the minigame": replaced by the
  lock.
