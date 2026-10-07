# Design

Revised 2026-10-07 after `review.md` (B1–B3, M1–M7, minors). The review items each decision answers are named in
brackets, for example [B2]. The resolution of every item is in `review.md` under "Resolution".

## Context

See proposal.md for the motivation (playtest 2026-10-07). State on `main` (`ba20816`):

- **Claims (row 1 D5).** `PartClaims` (client) puts Harmony prefixes on `PartScript.ActionUnMount/ActionMount`,
  `CarLoader.TakeOffCarPart(string)` and a postfix on `CanTakeOffCarPart`. If the mirror shows another holder, it
  blocks with a toast. Otherwise it sends `CarPartClaim { loader, SpawnSeq, keys = part + GetUnmountWith() }` and
  **lets the action run at once**. `UndoMounting`/`UndoUnMounting` postfixes release. The server's `CarClaims` grants
  if no key is held by another, broadcasts `CarPartClaimUpdate`, answers a loser with an update that names the holder
  (the loser's action is already running), releases on commit (`ReleaseCommitted`), on `PresenceEvents.Left`, on
  leaving the garage and after 120 s.
- **Commit (row 1 D3/D4).** `PartChangeTracker` polls every 0.1 s and sends a `CarPartsChange` after 3 stable polls.
  The change carries preconditions (`wasUnmounted` per key) and an `InventoryDelta`. The part transaction opens in the
  `DoMount` postfix. `FindConflict` rejects a precondition mismatch and a removal of an item another client removed.
  A removal of a UID the server never had is ignored, because the game builds new groups while mounting (a caliper
  with its piston) that never reach the server (`035887c`, `e26d219`). The loser gets the stored records and
  `RestoreUids` and rolls back. That rollback is where finding 4 desynced.
- **Lifts and moves (row 2).** `LifterSync` runs `CarLifter.Action` locally and then sends `LifterActionRequest`.
  Remote clients animate the step later, in `LifterSync.Apply`, one step at a time. `CarPlacementSync` lets
  `NotificationCenter.<ChangeCarPos>d__20` run and sends `CarPlaceChangeRequest`. Parking from the garage
  (`ParkingHandlers.ParkFromGarage`), job end (`JobsService`) and car delete (`CarHandlers`) clear the loader and
  check only the away claim. None of them checks part claims, which is finding 1.
- **Fluids (row 4).** Fluids are a 1 Hz polled section with a 0.5 s flush delay (`CarDetailsSync`), last write
  wins. Its `Flush` is private and does not send while the loader is `awaiting` or `applying`. Refill is
  `FluidRefill.Use` → per-frame `FluidRefillLogic.Update` while the button is held → `FluidRefill.Hide` (money). The
  extractor drains in `FluidExtractor.<UseAnim>d__5`. The oil bin runs `ToolsManager.<UseOilDrain>d__40`, started
  inline by `CarLoader.UseOilbin`. Unmounting a container drains its fluids in the first step of
  `PartScript.<Hide>d__159`. `FluidsData` is a struct and must not be patched.
- **Away (row 13).** `CarAwayRegistry` holds one runtime away claim per car. It refuses a request while another
  player holds part claims (`InUse`), and releases the requester's own claims on the grant.
- **Machines (row 5a).** `ToolSync`/`ToolsStore` lock a machine (the balancer) with their own claim.
- **Visuals (row 17 part 1).** `PartClaims.ClaimChanged(loader, keys, owner, fromSnapshot)` drives `BoltReplay`,
  `PartGhosts` and `ActivityCapture`, which use `keys[0]`. The release arrives before the commit in the same frame.
- **Guard (row 14a).** `GuardHooks` toggles pie options in a `PrepareIcons` postfix, refuses in
  `CheckSelectedOption`, and runs first on `GameMode.SetCurrentMode`.
- **Static facts** (row 1 design, native xref, the review's decompile reads):
  - `ActionUnMount` ← `Raycast.PartSelect`. It returns early when `canMountUnmountOnlyOnCarLoader && !IsOnCarLoader`,
    when `!canBeUnmount` (error sound + `Cursor3D.ResetButton`) and when `CheckSendMessage()` is true. On success it
    sets `Cursor3D.isHoldInProgress = false` and calls `GameScript.SelectToUnMount`.
  - `ActionMount` ← `Raycast.PartSelectMount`. It calls `GameScript.SetPartMouseOver` and opens
    `ChoosePartUpWindow`. `DoMount` ← `GameScript.SelectPartToMount(BaseItem)`. The chooser builds groups in
    `SelectItemInCreateGroup`/`SubmitGroupItem`.
  - `CarLifter.Action` returns while `isMoving` and sets game mode 7. `FluidRefill.Use` returns when no car is under
    the cursor.
  - `TakeOffCarPart`/`CanTakeOffCarPart`/`SwitchCarPart` ← `GameScript.ClickIO`. `UndoMounting`/`UndoUnMounting` ←
    `GameScript.CleanUnfinishedMount`/`CleanUnfinishedUnMount`. Crane out = `NotificationCenter.ActionUnMountGroup`.
  - `PartScript.unblockOnUnmount` is serialized. `blockedBy` is a private runtime list that `UnblockBlockParts`
    changes on hide and mount.
  - `CarFluid { FluidType, ID, HasReservoir }`, `CarPart.ConnectedParts`, `Cursor3D.fillTime`/`holdTime`/
    `GetIsButtonHold()`, `GameScript.SelectedToMount`, `SetPartMouseOver`, `GetRaycastOnItemName`,
    `InteractiveObject.SetMouseOver(bool, Color)` and `ChoosePartUpWindow.Show(List<BaseItem>, …)` exist in `dump.cs`.
  - None of the hooked methods takes `NewCarData` or `FluidsData` by value.

## Goals / Non-Goals

**Goals:**
- No two players ever run conflicting work on one car. "Conflicting" covers the same part, a part and the part it is
  fixed to, a fluid and the parts that hold or gate it, the car's position or its removal and any work on it, and the
  same inventory item.
- A player sees the conflict before acting (hover highlight and label), and a click on a part in use is refused at
  once, without a round trip.
- The server is the only referee. A stale client mirror can cost a denial, never a rollback.
- Every lock ends: on success, on cancel, on an action that did not start, and on a player backing out.
- No visible delay for hold actions, and at most one round trip for the rest. A lost answer never leaves the player
  stuck.
- `main` keeps working claims or working locks after every commit of this change [B3].

**Non-Goals:**
- Engine-stand parts (open question 3), machines (row 5a keeps its own claim), car tools such as the welder, paint,
  wash and detailing (open question 4), examine and diagnostic tools (read only).
- Queuing a denied request until the lock frees. The player retries.
- Mount-mode previews, item-chooser filtering and pie greying move to `part-locks-2` if open question 7 keeps its
  default (D9). Clicking a locked slot in mount mode is still refused by the gate here.
- Fixing the rollback of finding 4 itself. Locks make it unreachable for mounts, and the remaining reject path
  (unlocked harness verbs, machines) is row 1's to fix.
- Persisting locks. They are runtime only, and a server restart disconnects everyone anyway.

## Decisions

### D1. Strict lock before the action: block, request, re-invoke on grant

Each gated entry point gets a prefix that runs `LockGate.Enter(kind, target, args)`:

1. **Local refusal.** If the mirror (D6) shows a conflict, or a local lifter of that car is moving (D7) [M3], refuse:
   error sound, message (D9), `Cursor3D.ResetButton()` as vanilla does on its own refusals [M1]. No request is sent.
2. **Prefetched or chained lock.** A lock of this client counts only in two cases [minor 4]:
   - it was prefetched for the same kind and target at hold start (D10);
   - the call is the chained step of the same lock (`SelectPartToMount` after that slot's `ActionMount`), which is
     sent as an `ExtendLockId` request (D3) and waits like any other request.

   A lock whose work has already produced a change with an X flip is marked *ending* and is never reused.
3. **Request.** Otherwise return `false` (vanilla does not run), call `Cursor3D.ResetButton()`, send
   `CarLockRequest`, and keep a `Pending { RequestId, Kind, Target, Action, Context }`. A repeated call for the same
   kind and target while a request is pending is swallowed: no new request, no message [M1]. A call for a different
   target cancels the pending request; if that grant arrives later, it is released.
4. **Grant.** If `Context` still holds, run `Action` (the same vanilla method with the same arguments) inside
   `LockGate.Bypass(lockId)`, then check the kind's **started** predicate (D5 table) at once [B2]. If it is false,
   release the lock immediately. If `Context` no longer holds, release and do nothing.
   - `Context` covers the game mode, the target object still alive, the target's mount state, the car's `SpawnSeq`
     and the top window.
   - "Button still held" is part of `Context` only for actions whose effect lasts as long as the hold (the refill
     pour). It is not part of it for actions that fire when the hold completes, so a normal click works [M1].
5. **Denial.** Error sound and message.

Postfixes on gated methods run even when the prefix returns `false`, which is now the first call of every action.
Every existing postfix on a gated method must check `__runOriginal` [M6]. The audit list goes in
`docs/spikes/part-locks.md`:

| Postfix | Change |
|---|---|
| `EngineCraneHooks.AfterUnMountGroup` | must check it; today it sends an inventory Add for any engine group of that name |
| `PartHooks.AfterTakeOffCarPart` | harmless, made consistent |
| `LifterSync.AfterAction` | harmless (state based), made consistent |
| harness trace patches | made consistent |

Entry points that are `IEnumerator` methods (`PartScript.UnMountByGroup`, the `ChangeCarPos` body) are never blocked
by a `false` prefix. They are gated at their void caller or at the coroutine's first `MoveNext` (state 0, as
`CarPlacementSync` does today), and restarted with the same arguments on grant.

**Alternatives considered:**
- *Optimistic claim with rollback (today).* Rejected. It is what failed in the playtest.
- *Lock on hover.* Rejected. Merely looking at a part would take it from others, with a lot of lock churn. Prefetch
  at hold start (D10) is different: a hold is a deliberate click on that part.
- *Optimistic start, commit held until the grant.* Rejected. Bolts would turn and then vanish for the loser.
- *Server-side validation of the commit only.* This is the safety net (D8), not the mechanism.

### D2. Lock model: shared/exclusive keys per car scope, plus global item keys

A lock is `{ LockId, Owner, Loader, SpawnSeq, Kind, X (exclusive keys, main object first), S (shared keys), Items
(exclusive item UIDs), LinkedLockId, Since, RenewedAt, Phase }`. Keys live in one car's scope:

| Key | Meaning |
|---|---|
| `b:<index>` | body part (`CarPart`), as today |
| `s:<path>` | mechanical part (`PartScript`) by sibling-index path, as today |
| `f:<CarFluidType>.<id>` | one fluid of the car, for example `f:EngineCoolant.0`; oil is `f:EngineOil.0` |
| `car` | the car's position and presence: place, lift, away, park, delete, job end |

Items (`i:<uid>` in logs) are global, because the inventory is shared.

Compatibility between locks of **different owners**: X/X conflicts, X/S conflicts, S/S is compatible, and two locks
that share an item conflict. Locks of the same owner never conflict. A swap of two cars is two linked records, one
per loader, granted or denied together (`LinkedLockId`) [minor 5].

The order of X matters: the main object comes first, then the members, because `BoltReplay` and `ActivityCapture`
read `keys[0]`.

**Alternatives considered:**
- *All keys exclusive* (open question 1). Simpler, but two players could not unscrew two bearing caps of one
  crankshaft.
- *One lock per car.* Far too coarse for two to four players on one car.
- *Per-key exclusive only (today).* Misses connected parts, fluids and the car position.

### D3. The lock set of an action

`LockSets` (client) computes the set from the car's `PartRegistry` and serialized game fields only, cached per loader
and `SpawnSeq`. The same function serves the request, the local refusal and the hover check, so they always agree.

**Blocking relation [minor 2].** It is built from `unblockOnUnmount` (serialized) plus a reverse index built once per
registry, which covers both directions. It is never built from `blockedBy`, which changes with mount state.

**Ancestors** are compared by path segments: `3.22` is an ancestor of `3.22.4`, `3.2` is not [nit].

| Action (`CarLockKind`) | X | S |
|---|---|---|
| `PartUnmount`, `PartMount` on part P | the work group W: P's main object (`GetUnmountWithMainObject()` ?? P) first, then its `unmountWith` members | for every member of W: its `PartScript` ancestors in the registry; its blocking neighbours (both directions); its fluids (D4); `car` |
| `PartMount` item step (`SelectPartToMount`, `ExtendLockId`) | unchanged | unchanged; plus Items = the `BaseItem`'s UID, and for a `GroupItem` its group UID and member UIDs [B1] |
| `GroupUnmount`, `GroupMount` | every part of the group (`GameScript.GetUnmountGroup()`/`ForcePartGroup`, spike 1.2) | as for parts |
| `BodyPart` (take off or mount `b:i`) | `b:i` | `b:j` for each name in `ConnectedParts`; `car` |
| `Fluid` (refill, extractor) | `f:T.id` | `car` |
| `OilDrain` (oil bin) | `f:EngineOil.0` | `car` |
| `Crane` (engine out/in) | the engine root `s:<path>`; for "in", Items = the engine group UID | `car` |
| `Lift`, `Move` | `car` (a swap: a second linked record for the other loader) | — |

A key in both lists stays in X only.

What this covers:
- **Children** without listing them. Every lock on a descendant holds its ancestors shared, so an X on a part
  conflicts with any work below it. The crane's X on the engine root conflicts with all engine work.
- **Fixed-to neighbours.** The game's own blocking relation is what makes it refuse "remove the crankshaft before
  the caps", so those two conflict.
- **Siblings** share only S keys and stay compatible.

Spike 1.3 dumps the relations of three cars and measures the sets. If a set covers more than 40 keys, or makes two
parts the user would expect to be independent conflict, the rule is narrowed (design.md updated in the same commit).

### D4. Fluid systems

- `CarFluid` components below each `PartScript` give the key `f:<FluidType>.<ID>` (the row 4 spike: `ID` equals the
  list index). Oil has no `CarFluid`. Its parts come from `FluidRefillLockType == EngineOil` and the drain plug
  `korek_spustowy_1` (spike 1.3).
- A part takes its fluids **shared** when it does any of the following. Two players can unmount two coolant hoses
  together, and neither can start while someone fills coolant.
  - contains one (`IsFluidContainer()` or a `CarFluid` below it);
  - gates a refill (`FluidRefillLockType`; `All` means every fluid key of the car);
  - drains one when it is unmounted (`<Hide>d__159` zeroes coolant, washer and power steering fluid; spike 1.3 maps
    which part drains which fluid).
- Refill, extractor and oil bin take the fluid **exclusively** from the start of use to its end.
- **Flush before release [M5].** New API `CarDetailsSync.FlushNow(loader, sections) → FlushResult { Sent, Deferred }`.
  - `Sent`: the section was read and sent now.
  - `Deferred`: the loader is `awaiting` or `applying`. The caller waits until it can send (checked every frame, at
    most 1 s), then sends anyway and logs.

  It is called:
  1. before the release of a refill, extractor or oil-bin lock;
  2. in `PartChangeTracker.Send`, before the change is sent, whenever the change flips a part whose own lock holds an
     `f:` key.

  The details packet then travels ahead of the change on the same ordered stream, and therefore ahead of the
  release. The next holder starts from the drained level, not from the next 1 Hz poll.
- The user's example: the coolant reservoir holds `f:EngineCoolant.0` shared, and filling coolant needs it
  exclusive. They exclude each other in both orders, and the reservoir's drain is on the server before anyone can
  fill.

### D5. Server lock table and lock lifecycle

`CarLocks` stores runtime state only, under `GameDataManager.StateLock` (held by the dispatcher): `locks[LockId]`, a
per-loader index `key → (X owner | S owners)`, and `items[uid] → LockId`. Nothing is saved. Until the switch-over
task (5.1) it runs beside `CarClaims` and is fed only by the harness [B3]. Until then, every server check asks both
tables.

**Grant** (`CarLockRequest { RequestId, CarLoaderID, SpawnSeq, Kind, X, S, Items, ExtendLockId, OtherLoaderID,
OtherSpawnSeq }`):
1. Drop if the car has no baseline or the `SpawnSeq` differs (`Stale`).
2. Validate: every `b:`/`s:` key exists in `CarPartsStore` (`Invalid` otherwise), and `f:` keys are well formed.
   **Items:** a UID the server does not know is not a refusal. It is logged and left out of the lock, because the
   game builds mount groups the server never sees (the rule of `035887c`) [B1]. Only known UIDs are locked.
3. Derive: add `car` to S (unless it is in X), and add the stored ancestors (by path segment) of every `s:` key in X
   as S. With `lock_scope = part`, drop every S key except `car` instead.
4. Conflicts: another player's away claim counts as X on `car` (refusal `Away`). Then apply the D2 compatibility
   table against other owners' locks and items. The first conflict is reported.
5. On grant: store the lock (two linked records for a swap), answer `CarLockResult { RequestId, LockId, Granted }`,
   and broadcast `CarLockUpdate` to every client, the owner included. `ExtendLockId` merges the new keys and items
   into the owner's existing lock and moves it to its next phase.
6. On denial: `CarLockResult { Granted = false, Refusal, HolderPlayerId, ConflictKey }` to the requester only.

**Lifecycle per kind [B2].** "Started" is checked by the client right after the re-invoked call returns. A lock
that did not start is released at once. Spike 1.2/1.4 confirms each predicate and signal:

| Kind | Started when | Ends when (normal) | Backed out when | Idle cancel |
|---|---|---|---|---|
| `PartUnmount` | the game holds the part as selected to unmount (`GameScript.SelectedPart`, or the mode became `PartUnMount`) | every X key reached its target state on the server, or the client's tracker sent the last X flip [minor 3] | `UndoUnMounting` (from `CleanUnfinishedUnMount`); ESC out of the bolt view; a mode change the action did not set itself [minor 8]; leaving the garage | 5 min without bolt progress |
| `PartMount`, slot phase | `ChoosePartUpWindow` is shown | moves to the item phase on `SelectPartToMount` | the chooser closes (`Hide`/back, spike 1.2) without a `SelectPartToMount`; a mode change; leaving the garage | 60 s in the chooser |
| `PartMount`, item phase | `DoMount` started (the part transaction opened) | as `PartUnmount` | `UndoMounting`; a mode change; leaving the garage | 5 min without bolt progress |
| `GroupUnmount`/`GroupMount` | the group mode is entered | as `PartUnmount` | `CleanUnfinished*`; a mode change | 5 min |
| `BodyPart` | the panel's `TakeOnOffInProgress` is true or its `Unmounted` flipped | its X key reached the target state | not started | — |
| `Fluid` refill | `FluidRefill` is active (`ExamineTool.IsActive`) | `FluidRefill.Hide`, after `FlushNow` | `Hide` without a level change | — (hold) |
| `Fluid` extractor | `<UseAnim>d__5` started | its last `MoveNext`, after `FlushNow` | — | — |
| `OilDrain` | the first `MoveNext` of `<UseOilDrain>d__40` yielded (state 0 returns `false` when there is no oil or plug) | its last `MoveNext`, after `FlushNow` | — | — |
| `Crane` | `ActionUnMountGroup`/`InsertEngineToCar` ran | the engine change is committed | — | — |
| `Lift` | `isMoving` became true | the local `isMoving` falls (30 s cap) | not started | — |
| `Move` | the coroutine advanced past state 0 | the coroutine ended | not started | — |

**Mode changes** release a lock only when the new mode is not one the gated action sets itself (the unmount view,
the extractor's mode 0x17, mode 7 from `CarLifter.Action`), and only after the guard has let the change through.
The guard's prefix runs first [minor 8].

**Server release** (one `CarLockUpdate { OwnerPlayerId = -1 }` per released record):
- `CarLockRelease { LockId }` from the owner;
- an accepted `CarPartsChange` after which every X key of the sender's lock is in its target state. The release is
  broadcast **before** the change is relayed (row 17's order). Attribute-only changes do not release;
- `PresenceEvents.Left`, `SceneChanged` away from the garage, `CarPartsStore.LoaderCleared`, a new `SpawnSeq`;
- the away grant (row 13), which releases the requester's own locks on that car, as it does with claims today
  [minor 9];
- expiry: `lock_expiry_seconds` (server setting, default 90) without `CarLockRenew { LockIds }`. Clients renew every
  `expiry / 3` seconds [M7];
- `Lift`/`Move` records end 30 s after the grant at the latest.

The client sends its release from the lifecycle table. The server's commit release is the fast path.

API for other server code: `CarLocks.HeldByOther(loader, client)`, `ConflictFor(loader, client, keys)`,
`OwnerOfCar(loader)` and `Describe(now)`. The `locks` console command lists the locks and the counters: granted,
denied by refusal, expired, released by commit, `unlockedFlip` (D8), and `overlapViolations` (an audit after every
grant re-checks the whole table; it must stay 0). `--check-locks` runs the D2/D3 rules on a synthetic car in-process,
in the pattern of `--check-jobs`.

**Alternative considered:** keeping `CarClaims` and adding a second table for the extra keys. Rejected as the end
state: two tables could each grant half of a conflict. During the switch-over, both are asked.

### D6. Client mirror and the `PartClaims` view

`CarLockMirror` keeps every lock record from `CarLockUpdate`, indexed like the server's per-loader index. It is filled
from the snapshot (D11), cleared by `ClientData.Reset`, per loader on car delete or a `SpawnSeq` change, and on
leaving the garage. Leaving the garage also drops the pending request and the own-lock bookkeeping, not only
disconnect and resync [minor 10]. It answers `Conflict(loader, lockSet) → (holder, key)`.

`PartClaims` keeps its public API as a view over the mirror. `Held`/`OwnerOf`/`HeldByOther` report the X part keys
of every lock. `ClaimChanged(loader, keys, owner, fromSnapshot)` is raised with the X part keys (main object first)
when a lock is granted or released, which is what row 17 expects. Its Harmony patches and `PartClaims.Claim` go away
in the switch-over task, together with the server's `CarClaims` [B3]. The harness gets `lock-take` (a bare request,
no action) for the scenarios that used `part-claim` as a reservation (`car-live`, `economy-trades`, `test-drive`)
[minor 13].

`ClientDigests` skips a car only while this client holds a lock on it or has an unconfirmed change, not whenever
anyone holds a lock. Locks now last longer, and four busy players would otherwise keep a car out of the digest check
[minor 11]. `CarAwaySync` stays as it is, and the mirror treats an away claim as X `car`.

### D7. Car-level lock: lifts, moves, park, delete, job end, away

- **Lift.** The `CarLifter.Action` prefix gates with `Kind = Lift` on the lifted car. With no car on the lifter there
  is no lock, as today. On grant the action runs and `LifterActionRequest` is sent as today. The release comes when
  the local `isMoving` falls (30 s cap).
- **Local moving check [M3].** Remote clients finish a lift step later than the requester. Each client therefore
  also refuses part work locally (gate step 1 and the hover check) while any local `CarLifter` connected to that car
  `isMoving`, or while `LifterSync`/`CarPlacementSync` is applying a remote step or move for it. This needs no
  protocol and closes the window on every client.
- **Move and swap.** `_ChangeCarPos_d__20.MoveNext` state 0 is blocked (`__result = false`), the `Move` lock is
  requested (two linked records for a swap), and on grant `ChangeCarPos(carLoader, pos, movePlayerToCar)` is started
  again on `NotificationCenter` (spike 1.5). The release comes when the coroutine ends.
- **Park, delete, job end [M4].** The server refuses `ParkFromGarage`, `CarDelete` and the job end that clears a car
  while another player holds a lock on that car. The refusal is `Busy`, sent through each feature's existing refusal
  path, and the client shows "<name> is working on this car." For job end this means the finishing player waits for
  the other player to finish or cancel. That is the user's "the car does not move while someone works on it".
- **Server safety net.** `OnLifterAction` and `OnCarPlaceChange` refuse when another player holds a lock on the car
  (or on the swap partner) and the sender holds no `car` X lock.
- **Away (row 13).** `CarAwayRegistry.OnRequest` asks `CarLocks.HeldByOther` (the same `InUse` refusal). An existing
  away claim counts as X `car`.
- **Economy.** Car sale asks `CarLocks.HeldByOther`.

Finding 1 can no longer happen: the lift is refused while the friend's mount lock holds `car` shared, and the mount
is refused while the lift's X lock holds or the lift is still moving on the friend's screen.

### D8. Interplay with the commit path, inventory transactions and machines

- **`FindConflict`** keeps both checks (preconditions, items removed by another client) as the safety net. It adds
  one rule: a record whose `Unmounted` flips on a key that another player holds in **X** is rejected ("locked by
  player N").

  A flip on another player's **S** key is accepted, counted as `unlockedFlip` and logged. Such flips come from game
  side effects, and rejecting them would run the fragile rollback again [minor 1]. Changes from unlocked paths
  (`FastUnmount`/`FastMount` harness verbs, tool overlays) are accepted when no other player holds the key in X, so
  `car-race` keeps testing the old safety net.
- **Inventory.** `PartTransactions` and `InventoryDelta` are unchanged. Item locks prevent the "same item mounted
  twice" race for mounts, including a group the game builds in the chooser (its known member UIDs are locked) [B1].
  Other consumers of items (machines, sale, scrap) do not ask the item lock; `RemovedByOther` still catches them.
- **Machines (row 5a)** and the balancer claim are separate.
- **Crane (row 5b).** The gate with `Kind = Crane` sits on `ActionUnMountGroup(InteractiveObject)` and
  `InsertEngineToCar(GroupItem)`. `EngineCraneHooks.AfterUnMountGroup` checks `__runOriginal` (D1).
- **Oil bin (row 5b).** The `CarLoader.UseOilbin()` prefix gates with `Kind = OilDrain`. The release comes on the
  final `MoveNext` of `<UseOilDrain>d__40`, which `OilBinHooks` already observes.
- **Resync (F7).** `ResyncController` releases this client's locks and pending request before the reload. The
  snapshot refills the mirror.
- **Guard (14a).** The guard's prefixes run first. A gated action the guard refuses never sends a request. No guard
  entry is added.
- **Visuals (17).** No change beyond D6.

### D9. Blocked at selection, and what the player sees

Every check here is local and read only, and uses `LockSets`, `CarLockMirror` and the local moving check. Own locks
are exempt, because `ActionMount` itself calls `SetPartMouseOver` on the caller's own locked part [M2].

**In this change:**

| Where | Hook (spike 1.1 confirms each fires on the real path) | Effect while another player's lock conflicts |
|---|---|---|
| Hover highlight | `PartScript.SetMouseOver(bool)` and `InteractiveObject.SetMouseOver(bool, Color)` prefixes skip the highlight. **`GameScript.SetPartMouseOver` is left alone**: it is the game's "what is under the cursor", and the label and the click need it [M2] | no highlight (or orange, open question 5) |
| Hover label | `GameScript.GetRaycastOnItemName()` postfix (or `UpdateRaycastOnItemName()` if the getter is inlined) | "<name> is working on this part" / "… on the <part>" |
| Interior parts | the same hooks, reached from `Raycast.InteriorDisassemble`/`InteriorAssemble`/`PartUnMountPartMount` [minor 12] | as above |
| Click | the gate (D1 step 1) | error sound, message, `Cursor3D.ResetButton()`, no request |
| Body panels | `CanTakeOffCarPart` postfix returns `false`. It does not set the `out TakePartOffLockReason`, because the game's reasons have no "another player" text and the game would show its own wrong message | the game does not offer the take-off; the gate shows the message |

**In `part-locks-2` (open question 7, default):**

| Where | Approach |
|---|---|
| Mount mode | `ShowPreviewToMount`/`ShowGroupPreview`/`ShowPreview` prefix, `HidePreview` on a new lock, re-show on release |
| Item chooser | filter the input list of `ChoosePartUpWindow.Show(List<BaseItem>, …)`, not the UI rows [minor 7]. A denied item step reopens the chooser with the message, if the window has already closed |
| Pie menu | one shared owner of pie option state for the guard and the locks, set in `PrepareIcons` (`SetEnableOption`) and refused in `CheckSelectedOption`, so the two never restore each other's values. `GetIsAvailable` is private and returns an IL2CPP `Func<bool>`, so it is not wrapped [minor 6] |

Without `part-locks-2`, these paths are still safe: a click on a locked slot or a pie option is refused by the gate
or the server with the message.

Messages (`LockMessages`, one place; the same text at most once per 2 s):

| Case | Text |
|---|---|
| same part | "<name> is working on this part." |
| connected part | "<name> is working on the <localized part name>." |
| fluid | "<name> is working on the <fluid> system." |
| car position | "<name> is working on this car." (lift, move, park, delete or job end refused) / "<name> is moving this car." (work refused during a move or lift) |
| away | row 13's existing message |
| item | "<name> is mounting this part." |
| waiting > 150 ms | "Waiting for the server…" in the hover label (toast if no label) |
| no answer after 3 s | "The server did not answer. Try again." |
| car not ready | the existing "This car is still loading for multiplayer." |

### D10. Latency and lost answers

- **Budget.** One round trip plus server time. Under 5 ms on the test lanes, 30–100 ms over Steam relay in the
  playtest. A grant is a few dictionary lookups on sets under 40 keys.
- **Prefetch at hold start (main design for hold actions) [M1].** The unmount click is a hold with a fill ring
  (`Cursor3D.fillTime`/`holdTime`). When a hold starts over a free part in a part mode, the client requests the lock
  for that part at once. The fill time is longer than a relay round trip (spike 1.6 measures it), so the grant
  usually arrives before the hold completes. `ActionUnMount` then finds the prefetched lock (D1 step 2) and runs with
  no visible wait.
  - A hold that is aborted or moved off the part releases the prefetched lock.
  - A grant that arrives after completion is used through the normal pending path.
  - Prefetch never shows a denial on its own; the denial shows when the hold completes.

  Actions without a hold (lift button, pie options, refill start) wait one round trip.
- **Lost answer.** After 3 s the pending action is dropped with the message. A grant that arrives later for a
  dropped `RequestId` is released at once, and a later denial is ignored.
- **Disconnect, leaving the garage, resync.** Pending requests are dropped and own bookkeeping is cleared (D6). The
  server releases the locks.
- **Lost renews.** A client that stops renewing loses its locks after `lock_expiry_seconds`. A later commit then goes
  through `FindConflict` like any unlocked change.

### D11. Late join, return to the garage, server restart

Locks are part of the `cars` snapshot (SyncOrder 100). For each car, after its records, `CarsSnapshotProvider` sends
one `CarLockUpdate` per active record (after the switch-over, instead of `CarPartClaimUpdate`), counted in the car's
item. The joiner's mirror is complete before `SyncEnd`. `ClaimChanged` fires with `fromSnapshot = true`, so no bolt
animation replays. A return to the garage uses the same path. Lock updates that arrive while a client is away are
mirror-only (`ClientScene.GarageBound(mirrorOnly)`). After a server restart the table is empty, and every client
reconnects through the late-join path.

### D12. Packets (appended to `PacketTypes` at merge time)

```
CarLockRequest  { int RequestId; int CarLoaderID; int SpawnSeq; CarLockKind Kind; List<string> X; List<string> S;
                  List<long> Items; int ExtendLockId = 0; int OtherLoaderID = -1; int OtherSpawnSeq; }   C→S
CarLockResult   { int RequestId; int LockId; bool Granted; CarLockRefusal Refusal; int HolderPlayerId;
                  string ConflictKey; }                                                                    S→requester
CarLockUpdate   { int LockId; int LinkedLockId; int CarLoaderID; int SpawnSeq; int OwnerPlayerId /* -1 = released */;
                  CarLockKind Kind; byte Phase; List<string> X; List<string> S; List<long> Items; }       S→all
CarLockRelease  { int LockId; }                                                                            C→S
CarLockRenew    { List<int> LockIds; }                                                                     C→S
enum CarLockKind    { PartUnmount, PartMount, GroupUnmount, GroupMount, BodyPart, Fluid, OilDrain, Crane, Lift, Move }
enum CarLockRefusal { None, Held, Away, NotReady, Stale, Invalid, Item, CarBusy }
```

Changed: `ServerInfo` gains `LockScope` (`connected` or `part`), so the client's `LockSets` and hover checks use the
server's rule. It is marked `[OptionalField]` like the other additions. That is moot while the protocol hash forces
equal builds, but harmless.

After the switch-over, `CarPartClaim` and `CarPartClaimUpdate` keep their enum values (append-only) but are no longer
sent. Each `CarLockUpdate` is a full record, so every apply is idempotent and late join uses the same code path. A
request with 30 keys is under 500 bytes.

### D13. Harness verbs, dump and scenarios

**Verbs** (owner row 18; registered in INTEGRATION.md):
- `lock-trace on|off|report` (spike, logging only): order and arguments of the hover, selection, chooser, fluid,
  lift and move hooks, `SelectedToMount`, game mode and `Cursor3D` hold state. `lock-trace relations <loader>`
  dumps the relations and computed sets (spike 1.3).
- `lock-take <loader> <kind> <key…> [release]`: a bare request without an action (replaces `part-claim`).
- `lock-try <loader> <kind> <target> [hold|finish|release|nogate]`: drives the entry point through the gate. Kinds:
  `unmount <key>`, `mount <key> <itemUid>`, `body <index>`, `fill <type> <id>`, `drain <type> <id>`, `oil`,
  `crane-out`, `lift <lifter> up|down`, `move <place>`. It reports `{ result: granted|denied|timeout|context-lost|
  not-started, holder, conflictKey, waitedMs, ran, started }`.
  - `finish` completes the started action under the held lock, so a commit and the release on commit really happen
    [M7]. It uses `PartScript.ActionAutomatic` (`_ActionAutomatic_d__106`) or `FastUnmount`/`FastMount` inside the
    bypass; spike 1.4 picks per kind.
  - `hold` keeps the lock after the start.
  - `nogate` (lift and move) sends the row 2 request without a lock.
- `lock-click <loader> <key> [hold <ms>]` (spike 1.7): aims the player camera at the part and feeds the button state
  to `Cursor3D` (harness patch on `GetIsButtonHold`/`GetIsButtonClick`), so the game's own `Raycast.PartSelect`,
  hover and hold paths run. If 1.7 fails, the input-driven checks stay in the manual checklist (task 11.3).
- `lock-chooser <loader> <key> open|close`: opens the item chooser through `ActionMount(true)` and closes it the way
  ESC does (back-out path, B2).
- `lock-hover <loader> <key|b:index>`: runs the hover path and reports `{ highlighted, label }`.
- `lock-release <loader> [all]`, `lock-renew on|off`, `lock-idle <bolt seconds> <chooser seconds>` (test override of
  the idle cancel).
- **Dump section `locks`:** the mirror per loader (`lockId`, `owner`, `kind`, `phase`, `x`, `s`, `items`), `pending`,
  and counters (`requested`, `granted`, `denied.<refusal>`, `timeouts`, `lateGrantsReleased`, `notStarted`,
  `backedOut`, `idleCancelled`, `prefetched`, `refusedLocally`, `blockedAtSelection.<where>`). The existing
  `cars[].claims` field stays (from the view).
- **Server:** `Send-ServerCommand locks`; `overlapViolations` must be 0 in every scenario. The server settings
  `lock_expiry_seconds` and `lock_scope` are set per scenario with `Set-ServerConfigValues`.

Existing helpers reused: `net-delay`, `net-hold`, `lift`, `car-move`, `park`, `car-delete`, `job-finish`,
`cardetails-fluid`, `tool-use`, `crane-out`, `part-fast-unmount`, `vfx-unscrew`.

**Scenarios** (area `locks`; each also lists the areas it touches):

| Scenario | What it checks |
|---|---|
| `locks-race` | Same part started together (jobs) and with `net-hold on` on both: one grant, one denial naming the holder; the winner's `finish` commits; no rejected `CarPartsChange`; `inventory`, `cars` and `locks` equal. The same item into two slots. A caliper with its piston as a chooser-built group into two calipers' slots (B1, the second half of finding 4). |
| `locks-leak` [B2] | `lock-chooser open` then `close`; `lock-try unmount` on a part with `canBeUnmount == false` (`not-started`); a refill with no car under the cursor; a lift that is already moving; a hold aborted after the prefetch. After each, the server's `locks` is empty within 1 s. `lock-idle 5 3`: a chooser left open is cancelled after 3 s, a held bolt view after 5 s. |
| `locks-connected` | Cap held → crankshaft denied naming the cap; sibling cap granted; crankshaft held → cap denied; an engine part held → `crane-out` denied; with `lock_scope = part`, the crankshaft is granted. |
| `locks-fluid` | Reservoir held → coolant fill denied, and the reverse; two coolant hoses granted together. A's reservoir unmount `finish` → the server's coolant level is 0 **before** the release arrives at B, and B's fill starts from 0 [M5]. Brake fluid fill on A, release, then B's `cardetails-fluid` equals A's at once. Oil bin held → oil filter denied. |
| `locks-car` | B holds a part → A's `lift`, `move`, `park` and `car-delete` refused, nothing moves or disappears. B holds a part on a job car → A's `job-finish` refused [M4]. A's lift granted with `net-delay 150` on B → B's unmount refused locally until B's own lift has stopped [M3]. `away-try` refused `InUse`. `lock-try lift … nogate` refused by the server. |
| `locks-select` | A holds a part → B's `lock-hover` shows `highlighted false` and the label; B's `lock-try unmount` is refused locally (`refusedLocally` +1, no request). With `lock-click` (if 1.7 works): the label shows while hovering, the click plays the refusal, and `GetPartMouseOver` is still the part [M2]. After A's release, all return to normal. |
| `locks-latency` | `net-delay 150`: a gated pie-style action starts after ≈ 150 ms; with `lock-click … hold`, `waitedMs` after the hold completes is 0 in at least 8 of 10 tries (prefetch). `net-hold out` 4 s: timeout, message, late grant released. `lock_expiry_seconds = 10` and `lock-renew off`: released within 10 ± 2 s. Holder disconnect frees the part for B at once. |
| `locks-latejoin` | B joins while A holds a part lock and an oil fill: B's `locks` equals the server's; B's hover and fill are blocked; no ghost or bolt replay starts; A's `finish` → B sees the commit and the release. |
| `locks-scale` (`# run-all: lane 3`) | Two to four instances for 3 minutes loop random `lock-try … finish` over a shared car (same part, connected parts, a fluid, the lift), half of them at `net-delay 80`. `overlapViolations` 0, no rejected `CarPartsChange` (changes are really sent through `finish`), every client's `locks` equals the server's at the end, dumps equal, median `waitedMs` under 2× the delay. |

Updated scenarios: `visual-lift` (the lift is refused while B works; after the release, no ghost or hidden renderer
remains), `visual-parts` and `visual-latejoin` (claims come from locks), `car-live`, `economy-trades` and
`test-drive` (`part-claim` → `lock-take`), `car-crane`, `tools-car-effects` (oil bin), `car-placement-race`.

**Manual checklist** for the next Steam playtest [M7], in STATUS.md and `docs/try-it.md`. These paths are only fully
real with a mouse:
- hover label and missing highlight on a friend's part;
- hold-to-unmount at about 100 ms ping (no visible wait);
- a click on a part in use (sound and message);
- chooser opened and closed with ESC (the friend can then use the slot);
- the refill pour on a car with a friend nearby;
- the lift refused while a friend works;
- parking and job end refused while a friend works.

### D14. Spikes: game methods to confirm in the IL2CPP dump and at runtime

Static (Il2CppDumper `dump.cs` + Ghidra into `native/out/locks_clean`, as in `docs/spikes/native-decompile.md`),
then a `lock-trace` run where the static answer cannot decide:

1. **Hover and selection:** `Raycast.Garage`, `PartSelect`, `PartSelectMount`, `GarageAssemble`, `InteriorDisassemble`,
   `InteriorAssemble`, `PartUnMountPartMount` and what they call: `GameScript.SetPartMouseOver`,
   `PartScript.SetMouseOver(bool)`, `SetMouseOver()`, `MouseOverGroup()`, `Flashing`,
   `InteractiveObject.SetMouseOver(bool, Color)`, `GameScript.UpdateRaycastOnItemName`/`GetRaycastOnItemName`, and
   the UI that reads the label. Decide which hook suppresses only the highlight while the mouse-over part, the label
   and the click stay intact (M2).
2. **Mount flow and back-outs:**
   - the path `ActionMount(bool)` → `ChoosePartUpWindow.Show(…)` → `SelectItemInCreateGroup`/`SubmitGroupItem` →
     `GameScript.SelectPartToMount(BaseItem)` → `DoMount`, logging the item and group UIDs at each step for a
     caliper with a piston (B1);
   - the chooser's close and back paths (`Hide`, `BackAction`/`HideAction`) and ESC out of the bolt view;
   - `CleanUnfinishedMount`/`CleanUnfinishedUnMount` callers;
   - the "started" predicate of each part kind;
   - the mode transitions each gated action sets itself (minor 8);
   - group modes and the void caller of `UnMountByGroup`.
3. **Relations:** `unblockOnUnmount`/`unblockOnUnmountName` and `AddToBlockedBy` (the reverse index),
   `CheckIsFluidContainer`, `GetFluidId`, `CheckOnSendMessage`, `<Hide>d__159`'s fluid zeroing per part, the oil
   parts, and how `CanTakeOffCarPart` uses `ConnectedParts`.
4. **Re-invocation and finish:** each D1 entry point re-invoked 150 ms later behaves like the direct call (mode,
   inventory, part state, no exception), and its started predicate. Which finisher `lock-try … finish` uses per kind
   (`ActionAutomatic`, `FastUnmount`/`FastMount`).
5. **Lift, move and pie:** how the lift's interactive object reaches `CarLifter.Action(int)`; restarting
   `ChangeCarPos(CarLoader, CarPlace, bool)` from code. Pie option ids for `part-locks-2`.
6. **Hold:** `Cursor3D.GetIsButtonHold`, `SetHoldID`, `fillTime`/`holdTime` on the unmount click: the fill time, and
   where a hold start over a part can be detected (prefetch, D10).
7. **Input shim:** whether a harness patch on `Cursor3D.GetIsButtonHold`/`GetIsButtonClick` plus a camera aimed at
   the part drives `Raycast.PartSelect` and the hover path in a headless test game (`lock-click`).

Rule for all patches: never patch `FluidsData` members or any method that takes `NewCarData` by value (it crashes the
game). Coroutine bodies are patched on their generated `MoveNext`. Inlined methods get their caller or a coarser
hook, recorded in `docs/spikes/part-locks.md`.

## Risks / Trade-offs

- **Lock sets too wide: players feel blocked on unrelated parts.**
  - Spike 1.3 measures set sizes, and `locks-connected` pins the sibling case.
  - `lock_scope = part` falls back to "same part only" without a new build. It also drops the fluid S keys, so "the
    reservoir vs a fill" is no longer protected in that mode [nit].
- **A re-invoked entry point behaves differently 100 ms later** → `Context` and the started predicate (D1, D5), spike
  1.4. Prefetch removes the gap for hold actions.
- **IL2CPP inlining: hover or label hooks never fire** → spike 1.1. The click refusal is the authoritative local
  block.
- **Lock leak (a missed end signal)** → the lifecycle table, the "started" check, the chooser idle (60 s), the mode
  exit release, renew expiry, `locks-leak`, and the server's `locks` output in every bug report (row 14d).
- **Job end, park and delete now wait for the other player** → the user's rule; the message names the player.
  Watched in the playtest checklist.
- **Item locks do not cover machines, sale and scrap** → `RemovedByOther` still catches them; a follow-up if the
  playtest shows it.
- **The input-driven paths cannot be fully proven headless** → spike 1.7's shim and the manual checklist.
- **Row 17 relies on the release-before-commit order** → kept (D5). The "all X keys reached" rule only delays the
  release for multi-part groups, until their last part commits.
- **Four players: a popular part gets many denials** → no queue (non-goal), clear messages, measured in
  `locks-scale`.

## Migration Plan

New packets are appended to `PacketTypes`. Client and server ship together (protocol hash), and nothing is migrated
in saves. The switch-over (task 5.1) is one commit: until then, claims work as today and the lock table runs beside
them. Rollback is reverting the change. The retired enum values are reused by the old code.

## Open questions / assumptions

The user's open questions are in proposal.md. Assumptions taken here without the user:

1. Requests go out at hold start (prefetch) or at the click, never at plain hover.
2. Denied requests are not queued.
3. Away claims stay in `CarAwayRegistry`, and `CarLocks` treats them as X `car`.
4. Lock records go to every client in the session (not scene-filtered). The traffic is small, and a returning player
   gets the full set from the snapshot.
5. Job end, park and delete are refused (not delayed) while another player holds a lock. The finishing player
   retries.

## Measurements

(Filled by tasks 1.3, 1.6 and 11.1: lock-set sizes per car, hold fill time, denial rate and median wait in
`locks-scale`.)
