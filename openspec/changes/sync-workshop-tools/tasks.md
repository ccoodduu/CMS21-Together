# Tasks

The order is shared foundation first, then the machines from most to least used: tire changer, wheel balancer, tool
positions, engine stand 1, engine crane, spring clamp, repair table, paint shop, car wash/interior, oil bin, welder,
brake lathe, battery charger, engine stand 2. Each machine group is a small client file on the shared `ToolSync` base
plus its harness steps, and can be merged on its own once its scenario step passes.

## 1. Dependencies and vanilla call order

- [ ] 1.1 Read the merged/active `sync-car-parts` and `sync-car-details` designs. Write down in this change's design.md
      (D6/D7) the final names of: the sub-part DTO and index-path resolver, the body-part sender, the dirt/wash, fluid
      and paint packets and their senders, and whether they already hook `CarLoader.TweenExteriorDustWash`,
      `TweenInteriorConditionAndDust`, `UseOilbin`, `UseEngineCrane`/`ActionUnMountGroup` and `MakeCarPaintEffects`.
      Done when D7's "result path" column names real types.
- [ ] 1.2 Check whether `sync-car-parts` adds `ItemActionType.Update` and idempotent ADD by UID. Mark tasks 2.3 and 3.2
      as "reuse" if it does. Done when design open question 2 is resolved in the text.
- [ ] 1.3 Add a harness `tool-trace on|off` command that logs, in order and with UIDs, every `Inventory.Add/Delete/AddGroup/DeleteGroup`
      and every tool method hooked by this change. Run each machine by hand once in one instance and save the trace
      under `tools/runs/`. Done when the call order for put/take of every slot machine, and whether engine stand
      take-off creates a new UID, are written into design.md D3/D6.

## 2. Core packets and state

- [ ] 2.1 Add `ModToolId` (TireChanger, WheelBalancer, SpringClamp, EngineStand1, EngineStand2, BrakeLathe, BatteryCharger,
      Welder, CarWash, InteriorDetailing, InteriorDetailingStationary, OilBin, EngineCrane, Paintshop, RepairTable,
      HeadlampAligner, WindowTint), `ToolProperty`, `ToolActionKind` and `ToolSlotState` in `Core/Data/GameType/`.
      Done when Core builds.
- [ ] 2.2 Add `ToolPackets.cs` with `ToolSlotUpdatePacket`, `ToolSlotRejectedPacket`, `ToolSlotPropertyPacket`,
      `ToolPartUpdatePacket`, `ToolPositionPacket`, `ToolActionPacket` and `ToolsStatePacket`. Append the new
      `PacketTypes` values at the end of the enum. Done when Core builds and `PacketRouter` registers all seven
      (log line on server start).
- [ ] 2.3 Add `ItemActionType.Update` (unless reused, see 1.2). Done when Core builds.
- [ ] 2.4 Add `ToolsState { Slots, Positions }` to `ModGameState`. Done when a server save → load round trip keeps a
      hand-filled `ToolsState` (check the JSON in `Saves/server_save.json`).

## 3. Server

- [ ] 3.1 Add `Network/Handlers/ToolHandlers.cs`: `ToolSlotUpdate` with CAS on `ExpectedUid` plus the allowed-occupant
      check per tool, relay to the others on accept, `ToolSlotRejected` to the sender on reject; `ToolSlotProperty`
      and `ToolPosition` stored last-write-wins and relayed; `ToolPartUpdate` stored in `Slots[tool].Parts` and relayed;
      `ToolAction` relayed only. A clear wipes `Parts`. Done when two clients' packets produce the expected log lines
      on accept and on reject.
- [ ] 3.2 Make inventory ADD idempotent by UID in `InventoryHandlers` (item and group), and handle `Update` (replace by
      UID, relay) (unless reused, see 1.2). Done when a duplicated ADD is logged as ignored and the inventory count
      does not change.
- [ ] 3.3 Send `ToolsStatePacket` in `AuthHandler.OnAskForSync` after the inventory batches and before `SyncEnd`.
      Done when the server log shows the order WorldState → GarageState → Inventory → ToolsState → SyncEnd.

## 4. Client framework

- [ ] 4.1 Add `Logic/Tools/ToolSync.cs`: the tool registry (`ModToolId` → game instance via `ToolsManager.Get()`,
      `ToolsMoveManager.Get()` and `Engine_stand_2`), `ClientToolsState` mirror, `ApplyingRemote(tool)` scope,
      `NeutralUids`, and a base class with `OnLocalChange` → `ToolSlotUpdate` (sets `ExpectedUid` from the mirror) and
      `ApplySnapshot`. Done when the client builds and a `tool-list` harness command lists the instance found for each id.
