# Design

## Context

Static spike `docs/spikes/singleplayer-features.md` sections 1–3 and `docs/spikes/car-details.md` sections 9–10;
runtime spike runs `20261008-230909_L2_sp-features-probe` and `…_sp-features-probe2` (lane 2); review
`review.md` (2026-10-08, decompiles `spfeat_clean`, `cardetails_clean`, `locks_clean`, `crane3_clean`).

- **Tune window.** Opened by clicking the dyno's tuning computer (`#dynoTune` in `GameScript.ClickIO`), through
  `WindowManager.Show(WindowID.Tune = 56, args)`, so the guard sees it. `TuneWindow.Show(object[])` accepts only a
  `CarLoader` in `args[0]`, sets mode 7 (`UI`); `Hide` restores the previous mode. `TuneWindow.PrepareTabs` sets
  `GearboxTab.carLoader` (`+0xA0`). The tabs are `EcuTuning`, `CarbTuning` (both tail-jump to `PartModule.Tune(short[],
  float)`, row 4's hook) and `GearboxTab`. `GearboxTab.ApplyAction` (`0x180D73180`) returns unless the gearbox
  `PartScript.IsTuned()` and it is mounted, then writes `gearboxHandle.finalDriveRatio` and a new `gearRatio` array;
  `ResetToDefaultAction` only resets sliders. A stock car's `GearboxHandle` exists with an empty `gearRatio` and
  `finalDriveRatio = 0` (spike run 1, Bolt Atlanta). Row 4's `CarDetailsIO.ReadTuning`/apply already handle the
  gearbox and the modules; the `t:gearbox` and `t:<partKey>` entries are merged per entry since row 19 part 1, and the
  flush sends only entries whose signature changed. The game keeps a part's tuning on its item (`Item.TuningData`,
  `Item.GearboxData`); `ModItem` has both fields, but `ItemConverter` does not fill or apply them.
- **Bonus parts.** `CarLoader.bonusParts` is a `List<BonusPart>`; a car has as many slots as its config defines (Bolt
  Atlanta: one, `#BonusDummy`, `UID "bonusPart0"`, unmounted). Remove (`ClickIO`, `BonusDisassemble`):
  `TakeOffBonusPart(io, false)` → `new Item(ID)` with the paint → real `Inventory.Add` →
  `BonusPartsManager.TryDeleteBonusPart` → `BonusPart.TakeOff` → `Change("_BonusDummy")`. Fit (`BonusAssemble`):
  `GameScript.BodyMount` and its inlined copy in `SelectPartToMount` do, for a bonus IO (`specialType == 0x14`),
  `PlaySFX("PartTakeOff")` → `TakeOffBonusPart(io, false)` (`Change(SelectedToMount.ID)` → `Paint(...)` →
  `TakeOn(instant)`) → on return `Inventory.FindItemIndex(SelectedToMount.UID)` and an inlined `List<Item>.RemoveAt` (no
  `Inventory.Delete`). So a refusal inside `TakeOffBonusPart` would still remove the item locally, and a
  `TakeOffBonusPart` postfix runs before the item leaves the inventory. `SwapBonusPart` has no callers.
- **Engine stand.** `CreateEngineAction` calls `EngineStandLogic.SetEngineOnEngineStand(currentEngine)` on
  `ToolsManager+0x30` (stand 1; no money in the decompiled path, confirmed by spike run 2), which wraps the item in
  `new GroupItem(id){ItemList=[engine]}` and starts `SetGroupOnEngineStand(g, true)`; row 5a's postfix and
  `_SetGroupOnEngineStand_d__8.MoveNext` postfix send the stand state when the build finishes. The coroutine calls
  `ClearEngineStand()` itself between states 1 and 2; row 5a's `BeforeClear` returns an engine to the inventory only
  when it is held in the inventory, and the next put sends `ExpectedUid` = the old engine, so without a refusal the
  server accepts the replacement and the old engine is lost for everyone. Only one engine stand exists in the garage
  (`EngineStand2` not present, spike run 1). In a harness game the build coroutine stalls (STATUS 2026-10-06, spike
  run 2: no engine on either stand after 25 s); `EngineStandSync.Put` (the remote apply) runs the same coroutine.
  `ToolSync.OnRejected` → `Compensate` adds a refused put's group to the inventory (hooks on) when it is neither on a
  machine nor in the inventory and the outcome is `Unchanged`, which is always the case for a UID the server never saw.
- **Locks (row 18).** Locks are requested before an action runs (`LockGate`, `CarLockMirror.Request(LockSet, …)`), keys
  `s:<path>` (mechanical), `b:<index>` (body), `f:<type>.<id>`, `car`, `engine`; `LockKeys.IsWellFormed` and
  `CarLocks.Exists` accept only these. `LockHooks.ItemAction` routes `BonusAssemble`/`BonusDisassemble` to
  `BodyMountAction` (they are in `BodyModes`), where `TryResolveBody("#…")` fails for a bonus slot, so the action is
  ungated today. Refusals carry `CarLockRefusal` and are shown by `LockMessages.ForKey`, which builds the text from the
  key; `CarLockResultPacket` has no kind. `CarLockMirror` renews every own lock; `LockLifecycle` has idle caps per kind
  (`BoltIdleSeconds`, `ChooserIdleSeconds`).

