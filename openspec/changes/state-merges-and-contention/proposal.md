# Proposal

Revised 2026-10-08 after `review.md` (verdict "ready after fixes"; every item's resolution is in its "Resolution"
section) and the user's decisions of 2026-10-07 on the review's coverage ledger.

## Why

The race and drift audit (`docs/audits/race-and-drift-audit.md`, 2026-10-07) found the mod safe wherever the server
decides first. The risk sits in the optimistic paths, where a client changes its own game first and the server only
arbitrates afterwards. Row 18 (`part-locks`) closes gaps 1, 2, 4 and 8, and gap 5 is fixed on `main` (`d1dd908`,
scenario `car-gone-inflight`). What is left are lost updates and duplicates that no lock prevents, drift the digests
cannot see, and clients that the server overrules without telling them:

- **Gap 3 (High): stale attribute changes.** An examine, a diagnostic tool, the welder or any condition change sends
  full part records. Only keys whose mount state the sender itself flipped get a precondition
  (`PartChangeTracker.cs:137-142`). If another player's unmount is committed but has not reached the sender yet, the
  server stores the part as mounted again and relays that to the actor, whose item stays: a duplicate, and a server
  that disagrees with the actor's game. On the receiving side, `PartChanges.OnRemoteChange` aborts the local open
  transaction for every key in the change (`PartChanges.cs:46`) and applies every record whole, so a remote examine
  re-mounts a part under the hands of a player who is unmounting it, or overwrites a condition change that player has
  not sent yet. Row 18 rejects such a flip only while the other player still holds the key exclusively; once that lock
  is released by the commit, the stale record is accepted again, and examine is not gated at all.
- **Gap 6 (Medium-High): car details are whole sections.** The client sends every fluid, every body panel and every
  wheel whenever one entry of a section changes (`CarDetailsIO.cs:49`, `CarDetailsSync.Flush`). Two players who
  change different entries (brake fluid and coolant; dust on the hood and on a door; two wheels; the FL and RR
  alignment) overwrite each other on every client. The server already merges fluids and cosmetics per entry
  (`CarDetailsStore.cs:70-89`), but cannot help when the entry it receives is the sender's stale copy, and it
  replaces wheels and alignment whole (`:107-108`). The sender also re-applies its own latest echo
  (`CarDetailsSync.cs:148`), which rolls a pour in progress back to the level of the send: the game's
  `FluidRefillLogic.Update` adds to the car's level every frame with no local accumulator
  (`native/out/cardetails_clean/FluidRefillLogic$$Update.c`).
- **Gap 9 (Medium): one item used by a machine and by a mount.** If the mount reaches the server first, the
  machine's inventory `Remove` is dropped without an answer (`InventoryHandlers.cs:31-39`) and `ToolsStore.Check`
  accepts a slot item the server no longer has. The item is then on the car and on the machine, and taking it off
  the machine duplicates it. Row 18's item locks cover mounts only.
- **Gap 10 (Medium): parking stores the parker's copy.** `ParkFromGarage` stores the parker's local car blob
  (`ParkingHandlers.cs:32-60`). A change another player committed a moment earlier, still on its way to the parker,
  is lost from the parked car, while its inventory side stays: the item is duplicated.
- **Gap 7, rest (Medium-High): the digests are blind where the races are.** Car details, machines, the warehouse,
  skills and upgrades, and jobs are in no digest. A "not ready" answer clears a pending mismatch
  (`ReconciliationService.Tick`/`OnDigest`), so a busy key never confirms. The forced check asks one car per client.
  The soak's checkpoint compares no car details (`HarnessClient.psm1:83`) and its forced digest round leaves out cars
  (`ScaleSession.psm1:151`). Row 18 D6 fixes only the car skip rule.
- **The soak avoids contention on purpose.** One action per loop, keys in use are skipped, parts move only through
  the fast verbs, and details change on one client (audit "Soak contention mode"). None of the races above can show
  up in it, and drift that every client shares (the item on both the car and the machine) is invisible to its
  client-to-client comparison.
- **Silent drops (review ledger, user decision of 2026-10-07).** Where the server refuses, ignores or overrides an
  action (a second skill unlock, a second park, an order from a client that is not the generator, an update of an
  item another player mounted, …) it often answers nothing, and the acting client keeps its own result until a
  digest or F7 repairs it. Two players can also sit in the same seat (S1): the server stores whatever each sends.

