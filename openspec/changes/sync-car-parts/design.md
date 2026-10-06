# Design

## Context

See proposal.md for why. Current state that shapes the approach:

- Car spawn is "hybrid": the client that loads a car runs `CarLoader.LoadCar` natively, the `LoadCarHook` prefix
  sends `CarSpawnRequest`, the server stores a `CarSpawnResponsePacket` in `CarState.LoadedCars` and relays it,
  and other clients run `LoadCar(name)` under `CarSpawnHooks.Suppress(loaderId)`. Nothing about parts follows,
  so random damage or missing parts already differ between clients right after a spawn.
- Inventory is a relay with server storage: the `Inventory.Add/Delete/AddGroup/DeleteGroup` prefixes send
  `InventoryItemAction`/`InventoryGroupItemAction`, the server appends/removes by UID and relays. Unmounting a part
  in-game calls `Inventory.Add` on the mechanic's client, so today the item is shared but the part is not.
- `ModGameState.CarState` already has `BodyParts` (by `PartIndex`) and `SubParts` (by `CarSubPartIdentity.BuildKey`)
  and the DTO comments explain why names are not used as keys (sibling `PartScript`s share names; the game's own
  `PartData.Path` from `GetGameObjectPathWithoutRoot` is ambiguous for the same reason).
- Server packet handlers run on each client's receive callback (`TCP.HandleData` → `PacketRouter.Dispatch`), so
  handlers for different clients run concurrently. The contract of `session-persistence-and-rejoin` (its task
  groups 1–2, which land before this change) creates `GameDataManager.StateLock` and holds it around every
  dispatch, `CommandSystem.Execute`, `Client.Disconnect` and main-loop ticks; it adds `ISaveSection`,
  `ISnapshotProvider`, `SyncOrder` (`cars` = 100), the `SyncBegin`/`SyncEnd{Items}`/`SyncAck` pipeline with the
  client `SyncTracker` (30 s no-progress timeout), the `cars` save section v1 (today's `CarState`), and harness
  helpers (`Send-ServerCommand`, `Wait-ServerLog`, `Stop-/Start-TestServer`, `to-menu`). This change plugs into
  those; it adds no lock, no sync flag and no join pipeline of its own.
- On join, `LoaderAddition.VanillaLoad` calls `DeleteCar()` + `LoadCarFromFile()` for every loader, i.e. it loads
  whatever cars the client's session profile (slot 4) holds, which the server knows nothing about.
- Old 0.4.17 lessons (upstream-MainMod `PartUpdateHooks`, `PartsReferencer`, `PartsUpdater`): it hooked
  `PartScript.DoMount`/`Hide` and `CarLoader.TakeOffCarPart`/`SwitchCarPart`, keyed parts by
  (group, index-in-`GetComponentsInChildren`), found the loader id by parsing `gameObject.name[10]`, and used fixed
  waits (`WaitForSeconds(1f)`, a 16-tick bolt loop) to guess when a change was done. It had no arbitration, so two
  players could take the same part, and the item was synced separately from the part. FixForTogether patches
  confirm the same failure classes: examined flags lost (`ExaminedSync`, hooks `PartScript.Examine(bool)`), and the
  mount window's temporary item reservation being sent as a real removal (`MountReservationSync`:
  `ChoosePartUpWindow.SelectItemInCreateGroup` deletes, `BackAllItems` re-adds, `SubmitGroupItem` commits).

## Goals / Non-Goals

**Goals:**
- One mount/unmount of a part results in exactly one consistent part state and one inventory change everywhere,
  also when two players act at once.
- The server can rebuild every loaded car's parts for a joining player without any other client's help.
- Remote changes are applied with game "from save" methods, without animations, camera or game-mode changes.

**Non-Goals:**
- Bolt-by-bolt animation on remote clients; others see the committed state and a "part in use" reservation.
- Parts while they are off the car on a tool (engine stand, repair table, tire changer): `sync-workshop-machines`, which reuses this
  change's `PartTransaction` for the engine stand (D3). Engine out/in with the crane is a group unmount/mount on the
  car and is handled here (D2); `sync-workshop-car-tools` only plays the effect.
- Fluids, wheels/tires/alignment, live paint/dirt/wash, license plates, tuning menu: row 4. They will call the
  `CarPartsSync.MarkDirty` entry point described below if their change lands on a part record.