## Goals / Non-Goals

**Goals:** the three features behave as in single player for the acting player and arrive for everyone, late joiners
and after a server restart; no two players change the same tuning or bonus slot at once and no fit overwrites another;
no engine is destroyed or created for free by a build.

**Non-Goals:** engine swaps (row 1 refuses them while connected); car versions (the car salon, row 26); tuning parts on
the engine stand (the tune window is only reachable at the dyno, with a car).

## Decisions

### D1. Gearbox commit

Postfix `GearboxTab.ApplyAction`: if `__instance.carLoader` is a loaded garage car, `CarDetailsSync.MarkDirty(car,
Tuning)`. Receivers keep row 4's apply (write `gearRatio` only when the stored array is non-empty, always
`finalDriveRatio`). No new entry; `t:gearbox` already exists.

### D2. Tune lock

- `TuneWindow.Show` prefix (connected, garage): build a `LockSet` of kind `Tune` with the synthetic car key `tune`
  (`LockKeys.Tune`, like `engine`) plus the part keys of the gearbox part and of every part that owns a `PartModule`
  (`CarDetailsIO.Modules`), all exclusive. The `tune` key makes the lock exclusive even when a car has no tunable part.
  The window opens only after the grant (row 18's gate re-invokes `Show`; the `CarLoader` arg stays in the closure); a
  refusal shows "<name> is tuning this car." and returns `__result = false`. `TuneWindow.Hide` postfix releases.
- The server treats `Tune` like an unmount lock on the part keys (exclusive, shared ancestors), so another player
  cannot unmount the ECU while it is being tuned. As a car-level lock it also makes `RefuseBusy` and `CarAwayRegistry`
  (`InUse`) refuse park, delete, lift and a dyno run by others; this is intended and stated in the spec.
- Idle cap: `LockLifecycle` gets `TuneIdleSeconds = 300`. Each `ApplyAction`/`Tune` call resets it; when it expires
  the client closes the window, which releases the lock.
- Message: `CarLockResultPacket` gains `[OptionalField] CarLockKind HolderKind` (the conflicting lock's kind, filled by
  `CarLocks`); `LockMessages` uses it, so a refused tuner reads "<name> is tuning this car." and a refused bonus fit
  "<name> is fitting a bonus part here." instead of the key-based "is working on the ECU". Local mirror refusals use
  the kind in `CarLockMirror`.

### D3. Bonus slots in the car details

- `ModBonusSlot { int Slot; string Id; bool Unmounted; bool IsPainted; ModColor Color; ModPaintType PaintType;
  ModPaintData PaintData }`; `ModCarDetails.BonusSlots` (`List<ModBonusSlot>`) replaces the `BonusParts` field, which
  the server merged and capped (`DetailsMerge`, `CarDetailsStore`) but no client ever wrote.
  `CarDetailSection.BonusParts` (256) stays the section flag.
- Entries `x:<slot>` in `CarDetailEntries.Split`, merged per entry, signatures like the other entries; the server caps
  slots at 16 and ids at 64 characters.
- Read: one slot per `bonusParts[i]` (`IsDummy()` → `Unmounted = true`, `Id = null`). Apply in row 4's D10 order after
  BodyCosmetics: differing id → `Change(id, false)` then `TakeOn(true)`; unmounted → `TakeOff(true)` then
  `BonusPartsManager.TryDeleteBonusPart` (no inventory side effect; frees the asset as the game's remove does); then
  `Paint` when the paint differs. `CarDetailsIO.All` gains `BonusParts`, so spawn snapshots and late join carry them.
- `BonusPart.Paint(bool, CustomColor, PaintData, PaintType)` takes a non-blittable struct by value; task 1.3 checks the
  call on a receiver. Fallback: set `IsPainted`/`Color`/`PaintType`/`PaintData` and call the renderer update the load
  path uses.
- Slots are matched by index and checked by the slot's `UID` (`bonusPart<i>`); a mismatch skips the entry and counts
  `bonusSlotMismatch` in the dump, so a soak shows it.

### D4. Bonus commit, inventory and lock

- **Fit gate.** `LockHooks.BodyMountAction` gains a bonus branch: when `IOMouseOverIO.specialType == 0x14` (a bonus
  slot), find the slot index (the `bonusParts` entry whose `InteractiveObject` is the IO), build a `BonusPart` lock set on
  `x:<slot>` plus the item lock on the selected item's UID, and keep `Run = () => game.SelectPartToMount(item)`. The
  existing `SelectPartToMount` prefix then gates the fit before the game plays a sound, changes the part or removes the
  item.
- **Fit commit.** A `SelectPartToMount` postfix and a `BodyMount` postfix, for a bonus IO, run after the inlined
  `RemoveAt`: when the remembered item has left the inventory, send `InventoryItemAction Remove` for its UID and
  `CarDetailsSync.MarkDirty(car, BonusParts)`. Release the lock after the flush.
- **Remove gate.** A `TakeOffBonusPart` prefix gates only the remove path (`BonusDisassemble`, called from `ClickIO`,
  which has no side effect before the call; task 1.3 confirms): request `BonusPart` on `x:<slot>` and run the original
  after the grant (row 18's re-invoke). A `TakeOffBonusPart` postfix on the remove path marks the car dirty; the item
  returns through the real `Inventory.Add`, which is synced.
- **Expected slot state.** The `BonusPart` lock request carries `[OptionalField] Expect` = the `x:<slot>` signature the
  client has applied (id, or empty). `CarLocks` compares it with the stored entry in `CarDetailsStore`; on a mismatch
  it refuses with `Stale` (D16 answer "This slot just changed.") and sends the stored entry to that client, which
  applies it. So a fit by B on a slot that A just filled, before B has applied A's entry, is refused and B keeps its
  item.
- The paint shop's `SubmitColor` postfix (row 4) adds `BonusParts` to the sections it marks.
- `LockKeys.Bonus(slot)` (`x:<slot>`) and `LockKeys.Tune`; `IsWellFormed` accepts both; `CarLocks.Exists` accepts
  `x:<slot>` when the slot is below the car's stored slot count and `tune` always.

### D5. Created engines

- `CreateEngineAction` prefix: if stand 1 (`ToolsManager+0x30`, the stand `CreateEngineAction` always uses) holds a
  group (`GroupOnEngineStand != null`) or a remote put for it is in flight or applying, refuse with "Take the engine off
  the stand first." and skip.
- `SetEngineOnEngineStand` postfix (only when called from `CreateEngineAction`): remember the built `GroupItem`'s UID
  as `EngineStandSync.PendingCreated`. The stand's send after the build (row 5a) sets `ToolSlotUpdatePacket.Created`
  only when the group on the stand has that UID. The flag is cleared on that send, on `ClearEngineStand`, on a scene
  change and on `Reset`, so it never marks an ordinary put.
- **Refused created put.** `PendingUpdate` keeps `Created`. In `ToolSync.Compensate`, a refused created group is
  dropped instead of being returned to the inventory (log "built engine discarded, the stand was taken"). This is the
  one real use of `Created` on the client; the gap-9 item race rule never applies to a UID the server never saw.
- **Remote put during a local build.** While `PendingCreated` is set, `ToolSync` defers the remote apply for stand 1
  until the local build ends; B's own send is then refused, and `Compensate` applies the server state.
- Server: `ToolsStore.Check` accepts a created put on an empty stand (it already accepts unknown UIDs) and counts it
  (`createdEngines`); a created put onto an occupied stand is refused as today (expected UID).
- **Self-check** `ToolsCheck` (style of `CarLocksCheck`/`MergeCheck`, run at server start in debug and by the
  harness): a created put on an empty slot is accepted and counted; a created put with an old `ExpectedUid` is refused;
  two puts with `ExpectedUid = 0` give one stand and one `ToolSlotRejected`.
- Money: none (spike run 2).

### D6. Tuning on items

`ItemConverter` fills `ModItem.TuningData` and `ModItem.GearboxData` from `Item.TuningData`/`Item.GearboxData` and
applies them back (additive; older clients ignore them). Task 1.2 confirms the game fills them on unmount; if it does
not, D6 is dropped and the proposal lists "tuning follows the part through the inventory" as a non-goal.

### D7. Server state

Nothing new is stored beyond the existing car details (the `cars` section v3 `Details`, now with `BonusSlots`), the
existing tool slots and the item fields above. Locks are runtime only, as in row 18. Late join: row 4's `car-details`
snapshot and row 5a's `workshop-tools` snapshot.

## Risks / Trade-offs

- [The `TuneWindow.Show` gate re-invoke loses the window args] → keep the args in the gate's closure; task 1.2 checks the
  tab opens on the right car.
- [The remove path through `ClickIO` has a side effect before `TakeOffBonusPart`] → task 1.3 logs it; if so, gate the
  remove at `ClickIO` for bonus IOs as the fit is gated at `SelectPartToMount`.
- [Bonus slot order differs between clients (DLC or mod configs)] → D3's `UID` check and counter.
- [The build coroutine cannot be driven in the harness] → task 1.1b is timeboxed; the server rules are covered by
  `ToolsCheck`, the client refusal by `tool-stand-create`, and the build steps by `docs/playtest.md`.
- [A `Tune` lock blocks others' dyno run, park, delete and lift] → intended; the idle cap bounds it.

## Migration Plan

`cars` section version bump with a no-op migration (`BonusParts` was stored but never written by a client). Packet
changes are additive. Client and server update together (version check). Rollback: revert; stored `BonusSlots` are
ignored by older servers.
