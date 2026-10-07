# Proposal

## Why

The race and drift audit (`docs/audits/race-and-drift-audit.md`, 2026-10-07) found the mod safe wherever the server
decides first. The risk sits in the optimistic paths, where a client changes its own game first and the server only
arbitrates afterwards. Row 18 (`part-locks`) closes gaps 1, 2, 4 and 8, and gap 5 is fixed on `main` (`d1dd908`,
scenario `car-gone-inflight`). What is left are lost updates and duplicates that no lock prevents, and drift the
digests cannot see:

- **Gap 3 (High): stale attribute changes.** An examine, a diagnostic tool, the welder or any condition change sends
  full part records. Only keys whose mount state the sender itself flipped get a precondition
  (`PartChangeTracker.cs:137-142`). If another player's unmount is committed but has not reached the sender yet, the
  server stores the part as mounted again and relays that to the actor, whose item stays: a duplicate, and a server
  that disagrees with the actor's game. On the receiving side, `PartChanges.OnRemoteChange` aborts the local open
  transaction for every key in the change (`PartChanges.cs:46`), so a remote examine re-mounts a part under the
  hands of a player who is unscrewing it. Row 18 rejects such a flip only while the other player still holds the key
  exclusively; once that lock is released by the commit, the stale record is accepted again, and examine is not
  gated at all.
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

Row 4 planned part of this and never finished it: its task 4.2 asked the flusher for "changed entries only", and its
task 5.1 a concurrent two-player details check that `car-details` does not contain (one actor).

## What Changes

**Part 1: state merges (gaps 3, 6, 9, 10).**

- **Part records carry what changed.** Every part record gets a `Changed` mask of field groups (mount, bolts,
  identity, condition, quality, examined, paint, dust; for body parts mount, switched, tuned, state). The client sets
  it from the difference to the last server record it knows. The server applies only the masked groups onto its
  stored record, except for a record that mounts a part, which describes a new item and is taken whole.
  - A stale examine therefore keeps the stored mount state, and the merged record is what the server stores, relays
    and returns to the sender.
  - The same merge serves engine-stand part changes (row 5a).
  - Counted as `staleMerged` in the server's `cars` output.
- **Receiving side.** A remote change aborts a local part transaction only when it really changes that key's mount
  state or identity. For a key the local player is working on (an open transaction, or a local flip not sent yet),
  the remote apply sets only attribute fields and leaves mount state and bolts alone. The local commit's
  precondition decides.
- **Car details as entries.** The client keeps `lastKnown` per entry (fluid `type.id`, wheel index, alignment field,
  body panel index, tuning module key, gearbox, and one entry each for paint, plates, info, bonus parts and dyno) and
  sends only changed entries. Wheels and alignment travel with a mask of the entries the packet carries; the server
  merges them per index or field, as it already does for fluids, cosmetics and tuning modules.
  - A remote apply writes and remembers only the entries it carries, so a local edit of another entry is still sent.
  - The sender skips its own echo per entry unless the server changed the value (clamping), comparing it with the
    copy it kept for that `ClientSeq`. A pour in progress is never rolled back.
  - Two players writing the same entry: the last write wins, as accepted on 2026-10-06 (open question 1). Row 18's
    fluid locks keep two pours of one fluid apart.
- **Machine items.** A machine put is refused when another client removed the item (mounted, sold, scrapped). The
  refusal says what happened to the item: the server put it back into the shared inventory (`Returned`), or it is
  gone elsewhere (`Gone`), and the client's existing compensation follows that instead of guessing. After row 18's
  switch-over, a put is also refused while the item is in another player's mount lock.
- **Parking keeps the server's record.** At park the server stores its own part records and details of that car next
  to the blob, server side only. At unpark it uses them in place of the unparker's baseline and the unparker's full
  details, sends them to everyone, the unparker included, and every client applies them like a late-join snapshot.
  A change that arrives after the park is rejected as "the car is gone" (gap 5 fix) and rolled back.

