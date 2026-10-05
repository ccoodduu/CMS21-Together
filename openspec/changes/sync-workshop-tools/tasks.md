# Tasks

Order: spikes and dependency checks, shared foundation, then the slot machines (groups 6–15, need only the
inventory), then the car-effect tools (groups 16–21, need `sync-car-parts` and `sync-car-details`), then the
two-instance verification. Each machine group is a small client file on the shared `ToolSync` base plus its harness
steps and can be merged on its own once its scenario step passes. Groups 16–21 are the candidate second change
(see review.md).

## 1. Spikes and dependencies

- [ ] 1.1 Re-check the merged `sync-car-parts` and `sync-car-details` against the names in design.md Context/D7–D9
      (`CarSubPartUpdatePacket`, `PartTransaction`, `InventoryDelta`, `CarPartsSync.MarkDirty`/`UploadBaseline`,
      `CarDetailsSync.MarkDirty`/`FlushNow`, `CarDetailSection`). Done when design.md names only types that exist.
- [ ] 1.2 Check that `sync-car-parts` provides what D7–D9 need: its hooks ignore `PartScript`s outside a car registry;
      `PartTransaction` can be opened for a non-car root; engine out/in via `NotificationCenter.ActionUnMountGroup` /
      `ActionInsertEngineToCar` is a `CarPartsChange`; `EngineSwap` in the per-loader entry with re-baseline; and whether row 13 has a dyno
      result sender yet. Done when each item is confirmed or recorded as a gap in
      `QUESTIONS.md`, with the dependent tasks (11.3, 16.x, 21.x) marked parked. Copy the interior-detailing field split found by `sync-car-details`' probe (its task 1.2) into D8.
- [ ] 1.3 Add a harness `tool-trace on|off` command that logs, in order and with UIDs, every `Inventory.Add/Delete/AddGroup/DeleteGroup`
      (including `Add(List<BaseItem>)`) and every tool method hooked by this change. **Needs the user:** one ~10-minute
      session using each machine through the normal UI in one instance; save the trace under `tools/runs/`. Done when
      design.md D3/D5–D8 record: the put/take call order per slot machine, which hooks fire from the UI (inlining),
      whether a take creates new UIDs, and whether `FinishBalance`/`IncreaseEngineStandAngle`/`BatteryChargerActivate`
      are hit.
- [ ] 1.4 Spike the remote-apply primitives in one instance from the harness (no UI): `SetGroupOnTireChanger(g, true, c)`
      on an occupied changer, `Clear()` coroutines run through `MelonCoroutines` under a hooks-off scope,
      `SetGroupOnEngineStand(g, false)` as a coroutine, `ClearEngineStand()`, `SetEngineStandAngle`, `MoveTo(t, p, false)`.
      Done when design.md D6 notes for each: models not duplicated, inventory unchanged, no player/camera lock.

## 2. Core packets and state

- [ ] 2.1 Add `ModToolId` (TireChanger, WheelBalancer, SpringClamp, EngineStand1, EngineStand2, BrakeLathe, BatteryCharger,
      Welder, CarWash, InteriorDetailing, InteriorDetailingStationary, OilBin, EngineCrane, Paintshop, RepairTable, Dyno),
      `ToolProperty`, `ToolActionKind` and `ToolSlotState` (with `Dictionary<string, CarSubPartUpdatePacket> Parts`)
      in `Core/Data/GameType/`. Done when Core builds.
- [ ] 2.2 Add `ToolPackets.cs` with `ToolSlotUpdatePacket`, `ToolSlotRejectedPacket`, `ToolSlotPropertyPacket`,
      `ToolPartChangePacket`, `ToolPositionPacket`, `ToolActionPacket` and `ToolsStatePacket`, appended at the end of
      `PacketTypes`. Done when `PacketRouter` registers all seven (log line on server start).
- [ ] 2.3 Add `ItemActionType.Update`. Done when Core builds.
- [ ] 2.4 Add `ToolsState { Slots, Positions }` to `ModGameState`. Done when Core builds.

