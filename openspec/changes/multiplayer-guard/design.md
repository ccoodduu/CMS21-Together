# Design

## Context

See proposal.md — Why. Split from `desync-detection-and-resync` (ROADMAP row 14 (a)) on 2026-10-06; decisions keep
their numbers D1–D6. Observed in the code and the game stubs (`%USERPROFILE%\CMS21-TestInstalls\decomp`):

- **Windows**: `CMS.UI.WindowManager` (firstpass) — `bool Show(WindowID, bool force = false)`,
  `bool Show(WindowID, Il2CppReferenceArray<Object> args)`, `ShowAfterFrame`, `ShowCoroutine`,
  `ShowAfterWindowClose(WindowID, WindowID)`, `EnableWindowOpening`/`DisableWindowOpening`/`EnableAllWindowsOpening`,
  `IsWindowActive`, `HideWindowOnTop`. `WindowID` has 73 values (`Map`, `Orders`, `Shop`, `Warehouse`, `PauseQuit`,
  `WheelBalance`, `Paintshop`, `Parking`, `Dyno`, `PathTest`, `Photo`, `CaseOpening`, …). Base class
  `CMS.UI.Windows.Base.Window` has its own `Show()`; whether anything calls it bypassing the manager is unknown.
  The game's own `DisableWindowOpening` is not usable as the guard: `CustomLoad` and the game call
  `EnableWindowOpening`/`EnableAllWindowsOpening` themselves.
- **Pie menu**: `PieMenuController` — `PreparePieMenu()`, `ReadOptionsFromIni(string)`, `optionsId`
  (`Il2CppStringArray`), `CurrOption`, `CheckSelectedOption()`, `GetOnClick(string id)` (35 compiler lambdas
  `_GetOnClick_b__72_N`), `GetIsAvailable(string)`, `SetEnableOption(string, bool)`, `Close()`; machine menus come from
  `PieMenuHelper.GetIniEntryForMachine(IOSpecialType, out string)`. `IOSpecialType`: `MainGate, WheelBalancer,
  TireChanger, SpringClamp, EngineStand, CarBatteryCharger, RepairTable, Junk, Paintshop, Jukebox, Welder,
  InteriorDetailingToolkit, Oilbin, EngineCrane, EngineCraneMount, GarageLook, HeadlampAlignmentSystem,
  WheelsAlignmentSystem, BrakeLathe, BonusPart, PathTest, CarWash, InteriorDetailingToolkitStationary, WindowTint`.
- **Game modes**: `GameMode.SetCurrentMode(gameMode)`, `GetCurrentMode()`, `BlockModeChange`; `gameMode` = `Garage,
  GarageDisassemble, GarageAssemble, InteriorDisassemble, InteriorAssemble, Interior, PartSelect, UI, PhotoMode,
  PartMount, PartUnMount, GroupUnMount, GroupMount, PartSelectMount, ExamineCondition, CarDrive, PathTest,
  ExamineTools, Dyno, Benchmark, ExamineGarage, BonusAssemble, BonusDisassemble, DrainTool, None`.
