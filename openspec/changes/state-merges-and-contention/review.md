# Review: state-merges-and-contention (row 19)

Reviewed 2026-10-07 against `change/state-merges` at `cd9a12c` (code state = `main` `d1dd908`), row 18 on
`change/part-locks` (`8d9b9bc`, plus the uncommitted edits in its worktree at review time), the IL2CPP dump
(`%USERPROFILE%\CMS21-TestInstalls\native\out\il2cppdumper\dump.cs`) and the decompiles in `native\out\clean\` and
`cardetails_clean\`. Documents only. Nothing was run.

**Verdict: ready after fixes.** The split into state merges and detection is right, and the field-group mask is the
right tool for gap 3: it carries the base information that a three-way merge needs without any history on the
server. Per-entry details, the server-side `Returned` for machine puts and the reconciliation rules are the right
shape too. Two things must change in the documents before implementation starts:

- the receiving side of part changes must be driven by what the server changed, not by a comparison with
  `LoaderSync` (B1);
- the per-entry echo rule must not skip an own echo that is the server's last write (B2).

The majors are a missing base check in the merge, the row 18 interplay (its rule and its work in progress), two
regressions (parking, the removed-item memory) and a test plan whose contention mode is replayable only in its
inputs.

Severity: **blocker** = the design as written gives wrong behaviour on a common path; **major** = a correctness or UX
hole that a playtest would hit, or a test plan that cannot prove its requirement; **minor** = should be fixed in the
documents but has a workaround; **nit** = wording or a reference.

---

## Blockers

### B1. The receiving side decides from `LoaderSync`, which holds the client's own unconfirmed sends and none of its unsent edits

**Where:** design D2, D1 step 3 ("relay the stored records"), D14 `car-stale-record` steps 2 and 3, task 3.3.

**Evidence:**
- `PartChangeTracker.Send` writes the *sent* records into `LoaderSync` before any answer
  (`PartChangeTracker.cs:145-146`). `LoaderSync` is "what this client last sent or applied", not "the server's
  state".
- Receivers apply every relayed record whole and then store it as known (`PartChanges.cs:123-137`,
  `PartApplier.cs:51-86`). After that the tracker compares the game with the relayed record
  (`PartChangeTracker.cs:98-99`).
- A part transaction for an unmount opens only when `Hide` starts (`PartHooks.cs:10-12` → `OpenForPart`), that is
  after the last bolt. `vfx-unscrew … pause` stops in the bolt loop, before `Hide`
  (`tools/TestHarness/Features/VisualCommands.cs:323-356`).

**Effect:**
1. **Own unconfirmed send (step 2 of `car-stale-record`).** B unmounts k and sends it while held. A's examine is
   stored first and relayed to B with k mounted (the stored state). On B, that record differs from `LoaderSync` (B's
   own sent "unmounted") in `Mount`, so D2 aborts and applies it: k is re-mounted on B. B's accepted result then
   returns the merged record (it differs from B's in `IsExamined`) and hides k again. Within 0.5 s that is P11:
   `ShowMounted` re-enables renderers and colliders without rechecking (`PartApplier.cs:90-96`), so B is left with a
   visible ghost of a part that is unmounted everywhere. If the merged record happens to equal B's sent record,
   nothing is returned and B keeps k mounted with its item in the inventory.
2. **Unsent local attribute edit.** A receiver whose own condition, dust or examine change of that key has not yet
   passed the tracker's three stable polls gets the merged record whole. The game value is overwritten, `LoaderSync`
   takes the relayed value, the tracker sees no difference, and the edit is never sent. This is the parts twin of
   the details lost update that D6 fixes.
3. **Bolt phase.** While B unscrews, no transaction is open and the game's mount state equals `LoaderSync`, so B is
   not "in local work". D2 then writes bolts (`SetMountObjectData`, `PartApplier.cs:71`) in the middle of the
   unscrew. Step 3's check "B's `parts.transactions` still shows the open transaction" is false by construction, so
   task 3.3 cannot pass as written.

**Recommendation:** relay what the server changed.
- After the merge, the server sets `Changed` on every relayed and returned record to the groups the merge actually
  wrote from this change (a mount record: all groups). A `None`/whole value stays for snapshots and resyncs only.
- Receivers write only the masked groups, update `LoaderSync` per group, and abort a local transaction only when the
  mask contains `Mount`, `Identity` or `Switched`. The sender applies the returned records the same way.
- This replaces D2's "in local work" heuristics: an attribute relay never touches mount state, bolts or identity,
  whatever the local state. A mount relay keeps today's behaviour (abort and apply), which is right because the
  local precondition will fail anyway.
- `car-stale-record` step 3 asserts through `vfx-unscrew` status (still `paused`, k mounted on B, bolt progress
  unchanged), then `resume` and the commit. Add a step for the unsent attribute edit (B `part-condition` within its
  0.3 s window while A's examine arrives). Add a `visuals` check to step 2 (no ghost).
- Move `PartTransactions.AbortFor` in `OnRemoteChange` after the revision check (`PartChanges.cs:46` runs before
  `:56`, so a dropped stale relay still aborts local work).

### B2. Skipping the own echo per entry breaks convergence when two players write the same entry

**Where:** design D7, spec "Same fluid by two players", `details-concurrent` "same entry" step.

**Evidence:** the server applies updates in arrival order and relays each to every client, the sender included
(`CarDetailsStore.cs:52-67`). The client skips own echoes older than its latest send and applies everything else
(`CarDetailsSync.cs:146-177`).

**Effect:** A sends brake 0.5 (seq 5). B's brake 0.8 reaches the server first, so the server ends at 0.5, and B gets
0.5. On A: B's 0.8 arrives and is applied (A shows 0.8), then A's own echo of seq 5 arrives with 0.5. Its signature
equals the kept copy, so D7 drops it. A stays at 0.8, the server and B at 0.5: a permanent desync that only part 2's
`car-details` digest can find. Today the latest own echo is applied and this case converges, so D7 makes it worse.
The existing "older than the latest send → skip" rule has the same hole across two sends, and D7 keeps it.

**Recommendation:** decide per entry.
- When a foreign update of entry e is applied, mark e in every kept send copy.
- An own echo applies an entry when the server changed it (clamp) **or** when that entry was marked (a foreign write
  came in between: the echo is the server's later write). Otherwise it is dropped.
- Apply the same rule to older own echoes instead of skipping them whole.
- `details-concurrent` runs the same-entry step in both orders, deterministically (`net-hold out` on both, release A
  then B, and B then A, waiting for the server log line in between), and checks that A, B and the server agree.

---

## Majors

### M1. A stale attribute change is applied to the part that replaced it

**Where:** D1 step 2, spec "Examine on a part another player has just replaced", D1 "Alternatives considered".

**Evidence:** the merge copies the masked groups onto whatever is stored. A welder or condition change made on A's
stale view of the old part (mask `Condition`) is copied onto the new part that B mounted meanwhile. An examine (mask
`Examined`) marks the new part as examined. The design names the replacement case as the reason to prefer masks
over the audit's "keep `Unmounted`", but the merge still writes A's condition onto B's new part.

**Recommendation:** use the base information the record already carries. If the mask lacks `Mount` and `Identity`,
the incoming record's `Unmounted`, `PartId` and `TunedID` are the sender's base. Apply the masked attribute groups
only when they equal the stored ones. Otherwise keep the stored record, and count and log it (`staleDropped`). Add
the case to `--check-merges` and a step to `car-stale-record` (B unmounts and mounts a new part of another quality
while A welds or examines). With this rule the first spec scenario no longer marks an unmounted part examined; adjust
its wording.

### M2. Row 18's lock rule rejects a stale examine instead of merging it

**Where:** D3 ("Row 18's `FindConflict` rule … runs before the merge and is unchanged").

**Evidence:** row 18 rejects "a record whose `Unmounted` flips on a key another player holds in X". On the branch it
compares the record with the stored state (`FlippedKeys` in `CarPartsHandlers.cs` of `change/part-locks`). A lock
can outlive the commit of one of its keys: release waits until every X key reached its target (row 18 minor 3). In
that window a stale examine carries `Unmounted = false` for a key the server has as unmounted and still locked. It
is rejected ("locked by player N"), and the examiner runs the rollback path that D1 wants to avoid.

**Recommendation:** in D3, define "flip" for row 18's rule as "`Mount` is in `Changed`, or the change has a
precondition for that key", and run the merge normalisation before the lock check. Count `unlockedFlip` on the same
definition. Add the case to `--check-merges`, or as step 4b of `car-stale-record` after task 8.1.

### M3. Row 18 is already building narrower versions of parts of this change

**Where:** design Context, D1, D4, D8, tasks 3.2, 4.2, 8.2; proposal open question 6.

**Evidence:** at review time, the row 18 worktree has uncommitted edits that:
- add `KeepStoredMountState` in `CarPartsHandlers.OnChange`, run before `FindConflict`. It keeps the stored
  `Unmounted` for records without a precondition and returns them. This is the audit's gap 3 server fix, which D1's
  alternatives reject.
- make every `CarDetailsSync.Flush` send only changed fluids (`OnlyChanged` against the previously remembered fluid
  list), not only `FlushNow`;
- add `PartTransactions.DropLoader`, called from `CarPartsSync.OnCarDeleted` (part of the audit's client half of gap
  5), and `CarDetailsStore.SendTo`.

This change's Context describes `main` and does not know these pieces. Groups 3 and 4 would then rewrite them a
second time, and the "whichever merges second adapts" note in D8 covers only `FlushNow`.

**Recommendation:** agree on one owner before either branch goes further. The cheapest path: row 18 keeps
`FlushNow` and the fluid-only send (it needs both for `locks-fluid`) and drops `KeepStoredMountState` (it has no
proving scenario there, and it is the version D1 replaces). Row 19's Context lists what row 18 ships, and tasks 3.2
and 4.2 say "replaces `OnlyChanged`" and "replaces `KeepStoredMountState`" if it stays. M2's ordering note then
applies to whatever row 18 merges.

### M4. Parking with the server's records loses the parker's own unsent change and can duplicate its item

**Where:** D10, spec "A parked car keeps every accepted change".

**Evidence:** the tracker sends a change only after three stable 0.1 s polls (`PartChangeTracker.cs:16-17, 110-124`).
Details go out after a 0.5 s delay on a 1 Hz poll (`CarDetailsSync.cs:22-23, 108-126`). Once the park clears the
loader, `Poll` drops it (`:82-86`), so the change is never sent. The open transaction then either flushes its
inventory delta as plain packets after 10 s (`PartTransactions.cs:206-220`: the item reaches the server while the
parked record still has the part mounted, so after the unpark the part is mounted *and* the item exists), or, with
row 18's `DropLoader`, it is dropped (the item exists only on the parker). Today the blob carries the parker's own
change, so D10 trades the other player's lost change for the parker's own. The same holds for a fluid filled less
than about 1.5 s before the park.

**Recommendation:** the client drains before it sends `CarParkRequest`. It waits (at most 1 s) until
`PartChangeTracker.IsPending(loader)` is false and no transaction is open or unconfirmed for that loader, then calls
`FlushNow(loader, All)`, then sends. If the 1 s runs out, it refuses locally with "Try again in a moment". Same
stream, so the server has everything before the park. Add a `park-stale` step: A unmounts k and parks within 0.2 s;
after the unpark, k is unmounted and the item exists once. (The same drain is worth having before `car-delete`.)

### M5. Remembering removed items for only 60 s weakens the existing "removed by another player" check

**Where:** D9 ("`InventoryChanges` keeps, for each removed UID, the remover, the time and a copy … for 60 s"), task
5.1.

**Evidence:** today `removedBy` never expires (`InventoryChanges.cs:11-21`). `FindConflict` rejects a removal of a
UID that another player removed, and otherwise logs "ignored" and accepts (`CarPartsHandlers.cs:116-128`). A mount
keeps its item in the open transaction until the commit, and a bolt job can take more than 60 s. If B sells the item
at second 0 and A's mount commits at second 70, an expired entry turns today's rejection into an accepted mount:
the item is sold and mounted.

**Recommendation:** keep the remover entries for the session, bounded by count (for example the last 10 000 UIDs),
as today. Only the item copy that `Returned` needs expires after 60 s. Also, `EconomyService.RemoveItem` notes
`UnknownRemover` (-1) for scrap, barn maps and quality upgrades (`EconomyService.cs:145-150`), so "<name> used this
part" has no name for those. Pass the client id through `EconomyOutcome.Effect`.

### M6. The contention mode is replayable in its inputs only, and several kinds have no verb

**Where:** D13, D14, tasks 10.2 and 10.3, spec "Replay of a contention".

**Evidence and gaps:**
- `net-hold on` holds incoming packets only (`NetHoldCommands.cs:11-14, 110-120`). Each member's change leaves as soon
  as its own tracker sends it (three 0.1 s polls in a separate process), so which member reaches the server first
  depends on poll phase, not on the seeded release order. With `net-hold out` and 0 ms gaps, two processes' TCP
  writes race. A replay therefore repeats the kind, members, targets and release order but not who won. Task 10.2's
  "Done when" (the same `contend` markers are written) holds for any replay and proves nothing.
- Targets with UIDs (`give-group`, `item-where`) differ per run; the marker needs the `{step:N:…}` templates that
  `Invoke-Machines` already uses (`soak.ps1:253-255`).
- Task 10.3's negative control ("a run with the gap 9 fix reverted fails rule 7 on `machine-same-item`") relies on the
  seeded mix drawing that kind in 10 minutes.
- Missing verbs. There is no warehouse verb in the harness at all, which `item-trades`, the `warehouse` digest's "not
  ready" rule and `state-corrupt warehouse` need. `diag-examine` returns only a count (`TestDriveCommands.cs:274-285`),
  but `examine-vs-unmount` and `car-stale-record` need to pick k from the tool's list. `sell-item` and `econ-scrap`
  take single items only (`SessionCommands.cs:12-21`, `EconomyCommands.cs:333-345`), but the tire changer needs a
  group, so "tool-put against sell-item" cannot run on `TireChanger`. `wheel-mount` takes `<loader> <key>
  <groupUid>`, not `<loader> <uid>`.

**Recommendation:**
- For kinds where the server order decides the outcome, use `net-hold out` and release member i+1 only after the
  server logged member i's packet (`Wait-ServerLog`). Record the observed server order in the marker. Outcome checks
  stay symmetric ("one winner").
- Task 10.2 is done when a replay reproduces the observed server order.
- Add `-ContentionKinds` to force kinds (for 10.3 and for debugging).
- Add `warehouse-move <uid> to|from`, `diag-examine … keys`, and group support in `sell-item`. Or run the sale and
  scrap rounds of `tools-item-race` on the brake lathe or battery charger, which take items (`ToolsStore.cs:21`).

### M7. Task order: verbs that row 18 needs sit in a group that waits for row 18

**Where:** tasks 4.1 and 2.1, proposal open question 6, row 18 D13/task 7.2.

**Evidence:** row 18's `locks-fluid` uses `cardetails-fluid` and lists it as an existing helper (row 18 design D13).
It is a row 4 name that was never built (`openspec/INTEGRATION.md:200`; no `HarnessCommand("cardetails-fluid")` in
`tools/TestHarness`). Row 19 builds it in 4.1, and group 4 "starts after row 18 has merged". Open question 6 at the
same time says harness work can start now. Separately, task 2.1's `--check-merges` checks wheel and alignment merges,
but that code is `CarDetailsStore.Merge`, task 4.3.

**Recommendation:** make the setters (`cardetails-fluid/-wheel/-alignment/-wash`) and the `carDetails` dump section a
task 1.4, "harness, can start now". Row 18's 7.2 depends on it, and INTEGRATION records the owner. Keep `cardetails-pour`
in 4.5. Either move the details merge into a Core helper in 2.1, or move those `--check-merges` cases to 4.3.

---

## Minors

1. **`None` means "whole record", which a computed empty mask would hit.** The tracker flags a record by `SameState`
   (`PartRecords.cs:42-48`: effective id, no `MountObjectData`, no `IsPainted`), while the groups compare raw
   `PartId`/`TunedID`, bolt arrays and paint. A record that `SameState` calls changed can get an empty group set
   (for example a wheel part whose id `TunePart` rewrote) and would then be merged whole: exactly the stale
   overwrite. Use an explicit `All` bit for "whole". A change record never carries 0 (log and skip it). Define the
   `Bolts` comparison (element-wise, same epsilon).
2. **The `Paint` group carries nothing.** `Capture` fills only `IsPainted` (`PartRecords.cs:19-40`), and
   `PartApplier` writes neither `IsPainted` nor `Color` (`PartApplier.cs:51-86`), yet D2 lists `Paint` among the
   groups a receiver writes. Define it as `IsPainted` only, stored but not applied, or drop it until a spike shows
   part paint on a mounted part.
3. **`OnlyExamines` breaks with a new field.** It compares the serialized record with the stored one
   (`CarPartsHandlers.cs:84-101`). `Changed` makes every examine look like more than an examine, so other players'
   examines are refused while the car is away (`diagnostics`, `test-drive`). Replace it with: every record's
   `Changed` ⊆ `Examined`, no preconditions, no body records, no removals. Store records with `Changed` cleared, since
   they go into the `cars` save section and into snapshots.
4. **Engine stands.** Stand changes are built in `EngineStandParts.cs:102-105`, not in `PartChangeTracker`, so task 3.1
   leaves their masks at `None` and 3.2's stand merge does nothing. Also, `ToolsStore.FindConflict` checks only
   preconditions (`ToolsStore.cs:140-146`). An item mounted on a stand engine after another player mounted it on a
   car is accepted, which the audit does not list. Add masks to the stand sender (3.1) and the removed-by-other check
   to the stand path (D9, 5.1).
5. **One body panel is one entry.** `c:<partIndex>` holds colour, paint type, livery, tint, dust and wash together,
   so a tint and a wash of the same panel are "last write wins". Say so in open question 1 ("the same panel"), or
   split it into `c:<i>.paint`, `.livery`, `.tint` and `.dirt`.
6. **`car-placement` v2 is not needed.** The persistence rules say "Adding a field needs no bump (Newtonsoft fills
   defaults)" (`CMS21-Together-Server/Data/Persistence/README.md:73`). `PlacementSection.Migrate` throws for any
   version, and a bump makes a rollback refuse the save. Keep v1. An old server ignores `Records`, which degrades to
   today's unpark from the blob.
7. **D10's live snapshot to the unparker** should be sent only when the parked records changed something, not on
   every first baseline. `ApplySnapshot` replaces the unparker's `LoaderSync` whole (`CarPartsSync.cs` `Remember`),
   so changes it sent in that round trip are forgotten. That is acceptable, but say so.
8. **The 60 s expiry.** The poll asks one car per 5 s (`ReconciliationService.cs:63-64`). With five loaders a car is
   asked every 25 s, so 60 s allows two asks, and one "not ready" in between expires the mismatch before it can
   confirm. Expire after N asks of that key (for example 4) instead. The stall series should count the client's
   `NotReady`, not a server-side `null` (`:91`). "Not ready" is logged only in verbose rounds (`:93`), so the
   `desync-autofix` busy step must use forced rounds and a long `IntervalSeconds`.
9. **The `workshop-tools` client projection** must read the machines (`ReadLocal`), not `ToolSync`'s mirror, which
   `SendLocal` sets optimistically (`ToolSync.cs:128`); otherwise the digest compares the server with its own echo.
   Say what the resend does with an item the machine apply removes (the `RemoveSilentAdditions` path).
10. **False alarms for the non-car keys.** Spike 1.3 covers car details only. Run `desync-soak` with `jobs`,
    `garage`, `warehouse` and `workshop-tools` first in a log-only mode (no resend), and turn on the resend per key
    when it is quiet. Add a wheel swap to spike 1.3 (tire and rim ids, `c369763`).
11. **Stall test length.** Task 10.4 holds for 130 s. Make the 120 s threshold a server setting, as row 18 did with
    `lock_expiry_seconds`. The spec scenario talks about "car state" while the test uses `inventory`.
12. **Spec "Condition change made during another player's lock"** promises A's condition on an unmounted slot that no
    one sees, while the item in B's inventory carries B's view. Rewrite it: while the part stays mounted, A's
    condition survives B's lock. Or drop it.
13. **"A player whose action lost SHALL be told who used the item"** holds for the machine put only. The mount loser
    gets "item is gone" in the log (`PartChanges.cs:78`) and no message. Scope the requirement, or carry the remover
    in the rejection and show it.
14. **Size.** Part 1 at 7–8 sessions was optimistic before B1, B2 and M4; about 9–11 is realistic. Part 2 has 16
    kinds, two rules, five keys with "not ready" rules, resends and corrupt verbs; about 6–7, not 4–5. A first
    contention pass with the kinds that prove this change's own gaps plus P1, P2, M1 and L1 would keep it at about 5.
15. **"In local work" includes empty transactions that `Examine` opens** (`PartHooks.cs:22-24` → `OpenForPart`). This
    is moot with B1's masks; otherwise exclude transactions with an empty delta.

## Nits

- `PartScript` has no `SetExamined`; the `SetExamined(bool)` at `dump.cs:425419` belongs to `CarEditrPart`. Harmless,
  since `PartApplier` writes the field.
- D4 lists a `bonus` entry, but `CarDetailsIO.Read` never reads `BonusParts` (it is not in `All`). `CarDetailsSync.Present`
  leaves out `Dyno`, so D6's "remember the carried entries" must add it.
- `state-corrupt` has no `jobs` mode, though 9.3 wants one corrupt step per key.
- The details mappers must sort fluids, panels and modules by key. The server's merge reorders lists (`RemoveAll` +
  `Add`, `CarDetailsStore.cs:72-104`).
- `cardetails-pour` should also add condition (`dt × 0.05`), as `FluidsData.AddFluid` does from
  `FluidRefillLogic.Update`.
- `[OptionalField]` is moot while the protocol hash forces equal builds (as in row 18). Harmless.
- A parked record is a full car of records (about 230 records); say in Risks that the save grows per parked car.

## Checked and fine

- The file:line references in proposal.md and D1–D10 match `main` (`PartChangeTracker.cs:137-142`,
  `PartChanges.cs:46`, `CarDetailsIO.cs:49`, `CarDetailsSync.cs:148`, `CarDetailsStore.cs:70-114`,
  `InventoryHandlers.cs:31-39`, `ToolsStore.Check`, `ParkingHandlers.cs:31-61`, `HarnessClient.psm1:83`,
  `ScaleSession.psm1:151`). The row 18 task numbers it waits on (5.1, 6.1, 7.1, `FlushNow` in 4.4) are right.
- `FluidsData` is a struct (`dump.cs:445429`) with `SetLevelAndCondition`, `GetLevel` and `AddFluid`. `AddFluid`
  reads and writes through `GetLevel`/`SetLevel` (`FluidsData$$AddFluid.c`), and `FluidRefillLogic.Update` keeps no
  amount of its own (`FluidRefillLogic$$Update.c`), so the pour continues from whatever level an apply writes.
  Calling `AddFluid` on the copy that `carLoader.FluidsData` returns works because `FluidData` is a class, as
  `ApplyFluids` already relies on. `FluidsData.Save(FluidsData)` and `Copy(FluidsData)` take it by value; nothing
  here calls or patches them, and no new patch is added at all.
- `WheelsAlignment` is a struct with `FL`–`RR` properties (`dump.cs:437140`); the read-modify-write in `ApplyAlignment`
  extends to one field at a time.
- Parking records stay server-side: `ParkingService.SendState`/`BroadcastSlot` build packets from slot ids and blobs.
- `d1dd908` answers a change for a gone car with `StillHeld`; D10's "rejected as the car is gone" holds.
- No new packet type; `PacketTypes` unchanged; the digest keys are strings.
- The server-side `Returned` (D9) is the right call with row 18's item locks: a client re-add through the hook would
  race B's commit.

## Simpler path (optional, for the user)

1. **B1's masked relay** is also the simpler design. It removes D2's "in local work" rules, the abort heuristics and
   task 8.1's lock check on the receiving side.
2. **Parking (D10).** A hybrid is smaller than storing and overlaying part records. Use the audit's revision guard
   for parts: the park request carries the parker's `Revision`, the server refuses with `Busy` while its revision is
   newer, and the client retries once after the next applied change. Store only the details at park (`ModCarDetails`
   is the server's own format) and send them at the unpark. That means no part overlay, no live snapshot to the
   unparker and no engine-swap mismatch. The cost is a rare refusal the player sees. M4's drain is needed either way.
3. **Contention.** Land the mode with the kinds of this change's gaps and P1, P2, M1, L1. Add the row 18 kinds when
   row 18 has merged (they would only be "known gap" until then).

---

## Coverage ledger

All 44 audit rows whose code coverage is **None** or **Partial**, or whose test coverage is **none** or one actor only
(D5). "18" = `openspec/changes/part-locks/` (task and scenario), "19" = this change, "main" = already merged. A row
closed by 18 or 19 but with a test hole says so in the last column.

| Row | Audit (code / test) | Risk | Closed by | Where | Gap and recommendation |
|---|---|---|---|---|---|
| P1 same part within one round trip | Partial / partial | Medium | 18 | D1 strict lock, task 5.1; `locks-race` (5.2) | 19 adds soak kinds `part-same`, `part-claim` |
| P3 chooser group vs member in use | fixed on main (`035887c`, `e26d219`) / none | Medium | 18 | task 6.1; `locks-race` caliper-with-piston step (6.2) | — |
| P4 connected parts | None / none | High | 18 | D3; `locks-connected` (6.2) | 19 kind `parent-child` |
| P5 stale attribute record | None / none | High | 19 | D1–D3, tasks 3.1–3.4, 8.1; `car-stale-record`; kind `examine-vs-unmount` | Fix B1, M1, M2 first; otherwise the receiving side still reverts and the replaced part gets stale values |
| P6 remote change during own unscrew | Partial / partial | Medium | 18 + 19 | 18: the lock makes "claim lost after the start" impossible (5.1, `locks-race`); 19: attribute relays during bolts (3.3, `car-stale-record` 3–4) | Step 3 as written cannot pass (B1) |
| P7 change in flight for a gone car | None / none | High | main | `d1dd908`, `car-gone-inflight` (delete only) | **Add to 19:** `park` and `job-finish` steps in `car-gone-inflight` (task 3.4), and `PartTransactions` dropping `committed` per loader, which `HasOpen` counts globally (`PartTransactions.cs:75`). Row 18's WIP drops `open` only |
| P8 claims kept after the car is removed | None / none | Medium | 18 | D5 release on `LoaderCleared` (2.3), D6 mirror reset (4.1); `locks-car` | The audit's added check (mirrors empty, new car digest-ready) is not in row 18's `locks-car`: add it there, or as 19 task 8.1 step |
| P9 crane vs engine work | Partial / none | Medium | 18 | D3 key `engine`; `locks-connected` crane step | — |
| P10 two players mount one body panel | Partial / none | Low | 18 | body mount gate (5.1) | No body-panel step in `locks-race`; add one in row 18 |
| P11 `ShowMounted` without recheck | None / none | Low | not covered | 19 non-goal | **Add to 19** (task 3.3, a step in `car-stale-record`): three lines, and B1 shows that 19's own path triggers it |
| P12 claim outlives the work | Partial / partial | Low | 18 | renew, expiry, idle cancels (2.2, 4.1, 5.1); `locks-leak`, `locks-latency` | — |
| P15 condition look without highlighter | fixed (`1efb71c`) / none | Low | main | `1efb71c` | Accept: visual only |
| I1 item used by machine and mount | Partial / none | Medium | 19 | D9, tasks 5.1–5.3, 8.3; `tools-item-race`; kind `machine-same-item` | Fix M5; sale/scrap rounds need item machines or group sale (M6) |
| I2 two warehouse moves of one item | server first / none | Low | 19 | kind `item-trades` (10.3) | No warehouse verb exists (M6): **add** `warehouse-move` in 19 |
| I3 two updates of one item | LWW / none | Low | not covered | — | Accept: one item cannot be on two machines (M2 rule), and an update of a gone item is ignored |
| I5 sale or scrap vs mount | server first / none | Low | not covered | 19 covers machine vs sale, not mount vs sale | **Add to 19:** a mount-vs-sale round in `tools-item-race` (5.3) and a soak kind (`part-fast-mount … uid` against `sell-item uid`) |
| I6 reused UID ranges | Partial / none | Low | not covered | 19 non-goal | Later change (a hardening row with C1, C5, E5) |
| I7 uncommitted transaction holds inventory 10 s | Partial / none | Low | not covered | — | Accept: bounded by the 10 s idle flush, and D12 keeps a pending mismatch across it |
| E4 same skill or upgrade twice | server first, idempotent / none | Low | not covered (detection only by 19's `garage` key) | — | Accept |
| E5 duplicated economy request | Partial / partial | Low | not covered | 19 non-goal | Later change (hardening row): the transport is reliable, only the harness resends |
| D1 fill vs removing the container | None / none | High | 18 | D4 fluid keys, task 7.1; `locks-fluid` (7.2) | 19 kind `fluid-vs-part` |
| D2 different entries of one section | None / none | Medium-High | 19 | D4–D6, tasks 4.1–4.4, 4.6; `details-concurrent`; kind `details-pair` | Coordinate with row 18's fluid-only send (M3) |
| D3 own echo rolls a pour back | None / none | Low | 19 | D7, task 4.5; `details-concurrent` pour step | Fix B2 |
| D4 drain and part change in two packets | Partial / none | Medium | 18 | `FlushNow` (4.4, 7.1); `locks-fluid` order check | — |
| D5 same field by two players | by design / one actor | Low | 19 (test) | accepted LWW (2026-10-06); `details-concurrent` "same entry" | Converges only with B2's fix; run both orders |
| M4 same engine-stand part | precondition / none | Medium | not covered | 19 non-goal; 3.2 changes `ToolsStore.OnPartChange` without a race test | **Add to 19:** a two-player stand-part step in `tools-race` (task 3.2's proof), plus masks (minor 4) and the removed-by-other check on the stand |
| M5 stand part vs engine off the stand | rejected / none | Low | not covered | — | **Add to 19** with the M4 step (same setup, one more line) |
| M8 repair or paint vs mount | ignored / none | Low | not covered | — | Accept: the update of a gone item is ignored and the fee is refused |
| M9 oil bin vs oil work | None / none | Medium | 18 | `OilDrain` lock (7.1); `locks-fluid` oil-bin step | — |
| L2 lift while someone works | None / partial | High | 18 | D7 lift lock and M3 local check (8.1); `locks-car`, updated `visual-lift` (8.3) | 19 kind `lift-vs-work` |
| L3 move while someone works | None / none | Medium-High | 18 | D7 move lock (8.1); `locks-car` | 19 kind `move-vs-work` |
| L4 two moves to one free place | swap by design / none | Low | 19 | kind `place-same` (10.3), soak only | — |
| L5 remote lift still animating | None / partial | Medium | 18 | D7 M3 local moving check (8.1); `locks-car` with `net-delay 150` | — |
| L8 parking stores the parker's blob | None / none | Medium | 19 | D10, tasks 6.1–6.3; `park-stale`; kind `park-stale` | Fix M4 (own unsent change), minor 6 (no v2) |
| C1 spawn into an occupied loader | Partial / partial | Low | not covered | 19 non-goal | Later change (hardening row) |
| C2 spawner leaves before the baseline | handled / none | Low | not covered | 19 task 6.2 changes `SpawnerLeft` but tests it nowhere | **Add to 19:** a `park-stale` step: the unparker disconnects before its baseline; the car is back in its slot with its record |
| C3 park, delete or job end during work | None / none | High | 18 | M4 refusals (2.3, 8.2); `locks-car` | 19 kinds `park-vs-work`, `delete-vs-work` |
| C4 two deletes or parks of one car | harmless / none | Low | not covered | — | Accept (harmless by construction); optional soak kind |
| C5 second baseline replaces records | Partial / none | Low | not covered | 19 non-goal | Later change; D10 overlays only the first baseline, so it is not made worse |
| J2 generator change while generating | handled / none | Low | not covered (detection by 19's `jobs` key) | — | Accept |
| J4 order expires while accepted | handled / none | Low | not covered (detection by 19's `jobs` key) | — | Accept |
| J5 two players end one job | handled / none | Low | not covered | — | Accept; optional soak kind |
| S1 two players in one seat | None / none | Low | not covered | — | Accept (cosmetic presence data) |
| X4 F7 with a claim or change in flight | Partial / none | Low | 18 | D8 release before the reload (task 4.1) | No scenario: add the audit's `resync-key` step in row 18 (or 19 task 8.1) |

**Counts (44 rows):** closed by row 18: 16 (P1, P3, P4, P6, P8, P9, P10, P12, D1, D4, M9, L2, L3, L5, C3, X4);
by row 19: 8 (P5, I1, I2, D2, D3, D5, L4, L8); already on `main`: 2 (P7, P15); not covered: 18.

**Not covered, recommendation:**
- **Add to row 19 (5):** P11 (task 3.3), I5 (task 5.3 and a soak kind), M4 and M5 (`tools-race` steps with task 3.2),
  C2 (`park-stale` step with task 6.2).
- **Later change (4):** I6, E5, C1, C5. One small hardening row: UID ranges, request-id dedupe, spawn into an
  occupied loader, second baseline.
- **Accept (9):** I3, I7, E4, M8, C4, J2, J4, J5, S1. Each is server-first, bounded or cosmetic. E4, J2 and J4 get
  detection from row 19's `garage` and `jobs` keys.

**Test holes in rows closed elsewhere:** P7 (park and job-finish steps, per-loader `committed`: row 19), P8 (the
audit's added `locks-car` check), P10 (a body-panel step in `locks-race`), X4 (a `resync-key` step): the last three
belong to row 18. I2 needs a warehouse verb (row 19).

The audit's notes for row 18 are all placed: `FlushNow` per entry (row 19 D8, task 8.2; row 18's WIP already sends
changed fluids only); stale records after the lock release (row 19 D1, with M2); clearing transactions on car delete
(row 18's WIP `DropLoader` for open transactions; `committed` is still global: see P7).

---

## Resolution

Revised 2026-10-07 and 2026-10-08 in proposal.md, design.md (D1–D17), tasks.md (groups 1–13, parts 1–3) and both spec
files, together with the user's decision of 2026-10-07 (late evening) on the ledger's "accept" rows. Every item is
resolved as recommended unless it says otherwise.

### Blockers

- **B1. Resolved.** D2 is rewritten: the server relays the stored records with `Changed` = the groups the normalisation
  wrote, and returns to the sender the groups that differ from its send. Receivers write only the masked groups, to the
  game and to `LoaderSync` per group; a transaction is aborted only for `Mount`, `Identity`, `Switched` or `All`, and
  `AbortFor` runs after the revision check. The first draft's "in local work" rules are gone. `car-stale-record` step
  2 checks `visuals` (no ghost), step 3 asserts through `vfx-unscrew` status (`paused`, k mounted, bolt progress
  unchanged), step 4 is the unsent attribute edit with the new verb `part-condition` (D14; tasks 3.3, 3.4).
- **B2. Resolved.** D7 decides the own echo per entry: each kept send copy (16 sends or 10 s per loader) marks entries
  `foreignSince` when a foreign update of them is applied; an own echo, older or latest, applies an entry the server
  clamped or a foreign write overwrote, and drops the rest. `details-concurrent` runs the same-entry step in both
  server orders with `net-hold out` and a wait for the server log line (D14; tasks 4.4, 4.5). Spec scenario "Same fluid
  by two players, in either order".

### Majors

- **M1. Resolved.** D1 step 3 is the base check: without `Mount` in the mask, the incoming `Unmounted` (and `Switched`),
  and `PartId`/`TunedID` unless `Identity` is masked, must equal the stored ones, else the record is dropped and counted
  `staleDropped`. `--check-merges` covers a stale and a replaced base (task 2.1); `car-stale-record` step 5 is the
  replaced part. The first spec scenario no longer marks an unmounted part examined, and a new scenario covers the
  replaced part.
- **M2. Resolved.** D3 defines a flip for row 18's rule as `Mount` in the written groups or a precondition for that key;
  normalisation runs before row 18's lock check, and `FlippedKeys` and `unlockedFlip` use the definition (tasks 3.2,
  8.1). `car-stale-record` step 6 (task 8.1) checks a stale examine in the window before B's lock is released.
- **M3. Resolved in this change's documents; one edit is left for row 18.** Design Context lists what row 18 ships and
  the agreed ownership: row 18 keeps `FlushNow` with its changed-fluids send, `DropLoader` and `CarDetailsStore.SendTo`,
  and drops `KeepStoredMountState`. Tasks 3.2 (removes `KeepStoredMountState` if it reached `main`), 3.5 (builds on
  `DropLoader`) and 4.1 (replaces `OnlyChanged`) say so; proposal open question 5 asks the user. Dropping
  `KeepStoredMountState` is an edit on `change/part-locks`, not made here.
- **M4. Resolved.** D10 drains before a park request and before a car delete: at most 1 s for the tracker and the
  loader's transactions, then `FlushNow(loader, All)`, else "Try again in a moment." (task 6.3, after 3.5). `park-stale`
  gains the own-unsent-change step; spec scenario "Own unmount just before the park".
- **M5. Resolved.** D9 keeps the remover of every removed UID for the session, bounded at 10 000 UIDs, and only the
  item copy for `Returned` expires after 60 s. `EconomyService.RemoveItem` gets the client id through
  `EconomyOutcome.Effect`, so scrap, barn maps and quality upgrades name their remover (task 5.1).
- **M6. Resolved.** D13 forces the server order: `net-hold out` on every member, release member i+1 only after the
  server logged member i (`Wait-ServerLog`), the observed order in the marker, `{step:N:…}` templates for per-run
  values, and `-ContentionKinds`. Task 10.2 is done when a replay reproduces the observed server order; task 10.3's
  negative control forces `machine-same-item`. New verbs `warehouse-move`, `diag-examine … keys` and `sell-item [uid]`
  for groups (D14; tasks 3.4, 5.2); the sale and scrap rounds run on the brake lathe; `wheel-mount` keeps its
  signature. The spec's replay scenario now says the server receives the actions in the same order.
- **M7. Resolved.** Task 1.4 (the four `cardetails-*` setters and the `carDetails` dump section) can start now, and
  INTEGRATION.md records row 19 as the builder of row 4's names. The details merge is the Core helper `DetailsMerge`,
  and its `--check-merges` cases are in task 4.2. `cardetails-pour` moved with the echo work to task 4.4.

### Minors

1. **Resolved.** `PartFields.All` is an explicit bit; a change record never carries 0 (logged and skipped); groups
   compare raw values, `Bolts` element-wise with epsilon 0.001; a `SameState` difference with no group gets the
   effective-value groups (D1).
2. **Resolved.** `Paint` is `IsPainted` only, stored and never applied (D1 table).
3. **Resolved.** `OnlyExamines` checks masks (every `Changed` is `Examined`, no preconditions, body records or
   removals); stored records keep `Changed` cleared (D1; task 3.2).
4. **Resolved.** `EngineStandParts` sets masks (D1; task 3.1); `ToolsStore.FindConflict` gets the removed-by-other check
   (D9; task 3.2); `tools-race` gains the two stand steps (ledger M4, M5).
5. **Resolved by naming it, not by splitting.** One panel stays one entry (D4 alternatives, Non-Goals); proposal open
   question 2 asks the user. Splitting would need a mask per panel for a rare race.
6. **Resolved.** No `car-placement` v2: `Parking.Records` is an additive field (D10, Migration).
7. **Resolved.** The live snapshot goes to the unparker only when the parked records replaced something, and D10 says
   that the unparker's `LoaderSync` is replaced whole and the digest catches what it forgot.
8. **Resolved.** A pending mismatch expires after four asks of that key; the stall series counts the client's `NotReady`
   flag; the `desync-autofix` steps use forced rounds and a long `desync_check_interval_seconds` (D12; task 9.5).
9. **Resolved.** `workshop-tools` reads `ToolMachine.ReadLocal`; D11 says the resend's apply drops items the game adds
   during it (`RemoveSilentAdditions`).
10. **Resolved.** New keys start log-only (`desync_resend_keys`) and get their resend after a quiet `desync-soak` (D11;
    tasks 9.3, 9.4; proposal open question 7). Spike 1.3 includes a wheel swap.
11. **Resolved.** `desync_stall_seconds` (default 120) is a server setting; task 10.4 tests with 20 s. The spec's stall
    scenario speaks of any kind of state (a car's parts or details, the inventory, …), so the inventory test proves it.
12. **Resolved.** D3 states that an examine or condition change during B's lock survives B's commit while the part stays
    mounted; the spec scenario is rewritten ("Examine during another player's lock").
13. **Resolved by scoping.** The spec promises the name for a machine put only; a rejected mount keeps today's log-only
    behaviour (D9).
14. **Resolved.** Part 1 ≈ 9–11 sessions, part 2 ≈ 6–7, and the new part 3 (D16, D17) ≈ 3, in proposal.md, tasks.md and
    ROADMAP row 19. The first-pass/second-pass split of the contention kinds is adopted (D13), but the second pass
    stays in part 2 (task 10.5), so part 2 stays at 6–7.
15. **Moot** with B1: D2 no longer looks at local transactions.

### Nits

- `SetExamined`: design Context says `PartScript.IsExamined` is a plain field that `PartApplier` writes. Done.
- `bonus` and `Dyno`: `BonusParts` stays out (D4); `Present` includes `Dyno` (D6). Done.
- `state-corrupt` has no `jobs` mode; `jobs` is proven by `desync-soak` only (D14). Done.
- The details mappers sort lists by key (D11; task 9.1). Done.
- `cardetails-pour` adds condition `dt × 0.05` (D14). Done.
- `[OptionalField]`: kept, noted as moot but harmless (design Packets). Done.
- Parked record size: D10 says the save grows by about 230 records per parked car. Done.

### Checked and fine: one line superseded

"No new packet type" no longer holds: the user's seat decision adds `SeatRefused` (D17), appended to `PacketTypes` at
merge time after row 18's packets.

### Simpler path

1. **Adopted.** B1's masked relay is D2.
2. **Not adopted; left to the user.** D10 keeps the server's record of the parked car. The hybrid shows the player a
   refusal whenever a newer part change is on its way, and its window is a guess; the drain (M4) is needed either way,
   and with it D10 loses nothing. Proposal open question 3 offers the hybrid as the alternative.
3. **Adopted.** D13's first pass covers this change's gaps plus P1, P2, M1 and L1; row 18's kinds are the second pass
   (task 10.5), known gaps until then.

### Coverage ledger

- **Added to row 19 as recommended:** P7 (task 3.5, `car-gone-inflight` park and job-finish steps), P11 (D2, task 3.3),
  I2 (`warehouse-move`, task 5.3), I5 (`tools-item-race` mount-against-sale round, kind `mount-vs-sale`), M4 and M5
  (`tools-race` stand steps, task 3.2), C2 (D10, tasks 6.2 and 6.4).
- **The "accept" rows, decided by the user on 2026-10-07:** S1 is fixed (D17, task 12.4, `seat-engine` two-player
  step). For I3, I7, E4, M8, C4, J2, J4 and J5 the server's choice stays, and the acting client gets the authoritative
  result (D16, tasks 12.1–12.3 and 12.5, scenario `server-answers`). D16's audit of the server's silent paths covers
  further rows without an audit id.
- **Later change:** I6, E5, C1 and C5 go to `race-hardening`, ROADMAP row 20 (not drafted); named in proposal.md and
  design Non-Goals.
- **Test holes in rows closed by row 18 (P8, P10, X4):** not taken into row 19. They belong in row 18's scenarios
  (`locks-car` check after the car is removed, a body-panel step in `locks-race`, a `resync-key` step), to be added on
  `change/part-locks`.

### Consistency pass (2026-10-08)

- Task references in design.md corrected: `OnlyChanged` is replaced by task 4.1 (was 4.2), `DetailsMerge`'s check is
  task 4.2 (was 4.3), the same-entry check is tasks 4.4 and 4.5 (was 4.6).
- D17 and design Context: `GameScript.ExitFromInterior` is already patched by row 6's `SeatEngine` prefix, which clears
  the local seat; the earlier "not patched" was wrong. Nothing new is patched.
- `drift-detection-coverage`: expiry after four comparisons (was 60 s), the stall time as a server setting, the replay
  scenario, and log-only for a newly compared kind.
