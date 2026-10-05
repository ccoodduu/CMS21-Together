# Design

## Context

See proposal.md for the motivation. State today:

- The inventory is server-authoritative but simple: `InventoryHook` sends every local `Inventory.Add(Item,bool)`,
  `Delete(Item)`, `AddGroup(GroupItem)` and `DeleteGroup(long)` as `InventoryItemAction`/`InventoryGroupItemAction`;
  the server appends or removes by UID and relays to the others. ADDs are not checked for duplicate UIDs on the server
  or the client. `Inventory.Add(List<BaseItem>)` is not hooked.
- The pattern for "the sender already ran vanilla code" exists: `CarSpawnRequest` → server validates →
  `CarSpawnRejected` makes the sender undo locally.
- The game has one instance of each machine, reachable through `ToolsManager.Get()` (`TireChangerLogic`,
  `WheelBalancerLogic`, `SpringClampLogic`, `EngineStandLogic`, `BrakeLatheLogic`, `BatteryChargerLogic`) and
  `ToolsMoveManager.Get()` (`WelderLogic`, `CarWashLogic`, `InteriorDetailingToolkitLogic`, and the movable tool
  transforms via `GetTool(IOSpecialType)`). The second engine stand is a separate `EngineStandLogic` on the GameObject
  `Engine_stand_2`.
- Car loaders are identified with `CarLoaderPlaces.Get().GetCarLoaderId(loader)`, not by parsing the GameObject name.
- Only stubs of the game are available, so the order in which vanilla code changes the inventory and calls
  `SetGroupOn…` is unknown. The design must not depend on it.
- Contracts from the other changes that this one plugs into (names as in their drafts):
  - `session-persistence-and-rejoin` D2–D4 (task groups 1–2, land before this change): `ISaveSection`,
    `ISnapshotProvider` (`int SendSnapshot(clientId)` returns the item count), `[SessionSection]`, `SyncOrder` slot
    **300 `workshop-tools`**, `SyncBegin`/`SyncEnd{Items}`/`SyncAck`, client `SyncTracker.Applied(key)` (rows count
    items instead of adding own "synced" flags), and `GameDataManager.StateLock` held around every server dispatch,
    `Client.Disconnect` and save build.
  - `sync-car-parts`: part keys (`PartKeys`, `CarSubPartIdentity.BuildKey`), the nested record
    `CarSubPartUpdatePacket`, `PartTransaction` + `InventoryDelta`, `CarPartsChange`, and the client API
    `CarPartsSync.MarkDirty(loaderId, part)` / `UploadBaseline(loaderId)`.
  - `sync-car-details` D6: `CarDetailsSync.MarkDirty(CarLoader, CarDetailSection, IEnumerable<int> = null)` and
    `CarDetailsSync.FlushNow(CarLoader)`; sections `Paint`, `BodyCosmetics` (fluids are polled by row 4).
  - `sync-players-and-scenes` D6: `ClientScene.IsGarageReady`; returning to the garage is a full resync.

What went wrong in 0.4.x and FixForTogether (read, not copied):

- Global `listen` flags: one flag per tool, reset by whichever hook ran next, so a remote apply could swallow the
  next local action or re-send a remote one.
- Every client ran the vanilla take-off, so each client added the item to its own inventory and sent an ADD →
  duplicates. FixForTogether blocks "the next ADD" for up to 60 s.
- Deltas instead of state: the engine stand sent angle increments, the balancer forced `IsBalanced = true` and skipped
  the minigame, and engine stand 2 was so broken that FixForTogether moves it 25 m under the floor.
- Engine stand take-off creates a **new GroupItem UID** on every client that runs it (FixForTogether
  `EngineStandSync`), so idempotent ADD alone does not prevent duplicates there.
- Upstream #87: the tire changer showed the hood the player had just removed (no identity/type check).
- For tools that act on a car, remote clients should apply only the final state and not run `DoWorkAnim`, which
  locks interactive objects, closes the car and disables lifter buttons on the remote client (FixForTogether `desync_*`).