- **Scenes**: `NotificationCenter` — `IEnumerator SelectSceneToLoad(string, SceneType, bool useFader, bool saveGame)`
  (patched today by `DisconnectHooks` and planned by row 6's scene prefix), `void SelectSceneToLoad(string, SceneType,
  bool)`, `void StartSelectSceneToLoad(string, SceneType, bool, bool)`. `SceneType`: `Junkyard, Barn, Showroom,
  Garage, Auction, TestTrack, RaceTrack, Salon, Parking, Menu, FunTrack, OffroadTrack, DragStrip, CustomTrack,
  Tutorial, PhotoLocation, SpeedTrack`.
- **Messages**: `UIManager.Get().ShowPopup(string title, string text, PopupType)` (non-modal; `PopupType.Normal`),
  `ShowInfoWindow(string)` (modal, goes through `WindowManager`).
- **Saves and pause**: `GarageLoader.Save(bool)` and `GameDataManager.Save(int)`; `PauseQuitWindow.CreateSaveButton`,
  `CreateSaveAndQuitButton`, `CreateQuitButton`, `HideWithoutChangeMode`. The user's AutosaveMod calls
  `GarageLoader.Save(false)` on a timer. Row 7 blocks `GameDataManager.Save(int)` and the profile methods while
  `SessionGuard.Active`.
- Row 7: `SessionGuard.Active` (client, from `StartGame` until the menu has loaded; the class name `SessionGuard` is
  row 7's, so this change's class is `FeatureGuard`). Row 6: `ClientScene.LocalScene` and the scene prefix on the
  coroutine `SelectSceneToLoad` (`SceneHooks`, formerly `DisconnectHooks`). Row 8: `ModNotify`.

## Goals / Non-Goals

**Goals:** no unsynced feature reachable while connected, through one rule table that every row extends when it
lands. Nothing changes outside a session.

**Non-Goals:** syncing blocked features; reconciliation, the resync key and bug reports (row 14,
`desync-detection-and-resync`, which uses `FeatureGuard.Bypass` and the guard log).

## Decisions

### D1. One decision function, four hook kinds

`FeatureGuard.Decide(GuardKind kind, string id)` → `Allow | Block`, where `GuardKind = Window | Pie | Mode | Scene`
and `id` is the enum name or the pie option id (`Window:Map`, `Pie:<optionId>`, `Mode:GarageDisassemble`,
`Scene:Junkyard`). It returns `Allow` when the guard is inactive (`!SessionGuard.Active || !Client.Instance.IsConnected`),
when the call comes from the mod itself (`FeatureGuard.Bypass` scope, used by row 14's resync reload and row code that
opens windows or changes modes to apply a remote change), or when the rule table plus overrides allow `kind:id`.
Harness verbs that act as the player (`travel`, `part-unmount`, `tool-put`, …) are not bypassed, so every row's
scenario exercises its own guard entries; scenarios that need a feature not yet allowed use `guard-allow`. Every block is logged once per
id per 10 s and kept in a 200-entry ring buffer (row 14's bug report, `guard-log`). Rejected: one patch per known feature (the
roadmap's reason for this row: that list never covers the next feature); `DisableWindowOpening` (the game re-enables
windows itself).

### D2. Hook points and how each is cancelled

| Kind | Hook | Cancel |
|---|---|---|
| Window | prefix on both `WindowManager.Show` overloads | `__result = false`, return false |
| Pie | postfix on `PieMenuController.PreparePieMenu()`: `SetEnableOption(id, false)` for blocked ids (shown locked); prefix on `CheckSelectedOption()` reading `optionsId[CurrOption]` | popup, `Close()`, return false |
| Mode | prefix on `GameMode.SetCurrentMode(gameMode)` | return false (mode stays) |
| Scene | prefix on `StartSelectSceneToLoad` and the 3-argument `SelectSceneToLoad` (void); prefix on the coroutine `SelectSceneToLoad(string, SceneType, bool, bool)` | void: return false; coroutine: `__result` = an injected empty Il2Cpp `IEnumerator` (`ClassInjector`), return false |

- Order of prefixes on the same method: the guard uses `[HarmonyPriority(Priority.First)]`; every other prefix of this
  mod on a guarded method (today `DisconnectHooks`, which row 6 part 1 turns into `SceneHooks` before the guard lands
  in M1) takes `bool __runOriginal` and does nothing when it is false. This works whether or not the bundled HarmonyX skips later prefixes.
- Task 1.1 (trace) decides three things before enforcement: whether `ShowAfterFrame`/`ShowCoroutine`/
  `ShowAfterWindowClose` and direct `Window.Show()` calls reach `WindowManager.Show` (else they get the same prefix);
  whether `CheckSelectedOption` is the only path that runs a pie action (else the prefix moves to the
  `_GetOnClick_b__72_N` lambdas named by the trace); and whether blocking each denied mode in `SetCurrentMode` leaves the
  game usable. A mode whose block leaves the game stuck becomes log-only and is blocked at its pie option instead.
- Scene cancel: if the injected empty enumerator is not accepted by `StartCoroutine` (spike in task 1.1), the
  coroutine prefix becomes log-only and travel is blocked one step earlier at its windows (`Map`, `Parking`, `Showroom`,
  `Auction`, `PathTest`) and pie options (test drive). The guard scenario checks both.

### D3. Message

`ModNotify.Message("Multiplayer", "<feature> is not supported in multiplayer yet")` — row 8's notification API
(`hosting-and-join-ui` D1, task 1.3, M1), which uses `UIManager.Get().ShowPopup(title, text, PopupType.Normal)` in
garage scenes. If the guard lands before row 8 group 1, it calls `ShowPopup` from one private helper that row 8 then
redirects to `ModNotify`. At most one message per 2 s; the feature name comes from the rule table (fallback: the id).
A non-modal popup avoids a modal window that the guard itself would have to allow and that would steal input.

### D4. Rule table (`Guard/GuardRules.cs`, one entry per line)

`GuardRule(Kind, Id, Owner, Label)`. Owner `base` = always allowed; `M1` = synced in Dev or by M1 rows; a row number
= allowed once that row is merged (the row adds the line in its own merge commit; nothing is allowed ahead of its row).
Initial content (ids not yet known are filled by task 1.1):

| Owner | Allowed |
|---|---|
| base | Scene `Menu`, `Garage`; Window `AskWindow`, `InfoWindow`, `InputWindow`, `PieMenu`, `PauseQuit`, `Settings`, `Tutorials`, `Changelog`, `Radio`; Mode `Garage`, `UI`, `None`; Pie: the pie menu's top level and Jukebox/radio options |
| M1 (Dev-synced: shop, inventory, warehouse, exchange, upgrades, stats) | Window `Inventory`, `Sorting`, `Warehouse`, `WarehouseChange`, `Shop`, `ShopBuy`, `ShopList`, `ItemsExchange`, `SellPerCondition`, `TakenItems`, `Upgrades`; the pie options that open them |
| row 1 | Modes `GarageDisassemble`, `GarageAssemble`, `PartSelect`, `PartMount`, `PartUnMount`, `GroupMount`, `GroupUnMount`, `PartSelectMount`, `InteriorDisassemble`, `InteriorAssemble`, `Interior`, `ExamineCondition`, `ExamineGarage`; Window `PartInspector`, `CarInfo`; car pie options for parts; `EngineCrane`/`EngineCraneMount` options (crane out/in) |
| row 2 | Window `CarLocationWindow`, `Parking`, `ParkingManagement` (garage parking only); lifter and car-move pie options |
| row 3 | Window `Orders`, `ExamineReport` (job check) |
| row 4 | Window `WheelsAlignment`, `LampAlignment`, `Tune`, `Tinting`, `CarVersion`; Mode `BonusAssemble`, `BonusDisassemble`, `DrainTool`; machines `WheelsAlignmentSystem`, `HeadlampAlignmentSystem`, `BonusPart`, `WindowTint` |
| row 5a | Window `WheelBalance`, `RepairPart`, `BrakeLathe`, `ChoosePartUp`; machines `TireChanger`, `WheelBalancer`, `SpringClamp`, `EngineStand`, `CarBatteryCharger`, `RepairTable`, `BrakeLathe` |
| row 5b | Window `Paintshop`; machines `Paintshop`, `CarWash`, `InteriorDetailingToolkit`, `InteriorDetailingToolkitStationary`, `Oilbin`, `Welder` |
| row 1 (M2: garage cars come back from the snapshot after a trip; ROADMAP M2 "travel to the junkyard (parts only)") | Window `Map`; Scene `Junkyard` (parts only — the junkyard's car-buy option stays blocked until row 6 part 2) |
| row 6 part 2 | Scenes `Barn`, `Auction`, `Showroom`, `Salon`, `Parking` (row 2 blocks the Parking scene's take-out itself); Windows `Showroom`, `SalonSelectCar`, `SalonWizard`, `Auction`; junkyard car buy; Mode `CarDrive` (seat) |
| row 10 | Window `CaseOpening`, `Scrap`, `ScrapPerCondition`, `ShopLicenseBuy` (until audited) |
| row 13 | Window `PathTest`, `Dyno`, `Benchmark`; Modes `PathTest`, `Dyno`, `Benchmark`, `ExamineTools`; Scene `TestTrack`; machine `PathTest` |
| never (backlog) | Scenes `RaceTrack`, `FunTrack`, `OffroadTrack`, `DragStrip`, `CustomTrack`, `SpeedTrack`, `PhotoLocation`, `Tutorial`; Windows `NewSaveWindow`, `SaveDetails`, `RevertBackup`, `GarageCustomization`, `Drag*`, `Demo*`, `Tutorial`, `TutorialEnd`, `CreateEngine`, `ChooseEngine` (until row 1's engine swap lands); machine `GarageLook` |

`Photo`/`PhotoMode`, `Map` while staying in the garage, `MainGate` and `Junk` are decided by the audit (D6). The table
is the single source; `guard-rules` prints it, and ROADMAP's integration notes get the rule "a row adds its guard
entries in its merge commit".

### D5. Configuration and harness

MelonPreferences category `CMS21Together_Guard`: `Mode = Enforce | LogOnly | Off` (default `Enforce`), `Allow` and
`Deny` (`;`-separated `Kind:Id` lists, `Deny` wins). Logged at session start. Harness verbs (in memory, for one run):
`guard-set <enforce|logonly|off>`, `guard-allow <Kind:Id>`, `guard-try <Kind:Id>` (runs the real entry point: shows the
window / sets the mode / starts the scene load / selects the pie option, and returns `allowed`/`blocked`), `guard-log`,
`guard-rules`. Row 6's travel scenarios use `guard-allow Scene:<name>` (row 6 review question 1).

### D6. Single-player assumption audit (task group 3)

Each item is checked in a two-instance session; the outcome (handled / blocked / known gap + owner) is written into
this table by task 3.1:

| Assumption | Expectation to verify | Planned handling |
|---|---|---|
| Pause menu pauses the game (`Time.timeScale`, `GameScript.canCountTime`) | Opening `PauseQuit` must not stop the local simulation while others act | Postfix on the window's show path restores `timeScale = 1` while connected, if the game sets it |
| Pause menu save buttons | Row 7 blocks the write, but the button suggests a save | Prefix `CreateSaveButton`/`CreateSaveAndQuitButton` → skip while connected; quit stays |
| `Time.timeScale` writes elsewhere (windows, photo mode, car physics) | Find every writer (`GlobalData`, `CarLoader`, `PrepareCarPhysics`, …) by a setter trace | Block or reset per writer |
| Camera modes (examine/inspection, photo, `ExamineTools`, orbit camera) | Local-only view or does it change state? | Allowed if local-only (Photo likely), else mode rule |
| Autosave by the game (`GarageLoader.Save`) and by mods (AutosaveMod) | Does `GarageLoader.Save(bool)` end in `GameDataManager.Save(int)` (blocked by row 7)? | If not: tell row 7 to add `GarageLoader.Save` to its blocked list (review.md) |
| Modal windows over remotely changed data (shop/warehouse/inventory open while another player changes it) | Stale list or exception? | Known gap per owning row, or refresh hook |
| Game-over/idle timers, order timers counting while a window is open | Run while a window is open | Note to row 3 |
| Steam achievements/stats outside jobs | Which fire locally | Note to row 3/10 |

### What the guard stores

Nothing on the server. Client: preferences and the in-memory ring buffer.

### Late join

Nothing to replay: the guard is client-only and decides per call from `SessionGuard.Active` and the connection. A late
joiner is guarded from `StartGame` on, before its join snapshot arrives.

## Risks / Trade-offs

- [The guard blocks something a row already syncs, or the table is forgotten when a row lands] → the rule table names
  the owner; each row's scenario runs with the guard on `Enforce`; `guard-log` in every scenario's notes.
- [Blocking `SetCurrentMode` mid-transition leaves the game stuck] → trace per mode (task 1.1); fallback log-only + pie
  block.
- [Cancelling the scene coroutine is not possible in IL2CPP] → block at the windows/pie options that start travel.
- [Pie option ids differ per machine ini and are only known at runtime] → trace task dumps them; unknown ids default
  to blocked.
- [The trace needs a manual walk through every window and machine] → needs the user once (like row 5a's machine
  trace); without it the pie-option ids stay unknown and those options stay blocked.

## Migration Plan

No save or server change. Client preferences get defaults (`Mode = Enforce`). Rollback: `Mode = Off` or revert.

## Open Questions

1. Exact pie option ids and which window each opens (task 1.1 fills D4).
2. Whether `GarageLoader.Save(bool)` reaches `GameDataManager.Save(int)` (task 3.1; affects row 7, not this design).

### D6 outcomes (task 3.1, 2026-10-06)

| Assumption | Outcome | Evidence |
|---|---|---|
| Pause menu pauses the game | Handled: the game keeps running behind the pause menu | `guard` (`20261006-094732`): B received A's scrap within 2 s with `PauseQuit` open |
| Pause menu save buttons | Handled: hidden while connected | screenshot `shot_pause_B.png` (Continue, Tutorials, Settings, Return to Menu) |
| `Time.timeScale` writers | No writer found | native decompile: nothing in the game assembly writes `Time.timeScale` (static trace, review.md) |
| Camera modes | Known gap, photo mode allowed (user decision), examine modes blocked by the row 1 rules | runtime walk-through not done; part of the user's M1 playtest checklist |
| Game autosave (`GarageLoader.Save`) | Handled by row 7: `GarageLoader.Save` reaches `GameDataManager.Save`, which `SessionGuard` blocks | client logs: "GarageLoader.Save called during a session" followed by "Blocked GameDataManager.Save(0)" (`20261006-084505_L1_scenes`) |
| Modal windows over remotely changed data | Inventory and warehouse windows refresh on server updates (`InventoryHandlers.Refresh*Window`); the shop list is not refreshed: known gap, owner row 10 (`economy-audit`) | code read |
| Timers while a window is open | Note for row 3 (order expiry runs on the server) | design of row 3 |
| Steam stats outside jobs | `stat_level` is set from `WorldState` (shared level); other stats belong to rows 3/10 | `WorldStatesPackets.HandleWorldState`, native stat map in `docs/spikes/native-decompile.md` |
