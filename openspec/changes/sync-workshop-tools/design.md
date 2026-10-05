# Design

## Context

See proposal.md for the motivation. State today:

- The inventory is server-authoritative but simple: `InventoryHook` sends every local `Inventory.Add/Delete/AddGroup/DeleteGroup`
  as `InventoryItemAction`/`InventoryGroupItemAction`; the server appends or removes by UID and relays to the others.
  ADDs are not checked for duplicate UIDs on the server or the client.
- The pattern for "the sender already ran vanilla code" exists: `CarSpawnRequest` → server validates → `CarSpawnRejected`
  makes the sender undo locally.
- The game has one instance of each machine, reachable through `ToolsManager.Get()` (`TireChangerLogic`,
  `WheelBalancerLogic`, `SpringClampLogic`, `EngineStandLogic`, `BrakeLatheLogic`, `BatteryChargerLogic`) and
  `ToolsMoveManager.Get()` (`WelderLogic`, `CarWashLogic`, `InteriorDetailingToolkitLogic`, and the movable tool
  transforms). The second engine stand is a separate `EngineStandLogic` on the GameObject `Engine_stand_2`.
- Car loaders are identified with `CarLoaderPlaces.Get().GetCarLoaderId(loader)` (as in `CarSpawnHooks`), not by
  parsing the GameObject name as 0.4.x did.
- Only stubs of the game are available, so the order in which vanilla code removes the item from the inventory and
  calls `SetGroupOn…` is unknown. The design must not depend on it.

What went wrong in 0.4.x and FixForTogether (read, not copied):

- Global `listen` flags: one flag per tool, reset by whichever hook ran next, so a remote apply could swallow the
  next local action or re-send a remote one.
- Every client ran the vanilla take-off, so each client added the item to its own inventory and sent an ADD →
  duplicates. FixForTogether worked around this by blocking "the next ADD packet" for 60 s.
- Deltas instead of state: the engine stand sent angle increments, the balancer forced `IsBalanced = true` and skipped
  the minigame, and the second engine stand was so broken that FixForTogether moves it 25 m under the floor.
- Upstream #87: the tire changer showed the hood the player had just removed. The tool accepted whatever item
  arrived, with no check on identity or type.
- Lesson kept from FixForTogether (`desync_*`): for tools that act on a car, remote clients should apply only the
  final state, not run `DoWorkAnim`, because `DoWorkAnim` locks interactive objects, closes the car and disables lifter
  buttons on the remote client.

## Goals / Non-Goals

**Goals:**
- One mechanism per category (slot machine, car-effect tool, item processing, tool position), so a new machine is a
  small client file plus an enum value.
- No item is lost or duplicated by races or remote applies. The server is the single referee.
- Late join and server restart rebuild the machines from server state alone.

**Non-Goals:**
- Car state itself (part condition, dirt, fluids, paint, engine parts on cars). It is owned by `sync-car-parts` and
  `sync-car-details`. This change only makes sure the tool's result is emitted through their packets.
- Headlamp alignment and window tint results, wheel alignment and dyno (rows 4 and later). Only the positions of the
  headlamp aligner and the tinting kit are synced here.
- Mirroring another player's minigame or camera (wheel balance window, repair table bar, paint shop camera).
- Save versioning and rejoin identity (`session-persistence-and-rejoin`).

## Decisions

### D1. Four tool categories, one transport each

| Category | Machines | Server stores | Server relays only |
|---|---|---|---|
| Slot machine | tire changer, wheel balancer, spring clamp, engine stand 1/2, brake lathe, battery charger | `ToolSlotState` per machine (held item or group, flags, angle, part overlay for the stands) | nothing |
| Car-effect tool | welder, car wash, interior detailing (portable and stationary), oil bin, engine crane, paint shop (car) | nothing new: results land in car state owned by rows 1/4 | `ToolAction` (visual effect) |
| Item processing | repair table, paint shop (part) | the updated item in `InventoryState` | — |
| Tool position | welder, interior detailing kit, oil bin, engine crane, headlamp aligner, tinting kit | `Positions[tool]` (CarPlace or default) | — |

Alternative considered: one packet type per machine, as in 0.4.x (≈20 packets). Rejected because every machine would
need its own server handler, late-join code and race handling.

### D2. Slot state is a full snapshot with compare-and-set on the held UID