## Goals / Non-Goals

**Goals:**
- One mechanism per category (slot machine, car-effect tool, item processing, tool position), so a new machine is a
  small client file plus an enum value.
- No item is lost or duplicated by races or remote applies. The server is the single referee.
- Late join, return to the garage and server restart rebuild the machines from server state alone.

**Non-Goals:**
- Car state itself (part mount state, condition, dirt, fluids, paint). It is owned by `sync-car-parts` and
  `sync-car-details`; this change only triggers their senders at the tools' commit points.
- Engine out/in with the crane as a part change: it is a group mount/unmount on a car, owned by `sync-car-parts`
  (dependency, D7). Engine swap state is stored with the car by `sync-car-parts` (D7).
- Headlamp alignment, wheel alignment and window tint results (row 4). Only the positions of the headlamp aligner and
  the tinting kit are synced here.
- Mirroring another player's minigame or camera (balance window, repair table bar, paint shop camera).
- Storing and replaying dyno results (`EngineData.measured`, `MeasuredDragIndex`): roadmap row 13
  `sync-test-drive-and-diagnostics`. This change only detects the end of a dyno run (D8, group 21).
- Costs of tool use (paint, wash, welder, repair) beyond "the remote side never pays again": `economy-audit`.
- Save versioning and rejoin identity (`session-persistence-and-rejoin`).

## Decisions

### D1. Four tool categories, one transport each

| Category | Machines | Server stores | Server relays only |
|---|---|---|---|
| Slot machine | tire changer, wheel balancer, spring clamp, engine stand 1/2, brake lathe, battery charger | `ToolSlotState` per machine (held item or group, flags, angle, part overlay for the stands) | — |
| Car-effect tool | welder, car wash, interior detailing (portable and stationary), oil bin, engine crane, paint shop (car), dyno | nothing new: results land in car state owned by rows 1/4/13 | `ToolAction` (visual effect) |
| Item processing | repair table, paint shop (part) | the updated item in `InventoryState` | — |
| Tool position | welder, interior detailing kit, oil bin, engine crane, headlamp aligner, tinting kit | `Positions[IOSpecialType]` (CarPlace or default) | — |

Alternative considered: one packet type per machine, as in 0.4.x (≈20 packets). Rejected because every machine would
need its own server handler, late-join code and race handling.

### D2. Slot state is a full snapshot with compare-and-set on the held UID

`ToolSlotState { ModToolId Tool; ModItem Item; ModGroupItem Group; bool Mounting; bool Active; float Angle;
Dictionary<string, CarSubPartUpdatePacket> Parts }` (exactly one of `Item`/`Group` set, or neither when empty;
`Mounting` mirrors `GroupOnTireChangerIsMounting` / `GroupOnSpringClampIsMounting`).

`ToolSlotUpdatePacket { ToolSlotState State; long ExpectedUid }` is sent by the acting client after its local vanilla
action (put, take, separate/connect, balance finished). The server accepts it only if:
1. the UID currently held equals `ExpectedUid` (0 = empty),
2. the new occupant has the right kind for the tool (Item for lathe/charger, Group for the others; an optional ID
   prefix table per tool, filled from `Database/item_database.json` where the IDs are known), and
3. the new occupant's UID is not held by another machine (two machines loaded with the same item at once).

On accept it stores the state and relays it to everyone else. On reject it sends
`ToolSlotRejectedPacket { ToolSlotState Current; string Reason }` to the sender only. Handlers run under
`GameDataManager.StateLock` (held by the dispatcher, row 7 D3); they take no lock of their own.

- A full snapshot makes every apply idempotent and gives late join the same code path ("apply snapshot").
- CAS on the UID, not a revision number: content changes (balanced, separated) keep the UID and never conflict;
  conflicting content changes are last-write-wins, which is harmless.

