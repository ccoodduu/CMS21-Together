# Proposal

## Why

The first Steam playtest (2026-10-07, two then three players on the same car) hit race after race, all from the
optimistic claims of `sync-car-parts` (row 1 D5): the client starts an action at once and sends `CarPartClaim`; the
server only tells the loser afterwards, and the loser's game has already run vanilla code that must be rolled back.

- Both players mounted the same crankshaft bearing cap within a second. The server rejected one change, and the
  loser's inventory stayed out of sync until F7 (QUESTIONS.md, playtest finding 4).
- One player removed the coolant reservoir while the other filled coolant. Fluids are a polled detail section (row 4)
  with no reservation at all, so the fill landed on a car without a reservoir.
- The host moved the lift while the friend mounted a brake caliper. The ghost floated where the caliper had been and
  the real caliper stayed invisible (finding 1). Lifts and car moves check only the away claim (row 13), never the
  part claims.
- Claims cover one part and its `unmountWith` members. A player could unmount the crankshaft while another screwed a
  bearing cap onto it, because the two keys differ.

The user's direction (approved 2026-10-07): "lock so a player cannot even enter a part another player is using".

## What Changes

- **Strict locks.** Every action that changes a car part, a fluid or the car's position first asks the server and
  starts only after the grant. The entry point is blocked, a `CarLockRequest` is sent, and on `CarLockResult
  { Granted }` the client runs the same vanilla entry point again under a bypass scope, after checking that the
  context is still the same. The cost is one round trip (30–100 ms over Steam relay). Nothing is shown during the
  first 150 ms, then "Waiting for the server…". After 3 s without an answer the action is cancelled, and a late grant
  is released at once. A denial plays the game's error sound and shows "<name> is working on this part".
- **Lock what belongs together.** A lock has exclusive keys (what the action changes) and shared keys (what must not
  change while it runs). A part action takes the part and its `unmountWith` group exclusively. It takes these shared:
  the part's ancestors in the part hierarchy, the parts the game itself blocks it with (`blockedBy`,
  `unblockOnUnmount`), the fluids it holds, gates or drains (`CarFluid`, `FluidRefillLockType`), and the car. A fill
  or drain takes its fluid (`f:EngineCoolant.0`) exclusively, so filling coolant and removing the reservoir exclude
  each other. A mount also locks the inventory item it consumes, across all cars. Two players can still work on
  sibling parts, such as two bearing caps on one crankshaft, because they share only their parent.
- **Car-level lock.** Moving a car, swapping two cars and moving a lift take the car key `car` exclusively, and every
  part or fluid lock takes it shared. A lift cannot move while someone works on the car, and nobody can start work
  while a lift or the car moves. The away claim of row 13 counts as an exclusive car lock.
- **Blocked at selection.** A part another player has locked (or a connected one) is not highlighted on hover. Its
  hover label reads "<name> is working on this part", its mount-mode preview is hidden, the pie-menu options that move
  or lift a locked car are unavailable, and items another player is mounting are greyed out in the item chooser. A
  click is refused locally without a request. All of this is a local view of the server's lock mirror. The server
  stays the referee.
- **Server.** `CarLocks` replaces `CarClaims` as the single runtime lock table. The server grants atomically under
  `GameDataManager.StateLock`, adds the keys it can derive itself (ancestors from part paths, `car`) and checks that
  every key exists. Locks are released on the owner's release, on the commit that flips an exclusive part, on
  disconnect, on leaving the garage, when the car is cleared, or after 90 s without a renew (clients renew every 30 s).
  `FindConflict` keeps its preconditions and inventory checks as the safety net and also rejects a mount-state change
  of a key another player holds. Lifter and car-move requests are refused unless the sender holds the car lock.
- **Game hooks** (all in `dump.cs`; the runtime path is confirmed by the spikes in tasks group 1):
  - Entry points re-invoked after the grant: `PartScript.ActionUnMount()`, `ActionMount(bool)`,
    `CarLoader.TakeOffCarPart(string)`, `GameScript.SelectPartToMount(BaseItem)`, `FluidRefill.Use()`,
    `FluidExtractor.Use()`, `CarLoader.UseOilbin()`, `CarLifter.Action(int)`, `NotificationCenter.ChangeCarPos(CarLoader,
    CarPlace, bool)` (blocked in `_ChangeCarPos_d__20.MoveNext` state 0 as today), `NotificationCenter.ActionUnMountGroup(InteractiveObject)`
    and `InsertEngineToCar(GroupItem)`.
  - Release signals: `UndoMounting`/`UndoUnMounting` (from `GameScript.CleanUnfinishedMount`/`CleanUnfinishedUnMount`),
    `FluidRefill.Hide()`, the end of `FluidExtractor.<UseAnim>d__5` and of `ToolsManager.<UseOilDrain>d__40`,
    `CarLifter.isMoving` falling, and `GameMode.SetCurrentMode` (already patched by the guard).
  - Selection: `GameScript.SetPartMouseOver(PartScript)`, `GameScript.UpdateRaycastOnItemName()`/`GetRaycastOnItemName()`,
    `PartScript.SetMouseOver(bool)`, `InteractiveObject.SetMouseOver(bool, Color)`, `PartScript.ShowPreviewToMount()`/
    `ShowGroupPreview()`/`ShowPreview()`/`HidePreview()`, `PieMenuController.GetIsAvailable(string)`, and the
    `ChoosePartUpWindow` item list.
  - Lock-set inputs (read only): `PartScript.unmountWith`/`GetUnmountWithMainObject()`, `blockedBy`,
    `unblockOnUnmount`, `FluidRefillLockType`, `IsFluidContainer()`, `CarFluid.FluidType`/`ID`,
    `CarPart.ConnectedParts`, `GameScript.SelectedToMount`.
  - Never patched: `FluidsData` (a struct; see the row 4 spike) and any method that takes `NewCarData` by value.
