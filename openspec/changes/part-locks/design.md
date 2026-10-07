# Design

## Context

See proposal.md for the motivation (playtest 2026-10-07). State on `main` (`ba20816`):

- **Claims (row 1 D5).** `PartClaims` (client) puts Harmony prefixes on `PartScript.ActionUnMount/ActionMount`,
  `CarLoader.TakeOffCarPart(string)` and a postfix on `CanTakeOffCarPart`. If the mirror shows another holder, it
  blocks with a toast. Otherwise it sends `CarPartClaim { loader, SpawnSeq, keys = part + GetUnmountWith() }` and
  **lets the action run at once**. `UndoMounting`/`UndoUnMounting` postfixes release. The server's `CarClaims` grants
  if no key is held by another, broadcasts `CarPartClaimUpdate`, answers a loser with an update that names the holder
  (the loser's action is already running), releases on commit (`ReleaseCommitted`), on `PresenceEvents.Left`, on
  leaving the garage and after 120 s.
- **Commit (row 1 D3/D4).** `CarPartsChange` carries preconditions (`wasUnmounted` per key) and an `InventoryDelta`.
  `FindConflict` rejects a precondition mismatch and a removal of an item another client removed (`e26d219`). The
  loser gets the stored records and `RestoreUids` and rolls back. That rollback is where finding 4 desynced.
- **Lifts and moves (row 2).** `LifterSync` runs `CarLifter.Action` locally and then sends `LifterActionRequest`.
  `CarPlacementSync` lets `NotificationCenter.<ChangeCarPos>d__20` run and sends `CarPlaceChangeRequest`. The server
  refuses a stale or away request and sends the stored state back (the requester animates back). Neither checks part
  claims, which is finding 1.
- **Fluids (row 4).** Fluids are a 1 Hz polled section (`CarDetailsIO.Polled`), last write wins. Refill is
  `FluidRefill.Use` → per-frame `FluidRefillLogic.Update` while the button is held → `FluidRefill.Hide` (money). The
  extractor drains in `FluidExtractor.<UseAnim>d__5`. The oil bin runs `ToolsManager.<UseOilDrain>d__40`, started
  inline by `CarLoader.UseOilbin`. Unmounting a container drains its fluid in `PartScript.CheckMessageOnHide`
  (`docs/spikes/car-details.md` §1). `FluidsData` is a struct and must not be patched.
- **Away (row 13).** `CarAwayRegistry` holds one runtime away claim per car. It refuses a request while another
  player holds part claims (`InUse`), and lifts, moves and part changes refuse while the car is away.
- **Machines (row 5a).** `ToolSync`/`ToolsStore` lock a machine (the balancer) with their own claim. Engine-stand
  parts use `ToolPartChange` with keys rooted at the stand.
- **Visuals (row 17 part 1).** `PartClaims.ClaimChanged(loader, keys, owner, fromSnapshot)` drives `BoltReplay`,
  `PartGhosts` and `ActivityCapture`. The release arrives before the commit in the same frame (spike 1.3 of row 17).
- **Static facts already known** (`sync-car-parts` design, native xref 2026-10-06): `ActionUnMount` ←
  `Raycast.PartSelect`; `ActionMount` ← `Raycast.PartSelectMount`; `DoMount` ← `GameScript.SelectPartToMount`;
  `TakeOffCarPart`/`CanTakeOffCarPart`/`SwitchCarPart` ← `GameScript.ClickIO`; `UndoMounting`/`UndoUnMounting` ←
  `GameScript.CleanUnfinishedMount`/`CleanUnfinishedUnMount`; crane out = `NotificationCenter.ActionUnMountGroup`.
  `ActionUnMount` redirects to `unmountWithMainObject` and calls `GameScript.SelectToUnMount(part, MountObjects)`.
- `dump.cs` adds: `GameScript.SelectedToMount` (`BaseItem`), `SetPartMouseOver(PartScript)`, `GetPartMouseOver()`,
  `UpdateRaycastOnItemName()`, `GetRaycastOnItemName()`, `IsPartAccessible(string)`, `ShowAllUnMountedGroups()`;
  `PartScript.blockedBy` (private `List<PartScript>`), `unblockOnUnmount`, `unmountWith`, `FluidRefillLockType`,
  `IsFluidContainer()`, `GetFluidId()` (private), `SetMouseOver(bool)`, `Flashing(bool, Color?)`,
  `ShowPreviewToMount()`, `ShowGroupPreview()`, `HidePreview()`; `InteractiveObject.SetMouseOver(bool, Color)`;
  `CarFluid { FluidType, ID, HasReservoir }`; `CarPart.ConnectedParts` (`List<string>`); `Cursor3D.GetIsButtonHold()`,
  `isHoldInProgress`; `PieMenuController.GetIsAvailable(string)` (returns `Func<bool>`); `ChoosePartUpWindow`
  (`items`, `currentItem`).

## Goals / Non-Goals

**Goals:**
- No two players ever run conflicting work on one car. "Conflicting" covers the same part, a part and the part it is
  fixed to, a fluid and the parts that hold or gate it, the car's position and any work on it, and the same inventory
  item.
- A player sees the conflict before acting (hover, pie menu, mount mode), not after.
- The server is the only referee. A stale client mirror can cost a denial, never a rollback.
- At most one round trip of added delay. A lost answer never leaves the player stuck.
- Row 17's visuals, row 13's away claim and the harness keep working with small, local changes.

**Non-Goals:**
- Engine-stand parts (open question 3), machines (row 5a keeps its own claim), car tools such as the welder, paint,
  wash and detailing (open question 4), examine and diagnostic tools (read only).
- Queuing a denied request until the lock frees. The player retries.
- Fixing the rollback of finding 4 itself. Locks make it unreachable for mounts, and the remaining reject path
  (unlocked harness verbs, machines) is row 1's bug to fix.
- Persisting locks. They are runtime only, and a server restart disconnects everyone anyway.

## Decisions

### D1. Strict lock before the action: block, request, re-invoke on grant

Each guarded entry point gets a prefix that runs `LockGate.Enter(action)`:

1. If the mirror (D6) already shows a conflict, refuse locally: error sound and message (D9), no request.
2. If this client already holds a lock that covers the action (for example the mount slot lock when
   `SelectPartToMount` runs), let the original run.
3. Otherwise return `false` (the vanilla call does not run), send `CarLockRequest`, and keep a `Pending { RequestId,
   Action, Context }`. `Action` re-invokes the same vanilla method with the same arguments. `Context` is what must
   still hold when the grant arrives: game mode, target object alive, the target's mount state, car `SpawnSeq`, no
   window open (`WindowManager` top), and for hold actions that the button is still held (`Cursor3D.GetIsButtonHold()`).
4. On `CarLockResult { Granted }`: if `Context` still holds, run `Action` inside `LockGate.Bypass(lockId)`. The
   prefix sees the bypass and lets vanilla run. If the context changed, release the lock at once and do nothing.
5. On a denial: error sound (`SoundManager.PlaySFX("Error")`, as vanilla does when `canBeUnmount` is false) and the
   message.

Only one request is pending per client. A new action cancels the old pending one (its grant, if it comes, is
released). Entry points that are `IEnumerator` methods (`PartScript.UnMountByGroup`, the `ChangeCarPos` body) are
never blocked by a `false` prefix. They are gated at their void caller or at the coroutine's first `MoveNext` (state
0, as `CarPlacementSync` does today), and re-started with the same arguments on grant.