- Lifts, moving cars between loaders, parking: row 2. Job random damage: row 3 (it calls `UploadBaseline`).
- Save envelope, backups and the join pipeline (`session-persistence-and-rejoin`); importing a single-player save (backlog).

## Decisions

### D1. Part identity: keys frozen at load time
- Body part key `b:<i>` = index in `CarLoader.carParts` (built by `CreateParts` from the car config, same order on
  every client for the same `carToLoad` + `ConfigVersion`). `PartName` (`CarPart.name`) is sent and checked.
- Mechanical part key `s:<path>` = `PartIndexPath`, the `Transform.GetSiblingIndex()` chain from the transform that
  carries `CarLoaderOnCar` down to the `PartScript`, joined by `CarSubPartIdentity.BuildKey`. `PartId`
  (`PartScript.id`) is sent and checked.
- Keys are computed **once**, in a per-car `PartRegistry` built right after `IsCarLoaded()` and before any
  interaction, by walking the hierarchy (including inactive objects). Afterwards lookups go through the registry's
  two dictionaries (`GetInstanceID()` of the `PartScript`/`CarPart.handle` → key, key → object), so later
  reparenting or inserted children (wheel resize, tuning swaps, engine on crane) cannot shift a key. Unhollower
  wrappers are not stable dictionary keys (a new wrapper per native fetch), hence the instance id. Every client
  builds its registry from a fresh `LoadCar` of the same model, so the structure is the same.
- `PartRegistry.Build(Transform root)` takes any root, so `sync-workshop-machines` can reuse it for an engine on a
  stand. `CarPartsSync.RebuildRegistry(loaderId)` exists for hierarchy-replacing actions owned by other rows
  (engine swap, `sync-workshop-car-tools`); the caller then calls `UploadBaseline` (D6).
- On a key that does not resolve or whose id/name does not match: drop the change, log it, send
  `CarPartsResyncRequest`; the server answers with a `CarPartsSnapshot` of that car.
