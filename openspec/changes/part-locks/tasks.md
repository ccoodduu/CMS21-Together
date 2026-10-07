# Tasks

**Row 18 — playtest fix (findings 1 and 4 of 2026-10-07, plus the coolant and connected-part races), lands before the
next Steam playtest.** Prerequisites (all on `main`): rows 1, 2, 4, 5b, 13, 14a and 17 part 1. Size L ≈ 7–8 sessions.
Runtime facts that the static decompile cannot give are spikes (group 1). A spike that changes a decision updates
design.md in the same commit, and the findings go to `docs/spikes/part-locks.md`. Every scenario carries
`# areas: locks, …` (the new area, task 4.2), and every scenario checks the server's `locks` command for
`overlapViolations` 0. Commit per finished task group with conventional commits.

## 1. Spikes

- [ ] 1.1 Hover and selection path. Static decompile into `native/out/locks_clean` of `Raycast.Garage`, `PartSelect`,
      `PartSelectMount`, `GarageAssemble`, `GameScript.SetPartMouseOver`, `UpdateRaycastOnItemName`,
      `GetRaycastOnItemName`, `PartScript.SetMouseOver(bool)`, `SetMouseOver()`, `MouseOverGroup()`, `Flashing`,
      `InteractiveObject.SetMouseOver(bool, Color)`, and the UI that reads the hover label. Add the verb
      `lock-trace on|off|report` (logging-only prefixes on those methods: caller order, part key, frame) and run it
      on one client while `harness-mouse-over` style hovering walks five parts and two body panels. Done when
      design.md D9 names, for each row of its table, the hook that fires on the real path, or the fallback when it is
      inlined, and records whether the coloured highlight is local only (open question 5).
- [ ] 1.2 Mount flow and cancels. Static: `PartScript.ActionMount(bool)`, `ChoosePartUpWindow.Show(…)` and the method
      that fills `items`, `GameScript.SelectPartToMount(BaseItem)`, where `SelectedToMount` is set,
      `ShowPreviewToMount`/`ShowGroupPreview`/`ShowPreview`/`HidePreview`, `PrepareItemsToMount`,
      `ShowAllUnMountedGroups`, the void caller of `UnMountByGroup`, `GetUnmountGroup`/`ForcePartGroup`, and every
      path into `CleanUnfinishedMount`/`CleanUnfinishedUnMount`. Runtime: `lock-trace` while mounting a disc and a
      caliper with a piston (group), cancelling with ESC and with a mode change, and a group unmount. Done when
      design.md D3/D9 name the item step hook, the group source, the preview hooks and the list of release signals
      (mode values included).
- [ ] 1.3 Relations and lock-set sizes. `lock-trace relations <loader>`: for every registry part, dump its key, id,
      `PartScript` ancestors, `unmountWith`/main object, `blockedBy`, `unblockOnUnmount`, `CarFluid` below it
      (type, id), `FluidRefillLockType`, `IsFluidContainer()`; for body parts, `ConnectedParts`. Static:
      `AddToBlockedBy`, `UnblockBlockParts`, `CheckIsFluidContainer`, `GetFluidId`, `CheckOnSendMessage`,
      `CheckMessageOnHide` (which fluids a hide drains), and the oil parts (`korek_spustowy_1`, oil filter, oil cap).
      Run on `car_boltatlanta`, `car_sixoncebulion` and one car with all five fluids. Compute the D3 lock set for
      every part. Done when `docs/spikes/part-locks.md` lists the relations summary, the largest sets (fewer than 40
      keys, or the rule is narrowed in D3), the bearing cap and crankshaft pair used by `locks-connected`, and the
      part-to-fluid map of D4.
- [ ] 1.4 Re-invocation after a delay. For each D1 entry point (`ActionUnMount`, `ActionMount`, `TakeOffCarPart`,
      `SelectPartToMount`, `FluidRefill.Use`, `FluidExtractor.Use`, `UseOilbin`, `CarLifter.Action`, `ChangeCarPos`
      restarted from code, `ActionUnMountGroup(iO)`, `InsertEngineToCar(group)`), let a scratch prefix block the first
      call and re-invoke it 150 ms later. Check that it behaves like the direct call: same game mode, same
      inventory, same part state, no exception in the log. Done when D1/D7 list the `Context` fields each entry needs
      and any entry that needs a different re-invocation (for example `FluidRefill.Use` while the button is
      no longer held).
