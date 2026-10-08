# Proposal

## Why

The user wants three car-side features of single player in multiplayer (2026-10-08, ROADMAP row 25). The guard blocks
them today (`GuardRules`: `Window Tune`, `Mode BonusAssemble`/`BonusDisassemble`, pies `mode_bonus_assemble`/
`mode_bonus_disassemble`, `Window CreateEngine`, pie `engine_new`, all "Planned"):

1. **Tuning** (`TuneWindow`, tabs ECU, carburettor and gearbox). The player opens it at the dyno's tuning computer
   (`#dynoTune`, `GameScript.ClickIO`), for a car on the dyno with tuned (racing) parts; customer cars from an order are
   refused by the game (`GUI_NoTuneCarFromOrder`). Row 4 already syncs ECU and carburettor values per part (the
   `PartModule.Tune` postfix and the `t:<partKey>` detail entries) and reads and applies the gearbox ratios (`t:gearbox`),
   but nothing marks the car dirty when the gearbox tab is applied (`GearboxTab.ApplyAction` has no hook), nothing
   stops two players from tuning the same car at once, and a tuned part that is taken off loses its tuning in the
   shared inventory (`ItemConverter` carries neither `Item.TuningData` nor `Item.GearboxData`).
2. **Bonus (visual tuning) parts**: spoilers, bumpers, skirts and the like that fit into a car's bonus slots
   (`CarLoader.bonusParts`, `BonusPart { ID, UID, IsUnmounted, IsPainted, Color, PaintType, PaintData }`). Row 4's
   design planned a `BonusParts` section; the DTO exists (`ModBonusParts`, one ID list with one paint). The server
   merges and caps it (`DetailsMerge`, `CarDetailsStore`), but no client reads, applies, hooks or writes it, and it cannot
   carry a paint per slot.
3. **Building a new engine** on the engine stand (pie `engine_new` → `CreateEngineWindow` → `CreateEngineAction` →
   `EngineStandLogic.SetEngineOnEngineStand(new Item(engineId))` on stand 1, an engine block with nothing mounted). Row
   5a syncs everything on the stand once a group is on it (`SetGroupOnEngineStand` hooks, parts on the stand), and the
   server accepts a put of an item it never saw (`ToolsStore.Check`), so most of the path exists. It was never run, the
   harness verb for it never worked (`tool-stand-create` throws `BadImageFormatException` on
   `GameInventory.GetEnginesToCreate(out List<string>)`, spike run `20261008-230909_L2`), the build replaces an engine
   that is already on the stand without asking, and a refused build would hand the new engine to the shared inventory
   (`ToolSync.Compensate`).

"Car version" (`Window CarVersion`) is not a garage-car feature: the window is only used by the car salon to pick a
model's version before buying. It belongs to the car salon (row 26).

## What Changes

