# Tasks

> **Read first (2026-10-06):** `docs/spikes/workshop-car-tools.md` (static decompile). It corrects hooks in design.md that never fire (inlined builders, shared native bodies) and lists the runtime checks still needed.

Prerequisites: `sync-workshop-machines` (framework, harness `ToolsCommands.cs`, `Wait-HarnessDumpsEqual`),
`sync-car-parts` and `sync-car-details` are merged. Group 10 also waits for ROADMAP row 13. Each tool group can be
merged on its own once its scenario step passes.

## 1. Spikes and dependencies

- [x] 1.1 Re-check the merged `sync-car-parts` and `sync-car-details` against the names in design.md Context/D1–D2
      (`CarPartsSync.MarkDirty`/`RebuildRegistry`/`UploadBaseline`, `CarDetailsSync.MarkDirty`/`FlushNow`,
      `CarDetailSection`). Done when design.md names only types that exist.
      **2026-10-06:** row 4 has `CarDetailsSync.MarkDirty(CarLoader, CarDetailSection)` only (no part list, no
      `FlushNow`: dirty sections flush after 0.5 s) and does not sync `BonusParts` (the enum value exists, no reader or
      apply). design.md "Code (2026-10-06)" lists the names the code uses.
- [ ] 1.2 **In code (2026-10-06):** needs a game run. Check that `sync-car-parts` provides what D1–D2 need: engine out/in via `NotificationCenter.ActionUnMountGroup` /
      `ActionInsertEngineToCar` is a `CarPartsChange`; `EngineSwap` in every baseline with replay on other clients and
      late joiners (or the "swap blocked while connected" fallback); and whether row 13 has a dyno result sender yet.
      Extend `tool-trace` with this change's hooks and record in design.md which fire from the UI. Copy the
      interior-detailing field split found by `sync-car-details`' probe (its task 1.2) into D1. Done when each item is
      confirmed or recorded as a gap in `QUESTIONS.md`, with the dependent tasks (5.x, 10.x) marked parked.
      Static answers: row 1's `EngineCraneHooks` makes out/in one transaction and refuses a different engine while
      connected (toast), and the receivers never apply `EngineSwap`, so the D2 fallback is in force; row 13 has
      `DynoSync.Commit(CarLoader)` (branch `change/sync-test-drive-and-diagnostics`); interior detailing touches only
      `CarPart`s (spike finding 6). `tool-trace on` now logs every car tool action, end and whole-car wash. Left for the
      game run: which hooks fire from the UI.

## 2. Core packets

- [ ] 2.1 **In code (2026-10-06):** needs a game run. Append Welder, CarWash, InteriorDetailing, InteriorDetailingStationary, OilBin, EngineCrane, Paintshop, Dyno
      to `ModToolId`; add `ToolActionKind` and `ToolActionPacket` to `ToolPackets.cs`, appended at the end of
      `PacketTypes`. Done when `PacketRouter` registers it (log line on server start).

## 3. Server

- [ ] 3.1 **In code (2026-10-06):** needs a game run. `ToolHandlers.cs`: relay `ToolAction` to the other clients, nothing stored. Done when two clients' packets
      produce the relay log line. The machine handlers now accept only machine ids (`ModTools.IsMachine`).

## 4. Client framework and harness

- [ ] 4.1 **In code (2026-10-06):** needs a game run. `ToolAction` handler through `ClientScene.GarageBound`: play `GarageTool.particles`/`sfx` or
      `PaintshopManager.particleSystem` at the car, never the tool's work method; count per kind for the dump. Done when
      the client builds. (`Logic/Tools/CarTools/CarToolActions.cs`; no effect while the local player runs the same tool.)
- [ ] 4.2 **In code (2026-10-06):** needs a game run. Extend `StateDump.Full()` with `toolActionsSeen` (count per received `ToolActionKind`, not compared) and
      `lifterButtonsEnabled` per loader. Create `tools-car-effects.ps1` with the connect preamble and an empty step
      list. Done when `Run-Session.ps1 -Scenario tools-car-effects` runs to the end and passes.

## 5. Engine crane

