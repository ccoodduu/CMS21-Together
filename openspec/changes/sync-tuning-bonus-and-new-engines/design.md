# Design

## Context

Static spike `docs/spikes/singleplayer-features.md` sections 1–3 and `docs/spikes/car-details.md` sections 9–10;
runtime spike runs `20261008-230909_L2_sp-features-probe` and `…_sp-features-probe2` (lane 2).

- **Tune window.** Opened by clicking the dyno's tuning computer (`#dynoTune` in `GameScript.ClickIO`), through
  `WindowManager.Show(WindowID.Tune = 56, args)`, so the guard sees it. `TuneWindow.ParseArgs` takes the car; the tabs
  are `EcuTuning`, `CarbTuning` (both tail-jump to `PartModule.Tune(short[], float)`, row 4's hook) and `GearboxTab`.
  `GearboxTab.ApplyAction` (`0x180D73180`) returns unless the gearbox `PartScript.IsTuned()` and it is mounted, then
  writes `gearboxHandle.finalDriveRatio` and a new `gearRatio` array; `ResetToDefaultAction` only resets sliders. A
  stock car's `GearboxHandle` exists with an empty `gearRatio` and `finalDriveRatio = 0` (spike run 1, Bolt Atlanta).
  Row 4's `CarDetailsIO.ReadTuning`/apply already handle the gearbox and the modules; the `t:gearbox` and
  `t:<partKey>` entries are merged per entry since row 19 part 1.
- **Bonus parts.** `CarLoader.bonusParts` is a `List<BonusPart>`; a car has as many slots as its config defines (Bolt
  Atlanta: one, `#BonusDummy`, `UID "bonusPart0"`, unmounted). Fit and remove both run through
  `CarLoader.TakeOffBonusPart(io, false)` from `GameScript.BodyMount`/`ClickIO`: remove = `new Item(ID)` with the paint →
  real `Inventory.Add` → `BonusPartsManager.TryDeleteBonusPart` → `BonusPart.TakeOff` → `Change("_BonusDummy")`;
  fit = `GameScript.SelectedToMount` → `Change(item.ID)` → `Paint(...)` → `TakeOn(instant)`, then `BodyMount` removes
  the item with an inlined `List<Item>.RemoveAt` (no `Inventory.Delete`). `SwapBonusPart` has no callers.
- **Engine stand.** `CreateEngineAction` calls `EngineStandLogic.SetEngineOnEngineStand(currentEngine)` (no money in
  the decompiled path), which wraps the item in `new GroupItem(id)` and starts `SetGroupOnEngineStand(g, true)`; row
  5a's postfix and `_SetGroupOnEngineStand_d__8.MoveNext` postfix send the stand state when the build finishes. State 2
  of the coroutine calls `ClearEngineStand()` itself; row 5a's `BeforeClear` returns an engine to the inventory only
  when it is held in the inventory, so an engine on the stand would be lost. Only one engine stand exists in the
  garage (`EngineStand2` not present, spike run 1).
- **Locks (row 18).** Locks are requested before an action runs (`LockGate`, `CarLockMirror.Request(LockSet, …)`), keys
  `s:<path>` (mechanical), `b:<index>` (body), `f:<type>.<id>`, `car`, `engine`; refusals carry `CarLockRefusal` and are
  shown by `LockMessages`.

## Goals / Non-Goals

**Goals:** the three features behave as in single player for the acting player and arrive for everyone, late joiners
and after a server restart; no two players change the same tuning or bonus slot at once; no engine is destroyed by a
build.

**Non-Goals:** engine swaps (row 1 refuses them while connected); car versions (row 26); tuning parts on the engine
stand (the tune window is only reachable at the dyno, with a car).

## Decisions

### D1. Gearbox commit

Postfix `GearboxTab.ApplyAction`: if `__instance.carLoader` is a loaded garage car, `CarDetailsSync.MarkDirty(car,
Tuning)`. Receivers keep row 4's apply (write `gearRatio` only when the stored array is non-empty, always
`finalDriveRatio`). No new entry; `t:gearbox` already exists.

### D2. Tune lock