**Part 2: detection and contention (gap 7 rest, soak contention mode).**

- **New digest keys:** `car-details:<loader>` (rounded like `CarDetailsSync.Signature`), `workshop-tools` (slot UID,
  item or group id, balanced, mounting per machine), `warehouse`, `garage` (skills, upgrades, barns) and `jobs` (open
  order ids and active jobs). Each has a "not ready" rule on the client and a resend path on the server.
- **Reconciliation rules.** A "not ready" answer no longer clears a pending mismatch; a pending mismatch expires after
  60 s instead. A forced round (`desync check`) asks every car and every key. After row 18's switch-over, the server
  logs a warning when a client answers "not ready" for the same key for 120 s (a silent stall, the symptom of gaps 5
  and 8).
- **Soak contention mode.** `soak.ps1 -Contention` adds a `contention` catalogue row: seeded groups of two to four
  actors that act on the same target behind `net-hold` and are released in a seeded order (same part, same item,
  examine against unmount, two detail entries, a fluid against its part, lift or move or park against work, machine
  against mount, sale and scrap of one item). Each group is logged for `-Replay` and checks its outcome. Two new soak
  rules: conservation of items (rule 7) and the kind's outcome (rule 8), with `soak-contention-known.txt` listing the
  kinds whose gap is still open (reported and counted, not failed). The checkpoint gains a `carDetails` dump section,
  a forced digest of every car and the new keys, and "no key not ready in two checkpoints in a row".

**Game hooks:** none new. The change calls existing setters only: `CarLoader.SetWheelSize`/`SetET`/`UpdateWheels`,
the `WheelsAlignment` and headlamp alignment fields, `FluidsData.SetLevelAndCondition`, and `PartApplier`'s "from
save" methods. The harness pour verb calls `FluidsData.AddFluid` on the car's field. Nothing patches a method that
takes `NewCarData` or `FluidsData` by value.

**Packets** (all changes are additive fields, `[OptionalField]`; no new packet type):
- `CarBodyPartUpdatePacket.Changed`, `CarSubPartUpdatePacket.Changed` (`PartFields` flags; `None` = whole record).
- `CarDetailsUpdatePacket.WheelMask` (bit per wheel index) and `AlignmentMask` (`AlignmentFields` flags).
- `ToolSlotRejectedPacket.Item` (`SlotItemOutcome`: `Unchanged`, `Returned`, `Gone`).
- `ParkedCar` is unchanged on the wire; the records live in a server-only `PlacementState.Parking.Records`.

## Capabilities

### New Capabilities
- `concurrent-state-merges`: attribute changes never undo another player's mount, details of one car changed by
  several players keep every entry, the own echo never rolls a change back, an item ends up in exactly one place, and
  a parked car keeps every accepted change.
- `drift-detection-coverage`: what the digests compare, when a mismatch confirms, and the soak's contention mode
  with its conservation and outcome checks.

### Modified Capabilities
<!-- none: openspec/specs/ is empty. When rows 1, 4, 5a, 2 and 14 are archived, these requirements extend
sync-car-parts ("a part change"), sync-car-details ("last write wins per section" becomes per entry),
sync-workshop-machines, sync-car-placement-and-lifts (parking) and desync-detection-and-resync. -->

## Impact

- **Core:** `PartFields` and `PartRecordMerge` (next to `PartRecords`), the `Changed` fields, the details masks,
  `SlotItemOutcome`, and `DigestMappers.Details/Tools/Warehouse/Garage/Jobs`.
- **Server:**
  - `CarPartsHandlers.OnChange` (merge, relay and result of merged records), `ToolsStore.OnPartChange` (same merge);
  - `CarDetailsStore.Merge` (wheels per index, alignment per field);
  - `ToolsStore.Check`/`OnSlotUpdate` and `InventoryChanges` (a short-lived copy of removed items for `Returned`);
  - `ParkingHandlers`, `CarPartsStore.StoreBaseline`, `CarDetailsStore` (parked records), `PlacementSection` v2;
  - `ReconciliationService` (keys, rules, forced round, stall warning), and a `--check-merges` self-test.
