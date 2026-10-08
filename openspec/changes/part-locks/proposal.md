# Proposal

Revised 2026-10-07 after `review.md` (verdict "ready after fixes"; resolution in that file).

## Why

The first Steam playtest (2026-10-07, two then three players on the same car) hit race after race. All of them came
from the optimistic claims of `sync-car-parts` (row 1 D5): the client starts an action at once and sends
`CarPartClaim`. The server only tells the loser afterwards, and by then the loser's game has already run vanilla
code that must be rolled back.

- Both players mounted the same crankshaft bearing cap within a second. The server rejected one change, and the
  loser's inventory stayed out of sync until F7 (QUESTIONS.md, playtest finding 4).
- One player removed the coolant reservoir while the other filled coolant. Fluids are a polled detail section (row 4)
  with no reservation at all, so the fill landed on a car without a reservoir.
- The host moved the lift while the friend mounted a brake caliper. The ghost floated where the caliper had been and
  the real caliper stayed invisible (finding 1). Lifts, moves, parking, job end and car delete check only the away
  claim (row 13), never the part claims.
- A claim covers only one part and its `unmountWith` members. A player could unmount the crankshaft while another
  screwed a bearing cap onto it, because the two keys differ.

The user's direction (approved 2026-10-07): "lock so a player cannot even enter a part another player is using".

## What Changes

- **Strict locks.** Every action that changes a car part, a fluid, or the car's position or presence first asks the
  server. It starts only after the grant.
  - The entry point is blocked, a `CarLockRequest` is sent, and on the grant the same vanilla entry point runs
    again, after checking that the context still holds.
  - A kind-specific "started" check releases a lock at once if the game then did nothing (part not removable,
    lift already moving, no car under the cursor).
  - For the unmount hold, the request goes out when the hold starts, so the grant arrives while the ring fills and
    there is no visible wait. Other actions cost one round trip (30–100 ms over Steam relay).
  - Waiting: nothing is shown during the first 150 ms, then "Waiting for the server…". After 3 s without an answer
    the action is cancelled, and a late grant is released at once.
  - A denial plays the game's error sound and shows "<name> is working on this part".
- **Lock what belongs together.** A lock has exclusive keys (what the action changes) and shared keys (what must not
  change while it runs).
  - A part action locks exclusively the part and its `unmountWith` group, main object first.
  - It locks these shared: the part's ancestors in the part hierarchy, the parts the game itself blocks it with (the
    serialized `unblockOnUnmount` relation, both directions), the fluids it holds, gates or drains, and the car.
  - A fill or drain takes its fluid (`f:EngineCoolant.0`) exclusively, so filling coolant and removing the reservoir
    exclude each other.
  - A mount also locks the inventory items it consumes, across all cars. That includes the members of a group the
    game builds while mounting, such as a caliper with its piston. UIDs the server never saw are logged, not refused.
  - Two players can still work on sibling parts (two bearing caps on one crankshaft), because they share only their
    parent.
- **Car-level lock.** Moving a car, swapping two cars and moving a lift take the car key `car` exclusively. Every
  part or fluid lock takes it shared.
  - Parking from the garage, deleting the car and the job end that clears it are refused while another player works
    on the car.
  - Each client also refuses part work while a lift or move of that car is still running on its own screen.
  - The away claim of row 13 counts as an exclusive car lock.
- **Locks always end.** Every lock kind has a defined end:
  - the work is done or cancelled;
  - the action did not start;
  - the player backed out (chooser closed, ESC, mode change);
  - idle (60 s in the item chooser, 5 min without bolt progress);
  - disconnect, leaving the garage, or the car removed;
  - the renew expiry (`lock_expiry_seconds`, default 90).
- **Fluid levels before the release.** A new `CarDetailsSync.FlushNow` sends fluid levels before a fill's release and
  before a part change that drains a fluid. The next player starts from the real level.
- **Blocked at selection.** A part another player has locked (or a connected one) is not highlighted on hover, and
  its hover label reads "<name> is working on this part". A click on it is refused at once, without asking the
  server. Mount-mode previews, item-chooser filtering and pie-menu greying come in `part-locks-2` (open question 7).
  Until then, those paths are refused by the click check or the server with the same message.