`TuneWindow.Show` prefix (connected, garage): build a `LockSet` of kind `Tune` with the part keys of the gearbox part and
of every part that owns a `PartModule` (`CarDetailsIO.Modules`), exclusive. The window opens only after the grant (row
18's gate re-invokes `Show` with the same args); a refusal shows "<name> is tuning this car." and returns
`__result = false`. `TuneWindow.Hide` postfix releases. The server treats `Tune` like an unmount lock on those keys
(exclusive, shared ancestors), so another player cannot unmount the ECU while it is being tuned.

### D3. Bonus slots in the car details

- `ModBonusSlot { int Slot; string Id; bool Unmounted; bool IsPainted; ModColor Color; ModPaintType PaintType;
  ModPaintData PaintData }`; `ModCarDetails.BonusSlots` (`List<ModBonusSlot>`) replaces the unused `BonusParts`.
  `CarDetailSection.BonusParts` (256) stays the section flag.
- Entries `x:<slot>` in `CarDetailEntries.Split`, merged per entry, signatures like the other entries; the server caps
  slots at 16 and ids at 64 characters.
- Read: one slot per `bonusParts[i]` (`IsDummy()` → `Unmounted = true`, `Id = null`). Apply in row 4's D10 order after
  BodyCosmetics: differing id → `Change(id, false)` then `TakeOn(true)`; unmounted → `TakeOff(true)`; then `Paint` when
  the paint differs. `CarDetailsIO.All` gains `BonusParts`, so spawn snapshots and late join carry them.

### D4. Bonus commit, inventory and lock

- `TakeOffBonusPart` prefix: find the slot (`bonusParts` index whose `InteractiveObject` is `io`); if another player's
  lock on `x:<slot>` is in the mirror, refuse with the message and skip the original; otherwise gate it: request
  `BonusPart` on `x:<slot>` (plus the item lock for `GameScript.SelectedToMount` when fitting) and run the original
  after the grant (row 18's re-invoke pattern). Remember the fitted item's UID.
- Postfix: `CarDetailsSync.MarkDirty(car, BonusParts)`; when fitting and the remembered item has left the inventory,
  send `InventoryItemAction Remove` for its UID (the inlined `RemoveAt` sends nothing). Removing needs nothing extra
  (the real `Inventory.Add` is synced). Release the lock after the flush.
- The paint shop's `SubmitColor` postfix (row 4) adds `BonusParts` to the sections it marks.

### D5. Created engines

- `CreateEngineAction` prefix: if the stand holds a group (`GroupOnEngineStand != null`) or a remote put is in flight,
  refuse with "Take the engine off the stand first." and skip; otherwise set `EngineStandSync.PendingCreated = true`.
- The stand's send after the build (row 5a) sets `ToolSlotUpdatePacket.Created` when the flag is set. The server's
  `ToolsStore.Check` accepts a created put on an empty stand (it already accepts unknown UIDs) and counts it
  (`createdEngines`); a created put onto an occupied stand is refused as today (expected UID).
- Money: none (open question 4).

### D6. Server state

Nothing new is stored beyond the existing car details (the `cars` section v3 `Details`, now with `BonusSlots`) and the
existing tool slots. Locks are runtime only, as in row 18. Late join: row 4's `car-details` snapshot and row 5a's
`workshop-tools` snapshot.

## Risks / Trade-offs

- [The `TuneWindow.Show` gate re-invoke loses the window args] → keep the args in the gate's closure; task 1.2 checks the
  tab opens on the right car.
- [`TakeOffBonusPart` runs inside `BodyMount`, which plays sounds and changes the selected item before our prefix can
  refuse] → if so, gate at `GameScript.BodyMount`/`ClickIO` for bonus interactive objects instead (task 1.3).
- [Bonus slot order differs between clients (DLC or mod configs)] → slots are matched by index and checked by the
  slot's `UID` (`bonusPart<i>`); a mismatch skips the entry with a debug log (row 4's unknown-key rule).
- [The engine stand build still throws a native exception in the harness (STATUS 2026-10-06)] → spike run 2 shows
  whether it builds; if not, `engine-build` stays a hand check and the scenario checks the server and B's stand state
  only.

## Migration Plan

`cars` section version bump with a no-op migration (`BonusParts` was never written). Packet changes are additive.
Client and server update together (version check). Rollback: revert; stored `BonusSlots` are ignored by older servers.
