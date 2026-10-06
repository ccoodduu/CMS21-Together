# Review: sync-car-parts (2026-10-05)

Checked against ROADMAP integration notes, QUESTIONS.md answers, the contract in
`session-persistence-and-rejoin` (groups 1–2, as revised), the other five drafts, the decompiled stubs and the repo.
All hook methods in the D2 table and the D7 setters exist in `Assembly-CSharp-firstpass` with the named signatures
(`PartScript`, `CarLoader`, `CarPart`, `ChoosePartUpWindow`, `CarLoaderOnCar`, `CarLoaderPlaces`, `UIManager`,
`MountObjectData`). `openspec validate sync-car-parts --strict` passes.

## Findings

### Blocker
1. **Own join pipeline instead of the contract.** Snapshots were sent straight from `OnAskForSync`, with an own
   `ClientData.IsCarsSynced` flag and a "10 s without progress" timeout. Changed: `CarsSnapshotProvider` in slot
   `cars` (100) returns the car count for `SyncEnd{Items}`; the client calls `SyncTracker.Applied("cars")` per
   `Ready` car; the contract's `SyncAck` and 30 s timeout apply. `CarPartsSnapshot` gets `SnapshotId` (the tracker
   ignores other ids). (Coordinator contract update, design D9, tasks 2.6/4.2.)
2. **Own state lock.** Task 2.1 created `GameDataManager.StateLock`. Changed: the contract creates it and holds it
   around dispatch, `Client.Disconnect` and ticks; this change only uses it (claim expiry runs in `TickServer`).
3. **`CarState` shape change without a section version.** Changed: the `cars` save section goes v1 → v2 with
   `Migrate` (v1 cars become entries without baseline, `Details` copied unchanged) and `Load` drops cars without
   baseline (Migration Plan, task 2.1). `sync-car-details` bumps it once more with its own step (coordinator).

### Major
4. **Mount race duplicated items.** A rejected mount re-added every consumed item, also one another player had
   already used. Changed: `CarPartsChangeResult.RestoreUids`; only those are re-added (D4, task 3.7).
5. **IL2CPP: Unhollower wrappers as dictionary keys.** A new wrapper per native fetch breaks `PartScript → key`
   lookups. Changed: key by `GetInstanceID()` (D1).
6. **IL2CPP: prefix returning `false` on `TakeOffCarPart(string,bool)` (an `IEnumerator`)** would start a null
   coroutine. Changed: block only on the void overload and `CanTakeOffCarPart` (D2).
7. **IL2CPP inlining not checked.** The spike now confirms each hook fires on the real UI path, with a fallback
   (D2, task 0.1).
8. **Other rows could not plug in.** Only one baseline per spawn was accepted (row 3 needs one after `PrepareJob`,
   row 2 after unpark), and there was no server entry point to register/clear a loader. Changed: a repeated
   baseline (from any client whose loader is `Ready`) replaces the records; `CarPartsStore.RegisterSpawn`/
   `ClearLoader`; a D11 API table with exactly what `sync-car-details` A1 asks for (coordinator update):
   `CarPartsSync.IsReady(loaderId)`, `SpawnSeq(loaderId)`, events `BaselineUploaded` and `LocalPartsCommitted`,
   registry lookup by key. No server-side body-mount event (row 4 dropped `OnBodyPartReplaced`).
9. **Body cosmetics applied on every record**, which would overwrite paint/tint synced by `sync-car-details`
   (coordinator update). Changed in D7: colour/paint/livery/tint only when the record mounts the part.
10. **Mechanical-part dust/paint had no owner.** Row 4 says they are row 1's, and the DTO already has them. Added
    to the spec requirement, D7 and the dump.
11. **Engine swap breaks part keys** (the engine hierarchy changes) and no row stored it. Changed (coordinator
    update, matches `sync-car-details` A5 and `sync-workshop-tools` assumption 3): every baseline carries
    `EngineParams.EngineSwap`, this change stores it and applies it on replay before building the registry; row 5
    triggers live swaps and re-uploads the baseline. Spike 0.1 finds the swap call; spec scenario and an optional
    `engine-swap` harness step added. See open question 1.
12. **Claims missing on late join and on leaving the garage.** Changed: claims go into the snapshot and are
    released on `PresenceEvents` scene leave. Spec scenarios added for timeout and joiner.
13. **Garage reloads.** Per-loader state was not reset when the garage scene reloads (returning from another
    scene), and `DeleteCar()` in `VanillaLoad` was not suppressed. Changed: reset at every garage load, live
    packets dropped while away, delete under `Suppress` (D8, D9, tasks 3.2/4.1).
14. **Scenarios not drivable or not deterministic.** Server checks needed typing into the console. "B checked
    while still loading" depended on timing. The race step never reached the server precondition because claims
    intercept first. The mount race, spawner-left and restart cases were missing. Changed: `cars` command used
    through `Send-ServerCommand`/`Wait-ServerLog`, `car-hold`, `--no-claim`, a two-loader mount race, and in 5.4
    `to-menu`, rejoin and `Stop-/Start-TestServer`.

### Minor
15. The server class is `AuthHandler` (file `AuthHandlers.cs`). The reference was removed, because nothing is sent
    from it now.
16. `CarPart` has no `GetID()`, only `GetIDWithTuned()`. Fixed in D3.
17. `PacketTypes` renumbering went against the other rows' "append, never renumber" rule. Changed: the two old
    values are renamed in place and the rest appended.