- **Server.** `CarLocks` replaces `CarClaims` as the single runtime lock table. It grants atomically under
  `GameDataManager.StateLock`, adds the keys it can derive itself (ancestors by path segment, `car`), and checks that
  every part key exists.
  - It releases on the owner's release, on the commit after which every exclusive part reached its target state, on
    disconnect, on leaving the garage, when the car is cleared, on the owner's away grant, and on expiry.
  - `FindConflict` keeps its preconditions and inventory checks as the safety net. It also rejects a mount-state
    change of a part another player holds exclusively; flips of shared keys are only counted.
- **Order of work.** Claims keep working until the one switch-over commit that turns the gates on, so `main` never
  loses its protection in between.
- **Game hooks** (all in `dump.cs`; the runtime path is confirmed by the spikes of tasks group 1):
  - Entry points re-invoked after the grant: `PartScript.ActionUnMount()`, `ActionMount(bool)`,
    `CarLoader.TakeOffCarPart(string)`, `GameScript.SelectPartToMount(BaseItem)`, `FluidRefill.Use()`,
    `FluidExtractor.Use()`, `CarLoader.UseOilbin()`, `CarLifter.Action(int)`, `NotificationCenter.ChangeCarPos(CarLoader,
    CarPlace, bool)` (blocked in `_ChangeCarPos_d__20.MoveNext` state 0 as today),
    `NotificationCenter.ActionUnMountGroup(InteractiveObject)` and `InsertEngineToCar(GroupItem)`.
  - End and back-out signals: `UndoMounting`/`UndoUnMounting` (from `GameScript.CleanUnfinishedMount`/
    `CleanUnfinishedUnMount`), the `ChoosePartUpWindow` close paths, `FluidRefill.Hide()`, the end of
    `FluidExtractor.<UseAnim>d__5` and `ToolsManager.<UseOilDrain>d__40`, `CarLifter.isMoving` falling, and
    `GameMode.SetCurrentMode` (after the guard; the modes an action sets itself are ignored).
  - Hold prefetch: `Cursor3D` hold state (`GetIsButtonHold`, `fillTime`).
  - Selection: `PartScript.SetMouseOver(bool)`, `InteractiveObject.SetMouseOver(bool, Color)` and
    `GameScript.GetRaycastOnItemName()`/`UpdateRaycastOnItemName()`. `GameScript.SetPartMouseOver` is deliberately not
    changed.
  - Lock-set inputs (read only): `PartScript.unmountWith`/`GetUnmountWithMainObject()`, `unblockOnUnmount`,
    `FluidRefillLockType`, `IsFluidContainer()`, `CarFluid.FluidType`/`ID`, `CarPart.ConnectedParts`, the `BaseItem`
    or `GroupItem` passed to `SelectPartToMount`.
  - Every existing postfix on a gated method checks `__runOriginal` (`EngineCraneHooks.AfterUnMountGroup` today does
    not).
  - Never patched: `FluidsData` (a struct) and any method that takes `NewCarData` by value.
- **Packets:** new `CarLockRequest`, `CarLockResult`, `CarLockUpdate`, `CarLockRelease`, `CarLockRenew`, appended to
  the end of `PacketTypes` at merge time. Changed: `ServerInfo` gains `LockScope` for the server setting
  `lock_scope = connected|part`, which lets the user fall back to "same part only" without a new build.
  `CarPartClaim` and `CarPartClaimUpdate` are retired in the switch-over: their enum values stay (append-only), and no
  code sends them. `PartClaims` stays as a thin view over the new mirror, so row 17's visuals, the activity capture
  and the harness keep their API.

## Capabilities

### New Capabilities
- `car-work-locks`: locks on parts, fluids, items and whole cars, granted by the server before the work starts. Covers
  what a lock covers, how players are told, and when locks end. Includes late join and four-player contention.

### Modified Capabilities
<!-- none: openspec/specs/ is empty. When rows are archived, car-work-locks replaces sync-car-parts' requirement
"A part being worked on is reserved" (the 120 s claim timeout becomes the renew expiry). -->

## Impact

- **Core:** `Network/Packets/LockPackets.cs` (five packets, `CarLockKind`, `CarLockRefusal`, `LockKeys`),
  `PacketTypes` (appended), `ServerInfo.LockScope`.