- Alternatives: name paths (`GetGameObjectPathWithoutRoot`, as the game's own `PartData`) - ambiguous between
  identical siblings; upstream's (group, `GetComponentsInChildren` index) - same idea but recomputed at hook time,
  so any hierarchy change shifts it; index into `CarLoader.partScriptCache` - flat and simple, but we do not know
  when the game rebuilds it (`CachePartScripts`/`ClearPartScriptCache`) and it gives no debuggable path.

### D2. Detecting a local change: hooks mark dirty, a tracker commits stable diffs
Hooks only mark parts dirty; a per-loader `PartChangeTracker` coroutine (every ~0.1 s) compares the dirty parts'
current state with their last synced record and sends once the part is stable for 3 polls and not in progress
(`CarPart.TakeOnOffInProgress`/`InProgress`, or a `PartScript` whose `MountObjects` are partly unscrewed or whose
`MountAnimationCompleted` is false). This replaces upstream's fixed waits and works whether the game finishes a
change inside the hooked method or in a coroutine started by it (`Hide`, `DoMount`, `TakeOffCarPart(string,bool)`
are all `IEnumerator`s, and a Harmony postfix only sees the enumerator being created).

Hook points (all exist in the decompiled stubs):
| Game method | Use |
|---|---|
| `PartScript.ActionUnMount()`, `ActionMount(bool)`, `FastUnmount()`, `FastMount()` | prefix: block if reserved by someone else / car not ready; open transaction + claim; mark dirty |
| `PartScript.Hide()`, `DoMount()` | postfix: mark dirty (incl. `GetUnmountWith()` members) |
| `PartScript.UndoMounting()`, `UndoUnMounting()`, `CancelUnmountAnim()`; `ChoosePartUpWindow.BackAllItems()` | postfix: cancel transaction, release claim |
| `PartScript.Examine(bool)` | prefix/postfix: mark dirty when `IsExamined` flips |
| `CarLoader.TakeOffCarPart(string)` | prefix: block (return false) / open transaction + claim; postfix: mark dirty |
| `CarLoader.TakeOffCarPart(string,bool)` (`IEnumerator`) | prefix, never returns false: open transaction + claim if not yet open; mark dirty |
| `CarLoader.CanTakeOffCarPart(string, out TakePartOffLockReason)` | postfix: return false while reserved by another player or the car is not `Ready` |
| `CarLoader.SwitchCarPart(string)`, `SwitchCarPart(string,bool)` | postfix: mark dirty (`Switched`) |
| `CarLoader.ExamineAllParts()` | postfix: mark whole car dirty |
| `NotificationCenter.ActionUnMountGroup(InteractiveObject)`, `ActionInsertEngineToCar(GroupItem)` (engine crane out/in; `MountGroup(long)` if spike 0.1 shows it on that path) | prefix: block if reserved / car not ready; open one transaction for the engine group's parts + claim; postfix: mark them dirty |
| `CarLoader.DeleteCar()` (existing hook) and garage scene load | drop registry, tracker, queue |

A prefix on an `IEnumerator` method must not return `false` (the caller would start a null coroutine); blocking is
done on the void entry points and on `CanTakeOffCarPart`. IL2CPP may inline small methods so that a native caller
never reaches the detour; spike 0.1 confirms that every hook above fires on the real UI path and replaces any that
does not (with its caller or a coarser postfix).

**Static check of the hooks (2026-10-06, native `xref.py` over `GameAssembly.dll`):** every hook target is a real
function with direct callers on the interactive path, so a Harmony detour catches it (indirect calls through method
pointers reach the detour too): `ActionUnMount` ← `Raycast.PartSelect`; `ActionMount` ← `Raycast.PartSelectMount`;
`DoMount` ← `GameScript.SelectPartToMount`; `Hide` ← `ActionUnMount`, `FastUnmount`, `CancelUnmountAnim`,
`PartScript.Update`; `Examine` ← `Raycast.ExamineCondition` and the diagnostic tools' `UseAnim`; `TakeOffCarPart`
← `GameScript.ClickIO`/`BodyMount`; `CanTakeOffCarPart` and `SwitchCarPart` ← `GameScript.ClickIO`;
`UndoMounting`/`UndoUnMounting` ← `GameScript.CleanUnfinishedMount`/`CleanUnfinishedUnMount`; engine crane out ←
`ToolsManager.UseEngineCrane` → `NotificationCenter.ActionUnMountGroup`. `FastUnmount`, `FastMount` and
`ActionInsertEngineToCar` have no direct caller (debug/UI-callback entry points). Event order, item IDs and the
`ShowMounted` mode question still need a runtime trace.

**Part identity spike (2026-10-06, scenario `part-identity`):** client A spawns each car model on loader 0, client B
loads it from the server's `CarSpawnResponse`, both dump body indices/names and mechanical sibling paths/ids from
`CarLoader.root` (the object carrying `CarLoaderOnCar`); see STATUS.md for the run over all 163 models.

Alternative considered: a periodic full scan of all parts. Rejected as the primary mechanism (hundreds of parts
per car); the harness diff is our drift detector instead. If spike 0.1 shows a path that no hook catches, a slow
(~5 s) full compare of that car while it is `Ready` is the fallback.

### D3. A mount/unmount is one transaction with its inventory effect
The action-start prefix opens a client `PartTransaction` for that part (and its `GetUnmountWith()` members). If
spike 0.1 shows the game lets a player start a second action before the first finished, several transactions can be
open, one per part. While one is open, the existing inventory prefixes hand matching events to it instead of
sending them: items whose `ID` equals `PartScript.GetID()`/`GetIDWithTuned()` or `CarPart.GetIDWithTuned()`
(`CarPart` has no `GetID()`) of a part in the transaction, and the items held by `ChoosePartUpWindow`
(`currentItem`, `selectedItemsToCreateGroup`). A Delete
followed by an Add of the same UID cancels out (this is the reservation/rollback pattern FixForTogether documented;
the idea is credited to TogetherFixer, no code is adapted). On commit the buffer becomes the transaction's
`InventoryDelta`; on cancel or a 10 s idle timeout the buffer is flushed through the normal inventory packets.

`CarPartsChange` = `{CarLoaderID, SpawnSeq, TxId, Preconditions[(key, wasUnmounted)], BodyParts[], SubParts[],
InventoryDelta{AddedItems, AddedGroups, RemovedItemUids, RemovedGroupUids}}`. Every part whose `Unmounted` differs
from its last synced record gets a precondition, also when the change has no transaction (e.g. a `sync-workshop-car-tools` tool calling
`MarkDirty`); attribute-only changes (examined, switched, condition, dust) have no preconditions and an empty delta.

Hooks ignore a `PartScript` that is in no car registry (an engine on a stand). `PartTransaction` and `InventoryDelta`
also work for a non-car root (`PartRegistry.Build(engineGameObject)`), so `sync-workshop-machines` can send its own `ToolPartChange`
for parts on the engine stand with the same precondition/delta rules.

Alternative: keep part and inventory packets separate and correlate on the server. Rejected: the server cannot
undo an inventory add it already relayed when the part change later loses a race; that is the old duplication bug.

### D4. Server arbitration; what it stores and what it relays
Server handling of `CarPartsChange` runs under the contract's `GameDataManager.StateLock` (held around every
dispatch, `Client.Disconnect` and main-loop ticks, so claim cleanup on disconnect and the claim-expiry check in
`ServerWindow.TickServer` are covered too):
1. Drop if `SpawnSeq` ≠ `LoadedCars[loader].SpawnSeq` (car replaced) or the car has no baseline yet.
2. Reject if any precondition's `wasUnmounted` ≠ stored `Unmounted`, or any `RemovedItemUids`/`RemovedGroupUids` is
   not in `InventoryState`.