## 3. Server

- [ ] 3.1 Add `Network/Handlers/ToolHandlers.cs` (no own lock; dispatch holds `StateLock`): `ToolSlotUpdate` with CAS on
      `ExpectedUid`, the kind/prefix check and the "UID not on another machine" check, relay on accept,
      `ToolSlotRejected` to the sender on reject; `ToolSlotProperty` and `ToolPosition` stored last-write-wins and
      relayed; `ToolAction` relayed only. A clear wipes `Parts`. Done when two clients' packets produce the expected
      accept/reject log lines.
- [ ] 3.2 Make inventory ADD idempotent by UID in `InventoryHandlers` (item and group) and handle `Update` (replace by
      UID and relay; unknown UID → log and ignore). Done when a duplicated ADD is logged as ignored and the count is unchanged.
- [ ] 3.3 Add `Data/Persistence/WorkshopToolsSection.cs`: `[SessionSection]`, `ISaveSection` (key `workshop-tools`,
      `Version = 1`, thin adapter over `ModGameState.ToolsState`, `Reset()` = empty) and `ISnapshotProvider`
      (`SyncOrder = 300`, `SendSnapshot` sends one `ToolsStatePacket` and returns 1). Done when the server logs the key
      at start, the `connect` log lists `workshop-tools` between `car-placement` and `jobs`, and a hand-filled
      `ToolsState` survives `Send-ServerCommand save` + restart (section `workshop-tools` v1 in `server_save.json`).

## 4. Client framework

- [ ] 4.1 Add `Logic/Tools/ToolSync.cs`: the registry (`ModToolId` → instance via `ToolsManager.Get()`,
      `ToolsMoveManager.Get()` and `Engine_stand_2`), `ClientToolsState` mirror, `ApplyingRemote(tool)` scope released
      at the end of the wrapping coroutine, `NeutralUids`, and a base class with `OnLocalChange` → `ToolSlotUpdate`
      (`ExpectedUid` from the mirror) and `ApplySnapshot`. Sends are gated on initial sync finished and
      `ClientScene.IsGarageReady` (D4). Done when the client builds and `tool-list` lists the instance found for each id.
- [ ] 4.2 In `InventoryHook`, make the four prefixes return `false` (no send, no local change) for UIDs in
      `NeutralUids`; add the before/after inventory snapshot for remote applies (D3.3). In `InventoryHandlers`, skip
      an ADD whose UID is already present and apply `Update` in place. Done when `give-item` of an existing UID leaves
      the inventory unchanged.
- [ ] 4.3 Add `Network/Handlers/ToolHandlers.cs`: update the mirror and apply in the garage; during initial sync queue
      the apply after the snapshot; drop while away from the garage (D12); `ToolSlotRejected` → put or take
      compensation per D3.2. Done when the client builds; behaviour is covered by the machine groups.
- [ ] 4.4 Late join: handle `ToolsStatePacket` (it follows the `inventory` section on the stream; no own synced flag):
      clear every machine the local save loaded (inventory-neutral), apply all slots (`instant = true`) and overlays,
      then positions, then call `SyncTracker.Applied("workshop-tools")` once. Done when `syncAcked` is true in
      `tools-latejoin` and a forced apply failure shows `workshop-tools` in the sync timeout log.

## 5. Harness foundation

- [ ] 5.1 Add `tools/TestHarness/Features/ToolsCommands.cs` with `tool-list`, `tool-trace`, `give-item <id>`,
      `give-group <wheel|engine|shock>` (through `Inventory.Add`/`AddGroup` with hooks on; reuse if another change added
      them), `tool-put <tool> <uid>` and `tool-take <tool>` (inventory call plus the logic method, in the order 1.3
      recorded), `tool-hold on|off` (buffers incoming tool packets before the mirror, to force races deterministically),
      `tool-local-put <tool> <id>` (hooks off; simulates the local save) and `tool-resync` (sends `AskForSync`).
      Done when each command answers from `Send-HarnessCommand`.