- [ ] 1.5 Pie menu and lift. Static: the `PieMenuController.GetIsAvailable(string)` lambdas for `move_*`,
      `equipment_use`, `drainTool`; how the lift's interactive object reaches `CarLifter.Action(int)`; and whether
      `NotificationCenter.ChangeCarPos(CarLoader, CarPlace, bool)` can be started from code with the pie's
      arguments (`movePlayerToCar`). Done when D7/D9 name the hooks and the restart call.
- [ ] 1.6 Input hold. `lock-trace` the unmount click: is it a hold with a fill ring (`Cursor3D.GetIsButtonHold`,
      `SetHoldID`, `fillTime`/`holdTime`), and how long it takes. Done when D10 says "hold: prefetch at hold start"
      with the measured fill time, or "click: plain wait" (task 9.3 is then dropped).

## 2. Core and server lock table

- [ ] 2.1 Core `Network/Packets/LockPackets.cs`: `CarLockRequestPacket`, `CarLockResultPacket`,
      `CarLockUpdatePacket`, `CarLockReleasePacket`, `CarLockRenewPacket` (D12), `CarLockKind`, `CarLockRefusal`, and
      `LockKeys` (`Car = "car"`, `Fluid(type, id)`, parsing helpers) next to `PartKeys`. Append the five packets to
      the end of `PacketTypes` (after whatever `main` has then). Done when the solution builds and the server
      start log shows the new packet count.
- [ ] 2.2 Server `Data/Cars/CarLocks.cs` per D5: validation, derived keys (ancestors from stored `SubParts` paths,
      `car`), the D2 compatibility table against other owners and items, away claims as X `car`, `ExtendLockId`,
      two-loader moves, release paths, renew and 90 s expiry, the 30 s cap for `Lift`/`Move`, the post-grant audit
      (`overlapViolations`), and counters. `Network/Handlers/LockHandlers.cs` for request, release and renew.
      Delete `CarClaims.cs` and `CarPartsHandlers.OnClaim`. Add the `--check-locks` self-test with the cases of D2/D3
      (same key, X/S, S/S, ancestor derivation, item conflict, away, extend, same owner, swap). Done when
      `CMS21-Together-Server.exe --check-locks` passes.
- [ ] 2.3 Server integration per D5/D7/D8: `FindConflict` rule 3 (mount flip of a key held by another player);
      release on an accepted mount flip broadcast before the relay; `CarAwayRegistry`, `EconomyRules` (car sale) and
      `CarPartsStore.Describe` ask `CarLocks`; `PlacementHandlers` refuse lift and move without the car lock while
      another player holds a lock; `CarsSnapshotProvider` sends `CarLockUpdate` per lock in the `cars` snapshot;
      expiry in the server tick; `PresenceEvents.Left`/`SceneChanged` and `LoaderCleared` release; console command
      `locks`; server setting `lock_scope = connected|part` (default `connected`; `part` drops the derived and
      client S keys except `car`), sent as `ServerInfo.LockScope` (`[OptionalField]`). Done when the solution builds and a two-client `connect` run with a scripted
      request logs grant, broadcast, renew and release, and `locks` lists it.

## 3. Client lock core

- [ ] 3.1 `Logic/Car/Locks/CarLockMirror.cs` per D6/D10/D11: records from `CarLockUpdate` through
      `ClientScene.GarageBound` (mirror-only while away), `Conflict(loader, set)`, pending requests with the 3 s
      timeout, late-grant release, renew every 30 s, reset in `ClientData.Reset`, on car delete and on `SpawnSeq`
      change, and release of own locks on disconnect and before a resync. Done when the client builds and the
      `locks` dump section (task 4.1) shows the mirror after a scripted request.
- [ ] 3.2 `LockSets.cs` per D3/D4 with the rule spike 1.3 settled, cached per loader and `SpawnSeq`, and `lock_scope`
      read from `ServerInfo` (the server sends it). Done when `lock-trace relations` prints the computed set per part
      and the sets for the spike's three cars match `docs/spikes/part-locks.md`.