3. Otherwise: apply the delta to `InventoryState` (an added UID that is already present is skipped: UID-idempotent
   ADD, which this change also adds to `InventoryHandlers` on server and client per the ROADMAP integration note;
   `ItemActionType.Update` is not needed here and stays with `sync-workshop-machines`), merge the records (examined is
   OR-merged), bump the car's `Revision`, stamp the records, send `CarPartsChangeResult{Accepted, Revision}` to the
   sender and the change (with `Revision`) to all other clients.
4. On reject: `CarPartsChangeResult{Accepted=false, Reason, current records of the touched parts, RestoreUids}`;
   `RestoreUids` lists the consumed items the server still holds. The sender applies the records and reverts its
   local delta with inventory hooks suppressed: delete the items it added, re-add only the consumed items in
   `RestoreUids` (an item another player already used stays gone).

| Data | Server |
|---|---|
| `LoadedCars[loader]` + new `SpawnSeq`, `Revision`, `HasBaseline`, `SpawnedBy` (runtime), `EngineSwap` | stored, saved (except `SpawnedBy`) |
| `BodyParts`, `SubParts` records (with `Revision`) | stored, saved |
| Inventory delta of a transaction | stored in `InventoryState`, relayed inside `CarPartsChange` |
| Claims | runtime only (not saved), relayed as `CarPartClaimUpdate` |
| `CarPartsResyncRequest` | answered from stored state |

Attribute-only changes are last-writer-wins: they cannot duplicate items, and the server serializes them.

### D5. "In progress" as a server-side reservation
On action start the client sends `CarPartClaim{loader, SpawnSeq, keys (part + unmountWith members), release=false}`.
The server grants if no key is held by another player, stores `{owner, time}`, and broadcasts
`CarPartClaimUpdate{keys, ownerPlayerId}` to everyone (owner −1 = released). Other clients block new actions on
those keys (D2 table) and show `UIManager.ShowInfoWindow("<player> is working on this part")`. If the requester
gets an update naming someone else it cancels its local action (`CancelUnmountAnim`/`UndoUnMounting`/
`UndoMounting`); if it was too late, D4's precondition still rejects the commit. Claims end on commit, cancel,
the holder's disconnect (`Client.Disconnect`), the holder leaving the garage (`PresenceEvents.Left`/`SceneChanged`
from `sync-players-and-scenes`, merged in M1 before this change) or after 120 s. Active claims are part of the late-join snapshot
(D9), so a joiner also sees parts that are in use.

### D6. Baseline and `SpawnSeq`
- The server assigns a monotonically increasing `SpawnSeq` per accepted spawn, sends `CarSpawnAck{loader, SpawnSeq}`
  to the spawner and adds `SpawnSeq` to the relayed `CarSpawnResponse`. Every part packet carries it, so packets
  for a deleted/replaced car are dropped on both sides. Delete clears `BodyParts`/`SubParts`/claims for the loader.
