# Proposal

## Why

Several workshop tools act on a car rather than holding an item: welder, car wash, interior detailing, oil bin,
engine crane, the paint shop (car paint) and the dyno. Today their result exists only on the acting player's car,
and the old 0.4.x mod replayed the whole tool animation on every client (`DoWorkAnim`), which locked other players'
controls, closed the car and charged or rewarded a second time (FixForTogether `desync_*`). With part state
(`sync-car-parts`), car details (`sync-car-details`) and the machine framework (`sync-workshop-machines`) in place,
these tools only need to trigger the right senders and show the effect to others.

This change is the second half of the former `sync-workshop-tools` (split by user decision 2026-10-05).

## What Changes

- Car-effect tools: welder, car wash, interior detailing (portable and stationary), oil bin, engine crane (effect;
  engine out/in is a part transaction of `sync-car-parts`; engine swap trigger), paint shop (car paint), dyno
  (result trigger). The acting client runs vanilla and, at the tool's commit point, calls the owning sender:
  `CarPartsSync.MarkDirty` (row 1) or `CarDetailsSync.MarkDirty`/`FlushNow` (row 4); dyno results go to ROADMAP
  row 13's sender.
- A relay-only **action event** (`ToolAction`) so other players see particles and sound at the car without running
  the tool.
- Engine swap: the actor's native swap is followed by `CarPartsSync.RebuildRegistry` + `UploadBaseline`; row 1 stores
  `EngineSwap` from the baseline and replays it on other clients and late joiners. If row 1's spike finds no clean
  replay, inserting a different engine is blocked while connected (fallback accepted by the user 2026-10-05).
- Game hooks (all verified in the decompiled stubs): `WelderLogic`/`CarWashLogic`/`InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)`,
  `ToolsMoveManager.UseInteriorDetailingToolkitStationary`, `CarLoader.UseOilbin`/`UseEngineCrane`/`MeasurePower`,
  `DynoManager.CloseDyno`, `NotificationCenter.InsertEngineToCar`, `PaintshopManager.MakeCarPaintEffects`.
- New packet: `ToolAction`. `ModToolId` gains Welder, CarWash, InteriorDetailing, InteriorDetailingStationary,
  OilBin, EngineCrane, Paintshop, Dyno; new `ToolActionKind`.

## Capabilities

### New Capabilities
- `workshop-car-tools-sync`: results of tool work on a car reach every player through part and detail sync, and the
  tool's visible effect is shown to others without a second cost, reward or locked controls.

### Modified Capabilities
- None. There are no main specs yet.

## Impact

- Core: `ToolActionPacket` and `ToolActionKind` in `ToolPackets.cs`, appended `ModToolId` values, new `PacketTypes` entry.
- Server: `ToolAction` relay in `Network/Handlers/ToolHandlers.cs` (no stored state, no save section).
- Client: new `Logic/Tools/CarTools/*` (one small file per tool), `ToolAction` handler (effect only).
- Harness: car-tool verbs in `tools/TestHarness/Features/ToolsCommands.cs`, `toolActionsSeen` dump section, scenario
  `tools-car-effects`.
- Depends on `sync-workshop-machines` (framework: `ToolSync`, `ModToolId`, `ToolsCommands.cs`, `Wait-HarnessDumpsEqual`),
  `sync-car-parts` (`CarPartsSync.MarkDirty`/`RebuildRegistry`/`UploadBaseline`, engine crane group transactions,
  `EngineSwap` storage and replay), `sync-car-details` (`CarDetailsSync.MarkDirty`/`FlushNow`, sections `Paint`,
  `BodyCosmetics`, `BonusParts`), `sync-players-and-scenes` (`ClientScene.GarageBound`) and ROADMAP row 13
  `sync-test-drive-and-diagnostics` (stores dyno results).
