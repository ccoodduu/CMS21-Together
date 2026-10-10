# Spike: single-player features blocked in multiplayer

ROADMAP rows 25–30 (user wishes of 2026-10-08). Static decompile (setup in `native-decompile.md`; targets
`work\targets` were passed by file, output in `%USERPROFILE%\CMS21-TestInstalls\native\out\spfeat_clean` and
`spfeat2_clean`) plus two runtime runs on test lane 2 with the harness verbs in
`tools/TestHarness/Features/SpFeatureProbeCommands.cs` and the scenarios `sp-features-probe` and `sp-features-probe2`:

- `20261008-230909_L2_sp-features-probe` (guard on, as in every harness run);
- `20261008-231615_L2_sp-features-probe2` (`guard-allow` for `Scene:RaceTrack`, `Scene:SpeedTrack`, `Mode:CarDrive`).

## 1. Tuning (`TuneWindow`)

- Opened by clicking the dyno's tuning computer: `#dynoTune` in `GameScript.ClickIO` (and `InteractiveObject.CanHold`),
  through `WindowManager.Show(Tune = 56, args)`, so the guard sees it. Customer cars are refused by the game
  (`GUI_NoTuneCarFromOrder`, also in `ClickIO`). Tabs without a tuned part show `GUI_NoTuneEcu/Carb/Gearbox`.