- The spawner uploads a complete `CarPartsSnapshot` (direction client→server) once the car is loaded, its registry
  is built and no part changed for 1 s; it becomes `Ready` when it sends it (its own ordered stream puts the
  baseline before any of its changes). `CarPartsSync.UploadBaseline(loaderId)` is public so rows 2/3/5 can call it
  right after their own post-load steps (job damage after `PrepareJob`, load from parking, engine swap). The server
  accepts the first baseline for a `SpawnSeq` only from `SpawnedBy` and stores it with `Revision = 1`; a later one
  (re-baseline, e.g. after `PrepareJob` rolled job damage or after an engine swap) is accepted from any client whose
  loader is `Ready` for that `SpawnSeq`, replaces all records and bumps `Revision`. Each is relayed to the others as
  a full snapshot.
- Current plan: the spawner's own random roll (damage, condition, colour) becomes the shared state through its first
  baseline; the server only decides which client's roll counts. Interim: replaced by server-side generation in
  ROADMAP row 16 `server-game-logic` once its decompile spike confirms it.
- Every baseline carries `EngineSwap` (`CarLoader.EngineParams.EngineSwap`, empty = original engine), which this
  change stores as car state: the swap replaces the engine's part hierarchy, so part keys depend on it. `sync-workshop-car-tools`
  triggers live swaps (engine crane) and then calls `RebuildRegistry` + `UploadBaseline`. On replay (D9) this
  change applies a stored swap before it builds the registry; spike 0.1 finds the game call for that.