Row 4 planned part of this and never finished it: its task 4.2 asked the flusher for "changed entries only", and its
task 5.1 a concurrent two-player details check that `car-details` does not contain (one actor).

## User decisions this change follows

- **2026-10-06:** two players writing the same detail entry: the last write the server receives wins.
- **2026-10-07 (evening):** an agent reviews every complicated change before it is implemented (done: `review.md`);
  races between players get a soak contention mode after row 18.
- **2026-10-07 (late evening), on the review's coverage ledger:**
  - S1 (two players in one seat) is fixed here: the server arbitrates, the second player is refused and leaves the
    seat (design D17, part 3).
  - For I3, I7, E4, M8, C4, J2, J4 and J5 the server's choice stays, but every refused, ignored or overridden action
    reaches the acting client with the authoritative result, so that client rolls back and never stays out of sync
    (no silent drops; design D16, part 3).
  - The audit's rows are all either covered by rows 18 and 19, by this rule, or by a later hardening change (below).

## What Changes

**Part 1: state merges (gaps 3, 6, 9, 10; ledger rows P7, P11, I2, I5, M4, M5, C2).**

- **Part records carry what changed** (D1). Every part record gets a `Changed` mask of field groups (`Mount`, `Bolts`,
  `Identity`, `Condition`, `Quality`, `Examined`, `Paint`, `Dust`, `Switched`, and an explicit `All` for a whole
  record). The client sets it from the difference to its last known record of that key.
  - The server normalises each record before `FindConflict` and row 18's lock check. A mount record is taken whole.
    An unmount copies its masked groups. Any other record is checked against its base (the sender's `Unmounted`,
    `Switched`, `PartId` and `TunedID`): when the base matches the stored record, the masked groups are merged onto
    it; when it does not (the part was unmounted or replaced meanwhile), the record is dropped and counted
    `staleDropped`. A stale examine never re-mounts a part, and a change made on a replaced part never lands on the
    new one.
  - The same merge serves engine-stand part changes, which also get the removed-by-other item check.
  - For row 18's lock rule, a "flip" is `Mount` in the written groups or a precondition (D3), so a stale examine is
    never refused as "locked".
- **Receivers write only what the server changed** (D2). The server relays the stored records with the groups it
  wrote, and returns to the sender the groups that differ from what it sent. A receiver writes only the masked groups,
  to the game and to its known state, so an own condition edit that has not been sent yet survives. A local part
  transaction is aborted only for `Mount`, `Identity`, `Switched` or `All`, after the revision check: an examine never
  disturbs a player who is unscrewing. `PartApplier.ShowMounted` rechecks the mount state after its wait (P11).
- **Car details as entries** (D4–D7). The client remembers a signature per entry (fluid, wheel index, alignment
  field, body panel, tuning module, gearbox, paint, plates, info, dyno) and sends only changed entries; wheels and
  alignment travel with a mask. The server merges per entry (`DetailsMerge` in Core). A remote apply writes and
  remembers only the entries it carries. The own echo is decided per entry: it applies an entry only when the server
  clamped it or another player's write of that entry came in between, so a pour is never rolled back and two writes
  of one entry converge on the server's value in either order.
- **Item removals and machines** (D9). The server keeps every removed UID's remover for the session (bounded), and a
  copy of the item for 60 s. A machine put of an item another client removed (mounted, sold, scrapped, moved) is
  refused, and the refusal says `Returned` (the server put the item back) or `Gone`; the player sees
  "<name> used this part." Scrap, barn maps and quality upgrades name their remover. Harness verbs for the
  warehouse and group sales make the I2 and I5 races testable.
- **Parking keeps the server's record** (D10). Before a park (and a car delete) the client drains its own pending part
  change and details, at most 1 s, else "Try again in a moment." At park the server copies its own part records and
  details next to the blob, server side only and saved without a section bump. At unpark it overlays the unparker's
  first baseline with the differing parked records and sends the parked details. A change that arrives after the
  park is rejected as "the car is gone" and rolled back. An unparker who leaves before its baseline puts the car back
  with its record (C2).