- [ ] 5.2 Extend `StateDump.Full()` with `tools` (per `ModToolId`: occupant ID and UID, sorted item UIDs, `mounting`,
      `active`, angle rounded to 1°, `balanced` per wheel item, unmounted part keys for the stands), `toolPositions`
      (tool → CarPlace or `default`) and `toolActionsSeen` (count per received `ToolActionKind`, not compared). Done
      when a dump contains the three sections.
- [ ] 5.3 Add `Wait-HarnessDumpsEqual` to `HarnessClient.psm1`: polls both dumps until the given sections are equal or
      times out with the differing section names. Done when used by 5.4.
- [ ] 5.4 Create `tools-slots.ps1`, `tools-race.ps1`, `tools-car-effects.ps1` and `tools-latejoin.ps1` with the connect
      preamble and empty step lists (in `tools-latejoin`, B connects after A's steps). Done when
      `Run-Session.ps1 -Scenario tools-slots` runs to the end and passes.

## 6. Tire changer

- [ ] 6.1 Hook `SetGroupOnTireChanger(GroupItem,bool,bool)` (postfix) and `Clear()` (prefix) per D6. Remote:
      `SetGroupOnTireChanger(group, true, connect)` and a neutral `Clear()` coroutine. Done when the client builds.
- [ ] 6.2 Add `tool-mount <tool> <true|false>`. `tools-slots` steps: A puts a wheel → equal; A separates → equal; B takes
      → equal, inventory count as before the put. Done when the steps pass.
- [ ] 6.3 `tools-race` steps with `tool-hold on` on both: A and B put different wheels, release → one wheel on the
      changer, the other in the inventory; same with both taking; A puts wheel W on the changer while B puts W on the
      balancer → W on one machine only. Done when the steps pass 5 times in a row.
- [ ] 6.4 `tools-latejoin` step: a wheel on the changer before B connects → equal `tools` and `inventory`. Done when it passes.

## 7. Wheel balancer

- [ ] 7.1 Hook `SetGroupOnWheelBalancer(GroupItem,bool)` (postfix), `FinishBalance()` (postfix, or
      `FinishBalanceInternal()` per 1.3) and `Clear()` (prefix). Remote: apply the group; copy `IsBalanced` in place;
      `WheelBalanceWindow.CancelAction()` before a remote clear. The minigame is not skipped. Done when the client builds.
- [ ] 7.2 Add `tool-balance` (calls `FinishBalance()` on the loaded wheel, no UI). `tools-slots` steps: A puts, A
      balances, B takes → `balanced = true` on both. Add a late-join step with a balanced wheel. Done when the steps pass.

## 8. Spring clamp

- [ ] 8.1 Hook `SetGroupOnSpringClamp(GroupItem,bool,bool)` (postfix) and `ClearSpringClamp()` (prefix). Remote:
      `SetGroupOnSpringClamp(group, true, mount)` / neutral clear. Done when the client builds.
- [ ] 8.2 `tools-slots` steps with `give-group shock`: put, `tool-mount SpringClamp false`, take → equal dumps, count
      unchanged. Add a late-join step. Done when the steps pass.

## 9. Brake lathe

- [ ] 9.1 Hook `BrakeLatheLogic.SetItem(Item,bool)` (postfix) and `Clear()` (prefix). Remote: `SetItem(item, true)` /
      neutral `Clear()`. Done when the client builds.
- [ ] 9.2 `tools-slots` steps with a brake disc: A puts, A `to-menu` (disconnect), B still sees it and takes it → one
      copy in the inventory; A reconnects → equal. Done when the steps pass.

## 10. Battery charger

- [ ] 10.1 Hook `SetItemOnBatteryCharger(Item,bool)` (postfix), `ClearBatteryCharger()` (prefix) and
      `BatteryChargerActivate(bool)` (postfix → `ToolSlotProperty(Active)`). Remote: set item, activate, neutral clear.
      Done when the client builds.
- [ ] 10.2 Add `tool-charger <on|off>`. `tools-slots` steps: put, switch on, B takes → equal. `tools-latejoin` step:
      after B joined, B `tool-local-put BatteryCharger <battery id>` then `tool-resync` → B's charger empty and
      `inventory` equal to A's. Done when the steps pass.

## 11. Engine stand 1

- [ ] 11.1 Hook `SetGroupOnEngineStand(GroupItem,bool)` (prefix, stand from `__instance`), `SetEngineOnEngineStand(Item)`
      if 1.3 needs it, `ClearEngineStand()` (prefix) and `NotificationCenter.TakeOffEngineFromStand()` (marks a local
      take). Remote: `SetGroupOnEngineStand(group, false)` as a coroutine / neutral `ClearEngineStand()`. Done when the client builds.
- [ ] 11.2 Angle per D5 (hook or 10 Hz poll, throttled, final send). Done when `tool-angle EngineStand1 90` gives
      equal `angle` on both clients.
- [ ] 11.3 Parts on the stand per D7: `ToolPartChange` through `sync-car-parts`' `PartTransaction` rooted at
      `engineGameObject`; server precondition/delta check against `Slots[tool].Parts`. Done when
      `tool-stand-part EngineStand1 <key> unmount` gives equal unmounted keys and inventory, and a held race on the same
      part gives one item.
- [ ] 11.4 `tools-slots` steps (put, rotate, unmount one part, B takes off → one engine group) and `tools-race` (both
      take with `tool-hold` → one group); a late-join step (engine with one part removed and rotated). Done when the steps pass.

## 12. Engine stand 2

- [ ] 12.1 Check with `tool-list` that `Engine_stand_2` is found on both clients. Repeat 11.4 with `EngineStand2` while
      stand 1 holds a different engine. Done when both stands are equal and independent in the dumps. If it cannot
      work, disable stand 2's interactive object while connected and note it in `QUESTIONS.md`.

## 13. Tool positions

- [ ] 13.1 Hook `ToolsMoveManager.MoveTo(IOSpecialType, CarPlace, bool)` and `SetOnDefaultPosition(IOSpecialType)`
      (prefix, only when `CanMove` allows it). Remote: `MoveTo(tool, place, false)` / `SetOnDefaultPosition(tool)`;
      retry from the mirror when `CanMove` is false. Done when the client builds.
- [ ] 13.2 Add `tool-move <tool> <place|default>`. `tools-slots` steps for the welder, oil bin and engine crane (to a
      lifter and back) → equal `toolPositions`. Late-join step with the welder at a lifter. Done when the steps pass.

## 14. Repair table

- [ ] 14.1 Hook `RepairPartWindow.UpdateItemCondition(PartInfo, bool)` (postfix): send `InventoryItemAction(Update)` with
      the item by UID. Done when the client builds.
- [ ] 14.2 Add `tool-repair <uid> <success|fail>`. `tools-slots` step: A repairs → B's condition equals A's. Done when it passes.

## 15. Paint shop (part)

- [ ] 15.1 After `MakePartPaintEffects()` a watcher waits for `IsPainting` false and sends `Update` for
      `PaintshopManager.item`. Done when the client builds.
- [ ] 15.2 Add `tool-paint-part <uid> <r,g,b>`. Step: part colour equal in `inventory` (add colour to the inventory dump
      if missing). Done when the step passes.

## 16. Engine crane

- [ ] 16.1 Hook `CarLoader.UseEngineCrane()` (postfix → `ToolAction(EngineOut)`) and `NotificationCenter.InsertEngineToCar(GroupItem)`
      (prefix → `EngineIn`, or `EngineSwap` per D9). Remote: effect only. The part and inventory change is row 1's
      transaction (1.2). Done when the client builds.
- [ ] 16.2 Engine swap per D9: server sets `EngineSwap` and bumps `SpawnSeq` through row 1's API; remote `SwapEngine`
      coroutine under `ApplyingRemote`, then row 1 re-baseline. Done when a swapped car's `cars` dump is equal on both
      clients and on a late joiner.
- [ ] 16.3 Add `tool-engine-out <loader>` and `tool-engine-in <loader> <uid>`. `tools-car-effects` steps: out → equal
      `cars` and `inventory`, one engine group; in → equal; swap → equal. Done when the steps pass.

## 17. Paint shop (car)

- [ ] 17.1 `MakeCarPaintEffects()` prefix → `ToolAction(PaintCar, loader of PaintshopManager.carLoader)` and a watcher
      that calls `CarDetailsSync.MarkDirty(carLoader, Paint | BodyCosmetics)` + `FlushNow` when `IsPainting` turns
      false. Remote: `particleSystem` only. Done when the client builds.
- [ ] 17.2 Add `tool-paint-car <loader> <r,g,b>`. Step: car paint equal in `cars`; B's `toolActionsSeen.PaintCar` is 1.
      Done when the step passes.

## 18. Car wash and interior detailing

- [ ] 18.1 Hook `CarWashLogic.DoWorkAnim(CarLoader)`, `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)` and
      `ToolsMoveManager.UseInteriorDetailingToolkitStationary()` (prefix → `ToolAction` + watcher). Results per D8
      (car wash: `CarDetailsSync.MarkDirty(…, BodyCosmetics)` + `FlushNow`; interior: `CarPart` dust via
      `CarDetailsSync.MarkDirty(…, BodyCosmetics)`, part condition and `PartScript` dust via `CarPartsSync.MarkDirty`). Remote: particles
      and SFX only. Done when the client builds.
- [ ] 18.2 Add `tool-use <CarWash|InteriorDetailing|InteriorDetailingStationary> <loader>`. Steps: equal `cars`
      dirt/interior fields after the effect; B's `toolActionsSeen` is 1 each; B stays `playable`; `stats` money equal.
      Done when the steps pass.

## 19. Oil bin

- [ ] 19.1 Hook `CarLoader.UseOilbin()` (prefix → `ToolAction(DrainOil)`; a watcher calls `CarDetailsSync.FlushNow` after the drain;
      the value travels through row 4's Fluids poll).
      Remote: effect only. Done when the client builds.
- [ ] 19.2 `tools-car-effects` step `tool-use OilBin <loader>` → equal fluid fields in `cars`. Done when it passes.

## 20. Welder

- [ ] 20.1 Hook `WelderLogic.DoWorkAnim(CarLoader)` (prefix → `ToolAction(Weld)` + watcher → `CarPartsSync.MarkDirty`
      for the welded body parts). Remote: particles and SFX only, no `StartAnim`. Done when the client builds.
- [ ] 20.2 `tools-car-effects` step `tool-use Welder <loader>` → equal body condition in `cars`; B stays `playable` and
      B's lifter buttons are enabled (dump `lifterButtonsEnabled` for that loader). Done when the step passes.

## 21. Dyno (waits for ROADMAP row 13, which stores dyno results)

- [ ] 21.1 Hook `CarLoader.MeasurePower()` (postfix; fallback `DynoManager.CloseDyno()`) → row 13's dyno result send.
      No remote dyno run. Done when the client builds.
- [ ] 21.2 Add `tool-dyno <loader>` (calls `MeasurePower()` on a car at `CarPlace.Dyno`). Step: equal dyno fields in
      `cars`. Done when the step passes.

## 22. Two-instance verification

- [ ] 22.1 Run `tools/test-env/Run-Session.ps1 -Scenario tools-slots`, `tools-race`, `tools-car-effects` and
      `tools-latejoin` with instances A and B against the local server. Each must report PASSED with equal `stats`,
      `inventory`, `cars`, `tools` and `toolPositions` sections. Attach the run folders to the PR description.
- [ ] 22.2 Add a restart step at the end of `tools-latejoin` (inside one run, because `Run-Session.ps1` restores `Saves`
      afterwards): `Send-ServerCommand save`, `Stop-TestServer`, both `to-menu`, `Start-TestServer`, both reconnect;
      the `tools` and `toolPositions` dumps equal those before the restart. Add a return step: B `travel`s away (row 6 harness), A puts a
      wheel, B returns → equal. Done when the scenario passes.