- [ ] 4.2 In `InventoryHook`, make the four prefixes return `false` (no send, no local change) for UIDs in
      `NeutralUids`. In `InventoryHandlers`, skip an ADD whose UID is already present and apply `Update` in place on
      the existing `Item`. Done when the harness `give-item` of an existing UID leaves the inventory unchanged.
- [ ] 4.3 Add `Network/Handlers/ToolHandlers.cs`: update the mirror for every tool packet; apply only when in the garage
      and `IsInitialSyncFinished`; `ToolSlotRejected` → put compensation (wait until the item is gone locally, re-add
      with hooks on, apply `Current`) or take compensation (apply `Current`). Done when the client builds; behaviour
      is covered by the per-tool groups.
- [ ] 4.4 Late join: handle `ToolsStatePacket` after `IsInventorySynced`: clear every slot machine the local save
      loaded (inventory-neutral), apply all slots (`instant = true`) and part overlays, then positions. Re-apply the
      mirror on every garage load. Done when covered by the late-join scenario (5.4, extended per tool).

## 5. Harness foundation

- [ ] 5.1 Add `tools/TestHarness/Features/ToolsCommands.cs` with `tool-list`, `tool-trace`, `give-item <id>` and
      `give-group <wheel|engine|shock>`. Each creates the item through `Inventory.Add`/`AddGroup` with hooks on, so the
      server gets it; reuse these commands if another change already added them. Also add the generic `tool-put <tool> <uid>`
      and `tool-take <tool>`, which drive the same vanilla path the trace in 1.3 showed. Done when the commands answer
      from `Send-HarnessCommand`.
- [ ] 5.2 Extend `StateDump.Full()` with `tools` (per `ModToolId`: occupant ID, occupant UID, sorted item UIDs,
      `mounting`, `active`, angle rounded to 1°, `balanced` per wheel item, number of unmounted parts for the stands)
      and `toolPositions` (tool → CarPlace or `default`). Also add `toolActionsSeen` (count per kind of received
      `ToolAction`), which is not part of the comparison. Done when a dump contains the three sections.
- [ ] 5.3 Add `Wait-HarnessDumpsEqual` to `HarnessClient.psm1`. It polls both dumps until the given sections are
      equal, or times out with the differing section names. Also add `Send-HarnessCommandPair`, which writes the
      same command to both instances before waiting for either reply (needed for races). Done when both are used by 5.4.
- [ ] 5.4 Create the scenarios `tools-slots.ps1`, `tools-race.ps1`, `tools-car-effects.ps1` and `tools-latejoin.ps1`
      with the connect preamble and an empty step list. `tools-car-effects` spawns a car through the
      `sync-car-parts` scenario helper. In the late-join scenario, B connects only after A has finished its steps.
      Done when `Run-Session.ps1 -Scenario tools-slots` runs to the end and passes with no steps.

## 6. Tire changer

- [ ] 6.1 Hook `TireChangerLogic.SetGroupOnTireChanger(GroupItem,bool,bool)` (postfix → snapshot with
      `GroupOnTireChanger` and `GroupOnTireChangerIsMounting`) and `TireChangerLogic.Clear()` (prefix → empty snapshot).
      Remote: `SetGroupOnTireChanger(group, true, mounting)` and a neutral `Clear()`. Done when the client builds.
- [ ] 6.2 Add the harness command `tool-mount TireChanger <true|false>` (separate/connect). Add `tools-slots` steps: A puts
      a wheel → dumps equal; A separates → equal; B takes the rim and tire or the wheel → equal, inventory item count
      the same as before the put. Done when the steps pass.
- [ ] 6.3 Add `tools-race` steps: A and B `tool-put` different wheels within the same second → equal dumps, one wheel
      on the changer, the other in the inventory; both `tool-take` at once → equal dumps, one copy. Done when the
      steps pass 5 times in a row.
- [ ] 6.4 Add a `tools-latejoin` step: a wheel on the tire changer before B connects → equal `tools` and `inventory`.
      Also a case where B's own save has a wheel on the changer and the session's changer is empty. Done when the steps pass.

## 7. Wheel balancer

- [ ] 7.1 Hook `WheelBalancerLogic.SetGroupOnWheelBalancer(GroupItem,bool)` (postfix), `FinishBalance()` (postfix →
      snapshot with balanced `WheelData`) and `Clear()` (prefix). Remote: apply the group; for a balance on the same
      UID, copy `WheelData.IsBalanced` onto the held items in place; close the local `WheelBalanceWindow`
      (`CancelAction()`) before applying a clear. Do not skip the minigame. Done when the client builds.