- **Per-loader part transactions** (P7): committed transactions are dropped with their car, and a dropped transaction
  rolls its inventory change back locally.

**Part 2: detection and contention (gap 7 rest, the soak contention mode).**

- **New digest keys** (D11): `car-details:<loader>`, `workshop-tools` (read from the machines, not the sync mirror),
  `warehouse`, `garage` (skills, upgrades, barns) and `jobs`. Each has a "not ready" rule on the client and a resend
  on the server; each key starts log-only (`desync_resend_keys`) and gets its resend once `desync-soak` is quiet for
  it.
- **Reconciliation rules** (D12). A "not ready" answer neither confirms nor clears a pending mismatch; a pending
  mismatch expires after four asks of that key. A forced round (`desync check`) asks every car and every key. After
  row 18's switch-over, the server warns when a client stays "not ready" for one key longer than
  `desync_stall_seconds` (default 120), and lists it in `desync` and the bug report.
- **Soak contention mode** (D13). `soak.ps1 -Contention [-ContentionWeight] [-ContentionKinds]` runs seeded groups of
  two to four actors on the same target. Outgoing packets are held and released one member at a time, member i+1
  only after the server logged member i, so the server order is decided by the seed, recorded in the marker and
  reproduced by `-Replay`. A first pass covers this change's gaps plus P1, P2, M1 and L1; a second pass adds row
  18's kinds after it merges. New rules: conservation of items (rule 7) and the kind's outcome (rule 8), with
  `soak-contention-known.txt` for kinds whose gap is still open. The checkpoint gains `carDetails`, a forced digest
  of every car and key, and "no key not ready at two checkpoints in a row".

**Part 3: the server answers every refusal, and seats (user decision of 2026-10-07).**

- **No silent drops** (D16). Every path where the server refuses, ignores or overrides an action now sends the acting
  client the authoritative result: an inventory `Update` of a missing item answers with its `Remove`; a refused
  repair adds the item's `Remove`; a refused or no-op upgrade answers with `GarageState` and `WorldState`; a second
  park gets the parking state and the loader's delete; a dropped generated order, an expired accept (`JobRemoved`
  with `Expired`, "This order is no longer available.") and a second job end answer with the jobs state. The server's
  decisions themselves do not change. Scenario `server-answers` checks the losing client against the server right
  after each answer, without a resync.
- **Seats** (D17). The first player in a seat keeps it. A second claim is stored without seat and engine, and the
  server sends `SeatRefused`; that client leaves the seat with the game's own exit and shows "<name> is sitting
  there."

**Not in this change: a later hardening change, `race-hardening`** (ROADMAP row 20, not drafted). The four audit
rows the review left for it: I6 (reused UID ranges), E5 (economy request deduplication), C1 (a spawn into an occupied
loader) and C5 (a second baseline replacing records). Each is low risk today and none is made worse here.

**Game hooks:** none new. The change calls existing methods only: `CarLoader.SetWheelSize`/`SetET`/`UpdateWheels`,
the `WheelsAlignment` and headlamp alignment fields, `FluidsData.SetLevelAndCondition`, `PartApplier`'s "from save"
methods, and `GameScript.ExitFromInterior(true)` for a refused seat. The harness pour verb calls `FluidsData.AddFluid`
on the car's field. Nothing patches a method that takes `NewCarData` or `FluidsData` by value.

**Packets** (additive fields marked `[OptionalField]`, and one new packet type):
- `CarBodyPartUpdatePacket.Changed`, `CarSubPartUpdatePacket.Changed` (`PartFields` flags; `All` = whole record; a
  change record never carries 0).
- `CarDetailsUpdatePacket.WheelMask` (bit per wheel index) and `AlignmentMask` (`AlignmentFields` flags).
- `ToolSlotRejectedPacket.Item` (`SlotItemOutcome`: `Unchanged`, `Returned`, `Gone`).
- **New packet type `SeatRefused`** `{ CarLoaderID, SeatLeft, HolderPlayerId }`, server to the refused client,
  appended to the end of `PacketTypes` at merge time (after whatever row 18 appended).
- `ParkedCar` is unchanged on the wire; the records live in a server-only `PlacementState.Parking.Records`.

## Capabilities

