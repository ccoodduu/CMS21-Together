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
  handlers for different clients run concurrently and the current handlers take no lock.
- On join, `LoaderAddition.VanillaLoad` calls `DeleteCar()` + `LoadCarFromFile()` for every loader, i.e. it loads
  the joining player's local save cars, which the server knows nothing about.
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
- Parts while they are off the car on a tool (engine stand/crane, repair table, tire changer): row 5.
- Fluids, wheels/tires/alignment, live paint/dirt/wash, license plates, tuning menu: row 4. They will call the
  `CarPartsSync.MarkDirty` entry point described below if their change lands on a part record.
- Lifts, moving cars between loaders, parking: row 2. Job random damage: row 3 (it calls `UploadBaseline`).
- Save versioning and importing a player's local save into the server: row 7.

## Decisions

### D1. Part identity: keys frozen at load time
- Body part key `b:<i>` = index in `CarLoader.carParts` (built by `CreateParts` from the car config, same order on
  every client for the same `carToLoad` + `ConfigVersion`). `PartName` (`CarPart.name`) is sent and checked.
- Mechanical part key `s:<path>` = `PartIndexPath`, the `Transform.GetSiblingIndex()` chain from the transform that
  carries `CarLoaderOnCar` down to the `PartScript`, joined by `CarSubPartIdentity.BuildKey`. `PartId`
  (`PartScript.id`) is sent and checked.
- Keys are computed **once**, in a per-car `PartRegistry` built right after `IsCarLoaded()` and before any
  interaction, by walking the hierarchy (including inactive objects). Afterwards lookups go through the registry's
  two dictionaries (`PartScript` → key, key → `PartScript`), so later reparenting or inserted children (wheel
  resize, tuning swaps, engine on crane) cannot shift a key. Every client builds its registry from a fresh
  `LoadCar` of the same model, so the structure is the same.
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
| `CarLoader.TakeOffCarPart(string,bool)`, `TakeOffCarPart(string)` | prefix: block/open transaction + claim; postfix: mark dirty |
| `CarLoader.CanTakeOffCarPart(string, out TakePartOffLockReason)` | postfix: return false while reserved by another player |
| `CarLoader.SwitchCarPart(string)`, `SwitchCarPart(string,bool)` | postfix: mark dirty (`Switched`) |
| `CarLoader.ExamineAllParts()` | postfix: mark whole car dirty |
| `CarLoader.DeleteCar()` (existing hook) | drop registry, tracker, queue |

Alternative considered: a periodic full scan of all parts. Rejected as the primary mechanism (hundreds of parts
per car, and it would pick up changes that other rows sync through their own packets); the harness diff is our
drift detector instead.

### D3. A mount/unmount is one transaction with its inventory effect
The action-start prefix opens a client `PartTransaction` (one at a time; a new action closes the previous one).
While it is open, the existing inventory prefixes hand matching events to the transaction instead of sending them:
items whose `ID` equals `GetID()`/`GetIDWithTuned()` of a part in the transaction (both `PartScript` and `CarPart`
have these), and the items held by `ChoosePartUpWindow` (`currentItem`, `selectedItemsToCreateGroup`). A Delete
followed by an Add of the same UID cancels out (this is the reservation/rollback pattern FixForTogether documented;
the idea is credited to TogetherFixer, no code is adapted). On commit the buffer becomes the transaction's
`InventoryDelta`; on cancel or a 10 s idle timeout the buffer is flushed through the normal inventory packets.

`CarPartsChange` = `{CarLoaderID, SpawnSeq, TxId, Preconditions[(key, wasUnmounted)], BodyParts[], SubParts[],
InventoryDelta{AddedItems, AddedGroups, RemovedItemUids, RemovedGroupUids}}`. Attribute-only changes (examined,
switched, condition without mount change) use the same packet with no preconditions and an empty delta.

Alternative: keep part and inventory packets separate and correlate on the server. Rejected: the server cannot
undo an inventory add it already relayed when the part change later loses a race; that is the old duplication bug.

