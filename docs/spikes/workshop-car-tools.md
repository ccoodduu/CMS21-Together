# Spike: workshop car tools (row 5b `sync-workshop-car-tools`)

Static decompile only (setup in `native-decompile.md`), game not launched. Date: 2026-10-06.
Covers welder, car wash, interior detailing (portable and stationary), oil bin, paint shop (car), and, as far as row 5b
touches them, window tinting, headlamp alignment, OBD and dyno. The engine crane is in `engine-crane.md`.

Outputs: `%USERPROFILE%\CMS21-TestInstalls\native\out\cartools_clean`, `cartools2_clean`, `cartools3_clean`
(targets `work\targets\cartools*.txt`). Every "direct call", "tail jump", "virtual" and "inlined" below was checked
with `xref.py` (E8/E9 scan) and, for the central functions, with a capstone disassembly of the raw bytes.
"Fires" means the native entry of the method is reached (call, `jmp` or vtable dispatch), so a Harmony detour runs.

## Design-impacting findings (read first)

1. **`PaintshopManager.MakeCarPaintEffects()` never fires.** `<MakePaintEffects>d__80.MoveNext` builds
   `<MakeCarPaintEffects>d__81` inline (capstone: no call to `0x1809F8140`, only `StartCoroutine`). `d__81` is effects only.
   The **car paint is already final at `PaintshopManager.SubmitColor`**: the tabs paint the car live as a preview
   (`UpdateColor` → `CarLoader.SetCarColor`, `SetPaintType`, `SetLivery`), and `SubmitColor` makes that the new backup.
   Hook the postfix of `SubmitColor` (real call) instead. See "Paint shop".
2. **The `FinishAnim` builders never fire.** `WelderLogic.FinishAnim`, `InteriorDetailingToolkitLogic.FinishAnim` and
   `GarageTool.FinishAnim` have no callers: each `<DoWorkAnim>d__1` builds its `<FinishAnim>d__N` inline. You do not need a
   coroutine watcher. **`<DoWorkAnim>d__1.MoveNext` returns `false` only after `FinishAnim` has finished**, because state 0 does
   `yield return StartCoroutine(FinishAnim)`. A postfix with `__result == false` is the commit point.
3. **`ToolsManager.UseOilDrain` never fires.** `CarLoader.UseOilbin` builds `<UseOilDrain>d__40` inline. `UseOilbin`
   itself fires (direct call from `ToolsMoveManager.Use`).
4. **Never patch `WelderLogic.Use()` or `InteriorDetailingToolkitLogic.Use()`** (the overloads without arguments). Both are the
   shared empty stub `0x180336E30`, folded with hundreds of `Dispose`/`.ctor` methods. `CarWashLogic.EnableInteractiveObjects`
   and `WelderLogic.EnableInteractiveObjects` are also one function (`0x180929980`).
5. **The car wash is free.** It has no money, XP or stat calls. The welder and interior detailing charge only inside the
   ask-window lambda. Interior detailing is **free when the `car_wash` upgrade is unlocked**.
6. **Interior detailing touches only `CarPart`s, no `PartScript`.** The design's "`PartScript` dust → `CarPartsSync`" split is
   not needed for this tool. It changes `details`' Condition, and Dent and Dust on 8 interior `CarPart`s (see below).
7. **Opening the paint shop (or the tint window) on a dirty car** may wash it: an ask window, then
   `EnableDust(null, 0)`, `SetWashFactor(null, 1)` and `AddPlayerMoney(-100)`. This is a BodyCosmetics change outside
   any tool hook.
8. Every tween starts with `if (LeanTween.isTweening(root)) return;` (`TweenCondition`, `TweenExteriorDustWash`,
   `TweenInteriorConditionAndDust`). The money is already gone by then, so a second tool started during a running tween
   changes nothing (welder: Dent still changes). The result senders must diff the real state and not assume an outcome.

## Common structure (welder, car wash, interior detailing)

