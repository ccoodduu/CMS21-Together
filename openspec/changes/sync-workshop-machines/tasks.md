# Tasks

> **Read first (2026-10-06):** `docs/spikes/workshop-machines.md` (static decompile). It corrects hooks in design.md that never fire (inlined builders, shared native bodies) and lists the runtime checks still needed.

> **Status (2026-10-06, branch `change/sync-workshop-machines`):** groups 2–15 are in code and build (Server and
> TestHarness, 0 errors); nothing has run in the game. Open: 1.3 (manual UI trace, needs the user), 1.4 and 16
> (game runs). Where the code follows the spike instead of design.md:
> - Hooks: puts are setter postfixes; tire changer and balancer takes are seen in `_Clear_d__23/26.MoveNext` (prefix at
>   state 0, postfix when it returns false); spring clamp, lathe and charger takes are prefix/postfix on their void
>   clears; the balancer claim is a prefix on `Balance(bool)`, the result and release a postfix on
>   `FinishBalanceInternal` (plus `WheelBalanceWindow.CancelAction`); other players are blocked at
>   `PieMenuController._GetOnClick_b__72_64/65`; the repair table is a postfix on `RepairPartWindow.ProcessGameResult`.
> - Engine stand: put = builder postfix plus `_SetGroupOnEngineStand_d__8.MoveNext` returning false; take = prefix on
>   `ClearEngineStand` when the stand's group is already in `Inventory.groups` (the client sends that ADD itself, the
>   vanilla `groups.Add` is inlined); angle = `IncreaseEngineStandAngle` postfix, no poll. Each client builds its own
>   group UID, so the client tracks the server's UID (`EngineStandSync.AppliedUid`). Stand 2 is found by the GameObject
>   `Engine_stand_2` and synced if it exists (the spike says it does not; `tool-list` shows it).
> - Balancer: "balanced" is the slot flag `Balanced` (= `!balanceCanceled`), applied with `SetCanceled`. A put without
>   the minigame is unbalanced; a put sends `Balanced = false` until the minigame result arrives.
> - Battery charger: no `Active` property sends; active = a battery is on it. `ToolProperty.Active` stays unused.
> - Paint shop (part): no watcher; the painted item returns with the real `Inventory.Add` on exit (spike). The
>   `tool-paint-part` verb still exercises `ItemActionType.Update`.
> - Remote clears: `ClearForTutorial()` for the tire changer and balancer, `ClearEngineStand()` for the stand, the
>   vanilla clears with neutral UIDs for the spring clamp, lathe and charger.
> - Engine-stand part changes: a ninth packet `ToolPartChangeResult` answers `ToolPartChange`, like
>   `CarPartsChangeResult`; the put snapshot carries the full part records as the server's baseline.
> - `Initialize` of `ToolsStore` is in `Program.cs`; the section is `Data/Tools/WorkshopToolsSection.cs` (beside the
>   store, like `Data/Jobs/JobsSection.cs`).
> - Not done: guard entries for row 5a are still `Planned` (`guard.ps1` uses `Pie:wheel_take` as its blocked example,
>   so flipping them needs that scenario changed too); the DLC rule from INTEGRATION.md ("DLC content on shared
>   machines outside `SharedDlc`") has no code yet.
> - Harness item IDs are guesses until the first run: wheel `rim_3` + `tire_standard` (WheelData 15/195/65/35), shock
>   absorber `amortyzatorPrzod_1` + `sprezynnaPrzod_1` + `czapkaAmorPrzod_1`, engine `engine_r4`, disc
>   `tarczaHamulcowa_1`, battery `akumulator`.

Order: spikes and dependency checks, shared foundation, then the slot machines, item processing and tool positions
(groups 6–15, need only the inventory and `sync-car-parts`), then the two-instance verification. Each machine group is
a small client file on the shared `ToolSync` base plus its harness steps and can be merged on its own once its
scenario step passes. The car-effect tools are the separate change `sync-workshop-car-tools` (user decision
2026-10-05), which builds on groups 2–5 of this one.

Prerequisites: `session-persistence-and-rejoin` groups 1–2, `sync-players-and-scenes` part 1 (`ClientScene.GarageBound`,
`PresenceEvents`, `travel`, `Wait-HarnessDump`) and `sync-car-parts` are merged.

## 1. Spikes and dependencies

- [x] 1.1 **Done (2026-10-06):** the merged names are `CarSubPartUpdatePacket`, `PartTransactions` (static, keyed by an int loader id), `InventoryDelta`, `PartRegistry.Build(CarLoader|Transform)`; design.md now says `PartTransactions`. Re-check the merged `sync-car-parts` against the names in design.md Context/D3/D7 (`CarSubPartUpdatePacket`,
      `PartTransaction`, `InventoryDelta`, `PartRegistry.Build`, the idempotent ADD). Done when design.md names only
      types that exist.
- [x] 1.2 **Done (2026-10-06):** idempotent ADD by UID was missing on `main` on both sides and is added by this change (server and client `InventoryHandlers`); `PartHooks` ignores `PartScript`s outside a car registry (it now hands them to `EngineStandParts`); `PartTransactions` works for a non-car root through a pseudo loader id per stand (`-100 - tool`). Check that `sync-car-parts` provides what D3/D7 need: idempotent ADD by UID on server and client; its hooks
      ignore `PartScript`s outside a car registry; `PartTransaction` can be opened for a non-car root. Done when each
      item is confirmed or recorded as a gap in `QUESTIONS.md`, with the dependent task (11.3) marked parked.
- [ ] 1.3 **In code (2026-10-06):** needs a game run. Add a harness `tool-trace on|off` command that logs, in order and with UIDs, every `Inventory.Add/Delete/AddGroup/DeleteGroup`
      (including `Add(List<BaseItem>)`) and every machine method hooked by this change, plus the method that opens
      `WheelBalanceWindow`. **Needs the user:** one ~10-minute session using each machine through the normal UI in one
      instance; save the trace under `tools/runs/`. Done when design.md D3/D5–D7 record: the put/take call order per
      slot machine, which hooks fire from the UI (inlining), whether a take creates new UIDs, the balancer's
      minigame-open and take entry points (void, blockable), and whether `FinishBalance`/`IncreaseEngineStandAngle`/
      `BatteryChargerActivate` are hit.
- [ ] 1.4 Spike the remote-apply primitives in one instance from the harness (no UI): `SetGroupOnTireChanger(g, true, c)`
      on an occupied changer, `Clear()` coroutines run through `MelonCoroutines` under a hooks-off scope,
      `SetGroupOnEngineStand(g, false)` as a coroutine, `ClearEngineStand()`, `SetEngineStandAngle`, `MoveTo(t, p, false)`.
      Done when design.md D6 notes for each: models not duplicated, inventory unchanged, no player/camera lock.

## 2. Core packets and state

- [ ] 2.1 **In code (2026-10-06):** needs a game run. Add `ModToolId` (TireChanger, WheelBalancer, SpringClamp, EngineStand1, EngineStand2, BrakeLathe,
      BatteryCharger; `sync-workshop-car-tools` appends its ids), `ToolProperty` and `ToolSlotState` (with
      `Dictionary<string, CarSubPartUpdatePacket> Parts`) in `Core/Data/GameType/`. Done when Core builds.
- [ ] 2.2 **In code (2026-10-06):** needs a game run. Add `ToolPackets.cs` with `ToolSlotUpdatePacket`, `ToolSlotRejectedPacket`, `ToolSlotPropertyPacket`,
      `ToolPartChangePacket`, `ToolPositionPacket`, `ToolsStatePacket` (`Slots`, `Positions`, `Claims`),
      `ToolClaimPacket` and `ToolClaimUpdatePacket`, appended at the end of `PacketTypes`. Done when `PacketRouter`
      registers all eight (log line on server start).
- [ ] 2.3 **In code (2026-10-06):** needs a game run. Add `ItemActionType.Update`. Done when Core builds.
- [ ] 2.4 **In code (2026-10-06):** needs a game run. Add `ToolsState { Slots, Positions }` to `ModGameState`. Done when Core builds.

## 3. Server

- [ ] 3.1 **In code (2026-10-06):** needs a game run. Add `Network/Handlers/ToolHandlers.cs` (no own lock; dispatch holds `StateLock`): `ToolSlotUpdate` with CAS on
      `ExpectedUid`, the kind/prefix check, the "UID not on another machine" check and the reservation check (D2 rule 4),
      relay on accept, `ToolSlotRejected` to the sender on reject; `ToolSlotProperty` and `ToolPosition` stored
      last-write-wins and relayed; `ToolClaim` grant/deny/release with `ToolClaimUpdate` to everyone, release on
      `Client.Disconnect`, `PresenceEvents.Left`/`SceneChanged` away from the garage and after 300 s (tick in
      `ServerWindow.TickServer`). A clear wipes `Parts`. Done when two clients' packets produce the expected
      accept/reject/claim log lines.
- [ ] 3.2 **In code (2026-10-06):** needs a game run. Handle `ItemActionType.Update` in `InventoryHandlers` (replace by UID and relay; unknown UID → log and
      ignore). Done when an Update for an unknown UID is logged as ignored and the inventory is unchanged.
- [ ] 3.3 **In code (2026-10-06):** needs a game run. Add `Data/Persistence/WorkshopToolsSection.cs`: `[SessionSection]`, `ISaveSection` (key `workshop-tools`,
      `Version = 1`, thin adapter over `ModGameState.ToolsState`, `Reset()` = empty) and `ISnapshotProvider`
      (`SyncOrder = 300`, `SendSnapshot` sends one `ToolsStatePacket` incl. the active reservations and returns 1).
      Done when the server logs the key at start, the `connect` log lists `workshop-tools` between `car-placement`
      and `jobs`, and a hand-filled `ToolsState` survives `Send-ServerCommand save` + restart (section
      `workshop-tools` v1 in `server_save.json`).

## 4. Client framework

- [ ] 4.1 **In code (2026-10-06):** needs a game run. Add `Logic/Tools/ToolSync.cs`: the registry (`ModToolId` → instance via `ToolsManager.Get()`,
      `ToolsMoveManager.Get()` and `Engine_stand_2`), `ClientToolsState` mirror, `ApplyingRemote(tool)` scope released
      at the end of the wrapping coroutine, `NeutralUids`, and a base class with `OnLocalChange` → `ToolSlotUpdate`
      (`ExpectedUid` from the mirror) and `ApplySnapshot`. Sends are gated on initial sync finished and
      `ClientScene.IsGarageReady` (D4). Done when the client builds and `tool-list` lists the instance found for each id.
- [ ] 4.2 **In code (2026-10-06):** needs a game run. In `InventoryHook`, make the four prefixes return `false` (no send, no local change) for UIDs in
      `NeutralUids`; add the before/after inventory snapshot for remote applies (D3.3). In `InventoryHandlers`, apply
      `Update` in place (the idempotent ADD is already there from `sync-car-parts`). Done when `give-item` of an
      existing UID leaves the inventory unchanged.
- [ ] 4.3 **In code (2026-10-06):** needs a game run. Add `Network/Handlers/ToolHandlers.cs` routed through `ClientScene.GarageBound` (D10): update the mirror and
      apply in the garage; queue between `SyncEnd` and `SyncAck`; mirror only while away; `ToolSlotRejected` → put or
      take compensation per D3.2; `ToolClaimUpdate` → mirror, and close an own open window that lost. Done when the
      client builds; behaviour is covered by the machine groups.
- [ ] 4.4 **In code (2026-10-06):** needs a game run. Late join: handle `ToolsStatePacket` (it follows the `inventory` section on the stream; no own synced flag):
      clear every machine the local save loaded (inventory-neutral), apply all slots (`instant = true`) and overlays,
      then positions and reservations, then call `SyncTracker.Applied("workshop-tools")` once. Done when `syncAcked`
      is true in `tools-latejoin` and a forced apply failure shows `workshop-tools` in the sync timeout log.

## 5. Harness foundation

- [ ] 5.1 **In code (2026-10-06):** needs a game run. Add `tools/TestHarness/Features/ToolsCommands.cs` with `tool-list`, `tool-trace`, `give-item <id>`,
      `give-group <wheel|engine|shock>` (through `Inventory.Add`/`AddGroup` with hooks on), `tool-put <tool> <uid>`
      and `tool-take <tool>` (inventory call plus the logic method, in the order 1.3 recorded), `tool-hold on|off`
      (buffers incoming tool packets before the mirror, to force races deterministically), `tool-local-put <tool> <id>`
      (hooks off; simulates the local save); the resync uses row 14's `resync force` (garage reload + full snapshot), no own verb. `sync-workshop-car-tools` adds
      its verbs to the same file. Done when each command answers from `Send-HarnessCommand`.
- [ ] 5.2 **In code (2026-10-06):** needs a game run. Extend `StateDump.Full()` with `tools` (per `ModToolId`: occupant ID and UID, sorted item UIDs, `mounting`,
      `active`, angle rounded to 1°, `balanced` per wheel item, unmounted part keys for the stands, `claimedBy`) and
      `toolPositions` (tool → CarPlace or `default`). Done when a dump contains both sections.
- [ ] 5.3 **In code (2026-10-06):** needs a game run. Add `Wait-HarnessDumpsEqual` to `HarnessClient.psm1`, built on `sync-players-and-scenes`' `Wait-HarnessDump`:
      polls both dumps until the given sections are equal or times out with the differing section names. Done when
      used by 5.4.
- [ ] 5.4 **In code (2026-10-06):** needs a game run. Create `tools-slots.ps1`, `tools-race.ps1` and `tools-latejoin.ps1` with the connect preamble and empty step
      lists (in `tools-latejoin`, B connects after A's steps). Done when `Run-Session.ps1 -Scenario tools-slots` runs
      to the end and passes.

## 6. Tire changer

- [ ] 6.1 **In code (2026-10-06):** needs a game run. Hook `SetGroupOnTireChanger(GroupItem,bool,bool)` (postfix) and `Clear()` (prefix) per D6. Remote:
      `SetGroupOnTireChanger(group, true, connect)` and a neutral `Clear()` coroutine. Done when the client builds.
- [ ] 6.2 **In code (2026-10-06):** needs a game run. Add `tool-mount <tool> <true|false>`. `tools-slots` steps: A puts a wheel → equal; A separates → equal; B takes
      → equal, inventory count as before the put. Done when the steps pass.
- [ ] 6.3 **In code (2026-10-06):** needs a game run. `tools-race` steps with `tool-hold on` on both: A and B put different wheels, release → one wheel on the
      changer, the other in the inventory; same with both taking; A puts wheel W on the changer while B puts W on the
      balancer → W on one machine only. Done when the steps pass 5 times in a row.
- [ ] 6.4 **In code (2026-10-06):** needs a game run. `tools-latejoin` step: a wheel on the changer before B connects → equal `tools` and `inventory`. Done when it passes.

## 7. Wheel balancer

- [ ] 7.1 **In code (2026-10-06):** needs a game run. Hook `SetGroupOnWheelBalancer(GroupItem,bool)` (postfix), `FinishBalance()` (postfix, or
      `FinishBalanceInternal()` per 1.3) and `Clear()` (prefix). Remote: apply the group; copy `IsBalanced` in place.
      The minigame is not skipped. Done when the client builds.
- [ ] 7.2 **In code (2026-10-06):** needs a game run. Reservation per D6: the minigame-open entry from 1.3 sends `ToolClaim`; `FinishBalance` and
      `WheelBalanceWindow.CancelAction`/close send the release; while another player holds the balancer, block opening
      the minigame and taking the wheel at their void entry points with an info window; a `ToolClaimUpdate` naming
      someone else closes the own window with `CancelAction()`. Harness `tool-balance-open` / `tool-balance-cancel`.
      Done when the client builds.
- [ ] 7.3 **In code (2026-10-06):** needs a game run. Add `tool-balance` (calls `FinishBalance()` on the loaded wheel, no UI). `tools-slots` steps: A puts, A
      balances, B takes → `balanced = true` on both; A `tool-balance-open`, B `tool-take WheelBalancer` → refused,
      wheel still on the balancer for both, `claimedBy` = A on both; A `tool-balance-cancel`, B takes → succeeds;
      A opens again and `disconnect`s → B's `claimedBy` is empty. `tools-race`: A and B `tool-balance-open` with
      `tool-hold on` → one holder on both, the other's window closed. Add a late-join step with a balanced wheel and
      an open minigame (joiner sees `claimedBy`). Done when the steps pass.

## 8. Spring clamp

- [ ] 8.1 **In code (2026-10-06):** needs a game run. Hook `SetGroupOnSpringClamp(GroupItem,bool,bool)` (postfix) and `ClearSpringClamp()` (prefix). Remote:
      `SetGroupOnSpringClamp(group, true, mount)` / neutral clear. Done when the client builds.
- [ ] 8.2 **In code (2026-10-06):** needs a game run. `tools-slots` steps with `give-group shock`: put, `tool-mount SpringClamp false`, take → equal dumps, count
      unchanged. Add a late-join step. Done when the steps pass.

## 9. Brake lathe

- [ ] 9.1 **In code (2026-10-06):** needs a game run. Hook `BrakeLatheLogic.SetItem(Item,bool)` (postfix) and `Clear()` (prefix). Remote: `SetItem(item, true)` /
      neutral `Clear()`. Done when the client builds.
- [ ] 9.2 **In code (2026-10-06):** needs a game run. `tools-slots` steps with a brake disc: A puts, A `to-menu` (disconnect), B still sees it and takes it → one
      copy in the inventory; A reconnects → equal. Done when the steps pass.

## 10. Battery charger

- [ ] 10.1 **In code (2026-10-06):** needs a game run. Hook `SetItemOnBatteryCharger(Item,bool)` (postfix), `ClearBatteryCharger()` (prefix) and
      `BatteryChargerActivate(bool)` (postfix → `ToolSlotProperty(Active)`). Remote: set item, activate, neutral clear.
      Done when the client builds.
- [ ] 10.2 **In code (2026-10-06):** needs a game run. Add `tool-charger <on|off>`. `tools-slots` steps: put, switch on, B takes → equal. `tools-latejoin` step:
      after B joined, B `tool-local-put BatteryCharger <battery id>` then `resync force` (row 14) → B's charger empty and
      `inventory` equal to A's. Done when the steps pass.

## 11. Engine stand 1

- [ ] 11.1 **In code (2026-10-06):** needs a game run. Hook `SetGroupOnEngineStand(GroupItem,bool)` (prefix, stand from `__instance`), `SetEngineOnEngineStand(Item)`
      if 1.3 needs it, `ClearEngineStand()` (prefix) and `NotificationCenter.TakeOffEngineFromStand()` (marks a local
      take). Remote: `SetGroupOnEngineStand(group, false)` as a coroutine / neutral `ClearEngineStand()`. Done when the client builds.
- [ ] 11.2 **In code (2026-10-06):** needs a game run. Angle per D5 (hook or 10 Hz poll, throttled, final send). Done when `tool-angle EngineStand1 90` gives
      equal `angle` on both clients.
- [ ] 11.3 **In code (2026-10-06):** needs a game run. Parts on the stand per D7: `ToolPartChange` through `sync-car-parts`' `PartTransaction` rooted at
      `engineGameObject`; server precondition/delta check against `Slots[tool].Parts`. Done when
      `tool-stand-part EngineStand1 <key> unmount` gives equal unmounted keys and inventory, and a held race on the same
      part gives one item.
- [ ] 11.4 **In code (2026-10-06):** needs a game run. `tools-slots` steps (put, rotate, unmount one part, B takes off → one engine group) and `tools-race` (both
      take with `tool-hold` → one group); a late-join step (engine with one part removed and rotated). Done when the steps pass.

## 12. Engine stand 2

- [ ] 12.1 **In code (2026-10-06):** needs a game run. Check with `tool-list` that `Engine_stand_2` is found on both clients. Repeat 11.4 with `EngineStand2` while
      stand 1 holds a different engine. Done when both stands are equal and independent in the dumps. If it cannot
      work, disable stand 2's interactive object while connected and note it in `QUESTIONS.md`.

## 13. Tool positions

- [ ] 13.1 **In code (2026-10-06):** needs a game run. Hook `ToolsMoveManager.MoveTo(IOSpecialType, CarPlace, bool)` and `SetOnDefaultPosition(IOSpecialType)`
      (prefix, only when `CanMove` allows it). Remote: `MoveTo(tool, place, false)` / `SetOnDefaultPosition(tool)`;
      retry from the mirror when `CanMove` is false. Done when the client builds.
- [ ] 13.2 **In code (2026-10-06):** needs a game run. Add `tool-move <tool> <place|default>`. `tools-slots` steps for the welder, oil bin and engine crane (to a
      lifter and back) → equal `toolPositions`. Late-join step with the welder at a lifter. Done when the steps pass.

## 14. Repair table

- [ ] 14.1 **In code (2026-10-06):** needs a game run. Hook `RepairPartWindow.UpdateItemCondition(PartInfo, bool)` (postfix): send `InventoryItemAction(Update)` with
      the item by UID. Done when the client builds.
- [ ] 14.2 **In code (2026-10-06):** needs a game run. Add `tool-repair <uid> <success|fail>`. `tools-slots` step: A repairs → B's condition equals A's. Done when it passes.

## 15. Paint shop (part)

- [ ] 15.1 **In code (2026-10-06):** needs a game run. After `MakePartPaintEffects()` a watcher waits for `IsPainting` false and sends `Update` for
      `PaintshopManager.item`. Done when the client builds.
- [ ] 15.2 **In code (2026-10-06):** needs a game run. Add `tool-paint-part <uid> <r,g,b>`. Step: part colour equal in `inventory` (add colour to the inventory dump
      if missing). Done when the step passes.

## 16. Two-instance verification

- [ ] 16.1 Run `tools/test-env/Run-Session.ps1 -Scenario tools-slots`, `tools-race` and `tools-latejoin` with instances
      A and B against the local server. Each must report PASSED with equal `stats`, `inventory`, `tools` and
      `toolPositions` sections. Attach the run folders to the PR description.
- [ ] 16.2 Add a restart step at the end of `tools-latejoin` (inside one run, because `Run-Session.ps1` restores `Saves`
      afterwards): `Send-ServerCommand save`, `Stop-TestServer`, both `to-menu`, `Start-TestServer`, both reconnect;
      the `tools` and `toolPositions` dumps equal those before the restart (reservations empty). Add a return step: B
      `travel`s away (row 6 harness), A puts a wheel, B returns → equal. Done when the scenario passes.