**Alternatives considered:**
- *Optimistic claim with rollback (today).* Rejected. It is exactly what failed in the playtest: two starts in one
  round trip, then a rollback through inventory and part state, which has proven fragile.
- *Lock on hover (prefetch).* Rejected. Merely looking at a part would take it from others, and hovering produces a
  lot of lock churn.
- *Keep optimistic start, but hold the commit until the grant.* Rejected. The loser still sees bolts turning that
  then vanish, and the vanilla bolt state would have to be rolled back.
- *Server-side validation of the commit only.* This is the safety net (D8) and not the mechanism, because it can
  only reject after the fact.

Latency handling is in D10.

### D2. Lock model: shared/exclusive keys per car scope, plus global item keys

A lock is `{ LockId, Owner, Loader, SpawnSeq, Kind, X (exclusive keys), S (shared keys), Items (exclusive item
UIDs), Since, RenewedAt }`. Keys live in one car scope:

| Key | Meaning |
|---|---|
| `b:<index>` | body part (`CarPart`), as today |
| `s:<path>` | mechanical part (`PartScript`) by sibling-index path, as today |
| `f:<CarFluidType>.<id>` | one fluid of the car, for example `f:EngineCoolant.0`; oil is `f:EngineOil.0` |
| `car` | the car's position: place, lift, away |

Items (`i:<uid>` in logs) are global, because the inventory is shared across cars.

Compatibility between locks of **different owners**: X/X conflicts, X/S conflicts, S/S is compatible, and two locks
that share an item conflict. Locks of the same owner never conflict with each other. A player's own lift move does
not wait for their own part lock, and in practice the game cannot do both at once.