- **Client:** `PartChangeTracker` (mask), `PartChanges` and `PartApplier` (abort and apply rule), `CarDetailsSync`
  and `CarDetailsIO` (entries, masks, echo copies), `ToolSync.OnRejected`/`Compensate`, `ClientDigests` (new keys).
- **Harness:** row 4's registered setters that were never built (`cardetails-fluid`, `-wheel`, `-alignment`,
  `-wash` with an optional panel index), `cardetails-pour`, `details-corrupt`, `tool-corrupt`, `item-where`,
  `sell-item [uid]`, dump sections `carDetails` and `parts.transactions`; new scenarios `car-stale-record`,
  `details-concurrent`, `tools-item-race`, `park-stale`; extended `desync-autofix`, `desync-soak`, `soak`,
  `ScaleSession.psm1`, `HarnessClient.psm1`; after row 18, steps in `locks-fluid`.
- **Depends on** (merged): rows 1, 2, 4, 5a, 5b, 11, 13, 14 and the gap 5 fix. **Row 18** where it touches locks:
  tasks 8.1–8.3 and 10.4 wait for row 18's switch-over (its task 5.1); 8.2 also for its fluid gates (its task
  7.1) and 8.3 for its item step (its task 6.1).
  The rest does not need row 18, but the groups that edit `CarPartsHandlers`, `PartChanges` and `CarDetailsSync`
  start after row 18 has merged, to avoid two branches rewriting the same methods (open question 6).
- **Size:** part 1 L ≈ 7–8 sessions, part 2 M ≈ 4–5 sessions; XL ≈ 11–13 as one piece, so it is split by the
  ROADMAP rule (open question 5).

## Open questions for the user

Each has the default the draft works with.

1. **Same entry, two players.** Two players changing the same entry (the same fluid, the same panel's paint or dust,
   the same wheel) keep "the last write wins" (your answer of 2026-10-06), and only different entries merge. Row 18
   keeps two pours of one fluid apart anyway. **Default:** yes.
2. **Stale examine or condition change.** The server merges it (keeps the other player's mount, applies the examine
   or condition) instead of rejecting it. Rejecting would throw away the examine result and run a rollback.
   **Default:** merge.
3. **Parking.** The parked car keeps the server's own part records and details, so a change that reached the server
   just before the park is never lost. The cheaper alternative refuses a park while a newer change is on its way and
   lets the player retry. **Default:** the server's record.
4. **Machine against mount.** When a player puts an item on a machine that another player has just mounted (or
   sold), the put is refused, the item stays where the other player used it, and the player sees "<name> used this
   part". Items the server has never seen (groups the game builds itself) are accepted and logged, unless spike 1.1
   shows that machines never use such items. **Default:** yes.
5. **Split.** Part 1 (state merges, L) and part 2 (detection and soak contention, M) are merged separately, like row
   17. Part 2 can start once part 1's Core packets are in. **Default:** yes.
6. **Order with row 18.** Groups that change the same methods as row 18 (`CarPartsHandlers.OnChange`,
   `PartChanges`, `CarDetailsSync.Flush`, `ClientDigests.Car`) start after row 18 has merged. Machines, parking,
   Core, digests and harness work can start now on a second lane. **Default:** yes.
7. **Contention in the regular scale lane.** `Run-All -Lanes 3` runs the 10-minute soak with `-Contention` (weight
   15), and the long soak too. Kinds whose gap is still open are reported as "known gap" and do not fail the run.
   **Default:** yes, once group 10 is in.
8. **Stall warning.** A key that stays "not ready" for 120 s is logged by the server and goes into the bug report,
   but the player sees nothing. **Default:** log only.