### New Capabilities
- `concurrent-state-merges`: attribute changes never undo another player's mount or land on a replaced part, details
  of one car changed by several players keep every entry, the own echo never rolls a change back, an item ends up in
  exactly one place, a parked car keeps every accepted change, a refused, ignored or overridden action is answered
  with the server's state, and two players never sit in one seat.
- `drift-detection-coverage`: what the digests compare, when a mismatch confirms, and the soak's contention mode
  with its conservation and outcome checks.

### Modified Capabilities
<!-- none: openspec/specs/ is empty. When rows 1, 2, 3, 4, 5a, 6, 10 and 14 are archived, these requirements extend
sync-car-parts ("a part change"), sync-car-details ("last write wins per section" becomes per entry),
sync-workshop-machines, sync-car-placement-and-lifts (parking), sync-orders-and-jobs and economy-audit (answers to
refusals), sync-players-and-scenes (seats) and desync-detection-and-resync. -->

## Impact

- **Core:** `PartFields` (with `All`) and `PartRecordMerge.Normalise` (next to `PartRecords`), the `Changed` fields,
  `DetailsMerge`, the details masks and `AlignmentFields`, `SlotItemOutcome`, `SeatRefusedPacket` and its
  `PacketTypes` entry, `DigestMappers.Details/Tools/Warehouse/Garage/Jobs`.
- **Server:**
  - parts: `CarPartsHandlers.OnChange` (normalisation before `FindConflict` and row 18's lock check, relay of the
    stored records with the written groups, result of the differing groups, `OnlyExamines` by mask), row 18's
    `FlippedKeys`/`unlockedFlip` on D3's definition, `ToolsStore.OnPartChange` and `ToolsStore.FindConflict` (stand
    parts), `staleMerged`/`staleDropped` in `cars`;
  - details: `CarDetailsStore.Merge` through `DetailsMerge`;
  - items: `ToolsStore.Check`/`OnSlotUpdate`, `InventoryChanges` (removers for the session, copies for 60 s),
    `EconomyService.RemoveItem` and `EconomyOutcome.Effect`, `unknownSlotItem`/`removeMissing` in `tools`;
  - parking: `ParkingHandlers`, the baseline overlay in `CarPartsHandlers.OnBaseline`/`CarPartsStore`,
    `PlacementRules.OnLoaderCleared`, `PlacementState.Parking.Records` (no section bump);
  - detection: `ReconciliationService` (keys, rules, forced round, stall warning), settings `desync_resend_keys` and
    `desync_stall_seconds`, `--check-merges`;
  - answers (D16): `InventoryHandlers`, `EconomyRules.PartRepair`, `GarageUpgradeHandler`, `ParkingHandlers`,
    `CarHandlers.HandleCarSpawnDelete`, `PlacementHandlers.OnCarPlaceChange`, `JobsService` (generated orders, accept,
    job end), console `jobs expire <id>`; seats: `PlayerHandlers.OnPlayerPresence`.
- **Client:** `PartChangeTracker` and `EngineStandParts` (masks), `PartChanges` and `PartApplier` (masked apply,
  abort rule, `ShowMounted` recheck), `PartTransactions` (per loader, local rollback of a dropped transaction),
  `CarDetailsSync` and `CarDetailsIO` (entries, masks, send copies with `foreignSince`), the park and delete drain,
  `ToolSync.OnRejected`/`Compensate`, `ClientDigests` (new keys), the jobs client (`JobRemoved` `Expired` message),
  the `SeatRefused` handler.
- **Harness:**
  - verbs: row 4's registered setters that were never built (`cardetails-fluid`, `-wheel`, `-alignment`, `-wash`
    with an optional panel index), `cardetails-pour`, `part-condition`, `diag-examine … keys`, `state-corrupt` for
    the new keys, `digest-hold <key> notready`, `item-where`, `sell-item [uid]` (items and groups),
    `warehouse-move`, `inv-send`; dump sections `carDetails` and `parts.transactions`;
  - new scenarios `car-stale-record`, `details-concurrent`, `tools-item-race`, `park-stale`, `server-answers`;
  - extended `car-gone-inflight`, `tools-race`, `seat-engine`, `desync-autofix`, `desync-soak`, `soak`, and after row
    18 `locks-fluid`;
  - soak: `soak.ps1`, `ScaleSession.psm1`, `HarnessClient.psm1`, `scenarios/soak-contention-known.txt`.