**Alternatives considered:**
- *All keys exclusive* (the user's accepted downside, open question 1). It is simpler, but two players could not
  unscrew two bearing caps of one crankshaft, because both locks would hold the crankshaft. Shared keys keep sibling
  work possible and cost one more list per lock.
- *One lock per car.* Rejected. It is far too coarse for two to four players on one car.
- *Per-key exclusive only (today).* Rejected. It misses connected parts, fluids and the car position.

### D3. The lock set of an action

`LockSets` (client) computes the set from the car's `PartRegistry` and game fields. The result is cached per loader
and `SpawnSeq`, and rebuilt when the registry is rebuilt (engine swap is refused while connected, so paths are
stable). The same function serves the request, the local refusal and the selection checks (D9), so hover and request
always agree.

| Action (`CarLockKind`) | X | S |
|---|---|---|
| `PartUnmount`, `PartMount` on part P | the work group W: P's main object (`GetUnmountWithMainObject()` ?? P) and its `unmountWith` members | for every member of W: its `PartScript` ancestors in the registry; its blocking neighbours (`blockedBy`, `unblockOnUnmount`); its fluids (D4); `car` |
| `PartMount` item step (`SelectPartToMount`) | the slot lock's X, unchanged | the slot lock's S, unchanged; plus Items = the item or group UID and its members' UIDs |
| `GroupUnmount`, `GroupMount` | every part of the group (spike 1.2 names the group source: `GameScript.GetUnmountGroup()`/`ForcePartGroup`) | as for parts |
| `BodyPart` (take off or mount `b:i`) | `b:i` | `b:j` for each name in `ConnectedParts`; `car` |
| `Fluid` (refill, extractor) | `f:T.id` | `car` |
| `OilDrain` (oil bin) | `f:EngineOil.0` | `car` |
| `Crane` (engine out/in) | the engine root `s:<path>` (in: plus Items = the engine group UID) | `car` |
| `Lift`, `Move` | `car` (a swap: `car` of both loaders, `OtherLoaderID`) | — |

A key in both lists stays in X only.

Why this covers "what belongs together":
- **Children** are covered without listing them. Every lock on a descendant holds its ancestors shared, so an X on
  a part conflicts with any work below it. The engine crane's X on the engine root conflicts with all engine work.
- **Fixed-to neighbours** are the game's own relation. `blockedBy`/`unblockOnUnmount` is what makes the game refuse
  "remove the crankshaft before the caps", so a race between those two is exactly the conflict to lock.
- **Siblings** share only S keys and stay compatible.

Spike 1.3 dumps these relations for three cars, measures lock-set sizes, and decides whether transform ancestry adds
anything beyond the blocking relation. If a set covers more than 40 keys, or makes two parts the user would expect to
be independent conflict, the rule is narrowed there (design.md updated in the same commit).

The server **adds** what it can derive itself and **checks** the rest (D5).

### D4. Fluid systems

- `CarFluid` components (reservoirs) are found under each `PartScript` once per registry build: key
  `f:<FluidType>.<ID>`. The row 4 spike showed `CarFluid.ID` equals the list index. Oil has no `CarFluid`. Its
  container and gating parts come from `FluidRefillLockType == EngineOil` and the drain plug `korek_spustowy_1`
  (spike 1.3 confirms).
- A part takes its fluids **shared** when it contains one (`IsFluidContainer()` or a `CarFluid` below it), gates a
  refill (`FluidRefillLockType`; `All` means every fluid key of the car), or drains one on unmount
  (`CheckMessageOnHide`, spike 1.3). Two players can unmount two coolant hoses together, and neither can start while
  someone fills coolant.
- Refill, extractor and oil bin take the fluid **exclusively**, from the use start to its end. The ends are
  `FluidRefill.Hide()`, the last `MoveNext` of `FluidExtractor.<UseAnim>d__5`, and the last `MoveNext` of
  `ToolsManager.<UseOilDrain>d__40`. Before the release, the client calls `CarDetailsSync.FlushNow(loader, Fluids)`.
  The details update and the release go on the same ordered stream, so the next holder starts from the final level
  rather than from the 1 Hz poll.
- The user's example: the coolant reservoir holds `f:EngineCoolant.0` shared, and filling coolant needs it
  exclusive. They exclude each other in both orders.

Alternative considered: a lock on the whole fluid system's parts for a fill. It was not needed, because the shared
fluid key on every system part already conflicts with the fill's exclusive key.

### D5. Server lock table (`CarLocks`, replaces `CarClaims`)

All handlers run under `GameDataManager.StateLock` (held by the dispatcher). `CarLocks` stores runtime state only:
`locks[LockId]`, a per-loader index `key → (X owner | S owners)`, and `items[uid] → LockId`. Nothing is saved.

Grant (`CarLockRequest { RequestId, CarLoaderID, SpawnSeq, Kind, X, S, Items, ExtendLockId, OtherLoaderID }`):
1. Drop if the car has no baseline or `SpawnSeq` differs (`Stale`).
2. Validate: every `b:`/`s:` key exists in `CarPartsStore` (`Invalid` otherwise); `f:` keys are well formed (type
   name and id < 16); every item UID exists in `InventoryState` (`Item`).
3. Derive: add `car` to S (unless in X), and for every `s:` key in X add each path prefix that is a stored `SubParts`
   key as S (the ancestors of D3). This guards against a client bug, not a cheating client. With `lock_scope = part`
   the server drops every S key except `car` instead.
4. Conflicts: the away claim (`CarAwayRegistry`) of another player counts as X on `car` (refusal `Away`); then the
   compatibility table of D2 against other owners' locks and items. The first conflict found is reported.
5. On grant: store the lock, answer `CarLockResult { RequestId, LockId, Granted }`, and broadcast `CarLockUpdate`
   (the full record) to every client in the session, the owner included. The owner's mirror is idempotent.
   `ExtendLockId` merges the new keys and items into an existing lock of the same owner and re-broadcasts it.
6. On denial: `CarLockResult { Granted = false, Refusal, HolderPlayerId, ConflictKey }` to the requester only.

Release (one `CarLockUpdate { OwnerPlayerId = -1 }` per released lock):
- `CarLockRelease { LockId }` from the owner (cancel, end of work, context lost, late grant);
- an accepted `CarPartsChange` that flips the mount state (`Unmounted`) of an X key of a lock of the sender. The
  release is broadcast **before** the change is relayed, keeping row 17's order. Attribute-only changes (dust,
  condition, examined) do not release;