`ToolSlotState { ModToolId Tool; ModItem Item; ModGroupItem Group; bool Mounting; bool Active; float Angle;
Dictionary<string, ModSubPartState> Parts }` (exactly one of `Item`/`Group` set, or neither when empty).

`ToolSlotUpdatePacket { ToolSlotState State; long ExpectedUid }` is sent by the acting client after its local vanilla
action (put, take, separate/connect, balance finished). The server accepts it only if the UID currently held equals
`ExpectedUid` (0 = empty). On accept it stores the state and relays it to everyone else. On reject it sends
`ToolSlotRejectedPacket { ToolSlotState Current; string Reason }` to the sender only.

- A full snapshot makes every apply idempotent and gives late join the same code path ("apply snapshot").
- CAS on the UID, not a revision number: content changes (balanced, mounted) keep the UID and never conflict, and
  conflicts on content are last-write-wins, which is harmless.
- The server also checks the type: a tire changer/balancer only takes a wheel-type group, the lathe only a brake disc
  item, and so on (a table of allowed IDs/prefixes from `Database/*.json`, or the group shape when no IDs are
  available). This guards against #87-style wrong items.

Alternative considered: ask the server before the vanilla action (lock first, act on grant). Rejected because the
vanilla UI flow (pie menu → window → coroutine) cannot be paused without reimplementing it, and the race window is
only one round trip. The vanilla UI already refuses to load a machine that shows as occupied.

### D3. Inventory moves keep using the shared inventory flow; tool sync never moves items itself

The acting client's own `Inventory.Delete/DeleteGroup` (put) and `Add/AddGroup` (take) go to the server through the
existing inventory packets, unchanged. The slot packet only describes the machine. This works whatever order vanilla
uses, and for take-offs that come back as several items (tire changer: one group or rim and tire as two items).

What is needed for that to be safe:

1. **Idempotent ADD by UID** on the server (`HandleInventoryItemAction`/`HandleInventoryGroupItemAction` ignore an
   ADD whose UID is already in `InventoryState`) and on the client (`InventoryHandlers` skip an ADD whose UID is
   already in the local inventory). This makes the "both took it" race converge to one copy. If `sync-car-parts`
   already does this as part of the shared inventory flow, reuse it.
2. **Loser compensation on the client**, as with `CarSpawnRejected`: on `ToolSlotRejected` for a *put*, the client
   waits until the item is gone from its local inventory (vanilla has finished), then re-adds it with hooks on (→ ADD
   to the server), then applies `Current` to its machine. For a rejected *take*, the item the client added is already
   in the shared inventory, so the client only applies `Current`. TCP keeps one client's packets in order, so the
   compensating ADD always arrives after that client's REMOVE.
3. **Remote applies are inventory-neutral.** The applying code registers the UIDs of the snapshot (the group, its
   items, and the previous occupant) in `ToolSync.NeutralUids` for the duration of the apply coroutine. While a UID
   is registered, `InventoryHook` prefixes return `false` for it: no network send and no local inventory change.
   This replaces 0.4.x's global `listen` flag and `IgnoreInventoryHooks` for long coroutines like `TireChangerLogic.Clear()`,
   so the local player's own inventory actions during that time still work.