- **Server:**
  - `Data/Cars/CarLocks.cs` (replaces `CarClaims.cs` in the switch-over), `Network/Handlers/LockHandlers.cs`;
  - changes in `CarPartsHandlers` (`FindConflict`, release on commit; `OnClaim` removed), `PlacementHandlers`,
    `ParkingHandlers`, `CarHandlers` (delete), `JobsService` (job end), `CarAwayRegistry`, `EconomyRules`,
    `CarPartsStore.Describe`, `CarsSnapshotProvider` and the `Server.cs` tick;
  - a `locks` console command, the settings `lock_scope` and `lock_expiry_seconds`, and a `--check-locks` self-test.
- **Client:**
  - new `Logic/Car/Locks/` (`CarLockMirror`, `LockSets`, `LockGate`, `LockHooks`, `LockSelection`, `LockMessages`);
  - `PartClaims` becomes a view and loses its Harmony patches;
  - `LifterSync` and `CarPlacementSync` wait for the car lock;
  - `EngineCraneHooks` (`__runOriginal`), `OilBinHooks`, `ClientDigests` and `ResyncController` use the mirror;
  - `CarDetailsSync.FlushNow` is new, and `PartChangeTracker.Send` flushes fluids first.
- **Harness:**
  - `Features/LockCommands.cs` (`lock-take`, `lock-try` with `finish`, `lock-chooser`, `lock-hover`, `lock-idle`,
    `lock-renew`, `lock-trace`, and `lock-click` if the input spike works), a `locks` dump section, and the area
    `locks`;
  - new scenarios `locks-race`, `locks-leak`, `locks-connected`, `locks-fluid`, `locks-car`, `locks-select`,
    `locks-latency`, `locks-latejoin`, and `locks-scale` (lane 3);
  - updated scenarios `visual-lift`, `visual-parts`, `visual-latejoin`, `car-live`, `economy-trades`, `test-drive`,
    `car-crane`, `tools-car-effects` and `car-placement-race`;
  - a manual checklist for the next Steam playtest.
- **Depends on** (all merged): row 1 (part keys, `PartRegistry`, `PartTransactions`, `CarPartsStore`), row 2 (lifts,
  moves, parking), row 3 (job end), row 4 (fluid details), row 5b (crane, oil bin), row 13 (`CarAwayRegistry`), row
  14a (guard), row 17 part 1 (claim consumers). It is independent of row 17 part 2 and row 15; whichever merges first
  appends its packets first.
- **Size:** L ≈ 9–10 sessions with open question 7 at its default, plus `part-locks-2` (M ≈ 3). Without the split,
  XL ≈ 12–13 sessions.

## Open questions for the user

Each has the default the draft works with.

1. **Sibling parts.** Two players may work at once on parts that share only a parent (two bearing caps, two spark
   plugs), but not on a part and the part it is fixed to. The alternative is to make every connected part
   exclusive, which is simpler but blocks the whole engine for one player. **Default:** siblings allowed
   (shared/exclusive locks).
2. **Idle holder.** A player who enters a part and walks away keeps the lock. **Default:** the holder's own game
   cancels it after 60 s with the item chooser open and nothing chosen, or after 5 minutes in the bolt view without
   progress. It uses the game's own `UndoMounting`/`UndoUnMounting`. The server's renew expiry only covers crashes
   and lost connections.
3. **Engine stand.** Parts on the engine stand (row 5a) keep today's optimistic path in this change. **Default:**
   yes, and the stand moves to the lock table in a follow-up if the next playtest shows races there.
4. **Car tools.** The welder, car paint, car wash and interior detailing take no lock: they change cosmetics, and the
   last write wins. **Default:** no lock.
5. **Hover look.** **Default:** a locked part gets no highlight and the hover label shows the message. If spike 1.1
   shows the game's coloured highlight is local and cheap, an orange highlight is used instead.
6. **Message for a connected part.** **Default:** "<name> is working on the <part name>" when the conflict is on
   another part (for example the crankshaft), and "<name> is working on this part" when it is the same part.
7. **Smaller first step (from the review).** This change would ship the locks, the click-time refusal with the
   message, and the hover highlight and label. Mount-mode previews, item-chooser filtering and pie-menu greying would
   follow in `part-locks-2` (M ≈ 3 sessions), which needs three more spikes' worth of fragile hooks.
   - Even without part 2, a click on a locked slot or pie option is refused with the same message, so no race comes
     back.
   - This gets the race fixes to the next playtest sooner, and part 2 can be shaped by what the playtest shows.
   - **Default:** yes, split.