- `PresenceEvents.Left` and `SceneChanged` away from the garage (owner's locks), `CarPartsStore.LoaderCleared`
  (all locks of the loader), and a new `SpawnSeq`;
- expiry: 90 s without `CarLockRenew { LockIds }` (clients renew every 30 s while holding), checked in the server
  tick that runs `CarClaims.Expire` today;
- `Lift`/`Move` locks also end 30 s after the grant at the latest. That covers a client that crashes mid-move
  without disconnecting.

API for other server code (replacing `CarClaims.Held`): `CarLocks.HeldByOther(loader, client)`,
`ConflictFor(loader, client, keys)`, `OwnerOfCar(loader)` and `Describe(now)`. The server prints them with a `locks`
console command, which also reports counters: granted, denied by refusal, expired, released by commit, and
`overlapViolations`. That last counter comes from an audit run after every grant, which re-checks the whole table
and must stay 0. `--check-locks` runs the compatibility and derivation rules on a synthetic car in-process (pattern
of `--check-jobs`).

**Alternative considered:** keeping `CarClaims` and adding a second table for the extra keys. Rejected. Two tables
could each grant half of a conflict, and every other row would ask both.

### D6. Client mirror and the `PartClaims` view

`CarLockMirror` keeps every lock record from `CarLockUpdate`, indexed like the server's per-loader index. It is
filled from the late-join snapshot too (D11), cleared by `ClientData.Reset`, and per loader on car delete or
`SpawnSeq` change. It answers `Conflict(loader, lockSet) → (holder, key)` for D1 and D9.

`PartClaims` keeps its public API as a view over the mirror. `Held(loader)` and `OwnerOf(loader, key)` report the X
part keys (`b:`/`s:`) of every lock. `ClaimChanged(loader, keys, owner, fromSnapshot)` is raised with the X part keys
when a lock is granted or released, which is what row 17's `BoltReplay`, `PartGhosts` and `ActivityCapture` expect.
`HeldByOther` is answered from the mirror. Its Harmony patches move to `LockHooks`, and `PartClaims.Claim` (used by
the harness verb `part-claim`) goes away; `part-claim` is rewired to `lock-try` (D13).

`CarAwaySync` stays as it is. The mirror treats an away claim as an X lock on `car` for the conflict checks.

### D7. Car-level lock: lifts, moves, away

- **Lift.** The `CarLifter.Action` prefix gates with `Kind = Lift` on the lifted car's loader (no car on the lifter:
  no lock, as today). On grant the action runs, `LifterActionRequest` is sent as today, and the client releases the
  lock when `isMoving` falls (checked in `LifterSync`'s existing coroutine or a small poll) or after 30 s.
- **Move and swap.** `_ChangeCarPos_d__20.MoveNext` state 0 is blocked (`__result = false`), the lock is requested
  for the car and, if the target place is occupied, for that car too. On grant, `ChangeCarPos(carLoader, pos,
  movePlayerToCar)` is started again on `NotificationCenter` (spike 1.4 checks this replays the pie action
  faithfully). The release comes when the coroutine ends.
- **Server.** `OnLifterAction` and `OnCarPlaceChange` refuse when another player holds any lock on the car (or on
  the swap partner) and the sender holds no `car` X lock. This is the safety net for a client without the gate.
- **Away (row 13).** `CarAwayRegistry.OnRequest` asks `CarLocks.HeldByOther` instead of `CarClaims.Held` (same
  `InUse` refusal). An existing away claim counts as X `car` for lock requests. The away claim keeps its own
  lifecycle (watchdog, test-track grace), because it is long-lived and per kind.
- **Economy.** `EconomyRules`' "car sale refused while another player works on it" asks `CarLocks.HeldByOther`.

Finding 1 can no longer happen: the lift is refused while the friend's mount lock holds `car` shared, and the mount
is refused while the lift's X lock holds. `visual-lift` changes from "ghost follows the lift" to "lift refused; after
the release the lift moves and no ghost is left".

### D8. Interplay with the commit path, inventory transactions and machines

- **`FindConflict`** keeps both checks (preconditions, removed items) as the safety net. It adds a third: a record
  whose `Unmounted` flips on a key that another player holds in X or S is rejected with "locked by player N". Changes
  from unlocked paths (`FastUnmount`/`FastMount` harness verbs, tool overlays) are still accepted when no other
  player holds the key, so `car-race` keeps testing the old safety net.
- **Inventory.** `PartTransactions` and `InventoryDelta` are unchanged. Item locks prevent the "same item mounted
  twice" race for mounts, and the server does not grant an item that another lock holds. Other consumers of items
  (machines, sale, scrap) do not ask the item lock. A lock-held item that another player sells is caught by
  `RemovedByOther` as today (risk listed below).
- **Machines (row 5a)** and the **balancer claim** are separate. Taking a wheel off a car is a part lock, and putting
  it on the tire changer is the machine's CAS. Engine-stand parts are out of scope (open question 3).
- **Crane (row 5b).** `EngineCraneHooks`' `HeldByOther` check becomes the gate with `Kind = Crane` on the void
  entries `ActionUnMountGroup(InteractiveObject)` and `InsertEngineToCar(GroupItem)`.
- **Oil bin (row 5b).** The `CarLoader.UseOilbin()` prefix gates with `Kind = OilDrain`. The release comes on the
  final `MoveNext` of `<UseOilDrain>d__40`, which `OilBinHooks` already observes.
- **Digests (row 14b).** `ClientDigests` skips a car with any lock in the mirror, as it does with claims now.
- **Resync (F7, row 14c).** `ResyncController` releases this client's own locks and pending requests before the
  garage reload. The snapshot refills the mirror.
- **Guard (row 14a).** The guard's prefixes run first. A gated action that the guard refuses never sends a lock
  request (`__runOriginal` respected). No guard entry is added, because the guarded modes and pies stay as they are.
- **Visuals (row 17).** No change beyond D6. A remote change still animates. With locks, a lift can no longer move
  under a ghost.

### D9. Blocked at selection, and what the player sees

Every check below is local, read only, and uses `LockSets` + `CarLockMirror`. Nothing here changes game state.

| Where | Hook (spike 1.1/1.5 confirms each fires on the real path) | Effect while a conflicting lock is held by another player |
|---|---|---|
| Hover in disassembly/assembly modes | `GameScript.SetPartMouseOver(PartScript)` prefix (pass `null` for a locked part) and `PartScript.SetMouseOver(bool)` prefix (skip the highlight) | no highlight (or the orange highlight, open question 5) |
| Hover label | `GameScript.GetRaycastOnItemName()` postfix (or `UpdateRaycastOnItemName()` if the getter is inlined) | "<name> is working on this part" / "… on the <part>" |
| Body parts and lifts (interactive objects) | `InteractiveObject.SetMouseOver(bool, Color)` prefix | no highlight; label as above |
| Click | the gate (D1 step 1) | error sound and message, no request |
| Mount mode | `PartScript.ShowPreviewToMount()`/`ShowGroupPreview()`/`ShowPreview()` prefix returns `false` for a locked slot; a lock that arrives later calls `HidePreview()` on the slot, and a release re-shows it if mount mode is still on | the slot shows no preview and cannot be clicked |
| Item chooser | `ChoosePartUpWindow` after it fills `items` (spike 1.2 names the method) | items in another player's lock are greyed out or removed; selecting one anyway is refused at `SelectPartToMount` |
| Pie menu | `PieMenuController.GetIsAvailable(string)` postfix wraps the returned `Func<bool>` for `move_*`, `equipment_use` (crane), `drainTool` | option shown unavailable while the car under the cursor has another player's lock that the option would conflict with |

Messages (`LockMessages`, one place):

| Case | Text |
|---|---|
| same part | "<name> is working on this part." |
| connected part | "<name> is working on the <localized part name>." |
| fluid | "<name> is working on the <fluid> system." (coolant, brake fluid, oil, power steering fluid, washer fluid) |
| car position | "<name> is working on this car." (lift or move refused) / "<name> is moving this car." (work refused during a move) |
| away | row 13's existing message |
| item | "<name> is mounting this part." |
| waiting > 150 ms | "Waiting for the server…" in the hover label (toast if the label is not available) |
| no answer after 3 s | "The server did not answer. Try again." |
| car not ready | the existing "This car is still loading for multiplayer." |

The hover label and toasts are rate-limited (the same text at most once per 2 s), so hovering across a locked engine
does not spam.

### D10. Latency and lost answers

- **Budget.** One round trip plus server time. On the test lanes (local server) that is under 5 ms. Over Steam relay
  the playtest's pings were 30–100 ms. The server grant is a few dictionary lookups (sets are under 40 keys, D3).
- **Perceived delay.** Unmounting in disassembly mode is a click on a part followed by bolt work, so a 100 ms delay
  before the camera zooms is barely noticeable. Spike 1.6 measures whether the click is a hold with a fill ring
  (`Cursor3D` `fillTime`/`isHoldInProgress`). If it is, the request is sent at the start of the hold over a part and
  usually granted before the hold completes, and the gate then finds the lock already held (D1 step 2). A hold that
  is released or moved off the part releases the lock. This is an optimization behind the same gate, built only if
  1.6 shows a hold.
- **Lost answer.** After 3 s the pending action is dropped with the message. A grant that arrives later for a
  dropped `RequestId` is released at once (`CarLockRelease`). A denial that arrives later is ignored.
- **Disconnect.** All pending requests are dropped and the mirror is cleared with the session (the mod's disconnect
  path). The server releases all of the client's locks on `Left`.
- **Lost renews.** A client that stops renewing (stall, crash without TCP close) loses its locks after 90 s. Its
  later commit goes through `FindConflict` like any unlocked change.
- **Holder idle.** Open question 2: the holder's client cancels its own action after 5 minutes without bolt progress.

### D11. Late join, return to the garage, server restart

Locks are part of the `cars` snapshot (SyncOrder 100). For each car, after its records, `CarsSnapshotProvider` sends
one `CarLockUpdate` per active lock (replacing `CarPartClaimUpdate`), counted in the car's item like claims today. The
joiner's mirror is complete before `SyncEnd`. `PartClaims.ClaimChanged` fires with `fromSnapshot = true`, so no bolt
animation replays. A client returning to the garage gets the same path (row 6 "return = late join"). Lock updates
that arrive while the client is away are mirror-only (`ClientScene.GarageBound(mirrorOnly)`), as claims are now. On a
server restart, locks are empty, and every client reconnects through the late-join path.

### D12. Packets (appended to `PacketTypes` at merge time)

```
CarLockRequest  { int RequestId; int CarLoaderID; int SpawnSeq; CarLockKind Kind; List<string> X; List<string> S;
                  List<long> Items; int ExtendLockId = 0; int OtherLoaderID = -1; int OtherSpawnSeq; }   C→S
CarLockResult   { int RequestId; int LockId; bool Granted; CarLockRefusal Refusal; int HolderPlayerId;
                  string ConflictKey; }                                                                    S→requester
CarLockUpdate   { int LockId; int CarLoaderID; int SpawnSeq; int OwnerPlayerId /* -1 = released */; CarLockKind Kind;
                  List<string> X; List<string> S; List<long> Items; int OtherLoaderID; }                  S→all
CarLockRelease  { int LockId; }                                                                            C→S
CarLockRenew    { List<int> LockIds; }                                                                     C→S
enum CarLockKind    { PartUnmount, PartMount, GroupUnmount, GroupMount, BodyPart, Fluid, OilDrain, Crane, Lift, Move }
enum CarLockRefusal { None, Held, Away, NotReady, Stale, Invalid, Item, CarBusy }
```

Changed: `ServerInfo` gains `LockScope` (`[OptionalField]`, `connected` or `part`), so that the client's `LockSets`
and selection checks use the same rule as the server (risk "lock sets too wide"). With `part`, both sides keep only
the X keys, the items and `car`.

`CarPartClaim` and `CarPartClaimUpdate` keep their enum values (append-only) but are no longer sent. The server's
`OnClaim` handler is removed, and client and server ship together (protocol hash). Sizes: a request with 30 keys is
under 500 bytes, and lock traffic is a few packets per action, which is negligible against row 11's budget.

`CarLockUpdate` is a full record, not a delta, so every apply is idempotent and late join uses the same code path.

### D13. Harness verbs, dump and scenarios

Verbs (owner row 18; globally unique; registered in INTEGRATION.md):
- `lock-trace on|off|report` (spike, logging only): the order and arguments of the hover, selection, mount-mode,
  fluid, lift and move hooks of D1/D9, `GameScript.SelectedToMount`, game mode, `Cursor3D` hold state, and the
  relations dump of spike 1.3 (`lock-trace relations <loader>`).
- `lock-try <loader> <kind> <target> [hold <ms>|release]`: drives the **real** entry point through the gate. Kinds
  are `unmount <key>`, `mount <key> <itemUid>`, `body <index>`, `fill <type> <id>`, `drain <type> <id>`, `oil`,
  `crane-out`, `lift <lifter> up|down`, `move <place>`. It reports `{ result: granted|denied|timeout|context-lost,
  holder, conflictKey, waitedMs, ran }`. `hold` keeps the lock after the action starts, until `lock-release`.
  `nogate` (lift and move only) sends the row 2 request without a lock, to test the server's safety net.
- `lock-release <loader> [all]`, `lock-renew on|off` (stop renewing, to test expiry).
- `lock-hover <loader> <key|b:index>`: runs the hover path on that part (as `harness-mouse-over` does) and reports
  `{ highlighted, label }`.
- `lock-mount-mode <loader> <itemUid>`: enters mount mode with that item and reports the slots that show a preview.
- `lock-pie <loader> <option>`: reports the option's availability.
- Dump section `locks`: the mirror per loader (`lockId`, `owner`, `kind`, `x`, `s`, `items`), `pending`, and counters
  (`requested`, `granted`, `denied.<refusal>`, `timeouts`, `lateGrantsReleased`, `refusedLocally`,
  `blockedAtSelection.<where>`). The existing `cars[].claims` field stays (from the `PartClaims` view).
- Server: `Send-ServerCommand locks` (D5), `overlapViolations` must be 0 in every scenario.

Existing helpers reused: `net-delay <ms>`, `net-hold on|off|out`, `lift`, `car-move`, `cardetails-fluid`, `tool-use`,
`crane-out`, `part-fast-unmount`, `vfx-unscrew`.

Scenarios (area `locks`, new in `TestAreas.psm1`; each also lists `parts` and/or `placement`, `details`):
- `locks-race`: both clients `lock-try unmount` on the same part, started together (jobs, as `car-race`) and
  deterministic with `net-hold on` on both (stale mirrors). Exactly one is granted, the other denied with the holder.
  No `CarPartsChange` is rejected, and inventories, `cars` and `locks` are equal. The same holds for one item mounted
  into two slots by A and B.
- `locks-connected`: A holds a bearing cap (`hold`); B's unmount of its crankshaft is denied naming the crankshaft;
  B's unmount of a sibling cap is granted (open question 1). A holds the crankshaft; B's cap is denied. Crane out is
  denied while any engine part is held.
- `locks-fluid`: A holds the coolant reservoir unmount, B's `fill EngineCoolant 0` is denied; the reverse order is
  denied too; two coolant hoses at once are granted; after A's fill and release, B's `cardetails-fluid` read equals
  A's level (flush before release).
- `locks-car`: B holds a part, A's `lift` and `move` are denied and nothing moves on either client; A's lift is
  granted, B's unmount is denied until the lift stops; `away-try` is refused while B holds a part; the server
  refuses a `LifterActionRequest` sent without the gate (`lock-try lift … nogate`).
- `locks-select`: with A holding a part, B's `lock-hover` shows `highlighted false` and the label with A's name; B's
  `lock-mount-mode` hides A's slot; B's `lock-pie move_carLift1` is unavailable; after A's release all three return to
  normal.
- `locks-latency`: `net-delay 150` on A: A's unmount runs after ≈ 150 ms and `waitedMs` ≥ 150; `net-hold out` on A
  for 4 s: A's request times out, A sees the message, and the late grant is released (`lateGrantsReleased` 1, server
  shows no lock); A `lock-renew off` with a held lock: released after 90 s (`expired` 1); A disconnects while holding:
  B is granted at once.