Alternative considered: ask the server before the vanilla action (lock first, act on grant). Rejected because the
vanilla UI flow (pie menu → window → coroutine) cannot be paused without reimplementing it, and the race window is
one round trip. The vanilla UI already refuses to load a machine that shows as occupied.

### D3. Inventory moves keep using the shared inventory flow; tool sync never moves items itself

The acting client's own `Inventory.Delete/DeleteGroup` (put) and `Add/AddGroup` (take) go to the server through the
existing inventory packets, unchanged. The slot packet only describes the machine. This works whatever order vanilla
uses, and for take-offs that return several items (tire changer: one group, or rim and tire as two items).

What makes that safe:

1. **Idempotent ADD by UID**, owned by this change (`sync-car-parts` does not add it): server
   `HandleInventoryItemAction`/`HandleInventoryGroupItemAction` ignore an ADD whose UID is already in `InventoryState`;
   client `InventoryHandlers` skip an ADD whose UID is already local. A take race on a machine that returns the same
   UID converges to one copy.
2. **Loser compensation on the client** (as with `CarSpawnRejected`). The server relays the winner's accepted
   update before it sends the loser's reject on the same stream, so the loser's mirror is current when the reject
   arrives.
   - Rejected **put** of item X: wait until vanilla has removed X locally, apply `Current` (inventory-neutral, item 3),
     then re-add X with hooks on **only if X is now neither in the local inventory nor on any machine in the mirror**
     (covers "both put the same item" and "same item on two machines").
   - Rejected **take**: apply `Current`, then delete (hooks on) every UID the client added during that take that is
     not one of the previous occupant's UIDs (group UID and item UIDs). Same-UID take-offs keep their single copy;
     new-UID take-offs (engine stand) drop the loser's extra copy.
   - TCP keeps one client's packets in order, so a compensating ADD/REMOVE always follows that client's own REMOVE/ADD.
3. **Remote applies are inventory-neutral.** The applying code registers the UIDs of the snapshot (the group, its
   items, and the previous occupant) in `ToolSync.NeutralUids` for the duration of the apply coroutine. While a UID
   is registered, the `InventoryHook` prefixes return `false` for it: no network send and no local inventory change.
   As a guard against new UIDs created by vanilla (engine stand), the apply also snapshots the local inventory UIDs
   before and deletes, with hooks off, additions afterwards that did not arrive from the network.
   This replaces 0.4.x's global `listen` flag and `IgnoreInventoryHooks` for long coroutines like
   `TireChangerLogic.Clear()`, so the local player's own inventory actions during that time still work.