- [ ] 3.3 `LockGate.cs` + `LockMessages.cs` per D1/D9: local refusal, own-lock pass, block-request-reinvoke with the
      `Context` fields of spike 1.4, `Bypass(lockId)` scope, one pending request per client, error sound, messages
      and rate limit, the "Waiting for the server…" hint after 150 ms, and no request for an action the guard
      refused. Done when the client builds and, with the verbs of 4.1, a two-client run where B holds a part
      (`lock-try … hold`) shows A's first `lock-try unmount` on it `denied` with B as holder and `ran false`, and A's
      second try refused locally (`refusedLocally` +1, no new request in the server log).
- [ ] 3.4 `PartClaims` becomes the view of D6 (`Held`, `OwnerOf`, `HeldByOther`, `ClaimChanged` from X part keys,
      `fromSnapshot`). Its Harmony patches are removed. `ClientDigests` and `EngineCraneHooks` use the mirror, and
      the harness `part-claim` verb is rewired to the lock request. Done when `visual-parts` and `car-live` still
      pass.

## 4. Harness

- [ ] 4.1 `tools/TestHarness/Features/LockCommands.cs` per D13: `lock-try` (all kinds, `hold`, `release`, `nogate`),
      `lock-release`, `lock-renew`, `lock-hover`, `lock-mount-mode`, `lock-pie`; `lock-trace` from 1.1 moves here;
      dump section `locks` with the counters. Done when `dump` shows `locks` on both clients after `connect`, and
      `lock-try unmount` on a free part reports `granted` and `ran true`.
- [ ] 4.2 `TestAreas.psm1`: area `locks`; path rows for `Logic/Car/Locks/*`, `Network/Handlers/Lock*`,
      `Network/Packets/LockPackets.cs`, `Data/Cars/CarLocks.cs` (`locks, parts, placement`) and
      `tools/TestHarness/Features/Lock*`. Register the verbs, the dump section and the area in INTEGRATION.md
      (owner row 18). Done when `Run-All -List -Changed` maps the new paths.

## 5. Part work

- [ ] 5.1 `LockHooks.cs`, parts: gates on `ActionUnMount`, `ActionMount`, `TakeOffCarPart(string)` (and the body
      mount entry found by 1.2), the group-mode caller, and the crane's `ActionUnMountGroup`/`InsertEngineToCar`;
      `CanTakeOffCarPart` postfix from the mirror; releases on `UndoMounting`/`UndoUnMounting`, the mode exits of
      1.2, the accepted commit result, and the 5-minute idle cancel (open question 2). Done when the client builds
      and `car-crane` passes.
- [ ] 5.2 Item step: `SelectPartToMount(BaseItem)` gate extends the slot lock with the item UIDs (`ExtendLockId`);
      a denial keeps the chooser open with the message. Done when `lock-try mount <key> <uid>` reports the item in
      the lock's `items` on both clients.
- [ ] 5.3 `scenarios/locks-race.ps1` (`# areas: locks, parts`) per D13: same part, started together and with
      `net-hold on` on both; same item into two slots; checks one grant, one denial naming the holder, no rejected
      `CarPartsChange`, `inventory`, `cars` and `locks` equal. Done when it passes.
- [ ] 5.4 `scenarios/locks-connected.ps1` (`# areas: locks, parts`) with the pair from 1.3: cap held → crankshaft
      denied naming the cap; sibling cap granted; crankshaft held → cap denied; any engine part held → `crane-out`
      denied. With `lock_scope = part` (server restart with the setting) the crankshaft is granted. Done when it
      passes.

## 6. Fluids

- [ ] 6.1 Gates on `FluidRefill.Use`, `FluidExtractor.Use` and `CarLoader.UseOilbin` with the fluid of D4; releases at
      `FluidRefill.Hide`, the end of `FluidExtractor._UseAnim_d__5` and of `ToolsManager._UseOilDrain_d__40` (the
      `OilBinHooks` postfix), each after `CarDetailsSync.FlushNow(loader, Fluids)`. Done when the client builds and
      `tools-car-effects` passes.
- [ ] 6.2 `scenarios/locks-fluid.ps1` (`# areas: locks, details, parts`) per D13: reservoir held → coolant fill denied;
      fill held → reservoir denied; two coolant hoses granted together; fill brake fluid on A, release, then B's
      `cardetails-fluid` level equals A's at once (not after the next poll); oil bin held → oil filter unmount
      denied. Done when it passes.

