# Tasks

Prerequisites: `sync-workshop-machines` (framework, harness `ToolsCommands.cs`, `Wait-HarnessDumpsEqual`),
`sync-car-parts` and `sync-car-details` are merged. Group 10 also waits for ROADMAP row 13. Each tool group can be
merged on its own once its scenario step passes.

## 1. Spikes and dependencies

- [ ] 1.1 Re-check the merged `sync-car-parts` and `sync-car-details` against the names in design.md Context/D1–D2
      (`CarPartsSync.MarkDirty`/`RebuildRegistry`/`UploadBaseline`, `CarDetailsSync.MarkDirty`/`FlushNow`,
      `CarDetailSection`). Done when design.md names only types that exist.
- [ ] 1.2 Check that `sync-car-parts` provides what D1–D2 need: engine out/in via `NotificationCenter.ActionUnMountGroup` /
      `ActionInsertEngineToCar` is a `CarPartsChange`; `EngineSwap` in every baseline with replay on other clients and
      late joiners (or the "swap blocked while connected" fallback); and whether row 13 has a dyno result sender yet.
      Extend `tool-trace` with this change's hooks and record in design.md which fire from the UI. Copy the
      interior-detailing field split found by `sync-car-details`' probe (its task 1.2) into D1. Done when each item is
      confirmed or recorded as a gap in `QUESTIONS.md`, with the dependent tasks (5.x, 10.x) marked parked.

## 2. Core packets

- [ ] 2.1 Append Welder, CarWash, InteriorDetailing, InteriorDetailingStationary, OilBin, EngineCrane, Paintshop, Dyno
      to `ModToolId`; add `ToolActionKind` and `ToolActionPacket` to `ToolPackets.cs`, appended at the end of
      `PacketTypes`. Done when `PacketRouter` registers it (log line on server start).

## 3. Server

- [ ] 3.1 `ToolHandlers.cs`: relay `ToolAction` to the other clients, nothing stored. Done when two clients' packets
      produce the relay log line.

## 4. Client framework and harness

- [ ] 4.1 `ToolAction` handler through `ClientScene.GarageBound`: play `GarageTool.particles`/`sfx` or
      `PaintshopManager.particleSystem` at the car, never the tool's work method; count per kind for the dump. Done when
      the client builds.
- [ ] 4.2 Extend `StateDump.Full()` with `toolActionsSeen` (count per received `ToolActionKind`, not compared) and
      `lifterButtonsEnabled` per loader. Create `tools-car-effects.ps1` with the connect preamble and an empty step
      list. Done when `Run-Session.ps1 -Scenario tools-car-effects` runs to the end and passes.

## 5. Engine crane

- [ ] 5.1 Hook `CarLoader.UseEngineCrane()` (postfix → `ToolAction(EngineOut)`) and `NotificationCenter.InsertEngineToCar(GroupItem)`
      (prefix → `EngineIn`, or `EngineSwap` per D2). Remote: effect only. The part and inventory change is row 1's
      transaction (1.2). Done when the client builds.
- [ ] 5.2 Engine swap per D2: after the actor's swap, `CarPartsSync.RebuildRegistry` + `UploadBaseline`; row 1 stores
      `EngineSwap` and replays it. If 1.2 found no replay: block a different engine in `InsertEngineToCar` while
      connected with an info window. Done when a swapped car's `cars` dump is equal on both clients and on a late
      joiner (or the block message appears and nothing changes).
- [ ] 5.3 Add `tool-engine-out <loader>` and `tool-engine-in <loader> <uid>`. `tools-car-effects` steps: out → equal
      `cars` and `inventory`, one engine group; in → equal; swap → equal. Done when the steps pass.

## 6. Paint shop (car)

- [ ] 6.1 `MakeCarPaintEffects()` prefix → `ToolAction(PaintCar, loader of PaintshopManager.carLoader)` and a watcher
      that calls `CarDetailsSync.MarkDirty(carLoader, Paint | BodyCosmetics | BonusParts)` + `FlushNow` when
      `IsPainting` turns false. Remote: `particleSystem` only. Done when the client builds.
- [ ] 6.2 Add `tool-paint-car <loader> <r,g,b>`. Step: car paint equal in `cars`; B's `toolActionsSeen.PaintCar` is 1.
      Done when the step passes.

## 7. Car wash and interior detailing

- [ ] 7.1 Hook `CarWashLogic.DoWorkAnim(CarLoader)`, `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)` and
      `ToolsMoveManager.UseInteriorDetailingToolkitStationary()` (prefix → `ToolAction` + watcher). Results per D1
      (car wash: `CarDetailsSync.MarkDirty(…, BodyCosmetics)` + `FlushNow`; interior: `CarPart` dust via
      `CarDetailsSync.MarkDirty(…, BodyCosmetics)`, part condition and `PartScript` dust via `CarPartsSync.MarkDirty`).
      Remote: particles and SFX only. Done when the client builds.
- [ ] 7.2 Add `tool-use <CarWash|InteriorDetailing|InteriorDetailingStationary> <loader>`. Steps: equal `cars`
      dirt/interior fields after the effect; B's `toolActionsSeen` is 1 each; B stays `playable`; `stats` money equal.
      Done when the steps pass.

## 8. Oil bin

- [ ] 8.1 Hook `CarLoader.UseOilbin()` (prefix → `ToolAction(DrainOil)`; a watcher calls `CarDetailsSync.FlushNow` after
      the drain; the value travels through row 4's Fluids poll). Remote: effect only. Done when the client builds.
- [ ] 8.2 `tools-car-effects` step `tool-use OilBin <loader>` → equal fluid fields in `cars`. Done when it passes.

## 9. Welder

- [ ] 9.1 Hook `WelderLogic.DoWorkAnim(CarLoader)` (prefix → `ToolAction(Weld)` + watcher → `CarPartsSync.MarkDirty`
      for the welded body parts). Remote: particles and SFX only, no `StartAnim`. Done when the client builds.
- [ ] 9.2 `tools-car-effects` step `tool-use Welder <loader>` → equal body condition in `cars`; B stays `playable` and
      B's lifter buttons are enabled (`lifterButtonsEnabled` for that loader). Done when the step passes.

## 10. Dyno (waits for ROADMAP row 13, which stores dyno results)

- [ ] 10.1 Hook `CarLoader.MeasurePower()` (postfix; fallback `DynoManager.CloseDyno()`) → row 13's dyno result send.
      No remote dyno run. Done when the client builds.
- [ ] 10.2 Add `tool-dyno <loader>` (calls `MeasurePower()` on a car at `CarPlace.Dyno`). Step: equal dyno fields in
      `cars`. Done when the step passes.

## 11. Two-instance verification

- [ ] 11.1 Run `tools/test-env/Run-Session.ps1 -Scenario tools-car-effects` with instances A and B against the local
      server, plus the full regression run. It must report PASSED with equal `stats`, `inventory` and `cars` sections.
      Attach the run folders to the PR description.
- [ ] 11.2 Add a late-join step to `tools-car-effects`: after A's welding, washing and engine swap, B reconnects →
      equal `cars` (results came through rows 1/4). Done when the scenario passes.