Alternative considered: server-side atomic transfer (the slot packet carries the inventory delta, like
`sync-car-parts`' `CarPartsChange`). Cleaner, but the put-side removal happens in vanilla UI code before any tool hook
can open a transaction, which the stubs do not let us see. Spike 1.3 records the real order; if it shows a hook that
always precedes the inventory change, a `PartTransaction`-style buffer can replace item 2 later without changing
packets.

### D4. Suppression is per operation, not per tool

Each client tool file applies remote state inside `using (ToolSync.ApplyingRemote(tool))`. The hooks for that tool
check `ToolSync.IsApplyingRemote(tool)` and do not send. The scope ends when the apply ends, even if the vanilla call
threw. For coroutines (`Clear()`, `SetGroupOnEngineStand`, `SwapEngine`) the scope is released at the end of the
wrapping `MelonCoroutines` coroutine that runs the game's IEnumerator.

Hooks also do not send before initial sync is acknowledged (row 7: no state-changing packet before `SyncAck`;
today `ClientData.IsInitialSyncFinished`) or while `!ClientScene.IsGarageReady`. This stops the local save's own
`SetGroupOn…(…, instant: true)` calls during garage load from reaching the server.

### D5. Lightweight properties go separately

`ToolSlotPropertyPacket { ModToolId Tool; ToolProperty Property; float Value }` for `Angle` (engine stands) and
`Active` (battery charger). Last-write-wins, no CAS, stored in the slot.

The angle is sent as an absolute value (`EngineStandAngle`), throttled to 10 Hz with a final send when it stops
changing. Source: an `IncreaseEngineStandAngle(float)` postfix if spike 1.3 shows it fires from the input path
(small methods can be inlined by IL2CPP, so a hook may never run); otherwise a 10 Hz poll of `EngineStandAngle` that
compares against the last sent/applied value. Remote clients call `SetEngineStandAngle(angle)`. `Active` comes from
a `BatteryChargerActivate(bool)` postfix. The game has no getter for it, so if that hook is not hit the flag is
dropped: it is visual only, and the charge result still travels with the take-off ADD.

### D6. Slot machines: per-tool notes

- **Tire changer:** `SetGroupOnTireChanger(GroupItem, bool instant, bool connect)` postfix → snapshot with
  `GroupOnTireChanger` and `GroupOnTireChangerIsMounting`; `Clear()` (IEnumerator) prefix → empty snapshot. Remote:
  `SetGroupOnTireChanger(group, true, connect)`; spike 1.4 checks that calling it on an occupied changer does not
  duplicate the rim/tire models, otherwise the remote clears first (neutral).
- **Wheel balancer and its minigame (kept, user decision):** the actor plays the minigame locally; nothing of it is
  mirrored. `SetGroupOnWheelBalancer(GroupItem, bool)` postfix → snapshot; `FinishBalance()` postfix → snapshot with
  the balanced `WheelData` (falls back to `FinishBalanceInternal()` if spike 1.3 shows `FinishBalance` is not hit);
  `Clear()` prefix → empty. Remote: apply the group; for a balance on the same UID, copy `IsBalanced` onto the held
  items in place (`WheelData` is a struct: copy, set, assign back). If a remote clear arrives while the local player
  has `WheelBalanceWindow` open, the window is closed with `CancelAction()` first; that player's later
  `FinishBalance` is rejected by CAS. A minigame skip from another mod (QoLmod) still ends in `FinishBalance`.
- **Spring clamp:** `SetGroupOnSpringClamp(GroupItem, bool instant, bool mount)` postfix; `ClearSpringClamp()` prefix.
- **Brake lathe:** `SetItem(Item, bool)` postfix; `Clear()` (void) prefix.
- **Battery charger:** `SetItemOnBatteryCharger(Item, bool)` postfix; `ClearBatteryCharger()` prefix; `Active` via D5.
- Lathe and charger process over time on each client; the take-off ADD carries the taker's item, so the shared
  inventory always gets one result.

### D7. Engine stand: group plus part overlay through `sync-car-parts`' transaction

The engine group is stored when it is put on the stand. Identity of the stand: `__instance` in the
`EngineStandLogic` hooks (`gameObject.name == "Engine_stand_2"` → `EngineStand2`). `NotificationCenter.TakeOffEngineFromStand()`
has no stand argument; it only marks "local take-off in progress", and the stand comes from the following
`ClearEngineStand()` prefix. `CreateEngineWindow.CreateEngineAction` is expected to go through
`SetEngineOnEngineStand(Item)` and then `SetGroupOnEngineStand`; spike 1.3 verifies this.

Remote: `MelonCoroutines.Start(stand.SetGroupOnEngineStand(group, false))` (it is an IEnumerator) /
neutral `ClearEngineStand()` only, never `TakeOffEngineFromStand()`.

Parts mounted or unmounted on the engine on the stand are part changes with an inventory effect, so they reuse
`sync-car-parts`' client `PartTransaction` and `InventoryDelta` with keys rooted at `EngineStandLogic.engineGameObject`
instead of a car, sent as `ToolPartChangePacket { ModToolId Tool; long EngineUid; Preconditions; CarSubPartUpdatePacket[]
SubParts; InventoryDelta Delta }`. The server checks preconditions against `Slots[tool].Parts` and the delta against
`InventoryState` exactly like `CarPartsChange` (row 1 D4), stores the records and relays; the reject path also matches
row 1. This needs two things from `sync-car-parts` (dependency, task 1.2): its hooks ignore `PartScript`s that are not
in a car registry, and `PartTransaction` can be opened for a non-car root. Late join applies the group and then the
overlay; a clear wipes `Parts`.

### D8. Car-effect tools: result through rows 1/4, effect through `ToolAction`

`ToolActionPacket { ModToolId Tool; int CarLoaderId; ToolActionKind Kind; ModGroupItem Group }` is relayed to the
others and not stored. Remote clients play only particles and SFX (`GarageTool.particles`, `GarageTool.sfx`, or
`PaintshopManager.particleSystem`) at the car. They never call `DoWorkAnim`, `StartAnim`, `UseOilbin` or
`UseEngineCrane`, so they never lock controls, pay or gain XP a second time.

IEnumerator hooks (`DoWorkAnim`, `FinishAnim`, `MakeCarPaintEffects`) fire when the iterator is created, not when it
finishes. Under Unhollower the returned Il2Cpp iterator cannot simply be wrapped by a managed one, so the end of an
action is detected by a `MelonCoroutines` watcher started from the prefix: it waits for an observable end
(`PaintshopManager.IsPainting` false, `GarageTool.effectTime` elapsed plus 0.5 s, or the `FinishAnim` prefix) and
then calls the result sender.

| Tool | Actor hook | Result sender (owner) |
|---|---|---|
| Welder | `WelderLogic.DoWorkAnim(CarLoader)` prefix → action + watcher | `CarPartsSync.MarkDirty(loaderId, part)` for the welded body `CarPart`s (row 1) |
| Car wash | `CarWashLogic.DoWorkAnim(CarLoader)` prefix → action + watcher | `CarDetailsSync.MarkDirty(loader, CarDetailSection.BodyCosmetics)` + `FlushNow` (row 4 D6) |
| Interior detailing | `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)`, `ToolsMoveManager.UseInteriorDetailingToolkitStationary()` | split per row 4 D6, fields decided by row 4's probe (its task 1.2): `CarPart` dust → `CarDetailsSync.MarkDirty(loader, BodyCosmetics)`; part condition and `PartScript` dust → `CarPartsSync.MarkDirty(loaderId, part)` (row 1's attribute-only `CarPartsChange`) |
| Oil bin | `CarLoader.UseOilbin()` prefix → action | nothing to send: row 4's 1 Hz Fluids poll sees the drain; optional `CarDetailsSync.FlushNow(loader)` after it |
| Paint shop (car) | `PaintshopManager.MakeCarPaintEffects()` prefix → action + watcher | `CarDetailsSync.MarkDirty(PaintshopManager.carLoader, Paint \| BodyCosmetics)` + `FlushNow` (row 4 D6) |
| Engine crane | `CarLoader.UseEngineCrane()` (out), `NotificationCenter.InsertEngineToCar(GroupItem)` (in) → action | engine out/in is a group unmount/mount: a `CarPartsChange` transaction of row 1 (needs row 1 to hook `NotificationCenter.ActionUnMountGroup(InteractiveObject)` and `ActionInsertEngineToCar(GroupItem)`, task 1.2) |
| Dyno | `CarLoader.MeasurePower()` postfix, fallback `DynoManager.CloseDyno()` | row 13's dyno result sender (group 21 waits for row 13) |
| Engine swap | `InsertEngineToCar(group)` where `group.ID` differs from `GetEngineName()` and `CanSwapEngineTo` | stored as `EngineSwap` in row 1's per-loader car entry; a swap is a re-baseline (D9) |