`GarageTool` fields: `effectTime` 0x18, `particles` 0x20, `interactiveObject` 0x28, `sfx` 0x30, `shapeModule` 0x38,
`carCollider` 0x40. Vtable: slot 4 `DoWorkAnim`, 5 `StartAnim`, 6 `Use()`, 7 `Use(CarLoader)`.

```
click / pie menu → ToolsMoveManager.Use(IOSpecialType) or CarWashLogic.Use()      (fire)
  → <Tool>Logic.Use(CarLoader)  (virtual, fires): ShowAskWindow(…, Action<bool> PDM_0)
  → <>c__DisplayClassN_0.Method_Internal_Void_Boolean_PDM_0(bool wasAccepted)      (delegate, fires)
       accepted: [AddPlayerMoney(-cost)], StartCoroutine(this.DoWorkAnim(car))      (virtual, fires)
       declined: jmp GameMode.SetCurrentMode(…)
  → <DoWorkAnim>d__1.MoveNext state 0: StartAnim (locks), tween (state change), yield StartCoroutine(inline FinishAnim)
                               state 1: re-enable, return false   ← commit point
```

`StartAnim` (welder `0x180928A30`, interior `0x180C730B0`, base `GarageTool.StartAnim` `0x180D6C570` for the car wash):
`interactiveObject.enabled = false`, `GameMode.SetCurrentMode(previous)`, `CarLoader.CloseCar(true)` (welder and
car wash), `CarLoader.EnableIO(false)`, lifter `ButtonDown`/`ButtonUp` disabled (welder and interior), particle shape on
the car mesh (`body`, `details` or `CarPhysicCollider(Clone)`), `SoundManager.PlaySFX(sfx, pos)`, `particles.Play`.
`FinishAnim` waits while `particles.IsAlive`, then `EnableIO(true)`, lifter buttons and `interactiveObject` back on.
This is the lock that FixForTogether saw: **a remote client must never run `DoWorkAnim`/`StartAnim`.**

Tool connection: `ToolsMoveManager.MoveTo(tool, place)` sets `CarLoader.ToolsData.<Tool>IsConnected` (positions are
owned by `sync-workshop-machines`). `ToolsMoveManager.Use(tool)` `0x1810D72A0` loops over the loaders and picks the
first one with the flag. The pie menu (`PieMenuController.<GetOnClick>b__72_74`) checks `PlayerMoney >= GetWelderCost` /
`GetInteriorDetailingCost` first (`GUI_BrakKasy`). This check is UI only. `CanUseEquipment` refuses welder and interior while the lift is
`Up`.

## Welder

| Step | Method (VA) | Notes |
|---|---|---|
| use | `ToolsMoveManager.Use(11)` → vtable `WelderLogic.Use(CarLoader)` `0x1809296C0` | `WelderIsConnected` |
| ask | `UIManager.ShowAskWindow(GUI_PotwierdzenieNaprawy…)` | text includes the cost |
| accept | `WelderLogic.__c__DisplayClass5_0.Method_Internal_Void_Boolean_PDM_0` `0x1818CD1D0` | `AddPlayerMoney(-GetWelderCost())` **real call** (`0x1818CD247`), then virtual `DoWorkAnim` |
| builder | `WelderLogic.DoWorkAnim(CarLoader)` `0x180928930` | plain allocator, fires (vtable) |
| work | `WelderLogic._DoWorkAnim_d__1.MoveNext` `0x1818CD2E0` | see below |

`d__1` state 0 (capstone-checked):
```c
EnableInteractiveObjects(false); this.StartAnim(car);                  // virtual
car.TweenCondition("body", 1.0f, effectTime);                           // 0x18050AFD0: body.Condition → 1 (LeanTween)
car.SetDent(car.GetCarPart("body"), 1.0f);                              // 0x1804F95F0
car.SetDent(car.GetCarPart("details"), 1.0f);
yield return StartCoroutine(new <FinishAnim>d__3(this, car));           // builder inlined
// state 1: EnableInteractiveObjects(true); return false
```
`TweenCondition` returns early when `root` is tweening, the part has no handle, or the part is `Unmounted`. Its update lambda
(`<>c__DisplayClass468_0.<TweenCondition>b__0` `0x18171A970`) writes `Condition` and calls `SetConditionOnBody`/
`SetConditionOnDetails` for `body`/`details`. It is folded with the interior lambda `351_0.b__1`. The complete
lambda `b__1` writes the final `Condition`.