Alternative considered: server-side atomic transfer (the slot packet moves the item between inventory and machine,
and the actor's inventory hooks are suppressed). Cleaner on paper, but the client would have to know before vanilla
removes the item that the removal is for a machine, which the stubs do not let us see.

### D4. Suppression is per operation, not per tool

Each client tool file applies remote state inside `using (ToolSync.ApplyingRemote(tool))`. The hooks for that tool
check `ToolSync.IsApplyingRemote(tool)` and do not send. Unlike `listen = false`, the scope ends when the apply
ends, even if the vanilla call threw an exception. For coroutines the scope is released at the end of the
wrapping coroutine.

### D5. Lightweight properties go separately

`ToolSlotPropertyPacket { ModToolId Tool; ToolProperty Property; float Value }` for `Angle` (engine stands) and
`Active` (battery charger). Last-write-wins, no CAS, stored in the slot. The angle is sent as an absolute value from
an `IncreaseEngineStandAngle` postfix (reading `EngineStandAngle`). It is throttled to 10 Hz with a final send when
input stops. Remote clients call `SetEngineStandAngle(angle)`.

### D6. Engine on a stand: group plus part overlay

The engine group is stored when it is put on the stand. Parts mounted or unmounted on the engine on the stand are
sent as `ToolPartUpdatePacket { ModToolId Tool; <sync-car-parts sub-part DTO> }`, addressed by the same index-path
identity `sync-car-parts` uses, but rooted at `EngineStandLogic.engineGameObject` instead of a car. The server keeps
them in `ToolSlotState.Parts` (key = index path, as `CarState.SubParts`). Late join applies the group with
`SetGroupOnEngineStand(group, withFade:false)` and then replays the overlay.

The stand is identified from `__instance` in every hook (`__instance.gameObject.name == "Engine_stand_2"` →
`EngineStand2`), never from mouse-over state. `CreateEngineWindow.CreateEngineAction` is expected to go through
`EngineStandLogic.SetEngineOnEngineStand(Item)` and then `SetGroupOnEngineStand` (0.4.x called the former), so it would
be covered by the same hook. Task 2.1 verifies this, and an extra hook on `SetEngineOnEngineStand` is added if not.
Take-off: `NotificationCenter.TakeOffEngineFromStand()` on the actor; remote clients call `ClearEngineStand()` only.

### D7. Car-effect tools: result through rows 1/4, effect through `ToolAction`

`ToolActionPacket { ModToolId Tool; int CarLoaderId; ToolActionKind Kind; ModGroupItem Group }` is relayed to the
others and not stored. Remote clients play only particles and SFX (`GarageTool.particles`, `GarageTool.sfx`, or
`PaintshopManager.particleSystem`) at the car. They never call `DoWorkAnim`, `StartAnim` or `UseOilbin`.

| Tool | Actor hook | Result path (owner) |
|---|---|---|
| Welder | `WelderLogic.DoWorkAnim(CarLoader)` prefix → action; `FinishAnim(CarLoader)` prefix → emit body/details CarPart state | `CarBodyPartUpdate` (`sync-car-parts`) |
| Car wash | `CarWashLogic.DoWorkAnim(CarLoader)` | dirt/wash packet (`sync-car-details`) |
| Interior detailing | `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)`, `ToolsMoveManager.UseInteriorDetailingToolkitStationary()` | interior condition/dust (`sync-car-details`) |
| Oil bin | `CarLoader.UseOilbin()` | fluid level (`sync-car-details`) |
| Engine crane | `CarLoader.UseEngineCrane()` (out), `NotificationCenter.InsertEngineToCar(GroupItem)` (in / swap) | engine PartScripts (`sync-car-parts`); engine group via inventory flow; swap → `ToolAction(EngineSwap, Group)`, remote calls `CarLoader.SwapEngine(group)` |
| Paint shop (car) | `PaintshopManager.MakeCarPaintEffects()` | paint packet (`sync-car-details`) |

If rows 1/4 already capture the result through their own hooks (for example `CarLoader.TweenExteriorDustWash`), the
tool file only sends the `ToolAction`. The tasks check this before adding any result code.

### D8. Item processing: in-place item update

`ItemActionType.Update` replaces an inventory item by UID on the server and the clients (the client copies the fields
onto the existing `Item` so the UI tile stays). Sources: `RepairPartWindow.UpdateItemCondition(PartInfo, bool)` postfix
(repair table) and the end of `PaintshopManager.MakePartPaintEffects()` (wait until `IsPainting` is false). If the
painted part leaves and re-enters the inventory (FixForTogether saw REMOVE→ADD), the ADD already carries the paint
and the Update is a harmless no-op.

### D9. Tool positions

`ToolPositionPacket { int IoSpecialType; int CarPlace /* -1 = default */ }` from `ToolsMoveManager.MoveTo` and
`SetOnDefaultPosition` prefixes. The server stores it (last-write-wins) and relays it. Remote clients call
`MoveTo(tool, place, playSound:false)` or `SetOnDefaultPosition(tool)`. If `CanMove` is false on the remote client
(its car placement differs for a moment), the position is kept in the client mirror and retried on the next car
placement change.

### D10. Client mirror and late join

The client keeps `ClientToolsState`, a mirror of the server's `ToolsState`, updated by every tool packet even when
the client is not in the garage. Applying = "make the machines match the mirror". This runs on each tool packet, on
garage load (scene changes are owned by row 6) and after initial sync.

Late-join path: `AskForSync` → `WorldState`, `GarageState`, inventory batches, **`ToolsState { Slots, Positions }`**,
`SyncEnd`. The client handles `ToolsState` only after `IsInventorySynced`. It first clears every slot machine its own
save loaded (inventory-neutral, D3.3), then applies the snapshot with `instant = true` and the overlay, then the
positions. The car-effect results arrive with car state (rows 1/4).

### D11. Server-side state and persistence

`ModGameState.ToolsState { Dictionary<ModToolId, ToolSlotState> Slots; Dictionary<int, int> Positions }` is saved
with the rest of `ModGameState` by `GameDataManager.SaveSession`. The server does not simulate machines (no charging
or lathe timers). It stores what the acting client reports.

## Risks / Trade-offs

- [Vanilla removal/take paths touch the inventory in ways the stubs don't show (`Inventory.Add(List<BaseItem>)` is not
  hooked; engine stand take-off may create a new GroupItem UID)] → Task 2 first records the real call order with a
  harness trace. The neutral guard also covers new UIDs by snapshotting inventory UIDs around a remote apply and
  deleting additions that did not come from the network. Every per-tool scenario asserts equal inventories.