- [ ] 7.2 Add the harness command `tool-balance` (runs the balance to `FinishBalance()` without the UI). Add `tools-slots`
      steps: A puts a wheel, A balances, B takes it → B's inventory wheel has `balanced = true` in both dumps. Add a
      late-join step with a balanced wheel on the balancer. Done when the steps pass.

## 8. Tool positions

- [ ] 8.1 Hook `ToolsMoveManager.MoveTo(IOSpecialType, CarPlace, bool)` and `SetOnDefaultPosition(IOSpecialType)`
      (prefix, only if `CanMove` allows it). Remote: `MoveTo(tool, place, false)` / `SetOnDefaultPosition(tool)`; when
      `CanMove` is false, keep the position in the mirror and retry later. Done when the client builds.
- [ ] 8.2 Add the harness command `tool-move <tool> <place|default>`. Add `tools-slots` steps for the welder, oil bin
      and engine crane (to a lifter and back to default) → equal `toolPositions`. Add a late-join step with the welder
      at a lifter. Done when the steps pass.

## 9. Engine stand 1

- [ ] 9.1 Hook `EngineStandLogic.SetGroupOnEngineStand(GroupItem,bool)` (prefix, record the group; identify the
      stand from `__instance`), `ClearEngineStand()` (prefix) and `NotificationCenter.TakeOffEngineFromStand()` (actor
      take-off). Remote: `SetGroupOnEngineStand(group, false)` / neutral `ClearEngineStand()`. Add the
      `SetEngineOnEngineStand(Item)` hook if 1.3 showed it is needed. Done when the client builds.
- [ ] 9.2 Angle: an `IncreaseEngineStandAngle(float)` postfix sends `ToolSlotProperty(Angle, EngineStandAngle)`, throttled
      to 10 Hz with a final send. Remote: `SetEngineStandAngle(angle)`. Done when the harness `tool-angle EngineStand1 90`
      gives equal `tools.EngineStand1.angle` on both clients.
- [ ] 9.3 Parts on the stand: send `ToolPartUpdate` on mount/unmount of a PartScript under `engineGameObject`, using the
      `sync-car-parts` DTO/resolver rooted at the stand. Remote applies through that resolver. Done when the harness
      `tool-stand-part EngineStand1 <indexPath> unmount` gives an equal unmounted-part count and inventory.
- [ ] 9.4 Add `tools-slots` steps (put engine, rotate, unmount one part, take off → equal dumps) and a late-join step
      (engine with one part removed and rotated). Done when the steps pass.

## 10. Engine crane

- [ ] 10.1 Hook `CarLoader.UseEngineCrane()` (postfix → `ToolAction(EngineOut)`) and `NotificationCenter.InsertEngineToCar(GroupItem)`
      (prefix → `EngineIn`, or `EngineSwap` with the group when its ID differs from `GetEngine().name`). Remote: effect
      only; `EngineSwap` → `CarLoader.SwapEngine(group)` under `ApplyingRemote`. Engine part state comes from
      `sync-car-parts`, the engine group from the inventory flow. Done when the client builds.