- **Depends on** (merged): rows 1, 2, 4, 5a, 5b, 11, 13, 14 and the gap 5 fix. **Row 18** where it touches locks:
  - task 1.4 lands first, because row 18's `locks-fluid` uses `cardetails-fluid` (its task 7.2);
  - groups 3 and 4, task 6.3 and task 9.2's car keys start after row 18 has merged, to avoid two branches rewriting
    `CarPartsHandlers.OnChange`, `PartChanges`, `CarDetailsSync` and `ClientDigests.Car` (open question 5);
  - tasks 8.1–8.3 and 10.4 wait for row 18's switch-over (its task 5.1); 8.2 also for its fluid gates (7.1), 8.3 for
    its item step (6.1); task 10.5 for row 18 merged;
  - ownership agreed in design Context: row 18 keeps `FlushNow` with its changed-fluids send, `DropLoader` and
    `CarDetailsStore.SendTo`, and drops its work-in-progress `KeepStoredMountState`.
- **Size** (minor 14 of the review): part 1 L ≈ 9–11 sessions, part 2 L ≈ 6–7, part 3 M ≈ 3; about 18–21 as one
  piece, so it is split by the ROADMAP rule into three parts merged separately (open question 4). Group 8 (row 18
  tie-ins) merges with whichever part is open when row 18 has merged.

## Open questions for the user

Each has the default the documents work with.

1. **Stale examine or condition change.** The server merges it when the player's view of the part still matches
   (keeps the other player's mount, applies the examine or condition), and drops it, counted, when the part was
   unmounted or replaced meanwhile. The player can examine again. Rejecting the whole change instead would throw away
   every examine in it and run a rollback. **Default:** merge, drop only the stale parts.
2. **One body panel is one detail entry.** Paint, livery, tint, dust and wash of one panel travel together, so a tint
   and a wash of the same panel by two players at once keep only the later one (different panels always merge).
   Splitting a panel into four entries costs a mask per panel. **Default:** one entry per panel.
3. **Parking.** The parked car keeps the server's own part records and details, and the parker's game first sends
   its own last change (up to 1 s, else "Try again in a moment."). The cheaper alternative (the review's hybrid)
   stores only the details and refuses a park while a newer part change is on its way, which the player sees more
   often. **Default:** the server's record, with the drain.
4. **Split.** Part 1 (state merges), part 2 (detection and contention) and part 3 (answers and seats) are merged
   separately, like row 17. Part 2 builds on part 1's Core (task 2.1, which adds `--check-merges`); part 3 can start
   now and needs part 1 only for its dropped-transaction step (task 3.5). **Default:** yes.
5. **Order and ownership with row 18.** Groups that change the same methods as row 18 start after row 18 has merged;
   spikes, Core, machines, parking groups 6.1–6.2, digests, soak harness, part 3 and task 1.4 can start now. Row 18
   drops its `KeepStoredMountState` and keeps `FlushNow` and the changed-fluids send, which this change later
   generalises. **Default:** yes (row 18's branch needs that one edit).
6. **Machine against mount.** A put of an item another player has just mounted, sold or scrapped is refused, the
   item stays where the other player used it, and the player sees "<name> used this part." Items the server has
   never seen (groups the game builds itself) are accepted and logged, unless spike 1.1 shows that machines never
   use such items. **Default:** yes.
7. **New digest keys start log-only.** Each new key only logs a mismatch until `desync-soak` is quiet for it; then
   its automatic repair is turned on by default. **Default:** yes.
8. **Stall warning.** A key that stays "not ready" for longer than `desync_stall_seconds` (default 120 s) is logged
   by the server and goes into the bug report, but the player sees nothing. **Default:** log only.
9. **Contention in the regular scale lane.** `Run-All -Lanes 3` runs the 10-minute soak with `-Contention` (weight
   15), and the long soak too. Kinds whose gap is still open are reported as "known gap" and do not fail the run.
   **Default:** yes, once task 10.3 is in.
10. **Player messages.** "<name> used this part." (machine put), "<name> is sitting there." (seat), "This order is no
    longer available." (expired accept), "Try again in a moment." (park or delete while the own change is still
    settling). **Default:** these texts.