18. A mount-state change outside a transaction (a row-5 tool calling `MarkDirty`) had no precondition. Fixed in D3.
19. "One transaction at a time; a new action closes the previous one" was undefined for overlapping actions.
    Changed: transactions are per part, and the spike checks whether overlap can happen.
20. The spike was task 3.1, after the core and server work that depends on it. Moved to 0.1.
21. The game has its own `PartScript.ShowMounted()`. It is now preferred over a copied finisher if it does not
    switch the game mode.
22. Body parts are applied by name (`TakeOn/OffCarPartFromSave(name)`) but keyed by index. The registry now logs
    duplicate names and the spike checks them.
23. Integration note "UID-idempotent ADD / `ItemActionType.Update`, whichever of rows 1/5 lands first": row 1 adds
    the idempotent ADD (its delta needs it). `Update` is left to row 5, because row 1 has no use for it.
24. The open-questions entry said single-player import is row 7's job. Per the user's answer it is backlog. Fixed.
25. The spec lacked scenarios for reservation timeout, a joiner seeing reservations, and part state surviving a
    restart. Added.
26. Left as is: body `StructureCondition`/`ConditionPaint` are not in `ModItem`. If the welder changes them,
    row 5's welder step (16.2) will show it, and the record can gain the fields then.

## Cross-change conflicts to fix elsewhere
- `sync-players-and-scenes` design D6 / task 4.6: `ClientScene.IsGarageReady` drops garage packets "while initial
  sync is unfinished". That would drop the `cars` snapshot and every live part/spawn packet queued behind it, and a
  car would be lost. Garage packets have to be queued (or allowed) during sync, and dropped only while not in the
  garage.
- `sync-car-details`: none left after its revision (A1 names match D11; A5 engine swap matches). Check that its
  late-join apply waits for `IsReady(loader)`.
- `sync-workshop-tools` design D7 table: interior detailing results are routed to `sync-car-details`, but row 4
  says `PartScript` dust/condition stay with row 1. They should be `CarPartsSync.MarkDirty(loader, partScript)`.
  After a live swap (`InsertEngineToCar`/`SwapEngine`) it must call `RebuildRegistry` + `UploadBaseline`; it must
  not add `EngineSwap` to the car record itself. Task 1.2: the idempotent ADD comes from row 1.
- `sync-car-placement-and-lifts` design §5 (park/unpark), §10 step 5, A3 and the tasks prerequisite: unpark must
  call `CarPartsStore.RegisterSpawn` (so the unparking client gets `CarSpawnAck` and uploads a baseline). Park must
  call `ClearLoader`. "SyncEnd at the end of `OnAskForSync`, shared with row 1" is outdated and should become
  provider `car-placement` (200).
- `sync-orders-and-jobs` D5/D8/D10: call `CarPartsSync.UploadBaseline(loaderId)` after `PrepareJob` and
  `CarPartsStore.ClearLoader` at job end, and send `JobsState` from provider `jobs` (400), not from `OnAskForSync`
  "before the inventory batches".

## Open questions for the user
1. **Engine swap while connected if the game offers no clean way to replay a swap on a freshly loaded car.**
   Default: (a) row 1 stores and replays the swap (current draft, the coordinator's decision). If spike 0.1
   finds no replay call, (b) disable engine swap while connected until a later change. Recommended: accept (b) as
   the fallback so the spike result does not block row 1.

## Integration pass (2026-10-06)

- D6/D11, tasks 2.2–2.3: `ClearLoader(loader, reason)` (`Deleted`, `Parked`, `JobEnded`, `SpawnerLeft`) and server
  events `SpawnRegistered` / `LoaderCleared(loader, removedRecord, reason)`; rows 2 and 3 subscribe instead of being
  called (lift reset, unparked car back to parking, lost job car reopens). Cars dropped on load raise no event.
- D2 / tasks 0.1, 3.5: engine crane out/in (`NotificationCenter.ActionUnMountGroup`, `ActionInsertEngineToCar`) is a
  group transaction here (asked by the workshop review). D3/D11, task 3.6: `PartTransaction` for a non-car root and
  hooks ignore `PartScript`s outside a registry (engine stand, `sync-workshop-machines`).
- D9 / task 4.2: a live re-baseline with a different `EngineSwap` swaps, then rebuilds the registry; snapshot spawn
  info is the stored `CarSpawnResponsePacket` incl. row 2's `CarData`/place and `IsJob`/`JobID`; snapshot handlers do
  not wait for `IsInitialSyncFinished`.
- D5 / task 2.5: `PresenceEvents` are merged in M1, so the "if it exists" condition is gone.
- Engine swap fallback (blocked while connected) accepted by the user; noted in D Open Questions and task 0.1.
- D6: the spawner's roll is marked as the interim plan until ROADMAP row 16 (server-side generation).
- References to row 5 now name `sync-workshop-machines` / `sync-workshop-car-tools`.

## Finding (2026-10-06): DLC cars

Spawning `car_astonmartindb5` (DLC 23) on test installs without that DLC loads the car but makes the game show its
"problem with following assets" window (missing DLC engine "Tadek Marek") on the other client. Per the user's DLC
decision (QUESTIONS.md fifth round), a car whose `CarBundleLoader.CheckHaveDLCForCar` is not -1 must only be
spawned into the shared garage when every connected player owns that DLC (set tracked by `mod-compatibility`).
Enforce in `CarSpawnRequest` on the server (refuse with `CarSpawnRejected`) once row 9's DLC set is available; the
same rule applies to rows 2/3 (parking, job cars) and 6 part 2 (bought cars). Test scenarios use base-game cars.