- [ ] 10.2 Add the harness commands `tool-engine-out <loader>` and `tool-engine-in <loader> <uid>`. Add `tools-car-effects.ps1`
      steps: engine out → equal `cars` and `inventory`; engine back in → equal. Done when the steps pass (needs a car
      from `sync-car-parts`' scenario setup).

## 11. Spring clamp

- [ ] 11.1 Hook `SpringClampLogic.SetGroupOnSpringClamp(GroupItem,bool,bool)` (postfix, with `GroupOnSpringClampIsMounting`)
      and `ClearSpringClamp()` (prefix). Remote: `SetGroupOnSpringClamp(group, true, mounting)` / neutral `ClearSpringClamp()`.
      Done when the client builds.
- [ ] 11.2 Add `tools-slots` steps with `give-group shock`: put, `tool-mount SpringClamp false` (separate), take as single
      items → equal dumps and the inventory count unchanged. Add a late-join step. Done when the steps pass.

## 12. Repair table

- [ ] 12.1 Hook `RepairPartWindow.UpdateItemCondition(PartInfo, bool)` (postfix): send `InventoryItemAction(Update)` with the
      item from the inventory by UID (condition, dent, `RepairAmount`). Done when the client builds.
- [ ] 12.2 Add the harness command `tool-repair <uid> <success|fail>`. Add a `tools-slots` step: A repairs → B's
      inventory condition equals A's. Done when the step passes.

## 13. Paint shop

- [ ] 13.1 Car: `PaintshopManager.MakeCarPaintEffects()` prefix → `ToolAction(PaintCar, loader of PaintshopManager.carLoader)`;
      the paint result goes through `sync-car-details` (add the send only if 1.1 shows it is missing). Remote:
      `particleSystem` effect only. Done when the client builds.
- [ ] 13.2 Part: once `MakePartPaintEffects()` has finished (`IsPainting` is false), send `InventoryItemAction(Update)`
      for the painted item. Done when the client builds.
- [ ] 13.3 Add the harness commands `tool-paint-part <uid> <r,g,b>` and `tool-paint-car <loader> <r,g,b>`. Add steps:
      the part paint is equal in `inventory` (add colour to the inventory dump if missing); the car paint is equal in
      `cars` (via `sync-car-details` dump fields); B's `toolActionsSeen.PaintCar` is 1. Done when the steps pass.

## 14. Car wash and interior detailing

- [ ] 14.1 Hook `CarWashLogic.DoWorkAnim(CarLoader)`, `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)` and
      `ToolsMoveManager.UseInteriorDetailingToolkitStationary()` (prefix → `ToolAction`). Remote: particles and SFX
      only. Add a result send only if 1.1 shows `sync-car-details` misses it. Done when the client builds.
- [ ] 14.2 Add the harness command `tool-use <CarWash|InteriorDetailing|InteriorDetailingStationary> <loader>`. Add
      `tools-car-effects` steps: equal `cars` dirt/interior fields after the effect time; `toolActionsSeen` on B
      is 1 for each; B can move during the effect (status `playable` stays true). Done when the steps pass.

## 15. Oil bin

- [ ] 15.1 Hook `CarLoader.UseOilbin()` (prefix → `ToolAction(DrainOil)`). Remote: effect only. The fluid result
      comes from `sync-car-details`. Done when the client builds.
- [ ] 15.2 Add `tools-car-effects` step `tool-use OilBin <loader>` → equal fluid fields in `cars`. Done when the step passes.

## 16. Welder

- [ ] 16.1 Hook `WelderLogic.DoWorkAnim(CarLoader)` (prefix → `ToolAction(Weld)`) and `WelderLogic.FinishAnim(CarLoader)`
      (prefix → send the `body`/`details` CarPart state through the `sync-car-parts` body-part sender). Remote: particles
      and SFX only, no `StartAnim` (it disables IO and lifter buttons). Done when the client builds.
- [ ] 16.2 Add `tools-car-effects` step `tool-use Welder <loader>` → equal `cars` body condition/dent; B stays
      `playable` and B's lifter buttons are enabled (dump `lifterButtonsEnabled` for that loader). Done when the step passes.

## 17. Brake lathe

- [ ] 17.1 Hook `BrakeLatheLogic.SetItem(Item,bool)` (postfix → snapshot with `Item`) and `Clear()` (prefix). Remote:
      `SetItem(item, true)` / neutral `Clear()`. Done when the client builds.
- [ ] 17.2 Add `tools-slots` steps with a brake disc from `give-item`: put, wait for processing, B takes it → equal
      inventory (including condition). Add a late-join step. Done when the steps pass.

## 18. Battery charger

- [ ] 18.1 Hook `BatteryChargerLogic.SetItemOnBatteryCharger(Item,bool)` (postfix), `ClearBatteryCharger()` (prefix)
      and `BatteryChargerActivate(bool)` (postfix → `ToolSlotProperty(Active)`). Remote: `SetItemOnBatteryCharger(item, true)`,
      `BatteryChargerActivate(active)` and neutral clear. Done when the client builds.
- [ ] 18.2 Add the harness command `tool-charger <on|off>` and `tools-slots` steps: put a battery, switch on, B takes it
      → equal inventory. Add a late-join step that also covers "B's own save had a battery on the charger". Done when the steps pass.

## 19. Engine stand 2

- [ ] 19.1 Check with `tool-list` that `Engine_stand_2` is found on both clients. Repeat the 9.4 steps with
      `EngineStand2` while stand 1 holds a different engine. Done when both stands are equal and independent in the
      dumps. If this cannot be made to work, disable stand 2's interactive object while connected and log the
      question in `QUESTIONS.md` (design open question 1).

## 20. Two-instance verification

- [ ] 20.1 Run `tools/test-env/Run-Session.ps1 -Scenario tools-slots`, `-Scenario tools-race`,
      `-Scenario tools-car-effects` and `-Scenario tools-latejoin` with instances A and B against the local server.
      Each must report PASSED with equal `stats`, `inventory`, `cars`, `tools` and `toolPositions` sections. Attach
      the run folders to the PR description.
- [ ] 20.2 Restart the server between two `tools-latejoin` runs without wiping `Saves/server_save.json`. Done when the
      second run's first dump shows the same `tools` and `toolPositions` as the end of the first run.