- Every server path that creates a loader record goes through `CarPartsStore.RegisterSpawn(record, clientId)`
  (assigns `SpawnSeq`, sets `SpawnedBy`, sends `CarSpawnAck`, raises `SpawnRegistered(loader, record)`), and every
  path that removes one through `CarPartsStore.ClearLoader(loader, reason)` (drops records and claims, raises
  `LoaderCleared(loader, removedRecord, reason)` with `reason` = `Deleted`, `Parked`, `JobEnded`, `SpawnerLeft`):
  today `CarHandlers` (spawn, `Deleted`), later row 2 (unpark = spawn, park = `Parked`) and row 3 (job spawn, job
  end = `JobEnded`, claim release = `Deleted`). The events are server-side, raised under `StateLock`; rows that land
  later subscribe instead of being called (row 2: lift reset and returning an unparked car to parking; row 3: the
  job's car loader).
- If the spawner disconnects (or leaves the garage) before uploading, the server deletes the car with
  `ClearLoader(loader, SpawnerLeft)` and broadcasts `CarSpawnDelete`; a car without baseline cannot be replayed.
  `SpawnedBy` is cleared when that client leaves. Cars dropped by `Load` (no baseline) raise no event; rows that
  reference loaders check them after load.

### D7. Applying remote records (client)
Per loader, inside an `ApplyingRemote(loaderId)` scope that also sets `InventoryHandlers.IgnoreInventoryHooks`:
- Body: `TakeOffCarPartFromSave(name)` / `TakeOnCarPartFromSave(name)` when `Unmounted` differs (these take a
  name; the registry logs duplicate body-part names per car, and spike 0.1 checks they do not occur);
  `SwitchCarPart(part, true, switched)`; `TunePart(name, tunedId)` when different; `SetCondition(part, c)`,
  `SetDent(part, d)`, `Quality`; then `UpdateCarBodyPart(part)`. Colour/paint/livery/tint from the record's
  `ModItem` are applied (`SetCarColor`, `SetCarPaintType`, `SetCarLivery`) **only when the record mounts the part**
  (`Unmounted` true → false); after that, body cosmetics belong to `sync-car-details` (its ownership rule).
- Mechanical: `SetCondition(c)`, `SetConditionNormal(c)`, `Quality`, `IsExamined`, `UpdateDust(dust, true)`,
  paint (`SetColor`, `CurrentPaintType`/`CurrentPaintData`) when `IsPainted`, `SetMountObjectData(...)` (bolt
  condition/stuck), `TunePart(tunedId)` when different; `HideBySavegame(false, carLoader)` to unmount,
  `ShowBySaveGame()` + a mount finisher to mount. The finisher is the game's own `PartScript.ShowMounted()` if spike
  0.1 shows it does not switch the game mode; otherwise a copy of upstream's `CustomPartScriptMethod.ShowMounted`
  without the game-mode switch. Group members arrive as their own records, so `withUnmountWith` is always false.
- The record becomes the tracker's "last synced" state, so the apply is never echoed back.

### D8. Per-loader state machine and queue
`Empty → Loading → AwaitingBaseline → Ready`. Changes/claims for a loader that is not `Ready` are queued; when it
becomes `Ready` the queue is drained in order, skipping `Revision ≤` the applied snapshot's revision and any
`SpawnSeq` mismatch. Local hooks refuse mount/unmount actions on a car that is not `Ready`. All per-loader state
is reset at the start of every garage load (`CustomLoad`, before `AskForSync`), because a scene load replaces the
`CarLoader` objects; while the local player is outside the garage, live part packets are dropped and the snapshot
on return (`sync-players-and-scenes` D6) rebuilds everything.

### D9. Late-join path
1. `CustomLoad` → `VanillaLoad`: in the connected path the loader loop only calls `DeleteCar()` (under
   `CarSpawnHooks.Suppress`, so a garage reload never sends a delete); it no longer calls `LoadCarFromFile()`, so
   no car from the session profile appears.
2. Server: `CarsSnapshotProvider : ISnapshotProvider` (`[SessionSection]`, key `cars`, `SyncOrder.Cars` = 100),
   called by the contract's pipeline under `StateLock`; nothing is sent from `OnAskForSync` directly.
   `SendSnapshot(clientId)` sends, per car with a baseline, a `CarPartsSnapshot` split into batches of ~100 records
   (`SnapshotId`, `BatchIndex`, `IsLastBatch`, car spawn info = the stored `CarSpawnResponsePacket` including
   row 2's `CarData`/place and row 3's `IsJob`/`JobID`, `SpawnSeq`, `Revision`, `EngineSwap`), then one
   `CarPartClaimUpdate` per active claim, and returns the number of cars sent (1 item = 1 car). Batching keeps
   BinaryFormatter packets small for Steam's send limits.
3. The client assembles each car's batches; once inventory + garage state are applied it loads the car with the
   existing suppressed `LoadCar` path (`ProcessCarSpawnResponse`), applies `EngineSwap` if set, builds the
   registry, applies all records, becomes `Ready`, calls `SyncTracker.Applied("cars")` and drains its queue (live
   changes after the snapshot have a higher `Revision`). There is no `IsCarsSynced` flag and no own timeout: the
   contract's `SyncEnd{Items}` count, `SyncAck` and 30 s no-progress timeout cover cars.
The same snapshot path (with `SnapshotId = 0`) serves `CarPartsResyncRequest` (single car, no reload unless
`carToLoad` differs) and live baselines; those do not count toward `SyncTracker`. A live re-baseline whose
`EngineSwap` differs from the local car's (an engine swap by another player, `sync-workshop-car-tools`) applies the swap first, then
`RebuildRegistry`, then the records.

### D10. Packets
New: `CarPartsChange`, `CarPartsChangeResult`, `CarPartClaim`, `CarPartClaimUpdate`, `CarPartsSnapshot`,
`CarPartsResyncRequest`, `CarSpawnAck`. Changed: `CarBodyPartUpdatePacket`/`CarSubPartUpdatePacket` lose their
`[NetworkPacket]` role and become nested records (`+PartId`, `+TunedID`, `+MountObjectData`, `+Revision`; the
sub-part record keeps its paint and `Dust` fields). Following the other rows' rule (never renumber), the
`CarBodyPartUpdate`/`CarSubPartUpdate` enum values are renamed in place to `CarPartsChange`/`CarPartsChangeResult`
and the other five are appended at the end. `CarSpawnResponsePacket` gains `SpawnSeq`.

### D11. API for other rows
| Side | Entry point | Used by |
|---|---|---|
| client | `CarPartsSync.MarkDirty(loaderId, PartScript)` / `MarkDirty(loaderId, CarPart)` | `sync-workshop-car-tools` (welder body state, interior detailing dust/condition) |
| client | `CarPartsSync.UploadBaseline(loaderId)`, `RebuildRegistry(loaderId)` | row 2 (unpark), row 3 (`PrepareJob`), `sync-workshop-car-tools` (engine swap) |
| client | `CarPartsSync.IsReady(loaderId)`, `SpawnSeq(loaderId)`; events `BaselineUploaded(loaderId)` (after every baseline this client sent), `LocalPartsCommitted(loaderId, keys)` (after an accepted local `CarPartsChange`) | row 4 (spawn snapshot, `SpawnSeq` on its packets, remount cosmetics, tuning) |
| client | `PartRegistry.Build(Transform root)`, lookup by key, sub-part record; `PartTransaction`/`InventoryDelta` for a non-car root | row 4, `sync-workshop-machines` (engine on a stand) |
| server | `CarPartsStore.RegisterSpawn(record, clientId)`, `ClearLoader(loader, reason)`; `LoadedCars[loader].SpawnSeq`/`HasBaseline` | rows 2, 3, 4 |
| server | events `CarPartsStore.SpawnRegistered(loader, record)`, `LoaderCleared(loader, removedRecord, reason)` | row 2 (lift reset, unparked car back to parking on `SpawnerLeft`), row 3 (job car loader, D12) |

## Risks / Trade-offs

- [Hierarchy differs between clients after `LoadCar` (e.g. DLC/mod parts, config-dependent `AddPartIfShould`)]
  → registry is built from a fresh `LoadCar` on every client; id/name check on every apply; resync on mismatch;
  harness dumps the registry size and a hash per car so a mismatch shows up immediately.
- [Inventory event of an unmount happens outside the transaction window or with an unexpected item ID]
  → spike 0.1 records the real event order and IDs for one body and one mechanical part before building D3;
  unmatched events fall through to today's inventory flow (no worse than now).
- [A hook never fires because IL2CPP inlined the method] → spike 0.1 checks each hook on the real UI path.
- [`HideBySavegame`/`ShowBySaveGame`/`TakeOn/OffCarPartFromSave` have side effects (blocking flags, unblocking
  dependent parts, wheel sizes) that differ from the interactive path] → apply in the same parent-to-child order
  as the snapshot; task 3.3 compares the blocked/unblocked state of neighbouring parts on both clients after a
  remote unmount (`PartScript.IsBlocked()` goes into the dump while debugging).
- [Concurrent server handlers] → the contract's `StateLock` around every dispatch; broadcast order follows lock order.
- [Large snapshots slow joining] → batching and progress-based timeout; loading time itself is unchanged.
- [Claims can strand a part if a release is lost] → 120 s expiry and disconnect/scene-leave cleanup.
- [Baseline timing for job cars] → row 3 calls `UploadBaseline` after `PrepareJob` (a repeated baseline replaces
  the first); the 1 s settle default covers plain spawns.
- [A repeated baseline overwrites a change another player made in between] → only the spawner sends it, right after
  its own post-load step, while other clients are usually still loading; accepted.
- Trade-off: always reloading cars on join is slower than patching a matching local car, but it never leaks the
  joiner's local state (including row-4 data we do not sync yet).

## Migration Plan

The contract's `cars` save section v1 wraps today's `CarState`. This change changes its shape, so it bumps the
section to **v2**: `Version = 2`, and `Migrate(data, 1)` turns every v1 `LoadedCars[loader]` into a v2 loader entry
with `HasBaseline = false`, sets `NextSpawnSeq` past the highest assigned value, drops the old (always empty)
`BodyParts`/`SubParts`, and copies every other field (e.g. row 4's `Details`) unchanged. `Load` then drops loader
entries without baseline (they cannot be replayed) and logs each one. `sync-car-details` bumps the section once more
for its `Details` field with its own step (v2 → v3, or the other way round if it lands first). The contract writes
the pre-migration backup.
No client data migration. Rollback = restore the pre-migration copy (an older build cannot read a v2 `cars`
section).

## Open Questions / assumptions

Decisions made without asking the user (recorded here as instructed):
- A player joining does **not** bring cars from their own profile into the session (QUESTIONS.md default);
  importing a single-player save is backlog (user decision 2026-10-05).
- Remote players see the committed state only, not bolt animations; "in progress" is shown as a reservation
  (QUESTIONS.md default).
- Parts on the engine stand are out of the car registry's reach while detached and are `sync-workshop-machines`' (with this change's
  `PartTransaction`); engine out/in with the crane is a group transaction here.
- Engine swap: this change stores and replays it (D6, settled with `sync-car-details` A5 and
  `sync-workshop-car-tools` D2); that change only triggers live swaps. If spike 0.1 finds no way to swap a freshly
  loaded car without an inventory engine group, engine swap is disabled while connected (review.md question 1;
  fallback accepted by the user 2026-10-05).
Deferrable unknowns:
- Exact item IDs produced for body parts on unmount (spike 0.1 settles them before D3 is coded).
- Whether `PartScript.OnHidePartFinished`/`OnMountFinished` fire reliably; if so they can replace polling in D2
  (subscribing needs an Il2Cpp delegate via `DelegateSupport.ConvertDelegate`).
