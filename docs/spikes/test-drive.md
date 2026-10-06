# Spike: test drive, test path, dyno and diagnostics

Roadmap row 13 `sync-test-drive-and-diagnostics` and the early spike "Test drive round trip". Method: static decompile
only (setup in `native-decompile.md`), game not launched. Date: 2026-10-06.

Decompiles are in `%USERPROFILE%\CMS21-TestInstalls\native\out\testdrive_clean`, `testdrive2_clean`, `testdrive3_clean`
and `testdrive4_clean` (targets in `work\targets\testdrive*.txt`). Older folders used: `placement*_clean`
(`GarageLoader.<Load>d__14`, `GarageLoader.Save`, `CarLoader.LoadCarFromFile*`, `CarLoaderPlaces.Save/Load`) and `clean`
(`MapWindow`, `NotificationCenter.<SelectSceneToLoad>d__34`). Branch targets and static-field stores marked
"(asm)" were checked against the raw bytes with capstone.

## Short answers

1. **Only the test track is a scene change.** The test path (`CarPlace.DiagnosticPath` 7) and the dyno (`CarPlace.Dyno`
   6) are in the garage scene. The benchmark is the graphics FPS benchmark from the main menu and has nothing to do
   with cars.
2. **Leaving:** the car is identified only by `GlobalData.SelectedCarLoader`, the loader's `GameObject` name
   (`CarLoader.GetSaveName()`, for example `#CarLoader1`). Index = `Helper.GetIndexFromCarLoaderName` (strips
   `#CarLoader`). `SelectSceneToLoad(..., saveGame: true)` runs `GarageLoader.Save()`, which writes every car into the
   in-memory profile with `SaveCarToFile(index)`. The track then loads its own copy with
   `CarLoader.LoadCarFromFile(SelectedCarLoader)` → `GameDataManager.LoadCarInGarage(index)`. No temp file is used.
3. **Track results are only three things:** the mileage delta (`GlobalData.NewMileage`, km), the body dirt from
   driving (`BodyPartData.WashFactor/Dust`, written into the in-memory profile slot), and a Steam stat. **No part wear**:
   the track code only reads conditions (`GetBrakeAvgCondition`, `GetSuspensionAvgCondition`).
4. **Examined parts are not produced on the track.** They are produced **in the garage after the return load**.
   `GarageLoader.Load` sees `TestToShow == "ExamineReport"` and opens `ExamineReportWindow`. Its `GetExaminedParts`
   finds the car by `SelectedCarLoader`, clears both globals and calls `PartScript.Examine(true)` on each mounted part
   whose `ExamineGroup` is in {2, 20, 24}. The report only appears if every track test was done; otherwise
   `TestToShow = ""`.
