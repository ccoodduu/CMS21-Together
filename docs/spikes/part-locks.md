# Spike: part locks (row 18, tasks group 1)

Change: `openspec/changes/part-locks/`. Date: 2026-10-07. Game: CMS 2021, IL2CPP, MelonLoader 0.5.7.

Static answers come from Il2CppDumper's `dump.cs` and Ghidra decompiles in
`%USERPROFILE%\CMS21-TestInstalls\native\out\locks_clean` and `locks2_clean` (targets
`native/work/targets/locks.txt`, `locks2.txt`; method in `docs/spikes/native-decompile.md`). Runtime answers come from
the spike scenario `tools/test-env/scenarios/locks-probe.ps1` and the harness file
`tools/TestHarness/Features/LockTraceCommands.cs` (`lock-trace`, `lock-probe`, `lock-click`), lane 1, headless:

| Run | What |
|---|---|
| `20261007-214438_L1_locks-probe` | relations of two cars, every entry point blocked and re-invoked after 150 ms |
| `20261007-215206_L1_locks-probe` | the same, with the input shim aiming at the part |
| `20261007-215950_L1_locks-probe` | relations, `lock-click` hover, hold and click, the caliper-with-piston chooser |

Traces are in each run folder (`trace-<step>.txt`, `findings.json`, `relations-<car>.tsv`).

## Summary

- The design holds. Every gated entry point can be blocked and re-invoked 150 ms later with the same result as a
  direct call (no exception, same mode change, same inventory change).
- Two decisions change (design.md updated in the same commit):
  1. **The crane takes one key `engine`.** Engine parts are siblings, not children of one part (`car_boltatlanta`: 111
     engine parts with no common part ancestor), so "X = the engine root" would be 111 keys. Every lock on an engine
     part now holds `engine` shared, and the crane holds it exclusively.
  2. **The item lock of a chooser-built group is taken when the item is picked, not at `SelectPartToMount`.**
     `ChoosePartUpWindow.SelectItemInCreateGroup(Item)` deletes the picked item from the inventory at once (so the
     client sends its removal), and `SubmitGroupItem` builds a new `GroupItem` that reaches
     `NotificationCenter.MountGroup` → `GameScript.SelectPartToMount` with a UID the server never saw. By then the
     member UIDs are gone on the server too. The item step therefore gates `SelectItemInCreateGroup` (one
     `ExtendLockId` request per picked item, before its `Inventory.Delete`) for groups, and `SelectPartToMount` for a
     single item. B1's rule (unknown UIDs are logged, not refused) stays for the group UID.
- Lock sets stay small: the largest has 21 keys (`car_boltatlanta`, cylinder head), the mean is 4.7. No narrowing of D3
  is needed beyond the crane key.
- `lock-click` works: with a harness patch on the button state and `ProMouse.GetLocalMousePosition` plus a camera aimed
  at the part, the game's own `Raycast` hovers the part, fills the hold ring and calls `ActionUnMount`. On headless test
  games the part has to be moved to layer 16 `Part` and its `PartScript` enabled first (see 1.7).

## 1.1 Hover and selection

Static (`Raycast$$PartSelect.c`, `Raycast$$Update.c`, `PartScript$$Update.c`, `GameScript$$SetPartMouseOver.c`):

- `Raycast.Update` dispatches per mode; the per-mode methods (`PartSelect`, `PartSelectMount`, `Garage`,
  `InteriorDisassemble`, `InteriorAssemble`, `GarageAssemble`, `PartUnMountPartMount`) are inlined into it, so they
  cannot be patched. The calls they make are real calls.
- Part modes: `Physics.Raycast(camera ray, 3.5 m, partSelectRaycastMask = layers 16 Part + 23)`; on a hit with a
  mounted part it calls `PartScript.SetMouseOver()` (the overload **without** arguments, which walks `unmountWith`) and
  `GameScript.SetPartMouseOver(part)`. A miss calls `SetPartMouseOver(null)`.