- `locks-latejoin`: A holds a part lock and a fluid lock; B joins: B's mirror equals the server's `locks`, B's hover
  is blocked, and no ghost or bolt replay starts (`visuals.ghostsStarted` unchanged).
- `locks-scale` (`# run-all: lane 3`, two to four instances): for 3 minutes every client loops random `lock-try` over
  a shared car (same part, connected parts, a fluid, the lift), with half the clients at `net-delay 80`. Checks:
  `overlapViolations` 0, no rejected `CarPartsChange`, every client's `locks` equals the server's at the end, dumps
  equal, and the median `waitedMs` under 2× the configured delay.
- Updated: `visual-lift` (lift refused while B works; after release no ghost or hidden renderer), `visual-parts` and
  `visual-latejoin` (claims come from locks), `car-crane`, `tools-car-effects` (oil bin), `car-placement-race`.

### D14. Spike list: game methods to confirm in the IL2CPP dump and at runtime

Static (Il2CppDumper `dump.cs` + Ghidra decompile into `native/out/locks_clean`, as in `docs/spikes/native-decompile.md`),
then a `lock-trace` run where the static answer cannot decide:

1. Hover and selection: `Raycast.Garage`, `PartSelect`, `PartSelectMount`, `GarageAssemble` and what they call:
   `GameScript.SetPartMouseOver`, `PartScript.SetMouseOver(bool)`, `SetMouseOver()`, `MouseOverGroup()`,
   `InteractiveObject.SetMouseOver(bool, Color)`, `GameScript.UpdateRaycastOnItemName`/`GetRaycastOnItemName`, and
   which UI reads the label. Inlining risk: a getter may be inlined into the UI.