5. **Dyno result** = the whole `CarLoader.EngineData` struct (incl. `measured`) plus `CarLoader.MeasuredDragIndex`.
   Both are saved in `NewCarData` (`EngineData` serializes `measured`; this corrects the note in
   `sync-car-details/review.md` #10).
6. **Test path result** = `PartScript.IsExamined` (groups {4, 16, 28, 40}) plus `CarLoader.specialState = 1`
   (persisted by `CarLoaderPlaces.Save`). The four bars only read conditions.
7. **OBD and the other examine tools** only call `PartScript.Examine(true)`. The existing `PartHooks.AfterExamine`
   postfix already catches them.
8. **The mod's return load drops the mileage.** `LoaderAddition.VanillaLoad` removed the per-loader
   `LoadCarFromFile` loop, and the vanilla `Mileage += NewMileage` step is inside that loop. The track dirt is also
   lost, because the server snapshot replaces the profile slot.

## 1. Leaving the garage for the test track

Two UI paths set the car:

| Path | Method (VA) | What it sets |
|------|-------------|--------------|
| Map opened while seated in a car | `MapWindow.Show` `0x180D08E10` → `VerifyCarStateIfInterior` `0x180D09EF0` | It checks oil, fluids and wheel sizes (info windows). Then `carNeedsDyno = …` and `GlobalData.SelectedCarLoader = car.GetSaveName()`. `haveToSelectCar = false` |
| Map opened on foot | `MapWindow.SubmitPanelAction` `0x180D0D650` case 2 → `OpenCarsPanel("Test_track_1", 6)` → `SideCarsPanel.DriveAction` `0x180DBFCC0` | `SelectedCarLoader = carSaveName`. If `sceneType == 6`, `TestToShow = "ExamineReport"`. If `carInfoPanel.needDyno`, `MapWindow.MeasurePowerForSelectedCarLoader` |

When seated, `SubmitPanelAction`/`SubmitItemAction` case 2 set `TestToShow = "ExamineReport"` themselves (the store
helper `0x180097AB0` writes `GlobalData+0xA8` (asm); `0x180095650` writes `+0xA0` = `SelectedCarLoader`). Both paths
end in `NotificationCenter.SelectSceneToLoad("Test_track_1", SceneType.TestTrack (6), useFader: true, saveGame: true)`.
The pie option `move_testdrive` only has a description (`GetSetDesc`). It has no `GetOnClick` entry.

`NotificationCenter.<SelectSceneToLoad>d__34.MoveNext` `0x180A6E5C0` (builder `0x1809E19D0`, already prefixed by
`SceneHooks`):

```c
if (loadingScene) return; loadingScene = true; ChangeInput(0); fade in;
if (sceneType in {6,7,11,13,14,17})                       // tracks
    foreach (cl in FindObjectsOfType<CarLoader>())
        if (cl.GetSaveName() == GlobalData.SelectedCarLoader) cl.CloseCar(true);
if (currentScene in {Junkyard, Barn}) { ShopListWindow.Save(); GameDataManager.Save(profile); }
if (saveGame && currentScene not in {Menu, None, Showroom}) GlobalData.Save();
if (saveGame && GarageLoader.instance) GarageLoader.Save(false);   // 0x180D685A0, then waits for SaveComplete
SceneLoader.SceneToLoad = name; SceneManager.LoadScene("SceneLoader");
```

`GarageLoader.Save` does the following:

- `SaveCarToFile(Helper.GetIndexFromCarLoaderName(name))` for every loader;
- saves the machines, radio, `CarLoaderPlaces.Save` (incl. `specialState`) and the lifters;
- saves `OrderGenerator`, `GlobalData`, unlocked positions, `Warehouse`, `Inventory`, `UpgradeSystem`, garage look and the shop list;
- calls `GameDataManager.Save(profile)`.

`SessionGuard` blocks only the final disk write, so the in-memory profile is up to date, and the track reads that.

## 2. On the track

- `PrepareCarPhysics.Awake` `0x1809806C0`: `AutoSaveMileage = (scene != DragStrip)`. `Start` `0x180982090` starts
  `<Prepare>d__38` `0x180964250`. That coroutine **inlines** the `LoadCarFromSave` builder (`new d__41`), so a hook on
  `PrepareCarPhysics.LoadCarFromSave` never fires.
- `<LoadCarFromSave>d__41` `0x180963140`:
  `carLoader.DeleteCar(); ClearCar(); StartCoroutine(carLoader.LoadCarFromFile(GlobalData.SelectedCarLoader))`.
  The string overload `0x1805077E0` → `d__424` `0x18171FB00` → `GameDataManager.LoadCarInGarage(index)` `0x181029CA0` →
  `LoadCarFromFile(NewCarData)` (`d__425`).
- `TestTrackManager.<Prepare>d__6` `0x180B7C390` builds the checklist from `ListOfTestOnTestTrack` (`BrakeTest` /
  `SuspensionTest` triggers), sets `GameMode.CarDrive` (15) and starts `PrepareCarPhysics.LoadCar`.
- `PrepareCarPhysics.CalcDistance` `0x1809831A0` (when `canIncrementMileage`): `mileage += |v|·dt` in metres. Every
  500 m it calls `carLoader.AddWashFactorValue(null, -TrackManager.Instance.WashFactorToAdd)`, which dirties the
  **track's** car.
- `TestTrackManager.DoneTest(i)` `0x180AE0950`: hides test i and shows the next one. After the last test:
  `IncrementStat("stat_finish_testtrack")` and the virtual `ReturnToGarage()`.

## 3. Return: what is saved, then loaded, in order

Exits: the last test (`DoneTest` → vtable `ReturnToGarage`) and the pause menu's track button
(`PauseQuitWindow.<CreateTrackButtons>g__Action|1` `0x180D2F850` → vtable `ReturnToGarage`). Quitting to the menu
disconnects (mod).

1. `TestTrackManager.ReturnToGarage` `0x180AE1180` → real call `TrackManager.ReturnToGarage` `0x18083FBE0`. That
   restores input and, when `AutoSaveMileage`, calls `PrepareCarPhysics.SaveMileage(false)` `0x1809836E0`:
   ```c
   GlobalData.NewMileage = (int)max(1, mileage / 1000);      // km, logged "SAVE MILEAGE: {0}"
   NewCarData d = GameDataManager.LoadCarInGarage(GetIndexFromCarLoaderName(SelectedCarLoader)); // struct copy
   d.UpdateBodyPartsData(carLoader);   // 0x1809D3980: writes BodyPartData(carParts[i]) into the SHARED
                                       // List<BodyPartData> items → the profile slot gets track dust/wash
   ```
2. If any `TestOnTestTrack` is still active: `GlobalData.TestToShow = ""`.
3. `SelectSceneToLoad("garage", Garage, true, true)`. The current scene is 6, so `GlobalData.Save()` runs. There is no
   `GarageLoader` on the track, so no car save.
4. Garage: `GarageLoader.<Load>d__14.MoveNext` `0x180899ED0` (vanilla; the mod replaces it with
   `LoaderAddition.CustomLoad`). The order:
   1. `GlobalData.Load`, difficulty, radio, `LoadMachines`, `Inventory.Load`, `Warehouse.Load`, `TempInventory` merge;
   2. `CarLoaderPlaces.Load` `0x181CB07E0`, which restores `specialState` and the Dyno/PathTest attach;
   3. garage level, look, texture packs;
   4. for each loader: `DeleteCar()`; `LoadCarFromFile(name)`; wait for `loadedFromFile`. Then
      **`if (GetSaveName() == SelectedCarLoader && NewMileage != 0) { CarInfoData.Mileage += NewMileage; NewMileage = 0; }`**
      (`String.op_Inequality(saveName, GlobalData+0xA0)` at `0x18089AEA8` (asm)). In `d__425`:
      `if (specialState == 1) PathTestManager.SetCarPositionAfterLoad(true)`;
   5. player position, lifters, unlocked positions, `OrderGenerator.Load`, shop list;
   6. fade. **`if (TestToShow == "ExamineReport") { EnableWindowOpening(0x20); GameScript.currentExamineType = TestDrive (1); WindowManager.Show(ExamineReport) }`**,
      then the DLC error window and `isReady`.
5. `ExamineReportWindow.Show` `0x180BEF690` → `GetExaminedParts` `0x180BF0960`:
   ```c
   car = GameScript.GetIOMouseOverCarLoader2();
   foreach (cl in FindObjectsOfType<CarLoader>()) if (cl.GetSaveName() == SelectedCarLoader) car = cl;
   if (GameMode.current == PathTest) car = PathTestManager.carLoader;
   if (car && car.root) {
       SelectedCarLoader = ""; TestToShow = "";
       foreach (ps in CarHelper.GetPartsToExamine(car, examineType).OrderBy(...)) { ps.Examine(true); list.Add(...) }
   }
   ```
   `CarHelper.GetPartsToExamine` `0x1804B7360`: mounted `PartScript`s (`!IsUnmounted`) filtered on
   `PartProperty.ExamineGroup`. OBD (0) uses ==1; TestDrive (1) uses {2, 20, 24}; PathTest (2) uses {4, 16, 28, 40};
   Compression 5, Multimeter 6, TireTread 7, CompoundMeter 8. `PartScript.Examine` `0x180FFF6B0` only sets
   `IsExamined = true` and `UpdateShaderParams` when it is not already examined.

The mod already copies step 6 (`LoaderAddition.cs` ~line 277, after `IsInitialSyncFinished`). Step 4's mileage line is
missing there.

## 4. Test path (garage, `CarPlace.DiagnosticPath`)

| Step | Method (VA) | Effect |
|------|-------------|--------|
| move | pie `move_pathTest` → `PieMenuController.<GetOnClick>b__72_50` `0x1812DB0C0` → `ButtonAccept(MoveCar, PositionTo 7)` | Also `PathTestManager.testIsComplete = false`, `carLoader = mouse-over car`, `car.specialState = 0` (the same as `ConnectCarLoader` `0x1808D7520`) |
| start | `GameScript.<>c__DisplayClass72_0.<ClickIO>g__RunPathTest|0` `0x1808945A0` | `PathTestManager.Prepare` `0x1808D75F0` (sits the player inside, `GameMode.PathTest` 16), ignition |
| tests | `PathTestManager.Update`/`DoTests`, `GetResult*` (no native callers: animation events) | Read-only `GetBrakeAvgCondition`/`GetSuspensionAvgCondition` → `PathTestWindow.UpdateValue` |
| end | `<EndAllTests>d__50` `0x180D2E9D0` | `testIsComplete = true`, **`car.specialState = 1`**, `GameScript.currentExamineType = PathTest (2)` |
| exit | `<ExitFromCar>d__47` `0x180D2EF70` | `stat_finish_testpath`, `GameScript.ExitFromInterior` `0x180E8FB40` → `<ExitFromInterior>d__105` `0x180895A70`: in PathTest mode, `Hide(0x26 PathTestWindow)` + `Show(0x20 ExamineReport)` (`0x180896556` (asm)) → `GetExaminedParts` as above |

`specialState` is saved per loader by `CarLoaderPlaces.Save` `0x181CB02B0` (`ProfileData+0x98`, int[] at `+0x18`). On load,
`specialState == 1` puts the car at `PathTestManager.places[1]`, the "after the test" spot. That is placement state
(row 2) and is optional for row 13.

## 5. Dyno (garage, `CarPlace.Dyno`)

| Step | Method (VA) | Effect |
|------|-------------|--------|
| start | `GameScript.<ClickIO>g__RunDyno|72_1` `0x180E95B70` → `DynoManager.RunDyno` `0x1810102B0` | `PrepareDyno`, `CloseCar(true)`, `GameMode.Dyno` (18), `Show(0x27 DynoWindow)` |
| prepare | `DynoManager.PrepareDyno` `0x18100E5E0` | Backs up `engineDataBackup` and `measuredDragIndexBackup`. Sets `car.EngineData = EngineData.GetCurrentEngineData(car, true, -1)` `0x180BE7540` (stock data scaled by `CalcTuningValue`), and `car.MeasuredDragIndex = round(CalcPerformanceIndex)` |
| run | `DynoWindow.StartAction` → `<StartDyno>d__32` `0x180885810` | `DynoMeasured = true` |
| close | `DynoWindow.HideAction` `0x181017490` → `DynoManager.CloseDyno` `0x181012140` (real call) | `if (!DynoMeasured)` restores both backups; `else { car.EngineData.measured = true; DynoMeasured = false; }` |

Fields: `CarLoader.EngineData` `+0x558` (struct, 0x3C bytes, `measured` at +0x38), `MeasuredDragIndex` `+0x170`.
`CarLoader.MeasurePower` `0x1804D4EE0` computes the same values without the UI. Its only caller is
`MapWindow.MeasurePowerForSelectedCarLoader` (`AddPlayerMoney(-500)` + `MeasurePower`, for a drag strip trip with
`needDyno`). `DynoManager` also has `job`/`haveJob` fields that this spike did not read.

## 6. OBD and other examine tools

`ObdScanner.Use` (virtual `ExamineTool`, no direct callers) → `<UseAnim>d__2` `0x180A6F860` →
`GetPartsToExamine(car, OBD)` → `PartScript.Examine(true)` per part, a popup, then `ExamineTool.ShowSummary`
`0x180BF1A40` (ExamineReport). `CarLoader.GetBlockOBD` `0x1804D4680` only reads the `blockOBD` ini flag for the engine.
That is config and needs no sync. Compression, multimeter, tread depth and compound meter follow the same pattern.

## 7. Benchmark

`CMS.Managers.BenchmarkManager` is the main-menu graphics benchmark
(`VideoSettingsTab.RunBenchmarkAction` → garage scene with `NotificationCenter.BenchmarkActive`). `LoadCars` loads demo
cars and `StopBenchmark`/`Update` go back to `"Menu"`. `BenchmarkState` is its UI state. It cannot be reached while
connected, so there is nothing to sync.

## 8. Hooks for row 13

**(a) Capture before the scene change**

- **Departure / claim:** `ClientScene.LeavingScene(Garage, TestTrack)` (the existing prefix on
  `SelectSceneToLoad(string, SceneType, bool, bool)`). `GlobalData.SelectedCarLoader` is already set on both UI
  paths. Map it with `Helper.GetIndexFromCarLoaderName` and claim that loader. An earlier intent point is a postfix on
  `SideCarsPanel.DriveAction` / `MapWindow.VerifyCarStateIfInterior`.
- **Return results:** `ClientScene.LeavingScene(TestTrack, Garage)`. By then `SaveMileage` has run (base
  `ReturnToGarage` runs before the `SelectSceneToLoad` call) and the track scene is still loaded. Read
  `GlobalData.NewMileage`, `SelectedCarLoader`, `TestToShow` and the dirt from
  `TrackManager.Instance.carPhysics.carLoader` `CarPart`s (`Dust`/`WashFactor`, row 4's BodyCosmetics shape). An
  alternative is a postfix on `PrepareCarPhysics.SaveMileage`. If the server applies the mileage, set
  `GlobalData.NewMileage = 0` afterwards so that a vanilla load never adds it twice.
- **Dyno:** prefix `DynoManager.CloseDyno` (read `DynoMeasured`) and postfix (send `CarLoader.EngineData` +
  `MeasuredDragIndex` when it was measured). This is garage-local, so it needs no scene handling, only the car claim
  while the dyno is open (`GameMode.Dyno`).
- **Test path / OBD:** nothing extra. `PartHooks.AfterExamine` covers the examined flags. `specialState` is optional
  (row 2).

**(b) Apply the server's state after the return load**

- The mod owns the return load (`LoaderAddition.CustomLoad`). After `IsInitialSyncFinished` and **before** the
  `TestToShow == "ExamineReport"` block, either rely on the server snapshot (mileage/dirt already folded in from (a)) or
  apply locally: `car.CarInfoData` copy-assign with `Mileage += NewMileage` for the loader whose `GetSaveName() ==
  SelectedCarLoader`, then `NewMileage = 0` (row 4's Info poll would then send it). The first matches row 6 design D6.
- Keep the ExamineReport block as it is. `GetExaminedParts` runs on the **snapshot car** and its `Examine` calls go
  through `PartHooks.AfterExamine` → row 1. This needs the server to put the car back on **the same loader name** (the
  claim prevents moves). Otherwise the report examines the mouse-over car or nothing.
- Remote dyno apply: copy-assign `CarLoader.EngineData` (whole struct) and set `MeasuredDragIndex`. These are plain
  fields with no side effects. `DynoWindow` reads them on open.

Note for row 6 (`sync-players-and-scenes/design.md` D6): "examined parts" are not results produced away. Only the
mileage and the dirt are. The examine happens after the return snapshot, in the garage.

## 9. Inlined or indirect calls

- `PrepareCarPhysics.LoadCarFromSave` builder: inlined into `<Prepare>d__38`. Hook `d__41.MoveNext` or
  `CarLoader.LoadCarFromFile(string)` (a real call) instead.
- `TrackManager.Instance`: a static field read, inlined (`ExitFromTrack.OnTriggerEnter`, `CalcDistance`).
- `TestTrackManager.ReturnToGarage`: only reached through the vtable (`DoneTest`, the pause button). It is an override
  with its own body, so it can be patched. `TrackManager.ReturnToGarage` and `SaveMileage` are real calls.
- `PathTestManager.GetResult*` and `ObdScanner.Use`: no native callers (animation events / virtual).
- `ExamineReportWindow.GetExaminedParts`, `PartScript.Examine`, `DynoManager.CloseDyno/RunDyno/PrepareDyno`,
  `GameScript.ExitFromInterior` and `MapWindow.VerifyCarStateIfInterior`: real calls.

## 10. Still to check at runtime

1. `GlobalData.Load` on return keeps `NewMileage`, `SelectedCarLoader` and `TestToShow`. Vanilla depends on this, but
   it is not read here.
2. The `LeavingScene` results packet reaches the server before the return `AskForSync` (row 6 ordering), and the
   snapshot then carries the new mileage and dirt.
3. The loader names (`#CarLoaderN`, from the `#CarLoader` literal in `GetIndexFromCarLoaderName`) and that the server
   puts the car back on the same loader.
4. `CloseCar(true)` on departure (tracks only) may open or close body parts through the `CarPart` overload. Check
   whether row 4 sends that during the scene change.
5. A hook trace: `PartHooks.AfterExamine` fires once per part when the report opens after return
   (`ClientScene.IsGarageReady` is true by then).
6. Whether `DynoManager.job/haveJob` changes job state (not read).
7. Test path with another player present: the car is moved and wheels turn only locally. A claim is needed while
   `GameMode.PathTest`.
