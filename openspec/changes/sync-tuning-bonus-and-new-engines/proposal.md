# Proposal

## Why

The user wants three car-side features of single player in multiplayer (2026-10-08, ROADMAP row 25). The guard blocks
them today (`GuardRules`: `Window Tune`, `Mode BonusAssemble`/`BonusDisassemble`, pies `mode_bonus_assemble`/
`mode_bonus_disassemble`, `Window CreateEngine`, pie `engine_new`, all "Planned"):

1. **Tuning** (`TuneWindow`, tabs ECU, carburettor and gearbox). The player opens it at the dyno's tuning computer
   (`#dynoTune`, `GameScript.ClickIO`), for a car on the dyno with tuned (racing) parts; customer cars from an order are
   refused by the game (`GUI_NoTuneCarFromOrder`). Row 4 already syncs ECU and carburettor values per part (the
   `PartModule.Tune` postfix and the `t:<partKey>` detail entries) and reads and applies the gearbox ratios (`t:gearbox`),
   but nothing marks the car dirty when the gearbox tab is applied (`GearboxTab.ApplyAction` has no hook), and nothing
   stops two players from tuning the same car at once.
2. **Bonus (visual tuning) parts**: spoilers, bumpers, skirts and the like that fit into a car's bonus slots
   (`CarLoader.bonusParts`, `BonusPart { ID, UID, IsUnmounted, IsPainted, Color, PaintType, PaintData }`). Row 4's
   design planned a `BonusParts` section; the DTO exists (`ModBonusParts`, one ID list with one paint) but is never read,
   applied, hooked or stored, and it cannot carry a paint per slot.
3. **Building a new engine** on the engine stand (pie `engine_new` → `CreateEngineWindow` → `CreateEngineAction` →
   `EngineStandLogic.SetEngineOnEngineStand(new Item(engineId))`, an engine block with nothing mounted). Row 5a syncs
   everything on the stand once a group is on it (`SetGroupOnEngineStand` hooks, parts on the stand), and the server
   accepts a put of an item it never saw (`ToolsStore.Check`), so most of the path exists; it was never run, the
   harness verb for it never worked (`tool-stand-create` throws `BadImageFormatException` on
   `GameInventory.GetEnginesToCreate(out List<string>)`, spike run `20261008-230909_L2`), and the build replaces an
   engine that is already on the stand without asking.

"Car version" (`Window CarVersion`) is not a garage-car feature: the window is only used by the car salon and the main
menu's Showroom to pick a model's version before buying or viewing. It belongs to row 26 (`shared-salon`).

## What Changes

- **Tuning**
  - Hook `GearboxTab.ApplyAction` (postfix, car from `GearboxTab.carLoader`) → `CarDetailsSync.MarkDirty(car,
    Tuning)`; ECU and carburettor keep row 4's `PartModule.Tune` postfix.
  - **One tuner per car.** Opening `TuneWindow` takes row 18 locks on the car's tunable parts (the gearbox part and
    every part with a `PartModule`), new lock kind `Tune`; another player is refused with "<name> is tuning this car."
    (D16 answer). The locks end when the window closes. Other work on the car goes on.
  - The proof covers all three tabs, late join and a server restart.
- **Bonus parts**
  - New per-slot entries `x:<slot>` in the car details (`ModBonusSlot { Slot, Id, Unmounted, IsPainted, Color,
    PaintType, PaintData }`), merged per entry like the other details (row 19 part 1). The unused `ModBonusParts`
    field is replaced.
  - Read from `CarLoader.bonusParts`; applied with `BonusPart.Change(id, false)`, `Paint(…)`, `TakeOn(true)` /
    `TakeOff(true)`, never with `CarLoader.TakeOffBonusPart` (it adds to the inventory and reads the selected item).
  - Commit hooks: `CarLoader.TakeOffBonusPart(InteractiveObject, bool)` postfix (fit and remove), the paint shop's
    `SubmitColor` (bonus parts are painted with the car).
  - Inventory: removing a bonus part adds it to the inventory through a real `Inventory.Add` (synced today); fitting one
    removes the item with an inlined `List.RemoveAt` that no hook sees, so the fit sends the item's removal itself.
  - Locks: new lock kind `BonusPart` on key `x:<slot>` (exclusive), plus row 18's item lock for the fitted item; a
    click on a slot in another player's lock is refused with the message.
  - Guard: `Mode BonusAssemble`/`BonusDisassemble` and their pies become allowed.