- The highlight is drawn by `PartScript.Update` from the field `MouseOver` (`CMS_Highlighter.On(colour)`), and
  `Update` clears `MouseOver` at the end of every frame. So the highlight lives exactly as long as
  `SetMouseOver()` keeps being called. `PartScript.SetMouseOver(bool)` is a one-line setter that the hover path does not
  call (0 calls in the runs).
- `SetPartMouseOver(part)` sets `partMouseOver`, `raycastOnItemID`/`raycastOnItemName` and calls
  `UIManager.SetIODescription(localized name, AlternativeDescriptionID)`; this is the label. It runs every frame
  (19 calls of `SetPartMouseOver`, 30 of `SetIODescription` in a 1.2 s hover).
- Body panels and interior parts (`Garage`, `InteriorDisassemble`, `InteriorAssemble`, bonus modes) highlight through
  `InteractiveObject.SetMouseOver(bool)` / `(bool, Color)` (real calls in `Raycast.Update`, both call
  `HighlightAll`/`HighlightNone`) and label through `GameScript.SetIOMouseOver(GameObject, string, InteractiveObject)`.
- The highlighter colour is local to each client (`CMS_Highlighter.On(Color)`), so an orange highlight would be
  possible; open question 5 keeps its default (no highlight).

Runtime (`trace-click-hover-hold.txt`, run `215950`):

```
689  PartScript.SetMouseOver() 0/s:13.120(v8_kolektor_wydechowy_stary_1) mode PartSelect
689  SetIODescription ClickMoveHoldTakeOff 'Exhaust Manifold (V8 OHV)'
690  SetPartMouseOver 0/s:13.120 ... label '!v8_kolektor_wydechowy_stary_1' description 'Exhaust Manifold (V8 OHV)'
```

**Hooks for D9:**

| Row | Hook | Fires on the real path |
|---|---|---|
| Part highlight | prefix on `PartScript.SetMouseOver()` (no arguments) returns `false` | yes (runtime) |
| Part label | postfix on `GameScript.SetPartMouseOver(PartScript)`: `UIManager.SetIODescription(message, type)` | yes (runtime), every frame |
| Body/interior highlight | prefix on `InteractiveObject.SetMouseOver(bool)` and `(bool, Color)` | static only |
| Body/interior label | postfix on `GameScript.SetIOMouseOver` | static only |
| Click | the gate on `ActionUnMount`/`ActionMount`/`TakeOffCarPart` | yes |

Skipping `SetMouseOver()` leaves `partMouseOver`, the label and the click intact, because those come from
`SetPartMouseOver`, which is not touched (M2).

## 1.2 Mount flow, item UIDs and back-outs

- `ActionMount(true)` on an unmounted slot: `SetPartMouseOver(main)`, then `ChoosePartUpWindow.Show(List<BaseItem>)`
  for a single item or `Show("MountGroup")` for a part with members (caliper with piston); `Show` sets mode `UI` (7).
  On a mounted part it plays the error sound and returns (mode unchanged).
- Back-out: `ChoosePartUpWindow.Hide(bool)` → `SetCurrentMode(previousMode)` (traced: `Hide(False)` →
  `PartSelectMount`). A `Hide` that is not followed by `SelectPartToMount`/`MountGroup` is the back-out signal.
- Single item: the chooser's accept goes `NotificationCenter.NewButtonAccept` → `GameScript.SelectPartToMount(item)`
  (static, `xref`), which starts `partMouseOver.DoMount()` in part modes. In body modes (1, 2, 3, 4, 21, 22) it mounts a
  body part through `CarLoader.UpdateCarBodyParts` instead: that is the body mount entry.
- Group (runtime, `trace-caliper-group.txt`):

```
SelectItemInCreateGroup item zaciskHamulcowy_1#1000000000002
Inventory.Delete item zaciskHamulcowy_1#1000000000002
SelectItemInCreateGroup item zaciskHamulcowy_tloczek_1#1000000000003
Inventory.Delete item zaciskHamulcowy_tloczek_1#1000000000003
SubmitGroupItem [item zaciskHamulcowy_1#...2; item zaciskHamulcowy_tloczek_1#...3]
ChoosePartUpWindow.Hide(False)
```

  `SubmitGroupItem` builds `new GroupItem(items)` and calls `NotificationCenter.MountGroup(uid)` →
  `SelectPartToMount(group)` (static). The members' UIDs are deleted (and sent as removals) when they are picked. Hence
  decision 2 above.