2. Mount flow: `ActionMount(bool showMenu)` → `ChoosePartUpWindow.Show(…)` → `GameScript.SelectPartToMount(BaseItem)`
   → `DoMount`; where `SelectedToMount` is set; `ShowPreviewToMount`/`ShowGroupPreview`/`ShowPreview`/`HidePreview`
   and `GameScript.PrepareItemsToMount`/`ShowAllUnMountedGroups`; group modes (`UnMountByGroup` is an `IEnumerator`:
   its void caller); `CleanUnfinishedMount`/`CleanUnfinishedUnMount` and every mode exit that cancels part work.
3. Relations: `PartScript.AddToBlockedBy`, `UnblockBlockParts`, `CheckIsFluidContainer`, `GetFluidId`,
   `CheckOnSendMessage(FluidRefillLockType, out string)`, `CheckMessageOnHide` (which fluids a hide drains),
   `CarLoader.CanTakeOffCarPart` (how `ConnectedParts` is used).
4. Re-invocation: each entry point of D1 called again 150 ms after its first call (`net-delay`), to check it reads
   no per-frame input that is gone by then (`FluidRefill.Use` reads the raycast target; `ChangeCarPos` restarted from
   code; `CarLifter.Action` from code; `ActionUnMountGroup(InteractiveObject)` with the stored `iO`).