### D9. Engine swap

`CarLoader.SwapEngine(GroupItem)` replaces the engine model, so the part keys under the engine change. Every client's
`sync-car-parts` registry was built from the original engine, so a swap must be handled like a new spawn of the same
car: the actor sends `ToolAction(EngineSwap, loader, group)`; the server stores `EngineSwap = group.ID` in
row 1's per-loader entry (`LoadedCars[loader]`) and bumps its `SpawnSeq`; remote clients run `SwapEngine` (as a
coroutine under `ApplyingRemote`), rebuild the registry and wait for the actor's `CarPartsSync.UploadBaseline`. Late
join applies `EngineSwap` right after `LoadCar` and before the registry is built. The field and the re-baseline belong
to row 1 (task 1.2); this change only detects the swap and sends the trigger.

### D10. Item processing: in-place item update

`ItemActionType.Update` (owned by this change) replaces an inventory item by UID on the server and the clients (the
client copies the fields onto the existing `Item` so the UI tile stays). An Update for a UID that is no longer in the
inventory (mounted or sold meanwhile) is ignored and logged. Sources: `RepairPartWindow.UpdateItemCondition(PartInfo,
bool)` postfix (repair table) and a watcher after `PaintshopManager.MakePartPaintEffects()` that waits for
`IsPainting` false and sends `PaintshopManager.item`. If the painted part leaves and re-enters the inventory
(FixForTogether saw REMOVE→ADD), the ADD already carries the paint and the Update is a no-op.

