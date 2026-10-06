# Spike: car placement, lifts and parking (static part of `sync-car-placement-and-lifts` group 1)

Static decompile only (setup in `native-decompile.md`), game not launched. Date: 2026-10-06.
Answers spikes 1.1, 1.2, 1.4 and 1.5 as far as the binary allows, plus the static facts for 1.3.

Outputs: `%USERPROFILE%\CMS21-TestInstalls\native\out\placement_clean`, `placement2_clean`, `placement3_clean`
(targets `work\targets\placement*.txt`). Every "direct call", "tail jump" and "inlined" below was checked against
the raw bytes (capstone over `GameAssembly.dll`, E8/E9 rel32 scan per target), not taken from Ghidra's merged bodies.
"Hookable" means the native function entry is reached (a call, a `jmp` to its start, or a delegate), so a Harmony
detour fires.

## Design-impacting findings (read first)

1. **`NotificationCenter.ChangeCarPos` is never called by the vanilla UI.** The pie menu calls
   `NotificationCenter.ButtonAccept(NewHash{Type="MoveCar",…})`, which builds `<ChangeCarPos>d__20` inline. A prefix
   on `ChangeCarPos` does not fire. Hook `<ChangeCarPos>d__20.MoveNext` (state 0) instead (see 1.2).
2. **`ChangeCarPos(…, movePlayerToCar: false)` still fades the screen and locks input** for about 2 s. Only the
   "teleport player next to car" step depends on the flag. Remote applies should use `ResetCarLifter` +
   `ChangePosition(int)` instead.