5. Pie availability: `PieMenuController.GetIsAvailable(string)` lambdas for `move_*`, `equipment_use`, `drainTool`,
   and how the lift's interactive object reaches `CarLifter.Action`.
6. Input hold: `Cursor3D.GetIsButtonHold`, `SetHoldID`, `fillTime`/`holdTime` on the unmount click (decides D10's
   optimization).

Rule for all patches: never patch `FluidsData` members or any method that takes `NewCarData` by value (crashes the
game). Coroutine bodies are patched on their generated `MoveNext` (`_UseOilDrain_d__40`, `_ChangeCarPos_d__20`,
`FluidExtractor._UseAnim_d__5`). Methods the spike finds inlined get their caller or a coarser hook, recorded in
`docs/spikes/part-locks.md`.

## Risks / Trade-offs

- [Lock sets too wide: players feel blocked on unrelated parts] → spike 1.3 measures set sizes on three cars, and
  `locks-connected` pins the sibling case. A server setting `lock_scope = connected|part` (default `connected`, sent
  in `ServerInfo.LockScope`, D12) lets the user fall back to "same part only" without a new build if a playtest shows
  over-blocking.
- [Re-invoked entry point behaves differently 100 ms later (input gone, cursor moved)] → `Context` check (D1),
  spike 1.4 per entry point. The hold optimization (D10) shortens the gap where it applies.