- **Packets:** new `CarLockRequest`, `CarLockResult`, `CarLockUpdate`, `CarLockRelease` and `CarLockRenew`, appended to
  the end of `PacketTypes` at merge time. Changed: `ServerInfo` gains `LockScope` (`[OptionalField]`), the server
  setting `lock_scope = connected|part` that lets the user fall back to "same part only" without a new build.
  `CarPartClaim` and `CarPartClaimUpdate` are retired: their enum values stay
  (append-only), no code sends them, and the server's handler is removed. `PartClaims` stays as a thin view over the
  new mirror, so the visuals of row 17, the activity capture and the harness keep their API.

## Capabilities

### New Capabilities
- `car-work-locks`: server-granted locks on parts, fluids, items and whole cars, taken before the work starts. Covers
  what a lock covers, how players are told, and when locks end. Includes late join and four-player contention.

### Modified Capabilities
<!-- none: openspec/specs/ is empty. When rows are archived, car-work-locks replaces sync-car-parts' requirement
"A part being worked on is reserved" (the 120 s claim timeout becomes the 90 s renew expiry). -->

## Impact

- Core: `Network/Packets/LockPackets.cs` (`CarLockRequestPacket`, `CarLockResultPacket`, `CarLockUpdatePacket`,
  `CarLockReleasePacket`, `CarLockRenewPacket`, `CarLockKind`, `CarLockRefusal`, `LockKeys` with the `car`, `f:` and
  item helpers next to `PartKeys`), `PacketTypes` (appended), `ServerInfo.LockScope`.
- Server: `Data/Cars/CarLocks.cs` (replaces `CarClaims.cs`), `Network/Handlers/LockHandlers.cs`; changes in
  `CarPartsHandlers.FindConflict` and `OnClaim` (removed), `PlacementHandlers` (lift and move need the car lock),
  `CarAwayRegistry` (asks `CarLocks` instead of `CarClaims`), `EconomyRules` (car sale), `CarPartsStore.Describe`,
  `CarsSnapshotProvider` (locks in the `cars` snapshot), `Server.cs` tick (expiry), a `locks` console command and a
  `--check-locks` self-test.
- Client: new `Logic/Car/Locks/` (`CarLockMirror`, `LockSets`, `LockGate`, `LockHooks`, `LockSelection`,
  `LockMessages`). `PartClaims` becomes a view over the mirror and loses its Harmony patches. `LifterSync` and
  `CarPlacementSync` wait for the car lock. `EngineCraneHooks`, `OilBinHooks`, `ClientDigests` and `ResyncController`
  use the mirror. `GuardHooks` keeps its own role, and the lock prefixes run only for actions the guard allowed.
- Harness: `Features/LockCommands.cs`, a `locks` dump section, the area `locks`, and the scenarios `locks-race`,
  `locks-connected`, `locks-fluid`, `locks-car`, `locks-select`, `locks-latency`, `locks-latejoin` and `locks-scale`
  (lane 3). Updated scenarios: `visual-lift`, `visual-parts`, `car-crane`, `tools-car-effects`, `car-placement-race`.
- Depends on (all merged): row 1 (part keys, `PartRegistry`, `PartTransactions`, `CarPartsStore`), row 2 (lifts,
  moves), row 4 (fluid details, `CarDetailsSync.FlushNow`), row 5b (crane, oil bin), row 13 (`CarAwayRegistry`),
  row 14a (guard), row 17 part 1 (claim consumers). It is independent of row 17 part 2 and row 15. Whichever merges
  first appends its packets first.

## Open questions for the user

Each has the default the draft works with.

1. **Sibling parts.** Two players may work at once on parts that share only a parent (two bearing caps, two spark
   plugs), but not on a part and the part it is fixed to. Alternative: every connected part is exclusive, which is
   simpler but blocks the whole engine for one player. **Default:** siblings allowed (shared/exclusive locks).
2. **Idle holder.** A player who enters a part and walks away keeps the lock. **Default:** the holder's client
   cancels the action (the game's own `UndoUnMounting`/`UndoMounting`) after 5 minutes without bolt progress. The
   server's 90 s renew expiry only covers crashes and lost connections.
3. **Engine stand.** Parts on the engine stand (row 5a) keep today's optimistic path in this change. **Default:**
   yes, and the stand moves to the lock table in a follow-up `part-locks-2` if the next playtest shows races there.
4. **Car tools.** The welder, car paint, car wash and interior detailing take no lock (they change cosmetics, last
   write wins). **Default:** no lock.
5. **Hover look.** **Default:** a locked part gets no highlight and the hover label shows the message. If spike 1.1
   shows the game's coloured highlight (`InteractiveObject.SetMouseOver(bool, Color)`, `PartScript.Flashing`) is
   local and cheap, an orange highlight is used instead.
6. **Message for a connected part.** **Default:** "<name> is working on the <part name>" when the conflict is on
   another part (for example the crankshaft), and "<name> is working on this part" when it is the same part.