- Car state: `CarPart body`: `Condition` 1, `Dent` 1. `CarPart details`: `Dent` 1. All row 1 (`CarPart` record).
- Cost: `GetWelderCost` `0x1804C8480` = `Convert.ToInt32((uniqueMod-1)*1000 + 500)` (banker's rounding). No XP, no stat.
- `CarLoader.UseWelder()` `0x18050AF10` (sets `body.Condition = 1`, `UpdateCarBodyPart`) has **no callers**. It is legacy; do not hook it.

## Car wash

| Step | Method (VA) | Notes |
|---|---|---|
| click | `GameScript.ClickIO` IO `#carWash` → virtual call slot 6 on `ToolsMoveManager.CarWashLogic` (+0xB8) | il2cpp virtual-invoke helper `0x180081F40(6, obj)` at `0x180E8AAD9` |
| use | `CarWashLogic.Use()` `0x180AEF850` → `GetCarLoaderForPlace(CarPlace.CarWash = 8)` → virtual `Use(CarLoader)` `0x180AEF8E0` | no car: `GUI_CarNotConnected`. Otherwise `GameMode.SetCurrentMode(7)` and ask `GUI_PotwierdzenieMycia` |
| accept | `CarWashLogic.__c__DisplayClass3_0.Method_Internal_Void_Boolean_PDM_0` `0x180770050` | **no money**, virtual `DoWorkAnim` |
| work | `CarWashLogic._DoWorkAnim_d__1.MoveNext` `0x180770110` | `EnableInteractiveObjects(false)`; **direct** `GarageTool.StartAnim` (`0x180770153`); `TweenExteriorDustWash(0, 1, effectTime)`; inline `GarageTool.<FinishAnim>d__8`; state 1 re-enables |

`CarLoader.TweenExteriorDustWash(targetDust, targetWash, time)` `0x1804F54F0`:
- returns if `root` is tweening;
- for every mounted `CarPart` with a handle, it tweens `WashFactor` (+0xB0) to `targetWash` (=1). Lambda `350_1.b__0` writes the field and
  the material `WashFactor`;
- for `details` (plus `details2`, `details3` when present), it tweens `Dust` (+0xAC) to `targetDust` (=0) (`b__2`);
- it also tweens material dust to 0 through `CarHelper.SetDustValue` for materials whose name matches a `GameInventory` list
  (`b__4`/`b__5`). *Uncertain:* which materials.

- Car state: `WashFactor` 1 on all mounted `CarPart`s; `Dust` 0 on `details*`. All BodyCosmetics (row 4).
- No cost, no XP, no stat. The car wash does not touch the lift buttons (`GarageTool.StartAnim` has no lifter code).

## Interior detailing (portable and stationary)

| Step | Method (VA) | Notes |
|---|---|---|
| portable | `ToolsMoveManager.Use(12)` → virtual `InteriorDetailingToolkitLogic.Use(CarLoader)` `0x180C73AD0` | `InteriorDetailingToolkitIsConnected` |
| stationary | `GameScript.ClickIO` → `ToolsMoveManager.Use(0x17)` → **call** `UseInteriorDetailingToolkitStationary` `0x1810D7540` (`0x1810D7521`) | car = `GetCarLoaderForPlace(8)` (car-wash place). Then `GameMode.SetCurrentMode(7)` and the **same** virtual `Use(CarLoader)` |
| ask | `Use(CarLoader)` | `haveCarWash = UpgradesHelper.FindUpgrade(…, "car_wash")` unlocked (*field at +0x20 assumed "unlocked"*). Text `GUI_PotwierdzenieSprzatania` with cost, or `…Myjnia` (free) |
| accept | `InteriorDetailingToolkitLogic.__c__DisplayClass6_0.Method_Internal_Void_Boolean_PDM_0` `0x180EAB520` | `if (!haveCarWash) AddPlayerMoney(-GetInteriorDetailingCost())`, **real call** `0x180EAB5A1`. Then virtual `DoWorkAnim` |
| work | `InteriorDetailingToolkitLogic._DoWorkAnim_d__1.MoveNext` `0x180EAB640` | virtual `StartAnim`; `TweenInteriorConditionAndDust(1, 0, effectTime)` (`0x180EAB6B4`); inline `<FinishAnim>d__3` |

`CarLoader.TweenInteriorConditionAndDust(targetCondition, targetDust, time)` `0x1804F6640`:
- returns if `root` is tweening;
- `benchFront`, `bench`, `steeringWheel`, `seatLeft`, `seatRight`: `FixDent` (→ `SetDent(part, 1)` if mounted with a handle) and
  `Dust = 0` immediately;
- `details`: `FixDent`; returns early if it has no handle or is `Unmounted`. `Condition` is tweened to 1 (`b__1` = the
  `TweenCondition` lambda: field + `SetConditionOnDetails`, `b__2` final value), and `Dust = 0`;
- `details2`/`details3` if found under `model`: `Dust = 0`, `FixDent`;
- material dust is tweened to 0 (`b__3`/`b__4`, `CarHelper.SetDustValue`).

- Car state: `details.Condition` 1 and `Dent` 1 on up to 8 interior `CarPart`s → row 1. `Dust` 0 on the same parts → row 4
  BodyCosmetics. **No `PartScript` is touched.**
- Cost: `GetInteriorDetailingCost` `0x1804C8500` = `Convert.ToInt32((uniqueMod-1)*150 + 100)`, or 0 with `car_wash`. No XP or stat.
- `CarLoader.UseInteriorDetailingToolkit()` `0x18050B390` (sets `details.Condition = 1`) has no callers. It is legacy.
- To tell the stationary from the portable variant in a `DoWorkAnim` prefix: the car stands at `placeNo == 8`, or a flag set in
  the `UseInteriorDetailingToolkitStationary` prefix (that prefix fires).

## Oil bin

| Step | Method (VA) | Notes |
|---|---|---|
| use | `ToolsMoveManager.Use(13)` → **call** `CarLoader.UseOilbin()` `0x18050B450` (`0x1810D7462`), then `jmp GameMode.SetCurrentMode` | `OilbinIsConnected`. No ask window, no cost |
| builder | `UseOilbin` builds `ToolsManager.<UseOilDrain>d__40` inline and starts it **on the CarLoader** | `ToolsManager.UseOilDrain` `0x1810D36D0`: no callers |
| work | `ToolsManager._UseOilDrain_d__40.MoveNext` `0x180B84840` | below |

`d__40`:
- State 0 returns `false` **without any change** when the `EngineOil` (3) level is 0, there is no `e_engine_h`, or the drain plug
  `korek_spustowy_1_0` under the engine is inactive.
- Otherwise it hides the plug, moves `Oil_drain_h` there, plays the particles and the `OilDrain` loop SFX, sets the oil bin
  `InteractiveObject.enabled = false` and calls `CarLoader.EnableIO(false)`, then waits `level * 5` s.
- State 1/2: stops the SFX and waits for the particles to die. Then:
```c
FluidsData.SetCondition(0, EngineOil, 0); FluidsData.SetLevel(0, EngineOil, 0);   // real calls, 0xB84AE8
car.EnableIO(true); plug.SetActive(true); ToolsManager.OnOilDrainFinished?.Invoke(); return false;
```
- Car state: oil level and condition 0, written only at the end → row 4 Fluids. No money, XP or stat.
- `CarLifter.Action` refuses to move while `OilbinIsConnected` (see `car-placement.md`).

## Paint shop (car)

Entry (`PaintshopWindow.Show` `0x1809F95C0`):
- When `CarLoader.NeedToWashCar()` is true (dust > 0 or any `WashFactor < GlobalData.WashFactorLvlToClean`), it asks
  `GUI_PotwierdzenieMyciaPaintshop` (100).
- Answer: `PaintshopWindow.Method_Private_Void_Boolean_PDM_0` (`<Show>g__ClearCarAction|8_0`) checks the money
  (`GUI_BrakKasy`), then starts `ShowCoroutine(clearCar)`.
- `<ShowCoroutine>d__10` with `clearCar`: `EnableDust(null, 0)`, `SetWashFactor(null, 1)`, **`AddPlayerMoney(-100)`**
  (real, `0xA7CBCD`). Then `BackupColor`, `CloseCar(true)`, and so on.
- `TintingWindow` does the same (`<ShowCoroutine>d__18`, `0xB801E6`).

Preview: the tab handlers (`PaintColorTab.OnHUEChanged`, `LiveriesTab.OnLiveryChange`, `PaintTypeTab.On*Change`, …) call
`UpdateColor` (→ `CarLoader.SetCarColor`, `PaintHelper.SetColor`), `SetPaintType` (→ `CarLoader.SetCarPaintType`),
`SetCustomPaintType` and `SetLivery` (→ `CarLoader.SetCarLivery`) **on the live car**. Cancel restores the backup through
`RestoreColor` (`PaintshopWindow.RestoreColorIfNeeded`, `<HideCoroutine>d__11`). Row 4 must not send during the preview.

Commit (identical in all three overrides):

| Step | Method (VA) | Notes |
|---|---|---|
| button | `PaintshopTab.PaintCarAction` `0x1809F8900` → virtual `PaintCar()` | `PaintshopTab <PaintCar>d__14` `0x180A7BC20`, `BasicTab <PaintCar>d__28` `0x1807BC8D0`, `AllowedColorsTab <PaintCar>d__11` `0x1807B6030` |
| pay | `PaintshopManager.TryGetMoneyForPaint` `0x1809F5040` (real) | `Garage` (0): money ≥ 1000 → `AddPlayerMoney(-1000)`; `GaragePart` (1): 100; `Showroom`/`Editor`: free. False → `GUI_BrakKasy`, nothing painted |
| **commit** | **`PaintshopManager.SubmitColor(false)` `0x1809F4E20` (real; only callers are the three `PaintCar` coroutines)** | `BackupColor()` (copies `carParts`/`bonusParts` paint into the backups, so no restore follows) + `PaintshopData.AddToLastColors` + `colorChangedButNotSubmitted = false` |
| effects | `MakePaintEffects()` `0x1809F80B0` (real builder) → `<MakePaintEffects>d__80` `0x180A7ADD0` | `isPainting = true`, IOs off. `Garage`: inline `<MakeCarPaintEffects>d__81` `0x180A7A680` (`PrepareCollider`, `EnableIO(false)`, SFX `CarPaint`, particles, collider destroyed, `EnableIO(true)`). Then `isPainting = false` |
| stat | `PaintCar` coroutine state 1 | `IncrementStat("stat_paint_car")` (vtable +0x1D8), `Garage` only |

- Car state: colour, paint type and data, livery per `CarPart` and `BonusPart` (`BackupColor` reads `CarLoader.GetBonusParts`),
  all set during the preview and final at `SubmitColor` → row 4 `Paint | BodyCosmetics | BonusParts`.
- `GaragePart` paints an inventory `Item`. `ExitFromPaintshopPart` calls `Inventory.Add` (machines row / inventory, not this change).

## Window tinting, headlamp alignment (row 4 owns the result) and OBD (row 1)

- Tint: `ToolsMoveManager.Use(24)` **tail-jumps to `WindowTintManager.Use`** `0x1807B0BF0` (capstone `0x1810D73FB`), which opens
  `TintingWindow` (0x40).
  - `TintingWindow.TintAction` `0x1810C9530` (delegate, fires) counts the tinted windows and does `AddPlayerMoney(count * -50)`
    (real). It then writes `CarPart.IsTinted`/`TintColor` directly.
  - **`TintingWindow.TryGetMoneyForTint` has no callers**: `TintAction` contains its own copy of the code.
  - The window also has the clear-car path from the paint shop (−100).
- Headlamp alignment: `LampAlignmentWindow` has no money, XP or stat calls. `HeadlampAlignment.set_Horizontal`/`set_Vertical`
  are only called from `<TakeOffCarPart>`, so the window writes the struct directly (*uncertain*). Hook `HideAction` as in row 4.
- OBD: `ObdScanner._UseAnim_d__2.MoveNext` `0x180A6F860`: for each `CarHelper.GetPartsToExamine` part,
  `if (!IsExamined) AddPlayerExp(1)` (real), then **`PartScript.Examine(true)`** (real). It is covered by row 1's `Examine`
  hook. The XP stays local to the actor. Nothing for row 5b.

## Dyno (trigger only)

- `CarLoader.MeasurePower()` `0x1804D4EE0` has one caller: `MapWindow.MeasurePowerForSelectedCarLoader` (map "measure
  power", `AddPlayerMoney(-500)` real, then `MeasurePower`).
  - It is pure data: `EngineData = GetCurrentEngineData(…)` (including `measured`) and
    `MeasuredDragIndex = CalcPerformanceIndex(...)`.
  - It has no money, sound or UI, so it is also usable as a remote primitive on an identical car.
- The garage dyno run (`GameScript.<ClickIO>g__RunDyno|72_1` → `DynoManager.RunDyno` → `<StartDyno>d__32`, which sets
  `DynoManager.DynoMeasured = true`) **does not call `MeasurePower`**.
  - The `MeasurePower` postfix therefore covers only the map path.
  - Where the garage run stores results on the car is open for row 13. `CloseDyno` fires from `DynoWindow.HideAction`,
    `PlaceAtPosition` and `ChangeCarPos`.

## Remote-apply primitives (no side effects)

| State | Primitive | Notes |
|---|---|---|
| `CarPart` condition | `CarLoader.SetCondition(CarPart, float)` `0x1804F2720` + `UpdateCarBodyPart(CarPart)` | as in `native-decompile.md` d. Row 1's body apply already does this |
| `CarPart` dent | `CarLoader.SetDent(CarPart, float)` `0x1804F95F0` | used by the welder and `FixDent` |
| wash factor | `CarLoader.SetWashFactor(CarPart part, float)` `0x1804F8450` | `part == null` → every mounted part. Field + `CarHelper.SetWashFactor` on renderers only |
| dust | `CarLoader.EnableDust(CarPart part, float)` `0x1804F7B30` | `null` → all parts. Field `Dust` + `CarHelper.SetDustValue` |
| oil | `FluidsData.SetLevel` / `SetCondition(v, CarFluidType.EngineOil, 0)` | pure data |
| paint | `CarLoader.SetCarColor`/`SetCarColorAndPaintType`, `SetCarPaintType`, `SetCustomCarPaintType`, `SetCarLivery` | do **not** use `PaintshopManager.UpdateColor`/`SetLivery`: they also touch the local paint-shop state (`LiveriesManager.RemoveAllUnusedLiveries`) |
| dyno | copy `EngineData`/`MeasuredDragIndex`, or call `CarLoader.MeasurePower()` | no fee inside |
| effect | `SoundManager.PlaySFX(tool.sfx, carPos)` + `GarageTool.particles.Play()` / `PaintshopManager.particleSystem.Play()` | *uncertain:* the shape module still points at the last car's mesh (set in `StartAnim`/`d__81`). It may need repositioning or `shapeType` reset |

Never on a remote client: `DoWorkAnim`, `StartAnim` (`EnableIO(false)`, lift buttons, `CloseCar`, `GameMode`),
`Tween*` (LeanTween on `root` blocks the next tween), `UseOilbin`, `TryGetMoneyForPaint`, `SubmitColor`.

## Recommended hooks

| Tool | Action event (`ToolAction`) | Result commit |
|---|---|---|
| Welder | prefix `WelderLogic.DoWorkAnim(CarLoader)` (fires via vtable; only caller is the PDM_0 lambda, after payment) | postfix `WelderLogic._DoWorkAnim_d__1.MoveNext`, `__result == false` → `CarPartsSync.MarkDirty(loaderId, body)` |
| Car wash | prefix `CarWashLogic.DoWorkAnim(CarLoader)` | postfix `CarWashLogic._DoWorkAnim_d__1.MoveNext` false → `CarDetailsSync.MarkDirty(BodyCosmetics)` + `FlushNow` |
| Interior | prefix `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)` (both variants). Stationary marker: prefix `ToolsMoveManager.UseInteriorDetailingToolkitStationary` | postfix `InteriorDetailingToolkitLogic._DoWorkAnim_d__1.MoveNext` false → `CarPartsSync.MarkDirty` (Condition/Dent) + `CarDetailsSync.MarkDirty(BodyCosmetics)` (Dust) |
| Oil bin | prefix `CarLoader.UseOilbin()` (fires). It may still do nothing, so send the action only when the oil level > 0 and the plug is active, or send it from the result | postfix `ToolsManager._UseOilDrain_d__40.MoveNext` false → `CarDetailsSync.FlushNow` (the Fluids poll would catch it anyway) |
| Paint shop | postfix `PaintshopManager.SubmitColor(bool)` with `PaintshopType == Garage` (fires) | same postfix → `MarkDirty(Paint \| BodyCosmetics \| BonusParts)` + `FlushNow` |
| Paint/tint clear-car | — | postfix `PaintshopWindow._ShowCoroutine_d__10.MoveNext` / `TintingWindow._ShowCoroutine_d__18.MoveNext` with `clearCar` → `MarkDirty(BodyCosmetics)` (row 4 or here) |
| Dyno | — | postfix `CarLoader.MeasurePower()` (map path only, see above) |

Do not hook these (they never fire, or they are shared): `PaintshopManager.MakeCarPaintEffects`/`MakePartPaintEffects`,
`WelderLogic.FinishAnim`, `InteriorDetailingToolkitLogic.FinishAnim`, `GarageTool.FinishAnim`, `ToolsManager.UseOilDrain`,
`CarLoader.UseWelder`/`UseInteriorDetailingToolkit` (dead), `TintingWindow.TryGetMoneyForTint` (dead), `WelderLogic.Use()`/
`InteriorDetailingToolkitLogic.Use()` (shared stub `0x180336E30`).

Generated names, checked with `dnfile` against `MelonLoader\Managed\Assembly-CSharp-firstpass.dll`:
- the ask lambdas: `WelderLogic.__c__DisplayClass5_0`, `CarWashLogic.__c__DisplayClass3_0` and
  `InteriorDetailingToolkitLogic.__c__DisplayClass6_0`, each with `.Method_Internal_Void_Boolean_PDM_0`;
- the clear-car answers: `PaintshopWindow.Method_Private_Void_Boolean_PDM_0` and `TintingWindow.Method_Private_Void_Boolean_PDM_0`;
- the state machines: `_DoWorkAnim_d__1`, `_UseOilDrain_d__40`, `_MakePaintEffects_d__80` and `_PaintCar_d__14/28/11`.

## Remaining runtime checks

1. Harmony postfixes on the nested `_DoWorkAnim_d__1.MoveNext` / `_UseOilDrain_d__40.MoveNext` fire under MelonLoader 0.5.7
   (not yet traced in `engine-crane.md` either). Fallback: a `MelonCoroutines` watcher that waits for
   `GarageTool.interactiveObject.enabled` to become true again (it is set at the end of `FinishAnim`).
2. When `d__1` returns false, check that the LeanTween `onComplete` has already written the final value (`effectTime` vs particle
   lifetime). If not, delay the `MarkDirty` by one `effectTime`.
3. The `isTweening(root)` guard: with two tools started back to back, the second changes nothing but still charges.
4. Paint shop: confirm that the car state at the `SubmitColor` postfix equals the state after closing the window, and that
   the preview does not leak through row 4's poll.
5. Interior detailing is free with the `car_wash` upgrade (field +0x20 of the upgrade entry).
6. Which materials the car wash's `b__4/b__5` dust tween affects, and whether `details*` dust also changes `CarPart.Dust`
   for exterior parts.
7. Remote effect: particles and SFX at the car without `StartAnim`; the shape module may point at a stale mesh.
8. Garage dyno run: where the measured values land on the car (row 13).