3. **Moving onto an occupied place swaps the two cars in vanilla**; the pie menu offers it. Decision 3 ("refuse a
   move onto an occupied place") would make every vanilla swap fail while connected. This needs a decision: either a swap
   request (two places in one atomic change) or accept the refusal as a known limitation.
4. **`Method_Private_Void_Boolean_PDM_0` is not the unlock callback.** It is the local function
   `<MoveCarFromParking>g__RemoveCar|57_0(bool wasAccepted)`, the "car unavailable (missing config/DLC), remove it from
   parking?" answer, and it **deletes a parked car**. Unlocking has no confirm dialog: it is
   `UnlockParkingLevelAction()` bound to the UIEnter key. Decision 7's fallback becomes the main hook, and the
   PDM_0 delete path must be synced or blocked.
5. **`ProfileData.carsOnParking` is a `NewCarData[]` of inline structs** (0x1B8 bytes each), but Unhollower exposes it as
   `Il2CppReferenceArray<NewCarData>`, which indexes 8-byte pointers. Indexing it very likely reads garbage. Read and
   write slots only through `GameDataManager.LoadCarInParking(i)` / `SaveCarInParking(data, i)`; runtime check in 1.3.
6. **Best park hook: postfix `CarLoader.SaveCarToFile(int index, bool toParking)`** with `toParking == true`. It is
   synchronous, gives both the loader (`__instance`) and the slot, and avoids Harmony marshalling of a by-value struct
   parameter (`SaveCarInParking(NewCarData, int)`).
7. **An empty lift cannot move** (`Action` returns without a connected car), and `InstantSet(1|2)` on an empty lift
   sets `currentState` but moves nothing (state/visual mismatch).

## 1.1 Lifts (`CarLifter`, firstpass)

Fields: `connectedCarLoader` 0x60, `currentState` 0x68, `isMoving` 0x6C, `lifterBlocker` 0x30.
`CarLifterState { OnFloor=0, Middle=1, Up=2 }`.

`Action(int actionType)` `0x1804C3B60`, called directly by `GameScript.ClickIO` (`0x180E8B5AA`): the button's
`transform.parent.GetComponent<CarLifter>()`, with **0 = up** and **1 = down**. In order:

- `lifterBlocker.IsPlayerInside() && currentState == Up` → `GUI_BlockedLifter`, return;
- connected car with `ToolsData.OilbinIsConnected` → same info, return;
- **`connectedCarLoader == null` (or destroyed) → return. An empty lift never moves;**
- `isMoving` → return;
- `OnFloor` and 0 → `StartCoroutine(MoveMedFromFloor(false, true))` (call `0x1804C3F37`);
- `Middle` and 0 → `StartCoroutine(MoveUp(false, true))` (call `0x1804C3F02`);
- `Middle` and 1 → if `CheckIfHaveWheels() == MissingWheels`: `GameMode 7` plus info `GUI_MissingWheels`, no move.
  Otherwise `StartCoroutine(MoveMiddleToFloor(false, true))` (call `0x1804C3ED5`);
- `Up` and 1 → **tail jump to `MoveDown(false, true)`** (`jmp 0x1804C4210` at `0x1804C3DB9`). Ghidra shows `MoveDown`'s
  body merged into `Action`;
- every other combination (Floor+down, Up+up) does nothing.

| Transition | Started by | Type | `isMoving = true` | Completion (sets `currentState`, `isMoving = false`) |
|---|---|---|---|---|
| OnFloor→Middle | `MoveMedFromFloor` `0x1804C4010` → `<MoveMedFromFloor>d__40` | coroutine, builder is a plain allocator | state 0 of `MoveNext`, synchronous inside `StartCoroutine` | LeanTween onComplete `<>c__DisplayClass40_0.<MoveMedFromFloor>b__1` → `Middle` (0.5 s arm wait + 4 s tween) |
| Middle→Up | `MoveUp` `0x1804C40C0` → `<MoveUp>d__41` | coroutine | state 0, synchronous | `<>c__DisplayClass41_0.<MoveUp>b__1` → `Up`, invokes `OnCarLifterUp` |
| Up→Middle | `MoveDown` `0x1804C4210` | **plain void** (LeanTween) | synchronous in `MoveDown` | `<>c__DisplayClass43_0.<MoveDown>b__1` starts `<OnCompleteMoveDown>d__44` → `Middle`, invokes `OnCarLifterMid` |
| Middle→OnFloor | `MoveMiddleToFloor` `0x1804C3F60` → `<MoveMiddleToFloor>d__39` | coroutine | state 0, synchronous | `<>c__DisplayClass39_0.<MoveMiddleToFloor>b__4` → `OnFloor` |

- **`isMoving` is true before `Action` returns** for every accepted step, because the coroutines' state 0 runs inside
  `StartCoroutine`. One exception: `MoveMiddleToFloor` state 0 sets `isMoving = true`, finds front or rear wheels of
  different size (`GUI_FrontWheelsDiffSize`/`GUI_RearWheelsDiffSize`), and sets it back to false before returning. The
  postfix check "`isMoving` went false→true" therefore works as designed.
- **`currentState` keeps the old value during the whole movement**, and changes only in the completion callback. So
  `GetState()` in the prefix is the from-state, and is still the from-state while `isMoving`.
- `GetPosForSave()` maps `Middle` without a car to `OnFloor`.
- `InstantSet(int _pos, bool switchIO)` `0x1804C4860` does nothing if the GameObject is inactive. Otherwise it sets
  `currentState = _pos` **first**, then starts `MoveMiddleToFloor(true)` (0), `MoveMedFromFloor(true)` (1) or
  `MoveUp(true)` (2) with a 0-second tween (completes on the next LeanTween update, so `isMoving` is true for about a
  frame). Caveats:
  - the coroutines bail if `isMoving` is already set, which leaves `currentState` changed but the lift unmoved:
    **wait for `!isMoving` before `InstantSet`**;
  - `MoveMedFromFloor` bails with log `[CarLifter] -> MoveMedFromFloor() No connectedGameObject` on an empty lift.
- `ConnectCar(CarLoader)` `0x1804C2C60` has **no native callers**; hooks on it never fire. Its logic is inlined in
  `CarLoader.PlaceAtPosition`: `lifter = groundPosition.GetComponent<CarLifter>()`, `connectedCarLoader = this`,
  `InstantSet(MissingWheels ? Middle : OnFloor, false)`.
- `DisconnectCar()` `0x1804C2BA0` clears the connection and sets `currentState = 0` + `MoveMiddleToFloor(instant)`. Called
  by `CarLoader.ChangePosition`, `CarLoader.DeleteCar()` and `CarLoader.ResetCarLifter`. So **delete, park and any move
  drop the lift to the floor locally**, which matches the server's "reset to OnFloor" rule.
- **Lift ↔ place:** the `CarLifter` component sits on the place transform itself. Evidence: `PlaceAtPosition` does
  `groundPosition.GetComponent<CarLifter>()`, and `ChangeCarPos` treats `placeNo` 3/4 as lifter places.
  `GarageLoader.carLifter[]` (`+0x28`) is a serialized scene array, so its order is not in the code. Vanilla
  saves and restores `carLiftersData[i]` ↔ `carLifter[i]` by index (`GarageLoader.Save` → `GameDataManager.SaveLifter`,
  `GarageLoader.<Load>d__14`). **Runtime: compute the map** (`carLifter[i].transform == CarLoaderPlaces.places[3|4]`)
  instead of assuming 0→`CarLifter1`.
- Vanilla restore at load (`<Load>d__14`): waits while `isMoving`. Then `InstantSet(1, true)` when the saved state is
  floor and the car misses any wheel, otherwise `InstantSet(connectedCarLoader == null ? 0 : saved, true)`. Afterwards
  `FPSInputController.ResetPosition()` if `carLifter[0|1].HaveToResetPlayer()`.

Hooks (1.1):

- **Prefix + postfix `CarLifter.Action(int)`** as designed (direct call, fires).
- Optional "finished" signal: postfix on the completion lambdas listed above (LeanTween delegates). Polling
  `!isMoving` works too.
- `MoveUp`/`MoveMedFromFloor`/`MoveMiddleToFloor` builders and `MoveDown` are also reachable (direct calls / `jmp`),
  but they also fire for `InstantSet`/`DisconnectCar`.
- Do not hook `ConnectCar`.

## 1.2 Car places

`CarPlace { Entrance1..3 = 0..2, CarLifter1 = 3, CarLifter2 = 4, Paintshop = 5, Dyno = 6, DiagnosticPath = 7, CarWash = 8 }`.

- **`placeNo` (`CarLoader+0x24`) is the `CarPlace` value whenever it is not -1.** `PlaceAtPosition` (only when
  `GameScript.CurrentSceneType == Garage (4)`) does `placeNo = Array.IndexOf(CarLoaderPlaces.places, groundPosition)`, and
  `GetCarLoaderForPlace(p)` compares `groundPosition` with `places[p]`. If `groundPosition` is null, it first takes
  `CarLoaderPlaces.GetFreePlace()`. `ChangePosition(int no)` sets `placeNo = no` after the move.
- `GetPlaceNo()` `0x180476560` just returns `placeNo` (folded with an identical getter).
- `IsInPlace(p)` `0x1804D1190` = `GetCarLoaderForPlace(p) == this`.
- `CarLoaderPlaces.carLoaderGroundPosition[]` is **only refreshed in `CarLoaderPlaces.Save`** (IndexOf or -1 per loader)
  and read in `Load`, so it is stale between saves. Do not use it for live state.
- A plain `LoadCar(string)` sets nothing. Callers such as `OrderGenerator.<TakeJob>` pick a place and call
  `PlaceAtPosition` themselves (which is why the harness spawn shows -1).
- `CarLoader.ChangePosition(int no)` `0x180507AF0` (the no-arg overload `jmp`s into it with `placeNo`). If `no != -1`:
  `DisconnectTools()`; if on a lifter → `lifter.DisconnectCar()`; `CarLoaderPlaces.ChangeGroundPosition(this, placeNo, no)`;
  `placeNo = no`.
- `CarLoaderPlaces.ChangeGroundPosition(CarLoader, int From, CarPlace)` `0x181CAEEF0`: `groundPosition = places[place]`,
  `PlaceAtPosition(true, true)` (which also connects a lifter at that place and `InstantSet`s it to floor/middle), and
  `CarPlaceManager.garagePlaces[loaderIndex] = true`. `From` is unused. Callers: `ChangePosition` and
  `BenchmarkManager`. It runs on every load path, so it is not a "player moved a car" signal.

`ChangeCarPos(CarLoader, CarPlace pos, bool movePlayerToCar)` `0x1809E0970` is a plain builder of
`<ChangeCarPos>d__20` (`MoveNext` `0x180A6BDA0`). **It has no native callers.** The only native producer of `d__20` is
`NotificationCenter.ButtonAccept(NewHash)` `0x1809DBA20`, branch `Type == "MoveCar"`. That branch:

- reads `PositionFrom` (the loader), `PositionTo` (`CarPlace`) and `MovePlayerToCar`;
- refuses with an info window for wheel problems when the source or destination is Dyno;
- disables `PathTestManager`/`DynoManager` if they hold the car;
- then `new d__20{carLoader, pos, movePlayerToCar}` + `StartCoroutine` **inline**.

`ButtonAccept` is called with "MoveCar" only by `PieMenuController.<GetOnClick>b__72_42 … b__72_50` (one lambda per
place; `MovePlayerToCar = !UISubmitModifier held`). The other callers (`CarLocationWindow`, `PauseQuitWindow`) use
other types. Non-UI paths that move a car without `d__20`: `LoadCarFromFile(NewCarData)` (`ChangePosition` +
`PlaceAtPosition`), job/junkyard/barn spawns (`PlaceAtPosition`), unpark (below). None of them call `ChangeCarPos`.

`d__20` sequence:

1. state 0: `InputManager.ChangeInput(0,0,0)`, disable `GameScript+0x38`, `SetIOMouseOverNull`,
   **`ScreenFader.NormalFadeIn` and wait for `fadeComplete`**;
2. `PathTestManager` on/off; if the source is at Paintshop (5) or Dyno (6), detach it there; root `eulerAngles = 0`,
   `CloseCar(true)`, hide `CarSupport`;
3. `dest = GetCarLoaderForPlace(pos)`. If occupied: the same cleanup on `dest`, then `dest.ResetCarLifter()`,
   `car.ResetCarLifter()`, **`dest.ChangePosition(car.placeNo)` (vanilla swap)**, `InstantSet(1, false)` on the
   source's lifter if `dest` misses wheels or has mismatched wheels, Paintshop/Dyno attach for `dest`;
4. `WaitForSeconds(0.5)`, `car.ChangePosition(pos)`. At places 3/4: `lifter.InstantSet(1, false)` on missing or
   mismatched wheels. `WaitForSeconds(1)`;
5. `SetIOMouseOverNull`, `GameMode`, `FPSInputController.SetPositionNextToCar(car)` **only if `movePlayerToCar`**;
6. `NormalFadeOut`, `ChangeInput(1,1,1)`, re-enable `GameScript+0x38`.

So `ChangeCarPos(…, false)` fades and locks input on the receiving client. A quiet apply is `car.ResetCarLifter();
car.ChangePosition(place)` (plus `InstantSet(1)` for missing wheels and the Paintshop/Dyno/PathTest attach when those places
matter). A remote **swap** is the same on both loaders.

Hooks (1.2):

- **Prefix `NotificationCenter._ChangeCarPos_d__20.MoveNext` when `__1__state == 0`** (fields `carLoader`, `pos`,
  `movePlayerToCar`). This is the first frame of every vanilla move, after `ButtonAccept`'s own refusals.
- If `dest` is occupied, it is a swap: send both loaders, or refuse (finding 3).
- Alternative: prefix `ButtonAccept` with `Type == "MoveCar"` (fires before the Dyno refusals, so less exact).
- Do not hook `ChangeCarPos` (never fires) or `ChangeGroundPosition` (fires on loads).
- `CarLoader.ChangePosition(int)` fires for both cars of a swap and on loads.

## 1.4 Parking paths

Garage → parking (pie menu "move to parking", `PieMenuController.<GetOnClick>b__72_51` `0x1812DB620`):

1. Refuses on mismatched front/rear wheel sizes or `ParkingCarPlaceManager.ParkingIsFull()` (info
   `GUI_CarsLimitInParking`). Otherwise `StartCoroutine(NotificationCenter.MoveCarToParking(loader))`, a **direct call:
   the prefix fires**. The other caller is `NotificationCenter.<BuyCar>d__21` (row 6).
2. `<MoveCarToParking>d__19` `0x180A6D960`:
   - `ChangeInput(0)`;
   - **`customerCar` → info `GUI_CustomerCarInParking`, restore input, nothing saved or deleted** (vanilla never parks a
     job car);
   - otherwise fade in, `DisconnectTools`;
   - **slot = lowest `i < GetMaxParkingPlacesAmount()` with `!CarExistsOnParking(i)`, else -1**;
   - `CarLoader.SaveCarToFile(slot, true)` (direct) → `GameDataManager.SaveCar(data, slot, true)` (direct) →
     `SaveCarInParking(data, slot)` (direct; sets `data.index = slot` when `carToLoad` is set and writes the profile
     array; `slot >= 800` or `>= Length` only logs);
   - wait `SaveComplete`;
   - `CarLoader.DeleteCar(sceneType != Salon)` (direct) → `DeleteCar(bool)` hides `CarSupport`, nulls `groundPosition` if
     true, `SetPlaceIsOccupied(false)`, then **tail-jumps to `DeleteCar()`** (`0x1804CD727`), so the existing
     `CarSpawnHooks.DeleteCarHook` fires;
   - `GameScript.SetCarLoaderOverNull`, `GarageLoader.Save()`, wait, fade out.
3. All of `MoveCarToParking`, `SaveCarToFile(int,bool)`, `SaveCar`, `SaveCarInParking`, `DeleteCar(bool)` and `DeleteCar()`
   fire, in that order. The slot reaches `SaveCarToFile(index, …)`, `SaveCar(…, index, true)` and
   `SaveCarInParking(…, index)`. No `DeleteCar(bool)` hook is needed beyond the existing `DeleteCar()` one.
   - If the lot is full at that moment, slot -1: vanilla still deletes the car. Only the pie menu check prevents this.

Parking → garage (`ParkingManagementWindow` "move to garage"):

1. `HandleMoveCarInput` / `MoveCarToGarageAction` → `MoveCarFromParking()` builder (direct) → `<MoveCarFromParking>d__57`
   `0x180D20DC0`.
2. `moveCarInProgress` guard. `data = LoadCarInParking(sel)`, where `sel = currentSelectedSourceSaveIndex` (`+0xC0`).
3. If `FindCarConfig` fails or the config version is out of range: `ShowErrorAskWindow(…, PDM_0)` and stop.
4. Loader = `CarPlaceManager.GetFreeCarLoaderIndexWithReservation()`; if none → `ShowFullGarageInfo`.
5. Fade + loading. `loader.placeNo = -1`, `loader.groundPosition = CarLoaderPlaces.GetFreePlace()`,
   `Helper.CheckAndMovePlayerIfNecessary`.
6. `StartCoroutine(loader.LoadCarFromFile(sel, true))` (**direct: the prefix fires**) → `d__423` →
   `GameDataManager.LoadCar(sel, true)` → `LoadCarInParking` → `LoadCarFromFile(NewCarData)` → `d__425`. That coroutine
   calls `LoadCar(carToLoad)` (**the `CarSpawnHooks.LoadCar` prefix fires; suppress it**), then `ChangePosition(-1)`
   (no-op), then `PlaceAtPosition` (`placeNo` = the free place), then sets `loadedFromFile = true`.
7. `CloseCar`, `SaveCarToFile()` (garage, `toParking = false`), wait `SaveComplete`.
8. **`SaveCarInParking(default(NewCarData), sel)`** (zeroed struct, direct) clears the slot.
9. Hide the window, fade out, restore input.

The slot clear goes through `SaveCarInParking` with an empty `carToLoad`. `ParkingCarPlaceManager.RemoveCar` has no
native callers (dead).

Swap (`ParkingManagementWindow`):

- `ConfirmSwapAction` (UIDescription delegate) or `HandleConfirmSwapInput` → `ConfirmSwap()` `0x180A00920` or
  `ConfirmSwap(int src, int dst)` `0x180A009A0` (`jmp`). Both call **`ParkingCarPlaceManager.MoveCar(from, to)`**
  `0x1809FE2E0` directly, then refresh.
- `ConfirmSwap()` bypasses `ConfirmSwap(int,int)`, so **`MoveCar` (static) is the one hook that always fires**.
- `ConfirmSwapAction` computes the destination slot as `destLevel*10 - 1 + button.index`.
- `MoveCar`: `LoadCarInParking(from)`. An empty source → log "Source car save does not exists!" and return. Otherwise
  it moves or swaps with two `SaveCarInParking` calls.

Delete a broken parked car: `PDM_0` = `<MoveCarFromParking>g__RemoveCar|57_0(bool wasAccepted)` `0x180A093E0`. If
accepted: `SaveCarInParking(default, sel)`, `PrepareSourceButtons`, `RefreshPanels`. Its log text is copied from
`ParkingCarPlaceManager.RemoveCar`. **This must be synced (server `TryRemove`) or blocked while connected.**

Parking scene: `ParkingWindow.MoveCarToGarageAction` inlines its `MoveCarToGarage` builder (`d__49` is built in both),
so hook `MoveCarToGarageAction` (delegate → fires), as task 5.6 plans.

Hooks (1.4):

| Purpose | Hook |
|---|---|
| mark loader "parking" | prefix `NotificationCenter.MoveCarToParking(CarLoader)` (refused customer cars still run this, so expire the mark or clear it in the postfix of `d__19.MoveNext` returning false) |
| **send `CarParkRequest`** | **postfix `CarLoader.SaveCarToFile(int index, bool toParking)` with `toParking`**. Slot = `index`. Read the blob with `GameDataManager.LoadCarInParking(index)` |
| silence delete | existing `DeleteCar()` prefix, skipped for marked loaders |
| unpark | prefix `CarLoader.LoadCarFromFile(int, bool)` with `fromParking == true` (the slot is `index`). Expect the `LoadCar(string)` hook and later `SaveCarInParking(empty, slot)` |
| swap | postfix `CMS.Managers.ParkingCarPlaceManager.MoveCar(int, int)` |
| remove broken car | prefix `ParkingManagementWindow.Method_Private_Void_Boolean_PDM_0(bool)` |
| fallback diff | `SaveCarInParking(NewCarData, int)` postfix, if struct-parameter patching works at runtime |

## 1.5 Parking levels

- `UnlockParkingLevelAction()` `0x180A00FF0` is the unlock. It is registered in `PrepareDescriptions` as a UnityAction
  for UIEnter variant 0xE (`DescriptionHelper.RegisterAction`). It has no native direct callers (delegate only), so the
  Unhollower `CallerCount` is 0, but a detour still fires. **There is no confirm dialog.**
- Body:
  ```c
  GlobalData.AddPlayerMoney(-parkingLevelPrice);
  GlobalData.UnlockedParkingLevels++;
  if (UnlockedParkingLevels == 10) IncrementStat("stat_unlock_parking", 1);
  PrepareSourceParkingLevelSelector(currentSelectedSourceParkingLevel);
  RefreshDestinationParkingLevelSelector();
  state = 0; SetIdleStateDescription();
  ```
  - No inventory change and no save; `NewGlobalDataWrapper.UnlockedParkingLevels` follows only on `GlobalData.Save`,
    which `GarageLoader.Save` calls.
  - `AddPlayerMoney` plays the money sound, refreshes stats, clamps, and does nothing with `UnlimitedMoney`.
  - **It does not check money or which level is selected.**
- Price, from `OnSourceParkingLevelChange(int index)` `0x180A03510` when `index + 1 > UnlockedParkingLevels`:
  `parkingLevelPrice = (int)(index * 50000 * (upgrade "cheaper_parking" unlocked ? 0.5f : 1f))`.
  - `50000` is `GlobalData.Cost_BaseParkingLevel` (a `const`, so it is inlined).
  - The level selector lists `UnlockedParkingLevels` entries plus one locked entry (only while `< 80`), so
    `index == UnlockedParkingLevels`. That gives **price = UnlockedLevels × 50,000 (× 0.5 with the upgrade)**; level 2
    costs 50,000.
- Money gating is UI only: `SetUnlockAlleyDescription` shows the UIEnter variant only when
  `PlayerMoney >= parkingLevelPrice`.
- Hook: prefix `UnlockParkingLevelAction` (skip vanilla when connected, send `TargetLevels = UnlockedParkingLevels + 1`,
  `Price = parkingLevelPrice`). Remove `PDM_0` from Decision 7, and add it to 1.4 as the delete path.

## 1.3 static facts (codec, layout)

- `NewCarData` is a **struct** (TypeDefIndex 8725, 0x1B8 bytes). Unhollower wraps it as
  `sealed class NewCarData : Il2CppSystem.ValueType`. Stubs pass it as `il2cpp_object_unbox(...)`, and
  `LoadCarInParking` returns a boxed copy, which is correct.
- `Serialize(BinaryWriter, byte saveVersion)` `0x1809D1D00` writes `carToLoad` first and **stops after it when
  `carToLoad` is empty**. The top-level layout is always the newest one (`ecuData` and `measuredDragIndex` are always
  written); `saveVersion` only goes to sub-serializers.
- `Deserialize(BinaryReader, byte)` `0x1809D27B0` reads `ecuData` only if `saveVersion > 1` and `measuredDragIndex`
  only if `> 3`. **Use the same, current `ProfileData.saveVersion` on both sides.** A blob cannot be read with an
  older version.
- Only callers: `ProfileData.SerializeToBytes`/`DeserializeFromBytes`.
- `IsDefault()` `0x1809D32D0` = `carToLoad == null || carToLoad.Length == 0`, so a zeroed `new NewCarData()` is
  default. No native callers.
- `GetMaxParkingPlacesAmount()` `0x180D7C930` = `UnlockedParkingLevels * 10`.
- `ProfileData.InitParkingCars` allocates **`carsOnParking = new NewCarData[800]`**. Each entry is a default struct
  (`carToLoad` null, 4-int wheel arrays, plate "Arizona").
- New profile: `UnlockedParkingLevels = 1` (`ProfileData.InitGlobalData`, `NewGlobalDataWrapper..ctor`).
  `GlobalData.Load`/`Save` copy it to and from wrapper `+0x2C`.
- **`ParkingLayout`: `SlotsPerLevel = 10`, `MaxLevels = 80`, `DefaultUnlockedLevels = 1`** (80 × 10 = 800 = array length).

## Still needs a runtime check

1. `carLifter[i]` ↔ `CarLifter1`/`CarLifter2` mapping (scene data).
2. `CarLifter.Action` pre/post logging: `isMoving` true in the postfix, the empty-lift return, and the
   wheel-size refusal (true→false).
3. Prefix on `NotificationCenter._ChangeCarPos_d__20.MoveNext` fires under Unhollower and exposes `carLoader`/`pos`.
   Also confirm that the pie menu lambda → `CarPlace` order is 42→0 … 50→8 (assumed, not checked per lambda).
4. Quiet apply via `ResetCarLifter` + `ChangePosition(place)`: car placed correctly, lift connected, no fade. Also what
   happens to a remote client's camera/player inside the place.
5. Whether moving a car that stands on a raised lift is offered by the pie menu (vanilla resets the lift to the
   floor via `ResetCarLifter` either way).
6. `carsOnParking` through `Il2CppReferenceArray<NewCarData>`: expected broken. Compare it with `LoadCarInParking(i)`.
7. Harmony patching of `SaveCarInParking(NewCarData, int)` (by-value struct parameter): whether it works and what
   `carData` contains.
8. Codec round trip (1.3), blob size, and `new NewCarData().IsDefault()`.
9. The `UnlockParkingLevelAction` prefix fires from the UIEnter key/button. Whether a hidden UIEnter variant really
   blocks the action when money is short.
10. The unpark sequence order with logs: `LoadCarFromFile(int,true)` → `LoadCar(string)` hook →
    `SaveCarToFile(idx,false)` → `SaveCarInParking(empty, slot)`.
11. That the `PDM_0` delete path can be triggered (a car whose config is missing) or can be ignored.

## Runtime results (2026-10-06, scenario `placement-spike`, run `20261006-121707`)

Checks from the list above, in its numbering:

1. Lift map: `lifters` reports each lift's nearest `CarPlace`; the lift a car on `CarLifter1` connects to is found
   by `GetConnectedCarLoader` (spike.json in the run folder has the indices).
2. `CarLifter.Action`: `isMoving` is `true` in the postfix while `GetState()` still shows the old state; an empty lift
   stays `OnFloor` with `isMoving false` (no movement). Up from `OnFloor` goes to `Middle`, the next up to `Up`.
3. A prefix on `NotificationCenter._ChangeCarPos_d__20.MoveNext` fires and reads `__1__state`, `carLoader`, `pos`,
   `movePlayerToCar`. States 0 → 1 (fade, ~0.5 s) → 2 → 5 (`ChangePosition(place)` and, for a car on a raised lift,
   `CarLifter.InstantSet(0)`) → 6 → 7 → 8. Started here through the `ChangeCarPos` builder; the pie-menu path is
   still to be confirmed by hand.
4. Quiet apply: `ResetCarLifter()` + `ChangePosition(place)` sets `placeNo` to the `CarPlace` value and puts the car
   in the place (`IsInPlace` true), without a fade. A car loaded with plain `LoadCar` has `placeNo -1` and is in no
   place until then.
6. `carsOnParking[slot]` reads correctly through Unhollower (empty slot: null; occupied: the car), contrary to the
   static concern; `LoadCarInParking` gives the same car. Array length 800; `GetMaxParkingPlacesAmount()` = 10 with one
   unlocked level (it is the usable slot count).
8. Codec: a parked `car_boltatlanta` is 15,555 bytes; serialize → deserialize → serialize gives identical bytes;
   `new NewCarData().IsDefault()` is true. Deserializing needs a prepared target: `BodyPartsData`, `PartData`,
   `FluidsData.Oil` (a class) and the four fluid lists must exist (`NewCarDataCodec.Deserialize`).
10. Park order: `NotificationCenter.MoveCarToParking` → `SaveCarToFile(0, toParking: true)` (slot 0, the lowest free)
    → `DeleteCar()` → `SaveCarToFile(i, false)` for every loader. Unpark through `LoadCarFromFile(slot, true)` →
    `LoadCar(name)` → `ChangePosition(-1)`; it does **not** clear the slot (the parking window does that), and the car
    ends on `Entrance1` (`placeNo 0`).

Not run: 5 (pie menu offering a move of a car on a raised lift), 7 (patching the by-value struct parameter, avoided by
design), 9 (unlock key), 11 (unloadable-car delete).