## 7. Car-level lock

- [ ] 7.1 `LifterSync`: gate `CarLifter.Action` with `Kind = Lift`, release when `isMoving` falls or after 30 s.
      `CarPlacementSync`: block `_ChangeCarPos_d__20` state 0, request `Move` (both cars for a swap), restart
      `ChangeCarPos` on grant (1.5), release at the coroutine's end. Done when `car-placement`,
      `car-placement-race` and `test-drive` still pass.
- [ ] 7.2 `scenarios/locks-car.ps1` (`# areas: locks, placement, testdrive`) per D13: B holds a part → A's lift and move
      denied, no lift state change on server or clients; A's lift granted → B's unmount denied until the lift stops;
      B holds a part → A's `away-try` refused `InUse`; `lock-try lift … nogate` refused by the server. Update
      `visual-lift` to the D7 expectation. Done when both pass.

## 8. Blocked at selection

- [ ] 8.1 `LockSelection.cs`: hover (`SetPartMouseOver`, `PartScript.SetMouseOver`, `InteractiveObject.SetMouseOver`)
      and the hover label per D9 with the hooks of 1.1. Done when `lock-hover` on a locked part reports
      `highlighted false` and the label, and on a free part `highlighted true`.
- [ ] 8.2 Mount mode and item chooser: preview prefixes, `HidePreview` on a new lock, re-show on release while still
      in mount mode, greyed items in `ChoosePartUpWindow`. Done when `lock-mount-mode` lists no preview on a locked
      slot and lists it again after the release.
- [ ] 8.3 Pie: `GetIsAvailable` postfix for the options of 1.5. Done when `lock-pie <loader> move_carLift1` reports
      unavailable while another player holds a lock on that car.
- [ ] 8.4 `scenarios/locks-select.ps1` (`# areas: locks, parts, placement, guard`, guard on `Enforce`) per D13, plus
      `blockedAtSelection` counters > 0 on B and 0 requests sent by B for the blocked attempts. Done when it passes.

## 9. Latency and late join

- [ ] 9.1 `scenarios/locks-latency.ps1` (`# areas: locks, parts, connect`) per D13: `net-delay 150` start delay;
      `net-hold out` timeout, message and late-grant release; `lock-renew off` expiry within 90 s (± 5 s); holder
      disconnect frees the part for B at once. Done when it passes.
- [ ] 9.2 `scenarios/locks-latejoin.ps1` (`# areas: locks, persistence, visuals`) per D13: B joins while A holds a part
      lock and an oil fill; B's `locks` equals the server's; B's hover and fill are blocked; `visuals.ghostsStarted`
      and `boltsActive` unchanged on B; A releases → B can act. Done when it passes.
- [ ] 9.3 Only if 1.6 found a hold: request at hold start, release on hold abort, and the gate finds the lock held.
      `locks-latency` gains a step: with `net-delay 80`, `waitedMs` after the hold completes is 0 in most tries
      (≥ 8 of 10). Done when it passes.

## 10. Scale and verification

- [ ] 10.1 `scenarios/locks-scale.ps1` (`# run-all: lane 3`, `# areas: locks, parts, placement`, two to four instances)
      per D13; add `lock-try` rows to row 11's soak action table. Done when it passes on lane 3 with four instances
      and the denial rate and median `waitedMs` are in design.md "Measurements".
- [ ] 10.2 Two-instance verification: `locks-race`, `locks-connected`, `locks-fluid`, `locks-car`, `locks-select`,
      `locks-latency`, `locks-latejoin` plus the scenarios of the `locks`, `parts`, `visuals`, `placement`,
      `testdrive`, `tools` and `details` areas and the smoke set (`Run-All -Changed`); run ids in STATUS. Done when
      all are green.
- [ ] 10.3 Docs: INTEGRATION.md (packets; retired `CarPartClaim`/`CarPartClaimUpdate`; `CarLocks` replaces
      `CarClaims` in the API table; `PartClaims` view; `lock_scope`; verbs, dump section, area), README "Playing
      together" (what a part in use looks like and the messages), `docs/spikes/part-locks.md` final, the QUESTIONS.md
      findings 1 and 4 marked fixed by row 18, and ROADMAP status. Done when `openspec validate part-locks --strict`
      passes and the change is merged.
