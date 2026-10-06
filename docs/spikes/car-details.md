# Spike: car details (row 4 `sync-car-details`)

Goal: answer statically where each car detail lives, which game methods change it on the player paths, which of
them are hookable, and which setters apply a remote value without sound, inventory, money, XP or saves.
Method: static decompile only (setup in `native-decompile.md`). The game was not launched. Date: 2026-10-06.

Outputs are in `%USERPROFILE%\CMS21-TestInstalls\native\out\cardetails_clean` and `cardetails2_clean` (targets in
`work\targets\cardetails*.txt`). Older decompiles that were reused: `out\clean` (setters, `ResizeWheel`) and
`out\placement_clean` (`LoadCarFromFile d__425`, `SaveCarToFile`, `NewCarData`). Two helpers were added to `work\`:

- `dasm.py <va> <hexsize>`: capstone disassembly with call/jmp targets named. Run it with `uv run --with capstone python dasm.py …`.
- `fieldw.py <hexdisp32> [regex]`: lists the functions that store a byte to `[reg+disp32]`.
- `at.py <va>…`: lists every method folded onto a native address.

"Folded" means that several methods share one native body (identical-code folding). A Harmony patch on one of them
fires for all of them.

## Short answers

1. **Design risks resolved:** `FluidsData.SetLevel/SetCondition(…, id)` take the **list index**. `FluidsData.Add` sets
   `CarFluid.ID = list.Count`, so the index and `CarFluid.ID` are the same number. `SetWheelSize(w, rim, tire, type)` writes
   `Wheel.Width = w`, `Size = rim` and `Profile = tire` as plain int→float casts, so the mapping is 1:1.
2. **Three design hooks never fire as intended:**
   - `TintingWindow`, `WheelsAlignmentWindow` and `LampAlignmentWindow.HideAction` are **folded** at `0x1804BEED0`
     with 39 `HideAction`s. The shared body is only `this.Hide(false)` through the vtable (`+0x198`).
   - `CarLoader.SwitchCarLights` is **check-only**: it never changes the lights. No store to `LightsOn` exists anywhere
     in the binary.
   - `CarLoader.SwapBonusPart` has **no callers**. Players fit and remove bonus parts through `CarLoader.TakeOffBonusPart`.
3. **The paint shop and the tint window restore their previews in `HideCoroutine`, after `Hide()` returns.** A
   postfix on `Hide`/`HideAction` would therefore read preview values. The commit points are
   `PaintshopManager.SubmitColor` and `TintingWindow.TintAction`.
4. **Alignment also changes outside its windows**: `PartScript.CheckMessageOnHide` calls `WheelsAlignment.SetRandom`,
   and the headlight mount sets `HeadlampAlignment`. It has no preview state, so it should be polled like
   Fluids, Wheels and Info.
5. **Do not use `TakeOffBonusPart` to apply a remote change.** It calls `Inventory.Add` (unmount) or needs
   `GameScript`'s selected item (mount). Use the `BonusPart` methods instead (section 9).
6. **`FluidsData` is a struct.** So are `LicensePlatesData`, `BonusPartsData` and `NewCarData`, and they hold references.
   See "Harmony and Unhollower flags".

## Where the state lives (`CarLoader`, offsets from `dump.cs`)

| Detail | Field | Type |
|---|---|---|
| Fluids | `FluidsData` `+0xA8` | **struct** `{FluidData Oil; List<FluidData> Brake, EngineCoolant, PowerSteering, WindscreenWash}`. `FluidData` is a **class** `{float Level, Condition; CarFluid CarFluid}`, and `CarFluid` is the reservoir `MonoBehaviour` (`FluidType`, `ID`, `fluidMaterial`). Oil has no `CarFluid` |
| Tool state | `CurrentUsedFluid` `+0xD4`, `CurrentUsedFluidId` `+0xD0` | the extractor reads these |
| Wheels | `WheelsData` `+0x5D0` | `CarWheelsData{Wheel[] Wheels, OriginalWheels; WheelColorData[] WheelsColors}`, `Wheel` = class `{string Tire, Rim; float Width, Size, Profile; int ET}`. Also `rimOneSize` `+0x600` |
| Alignment | `WheelsAlignment` `+0x158`, `HeadlampLeftAlignment` `+0x148`, `HeadlampRightAlignment` `+0x150` | blittable structs (private floats `fl/fr/rl/rr`, `horizontal/vertical`) |
| Info | `CarInfoData` `+0xD8` | blittable `{int BuyPrice, Mileage; CarFrom CarFrom}` |
| Lights | `LightsOn` `+0x650` | bool. No writer found (see 8) |
| Car paint | `color` `+0x1A0`, `factoryColor` `+0x18C`, `factoryPaintType` `+0x19C`, `paintData` `+0x1C0`, `IsCustomPaintType` `+0x1D4` | |
| Body cosmetics | `carParts` `+0x1E8`, `List<CarPart>` | `CarPart`: `IsTinted` 0x38, `TintColor` 0x3C (a = opacity/255), `Color` 0x4C, `PaintType` 0x5C, `PaintData` 0x60, `Livery` 0x80, `LiveryStrength` 0x88, `Dent` 0x90, `Dust` 0xAC, `WashFactor` 0xB0 |
| Bonus parts | `bonusParts` `+0x1F0`, `List<BonusPart>` (class) | `BonusPart{ID, IsUnmounted, IsPainted, CustomColor Color, PaintType, PaintData, Handle, InteractiveObject, …}`. The design's `BonusPartsData` is only the save form. `CarBonusPartsData` `+0x218` is a config id list |
| Plates | `LicensePlatesData` `+0x50` | **struct** of 5 strings (`…NumberFront/Rear`, `FactoryLicensePlateNumber`, `…FrontTex/RearTex`) |
| Gearbox | `GearboxHandle` component under `root` | `{float[] gearRatio; float finalDriveRatio}` |
| ECU/carb | `PartModule` components (`EcuModule`, `CarbModule`) | `{PartScript partScript; TuningData data}`, where `TuningData` is a struct `{bool IsTuned; short[] Values; float TuningValue}`. `EcuModule.stage` `+0x38` |

The gearbox and ECU details live on components. `LoadCarFromFile d__425` applies them like this:

```c
gh = root.GetComponentInChildren<GearboxHandle>(); gh.gearRatio = carData.gearRatio; gh.finalDriveRatio = carData.finalDriveRatio;
ecu = root.GetComponentInChildren<EcuModule>(); ecu.CopyDataFrom(ref carData.ecuData);
```

Carb data is not stored on `NewCarData`. It goes per part through `PartData` (`PartData..ctor` calls `PartModule.CopyDataTo`).

## Player paths: what changes what

### 1. Fluids

| Path | Chain | Writes | Side effects |
|---|---|---|---|
| Refill can | `FluidRefill.Use` → `FluidRefillLogic.Update` (**every frame** while the button is held) | below level 0.05: `SetCondition(1)`. While level < 0.65: `FluidsData.AddFluid(dt*0.1, dt*0.05, type, id)` | particles and sound. **Money** in `FluidRefill.Hide` `0x1812887E0`: `AddPlayerMoney(-(Δlevel*20))` |
| Extractor (drain tool) | `FluidExtractor.<UseAnim>d__5` | a 2 s LeanTween writes `FluidsData.SetLevel` per frame (lambda `<>c__DisplayClass5_0.<UseAnim>b__0`), then `SetLevelAndCondition(0, 0, CurrentUsedFluid, CurrentUsedFluidId)` | `SoundManager "DrainTool"`, `ToolsManager` state, `GameMode 0x17` |
| Oil drain / oil bin | `ToolsMoveManager.Use` → `CarLoader.UseOilbin` `0x18050B450` → `ToolsManager.<UseOilDrain>d__40` | waits `oilLevel*5` s, then oil `SetCondition(0)` and `SetLevel(0)` | hides the `korek_spustowy_1` plug and shows `Oil_drain_h`, loop SFX, `EnableIO(false/true)` |
| Part unmount | `PartScript.CheckMessageOnHide` / `HideBySavegame` | `SetLevelAndCondition(OnAll)` drains | spill money (see `native-decompile.md` d) |
| Spawn / job | `PrepareJob`, `TakeJob`, `SetRandomLevel/Condition` | | |

`FluidsData.SetLevel` `0x18128B6F0` clamps to 0..1. For oil it writes `Oil.Level` directly; for the other fluids it calls
`FluidData.SetLevel` → `CarFluid.SetLevel` (the reservoir material's `LiquidLevel`, which shows 0 while the drain part is
unmounted). There is no sound, money or inventory in these setters.

### 2. Wheels (tires, rims, sizes, ET)

- The tire changer (`TireChangerLogic.SetGroupOnTireChanger`, `<Clear>d__23`) **does not touch `CarLoader`**. It only
  changes the inventory (`Inventory.Add`/`AddGroup` in `Clear`).
- The sizes change when a tire, rim or wheel group is fitted to the car:
  `PartScript.<DoMount>d__151` / `NotificationCenter.MountGroup` → `PartScript.TunePart(id, shouldResizeWheel)` →
  `PartScript.ResizeWheel()` `0x180FFB9F0`. That method:
  - takes the car from **`GameScript.GetIOMouseOverCarLoader2()`** (the car under the mouse);
  - **writes `Wheels[i].ET` inline** (the store at `+0x2C`);
  - calls `CarLoader.SetWheelSize(w, rim, tire, i)`, then `PlaceAtPosition(1,1)` when the car is not on a lifter, then
    `UpdateWheelMeshCollider(i)`.
- `CarLoader.SetET` `0x1804CC490` has **no callers**, so it cannot be hooked. `SetWheelSize` `0x1804EE730` is called
  for real (by `ResizeWheel` ×2 and `LoadCarFromFile`).
- `SetTire`/`SetRim` (the `Wheel.Tire/Rim` strings) are called only by the salon and debug code. **In the garage
  `Wheel.Tire/Rim` are never updated**, so they are useful only as diagnostics, as the design already says.

`SetWheelSize` returns early when `isFatalException` or `rimOneSize` is set. Otherwise it writes the three sizes, scales the wheel
handle `x` to `Width/255/Scale*SidesFlip`, and calls `DoWheelMath`, `CarHelper.SetET(transform, Wheels[i].ET)`,
`WheelBolts.RepositionDepth` and `CapSize.Resize`. It only sets data and transforms.

### 3. Alignment

- Wheels: `WheelsAlignmentWindow.ApplyCurrentMeasure` and `UpdateCarWheelAlignment` write `WheelsAlignment.fl..rr` straight from
  the measures. They are called from `OnButtonEvent`, `OnMeasureClick`, `ApplyValueAction` and **`Hide(bool)`**. There is no backup and no restore, so every write is a commit.
- Outside the window: `PartScript.CheckMessageOnHide` (unmounting a suspension part) calls `WheelsAlignment.SetRandom`,
  and so do spawn and job prep.
- Headlamps: they are written while the beam moves (the exact store was not located; uncertain). The window's `Hide` does not restore
  them. `CarLoader.<TakeOffCarPart>d__382` (body mount of a headlight) calls `HeadlampAlignment.set_Horizontal/Vertical`.

### 4. Paint (paint shop, `PaintshopType` 0 = Garage car, 1 = GaragePart item)

| Step | Method | Notes |
|---|---|---|
| preview | `PaintshopManager.UpdateColor(Color32, bool)`, `SetPaintType`, `SetCustomPaintType`, `SetLivery` | They change the live car through `CarLoader.SetCarColor(null, c)` and the other setters, and set `colorChangedButNotSubmitted` |
| commit | the `<PaintCar>` coroutines of `PaintshopTab` (d__14), `BasicTab` (d__28) and `AllowedColorsTab` (d__11) | `TryGetMoneyForPaint` (**money**) → **`PaintshopManager.SubmitColor(bool)` `0x1809F4E20`** → `MakePaintEffects` (sound, particles; `isPainting` only guards the effect) → `stat_paint_car` |
| `SubmitColor` | | calls `BackupColor` (the backup becomes the committed state: `carParts`, `bonusParts`, colour, livery, paint type), adds the colour to the profile's last colours, and clears `colorChangedButNotSubmitted` |
| cancel | `PaintshopWindow.<HideCoroutine>d__11` → `RestoreColor` (also the tab `RestoreColor` actions) | runs after the fade, **after `Hide()` returns** |

All `CarLoader` paint setters only change data and materials (`PaintHelper.*`):

- `SetCarColor` `0x1804FA200`
- `SetCarColorAndPaintType` `0x1804C98F0` (private)
- `SetCarPaintType` `0x1804C9270`
- `SetCustomCarPaintType(PaintData)` `0x1804C9EF0`, which sets the car's `paintData`, `IsCustomPaintType = true` and repaints the parts
- `SetCustomCarPaintType(CarPart, PaintData)` `0x1804CA6B0`
- `SetCarLivery` `0x1804FAEA0`
- `SetFactoryColor` `0x1804C8800`
- `SetFactoryPaintType` `0x1804C9080`

`SetCarColor(null, c)` sets the car's `color` and repaints **every** part (and the bonus parts) in `c`. It overwrites per-part colours.

### 5. Tint (`TintingWindow`)

- Preview: `WindowTintManager.UpdateWindow(i, color, opacity)` → `PaintHelper.SetWindowProperties` on the glass, using its own
  `tintStates`.
- Commit: **`TintingWindow.TintAction` `0x1810C9530`**:
  - money check: `GUI_BrakKasy` when `PlayerMoney < changed*50`, otherwise `AddPlayerMoney(-50*changed)`;
  - for each changed `tintStates` entry: `CarPart.IsTinted` and `TintColor` (alpha = opacity/255);
  - then the profile's last colours and `MakeBackup()`.

  `WindowTintManager.Tint()` exists, but `TintAction` does not call it. The window inlines the same work.
- Cancel: `<HideCoroutine>d__19` → `RestoreBackup`, also after `Hide()` returns.
- `TintingWindow.RestoreColorAction` and `RestoreBackupForSelected` are folded (`0x1810C9B30`).

### 6. Dirt and wash

| Path | Chain |
|---|---|
| Car wash | `CarWashLogic.Use` → ask window → `<Use>g__UseCarWashAction` (money) → `<DoWorkAnim>d__1` → `CarLoader.TweenExteriorDustWash` `0x1804F54F0` (LeanTween, writes per frame) |
| "Wash before painting/tinting?" | `PaintshopWindow`/`TintingWindow.Show` → ask → `<Show>g__ClearCarAction` (**money**, `GUI_BrakKasy`) → `ShowCoroutine(clearCar)` → `EnableDust(null, 0)` and `SetWashFactor(null, 1)` on every part (also `PaintshopManager.ClearCar` and `WindowTintManager.ClearCar`). **This path is not in the design** |
| Interior detailing | `InteriorDetailingToolkitLogic.<DoWorkAnim>d__1` → `TweenInteriorConditionAndDust` |
| Body mount | `<TakeOffCarPart>d__382` applies the item's wash, dust and dent to the `CarPart` |
| Test drive | `PrepareCarPhysics.CalcDistance` → `AddWashFactorValue` (other scene) |

`EnableDust(CarPart|null, float)` `0x1804F7B30` and `SetWashFactor(CarPart|null, float)` `0x1804F8450` write `Dust`/`WashFactor` and
call `CarHelper.SetDustValue`/`SetWashFactor` on the renderers. A `null` part means all parts. They have no other side effects.

### 7. License plates

There is **no UI for typing a plate**. Plates change only when a license-plate item is fitted
(`<TakeOffCarPart>d__382`):

```c
ChangeLicencePlateTexture(part, item.<+0x68>);              // (CarPart, string) 0x1804FCAD0
SetNewLicensePlateNumber(item.<+0x70>, part.name == "license_plate_front");   // 0x1804C86E0
SetLicensePlateNumber();
```

- `SetNewLicensePlateNumber` writes the front or rear number. An empty number falls back to the factory number. When
  either number is still empty, it rolls `CarHelper.GetRandomLicensePlate()` for all three. It then sets the plate `Text`s.
- `ChangeLicencePlateTexture` only sets the material texture and the text colour. It **does not write
  `LicensePlatesData.*Tex`**. Only `LoadCarFromFile` writes those fields, and `SaveCarToFile` saves the field as it is. So
  `*Tex` may be stale after a plate swap (uncertain). Read the plate's texture from the mounted plate part instead (probe).
- The `Nullable<LicensePlate>` overload `0x1804FCF90` is used by the spawn generators.

### 8. Headlights (`LightsOn`)

- `CarLoader.SwitchCarLights(bool on, out reason, bool instant, bool isTrack)` `0x1804D2AC0`. Checked in capstone: it
  checks the `ItemsNeededForElectronic` parts and the two headlights, sets `reason`, and returns a bool. It **never
  reads `on` and never writes**.
- Its callers are `LampAlignmentWindow.CanStartAlignment` and `Hide`, plus the car editor.
- `fieldw.py 0x650` finds **no byte store to `CarLoader+0x650`** in the whole binary. `NewCarData.LightsOn` is only
  (de)serialized, and `LoadCarFromFile` does not apply it.
- So `LightsOn` looks like dead state in this build. Syncing it gains nothing, and applying it through `SwitchCarLights`
  does nothing. *Uncertain*: a wider store (dword/struct copy) could still reach it. Confirm with the probe.

### 9. Bonus (visual tuning) parts

Both directions start in `GameScript.BodyMount` `0x180E8F150` (or `ClickIO`) → `CarLoader.TakeOffBonusPart(io, false)` `0x1804E88E0`:

- **Fitted → remove:** `new Item(ID)` with the colour and paint → **`Inventory.Add`** (a real call) →
  `BonusPartsManager.TryDeleteBonusPart` (unloads the part asset) → `BonusPart.TakeOff` → `Change("_BonusDummy")`.
- **Empty → fit:** reads `GameScript.SelectedToMount` → `BonusPart.Change(item.ID)` → `Paint(isPainted, color, paintData,
  paintType)` → `TakeOn(instant)`. `BodyMount` then removes the item with an **inlined `List<Item>.RemoveAt`**, so the
  `Inventory.Delete` hook does not see it. `BodyMount` also plays `PartTakeOff`.

`CarLoader.SwapBonusPart` is a 4-instruction wrapper that `jmp`s to `BonusPart.Change`, and nothing calls it. The paint shop
paints bonus parts through `SetCarColor` and the other setters, and `BackupColor` copies `bonusParts`.

### 10. Tuning (not a part swap)

- **Gearbox:** `GearboxTab.ApplyAction` `0x180D73180`. It returns unless the gearbox `PartScript.IsTuned()` and the part is
  mounted. It then writes `gearboxHandle.finalDriveRatio` and a **new** `float[]` `gearRatio`.
  `ResetToDefaultAction` only resets the UI sliders, so `ApplyAction` is the only commit.
- **ECU/carb:** `EcuTuning.ApplyAction` `0x181019D30` and `CarbTuning.ApplyAction` `0x180AF2430` copy the bars into
  `values`, then **tail-jump** to `PartModule.Tune(short[] values, float tuningValue)` `0x180FF1790` (capstone:
  `jmp 0x180ff1790`). `Tune` copies `Values`, sets `TuningValue`, `IsTuned = true` and `partScript.tuningBonus`.
- **ECU stage:** `EcuModule.SetStage` `0x180477B50` is **folded with 10 unrelated byte setters**, and the game never calls it.
  The stage likely follows the mounted ECU part (part swap → `sync-car-parts`). Uncertain.

### 11. Mileage and info

In the garage, `CarInfoData` is set only at spawn (`SetRandomMileage(SceneType|AuctionType)`, the generators) and by
`LoadCarFromFile`. The test drive counts `PrepareCarPhysics.mileage` in `CalcDistance`, and `SaveMileage` writes
`GlobalData.NewMileage` and the profile's car record. The garage sees the new mileage only when the car is loaded again. How
`NewMileage` reaches `CarInfoData` was not traced. The 1 Hz poll catches the result.

## Side-effect-free apply (remote values)

| Section | Use | Avoid |
|---|---|---|
| Fluids | `cl.FluidsData.SetLevelAndCondition(level, cond, type, listIndex)` `0x18128B0D0`. It works through Unhollower's boxed copy because `Oil` and the lists are references (verify). Or set the `FluidData` objects directly with `FluidData.SetLevel/SetCondition` (these update the `CarFluid` material) | `AddFluid`, the tool coroutines, `FluidRefill.Hide` (money) |
| Wheels | `cl.SetET(type, et)`, then `cl.SetWheelSize(w, rim, tire, type)` (it reads `Wheel.ET`), then `UpdateWheelMeshCollider(type)`. For tire mesh shape, run `PartScript.ResizeWheel(CarLoader, Wheel)` `0x181007000` on the mounted tire/rim parts (explicit car; it is what `UpdateWheels` uses) | parameterless `PartScript.ResizeWheel()` and `TunePart(…, true)`: both use the **mouse-over car**. `PlaceAtPosition` (ride height) belongs to `sync-car-placement-and-lifts`, so check whether it is needed |
| Alignment | copy, change and assign `WheelsAlignment`, `HeadlampLeft/RightAlignment` (blittable) | |
| Tuning | `GearboxHandle.gearRatio`/`finalDriveRatio` (`root.GetComponentInChildren<GearboxHandle>()`, as the load does); `PartModule.Tune(values, tuningValue)`, or `CopyDataFrom(ref TuningData)` `0x180FF19C0` to also clear `IsTuned` | |
| Paint (car) | `SetFactoryColor`, `SetFactoryPaintType`, the `color` field, `SetCustomCarPaintType(PaintData)` only when custom (it repaints all parts) | `SetCarColor(null, …)` before the per-part apply would overwrite the parts |
| Body cosmetics | per `CarPart`, in the order the load uses: `SetCarColor(part, part.Color)`, `SetCarPaintType(part, t)` / `SetCustomCarPaintType(part, d)`, `SetCarLivery(part, name, strength)`, tint `part.SetColorAndOpacity(c, opacity, tinted)` + `PaintHelper.SetWindowProperties(part.handle, part.TintColor)`, `EnableDust(part, d)`, `SetWashFactor(part, w)` | `TweenExteriorDustWash`, `TweenInteriorConditionAndDust` (tweens), `ClearCar` |
| Plates | `SetNewLicensePlateNumber(n, isFront)`, then `ChangeLicencePlateTexture(platePart, texName)` | a whole-struct `LicensePlatesData` write (see flags) |
| Bonus parts | fit: `bp.Change(id, false)`, `bp.Paint(isPainted, color, paintData, paintType)`, `bp.TakeOn(true)`; remove: `bp.TakeOff(true)` | **`TakeOffBonusPart`** (`Inventory.Add`, `GameScript` selected item, asset unload) |
| Info | copy, change and assign `CarInfoData` (blittable) | `SetRandomMileage` |

`LoadCarFromFile d__425` is the reference recipe for applying a whole car. In order, it:

1. calls `CarPart.Clone(BodyPartData)`, `SetCarColor(part)`, `SetWindowProperties` when tinted, `SetCarLivery` when a livery is set,
   `EnableDust` when > 0, `SetWashFactor` when < 1 and `SetDent`;
2. calls `SetWheelSize` ×4 and `UpdateET`;
3. calls `FluidsData.Copy`;
4. writes the alignment and info fields directly;
5. calls `BonusPart.Change` + `TakeOn` + `Paint`;
6. writes the gearbox handle and the ECU `CopyDataFrom`.

## What the save carries (`NewCarData`)

`NewCarData` holds:

- colour: `color`, `factoryColor`, `factoryPaintType`, `PaintData`, `HasCustomPaintType`;
- `FluidsData`;
- `BodyPartsData`: per part `Switched`, `Condition`, `Unmounted`, `TunedID`, `IsTinted`, `TintColor`, `Color`, `PaintType`, `PaintData`,
  `Livery(+Strength)`, `OutsaidRustEnabled`, `Dent`, `Quality`, `Dust`, `WashFactor`;
- `PartData`: per `PartScript`, including `PartModule` tuning, so the carb data is here;
- wheels: `tiresET`, `wheelsWidth`, `rimsSize`, `tiresSize` (`int[]`);
- `LicensePlatesData`;
- `EngineData` (dyno), `gearRatio`, `finalDriveRatio`, `ecuData`;
- `CarInfoData`, `HeadlampLeft/RightAlignmentData`, `WheelsAlignment`, `LightsOn` (not applied on load);
- `BonusPartsData` (IDs, IsPainted, Color, PaintType, PaintData, IdFromConfig);
- `engineSwap`, `ToolsData`, `AdditionalCarRot`, `measuredDragIndex`, `customerCar`, `orderConnection`.

It does not carry the `Wheel.Tire/Rim` strings (the parts determine them).

## Harmony and Unhollower flags

- **Folded native bodies, never patch:**
  - `0x1804BEED0`: 39 `HideAction`s, including `TintingWindow`, `WheelsAlignmentWindow` and `LampAlignmentWindow`;
  - `0x180477B50`: `EcuModule.SetStage`;
  - `0x1810C9B30`: `TintingWindow.RestoreColorAction` and `RestoreBackupForSelected`.

  Patch each window's own `Hide(bool)` override instead (unique VAs below). Before patching any small method, check it with
  `work\at.py <va>…` (lists every method name at a native address).
- **Value-type `this`:** `FluidsData.*` are instance methods of a struct. A Harmony patch gets a boxed copy as `__instance`,
  and 0.4.x hooked `SetLevel` per frame. Do not patch `FluidsData`.
- **Non-blittable struct fields** (`FluidsData`, `LicensePlatesData`, `EngineParams`, `NewCarData`):
  - Unhollower's getter returns a boxed copy, and its setter copies the raw bytes. The game writes reference fields with GC card-marking barriers when incremental
    GC is active (the flag at `0x182862620` appears in every decompile), and a raw copy skips them.
  - Assigning such a struct back from managed code (`cl.LicensePlatesData = copy`; also the `EngineParams` write-back suggested in
    `engine-crane.md`) **may skip the write barrier**. Prefer game setters or writes to referenced objects. *Uncertain*: verify that
    the strings survive a GC after a write-back.
- **By-value or ref struct parameters.** These are fine to *call* (verify), but none of the recommended hooks uses them. Do not *patch*:
  - `PaintshopManager.UpdateColor(Color32, bool)`, a 4-byte struct passed in a register;
  - `CarLoader.SetCarColor(CarPart, Color)`, `SetCarColorAndPaintType(…, Color, PaintType)` and `SetCustomCarPaintType(PaintData)`;
  - `CarPart.SetColorAndOpacity(Color, int, bool)`;
  - `BonusPart.Paint(bool, CustomColor, PaintData, PaintType)` (`CustomColor` holds a `float[]`, so it is non-blittable);
  - `ChangeLicencePlateTexture(CarPart, Nullable<LicensePlate>)` (a generic non-blittable struct);
  - `PartModule.CopyDataFrom(ref TuningData)` (ref non-blittable).
- **Coroutine bodies.** Generated names in `Assembly-CSharp-firstpass.dll` include `_PaintCar_d__14`, `_MakePaintEffects_d__80`, `_UseOilDrain_d__40`
  and `_UseAnim_d__5`. Whether patches on these `MoveNext` methods fire is still unverified (as in the crane spike); the
  recommendations below do not need them.

## Recommended hooks (all unique VAs, no struct parameters)

| Section | Hook | Car from |
|---|---|---|
| Paint + BodyCosmetics + BonusParts | postfix `PaintshopManager.SubmitColor(bool)` `0x1809F4E20`, only when `paintshopType == 0` | `PaintshopManager.carLoader` `+0x18` |
| BodyCosmetics (tint) | postfix `TintingWindow.TintAction` `0x1810C9530`, all windows (it returns early without money). **Not `Hide`/`HideAction`** | `tintManager.carLoader` |
| BodyCosmetics (wash prompt) | postfix `CarLoader.EnableDust` `0x1804F7B30` / `SetWashFactor` `0x1804F8450` when `part == null` (the paint shop or tint "wash first" prompt). The car wash and detailing keep D6's `MarkDirty` | `__instance` |
| Tuning (gearbox) | postfix `GearboxTab.ApplyAction` `0x180D73180` | `GearboxTab.carLoader` `+0xA0` |
| Tuning (ECU/carb) | postfix `PartModule.Tune(short[], float)` `0x180FF1790`. Its only callers are the two `ApplyAction` tail jumps. Or postfix both `ApplyAction`s | `partScript` → its `CarLoader` (`EcuTuning.carLoader` `+0x48`, `CarbTuning.carLoader` `+0x40`) |
| Alignment | **1 Hz poll** (the window writes commit directly, and `CheckMessageOnHide`/the headlight mount change it too). Optional quicker flush: postfix `WheelsAlignmentWindow.Hide(bool)` `0x180932DF0` (it calls `ApplyCurrentMeasure` synchronously) and `LampAlignmentWindow.Hide(bool)` `0x1813E3910` | window `carLoader` (`+0x58` / `+0x50`) |
| Plates | postfix `SetNewLicensePlateNumber(string, bool)` `0x1804C86E0` and `ChangeLicencePlateTexture(CarPart, string)` `0x1804FCAD0`. Both fire inside the body mount, which `sync-car-parts` also commits | `__instance` |
| BonusParts | postfix `CarLoader.TakeOffBonusPart(InteractiveObject, bool)` `0x1804E88E0` (fit and remove). Drop the `SwapBonusPart` hook | `__instance` |
| Fluids, Wheels, Info | 1 Hz poll (as designed). The pour path is per-frame `AddFluid`, and `ResizeWheel`'s inline ET write cannot be hooked | |
| Lights | drop the `SwitchCarLights` postfix and apply; keep `LightsOn` read-only or remove it from Info | |

## Remaining runtime checks (probe 1.2)

1. Fluids: check that `cl.FluidsData.SetLevelAndCondition(…)` on the Unhollower copy changes the live values. Confirm that the list index equals `CarFluid.ID`
   on a car with two brake reservoirs.
2. Wheels: compare `Wheel` before and after `SetWheelSize`. Check whether the tire mesh also needs `PartScript.ResizeWheel(cl, wheel)` on the
   mounted tire/rim, and whether skipping `PlaceAtPosition` leaves the car at the wrong height.
3. Lights: toggle the lights in the garage UI and watch `LightsOn` and the lights. Expect no change; if confirmed, drop the field.
4. Tint: check the round trip of `SetColorAndOpacity` + `SetWindowProperties`. Find which call clears the glass for an untinted window.
5. Plates: after fitting a plate, compare `LicensePlatesData.*Tex` with the plate part's `TunedID`/`AdditionalString`, and find where
   the texture name can be read back.
6. Check that the `TintingWindow.TintAction`, `PaintshopManager.SubmitColor` and `PartModule.Tune` postfixes fire once per commit, and that cancel
   (`HideCoroutine` restore) sends nothing.
7. Check that writing back a non-blittable struct (`LicensePlatesData`) keeps its strings after `GC.Collect`, or avoid such writes.
8. ECU stage: what sets `EcuModule.stage` (a store at `+0x38`, which `fieldw.py` does not cover because it is a disp8)? Is it the ECU part type?
9. Mileage: after a test drive and the return to the garage, does `CarInfoData.Mileage` change? Which path applies `GlobalData.NewMileage`?