- [`Clear()` coroutines on the tire changer and wheel balancer may play animations or need the player at the
  machine] → Remote take-off uses the coroutine under the neutral guard. If it misbehaves, fall back to the
  `ClearForTutorial()` variant or to setting the fields directly. This is decided per tool, with harness evidence.
- [Processing that runs over time on each client (battery charge tween, brake lathe) may not end in the same values,
  or `instant = true` on late join may skip it] → The take-off ADD carries the actor's item, so the shared inventory
  always gets one consistent result. The late-join dump compares only the occupant ID/UID and flags, not the
  condition, while a process is running.
- [A remote apply hits a machine whose window is open on that client (balancer minigame, pie menu)] → Before
  applying, close that machine's window (`WheelBalanceWindow.CancelAction()`, close the pie menu). Accepted: the
  local player loses that UI step.
- [Engine stand 2 may be absent (not bought or not in the scene) on some clients] → Store its slot like any other.
  A client without the object keeps it in the mirror and shows nothing. If the harness shows it cannot work,
  disable its interactive object while connected (open question 1).
- [Paint shop: two players paint the same car at once] → Last-write-wins on the car paint (row 4). No paint shop
  lock in this change.
- [Rows 1/4 change their packet shapes] → This change only calls their senders and the sub-part DTO/resolver. Their
  final names are bound in task 1.1.
- [Hooking IEnumerator methods (`SetGroupOnEngineStand`, `Clear`, `DoWorkAnim`, `MakeCarPaintEffects`) in IL2CPP:
  the prefix runs when the iterator is created, not when it finishes] → Detect the end of an action through a second
  hook (`FinishAnim`, `FinishBalance`, `IsPainting` turning false) or by wrapping the returned iterator.

## Migration Plan

New packet types are appended to `PacketTypes`, so existing values do not change. Client and server must be updated
together (the mod version check already enforces this). Old saves load with an empty `ToolsState`. Rollback means
reverting the change. Saves written with `ToolsState` still load in older builds if unknown JSON fields are ignored,
which row 7 confirms.

## Open questions / assumptions

Decisions taken without the user (recorded here instead of asking):

1. **Engine stand 2** is synced like stand 1 instead of being disabled. If harness runs show it is not workable,
   disable it while connected and put that question in `QUESTIONS.md`.
2. **`ItemActionType.Update`** and **idempotent ADD by UID** are added here unless `sync-car-parts` adds them first.
   Whichever change lands first owns them.
3. **Engine swap identity** (`CarLoader.EngineParams.EngineSwap`) is assumed to be car state owned by `sync-car-parts`.
   This change only triggers `SwapEngine` on remote clients. If row 1 does not store it, a late joiner sees the
   original engine model, and a task adds `EngineSwap` to the server's car record.
4. **Car wash, interior detailing, oil bin and paint results** are assumed to be fully covered by `sync-car-details`
   packets. This change adds only the visual `ToolAction`.
5. **Minigames are not skipped or mirrored.** The wheel balancer result is taken from `FinishBalance()`. 0.4.x and
   FixForTogether both had a minigame skip; it is not carried over.
6. No FixForTogether code is adapted. Only its findings (final-state-only for car-effect tools, the ADD duplication
   cause, engine stand 2 problems) inform this design. If code is adapted later, credit TogetherFixer and link the
   repository, as its license requires.