- **Tuning**
  - Hook `GearboxTab.ApplyAction` (postfix, car from `GearboxTab.carLoader`) → `CarDetailsSync.MarkDirty(car,
    Tuning)`; ECU and carburettor keep row 4's `PartModule.Tune` postfix.
  - **One tuner per car.** Opening `TuneWindow` takes a row 18 lock of the new kind `Tune` on a synthetic car key `tune`
    plus the car's tunable parts (the gearbox part and every part with a `PartModule`); another player is refused with
    "<name> is tuning this car." (D16 answer, message built from the holder's lock kind). The lock ends when the window
    closes, or after 5 minutes without an apply (the window closes). While it is held, other players cannot take off
    the tuned parts, run the dyno, park, delete or lift the car; the rest of the car stays free.
  - **Tuning follows the part.** `ItemConverter` carries `Item.TuningData` and `Item.GearboxData` (the fields exist on
    `ModItem`), so a tuned ECU or racing gearbox keeps its tuning through the shared inventory (task 1.2 confirms the
    game fills them on unmount; if not, this is a non-goal and `docs/try-it.md` says so).
  - The proof covers the gearbox tab on its own first (the old code fails there), then ECU, late join and a server
    restart.
- **Bonus parts**
  - New per-slot entries `x:<slot>` in the car details (`ModBonusSlot { Slot, Id, Unmounted, IsPainted, Color,
    PaintType, PaintData }`), merged per entry like the other details (row 19 part 1). The unused `ModBonusParts`
    field is replaced.
  - Read from `CarLoader.bonusParts`; applied with `BonusPart.Change(id, false)`, `Paint(…)`, `TakeOn(true)` /
    `TakeOff(true)` plus `BonusPartsManager.TryDeleteBonusPart` on a remove, never with `CarLoader.TakeOffBonusPart`
    (it adds to the inventory and reads the selected item).
  - **Fit** (`BonusAssemble`): gated in the existing `SelectPartToMount` prefix (`LockHooks.ItemAction` →
    `BodyMountAction` gains a bonus branch, key `x:<slot>` plus the item lock), because the game's `BodyMount`/
    `SelectPartToMount` removes the selected item from the inventory right after `TakeOffBonusPart` returns, so a gate
    inside `TakeOffBonusPart` would lose the item locally. A `SelectPartToMount`/`BodyMount` postfix sends the item's
    removal (the inlined `List.RemoveAt` sends nothing) and marks the car dirty.
  - **Remove** (`BonusDisassemble`, `ClickIO`): gated in a `TakeOffBonusPart` prefix; the item returns through a real
    `Inventory.Add` (synced today).
  - **No overwrite.** The `BonusPart` lock request carries the slot state the client sees; the server refuses it as
    `Stale` when its stored `x:<slot>` differs and sends the stored entry back, so a player whose copy is behind cannot
    fit over a part that was just fitted.
  - Paint: the paint shop's `SubmitColor` (bonus parts are painted with the car).
  - Locks: new lock kind `BonusPart` on key `x:<slot>` (exclusive); `LockKeys.IsWellFormed` and `CarLocks.Exists` learn
    `x:` (and `tune`).
  - Guard: `Mode BonusAssemble`/`BonusDisassemble` and their pies become allowed.
- **New engine on the stand**
  - The put of a created engine is marked (`ToolSlotUpdate.Created = true`, `[OptionalField]`). The mark is tied to the
    built group (remembered in a `SetEngineOnEngineStand` postfix), never to "the next put", and is cleared on
    `ClearEngineStand`, a scene change and a reset. The server logs and counts created puts.
  - Building on an occupied stand is refused before the game clears it ("Take the engine off the stand first."),
    because the game's build would silently destroy the engine on the stand (`ClearEngineStand` between states 1 and 2
    of `<SetGroupOnEngineStand>d__8`). The check is on stand 1, the only stand `CreateEngineAction` uses.
  - Two players building at the same moment: the second put is refused by the stand's expected-UID check (row 5a) and
    answered (`ToolSlotRejected`, D16). The refused built engine is discarded, not returned to the inventory, and a
    remote put that arrives while a local build runs waits until that build ends.
  - A server self-check covers the created-put rules, because the build coroutine stalls in a harness game (STATUS
    2026-10-06); the build itself is a hand check unless spike 1.1b finds a way to drive it.
  - The harness verb `tool-stand-create` runs `CreateEngineWindow.CreateEngineAction` on a prepared window, so the
    refusal prefix is exercised.
  - Guard: `Window CreateEngine` and pie `engine_new` become allowed.

Hooks: `CMS.UI.Logic.Tune.GearboxTab.ApplyAction` (postfix), `CMS.UI.Windows.TuneWindow.Show`/`Hide` (lock and
release), `GameScript.SelectPartToMount` (existing prefix, bonus branch; new postfix) and `GameScript.BodyMount`
(postfix, bonus IO), `CarLoader.TakeOffBonusPart` (prefix: gate of the remove path only; postfix: mark dirty),
`CMS.UI.Windows.CreateEngineWindow.CreateEngineAction` (prefix: occupied-stand refusal), `EngineStandLogic.
SetEngineOnEngineStand` (postfix: remember the built group). Packets: changed only, all additive (`CarLockKind` gains
`Tune`, `BonusPart`; `CarLockRequestPacket.Expect` and `CarLockResultPacket.HolderKind`, both `[OptionalField]`;
`ToolSlotUpdatePacket.Created`; `ModCarDetails.BonusSlots`).