- **New engine on the stand**
  - The put of a created engine is marked (`ToolSlotUpdate.Created = true`, `[OptionalField]`), so the server logs and
    counts it as created and the gap-9 item race rule does not apply to it.
  - Building on an occupied stand is refused before the game clears it ("Take the engine off the stand first."),
    because the game's build would silently destroy the engine on the stand (`ClearEngineStand` in
    `<SetGroupOnEngineStand>d__8` state 2).
  - Two players building at the same moment: the second put is refused by the stand's expected-UID check (row 5a) and
    answered (`ToolSlotRejected`, D16).
  - The harness verb `tool-stand-create` is fixed (engine id read without the generic out parameter).
  - Guard: `Window CreateEngine` and pie `engine_new` become allowed.

Hooks: `CMS.UI.Logic.Tune.GearboxTab.ApplyAction`, `CMS.UI.Windows.TuneWindow.Show`/`Hide` (lock and release),
`CarLoader.TakeOffBonusPart` (prefix: gate and item capture; postfix: commit), `CMS.UI.Windows.CreateEngineWindow.
CreateEngineAction` (prefix: occupied-stand refusal, created flag). Packets: changed only, all additive
(`CarLockKind` gains `Tune`, `BonusPart`; `ToolSlotUpdatePacket.Created`; `ModCarDetails.BonusSlots`).

## Capabilities

### New Capabilities
- `car-tuning-sync`: gearbox, ECU and carburettor tuning reach every player, one tuner per car.
- `bonus-parts-sync`: bonus parts fitted, removed and painted on a car look the same for everyone, with the item moving
  once through the shared inventory.
- `engine-build-sync`: an engine built on the engine stand appears for everyone and never destroys an engine already on
  the stand.

### Modified Capabilities
- None in `openspec/specs/` (rows 4, 5a and 18 are not archived; this change finishes row 4's bonus-parts and
  gearbox requirements and adds two lock kinds to row 18).

## Impact

- Core: `ModCarDetails` (`BonusSlots` replaces `BonusParts`), `CarDetailEntries` (`x:<slot>`), `DetailsMerge`,
  `CarDetailsStore` caps (16 slots), `LockPackets` (`CarLockKind.Tune`, `BonusPart`; `LockKeys.Bonus(slot)` with prefix
  `x:`), `ToolPackets` (`Created`).
- Server: `CarLocks` (the two kinds: `Tune` exclusive on the listed part keys, `BonusPart` exclusive on `x:` keys),
  `CarDetailsStore` (per-slot merge), `ToolsStore` (created puts counted and logged), cars section version bump
  (`BonusSlots`).
- Client: `Logic/Car/Details/CarDetailsHooks.cs` (gearbox, bonus), `CarDetailsIO` (bonus read/apply),
  `Logic/Car/Locks/LockTuneHooks.cs` and `LockBonusHooks.cs` (new), `Logic/Tools/EngineStandSync.cs` (created flag,
  occupied refusal), `Guard/GuardRules.cs`.
- Harness: `cardetails-gearbox <loader> <final> <r1,r2,…>`, `cardetails-ui gearbox <loader>` (runs `ApplyAction` on a
  prepared `GearboxTab`), `tune-open <loader>`/`tune-close` (the window with its locks), `bonus-probe`, `bonus-fit
  <loader> <slot> <itemUid>` and `bonus-remove <loader> <slot>` (through `TakeOffBonusPart` with the game's selected
  item), `tool-stand-create` fix, `stand-new` from the spike; scenarios `car-tuning`, `car-bonus`, `engine-build`.
- Depends on (merged): rows 4, 5a, 5b (paint shop), 13 (dyno place), 18 and 24 (locks), 19 (per-entry details, D16).

## Open questions

Each has the default the draft works with.

1. **Lock while tuning.** **Default:** the tunable parts are locked for others while the window is open; the rest of
   the car stays free. Alternative: row 13's dyno claim for the whole car.
2. **Live tuning values.** **Default:** others see the values when the player applies a tab (`ApplyAction`/`Tune`), not
   while the sliders move.
3. **Bonus part paint.** **Default:** per slot, as the game stores it at runtime (the save format has one paint for all
   slots; the server keeps the runtime form).
4. **Engine price.** The decompiled path takes no money for a new engine block. **Default:** free, as in single player
   (spike run 2 confirms or corrects; if the game charges, the charge goes through row 10's economy scope).
5. **Swapping a built engine into a car.** A new engine of another type would be an engine swap, which row 1 refuses
   while connected. **Default:** unchanged in this change; a built engine of the car's own type can be installed with
   the crane as today.
