# Tasks

> **Resume here (2026-10-08, 08:00).** Branch `change/part-locks`, worktree `CMS21-Together-wt/part-locks`.
>
> - **Done and committed:** groups 1-4, 5.1 (switch-over `562aa10`), 5.2, 5.3; `locks-connected`. `63b6837` leaves
>   audit gap 3 to row 19. `005182f` (wip) holds `LockSelection` (9.1), `LockPrefetch` (10.1) and `lock-hover`.
> - **In the working tree, built, not run yet (lane 1 was the soak's until about 12:30):**
>   - 6.1/6.2: the chooser-built group mounts through `NotificationCenter.MountGroup`, which needs the `mountGroup`
>     that `Raycast.PartSelectMount` sets (spike doc 1.2); the gate restores it on the re-invoked `ActionMount`.
>     Extensions never release the lock they extend; `lock-try … mount <key> group <uid…>`; `locks-race` caliper step.
>   - 7: `LockFluidHooks` (refill, extractor, oil bin; release after `FlushNow`), `FlushBeforeChange` in
>     `PartChangeTracker.Send`; `locks-fluid`; `lock-try fill|drain|oil`, `lock-watch`, `lock-fluid`, `lock-tool-end`.
>   - 8: `LockCarHooks` (lift in `LifterSync`, move and swap in `CarPlacementSync`), Busy messages; `locks-car`
>     (with P8), `visual-lift` updated, `locks-leak` gains prefetch abort, refill without a car and moving lift.
>   - 9.2: `locks-select` (with `lock-click` label and mouse-over recording). P10 in `locks-race`, X4 in `resync-key`.
> - **Next step:** `Run-Session -Lane 1 -Deploy -Scenarios tools-car-effects,tools-latejoin,visual-activity,visual-latejoin,visual-lift,visual-parts,economy-latejoin,car-gone-inflight,test-drive`
>   (5.1's remaining proof), then `locks-race`, `locks-fluid`, `locks-car`, `locks-leak`, `locks-select`,
>   `resync-key`, `car-placement`, `car-placement-race`, `car-details`; then the smoke set; then commit per group.

**Row 18: playtest fix, landing before the next Steam playtest.** It fixes findings 1 and 4 of 2026-10-07, plus the
coolant and connected-part races.

- **Prerequisites** (all on `main`): rows 1, 2, 4, 5b, 13, 14a and 17 part 1.
- **Size:** L ≈ 9–10 sessions with open question 7 at its default (mount-mode, chooser and pie greying move to
  `part-locks-2`, M ≈ 3). If question 7 is answered "no", group 12 stays here and the change is XL ≈ 12–13 sessions.
- **Order rule [review B3]:** claims keep working until the switch-over commit (task 5.1). Until then, the new lock
  table runs beside them and only the harness feeds it.
- **Spikes:** runtime facts the static decompile cannot give are spikes (group 1). A spike that changes a decision
  updates design.md in the same commit; findings go to `docs/spikes/part-locks.md`.
- **Scenarios:** each carries `# areas: locks, …` and checks the server's `locks` command for `overlapViolations` 0.
- **Commits:** one conventional commit per finished task group.

## 1. Spikes

- [x] 1.1 Hover and selection path.
      - Static decompile into `native/out/locks_clean`: `Raycast.Garage`, `PartSelect`, `PartSelectMount`,
        `GarageAssemble`, `InteriorDisassemble`, `InteriorAssemble`, `PartUnMountPartMount`,
        `GameScript.SetPartMouseOver`, `UpdateRaycastOnItemName`, `GetRaycastOnItemName`,
        `PartScript.SetMouseOver(bool)`, `SetMouseOver()`, `MouseOverGroup()`, `Flashing`,
        `InteractiveObject.SetMouseOver(bool, Color)`, and the UI that reads the label.
      - Add the logging-only verb `lock-trace on|off|report` and run it while hovering five parts, two body panels
        and one interior part (by `lock-click` if 1.7 works, else by a person on a visible test game).

      Done when design.md D9 names, for each row of its table, the hook that fires on the real path. It must state
      that the highlight is suppressed while `GetPartMouseOver()`, the label and the click stay intact, and whether
      the coloured highlight is local only (open question 5).
      **Done (2026-10-07):** `PartScript.SetMouseOver()` (no arguments) for the highlight, `GameScript.SetPartMouseOver` for the label, both on the real path through `lock-click`; body and interior hooks static only. See `docs/spikes/part-locks.md` 1.1 and design.md D9; runs `20261007-214438`, `20261007-215206`, `20261007-215950` (`locks-probe`).
- [x] 1.2 Mount flow, item UIDs and back-outs.
      - Static: `ActionMount(bool)`, `ChoosePartUpWindow.Show(…)`, `SelectItemInCreateGroup`, `SubmitGroupItem`,
        `Hide`/`BackAction`/`HideAction`, `GameScript.SelectPartToMount(BaseItem)`, where `SelectedToMount` is set,
        the callers of `CleanUnfinishedMount`/`CleanUnfinishedUnMount`, the void caller of `UnMountByGroup`,
        `GetUnmountGroup`/`ForcePartGroup`, and the game modes each gated action sets itself.
      - Runtime with `lock-trace`, logging item and group UIDs at `Show`, `SubmitGroupItem` and `SelectPartToMount`:
        mount a disc; mount a caliper with a piston (group built in the chooser); open the chooser and ESC; ESC out of
        the bolt view; a mode change mid-unmount; a group unmount.

      Done when design.md D5's lifecycle table has a confirmed "started" predicate and back-out signal per part kind,
      the self-set modes are listed, and D3's item step states which UIDs reach `SelectPartToMount` for a group.
      **Done (2026-10-07):** started predicates and back-outs in design.md D5; a chooser-built group deletes its items when they are picked, so the item step gates `SelectItemInCreateGroup` (design.md D3). `docs/spikes/part-locks.md` 1.2; runs `20261007-214438`, `20261007-215206`, `20261007-215950` (`locks-probe`).
- [x] 1.3 Relations and lock-set sizes.
      - `lock-trace relations <loader>` dumps, for every registry part: key, id, `PartScript` ancestors,
        `unmountWith`/main object, `unblockOnUnmount` and the reverse index, `CarFluid` below it, `FluidRefillLockType`,
        `IsFluidContainer()`, and the fluids its hide drains (`<Hide>d__159`). For body parts it dumps
        `ConnectedParts`.
      - Static: `AddToBlockedBy`, `CheckIsFluidContainer`, `GetFluidId`, `CheckOnSendMessage`, the oil parts.
      - Run on `car_boltatlanta`, `car_sixoncebulion` and one car with all five fluids, and compute the D3 set for
        every part.

      Done when `docs/spikes/part-locks.md` lists the relations summary, the largest sets (under 40 keys, or D3 is
      narrowed), the bearing cap and crankshaft pair for `locks-connected`, and D4's part-to-fluid map.
      **Done (2026-10-07):** largest set 21 keys, none over 40; crane narrowed to the key `engine`; pair crankshaft `s:13.5` / caps `s:13.65`–`s:13.67`; fluid map. `docs/spikes/part-locks.md` 1.3; `relations-*.tsv` in runs `20261007-214438`, `20261007-215206`, `20261007-215950` (`locks-probe`).
- [x] 1.4 Re-invocation, started predicates and finishers.
      - A scratch prefix blocks the first call of each D1 entry point and re-invokes it 150 ms later. Entry points:
        `ActionUnMount`, `ActionMount`, `TakeOffCarPart`, `SelectPartToMount`, `FluidRefill.Use`,
        `FluidExtractor.Use`, `UseOilbin`, `CarLifter.Action`, `ChangeCarPos`, `ActionUnMountGroup(iO)`,
        `InsertEngineToCar(group)`.
      - Record whether it behaves like the direct call, and the value of its started predicate, including the
        refused cases (`canBeUnmount == false`, a moving lifter, no car under the cursor, no oil).
      - Pick the finisher per kind for `lock-try … finish`.

      Done when D1's `Context` fields, D5's started column and D13's finisher are confirmed or corrected.
      **Done (2026-10-07):** every entry point re-invoked after 150 ms behaves like the direct call; finisher = bolt loop plus `Hide`/`ShowMounted`. `docs/spikes/part-locks.md` 1.4; runs `20261007-214438`, `20261007-215206`, `20261007-215950` (`locks-probe`).
- [x] 1.5 Lift and move. How the lift's interactive object reaches `CarLifter.Action(int)`. Whether
      `NotificationCenter.ChangeCarPos(CarLoader, CarPlace, bool)` can be restarted from code with the pie's
      arguments. The pie option ids for `part-locks-2`. Done when D7 names the restart call and the lift hook.
      **Done (2026-10-07):** lift hook `CarLifter.Action`; move restart `StartCoroutine(ChangeCarPos(carLoader, place, movePlayer))` after setting `previousMode`. `docs/spikes/part-locks.md` 1.5.
- [x] 1.6 Hold. `lock-trace` the unmount click: `Cursor3D.GetIsButtonHold`, `SetHoldID`, `fillTime`/`holdTime`,
      where a hold start over a part is visible, and how long the fill takes. Done when D10 records the fill time
      and the hold-start hook, and the fill time is longer than 150 ms (else D10's prefetch is dropped and recorded).
      **Done (2026-10-07):** hold start = `Cursor3D.canCountTime` rising over a part; full after 1134 ms (150 ms + `fillTime` 1000 ms); prefetch stays. `docs/spikes/part-locks.md` 1.6; run `20261007-215950`.
- [x] 1.7 Input shim for `lock-click`: a harness patch on `Cursor3D.GetIsButtonHold`/`GetIsButtonClick` plus a camera
      aimed at the part drives `Raycast.PartSelect`, the hover highlight, the label and the hold path in a headless
      test game. Done when `lock-click` unmounts a free part through the game's own raycast (`lock-trace` shows
      `Raycast.PartSelect` → `ActionUnMount`), or design.md D13 records that it does not work and the input checks
      stay manual (task 11.3).
      **Done (2026-10-07):** `lock-click` drives `Raycast` → hover → hold → `ActionUnMount` headless, after moving the part to layer `Part` and enabling its `PartScript`. `docs/spikes/part-locks.md` 1.7; run `20261007-215950`.

## 2. Core and server lock table (beside the claims)

- [x] 2.1 Core `Network/Packets/LockPackets.cs`: the five packets of D12, `CarLockKind`, `CarLockRefusal`, and
      `LockKeys` (`Car`, `Fluid(type, id)`, path-segment ancestry helpers) next to `PartKeys`. Append the packets to
      `PacketTypes` (after whatever `main` has then), and add `ServerInfo.LockScope`. Done when the solution builds
      and the server start log shows the new packet count.
      **Done (2026-10-07):** builds; the server logs "53 handlers and 94 packets registered" (run `20261007-215950`). Also `LockKeys.Engine` (spike 1.3) and `ParkRefusal.Busy`.
- [ ] 2.2 Server `Data/Cars/CarLocks.cs` and `Network/Handlers/LockHandlers.cs` per D5:
      - validation, with unknown item UIDs logged and not refused [B1];
      - segment-based ancestor derivation, the D2 table with linked swap records, away claims as X `car`;
      - `ExtendLockId`/phases, release paths, renew and `lock_expiry_seconds`, the 30 s `Lift`/`Move` cap;
      - the post-grant audit, the counters, and `--check-locks` (same key, X/S, S/S, ancestor vs. look-alike prefix,
        known and unknown item, item conflict, away, extend, same owner, linked swap).

      `CarClaims` stays. Done when `CMS21-Together-Server.exe --check-locks` passes and the existing `parts` area is
      still green.
      **In code (2026-10-07):** `CarLocks.cs`, `CarLocksCheck.cs`, `LockHandlers.cs`; `--check-locks` passes (20 checks). The `parts` area run is still open.
- [ ] 2.3 Server integration, asking both `CarClaims` and `CarLocks` until 5.1:
      - `FindConflict`: reject a mount flip of another player's X key; count S flips as `unlockedFlip`;
      - release on an accepted commit once all X keys reached their target state, broadcast before the relay;
      - `CarAwayRegistry` (InUse, and its grant releases the requester's own locks on that car), `EconomyRules`
        (car sale), `PlacementHandlers` (lift and move need the car lock while another player holds a lock);
      - `ParkFromGarage`, `CarDelete` and the clearing job end refused with `Busy` while another player holds a lock
        [M4];
      - `PresenceEvents.Left`/`SceneChanged` and `LoaderCleared` releases, expiry in the tick, `CarPartsStore.Describe`;
      - the `locks` console command, and the settings `lock_scope` and `lock_expiry_seconds`.

      `CarsSnapshotProvider` sends `CarLockUpdate` for lock records next to today's claim updates. Done when the
      solution builds, and a two-client run with `lock-take` (3.1) logs grant, broadcast, renew, release and expiry
      (`lock_expiry_seconds = 10`), and `locks` lists them.
      **In code (2026-10-07):** every item above; refusals without a request reach the client as `CarLockResult { RequestId = 0, Refusal = CarBusy }` (design.md D12). Not run yet (needs `lock-take`, 3.1).

## 3. Harness (before the client gate)

- [x] 3.1 `tools/TestHarness/Features/LockCommands.cs` per D13: `lock-take`, `lock-try` (all kinds, `hold`,
      `finish`, `release`, `nogate`), `lock-chooser`, `lock-hover`, `lock-release`, `lock-renew`, `lock-idle`;
      `lock-trace` moves here; dump section `locks` with the counters. Until 5.1, `lock-try` reports `not gated` for
      kinds whose gate is not in yet. Done when `dump` shows `locks` on both clients after `connect`, and `lock-take`
      on a free part shows the record in both mirrors and in the server's `locks`.
      **Done (2026-10-07):** `LockCommands.cs`, `LockTryCommands.cs`, dump section `locks`; `locks-basic` passes (`20261007-232118`).
- [x] 3.2 `TestAreas.psm1`: area `locks`; path rows for `Logic/Car/Locks/*`, `Network/Handlers/Lock*`,
      `Network/Packets/LockPackets.cs`, `Data/Cars/CarLocks.cs` (`locks, parts, placement, economy`) and
      `tools/TestHarness/Features/Lock*`. INTEGRATION.md: verbs, dump section, area, settings (owner row 18). Done
      when `Run-All -List -Changed` maps the new paths.
      **Done (2026-10-07):** area `locks` and its path rows; `Run-All -List -Changed` maps them; INTEGRATION.md updated.
- [x] 3.3 Only if 1.7 succeeded: `lock-click <loader> <key> [hold <ms>]` with the shim. Done when `lock-click` on a
      free part unmounts it through `Raycast.PartSelect`.
      **Done (2026-10-07):** `lock-click` came with spike 1.7 (`LockTraceCommands.cs`).

## 4. Client lock core (not wired to game hooks yet)

- [x] 4.1 `Logic/Car/Locks/CarLockMirror.cs` per D6/D10/D11:
      - records from `CarLockUpdate` through `ClientScene.GarageBound`, and `Conflict(loader, set)`;
      - pending requests with the 3 s timeout, late-grant release, renew every `expiry / 3`;
      - reset on `ClientData.Reset`, car delete, a `SpawnSeq` change and leaving the garage (pending and own
        bookkeeping too), and release of own locks before a resync.

      Done when the `locks` dump section shows the mirror after `lock-take`, and a `net-hold out` of 4 s on the
      requester ends with `timeouts` 1, `lateGrantsReleased` 1 and an empty server `locks`.
      **Done (2026-10-07):** `CarLockMirror`; `locks-basic` checks the mirrors against the server, the 4 s stall (`timeouts` 1, `lateGrantsReleased` 1) and renew/expiry.
- [x] 4.2 `LockSets.cs` per D3/D4 (from `unblockOnUnmount` plus the reverse index, segment ancestry, X order with the
      main object first, `LockScope` from `ServerInfo`), cached per loader and `SpawnSeq`. Done when
      `lock-trace relations` prints the sets and they match `docs/spikes/part-locks.md` for the spike's three cars.
      **Done (2026-10-07):** `LockSets` (crane key `engine`, spike 1.3).
- [x] 4.3 `LockGate.cs` and `LockMessages.cs` per D1/D9:
      - local refusal with `Cursor3D.ResetButton()`, prefetched or chained pass only, swallowed repeats, cancel on a
        different target;
      - the `Bypass` scope, `Context`, the started check, error sound, rate-limited messages, the 150 ms waiting hint;
      - no request for an action the guard refused.

      Done when `lock-try unmount` on a part B holds (`lock-take`), driven through the gate by a harness-only test
      hook, reports `denied`, `ran false` and B as holder. A second try reports `refusedLocally` +1 with no new
      request in the server log.
      **Done (2026-10-07):** `LockGate`, `LockMessages`, `CarMotion`; `locks-race` (denied, ran false, holder) and `locks-connected` (refusedLocally, no request on the server).
- [x] 4.4 `CarDetailsSync.FlushNow(loader, sections) → FlushResult` per D4 (`Sent`, or `Deferred` with the 1 s wait
      and a log line). Done when a harness call during an `applying` window logs `Deferred`, then sends within 1 s,
      and `car-details` still passes.
      **Done (2026-10-07):** `FlushNow`; `locks-basic` sees Deferred and the send within 1 s. Polled and flushed fluid updates carry only the changed fluids (audit note 1).

## 5. Switch-over: part work through the gate

- [x] 5.1 One commit that turns the gates on and the claims off [B3]:
      - `LockHooks.cs` gates `ActionUnMount`, `ActionMount`, `TakeOffCarPart(string)` (and the body mount entry of
        1.2), the group-mode caller, and the crane's `ActionUnMountGroup`/`InsertEngineToCar`;
      - the D5 lifecycle for these kinds (started check, end and back-out signals, mode exits that ignore self-set
        modes, idle cancels of 5 min and 60 s);
      - the `__runOriginal` audit of D1, with `EngineCraneHooks.AfterUnMountGroup` fixed;
      - `PartClaims` becomes the view (its patches and `Claim` removed);
      - `CarClaims.cs`, `OnClaim` and the claim updates in the snapshot are deleted, and every server check asks only
        `CarLocks`;
      - `ClientDigests` per D6 (digests keep running while locks are held; skip only during an open transaction or an
        unsent or unconfirmed change, playtest finding 6), and the scenarios `car-live`, `economy-trades`, `test-drive` changed from
        `part-claim` to `lock-take`.

      Done when `visual-parts`, `visual-latejoin`, `car-live`, `car-race`, `car-crane`, `economy-trades` and
      `test-drive` pass.
      **Done (2026-10-07, commit 562aa10):** `visual-parts`, `visual-latejoin`, `car-live`, `car-race`, `economy-trades` (`20261007-2222`–`2226`), `car-crane`, `test-drive` (`20261007-223134`, `223201`) pass.
- [x] 5.2 `scenarios/locks-race.ps1` (`# areas: locks, parts`) per D13, with the same part and the same item; the
      caliper-with-piston group case follows in 6.2. Done when it passes.
      **Done (2026-10-07):** `locks-race` passes (same part in parallel, same part with incoming held, same item into two slots).
- [x] 5.3 `scenarios/locks-leak.ps1` (`# areas: locks, parts, details, placement`) per D13 [B2]: chooser open and
      close, refused `ActionUnMount`, a refill with no target, a lift that is already moving, an aborted prefetch
      (once 10.1 is in), and the idle cancels with `lock-idle 5 3`. Each ends with an empty server `locks` within
      1 s. Done when it passes (the prefetch step is added in 10.1).
      **Done (2026-10-07):** `locks-leak` passes (chooser close, refused unmount, mount on a mounted part, idle cancels); refill, lift and prefetch steps follow with 7.1, 8.1 and 10.1.

## 6. Connected parts and items

- [ ] 6.1 Item step: the `SelectPartToMount(BaseItem)` gate sends `ExtendLockId` with the item UID, or the group UID
      and its member UIDs (B1). A denial shows the message, and the slot phase stays until its back-out or idle. Done
      when `lock-try mount <key> <uid>` for a caliper-with-piston group shows the known member UIDs in the lock's
      `items` on both clients, and the mount commits (`finish`).
- [ ] 6.2 `scenarios/locks-connected.ps1` (`# areas: locks, parts`) per D13, with the pair from 1.3. `locks-race`
      gains the caliper-with-piston step: A and B mount the same group into two calipers' slots, one is granted,
      and no item is lost or duplicated. Done when both pass.
- [ ] 6.3 Row 19 review P10: `locks-race` gains a body-panel step (both players take off the same door or hood at
      once; one commits, the other is refused, the panel reaches the inventory once). Done when `locks-race` passes.

## 7. Fluids

- [ ] 7.1 Gates on `FluidRefill.Use` (no button in `Context`, design.md D1), `FluidExtractor.Use` and `CarLoader.UseOilbin`, with the
      D5 lifecycle and release after `FlushNow`. `PartChangeTracker.Send` flushes `Fluids` before a change that flips
      a part whose lock holds an `f:` key [M5]. Done when `tools-car-effects` and `car-details` pass.
- [ ] 7.2 `scenarios/locks-fluid.ps1` (`# areas: locks, details, parts`) per D13, including the order check that the
      server's level after a reservoir unmount is 0 before B sees the release. Done when it passes.

## 8. Car-level lock

- [ ] 8.1 `LifterSync` gate (`Kind = Lift`, release when the local `isMoving` falls, 30 s cap). `CarPlacementSync`
      gate (block state 0, `Move` with linked records for a swap, restart `ChangeCarPos`, release at the end). The
      local moving check of D7 in the gate and the hover check [M3]. Done when `car-placement`,
      `car-placement-race` and `test-drive` pass.
- [ ] 8.2 Client messages for the `Busy` refusals of park, delete and job end (2.3), through each feature's existing
      refusal path. Done when `purchases` and `jobs` still pass and a scripted park of a car B works on shows
      the message on A.
- [ ] 8.3 `scenarios/locks-car.ps1` (`# areas: locks, placement, testdrive, jobs`) per D13, including `net-delay 150`
      on B for the lift window and the park, delete and job-end steps. `visual-lift` is updated to D7's
      expectation. Done when both pass.
- [ ] 8.4 Row 19 review P8: `locks-car` ends with a car removed while its owner holds a lock on it: no lock is left on
      the server or in either mirror, and a part of the next car on that loader can be locked (and is refused to the
      other player). Done when `locks-car` passes.

## 9. Hover

- [ ] 9.1 `LockSelection.cs`: highlight suppression (`PartScript.SetMouseOver(bool)`,
      `InteractiveObject.SetMouseOver(bool, Color)`), the label override, interior parts, own locks exempt, and
      `GameScript.SetPartMouseOver` untouched [M2]. Done when `lock-hover` on a locked part reports
      `highlighted false` with the label, and `highlighted true` on a free part.
- [ ] 9.2 `scenarios/locks-select.ps1` (`# areas: locks, parts, guard`, guard on `Enforce`) per D13, with the
      `lock-click` steps if 3.3 exists. Done when it passes.

## 10. Latency and late join

- [ ] 10.1 Prefetch at hold start per D10 (unless 1.6 dropped it): the request at hold start over a free part, the
      release on abort or move-off, and the gate using the prefetched lock. The abort step is added to `locks-leak`.
      Done when `locks-leak` passes, and with `lock-click … hold` at `net-delay 80`, `waitedMs` is 0 in at least 8 of
      10 tries.
- [ ] 10.2 `scenarios/locks-latency.ps1` (`# areas: locks, parts, connect`) per D13, with `lock_expiry_seconds = 10`.
      Done when it passes in under 2 minutes.
- [ ] 10.3 `scenarios/locks-latejoin.ps1` (`# areas: locks, persistence, visuals`) per D13. Done when it passes.
- [ ] 10.4 Row 19 review X4: `resync-key` gains a lock step: A and B each hold a part lock when B resyncs; B's lock is
      released, A's stays on the server and is back in B's mirror after the reload, and A's release reaches B. Done when
      `resync-key` passes.

## 11. Scale, verification and docs

- [ ] 11.1 `scenarios/locks-scale.ps1` (`# run-all: lane 3`, `# areas: locks, parts, placement`, two to four
      instances) per D13, with `lock-try … finish`. Add `lock-try` rows to row 11's soak action table. Done when it
      passes on lane 3 with four instances and the denial rate and median `waitedMs` are in design.md
      "Measurements".
- [ ] 11.2 Two-instance verification:
      - `locks-race`, `locks-leak`, `locks-connected`, `locks-fluid`, `locks-car`, `locks-select`, `locks-latency`,
        `locks-latejoin`;
      - the scenarios of the `locks`, `parts`, `visuals`, `placement`, `testdrive`, `tools`, `details`, `jobs` and
        `economy` areas;
      - the smoke set (`Run-All -Changed`).

      Done when all are green and their run ids are in STATUS.
- [ ] 11.3 Manual playtest checklist (D13) in STATUS.md and `docs/try-it.md`, with what to report for each item. Done
      when it is in both files and the user has it for the next Steam playtest.
- [ ] 11.4 Docs:
      - INTEGRATION.md: packets, the retired `CarPartClaim`/`CarPartClaimUpdate`, `CarLocks` replacing `CarClaims`
        in the API table, the `PartClaims` view, `CarDetailsSync.FlushNow`, settings, verbs, dump section, area;
      - README "Playing together" (what a part in use looks like, the messages);
      - `docs/spikes/part-locks.md` in its final form;
      - findings 1 and 4 in QUESTIONS.md marked as fixed by row 18;
      - `part-locks-2` added to the ROADMAP (if question 7 keeps its default), and the ROADMAP status.

      Done when `openspec validate part-locks --strict` passes and the change is merged.

## 12. Selection in mount mode, chooser and pie (only if open question 7 is answered "no"; otherwise `part-locks-2`)

- [ ] 12.1 Mount-mode previews (`ShowPreviewToMount`/`ShowGroupPreview`/`ShowPreview` prefix, `HidePreview` on a new
      lock, re-show on release). Done when a `lock-mount-mode` verb lists no preview on a locked slot and lists it
      again after the release.
- [ ] 12.2 Item chooser: filter the input of `ChoosePartUpWindow.Show(List<BaseItem>, …)`, and reopen with the message
      after a denied item step if the window has closed. Done when `lock-chooser open` on B leaves out the items in
      A's lock.
- [ ] 12.3 Pie: one shared owner of option state for the guard and the locks (`PrepareIcons`/`CheckSelectedOption`).
      Done when a `lock-pie <loader> move_carLift1` verb reports unavailable while another player holds a lock on that
      car, and `guard` still passes.
- [ ] 12.4 `locks-select` gains the mount-mode, chooser and pie steps. Done when it passes.