- ECU and carburettor: both `ApplyAction`s tail-jump to `PartModule.Tune` (row 4's hook, `car-details.md` section 10).
- Gearbox: `CMS.UI.Logic.Tune.GearboxTab.ApplyAction` (`0x180D73180`) writes `gearboxHandle.finalDriveRatio` and a new
  `gearRatio` array when the gearbox part `IsTuned()`; no hook today.
- Runtime: a stock Bolt Atlanta has a `GearboxHandle` under `engine_v8_stary` with an empty `gearRatio` and
  `finalDriveRatio = 0`.
- Row 25 spike 1.2 (`20261009-220832_L1_tune-probe`, `20261009-221010_L1_tune-probe`, headless): a racing part is the
  stock part with a `tunedID` from the game's tuning table (`GameInventory.tuningArray` rows `t_<id>|<id>`); for the
  Bolt Atlanta `t_v8_gearbox_stary` (gearbox `s:13.73`) and `t_v8_gaznik_1` (carburettor `s:13.28`; the car has no
  ECU). `PartScript.TunePart(tunedId)` makes `IsTuned()` true. The `#dynoTune` click through `GameScript.ClickIO`
  opens the window on the car at the dyno (mode `UI`, gearbox tab first, `GearboxTab.carLoader` set); the gearbox tab
  is `hasGearbox` only with the racing gearbox. `GearboxTab.ApplyAction` after a final-drive slider change writes
  final 3.7 and five ratios; B's copy stayed unchanged (no hook), and the carburettor tab's `ApplyAction` then carried
  `t:gearbox` along with the carburettor entry. Taking off the tuned parts (`FastUnmount`, `PartScript.Hide`) gives
  items `t_v8_gearbox_stary` with `GearboxData` (final 3.7, the ratios) and `t_v8_gaznik_1` with `tuningData`
  (`IsTuned`, values `2,0,-1`): D6 holds. `PartScript.DoMount` copies an item's `tuningData` into the part's
  `EcuModule`/`CarbModule` (`CopyDataFrom`) and its `GearboxData` into the `GearboxHandle`, without `PartModule.Tune`,
  so the fit needs its own commit point (a mount in a part change marks `Tuning` dirty).

## 2. Bonus parts

- `CarLoader.bonusParts` (`List<BonusPart>`); `BonusPart` has `ID`, `UID` (`bonusPart<i>`), `IsUnmounted`, `IsPainted`,
  `Color` (`CustomColor`), `PaintType`, `PaintData`, `Handle`, `IsDummy()`.
- Runtime: Bolt Atlanta has one slot, `#BonusDummy`, unmounted, unpainted.
- Fit and remove paths: `car-details.md` section 9 (`TakeOffBonusPart`, real `Inventory.Add` on remove, inlined
  `RemoveAt` on fit).
- Row 25 spike 1.3 (`20261009-225008_L1_bonus-spike`, `20261009-225816_L1_bonus-spike`, headless): Six Once Bulion has
  two slots (`bonusPart0` hood, `bonusPart1` trunk), the same on both clients (the third car did not load in the
  harness). 17 bonus items (`BonusPart.GetItems`); hood scoop and spoilers can be painted, the roof items cannot
  (`PartProperty.CanPaint`, which `BonusPart.Paint` checks). The fit through `SelectPartToMount` (mode
  `BonusAssemble`, the slot's `InteractiveObject` under the mouse) removes the item through `Inventory.Delete`, so the
  removal is already synced (the inlined `RemoveAt` in the decompile is not the path that runs); the remove through
  `ClickIO(1)` (mode `BonusDisassemble`) plays a sound, then `TakeOffBonusPart` adds a new item through
  `Inventory.Add`. On a receiver `Change` + `Paint` + `TakeOn` and `TakeOff` + `TryDeleteBonusPart` work;
  `BonusPart.Paint` takes a `CustomColor` built from its float array (its 4-float constructor throws
  `ObjectCollectedException`). A fit onto a slot that is already filled turns into the game's remove and still deletes
  the selected item, so the mod refuses it ("This slot just changed.").

## 3. New engine on the stand

- `CreateEngineWindow.GetEngines` lists `GameInventory.GetEnginesToCreate` ids that contain "engine";
  `CreateEngineAction` → `EngineStandLogic.SetEngineOnEngineStand(currentEngine)`; no money call in the path.
- The harness verb `tool-stand-create` throws `BadImageFormatException` ("Method with open type while not compiling
  gshared") on `GetEnginesToCreate(out List<string>)`: it never worked.
- Runtime run 2: `SetEngineOnEngineStand(new Item("engine_v8_stary"))` on an empty stand returned, money stayed 12500,
  and after 25 s neither A's nor B's stand held an engine, with no error logged: the build coroutine does not finish
  in a harness game (same as the engine stand note in STATUS 2026-10-06). The engine build stays a hand check unless
  the coroutine can be driven.
- Only one engine stand exists in the garage (`EngineStand2` not present, `tool-list`).
- Row 25 spike 1.1b (`20261010-031447_L1_engine-build-probe`, `20261010-031658_L1_engine-build-probe`, headless): the
  window lists 36 engines, the Bolt Atlanta's `engine_v8_stary` among them (not a bad id). The build stalls because
  `<SetGroupOnEngineStand>d__8` yields `WaitForEndOfFrame`, which never comes in a `-nographics` game, also without
  the fade. Stepped every frame (as the mod's remote put does), `CreateEngineAction` builds the engine; B gets it
  through the existing stand sync, money unchanged. A second build on the occupied stand replaces the engine with no
  message and no inventory return (the old engine is lost for everyone), as the review expected. The coroutine copies
  the group (new group UID) and keeps the engine item, so the built put is matched by the engine item's UID.

## 4. Salon, Showroom, car version

- Car salon = `SceneType.Salon`, scene `Auto_salon`, reached from the map (`MapWindow.SubmitPanelAction` case 6, free).
- Showroom = `SceneType.Showroom`, scene `Showroom_2`, loaded only by the main menu (`MainSection` lambda
  `<AssignActionToButton>b__1`). `ShowroomOptions`: SelectCar, XRay, StartEngine, ExplodeCar, Paint, Rust, SitInside,
  Exit; `ShowroomManager.ReturnToMenu` is the only way out. No money or save. Quitting to the menu disconnects a session
  (`SceneHooks`), so a connected player cannot get there.
- `CarVersionWindow` callers: `SalonSelectCarWindow.SubmitCar`, `ShowroomWindow.SubmitCar` and one obfuscated method. Both
  known callers call the window's own `Show(object[])` through the vtable; `CarVersionWindow.Show` does not call
  `WindowManager.Show`, so the guard's `Window CarVersion` rule never fires. Not a garage feature.

## 5. Tracks and DLC

- `SceneHelper.CanGoToScene(sceneType, out reason, level)`: Junkyard level 5, Auction level 15, Barn level 10 and barns;
  Garage, TestTrack, RaceTrack, Salon, Parking, FunTrack, SpeedTrack always; **DragStrip needs `IsDLCInstalled(22)`**
  (reason 2 = DLC); **CustomTrack needs Workshop track items** (reason 5). `SceneHelper.GetDLCForScene` returns 22 only
  for DragStrip.
- `SteamDLC.Init`: index 22 = "Drag_Racing_DLC" (Steam app 2112231). (Index 6 "Garage_Customization_DLC" and index 3
  "Tuning_DLC" have product id `-1`, are never owned, and nothing gates on them.)
- Build scenes: `Test_track_1`, `Race_track_1`, `SpeedTrack`, `Dragstrip`, `Custom_track`, `CustomPhotoLocation`,
  `photoLocation1-4`; no scene for FunTrack or OffroadTrack.
- Map: `MapDestinationID` RaceTrack 3 → `Race_track_1`, SpeedTrack 8 → `Speedtrack`, WorkshopMaps 9 → `Custom_track`,
  Dragstrip 11 → `Dragstrip` (`DisableDragstripButton` hides it).
- Runtime run 1: both trips refused by the guard (`[Guard] Blocked Scene:RaceTrack`, `Scene:SpeedTrack`).
- Runtime run 2 (guard opened):
  - race track: `RaceTrackManager`, `CarDrive`, `PrepareCarPhysics` with the car; `laps = 1`, `BestRaceTime = 0`;
  - speed track: `FreeTrackManager`; **`GameScript.CurrentSceneType` is `TestTrack`**, so the client logged
    "SpeedTrack ready as TestTrack" and started a drive stream for car -1;
  - no away claim on either trip (B's `away` empty), no drive state reached B on the race track;
  - mileage reached the car: 0 → 4 km after two 2.5 km drives (stored by the server).

## 6. Garage customization

- `GarageCustomizationWindow` (WindowID 42) edits `CMS.Garage.Customization.GarageLookManager.sections`
  (`GarageLookSection { Name, RequiredUpgrade, RequiredUpgrade2, RendererData[], ProjectMaterials[], cameraPoints[],
  SelectedMaterialIndex }`) and `TexturePackManager` (texture packs, `ModType.TexturePack`). Saved as
  `ProfileData.garageCustomizationData { int[] MaterialIndexes; string CurrentTexturePack }` by `GarageLoader.Save`;
  loaded by `GarageLookManager.Load()` (coroutine) and `TexturePackManager.Load()`, both called in `CustomLoad`.
- No decoration types in the build (no class, window or save field for placeable items).
- Runtime: 41 sections (interior floors and walls, lifts, tire changer, balancer, lockers, cabinets, exterior walls,
  gates, engine room, path test, paint shop, car wash and dyno areas, one decal section with 330 renderers), 1–26
  materials each, all `SelectedMaterialIndex = -1` in a session profile; no texture packs installed.
- Runtime: `SetMaterialIndexForSection(2, 3)` then `UpdateMaterials(2, false)` throws `IndexOutOfRangeException`
  (with and without the cached material list, which held 5 entries); the index is stored, the renderer is flagged
  replaced, the material does not change. Corrected by the row 28 review and spike: `UpdateMaterials(int, bool)`
  ignores the section's index and passes the manager's `currentMaterialIndex`, which is 0 after `Init`, so the
  renderer code read `ProjectMaterials[-1]`; `cachedMaterialsList` is only a scratch list for `GetSharedMaterials`.
- Row 28 spike (`20261009-212145_L1_garage-look-probe`, headless): the per-section coroutine
  `UpdateMaterials(RendererData, i, k + 1, restore)` sets a material and restores the default (`restore: true`), and
  `SelectedMaterialIndex` follows (the per-renderer coroutine calls `SetMaterialIndexForSection`), so
  `GarageLookManager.Save` writes the same indexes into the profile. Times: 2 sections 0.4 s, 1 restore 0.07 s,
  all 41 sections to material 0 28.6 s (the 330-renderer decal section loads one material per renderer with
  `Resources.LoadAsync`, at least one frame each), all 41 back to default 2.7 s. `SetActiveTexturePack` with an
  unknown id and `SetDefaultTexturePack` throw nothing (no pack installed; the default pack's id is `Default`).
  `ShowGarageCustomization` (`<ShowGarageCustomization>d__73`) disables input and fades out in its first step, so
  the gate is a `GameScript.ClickIO` prefix for `#garageLook` (its only caller).

## 7. Steam stats

All `stat_*` users (`dxref.py`): job end (`stat_finish_order`, `stat_bonus_exp`, `stat_bonus_money`,
`stat_finish_allmissions` in `EndJobCoroutine`), `stat_level` (`AddPlayerExp`), buying (`stat_buy_parts`,
`stat_buy_carsalon/_carjunkyard/_barn`), selling (`stat_sell_car`, `stat_sell_fix_car`, `stat_sell_junk`), workshop
(`stat_fix_parts`, `stat_fix_body`, `stat_paint_car`, `stat_unscrew`, `stat_shed_oil`, `stat_wheels_balanced`,
`stat_swap`), places (`stat_visit_junkyard/_barn/_salon`, `stat_finish_testtrack`, `stat_finish_testpath`,
`stat_timeattack` in `RaceTrackManager.LastTime`), upgrades (`stat_full_garage`, `stat_unlock_allupgrade`,
`stat_unlock_parking`), auction (`stat_win_car`), drag racing (`stat_dragrace_*`). Method names:
`CMS.Platforms.PlatformManager.IncrementStat`, `CMS.Platforms.Steam.SteamAchievements.IncrementStat`,
`CMS.Platforms.Base.BaseAchievements.IncrementStat`.