- [IL2CPP inlining: hover or label hooks never fire] → spike 1.1 with `lock-trace`. The click gate is the
  authoritative local block. Selection effects are comfort and may fall back to the click refusal.
- [Lock leak: a client keeps a lock after its work ended (missed release signal)] → mode-exit release
  (`GameMode.SetCurrentMode`), 90 s renew expiry, idle cancel (open question 2), and `locks` in every bug report
  (row 14d bundles include the server's `locks` output).
- [Item locks do not cover machines, sale and scrap] → a mounted-while-sold item is still caught by `RemovedByOther`.
  If the next playtest shows it, the item lock moves into the inventory handlers (follow-up).
- [Lift and move now wait for a round trip] → measured in `locks-latency`. The lift motion takes seconds, so the
  added delay is small next to it.
- [Row 17 relies on the release-before-commit order] → kept by D5 (release broadcast before the relay). `visual-parts`
  runs in the verification set.
- [Four players: a popular part gets many denials] → no queue (non-goal), clear messages, and `locks-scale` shows the
  rate.
- [Changing claim semantics breaks scenarios that drive `PartClaims.Claim`] → `part-claim` is rewired to `lock-try`
  in the same commit, and the `parts`, `visuals`, `placement`, `testdrive` and `tools` areas run in verification.

## Migration Plan

New packets are appended to `PacketTypes`. Client and server ship together (protocol hash). There is nothing to
migrate in saves, because locks are runtime only. Rollback is reverting the change: `CarClaims` and `PartClaims`
come back from git, and the retired enum values are reused by the old code.

## Open questions / assumptions

The user's open questions are in proposal.md with their defaults. Assumptions taken here without the user:

1. The lock is requested at the click, not the hover. Hover only reads the mirror.
2. Denied requests are not queued.
3. Sibling parts are compatible (shared parent). This is open question 1, default yes.
4. Away claims stay in `CarAwayRegistry`, and `CarLocks` treats them as an exclusive car lock.
5. Engine-stand parts, machines and car tools keep their current paths (open questions 3 and 4).
6. Lock records are broadcast to every client in the session (not filtered by scene). Lock traffic is small, and a
   player returning to the garage gets the full set from the snapshot anyway.