- [ ] 5.1 **In code (2026-10-06):** needs a game run. Postfixes on `NotificationCenter.ActionUnMountGroup` (→ `ToolAction(EngineOut)`) and
      `NotificationCenter.InsertEngineToCar(GroupItem)` (→ `EngineIn`), both only when the original ran
      (`__runOriginal`; row 1's prefixes refuse while a car is loading or claimed). `UseEngineCrane` is not hooked: its
      coroutine can still stop at a warning. Remote: effect only (the crane has no particles or sound). The part and
      inventory change is row 1's transaction (1.2). Done when the client builds.
- [ ] 5.2 **In code (2026-10-06):** needs a game run. Engine swap per D2: row 1 found no replay, so the fallback is in force: row 1's
      `InsertEngineToCar` prefix refuses a different engine while connected (toast, not an info window). No
      `RebuildRegistry`/`UploadBaseline` path is written. Done when the block message appears and nothing changes.
- [ ] 5.3 **In code (2026-10-06):** needs a game run. Add `tool-engine-out <loader>` (the real `UseEngineCrane` path; it reports the blockers
      `d__37` would refuse on, and the scenario falls back to row 1's `crane-out`) and
      `tool-engine-in <loader> <uid|swap>`. `tools-car-effects` steps: out → equal `cars` and `inventory`, one engine
      group; in → equal; swap → refused and equal. Done when the steps pass.

## 6. Paint shop (car)

- [ ] 6.1 **In code (2026-10-06):** needs a game run. Postfix `PaintshopManager.SubmitColor` with `PaintshopType.Garage` →
      `ToolAction(PaintCar, loader of PaintshopManager.carLoader)` (`MakeCarPaintEffects` never fires, spike finding 1).
      The result is row 4's own `SubmitColor` postfix (`Paint | BodyCosmetics`; `BonusParts` is not synced by row 4).
      The "wash the car first?" answer of the paint shop and tint windows (`EnableDust`/`SetWashFactor` with
      `part == null`) now marks `BodyCosmetics`. Remote: `particleSystem` and the `CarPaint` SFX only. Done when the
      client builds.
- [ ] 6.2 **In code (2026-10-06):** needs a game run. Add `tool-paint-car <loader> <r,g,b>`. Step: car paint equal in `cars`; B's `toolActionsSeen.PaintCar` is 1.
      Done when the step passes. (The paint is compared through `cardetails-show`.)

## 7. Car wash and interior detailing

- [ ] 7.1 **In code (2026-10-06):** needs a game run. Prefixes on `CarWashLogic.DoWorkAnim(CarLoader)` and
      `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)` → `ToolAction`; the commit point is the postfix of each
      `_DoWorkAnim_d__1.MoveNext` returning false (spike finding 2, no watcher). Car wash:
      `CarDetailsSync.MarkDirty(…, BodyCosmetics)`, marked again 1 s later; interior: `CarPartsSync.MarkDirty` for
      `details` and the seats (condition, dent) and `CarDetailsSync.MarkDirty(…, BodyCosmetics)` (dust). The stationary
      kit is told apart by the car standing at `CarPlace.CarWash`, not by a `UseInteriorDetailingToolkitStationary`
      hook. Remote: particles and SFX only. Done when the client builds.
- [ ] 7.2 **In code (2026-10-06):** needs a game run. Add `tool-use <CarWash|InteriorDetailing|InteriorDetailingStationary> <loader>`. Steps: equal `cars`
      dirt/interior fields after the effect; B's `toolActionsSeen` is 1 each; B stays `playable`; `stats` money equal.
      Done when the steps pass. (`tool-use` starts `DoWorkAnim` itself, so no fee is charged.)

## 8. Oil bin

- [ ] 8.1 **In code (2026-10-06):** needs a game run. Hook `ToolsManager._UseOilDrain_d__40.MoveNext` (`UseOilbin` builds it inline): the first step that
      yields → `ToolAction(DrainOil)`, so a drain that stops at once (no oil, no plug) sends nothing; the last step →
      `CarDetailsSync.MarkDirty(…, Fluids)` (row 4's poll would see it too). Remote: count only (the drain's loop
      SFX and plug animation are not replayed). Done when the client builds.
- [ ] 8.2 **In code (2026-10-06):** needs a game run. `tools-car-effects` step `tool-use OilBin <loader>` → equal fluid fields in `cars`. Done when it passes.

## 9. Welder

- [ ] 9.1 **In code (2026-10-06):** needs a game run. Hook `WelderLogic.DoWorkAnim(CarLoader)` (prefix → `ToolAction(Weld)`; postfix of
      `_DoWorkAnim_d__1.MoveNext` returning false → `CarPartsSync.MarkDirty` for `body` and `details`). Remote:
      particles and SFX only, no `StartAnim`. Done when the client builds.
- [ ] 9.2 **In code (2026-10-06):** needs a game run. `tools-car-effects` step `tool-use Welder <loader>` → equal body condition in `cars`; B stays `playable` and
      B's lifter buttons are enabled (`lifterButtonsEnabled` for that loader). Done when the step passes.

## 10. Dyno (waits for ROADMAP row 13, which stores dyno results)

- [ ] 10.1 **In code (2026-10-06):** needs a game run and row 13. Postfix `CarLoader.MeasurePower()` → row 13's `DynoSync.Commit`
      (`Logic/Tools/CarTools/DynoMeasureHooks.cs`, compiled only with `SYNC_TEST_DRIVE` until this branch is rebased
      onto row 13; then remove the `#if`). It covers the map's "measure power" only; the garage dyno run is row 13's
      `CloseDyno` hook. No remote dyno run. Done when the client builds.
- [ ] 10.2 **In code (2026-10-06):** needs a game run and row 13. Add `tool-dyno <loader>` (calls `MeasurePower()`). Step: equal dyno fields in
      `cardetails-show` (row 13's `Dyno` section). Done when the step passes.

## 11. Two-instance verification

- [ ] 11.1 Run `tools/test-env/Run-Session.ps1 -Scenario tools-car-effects` with instances A and B against the local
      server, plus the full regression run. It must report PASSED with equal `stats`, `inventory` and `cars` sections.
      Attach the run folders to the PR description.
- [ ] 11.2 **In code (2026-10-06):** needs a game run. Add a late-join step to `tools-car-effects`: after A's welding, washing and engine swap, B reconnects →
      equal `cars` (results came through rows 1/4). Done when the scenario passes.