### D4. Server arbitration; what it stores and what it relays
Server handling of `CarPartsChange`, under one `GameDataManager.StateLock` (also taken by `CarHandlers`,
`OnAskForSync`'s car snapshot, and the list mutations in `InventoryHandlers`, since handlers run concurrently):
1. Drop if `SpawnSeq` ≠ `LoadedCars[loader].SpawnSeq` (car replaced) or the car has no baseline yet.
2. Reject if any precondition's `wasUnmounted` ≠ stored `Unmounted`, or any `RemovedItemUids`/`RemovedGroupUids` is
   not in `InventoryState`.
3. Otherwise: apply the delta to `InventoryState`, merge the records (examined is OR-merged), bump the car's
   `Revision`, stamp the records, send `CarPartsChangeResult{Accepted, Revision}` to the sender and the change
   (with `Revision`) to all other clients.
4. On reject: `CarPartsChangeResult{Accepted=false, Reason, current records of the touched parts}`; the sender
   applies those records and reverts its local delta with inventory hooks suppressed (delete the items it added,
   re-add the items it consumed).

| Data | Server |
|---|---|
| `LoadedCars[loader]` + new `SpawnSeq`, `Revision`, `HasBaseline`, `SpawnedBy` | stored, saved |
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
the holder's disconnect (`Server.Client.Disconnect`) or after 120 s.

### D6. Baseline and `SpawnSeq`
- The server assigns a monotonically increasing `SpawnSeq` per accepted spawn, sends `CarSpawnAck{loader, SpawnSeq}`
  to the spawner and adds `SpawnSeq` to the relayed `CarSpawnResponse`. Every part packet carries it, so packets
  for a deleted/replaced car are dropped on both sides. Delete clears `BodyParts`/`SubParts`/claims for the loader.
- The spawner uploads a complete `CarPartsSnapshot` (direction client→server) once the car is loaded, its registry
  is built and no part changed for 1 s. `CarPartsSync.UploadBaseline(loaderId)` is public so rows 2/3 can call it
  right after their own post-load steps (job damage, load from parking). The server accepts only from the
  `SpawnedBy` client for the current `SpawnSeq`, stores it with `Revision = 1`, and relays it to the others.
- If the spawner disconnects before uploading, the server deletes the car (broadcast `CarSpawnDelete`); a car
  without baseline cannot be replayed.

### D7. Applying remote records (client)
Per loader, inside an `ApplyingRemote(loaderId)` scope that also sets `InventoryHandlers.IgnoreInventoryHooks`:
- Body: `TakeOffCarPartFromSave(name)` / `TakeOnCarPartFromSave(name)` when `Unmounted` differs;
  `SwitchCarPart(part, true, switched)`; `TunePart(name, tunedId)` when different; `SetCondition(part, c)`,
  `SetDent(part, d)`, `Quality`; colour/paint/livery from the record's `ModItem` via `SetCarColor`,
  `SetCarPaintType`, `SetCarLivery`; then `UpdateCarBodyPart(part)`.
- Mechanical: `SetCondition(c)`, `SetConditionNormal(c)`, `Quality`, `IsExamined`, `SetMountObjectData(...)`
  (bolt condition/stuck), `TunePart(tunedId)` when different; `HideBySavegame(false, carLoader)` to unmount,
  `ShowBySaveGame()` + a mount finisher (like upstream's `CustomPartScriptMethod.ShowMounted`, without the
  game-mode switch) to mount. Group members arrive as their own records, so `withUnmountWith` is always false.
- The record becomes the tracker's "last synced" state, so the apply is never echoed back.

### D8. Per-loader state machine and queue
`Empty → Loading → AwaitingBaseline → Ready`. Changes/claims for a loader that is not `Ready` are queued; when it
becomes `Ready` the queue is drained in order, skipping `Revision ≤` the applied snapshot's revision and any
`SpawnSeq` mismatch. Local hooks refuse mount/unmount actions on a car that is not `Ready`.

### D9. Late-join path
1. `CustomLoad` → `VanillaLoad`: in the connected path the loader loop only calls `DeleteCar()`; it no longer calls
   `LoadCarFromFile()`, so no local-save car appears.
2. `OnAskForSync`: after the inventory batches and before `SyncEnd`, under `StateLock`, the server sends for every
   car with a baseline a `CarPartsSnapshot` split into batches of ~100 records (`BatchIndex`, `IsLastBatch`,
   car spawn info, `SpawnSeq`, `Revision`). Batching keeps BinaryFormatter packets small for Steam's send limits.
3. The client assembles each car's batches; once inventory + garage state are synced it loads the car with the
   existing suppressed `LoadCar` path (`ProcessCarSpawnResponse`), builds the registry, applies all records,
   becomes `Ready` and drains its queue (live changes after the snapshot have a higher `Revision`).
4. `SyncEnd` arrives after all snapshots, so at `SyncEnd` the client knows the full set of cars to wait for;
   `WaitForSyncCompletion` also waits for `ClientData.IsCarsSynced`, and `CustomLoad`'s fixed 10 s timeout becomes
   "10 s without progress" while cars load.
The same snapshot path serves `CarPartsResyncRequest` (single car, no reload unless `carToLoad` differs).

### D10. Packets
New: `CarPartsChange`, `CarPartsChangeResult`, `CarPartClaim`, `CarPartClaimUpdate`, `CarPartsSnapshot`,
`CarPartsResyncRequest`, `CarSpawnAck`. Changed: `CarBodyPartUpdatePacket`/`CarSubPartUpdatePacket` lose their
`[NetworkPacket]` role and become nested records (`+PartId`, `+TunedID`, `+MountObjectData`, `+Revision`);
their two `PacketTypes` entries are replaced by the new ones (same-build check makes renumbering safe).
`CarSpawnResponsePacket` gains `SpawnSeq`.

## Risks / Trade-offs

- [Hierarchy differs between clients after `LoadCar` (e.g. DLC/mod parts, config-dependent `AddPartIfShould`)]
  → registry is built from a fresh `LoadCar` on every client; id/name check on every apply; resync on mismatch;
  harness dumps the registry size and a hash per car so a mismatch shows up immediately.
- [Inventory event of an unmount happens outside the transaction window or with an unexpected item ID]
  → spike task 3.1 records the real event order and IDs for one body and one mechanical part before building D3;
  unmatched events fall through to today's inventory flow (no worse than now).
- [`HideBySavegame`/`ShowBySaveGame`/`TakeOn/OffCarPartFromSave` have side effects (blocking flags, unblocking
  dependent parts, wheel sizes) that differ from the interactive path] → apply in the same parent-to-child order
  as the snapshot; task 3.4 compares the blocked/unblocked state of neighbouring parts on both clients after a
  remote unmount (`PartScript.IsBlocked()` goes into the dump while debugging).
- [Concurrent server handlers] → one `StateLock` around car and inventory state; broadcast order follows lock order.
- [Large snapshots slow joining] → batching and progress-based timeout; loading time itself is unchanged.
- [Claims can strand a part if a release is lost] → 120 s expiry and disconnect cleanup.
- [Baseline timing for job cars] → depends on row 3 calling `UploadBaseline`; the 1 s settle default is a fallback.
- Trade-off: always reloading cars on join is slower than patching a matching local car, but it never leaks the
  joiner's local save state (including row-4 data we do not sync yet).

## Migration Plan

Old server saves have `LoadedCars` without baseline; on load the server drops cars without baseline (they cannot be
replayed) and logs it. No client data migration. Rollback = revert the change; the save stays readable because
the new fields are additive JSON.

## Open Questions / assumptions

Decisions made without asking the user (recorded here as instructed):
- A player joining with cars in their local save does **not** get those cars into the session; importing a local
  save is row 7's job. (Alternative: first player seeds the server from their save.)
- Remote players see the committed state only, not bolt animations; "in progress" is shown as a reservation.
- Rows 2/3/4/5 integrate through `CarPartsSync.MarkDirty(loaderId, part)` and `UploadBaseline(loaderId)`; parts on
  tools (engine crane/stand) are out of the registry's reach while detached and are row 5's.
- Server dispatch is concurrent today; this change introduces `GameDataManager.StateLock` and asks other rows to
  use it.
Deferrable unknowns:
- Exact item IDs produced for body parts on unmount (spike 3.1 settles them before D3 is coded).
- Whether `PartScript.OnHidePartFinished`/`OnMountFinished` fire reliably; if so they can replace polling in D2.