- `CleanUnfinishedMount`/`CleanUnfinishedUnMount` are called only from `GameMode.SetCurrentMode` and
  `GameScript.HandleGameModeChange`: when the current mode is `UI` (7), the previous one was `PartMount` (9) /
  `PartUnMount` (10) and the new one is something else. That is ESC (pause/pie, mode 7) followed by any other mode. They
  call `UndoMounting`/`UndoUnMounting`, which stay the back-out signals of the bolt view.
- Modes the gated actions set themselves (ignored by the mode-change release):

| Action | Self-set modes |
|---|---|
| `ActionUnMount` | `PartUnMount` (10), via `SelectToUnMount` |
| `ActionMount` | `UI` (7) while the chooser is open; back to the previous mode on `Hide` |
| `DoMount` | `PartMount` (9) |
| bolts done | back to `PartSelect`/`PartSelectMount` |
| `FluidExtractor.Use` | `DrainTool` (0x17) about 3 s later |
| `ChangeCarPos` | the previous mode at its end |
| `CarLifter.Action` | none seen at runtime |

- Group unmount: `PartScript.UnMountByGroup` is started by `NotificationCenter.ActionUnMountGroup(InteractiveObject)`
  (crane and IO groups), by `PartScript.<Hide>d__159` (members) and by the engine stand. The void caller to gate for
  `GroupUnmount` is `ActionUnMountGroup`, as for the crane.

**Started predicates (D5):**

| Kind | Started when (checked right after the re-invoked call) | Runtime |
|---|---|---|
| `PartUnmount` | `SelectToUnMount(main)` ran in the call, i.e. the mode became `PartUnMount` (`SelectedPart` alone is stale: it keeps the last unmounted part); for `oneClickUnmount` parts `Hide` started | yes; refused part (`canBeUnmount == false`) leaves the mode unchanged |
| `PartMount` slot | the chooser is active (`WindowManager.IsWindowActive(ChoosePartUp)`) | yes; mounted part: no chooser |
| `PartMount` item | `DoMount` started (or `MountGroup` found its group) | static |
| `BodyPart` | `TakeOnOffInProgress` or `Unmounted` flipped | yes: both true at once, item added synchronously |
| `Fluid` refill | `FluidRefill.IsActive` | yes |
| `Fluid` extractor | `FluidExtractor.IsActive` | yes |
| `OilDrain` | the first `MoveNext` of `<UseOilDrain>d__40` returned `true` | yes; with no oil it returns `false` |
| `Lift` | `isMoving` false before and true after | yes; while moving the call returns without effect |
| `Crane` | an engine group appeared in the inventory (the check of `EngineCraneHooks.AfterUnMountGroup`) | ran without error |
| `Move` | the coroutine advanced past state 0 | restart works (`car-move` verb) |

## 1.3 Relations and lock-set sizes

`lock-trace relations <loader> <file>` (uses `LockSets.Build`). Both cars have all five fluids (`f:EngineOil.0`,
`f:Brake.0`, `f:EngineCoolant.0`, `f:PowerSteering.0`, `f:WindscreenWash.0`), so a third car was not needed.

| Car | Parts | Largest set | Mean | Over 40 | With blocking | With fluids | Engine parts |
|---|---|---|---|---|---|---|---|
| `car_boltatlanta` | 228 | 21 (`s:13.25` cylinder head: X1 S20) | 4.7 | 0 | 175 | 13 | 111 |
| `car_sixoncebulion` | 224 | 20 (`s:13.6` cylinder head) | 4.3 | 0 | 168 | 14 | 74 |

Static:

- `unblockOnUnmount` (serialized) lists the parts a part frees when it comes off; `PartScript.Start` turns it into the
  runtime `blockedBy` with `AddToBlockedBy`. The reverse index is built from the same arrays.