## Capabilities

### New Capabilities
- `car-tuning-sync`: gearbox, ECU and carburettor tuning reach every player, one tuner per car, and a tuned part keeps
  its tuning through the inventory.
- `bonus-parts-sync`: bonus parts fitted, removed and painted on a car look the same for everyone, with the item moving
  once through the shared inventory and no fit overwriting another.
- `engine-build-sync`: an engine built on the engine stand appears for everyone and never destroys an engine already on
  the stand nor creates a free one.

### Modified Capabilities
- None in `openspec/specs/` (rows 4, 5a and 18 are not archived; this change finishes row 4's bonus-parts and
  gearbox requirements and adds two lock kinds to row 18).

## Impact

- Core: `ModCarDetails` (`BonusSlots` replaces `BonusParts`), `CarDetailEntries` (`x:<slot>`), `DetailsMerge`,
  `CarDetailsStore` caps (16 slots), `LockPackets` (`CarLockKind.Tune`, `BonusPart`; `LockKeys.Bonus(slot)` with prefix
  `x:`, `LockKeys.Tune`; `IsWellFormed`; `Expect`, `HolderKind`), `ToolPackets` (`Created`).
- Server: `CarLocks` (the two kinds; `Exists` for `x:` and `tune`; the `Expect` check against `CarDetailsStore`),
  `CarDetailsStore` (per-slot merge), `ToolsStore` (created puts counted and logged), a `ToolsCheck` self-check (style of
  `CarLocksCheck`), cars section version bump (`BonusSlots`).
- Client: `Logic/Car/Details/CarDetailsHooks.cs` (gearbox, bonus), `CarDetailsIO` (bonus read/apply),
  `Logic/Car/Locks/LockHooks.cs` (bonus branch), `LockTuneHooks.cs` and `LockBonusHooks.cs` (new), `LockLifecycle`
  (`Tune` idle cap), `LockMessages` (holder kind), `Data/ItemConverter.cs` (tuning fields), `Logic/Tools/
  EngineStandSync.cs` and `ToolSync.cs` (created group, discard on refusal, deferred remote apply),
  `Guard/GuardRules.cs`.
- Harness: `cardetails-gearbox <loader> <final> <r1,r2,…>`, `cardetails-ui gearbox <loader>` (runs `ApplyAction` on a
  prepared `GearboxTab`), `tune-open <loader>`/`tune-close` (the window with its locks), `bonus-probe`, `bonus-fit
  <loader> <slot> <itemUid>` (through `SelectPartToMount`) and `bonus-remove <loader> <slot>` (through `ClickIO`),
  `tool-stand-create` (through `CreateEngineAction`), `stand-new` from the spike; scenarios `car-tuning`, `car-bonus`,
  `engine-build`.
- Depends on (merged): rows 4, 5a, 5b (paint shop), 13 (dyno place), 18 and 24 (locks), 19 (per-entry details, D16).

## Open questions

Each has the default the draft works with.

1. **Lock while tuning.** **Default:** the tunable parts and the `tune` key are locked for others while the window is
   open; this also blocks a dyno run, park, delete and lift by others. Alternative: row 13's dyno claim for the whole
   car.
2. **Live tuning values.** **Default:** others see the values when the player applies a tab (`ApplyAction`/`Tune`), not
   while the sliders move.
3. **Bonus part paint.** **Default:** per slot, as the game stores it at runtime (the save format has one paint for all
   slots; the server keeps the runtime form).
4. **Engine price.** The decompiled path takes no money for a new engine block, and spike run 2 confirms it.
   **Default:** free, as in single player.
5. **Swapping a built engine into a car.** A new engine of another type would be an engine swap, which row 1 refuses
   while connected. **Default:** unchanged in this change; a built engine of the car's own type can be installed with
   the crane as today.