### D11. Tool positions

`ToolPositionPacket { int IoSpecialType; int CarPlace /* -1 = default */ }` from `ToolsMoveManager.MoveTo(IOSpecialType,
CarPlace, bool)` and `SetOnDefaultPosition(IOSpecialType)` prefixes (only when `CanMove(tool, place)` is true). The
server stores it (last-write-wins) and relays it. Remote clients call `MoveTo(tool, place, false)` or
`SetOnDefaultPosition(tool)`. If `CanMove` is false on the remote client (its car placement differs for a moment),
the position stays in the mirror and is retried on the next car placement change.

### D12. Client state, scenes and late join

The client keeps `ClientToolsState`, a mirror of the server's `ToolsState`, used for `ExpectedUid`, compensation and
re-applying.

- In the garage after sync: every tool packet updates the mirror and is applied.
- During initial sync (until `SyncTracker` sends `SyncAck` and sets `IsInitialSyncFinished`): packets update the
  mirror and are applied after the snapshot apply. They are not dropped, because the server sends live changes after
  `SyncEnd`.
- Away from the garage (`ClientData.LocalScene != Garage`): tool packets are dropped; returning to the garage is a
  full resync (row 6 D6), which replaces the mirror.

Late-join path: `AskForSync` → `SyncBegin` → … `cars` (100) … `car-placement` (200) → **`workshop-tools` (300):
one `ToolsStatePacket { Slots, Positions }`** → … `SyncEnd`. It arrives after the `inventory` section (20) on the
same stream, so no extra wait flag is needed. The client first clears every machine its own save loaded (inventory-neutral, D3.3), then applies all slots
(`instant = true`) and stand overlays, then positions, then reports `SyncTracker.Applied("workshop-tools")` once (the
provider's `SendSnapshot` returns 1). A return to the garage and a repeated `AskForSync` use the same path. Car-effect results arrive with car state (rows 1/4).

### D13. Server-side state and persistence

`ModGameState.ToolsState { Dictionary<ModToolId, ToolSlotState> Slots; Dictionary<int, int> Positions }` is the
stored object. `WorkshopToolsSection` (`[SessionSection]`, `ISaveSection` + `ISnapshotProvider`, key
`workshop-tools`, `Version = 1`, `SyncOrder = 300`) is a thin adapter (row 7 D1): `Save()` serializes it,
`Load()` replaces it, `Reset()` empties it, `Migrate` has no steps yet, and `SendSnapshot` sends one `ToolsStatePacket`
and returns 1. It is discovered by `SessionRegistry`; nothing is added to `OnAskForSync` or `SaveSession` by hand.
Adding a field needs no version bump; renaming or reshaping `ToolSlotState` does, with a `JToken` migration. The server does not simulate machines (no charging or lathe timers); it stores what the acting client
reports.

### D14. Failure cases

- Disconnect between the put REMOVE and the `ToolSlotUpdate` (same frame on one stream): the item is lost. Accepted.
- Disconnect or scene change mid-minigame or with an item on a machine: no pending server state; the item stays on the
  machine for everyone. There are no tool claims to release on `PresenceEvents.Left`.
- Server restart: slots and positions load from the save; clients rejoin through the late-join path.

## Risks / Trade-offs

- [Vanilla take paths touch the inventory in ways the stubs don't show (`Inventory.Add(List<BaseItem>)` is not
  hooked; new UIDs)] → Spike 1.3 records the order; D3.3's before/after snapshot covers unexpected additions on
  remote applies; every scenario step asserts equal inventories.