- A part "contains" a fluid when the `CarFluid` is on its **parent** transform (`CheckIsFluidContainer`,
  `GetFluidId` read the parent's `CarFluid`), not below it.
- Draining on unmount is driven by `sendMessageOnHide` in `<Hide>d__159`: `ZeroOil`, `ZeroBrakeFluid`,
  `ZeroEngineCoolant`, `ZeroAllEngineCoolant`, `ZeroWindscreenWash`, `ZeroPowerSteering` (id from the parent's
  `CarFluid`, `ZeroAll…` = every id of that type).
- Body panels: `CarPart.ConnectedParts` lists names (doors: mirror and window).

**Pair for `locks-connected`** (`car_boltatlanta`): crankshaft `s:13.5` (`v8_walKorbowy_stary`, `oneClickUnmount`)
and bearing caps `s:13.65`, `s:13.66`, `s:13.67` (`pokrywa_lozyska`). Each cap holds `s:13.5` shared (it is in the
cap's `unblockOnUnmount`), the crankshaft holds the caps shared (reverse index); two caps share only `s:13.5` and
`s:13.63` (oil pan) shared, so they are compatible.

**Part-to-fluid map (D4)**, `car_boltatlanta` (bulion is alike):

| Part | Key | Why |
|---|---|---|
| `korek_spustowy_1` (drain plug) | `f:EngineOil.0` | drain plug rule |
| `v8_filtr_oleju_stary` (oil filter) | `f:EngineOil.0` | `FluidRefillLockType = EngineOil` |
| `v8_miska_olejowa_stara` (oil pan) | `f:EngineOil.0` | lock type, `ZeroOil` |
| `v8_pompaWody_stara` (water pump) | `f:EngineCoolant.*` | lock type, `ZeroAllEngineCoolant` |
| `chlodnica_2` (radiator), its cap | `f:EngineCoolant.0` | parent `CarFluid`, `ZeroEngineCoolant` |
| `pompa_wspomagania`, reservoir, cap | `f:PowerSteering.0` | lock type / parent / `ZeroPowerSteering` |
| washer reservoir, cap | `f:WindscreenWash.0` | parent / `ZeroWindscreenWash` |
| `serwoHamulca_1` (brake booster), cap | `f:Brake.0` | parent / `ZeroBrakeFluid` |

No hose holds a fluid, so `locks-fluid`'s "two coolant parts together" uses the water pump and the radiator (both
hold `f:EngineCoolant.0` shared).

## 1.4 Re-invocation, started predicates and finishers

Every entry point was blocked in a `Priority.First` prefix and re-invoked 150 ms later inside a bypass
(`trace-reinvoke-*.txt`, run `214438`):

| Entry point | Re-invoked | Result |
|---|---|---|
| `PartScript.ActionUnMount()` | ok | `SelectToUnMount`, mode `PartUnMount` |
| same, `canBeUnmount == false` | ok | nothing; mode unchanged |
| `PartScript.ActionMount(true)` | ok | chooser; on a mounted part nothing |
| `CarLoader.TakeOffCarPart(string)` | ok | panel off, item added |
| `CarLifter.Action(int)` | ok | moving; while moving nothing |
| `CarLoader.UseOilbin()` | ok | drain runs (about 6 s); no oil: first step `false` |
| `FluidRefill.Use()` | ok | `IsActive`, fluid `EngineCoolant.0` |
| `FluidExtractor.Use()` | ok | `IsActive`, then mode `DrainTool` |
| `NotificationCenter.ActionUnMountGroup(iO)` | ok | no exception |
| `ChangeCarPos` | restart from code works (`car-move`: `StartCoroutine(ChangeCarPos(carLoader, place, false))`, after setting `previousMode`) | |

`FluidRefill.Use()` with no car under the cursor throws a `NullReferenceException` when called directly (the game only
calls it with a car under the cursor); the gate needs no case for it.

`Context` (D1) is confirmed as: game mode, the target alive, the target's mount state, the car's `SpawnSeq`, the top
window. Nothing else changed in 150 ms.

**Finisher for `lock-try … finish`:** unmount: `MountObject.SetCanAction(true)` + `Action()` per frame until every
bolt is out (as `vfx-unscrew`); `PartScript.Update` then commits with `Hide`. On headless test games the `PartScript` is
disabled, so the finisher starts `Hide()` itself (or enables the script, as `lock-click` does). Mount: the chooser item
→ `SelectPartToMount`/`MountGroup` → `DoMount`, then the same bolt loop towards 1.0 and `ShowMounted`. `ActionAutomatic`
is not a finisher (it stops `Hide` and re-mounts).

## 1.5 Lift and move

- The lift buttons reach `CarLifter.Action(int)` through `GameScript.ClickIO`; `LifterSync` already patches it on the
  real path (row 2). It is the lift hook.
- Restart call for a move: `NotificationCenter.Get().StartCoroutine(NotificationCenter.Get().ChangeCarPos(carLoader,
  place, movePlayerToCar))`, with `GameMode.previousMode` set to the current mode first (the coroutine ends with
  `SetCurrentMode(previousMode)`).
- Pie option ids for `part-locks-2`: not needed in this change.

## 1.6 Hold

- `Cursor3D.Update`: while `canCountTime`, `holdTime += deltaTime * 1000`; above 150 ms the ring fills with
  `(holdTime - 150) / fillTime`. `GetIsButtonHold()` is `fill == 1`.
- `fillTime` comes from `GameMode.RecalculateFillSpeedForMode`: `PartSelect` 1500 / `fast_partremove` upgrade
  (1000 ms on the test profile), `PartUnMount`/`PartMount` 3000 / `fast_mount` (2000 ms).
- Hold start: `Raycast` sets `Cursor3D.canCountTime = true` on the button press over an accessible part. Runtime:
  `hold start` one frame after the press, `hold full after 1134 ms`, then `ActionUnMount` in the same frame and
  `ResetButton` right after. So the fill takes 150 + `fillTime` ms, far longer than a relay round trip: prefetch stays.
- M1's "the blocked call repeats every frame" does not happen: `Raycast` calls `ResetButton` right after
  `ActionUnMount`, which clears `canCountTime`.

## 1.7 Input shim (`lock-click`)

Works. `lock-click <loader> <key> hold <ms>`:

- puts the game in `PartSelect` (`UnmountGroup = 10`, so every part group is accessible);
- moves the part from layer 28 `PartsDisabled` to 16 `Part` and enables its `PartScript` (headless test games keep
  every part disabled; a player's game has them enabled on `Part`);
- finds a camera pose 0.4–2.2 m from the part with a clear `Physics.Raycast` on the game's mask, and sets the main
  camera there in a `Raycast.Update` prefix;
- returns the screen centre from `ProMouse.GetLocalMousePosition` and drives `InputManager.GameplayMechanicAction-
  ButtonDown/Button/ButtonUp`.

Result: `PartScript.SetMouseOver()` → `SetPartMouseOver` → label → `hold start` → `hold full after 1134 ms` →
`ActionUnMount` → `SelectToUnMount` → `PartUnMount`; a 300 ms press is a click (focus, no unmount). The real path of
hover, label, hold and click can therefore be tested headless (`locks-select`, `locks-latency`); body panels and
interior parts need the same shim in garage modes (not tried).

## Patch audit (D1, M6)

Existing postfixes on gated methods, which also run on the blocked first call:

| Postfix | Change in 5.1 |
|---|---|
| `EngineCraneHooks.AfterUnMountGroup` | must check `__runOriginal` (sends an inventory Add for any engine group today) |
| `EngineCraneEffects` (`ActionUnMountGroup`, `InsertEngineToCar`) | already checks `__runOriginal` |
| `PartHooks.AfterTakeOffCarPart` | harmless (marks the loader dirty), made consistent |
| `LifterSync.AfterAction` | harmless (compares state), made consistent |
| harness `PlacementTraceCommands`, `LockTraceCommands` | logging only |
