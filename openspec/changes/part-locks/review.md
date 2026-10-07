# Review: part-locks (row 18)

Reviewed 2026-10-07 against `change/part-locks` at `e1b07e8` (code state = `main` `ba20816`), the IL2CPP dump
(`%USERPROFILE%\CMS21-TestInstalls\native\out\il2cppdumper\dump.cs`) and the decompiles in `native\out\clean\`,
`cardetails_clean\` and `placement_clean\`. Documents only. Nothing was run.

**Verdict: ready after fixes.** The direction is right. Locks granted before the action, one server table, full-record
updates and a non-blocking grant (a denial never waits, so there is no deadlock) are the right shape for the playtest
races. Three things must change in the documents before implementation starts:

- the item-UID validation (B1);
- a lock-end rule for actions that never start, or that the player backs out of (B2);
- the task order, which leaves `main` without claims for several commits (B3).

The majors are mostly places where the design assumes a game behaviour that the code or the decompile contradicts.

Severity: **blocker** = the design as written gives wrong behaviour on a common path; **major** = a correctness or UX
hole that a playtest would hit, or a test plan that cannot prove its requirement; **minor** = should be fixed in the
documents but has a workaround; **nit** = wording or a reference.

---

## Blockers

### B1. Item validation refuses every mount that the game turns into a new group

**Where:** design D5 step 2 ("every item UID exists in `InventoryState` (`Item` otherwise)"), D3 row "`PartMount` item
step", task 5.2.

**Evidence:** commit `035887c`: "The game combines the caliper and its piston into a new inventory group while
mounting, and that group never reaches the server." Commit `e26d219`: the client had already sent the group's removal
on its own. `CarPartsHandlers.cs:110-121` therefore *ignores* a removal of a UID the server never had. The part
transaction opens only in the `DoMount` postfix (`PartHooks.cs:14-16` → `PartTransactions.OpenForPart`), after the
chooser has built the group (`ChoosePartUpWindow.SelectItemInCreateGroup`/`SubmitGroupItem`, dump 478406/478409). So
the `BaseItem` that reaches `SelectPartToMount` can be a group UID that is unknown to the server, and its members may
already be removed there.

**Effect:** with D5 step 2, every caliper-with-piston mount (the playtest's own case) is denied with `Item`. The player
can never mount it.

**Recommendation:** use the rule of `035887c`. An unknown item UID is not a refusal: the server locks only the UIDs it
knows and logs the rest. Also lock the member UIDs that the server still has (read them from the `GroupItem` at
`SelectPartToMount`). Add a step to spike 1.2: log the item/group UIDs at `ChoosePartUpWindow.Show`, at
`SubmitGroupItem` and at `SelectPartToMount` for a caliper with a piston. Add this case to `locks-race` (it is the
second half of finding 4).

### B2. A lock ends only on success or undo; every action that does not start leaks a renewed lock

**Where:** D1 steps 3–4, D5 "Release", D10 "Holder idle", tasks 5.1/6.1/7.1.

**Evidence:** after the grant, the re-invoked vanilla method often returns without doing anything:
- `PartScript.ActionUnMount` returns early on `canMountUnmountOnlyOnCarLoader && !IsOnCarLoader`, on
  `canBeUnmount == false` (error sound + `Cursor3D.ResetButton`) and when `CheckSendMessage()` is true
  (`clean/PartScript$$ActionUnMount.c`, first 40 lines).
- `PartScript.ActionMount` plays the error sound when the part is not unmounted, and returns when `!showMenu`
  (`clean/PartScript$$ActionMount.c`).
- `CarLifter.Action` returns while `isMoving` (`placement_clean/CarLifter$$Action.c:58`).
- `FluidRefill.Use` returns when `GetIOMouseOverCarLoader2()` is null (`cardetails_clean/FluidRefill$$Use.c`).
- The oil bin's first step ends without a change when there is no oil or plug (`OilBinHooks.cs:6-7`).

Separately, `ActionMount` only opens `ChoosePartUpWindow` (same decompile). A player who opens the chooser to see
what fits and then presses ESC causes no `UndoMounting` that the design names. Clients renew every 30 s, so the 90 s
expiry never fires for such a lock. The only remaining end is the 5-minute idle cancel, which is defined as "without
bolt progress" and does not fit a chooser or a refused call. Meanwhile, that lock holds `car` shared (no lift, no
move, no test drive), the part's ancestors and blockers, and its fluids. Today's claim has the same leak but only on
one key and for at most 120 s; this change makes it wider and longer.

**Recommendation:** make the lock lifecycle explicit per kind, as a table in D1/D5:
1. **Did it start?** Right after the re-invoked call returns (still inside `Bypass`), check a kind-specific "started"
   predicate. If it is false, release at once. Candidates for the spike to confirm: unmount → `GameScript` holds
   the part as selected-to-unmount / mode changed; mount → `ChoosePartUpWindow` is shown; refill →
   `FluidRefill.IsActive`; lift → `isMoving`; move → coroutine state advanced; oil bin → first step yielded.
2. **Back-out signals:** `ChoosePartUpWindow.Hide` (or `BackAction`/`HideAction`) without a following
   `SelectPartToMount`, ESC out of the bolt view, `FluidRefill.Hide`/extractor end even without a level change.
   Spike 1.2 must name these, not only the paths into `CleanUnfinished*`.
3. **Idle cancel** keyed to "no started work for N s" for the chooser phase (for example 60 s), separate from the
   5-minute bolt idle.
4. Add a `locks-leak` check (or a step in `locks-race`) that drives the open-chooser-then-close path and the refused
   `ActionUnMount` path, and asserts that the server's `locks` is empty within 1 s.

### B3. The task order leaves `main` without claims between groups 2 and 5

**Where:** task 2.2 ("Delete `CarClaims.cs` and `CarPartsHandlers.OnClaim`"), task 3.4 ("Its Harmony patches are
removed … Done when `visual-parts` and `car-live` still pass"), task 5.1 (the gates).

**Evidence:** after 2.2 the client still sends `CarPartClaim` (`PartClaims.cs:61`), but the server no longer handles
it. After 3.4 the client sends nothing at all until 5.1 adds the gates. `BoltReplay` and `PartGhosts` start their
remote work visuals from `ClaimChanged` at the start of the work (`BoltReplay.cs:36-62`). So `visual-parts`, the
acceptance check of 3.4, cannot pass at that point. `car-live`, `economy-trades` and `test-drive` use `part-claim`
(`car-live.ps1:65`, `economy-trades.ps1:102`, `test-drive.ps1:99`). The user's rule is a commit per finished group,
which makes each of these intermediate states a commit on the branch.

**Recommendation:** move the deletion of `CarClaims`/`OnClaim` and the removal of the `PartClaims` patches into
5.1, the commit that turns the gates on. Until then, `CarLocks` runs beside `CarClaims`, fed only by the harness. Or
merge 3.4 and 5.1 into one task. 3.3's "Done when" also uses the verbs of 4.1. Move 4.1 before 3.3, or make 3.3's
check a server-log check.

---

## Majors

### M1. Hold input: the blocked call may repeat every frame, and "button still held" drops normal clicks

**Where:** D1 step 3 (`Context` includes `Cursor3D.GetIsButtonHold()` for hold actions), D1 "Only one request is
pending per client. A new action cancels the old pending one", D10.

**Evidence:** the unmount click is a hold with a fill timer (`Cursor3D.fillTime`/`holdTime`/`cursorTimerImage`,
dump 429946ff). Vanilla `ActionUnMount` ends the hold itself: it sets `Cursor3D.isHoldInProgress = false` on
success, and calls `ResetButton` on refusal (`clean/PartScript$$ActionUnMount.c`, the `isHoldInProgress` and
`Cursor3D__ResetButton` lines). A prefix that returns `false` does neither. If `Raycast.PartSelect` calls
`ActionUnMount` again while the hold is still complete, the gate sends a new request every frame, and "a new action
cancels the old pending one" means no grant is ever used. Separately, a player releases the button right after the
ring fills. A grant that arrives 30–100 ms later then fails the "still held" context, and the click silently does
nothing.

**Recommendation:**
- A repeated call for the same kind and target while a request is pending is swallowed (no new request). Only a
  *different* target cancels the pending one.
- On block, call `Cursor3D.ResetButton()` as vanilla does on refusal.
- Remove "button still held" from `Context` for actions that fire when the hold completes. Keep it only for actions
  whose effect lasts as long as the hold (the refill pour, which `FluidRefillLogic.Update` reads anyway).
- Treat D10's prefetch at hold start as the main design, not an optimization. The fill time is longer than a relay
  round trip, so prefetch removes the visible wait entirely. Spike 1.6 then only measures the fill time.

### M2. Hover: passing `null` to `SetPartMouseOver` removes the label and the click refusal

**Where:** D9 table, rows "Hover" and "Hover label"; spec "Hovering a part in use".

**Evidence:** `SetPartMouseOver(PartScript)` (dump 431958) is the game's "what is under the cursor" state. `ActionMount`
itself calls it (`clean/PartScript$$ActionMount.c`, `GameScript__SetPartMouseOver(pGVar4,pPVar17,0)`). With `null`,
the label code reads no part (so it shows no message), and `Raycast.PartSelect` has nothing to click, so the spec's
"click refused locally with the error sound and message" never runs.

**Recommendation:** leave the mouse-over part as it is. Suppress only the highlight (`PartScript.SetMouseOver(bool)`
/ `InteractiveObject.SetMouseOver(bool, Color)` prefix), and override the label text. The click then reaches the gate,
which refuses with the message. Spike 1.1 should decide this, and its "Done when" should state it. Also exempt your
own locks (the `SetPartMouseOver` call inside `ActionMount` runs on your own locked part).

### M3. The lift lock ends while other clients' lifts are still moving

**Where:** D7 "Lift" (release when the local `isMoving` falls).

**Evidence:** remote clients animate the lift later, in `LifterSync.Apply` (`LifterSync.cs:60-101`): they wait while
`isMoving`, may step through several states, and each step takes seconds. The requester's lift stops roughly one
one-way latency before B's. B's mirror shows the release at about the same moment, and B can then start a part on a
car that is still moving *on B's screen*. That is finding 1's situation, in a smaller window. It grows when A presses
twice (two steps queued on B).

**Recommendation:** add a local check to the gate and to the selection checks. Refuse part work locally while any
local `CarLifter` connected to that car `isMoving`, or while `LifterSync`/`CarPlacementSync` is applying a remote step
for it. The check is cheap, needs no protocol and closes the window on every client. Extend `locks-car` with
`net-delay 150` on B.

### M4. The car lock does not cover parking, a finished job or deleting the car

**Where:** proposal "Car-level lock", D7. Only moves, swaps, lifts and car sale are listed.

**Evidence:** `ParkingHandlers.ParkFromGarage` refuses only when the car is away (`ParkingHandlers.cs:31-55`), then
`ClearLoader(… Parked)`. `JobsService.cs:213-215` clears a job car when another player finishes the job.
`CarHandlers.cs:71` deletes. In each case another player's work disappears in the middle: an open `PartTransactions`
entry, a held item, and a half-done mount.

**Recommendation:** add server-side refusals with `CarLocks.HeldByOther(loader, client)` (refusal `Busy`, same as
`EconomyRules.CarSale`) to park-from-garage, job end and car delete, with a client message. Parking is "moving the
car" from the player's point of view, and that is what the user asked to forbid. Add a step to `locks-car`.

### M5. Fluid levels drained by a part unmount reach the server after the lock is released

**Where:** D4 (flush before release only for refill, extractor and oil bin), D5 release on commit, spec "When a fluid
lock ends, the fluid level SHALL already be on the server".

**Evidence:** `PartScript.<Hide>d__159` sets `IsUnmounted = true` and zeroes coolant, washer and power-steering fluid
in its first step (`clean/PartScript._Hide_d__159$$MoveNext.c:130, 740-825`). Fluids travel in the 1 Hz polled
section (`CarDetailsSync.cs:22, 108-118`, with a 0.5 s flush delay), while the part change commits after 3 × 0.1 s
of stability (`PartChangeTracker.cs:16-17`). So the server releases the reservoir lock (the commit) up to about 1.5 s
before the drained level arrives. During that window a coolant fill by another player starts from the stale full
level, and the drainer's late poll then overwrites the fill (last write wins). Also, `CarDetailsSync.FlushNow`, listed
as an existing row 4 API in proposal "Impact" and D4, does not exist. `Flush` is private and returns without sending
while the loader is `awaiting`/`applying` (`CarDetailsSync.cs:128-131`).

**Recommendation:** add `CarDetailsSync.FlushNow(loader, sections)` as a task, with a defined result when it cannot
send. Call it for `Fluids` in `PartChangeTracker.Send` before the change is sent whenever the change flips a part
whose lock holds an `f:` key. The details packet and the change then go out in order, ahead of the release. Make
`locks-fluid` check the level right after a reservoir unmount, not only after a refill.

### M6. Existing postfixes on gated methods now run on every action's first, blocked call

**Where:** D1 (every first call is blocked), D8 "Crane".

**Evidence:** Harmony postfixes run even when a prefix returns `false`. Today a blocked call is rare; with strict
locks, it is the first call of *every* action. `EngineCraneHooks.AfterUnMountGroup` (`EngineCraneHooks.cs:37-50`)
does not check `__runOriginal`. On the blocked call it searches the inventory for a group with the engine's name,
sends an `InventoryGroupItemAction Add` for any match (for example another engine of that type), logs "engine taken
out" and marks the loader dirty. `EngineCraneEffects` already checks `__runOriginal` (`EngineCraneEffects.cs:13,20`).
Others to audit: `PartHooks.AfterTakeOffCarPart` (`PartHooks.cs:26-28`, harmless), `LifterSync.AfterAction` (based on
state, harmless) and the harness trace patches.

**Recommendation:** add a rule to D1 and a task item: every existing postfix on a gated method checks `__runOriginal`
(list them in `docs/spikes/part-locks.md`). Fix `EngineCraneHooks.AfterUnMountGroup` in 5.1.

### M7. The test plan cannot prove the input-driven requirements, and two checks prove nothing

**Where:** D13, tasks 5.3–9.2, 10.1, spec "A part in use cannot be selected", "Four players contend correctly",
"Locks always end".

**Evidence and gaps:**
- `lock-try` and `lock-hover` call the entry point or the hover method directly. Nothing in the harness drives
  `Raycast.PartSelect`/`Cursor3D` (there is no `harness-mouse-over` verb; scenarios set
  `GameScript.IOMouseOverCarLoader` directly, `CarCommands.cs:223`). So M1 (hold), M2 (label vs `null`) and "the hook
  fires on the real path" (spike 1.1's "Done when") cannot be shown by any scenario.
- D13 does not say how a `lock-try unmount` *finishes*. `ActionUnMount` enters the bolt view and waits for input,
  which is why the existing verbs use `HideBySavegame`/`FastUnmount` (`CarCommands.cs:193-226`). Without a finish
  step, `locks-scale`'s "no rejected `CarPartsChange`" is true because no change is sent, and release-on-commit is
  never tested.
- No scenario covers the 5-minute idle cancel, release on `LoaderCleared` (sale, park, job end), the chooser back-out
  (B2) or the commit-time order of fluid and release (M5).
- `lock-renew off` waits 90 s or more per run.

**Recommendation:**
- Add `lock-try … finish`, which completes the started action under the held lock (`PartScript.ActionAutomatic`, dump
  `_ActionAutomatic_d__106`, or `FastUnmount`/`FastMount` inside the bypass), and use it in `locks-race`,
  `locks-scale` and `locks-latejoin`.
- Add a server setting `lock_expiry_seconds` (default 90) so `locks-latency` runs in about 15 s.
- Add the missing steps listed above.
- Add a short manual checklist to 10.2 for the next Steam playtest: hover label, hold-to-unmount with 100 ms ping,
  chooser ESC, pie greying, refill pour. Those paths are only real with a mouse.

---

## Minors

1. **The `FindConflict` rule 3 on S keys creates new rollbacks** (D8). A change flips a key outside its own X only as
   a game side effect, and rejecting it runs the fragile rollback of finding 4. Reject only flips of another player's
   **X** keys. Count flips of S keys (`unlockedFlip`) and log them.
2. **The blocking relation should come from `unblockOnUnmount`, not `blockedBy`** (D3, D4, task 1.3).
   `blockedBy` is a private runtime list that `UnblockBlockParts(bool)` changes on hide and mount (dump 438786,
   438937; the harness picks parts by `!IsBlocked()` at runtime, `CarCommands.cs:213`). A set computed from it
   changes with state, which contradicts D3's cache per `SpawnSeq`. The serialized `unblockOnUnmount` (dump 438757)
   plus a reverse index is stable and covers both directions.
3. **Release on commit when only part of X has flipped.** `ShowMounted` mounts `unmountWith` members through
   `MountByGroup` (`clean/PartScript._ShowMounted_d__155$$MoveNext.c:56-90`), so the tracker can commit the main
   object first. Release when every X key has reached its target state (the server knows the kind), or let the client
   release after its tracker has sent the last X flip.
4. **The own-lock pass (D1 step 2) is open for one round trip after your own commit.** Your mirror still shows the
   lock until the release returns. Restrict step 2 to the chained step of the same lock (`ActionMount` →
   `SelectPartToMount`), and mark your own lock as ending once a change with an X flip has been sent. D1 step 2 also
   contradicts D3 and task 5.2 (the item step sends an `ExtendLockId` request). Pick one.
5. **Swap locks** use `car` for two loaders in one record (D3, D12). Keys are scoped to one car, so `X = ["car"]` is
   ambiguous. Grant two linked records atomically (one per loader). The snapshot, the mirror index and
   `PartClaims.Held` then need no special case.
6. **Pie menu:** `GetIsAvailable(string)` is private and returns an IL2CPP `Func<bool>` (dump 440003). Wrapping it
   needs `DelegateSupport.ConvertDelegate`, and the guard already toggles the same options in a `PrepareIcons` postfix
   and restores "was enabled" (`GuardHooks.cs:48-73`). Reuse the guard's pattern (`SetEnableOption` at
   `PrepareIcons`, refusal in `CheckSelectedOption`) through one shared owner of option state. Two owners would
   restore each other's values.
7. **Item chooser:** `ChoosePartUpWindow.items` is `InventoryItemDetails[]` (UI rows, dump 478346). Filter the input
   of `Show(List<BaseItem>, …)` (dump 478376) instead. Task 5.2's "a denial keeps the chooser open" is likely
   impossible if the window hides before `SelectPartToMount`. Let spike 1.2 confirm, and otherwise reopen it with the
   message.
8. **Releases on mode changes** (D5, Risks) must ignore the modes that the gated action sets itself (the unmount view,
   the extractor's mode 0x17, mode 7 in `CarLifter.Action` at `placement_clean/CarLifter$$Action.c:181-183`) and the
   changes the guard refuses (`GuardHooks.cs:105-113` runs first). Spike 1.2 should list the mode transitions that end
   each kind.
9. **Going away** (row 13): today the away grant releases the requester's own claims
   (`CarAwayRegistry.cs:71-72`). Say what happens to your own locks (release them all).
10. **Client state on leaving the garage:** the server releases your locks on `SceneChanged`. The client must also
    drop its pending request and its own-lock bookkeeping there, not only on disconnect and resync (D10, D11).
11. **`ClientDigests` skips a car with any lock** (D8). Locks now last longer (chooser, tool in hand), so a car that
    four players work on is rarely checked. Skip only while a lock *of this client* or an unconfirmed change exists, or
    accept this and say so.
12. **Spike 1.1** should also cover `Raycast.InteriorDisassemble`/`InteriorAssemble`/`PartUnMountPartMount` (dump
    Raycast methods). Interior parts go through the same `PartScript` path.
13. **`part-claim` rewiring** (D6): `economy-trades` and `test-drive` use it as a bare reservation. `lock-try` runs
    the real action, which is a different meaning. Keep a bare `lock-take <loader> <kind> <key>` (request only) for
    them, and add the `economy` area to 10.2 (`EconomyRules` changes).
14. **Size:** L ≈ 7–8 sessions is optimistic for 6 spikes, about 15 hook sites, 8 new scenarios, a lane-3 run and
    reworked visuals. 10–12 is more realistic. See "Simpler path" below.

## Nits

- Ancestor derivation (D5 step 3) must compare path **segments** (`3.22` is an ancestor of `3.22.4`, `3.2` is not),
  not string prefixes.
- Give the order of X: main object first, then members. `BoltReplay` and `ActivityCapture` use `keys[0]`
  (`BoltReplay.cs:46`, `ActivityCapture.cs:116`).
- `lock_scope = part` also drops the fluid S keys, so "reservoir vs fill" is no longer protected. Say this in the
  risk row.
- `[OptionalField]` on `ServerInfo.LockScope` is moot while client and server must match the protocol hash.
  Harmless.
- `CanTakeOffCarPart` has an `out TakePartOffLockReason` (dump 428911). The postfix could set a reason so that the
  game's own message path is used.
- D13 refers to a `harness-mouse-over` verb that does not exist.

## Checked and fine

- All hooks named in proposal/D14 exist in the dump with the stated shapes: `PartScript.ActionUnMount()`,
  `ActionMount(bool)`, `ShowPreviewToMount/ShowGroupPreview/ShowPreview/HidePreview`, `SetMouseOver(bool)`,
  `IsFluidContainer()`, `unmountWith`, `unblockOnUnmount`; `GameScript.SetPartMouseOver`, `GetRaycastOnItemName`,
  `UpdateRaycastOnItemName`, `SelectPartToMount(BaseItem)`, `CleanUnfinishedMount/UnMount`, `SelectedToMount`,
  `ForcePartGroup` (a `PartScript` field), `GetUnmountGroup()`; `InteractiveObject.SetMouseOver(bool, Color)`;
  `CarFluid { FluidType, ID, HasReservoir }`; `CarPart.ConnectedParts`; `NotificationCenter.ChangeCarPos(CarLoader,
  CarPlace, bool)` (`IEnumerator`), `ActionUnMountGroup(InteractiveObject)` and `InsertEngineToCar(GroupItem)`
  (void); `CarLoader.UseOilbin()` and `TakeOffCarPart(string)` (void); `CarLifter.Action(int)`;
  `FluidRefill.Use/Hide`, `FluidExtractor.Use`, `<UseAnim>d__5`, `ToolsManager.<UseOilDrain>d__40`.
- None of these take `NewCarData` or `FluidsData` by value. `DoMount`, `Hide`, `ShowMounted` and `UnMountByGroup` are
  coroutines, and the design gates them at their void callers, as it should. `FluidRefill.Use` reads the hover target
  and `CurrentUsedFluidId` (`cardetails_clean/FluidRefill$$Use.c`), as spike 1.4 expects.
- Release before relay is kept (`CarPartsHandlers.cs:66-73` is already in that order), so row 17's assumption holds.
- `net-hold on/out` and `net-delay` behave as `locks-race`/`locks-latency` need (`NetHoldCommands.cs`).

## Simpler path (optional, for the user)

Most of the playtest value comes from groups 2, 3, 5, 6, 7 and 9 with the **click-time** refusal and message. Those
alone fix the bearing-cap, coolant and lift races. The parts of group 8 that need many fragile hooks (mount-mode
previews, chooser greying, pie greying: three spikes' worth of hooks and verbs) could move to a `part-locks-2`. Keep
only "no highlight + label" from group 8, if spike 1.1 shows that both hooks fire. The user asked to be "blocked at
selection", so this is a scope choice for the user, not a recommendation to drop it silently.