- [IL2CPP inlining: a hook on a small method (`IncreaseEngineStandAngle`, `BatteryChargerActivate`, `FinishBalance`,
  `UseOilbin`) may never run when called from native UI code] → spike 1.3 runs the real UI with `tool-trace`; each
  missing hook gets the poll or caller-side fallback named in D5/D6/D8. Harness commands call the logic methods
  directly and cannot detect inlining, so this spike is the only check.
- [`Clear()` coroutines on the tire changer and wheel balancer may play animations or need the player at the
  machine] → spike 1.4; fallback `ClearForTutorial()` or setting the fields directly, decided per tool.
- [A remote apply hits a machine whose window is open on that client] → close it first (`WheelBalanceWindow.CancelAction()`,
  pie menu). The local player loses that UI step.
- [Engine stand 2 may be absent on some clients] → keep it in the mirror and show nothing; if the harness shows it
  cannot work, disable its interactive object while connected (QUESTIONS.md default).
- [Paint shop: two players paint the same car] → last-write-wins on the car paint (row 4).
- [Rows 1/4 rename their APIs] → this change only calls the senders named in the Context; task 1.1 re-checks them.

## Migration Plan

New packet types are appended to `PacketTypes`, so existing values do not change. Client and server must be updated
together (the version check enforces this). Old saves load with an empty `ToolsState`. Rollback means reverting the
change; an unknown `workshop-tools` section is kept as raw JSON by row 7.

## Open questions / assumptions

Decisions taken without the user (recorded here instead of asking):

1. **Engine stand 2** is synced like stand 1 (QUESTIONS.md default). If the harness shows it is not workable,
   disable it while connected.
2. **`ItemActionType.Update` and idempotent ADD by UID** are owned by this change: `sync-car-parts` does not add them.
3. **Engine crane out/in, the stand part overlay and engine swap storage** depend on `sync-car-parts` (D7, D8, D9).
   If row 1 is merged without them, task 1.2 parks those tasks and records the gap in ROADMAP/QUESTIONS instead of
   building a parallel mechanism here.
4. **Interior detailing** results are split between row 4 (`CarPart` dust) and row 1 (part condition, `PartScript`
   dust, sent as an attribute-only `CarPartsChange`); row 4's probe (its task 1.2) decides the exact fields.
5. **Dyno results:** this change owns the trigger; the values are stored by ROADMAP row 13
   `sync-test-drive-and-diagnostics` (decision 2026-10-05).
   No dyno run is mirrored.
6. **Minigames are not skipped or mirrored.** The balance result is taken from `FinishBalance()`.
7. No FixForTogether code is adapted. Its findings (final-state-only for car-effect tools, ADD duplication, new UID on
   stand take-off, engine stand 2, `MeasurePower` as the dyno commit point) inform this design. If code is adapted
   later, credit TogetherFixer and link the repository, as its license requires.
