# Design

Revised 2026-10-07 after `review.md` (verdict "ready after fixes"). The review items each decision answers are named
in brackets, for example [B1]; the coverage ledger's rows by their audit id, for example [P11]. The resolution of
every item is in `review.md` under "Resolution".

## Context

Source: `docs/audits/race-and-drift-audit.md` (gaps 3, 6, 7, 9, 10 and "Soak contention mode") and the review's
coverage ledger. State on `main` (`d1dd908`):

- **Part changes (row 1).** `PartChangeTracker` sends a `CarPartsChange` after three stable 0.1 s polls. Every record
  is the full captured state of a part. A precondition (`WasUnmounted`) is added only for keys whose mount state
  differs from the client's last known record (`PartChangeTracker.cs:137-142`). The sent records are written into
  `LoaderSync` at once (`:145-146`), so `LoaderSync` means "what this client last sent or applied", not "the server's
  state" [B1]. The server checks preconditions and removed items (`CarPartsHandlers.FindConflict`), then stores the
  records as they came. The only field merge is `IsExamined` (kept true, `:63-67`). The server relays the sender's
  packet to the others (`:80`). `OnlyExamines` (`:84-101`) lets examines through while a car is away by comparing
  serialized records.
- **Remote apply (row 1).** `PartChanges.OnRemoteChange` aborts the local open transaction for every key in the
  change (`PartChanges.cs:46`), before its revision check (`:56`), and applies every record whole through
  `PartApplier`, which flips mount state with the game's "from save" methods and writes condition, quality, examined,
  dust and bolt state (`SetMountObjectData`). A transaction for an unmount opens only when `Hide` starts, after the
  last bolt (`PartHooks.cs:10-12`). `PartApplier.ShowMounted` re-enables renderers and colliders 0.5 s later without
  checking that the part is still mounted (`PartApplier.cs:90-96`, audit P11).
- **Engine stands (row 5a).** Stand part changes are built in `EngineStandParts.cs:102-105`, not by the tracker.
  `ToolsStore.FindConflict` checks preconditions only (`ToolsStore.cs:140-146`), not items removed by another player.
- **Car details (row 4).** A 1 Hz poll plus hooks mark sections dirty; `Flush` reads whole sections after a 0.5 s
  delay and sends every changed section whole (`CarDetailsSync.cs:128-144`). The server replaces sections, except
  fluids, cosmetics and tuning modules, which it merges per entry (`CarDetailsStore.cs:70-114`), and relays the packet
  to every client including the sender, in arrival order. The sender skips echoes older than its latest send and
  applies the latest (`CarDetailsSync.cs:148`). After any apply, the client re-reads and remembers the whole section
  (`:172`), so a local edit of another entry that was not flushed yet is never sent.
- **Machines (row 5a).** A put runs the game (which deletes the item or group, so `InventoryHook` sends `Remove`) and
  then `ToolSync.SendLocal` sends `ToolSlotUpdate { State, ExpectedUid }`. `ToolsStore.Check` does not look at the
  inventory. A refused client runs `Compensate`, which re-adds the item locally through the hook.
- **Removed items.** `InventoryChanges.removedBy` remembers every removed UID's remover for the session
  (`InventoryChanges.cs:11-21`). `EconomyService.RemoveItem` notes `UnknownRemover` (-1) for scrap, barn maps and
  quality upgrades (`EconomyService.cs:145-150`).
- **Parking (row 2).** `ParkFromGarage` stores `request.Car` (the parker's `NewCarData` blob) and clears the loader.
  Unpark registers a spawn from the blob; the unparker uploads the baseline, which becomes the server's records.
- **Digests (row 14).** Keys `world`, `inventory` (warehouse passed as `null`), `cars:<loader>` (one car per 5 s
  round, round robin) and `car-placement`. A "not ready" answer clears a pending mismatch.
- **Soak (row 11).** One action per loop; keys in use skipped; checkpoint sections `stats, inventory, cars, placement,
  jobs, tools, toolPositions`; forced digest of `world`, `inventory`, `car-placement` only.

**What row 18 (`part-locks`) ships that this change builds on** [M3]. Agreed ownership:
- Row 18 keeps `CarDetailsSync.FlushNow(loader, sections)` and its send of **changed fluids only** (its `OnlyChanged`
  against the remembered fluid list); it needs both for `locks-fluid`. Task 4.1 generalises that send to every
  section and every entry kind and replaces `OnlyChanged`.
- Row 18 keeps `PartTransactions.DropLoader` (open transactions of a loader dropped on car delete) and
  `CarDetailsStore.SendTo`. Task 3.5 adds the `committed` half per loader [P7].
- Row 18 **drops** its work-in-progress `KeepStoredMountState` (keep the stored `Unmounted` for records without a
  precondition). Gap 3 is this change's (D1). If that code is on `main` when task 3.2 starts, 3.2 removes it.
- Row 18's `FindConflict` lock rule ("reject a flip of another player's X key", `FlippedKeys`) uses this change's
  definition of a flip (D3).

Game facts this design relies on (checked in `dump.cs` and the decompiles under `native/out/`):

- `FluidRefillLogic.Update` adds `deltaTime × 0.1` to the level and `deltaTime × 0.05` to the condition through
  `FluidsData.AddFluid` every frame while the button is held, and reads the level back through `GetLevel`. It keeps
  no amount of its own (`cardetails_clean/FluidRefillLogic$$Update.c`). A level written by an apply during a pour is
  where the pour continues from.
- `FluidsData` is a struct (`dump.cs:445429`) with `SetLevelAndCondition`, `GetLevel` and `AddFluid`; calling them on
  the copy `carLoader.FluidsData` returns works because `FluidData` is a class (as `CarDetailsIO.ApplyFluids` already
  relies on). `WheelsAlignment` is a struct (`dump.cs:437140`); its fields can be set one at a time by
  read-modify-write, as `ApplyAlignment` does for the headlamps.
- `PartScript.IsExamined` is a plain field (`dump.cs:438752`) that `PartApplier` writes directly.
- Nothing new is patched, and nothing this change calls takes `NewCarData` or `FluidsData` by value. Of the methods
  it calls, only `GameScript.ExitFromInterior` is patched already: row 6's `SeatEngine` prefix clears the local seat,
  which is what D17 wants.

## Goals / Non-Goals

**Goals:**
- A change that does not flip a part's mount state never changes that part's mount state, identity or bolts on the
  server or on any client, however stale the sender's view, and a change made on a part that has since been replaced
  is not written onto the new part.
- A remote change never overwrites a local edit that has not been sent yet, for part attributes and for car details.
- Changes by several players to different entries of one car's details are all kept, and every client ends on the
  server's value of each entry.
- A player's own change is never rolled back by its own echo.
- An inventory item ends up in exactly one place: the inventory, the warehouse, one machine, mounted on one car or on
  one engine stand, or sold.
- A parked car holds every change the server accepted before the park, the parker's own included.
- Whenever the server refuses, ignores or overrides an action, the client that made it receives the authoritative
  result and equals the server right after (user, 2026-10-07; D16).
- Two players never sit in the same seat (D17).
- Every piece of shared state the server owns is in a digest, and a busy key does not hide a mismatch forever.
- The soak can make players contend on purpose, in a server order that a replay reproduces, and fails when an item is
  duplicated or lost.

**Non-Goals:**
- Locks for car tools, machines or examine (row 18's non-goals stand).
- Two players writing the same detail entry: the last write the server receives wins (accepted 2026-10-06). One
  body panel's paint, livery, tint, dust and wash are one entry [minor 5; open question 2].
- The later hardening change (proposal): I6 (UID ranges), E5 (economy request deduplication), C1 (spawn into an
  occupied loader), C5 (second baseline).
- Engine-stand parts in a digest (the slot is compared, its part overlay is not).
- A server-built parking blob: the server has no game code, and the blob is the game's native format.
- The server's decision in the rows below stays; only what it tells the losing client changes (D16).

### Audit rows whose server decision stays

From the review's coverage ledger, as decided by the user on 2026-10-07. The ledger proposed accepting nine rows as
they are. The user decided:

- **S1 is fixed in this change** (D17): the server arbitrates seats.
- **The other eight keep the server's choice**, which is server first, bounded or harmless for the shared state. But
  the client whose action lost must never stay out of sync: wherever the server refuses, ignores or overrides an
  action, it now tells that client the authoritative result (D16).

| Row | Scenario | Server's choice (unchanged) | Answer to the losing client (D16) |
|---|---|---|---|
| I3 | Two updates of one item, or an update racing a mount | last write wins; an update of a gone item is ignored | the item's `Remove` (gone) or the server's copy (`Update`) |
| I7 | An uncommitted part transaction keeps its inventory change for up to 10 s | idle flush as plain packets | each plain packet answered per D16; a transaction dropped with its car rolls back locally |
| E4 | Two players unlock the same skill or upgrade | first wins, the second is a no-op | `GarageState` and `WorldState` |
| M8 | Repair or paint on an item another player mounts meanwhile | the update is ignored, the fee refused | the item's `Remove` |
| C4 | Two players delete or park the same car | the second is a no-op or refused | the parking state and the loader's delete |
| J2 | An order from a client that is not the generator | dropped | the open orders (jobs snapshot) |
| J4 | An order expires while it is being accepted | refused `Unknown` | the refusal plus `JobRemoved` for that order |
| J5 | Two players end the same job | the second is ignored | `WorldState` plus the jobs snapshot |

## Decisions

### D1. Part records carry a changed-field mask; the server checks the base and merges per field group

Every `CarBodyPartUpdatePacket` and `CarSubPartUpdatePacket` gets `PartFields Changed` (`[Flags]`, Core):

| Group | Mechanical record fields | Body record fields |
|---|---|---|
| `Mount` | `Unmounted` | `Unmounted` |
| `Bolts` | `MountObjectData` (element-wise, epsilon 0.001) | — |
| `Identity` | `PartId`, `TunedID` (raw values) | `TunedID` |
| `Condition` | `Condition` | `State.Condition`, `State.Dent` |
| `Quality` | `Quality` | `State.Quality` |
| `Examined` | `IsExamined` | — |
| `Paint` | `IsPainted` (stored, never applied) [minor 2] | — |
| `Dust` | `Dust` | — |
| `Switched` | — | `Switched` |
| `All` | the whole record | the whole record |

**Spike 1.2 (2026-10-08, `merges-probe`, run `20261008-171541_L1_merges-probe`):** `diag-examine` with every
`ToolType` (OBD 8 keys, Compression 35, Multimeter 13, TireTreadDepthTester 4, CompoundMeter 3, TestDrive 52, PathTest
43; OilBayonet examines none) changes only `IsExamined` (`Examined`), on the actor and on the receiver. The welder
(`tool-use Welder`) changes no part record. The game's mount (`PartScript.DoMount`, decompile
`PartScript._DoMount_d__151$$MoveNext.c`) writes the item's condition, quality, `IsPainted`, dust 0 and id
(`TunePart`) together with `Unmounted`: a mount record changes `Mount`, `Identity`, `Condition`, `Quality`, `Paint`
and `Dust` at once, which is why it is taken whole (step 1); a repaired and painted item only differs from the part
by these fields. Leaving the driver's seat flips a door's `Switched`. Every changed field belongs to exactly one group
of the table.

Other `State` fields of a body record follow the `Condition` group. `All` is an explicit bit [minor 1]: snapshots,
resyncs, baselines and the harness's corrupt verbs use it. A change record never carries 0; the server logs and
skips such a record.

**Client.** `PartChangeTracker.Send` sets `Changed` per record from the difference between the captured record and
`LoaderSync`'s record of that key, group by group (raw values, so a wheel part whose id `TunePart` rewrote gets
`Identity`). A key without a `LoaderSync` record gets `All`. If `SameState` flagged a record but no group differs, the
record gets the groups `SameState` compares that differ by effective value, never 0. `EngineStandParts` sets the mask
the same way for stand part changes [minor 4]. Preconditions are built as today.

**Server** (`CarPartsHandlers.OnChange`, before `FindConflict` and before row 18's lock check [M2]): normalise each
record into `(merged record, written groups)`:

1. **Mount record** (a precondition with `WasUnmounted = true` and `Unmounted = false`): taken whole; written groups
   `All`. It describes a new item, whose id, condition and quality belong to that item. The existing `IsExamined`
   rule stays.
2. **Other records with `Mount` in `Changed`** (an unmount, validated by its precondition): the masked groups are
   copied onto the stored record.
3. **Records without `Mount` in `Changed`: base check** [M1]. The incoming `Unmounted` (and `Switched` for a body
   part), the effective id (`PartId`/`TunedID`, as `PartKeys.EffectiveId`) when `Identity` is not in the mask (a tune
   changes it on purpose), and `Quality` when `Quality` is not in the mask are the sender's base. `Quality` is in the
   base because a part replaced by another of the same id is otherwise indistinguishable (task 2.1, 2026-10-08): a
   mounted part's quality only changes with a mount (spike 1.2), so a different quality means "replaced". A
   replacement with the same id and quality is still merged onto (accepted: the two parts are alike). If they equal
   the stored ones, the masked groups
   are copied onto the stored record (counted `staleMerged` when an unmasked group differed: the sender was stale
   elsewhere). If they differ, the part was unmounted or replaced since the sender's view: the record is dropped,
   nothing is written, and it is counted and logged as `staleDropped`.
4. **`All` without a precondition** (harness corrupt verbs, legacy): taken whole.

Then `FindConflict` and row 18's lock check run on the normalised change (D3). The stored records keep `Changed`
cleared, since they go into the `cars` save section and into snapshots [minor 3].

The helper `PartRecordMerge.Normalise(stored, incoming, precondition)` lives in Core and is used by
`CarPartsHandlers` and `ToolsStore.OnPartChange` (stand parts). `--check-merges` (in the pattern of `--check-locks`)
covers every rule above.

`OnlyExamines` becomes: no preconditions, no body records, no inventory removals, and every record's `Changed` is
`Examined` [minor 3].

Example (gap 3): B's unmount of `s:3.22.4` is stored. A, who has not received it, runs an examine. A's record has
`Changed = Examined` and base `Unmounted = false`, which differs from the stored `true`, so it is dropped. The server
returns the stored record to A with `Changed = Mount` (D2), and A's game hides the part. If B had replaced the disc
instead, A's examine would not mark the new disc either. A's examine of keys whose base matched is kept.

**Alternatives considered:**
- *Keep only the stored `Unmounted`/`Switched` (the audit's proposal, and row 18's dropped `KeepStoredMountState`).*
  Writes the old part's id and condition onto a new part B mounted meanwhile.
- *Merge the masked groups without a base check (this change's first draft).* Writes A's examine or condition onto the
  part that replaced the one A looked at [M1].
- *Reject a stale attribute record* (open question 1). A loses every examine in the change, not only the stale keys,
  and runs the rollback path that caused playtest finding 4.
- *A base revision per record and a three-way merge on the server.* Needs history per key; the mask and the base
  fields carry the same information.

### D2. Receivers write only what the server changed [B1]

This replaces the first draft's "in local work" rules.

- **The server sends what it wrote.** Every relayed record carries `Changed` = the groups the normalisation wrote
  from this change (a mount record: `All`). Records with nothing written are not relayed. The relay is built from the
  stored records, not from the sender's packet.
- **The sender gets what differs.** The accepted result returns each record whose stored state differs from what the
  sender sent, with `Changed` = the differing groups. A dropped record (D1 step 3) comes back with the groups that made
  its base stale (typically `Mount` or `Identity`).
- **Receivers write only the masked groups**, to the game and into `LoaderSync`, group by group. A local edit of
  another group that has not passed the tracker's three stable polls stays different from `LoaderSync`, so the tracker
  still sends it. This is the parts twin of D6.
- **Abort only on mount or identity.** A local part transaction is aborted only when the record's `Changed` contains
  `Mount`, `Identity` or `Switched` (or `All`). An attribute relay never touches mount state, bolts or identity,
  whatever the local state: B's bolt job, paused or running, is never disturbed by A's examine.
- **A mount relay keeps today's behaviour** (abort and apply). The local precondition would fail anyway.
- **Snapshots and resyncs** carry `All` and apply whole, as today.
- **Order.** `PartChanges.OnRemoteChange` runs `PartTransactions.AbortFor` only after the revision check, so a dropped
  stale relay no longer aborts local work (`PartChanges.cs:46` and `:56` today).
- **`ShowMounted` rechecks** [P11]: after its 0.5 s wait it returns without touching renderers, colliders or blocking
  when `script.IsUnmounted` is true again. With D2, a quick mount-then-unmount of one key (a race, a rejection
  restore, or this change's returned records) cannot leave a visible ghost.

### D3. Parts and row 18's locks

- **A flip, for row 18's rule, is defined as:** `Mount` in the normalised record's written groups, or the change has a
  precondition for that key [M2]. Row 18's `FlippedKeys` and its `unlockedFlip` counter use this definition. Because
  normalisation runs first, a stale examine (dropped, or merged without `Mount`) is never rejected as "locked by
  player N", also in the window where a lock outlives the commit of one of its keys (row 18's "release when every X
  key reached its target").
- An attribute change on a key another player holds in X (an examine while B unscrews) is accepted. Its written groups
  never contain `Mount`, `Bolts` or `Identity`.
- The lock holder's own unmount commit has `Changed = Mount|Bolts`, so an examine or condition change made during the
  lock survives B's commit while the part stays mounted [minor 12].
- Row 18's release on commit is evaluated on the stored records after normalisation.
- Gap 3 does not need row 18 for correctness. Its groups start after row 18 merges only to avoid two branches
  rewriting `CarPartsHandlers.OnChange` and `PartChanges` (open question 5).

### D4. Car details travel as entries

`CarDetailsSync.lastKnown` becomes `loader → entry id → signature`:

| Section | Entry id | Wire form |
|---|---|---|
| Fluids | `f:<type>.<id>` | `Fluids` holds only the changed entries |
| Wheels | `w:<index>` | `Wheels` is the whole small array; `WheelMask` (bit per index) says which entries count |
| Alignment | `a:<FL|FR|RL|RR|LampLH|LampLV|LampRH|LampRV>` | `Alignment` whole; `AlignmentMask` (`AlignmentFields`) says which fields count |
| BodyCosmetics | `c:<partIndex>` (paint, livery, tint, dust and wash of one panel) | `BodyCosmetics` holds only the changed panels |
| Tuning | `t:<partKey>`, `t:gearbox` | `Tuning.Modules` holds only the changed modules; `Gearbox` is `null` unless it changed |
| Paint, Plates, Info, Dyno | `paint`, `plates`, `info`, `dyno` | whole, when changed |

`BonusParts` is not read by `CarDetailsIO.Read` today and stays out.

- `Flush` reads the dirty sections, computes each entry's signature (the rounding of `CarDetailsSync.Signature`), and
  sends one `CarDetailsUpdate` with only the changed entries. It replaces row 18's fluids-only `OnlyChanged`; row 18's
  `FlushNow` inherits the per-entry send [M3].
- After the send, only the sent entries are remembered.
- A full snapshot (`SendFull`, `IsFull`) is unchanged: every entry, masks "all". A mask of 0 with a non-null `Wheels`
  or `Alignment` means "all".

**Alternatives considered:**
- *Nullable fields in `ModAlignment` and `null` entries in `Wheels`.* `ModCarDetails` is also the saved and stored
  form, which must stay complete; the masks live on the packet only.
- *Split a panel into `c:<i>.paint`, `.livery`, `.tint`, `.dirt`* [minor 5]. A tint and a wash of the same panel by two
  players at once is rare; it would need a mask per panel. Open question 2 asks the user instead.
- *Per-entry packets.* More packets for a multi-entry change such as a wash, for no gain.

### D5. Server: merge per entry, relay entries

- `CarDetailsStore.Merge` merges wheels per masked index (growing the stored array if needed) and alignment per masked
  field, and keeps its per-entry merge of fluids, cosmetics and tuning modules. Paint, plates, info and dyno stay
  whole. The merge is the Core helper `DetailsMerge`, so `--check-merges` covers it (task 4.2) [M7].
- The relay is the clamped incoming packet with its masks, as today, in arrival order, to every client including the
  sender. Receivers apply only what it carries (D6).
- Snapshots and resends send the stored, complete details.

### D6. Apply and remember per entry

- `CarDetailsIO.Apply` honours the masks: `ApplyWheels` skips unmasked indices; `ApplyAlignment` writes only the
  masked fields. Lists apply the entries they hold, as today.
- After an apply, the client remembers only the entries the packet carried (`Present` includes `Dyno`), read back from
  the game. A local edit of another entry stays "changed" and goes out with the next flush.

### D7. The own echo is decided per entry [B2]

The server applies updates in arrival order and relays each to everyone, so the last update it relays for an entry is
the server's value. A client therefore ends on the server's value if it applies every foreign update and, of its own
echoes, every entry that the server changed or that a foreign update overwrote after the send.

- The sender keeps, per loader, the signatures of the entries it sent under each `ClientSeq` (the last 16 sends or
  10 s, whichever is shorter), each with a `foreignSince` flag.
- When a foreign update (another client's, or a server-sourced packet such as a resend or test-drive fold) is applied,
  each entry it carries is marked `foreignSince` in every kept send copy.
- An own echo (`SourceClientId` = own id), for any kept `ClientSeq`, older or latest, applies an entry when:
  - its signature differs from the kept one (the server clamped it), **or**
  - the kept entry is marked `foreignSince` (the echo is the server's later write of that entry).

  Every other entry is dropped. The kept copy is then discarded. An own echo with no kept copy (expired) is applied
  whole, as today's latest echo is.
- A pour is never set back to the level of its last send (the `FluidRefillLogic.Update` fact in Context), unless
  another player wrote the same fluid in between, in which case the server's later value is the right one.
- `details-concurrent` checks the same-entry case in both server orders (tasks 4.4 and 4.5).

### D8. Fluids under row 18's locks

- Row 18 ships `FlushNow` with a changed-fluids-only send. Task 4.1 replaces that send with the per-entry `Flush`,
  which at a fill's release sends the filled fluid (and any other entry this client changed). This is the audit's
  correction for row 18 without a second API.
- Task 8.2 adds the audit's step to `locks-fluid`: A holds `f:Brake.0` and fills while B holds `f:EngineCoolant.0` and
  fills; after both releases, the server's details and both clients have both levels.
- Row 18's `locks-fluid` uses `cardetails-fluid`, which is built early in task 1.4 and does not wait for row 18 [M7].

### D9. Item removals: the removed-by-other rule for machines and stands, and the refusal says what happened

**Server.**
- `InventoryChanges` keeps the remover of every removed UID for the session, bounded by count (the last 10 000 UIDs),
  as today [M5]. It additionally keeps, for 60 s, the time and a copy of the removed item or group, which only
  `Returned` needs. Every remove path notes it: the inventory `Remove`, a part change's delta, sale, scrap, barn map
  and quality upgrade. `EconomyService.RemoveItem` gets the client id through `EconomyOutcome.Effect`, so scrap and
  the others name their remover instead of `UnknownRemover` [M5].
- `ToolsStore.Check`, for a put (incoming not empty, a UID other than the current one), after today's checks:
  - the UID is in the server's inventory, or this client removed it: accepted (today's path);
  - another client removed it (mounted, sold, scrapped, put elsewhere): refused, "used by player N";
  - the server never saw the UID: accepted, logged and counted (`unknownSlotItem`) (open question 6). Spike 1.1
    (2026-10-08, the batch `20261008-171529_L1_batch`: `tools-slots`, `tools-race`, `tools-latejoin`,
    `tools-car-effects`, debug line `[Tools] Put origin` in `ToolsStore.Check`): 17 puts that passed the existing
    checks (7, 7, 3, 0), every one "removed by the putter" (the game deletes the item first and `InventoryHook` sends `Remove`
    ahead of the slot update); none was in the inventory still, removed by another client or never seen. Machines
    did not use a never-seen item in these runs, but the scenarios hand out items through the hooked inventory only,
    so groups the game builds itself are not covered; the rule stays "accept and count", and the counter shows in
    the soak whether such puts happen at all.
- Every refusal of a put carries `ToolSlotRejectedPacket.Item`: `Returned` (this client had removed the item; the
  server puts the kept copy back into its inventory, clears the remover entry and relays the `Add` to the others),
  `Gone` (another client removed it; nothing is restored) or `Unchanged` (no item involved).
- **Engine stands** [minor 4, M4, M5]: `ToolsStore.FindConflict` gets the same removed-by-other check for a stand part
  change's removed items as `CarPartsHandlers.FindConflict` has, with `RestoreUids` from `StillHeld`.
- A `Remove` of a UID the server does not have stays without a reply; it is logged with the recorded remover and
  counted (`removeMissing`).

**Client.** `ToolSync.OnRejected`/`Compensate` follow the outcome: `Returned` re-adds the item locally with the
inventory hooks off; `Gone` drops it and shows "<name> used this part."; `Unchanged` keeps today's compensation. A
rejected part mount keeps today's behaviour (log only) [minor 13].

**Why `Returned` is done by the server.** A client re-add through the hook races a third player's mount commit, which
`RemovedByOther` would reject. With row 18's item locks that window becomes the normal case.

**After row 18's switch-over** (task 8.3): `ToolsStore.Check` also refuses a put of a UID in another owner's `CarLocks`
item lock (`Returned`, row 18's item message). Implemented as the reason "<uid> held by player N" (the client shows
"<name> is mounting this part."); a put refused because another client removed the item says "used by player N", and
the client names that player.

**Alternatives considered:**
- *Make the machine put one transaction that carries its inventory removal.* Needs a put window per machine to capture
  the game's `Inventory.Delete`; the server-side rule gives the same guarantee with the packets as they are.
- *Machines take row 18 item locks before a put.* A round trip for every put, for a race the server decides on
  arrival.

### D10. Parking stores the server's record of the car, after the client drained its own changes

- **Drain before the park request** [M4]. The parking client (and the deleting client, before `CarSpawnDelete`) waits
  at most 1 s until `PartChangeTracker.IsPending(loader)` is false and `PartTransactions` has no open or unconfirmed
  transaction for that loader (per loader, task 3.5), then calls row 18's `FlushNow(loader, All)`, then sends. If the
  1 s runs out, the action is refused locally with "Try again in a moment." The parts change and the details go out on
  the same ordered stream before the park request, so the server has the parker's own changes when it parks.
  *As built (task 6.3):* the park waits in a prefix on the park coroutine's `MoveNext` (`NotificationCenter.
  MoveCarToParking`), which answers "still running" for up to 1 s without running the game's step, and
  `PartChangeTracker.SendNow` sends a stable pending change at once instead of after three polls. The game's
  `DeleteCar` has no point before its effect where a wait is safe (it runs inside other game flows), so a local delete
  sends the pending change and details at once (`SendNow`, `FlushNow`) and does not wait. A local removal keeps the
  loader's committed transactions until their result (the server accepted them before the delete); a remote removal
  rolls them back (task 3.5).
- **At park** (`ParkFromGarage`, accepted, before `ClearLoader`): if the loader has a baseline, the server copies its
  body and mechanical records, its engine-swap flag and its valid details into `ParkedRecord { CarId, Body, Sub,
  EngineSwap, Details }` under `PlacementState.Parking.Records[ParkedCar.Id]`. The blob is stored as today. A parked
  record is about 230 part records; the save grows by that per parked car.
- **Server only.** Records are never broadcast. A parking swap keeps them (keyed by car id). A car removed from parking
  drops its record.
- **At unpark** the record moves to the new loader entry. When the unparker's first baseline arrives:
  - the server stores the uploaded records, then replaces each one whose key exists in the parked record and differs
    from it (keys that do not resolve are counted and logged: `parkedRecordsDropped`);
  - only if that replaced anything, it sends the live snapshot to every client, the unparker included [minor 7]. The
    unparker's `ApplySnapshot` replaces its `LoaderSync` whole, so a change it sent in that round trip is forgotten
    locally and caught by the car digest; this is acceptable for the first second after an unpark;
  - if the parked record has details, the server stores them for the new `SpawnSeq`, sends them full to everyone, and
    drops the unparker's first full details snapshot for that `SpawnSeq` (logged).
- **The unparker leaves before the baseline** (`PlacementRules.OnLoaderCleared`, `SpawnerLeft`): the car goes back to
  parking with its record [C2].
- **After the park**, a part change for that car is rejected as "the car is gone" and rolled back (the gap 5 fix).
- **Save:** no section bump [minor 6]. `Parking.Records` is a new field of the `car-placement` section, which
  Newtonsoft fills with its default for an old save (`Data/Persistence/README.md`). An old server ignores it and
  unparks from the blob as today.

**Alternatives considered:**
- *A revision guard (the audit's proposal; the review's hybrid with details stored at park).* Smaller (no part
  overlay, no snapshot to the unparker), but the player sees a refusal while a newer change is on its way, and the
  window is a guess. Open question 3.
- *Have the server write the blob.* Impossible without the game's native serializer.

### D11. New digest keys

Projected by one Core mapper per key on both sides (`DigestMappers`), using `SyncOrder` keys where one exists. Values
are rounded like `CarDetailsSync.Signature` (3 decimals); lists are sorted by key, since the server's merge reorders
them. Spike 1.3 lists fields to leave out.

| Key | Fields | Client "not ready" while | Server resend |
|---|---|---|---|
| `car-details:<loader>` | fluids; wheels (index, width, rim, tire, ET, tire and rim ids); alignment; tuning modules and gearbox; paint; cosmetics per panel; plates; mileage; dyno measured values | the car is not Ready or away; the loader is dirty, awaiting or applying; an own send is kept without echo; a refill, extractor or oil drain runs on that car | the stored details, full |
| `workshop-tools` | per machine: slot UID, item or group id, mounting, balanced, read from the machines (`ToolMachine.ReadLocal`), not from `ToolSync`'s mirror [minor 9] | `ToolSync.IsBusy`, a pending slot update, the balancer minigame open | the slot states; the machine apply's `RemoveSilentAdditions` drops items the game adds during it |
| `warehouse` | items and groups as the inventory digest projects them (`wh:` prefix) | a full inventory sync runs, a warehouse move is in flight | the inventory snapshot |
| `garage` | unlocked skills, garage upgrades, barn count | never (server first) | `GarageState` |
| `jobs` | open order ids; active jobs (id, loader, order id) | an order take of this client is in flight | the jobs snapshot |

- `car-details:<loader>` rides with `cars:<loader>`: the same car per round, from the same cursor.
- The other keys are asked every round.
- **Spike 1.3 (2026-10-08, `merges-probe`, run `20261008-171541_L1_merges-probe`):** no entry drifts. Every entry
  (`CarDetailEntries.Signatures`, rounded to 3 decimals) was compared every 5 s on both clients: 2 min idle, 2 min with
  A seated and the engine running, after a test-drive round trip (only `info` mileage changed, equal on both) and after
  a wheel swap with a new rim and tire type (only that wheel's size changed; `WheelsData` keeps its load-time `Tire`
  and `Rim` ids on both clients, so they agree). The `car-details` digest needs no exclusions.
- **Log-only first** [minor 10]: each new key starts with its resend off (`desync_resend_keys` server setting lists the
  keys whose resend is on). A key's resend is turned on after `desync-soak` is quiet for it.
- *As built (tasks 9.1-9.4, 2026-10-08):* `DigestMappers.Details` projects each entry's `Signature`, `info` only its
  mileage; `Tools` skips empty slots, and a slot whose UID is 0 counts as empty (the tire changer and the engine stand
  report an empty group object, which made the first `desync-autofix` run confirm and resend `workshop-tools` for
  both clients at once). A loader is "dirty" for the digest only when an entry really differs from the last sent or
  applied value: the 1 Hz poll marks every Ready car dirty for 0.5 s of each second, and its phase against the 5 s
  round is fixed, so one client answered `car-details` "not ready" for 74 s in a row. The `workshop-tools` resend is
  one `ToolSlotUpdate` per machine (the claims stay); `garage` resends `WorldState` and `GarageState`; `warehouse` the
  inventory snapshot. `desync-soak` (run `20261008-202110_L2_desync-soak`, now with details, the tire changer, the
  warehouse and orders in its loop) confirmed nothing for any key, so every new key's resend is on by default.

### D12. Reconciliation rules

- **"Not ready" keeps a pending mismatch.** It neither confirms nor clears it; a match clears it. A pending mismatch
  expires after four asks of that key without a confirmation [minor 8], not after a time, because a car is asked only
  every `5 s × cars`. Confirmation still needs two mismatch answers with equal hashes.
- **The forced round asks everything.** `desync check` (verbose) asks every loaded car (`cars` and `car-details`) and
  every key for every client in the garage. This replaces the audit's `desync check all`.
- **Stall warning** (task 10.4, waits for row 18's switch-over). Per client and key, the server notes the first answer
  with the client's `NotReady` flag (not a missing server projection) in an unbroken series. After
  `desync_stall_seconds` (server setting, default 120) it logs `[Desync] <key> for client N has not been ready for
  <n> s` once per series and lists open stalls in the `desync` output and the bug report [minor 11]. Before row 18's
  D6, a car is "not ready" during all shared work, so the warning would fire in normal play.
- *As built:* a series also ends when the key was not asked for `3 × desync_check_interval_seconds × (cars + 1) + 10`
  seconds (a deleted car, a player on a trip), and the end of a warned series is logged "is ready again". The automatic
  round runs `desync_check_interval_seconds` after the previous round, forced rounds included; the console command
  `desync interval <s>` changes it until the next restart (the forced-round steps of `desync-autofix` use it).

### D13. Soak contention mode [M6]

`soak.ps1 -Contention [-ContentionWeight 15] [-ContentionKinds <list>]` adds a catalogue row `contention`.
`-ContentionKinds` forces the kinds drawn (for negative controls and debugging). The existing rules (1)–(7) stay.
*Numbering as built:* the soak's rule 7 is "memory and logs over hours" (`multiplayer-soak-and-scale` D6), so the two
new rules are **rule 8** (conservation) and **rule 9** (outcome); this section keeps the review's names
"conservation" and "outcome".

**A contention group.**
- The row picks a seeded kind and 2–4 actors (two on lanes 1 and 2, up to four on lane 3).
- It snapshots what rule 8 needs: the touched part keys, item ids and UIDs (server `cars`, `cardetails`, and
  `item-where`).
- It sets `net-hold out` on every member, issues each member's verb through `Invoke-Step`, and waits until each
  member's outgoing packet is held.
- **Server order.** It releases the members one at a time in a seeded order, and releases member i+1 only after the
  server logged member i's packet (`Wait-ServerLog`). The marker records the observed server order. Who wins is
  therefore decided by the seed, and outcome checks stay symmetric ("one winner").
- UIDs and other per-run values in the marker use the `{step:N:…}` templates that `Invoke-Machines` already uses
  (`soak.ps1:253-255`), so `-Replay` resolves them in the replayed run.
- **Settle:** up to 10 s for the members' dumps to agree on the touched sections, then the outcome check.

**Kinds.** First pass (part 2): this change's gaps plus P1, P2, M1 and L1. Second pass (task 10.5, after row 18 has
merged): the kinds that prove row 18. Until a kind's fix is in, it is listed in `soak-contention-known.txt`.

| Kind | Members do | Expected outcome | Gap | Pass |
|---|---|---|---|---|
| `part-same` | all `part-fast-unmount L k` | one accepted change; one new item of that part's id | P1 | 1 |
| `mount-same-item` | `part-twins L`, then each `part-fast-mount L <twin> <uid>` | one slot mounted, the item gone once | P2 | 1 |
| `examine-vs-unmount` | A `diag-examine L <tool> keys` picks k; A `diag-examine L <tool>`, B `part-fast-unmount L k` | k unmounted on the server and every client; one item | P5 | 1 |
| `gone-inflight` | B `part-fast-unmount L k`; A `car-delete L` / `park L` / `job-finish` | B's `parts.transactions` empty; B's inventory digest a match | P7, C3 | 1 |
| `details-pair` | A and B `cardetails-fluid`/`-wash`/`-wheel` on two entries of one section | both values on the server and every client | D2 | 1 |
| `details-same` | A and B `cardetails-fluid` on one entry | every side has the value of the member the server took last | D5 | 1 |
| `machine-same-item` | `give-group wheel`; A `tool-put TireChanger <groupUid>`, B `wheel-mount L <key> <groupUid>` | the group in exactly one place | I1 | 1 |
| `machine-vs-sale` | `give-item`; A `tool-put BrakeLathe <uid>`, B `sell-item <uid>` | the item on the lathe or sold, money changed only if sold | I1 | 1 |
| `mount-vs-sale` | A `part-fast-mount L <key> <uid>`, B `sell-item <uid>` | mounted or sold, never both | I5 | 1 |
| `machine-same-slot` | all `tool-put` or all `tool-take` on one machine | one winner, no item lost or duplicated | M1, M2 | 1 |
| `item-trades` | all `sell-item <uid>`, `econ-scrap <uid> 0` or `warehouse-move <uid> to` | applied once; money or scrap changed once | E3, I2, I5 | 1 |
| `lift-same` | all `lift i up` | one step | L1 | 1 |
| `place-same` | two `car-move` of different cars to one free place | the same places on every client and the server | L4 | 1 |
| `park-stale` | B `part-fast-unmount L k`; A `park L` | after `unpark`, k unmounted; the item once | L8 | 1 |
| `part-claim` | all `lock-try unmount L k finish` | one grant | P1, P12 | 2 |
| `parent-child` | cap and crankshaft from `lock-trace relations` | one denied | P4 | 2 |
| `fluid-vs-part` | A `lock-try fill`, B unmounts the reservoir | one refused; level 0 with the reservoir off | D1, D4 | 2 |
| `lift-vs-work` | A `lift i up`, B `vfx-unscrew L k pause 0.5` | the lift refused; no ghost or hidden renderer (`visuals`) | L2, L5 | 2 |
| `move-vs-work`, `park-vs-work`, `delete-vs-work` | A `car-move`/`park`/`car-delete`, B mid-unmount | refused | L3, C3 | 2 |

**New rules.**
- **Rule 8 (as drafted: rule 7), conservation.** For each contended part: items of that id in the inventory = before + (1 if the key is now
  unmounted on the server and was not before, −1 for the reverse). For each contended UID: in at most one of
  inventory, warehouse and machine slots, and in none of them if it was mounted or sold.
- **Rule 9 (as drafted: rule 8), outcome.** The kind's expected outcome. A kind listed in `scenarios/soak-contention-known.txt` (kind, gap
  id, the row that closes it) is reported as "known gap" and counted, not failed.

**The checkpoint gains:** the dump section `carDetails`; a forced digest round that expects every loader's `cars` and
`car-details` and the D11 keys; **no silent stalls** under rule 2 (a key "not ready" at two checkpoints in a row);
after row 18, `overlapViolations` 0.

### D14. Harness verbs, dump and scenarios

**Verbs** (owner row 19, registered in INTEGRATION.md):
- Row 4's registered setters, built now (task 1.4) [M7]: `cardetails-fluid <loader> <type> <id> <level> [cond]`,
  `cardetails-wheel <loader> <index> <w> <rim> <tire> <et>`, `cardetails-alignment <loader> <FL> <FR> <RL> <RR>`
  (`-` leaves a field), `cardetails-wash <loader> <dust> <wash> [panelIndex]`. Each through the game setters and
  `MarkDirty`. They replace the audit's `cardetails-set`.
- `cardetails-pour <loader> <type> <id> <seconds>`: adds `dt × 0.1` to the level and `dt × 0.05` to the condition every
  frame through `FluidsData.AddFluid` on the car's field, as `FluidRefillLogic` does, and marks the loader dirty;
  reports `{ start, end, decreases }`.
- `part-condition <loader> <key> <value>`: sets a part's condition locally like a repair and marks the loader dirty (a
  local attribute edit, for D2's unsent-edit step).
- `diag-examine <loader> <ToolType> [keys]`: with `keys`, reports the part keys the tool examines instead of only a
  count.
- `state-corrupt <car-details|workshop-tools|warehouse|garage> [loader]`: changes local state without a packet. `jobs`
  has no corrupt mode; it is proven by `desync-soak` only.
- `digest-hold <key> notready` (row 14's verb gains a mode).
- `item-where <uid>`: `inventory`, `warehouse`, `machine:<tool>` or `none`.
- `sell-item [uid]` (an item or a group), `warehouse-move <uid> to|from` [M6, I2].
- `inv-send <add|update|remove> <uid> [condition]`: sends a raw inventory packet for a local item, for D16's
  inventory rows.
- Server console: `jobs expire <id>` (expires an open order at once, for D16's J4 step).

`wheel-mount` keeps its signature `<loader> <key> <groupUid>`.

**Dump:** `carDetails` (per loader, rounded like `Signature`, lists sorted) and `parts.transactions` (open and
committed counts per loader). **Server:** `cars` gains `staleMerged` and `staleDropped`; `tools` gains
`unknownSlotItem` and `removeMissing`; `desync` lists open stalls; `--check-merges`.

**Scenarios** (each with `# areas:`):

| Scenario | What it checks |
|---|---|
| `car-stale-record` (`parts, cars, visuals`) | (1) A held (`net-hold on`); B `part-fast-unmount L k`; A `diag-examine L <tool>` (k from `… keys`); A released. Within 5 s: k unmounted on the server and both clients, `staleDropped` +1, A's and B's `cars` and `inventory` equal, one item of k's id added, `cars:L` a match for both in a forced `desync check`. (2) Reverse: B's packets held (`net-hold out`) while A examines and B unmounts; B's own send is not undone, and B's `visuals` show no ghost or hidden renderer. (3) B `vfx-unscrew L k pause 0.5`; A examines; B's `vfx-unscrew` status stays `paused`, k mounted on B, bolt progress unchanged; B `resume` commits; k unmounted everywhere, one item. (4) Unsent edit: B `part-condition L k2 0.9`, and A's examine of k2 arrives within B's 0.3 s window; B's condition reaches the server and A. (5) Replaced part: B unmounts k and mounts a new part of another quality while A (held) runs `part-condition L k 0.2`; the new part keeps its own condition everywhere. (6) After row 18 (task 8.1): B `lock-try unmount L k hold`; A examines; accepted, k stays mounted, B's lock still held; B `finish`; then a stale examine of k in the window before B's lock is released is not rejected. |
| `details-concurrent` (`details, cars`) | `net-hold on` on both; A brake, B coolant; release; both values on the server (`cardetails L`) and both clients. The same for `cardetails-wash` on two panels, `cardetails-wheel` 0 and 3, `cardetails-alignment` FL and RR. Same entry, both server orders: `net-hold out` on both, A and B set brake to different values, release A, wait for the server log line, release B; every side has B's value; then the reverse order, every side has A's value. Unsent edit: A sets brake, and B's coolant arrives at A within A's flush delay; A's brake still reaches the server. Pour: `net-delay 150` on A, `cardetails-pour L Brake 0 3`: `decreases` 0, B's level equals A's at the end. |
| `tools-item-race` (`tools, parts, economy`) | `give-group wheel`; `net-hold out` on both; A `tool-put TireChanger <groupUid>`, B `wheel-mount L <key> <groupUid>`; released in both server orders. The group is in one place on every side; `item-where` agrees; `tools` and `inventory` equal; after `tool-take TireChanger` no group with that id exists twice. Then on the brake lathe with a single item: `tool-put` against `sell-item <uid>` and against `econ-scrap <uid> 0` (the message names the scrapper). Mount against sale [I5]: A `part-fast-mount L <key> <uid>`, B `sell-item <uid>`, both orders: mounted or sold, never both, money changed only when sold. After row 18 (task 8.3): B `lock-try mount <key> <uid> hold`, A `tool-put`: refused `Returned`, the item stays in the inventory, B's `finish` commits. |
| `park-stale` (`placement, parts, persistence`) | `net-hold on` on A; B `part-fast-unmount L k`; A `park L`; A released. After `unpark`, k is unmounted on every side, the item exists once, dumps equal. B `cardetails-fluid` coolant before A's park: after `unpark`, coolant equals B's. Own unsent change [M4]: A `part-fast-unmount L k2` and `park L` within 0.2 s; after `unpark` k2 is unmounted and its item exists once. Unparker leaves [C2]: B starts `unpark` and disconnects before its baseline; the car is back in its slot, and a later unpark by A shows k unmounted. Restart: park, restart the server, unpark: the same. |

| `server-answers` (`economy, jobs, placement, parts`) | One step per D16 row with a message; each step races or forces the losing action, then checks the loser's dump section for that key against the server right after the answer, without `resync`. Inventory: A `inv-send update <uid>` for an item B mounted (A held): A has no copy of it; A `inv-send add <uid> 0.3` for a stored item: A's copy equals the server's. Repair: A `tool-repair` on an item B mounted meanwhile: refused, A has no copy. Upgrades: A and B `econ-skill-unlock <id> <level>` with both held: points spent once, both `skills` equal the server's. Second park and second delete: A and B `park L` (and `car-delete L`) with both held: one wins, both `placement` and `parking` equal the server's. Orders: B, not the generator, `orders-generate`: B's open orders equal the server's. Expired accept: A held, server `jobs expire <id>`, A `orders-accept <id>`: A shows the message and its orders equal the server's. Second job end: A and B `job-finish` with both held: money once, both `stats` and `jobs` equal. Dropped transaction: B's unmount with `net-hold out` reaches `Hide`, A `car-delete L`: B's inventory equals the server's. |

**Updated scenarios:**
- `seat-engine` gains a two-player step [S1]: A and B `sit 0 left` with `net-hold out` on both, released in both
  orders: one is seated, the other is out of the seat locally right after (`local` seat in the dump) with the
  message, and both rosters show one player in that seat.
- `car-gone-inflight` gains `park` and `job-finish` steps (the audit's planned variants), and checks B's
  `parts.transactions` per loader [P7].
- `tools-race` gains a two-player stand-part step (both unmount the same stand part, both orders: one accepted, one
  rejected and rolled back) and a stand-part change while the other player takes the engine off the stand (rejected,
  rolled back) [M4, M5 rows of the ledger].
- `desync-autofix` (a `state-corrupt` step per new key; a mismatch confirmed across a `digest-hold … notready` round,
  with forced rounds and a long `desync_check_interval_seconds`).
- `desync-soak` (the new keys, log-only first, then with resend), `soak` (contention), `locks-fluid` (task 8.2),
  `car-details` (must still pass).

### D15. Spikes

1. **Machine items** (decides D9's unknown-UID rule): log in `ToolsStore.Check` whether each put's UID was in the
   server's inventory, removed by the putter, or never seen; run `tools-slots`, `tools-race`, `tools-latejoin`,
   `tools-car-effects`.
2. **What examine and condition paths change** (confirms D1's groups): `part-state` of the touched keys before and
   after `diag-examine` with each `ToolType`, `tool-use` with the welder on a body part, `tool-repair` and
   `tool-paint-part` on a part that is then mounted.
3. **Detail values that change by themselves** (decides D11's exclusions): `cardetails-show` every 5 s for 2 min on an
   idle car, with the engine running, after a test drive, and after a wheel swap (tire and rim ids, `c369763`).

### D16. No silent drops: the server answers every action it refuses, ignores or overrides

Rule (user, 2026-10-07): whenever the server refuses, ignores or overrides a client's action, it sends that client the
authoritative result: a refusal that carries the current state, or the resend of that key's state. The client whose
action was cancelled then equals the server right after, without F7 and without waiting for a digest.

Audit of every "ignored", "dropped" or silent-return path in the server's handlers (`rg "ignored|dropped"` plus the
early returns of `GarageUpgradeHandler`, `ParkingHandlers`, `CarHandlers`, `PlacementHandlers`, `JobsService`,
`InventoryHandlers`, `CarDetailsStore` and `CarPartsHandlers`) on `d1dd908`:

| Path | Row | Today | Now sends to that client |
|---|---|---|---|
| `InventoryHandlers` `Update` of an item the server does not have | I3, M8 | logged, nothing | `InventoryItemAction Remove` of that UID (the client drops its copy if it still has one), with the remover's name in the log line |
| `InventoryHandlers` `Add` of an item or group already present | I3 | logged, nothing | nothing when the stored copy is equal; the stored copy as `Update` when it differs |
| `InventoryHandlers` `Remove` of a UID the server does not have | I1, I7 | silent | nothing to send: the client already lacks the item, as the server does; logged and counted (`removeMissing`). The machine put is answered by D9 |
| `EconomyRules.PartRepair` refused because the item is gone | M8 | the refusal | the refusal plus the item's `Remove` |
| Part transaction flushed after 10 s idle | I7 | the delta as plain inventory packets | each packet answered by the rows above |
| Part transaction dropped with its loader (row 18 `DropLoader`, task 3.5) | I7 | the delta is dropped; the item stays only on that client | the client rolls the delta back locally, as the gap 5 rejection (`d1dd908`) does, so it equals the server |
| `GarageUpgradeHandler`: unknown id, level out of range, already unlocked, not enough money or points | E4 | silent `return`, or no change | `GarageState` and `WorldState` to the requester (a success still broadcasts both) |
| `ParkFromGarage` with no car on the loader (the second park) | C4 | `Invalid`, no give-back | `Invalid` plus `ParkingService.SendState` and `CarSpawnDelete` for that loader |
| `HandleCarSpawnDelete` of an empty loader (the second delete) | C4 | relayed to the others | not relayed (it is a no-op); the sender already equals the server |
| `PlacementHandlers.OnCarPlaceChange` for an unknown loader | — | debug log, nothing | `CarSpawnDelete` for that loader |
| `JobsService.OnOrderGenerated` from a client that is not the generator, a tutorial mission, or over the open-order limit | J2 | logged, nothing | the jobs snapshot (open orders) to that client, which drops its local order |
| `JobsService.OnOrderAction` Accept refused `Unknown` | J4 | the refusal; the client keeps the order and shows "Another order is being taken" | the refusal plus `JobRemoved { JobId, Reason = Expired }`; the client shows "This order is no longer available." |
| `JobsService.OnJobEnd` not active (the second end) or out of bounds | J5 | `WorldState` | `WorldState` plus the jobs snapshot (active jobs and orders) |
| `JobsService.OnJobEnd` refused because the car is away (row 13) or another player holds a lock on it (row 18, `Busy`) | — | `WorldState`, plus row 18's `CarLockResult { RequestId = 0, Refusal = CarBusy }` | the jobs snapshot as well, so the client gets the job back; today the native `EndJob` has already removed the job (and may have cleared the car) on the ending client, which keeps it lost until F7. Row 18 refuses the end on the client first while its mirror shows another player's lock (`JobHooks.BeforeEndJob`), so only a lock taken in flight reaches this path (note from row 18, 2026-10-08) |
| `CarDetailsStore.OnUpdate` for a gone or replaced car | — | debug log | nothing to send: that client gets the car's delete or new spawn through the car lifecycle |
| `CarPartsHandlers.OnBaseline` dropped (replaced car, or not the spawner) | — | logged | nothing to send: the spawner's baseline snapshot reaches every client |
| `CarPartsHandlers.FindConflict` removal of a UID nobody took | — | "ignored", accepted | nothing to send: the change is accepted (the game builds such groups itself) |
| Seat already taken | S1 | stored as sent | D17 |
| Drive and visual budget drops (`DriveHandlers`, `VisualHandlers`) | — | rate limit | nothing to send: visual-only streams, the next state replaces them |

Every row with a message gets a two-client step in `server-answers` (D14): the losing client's dump equals the
server's state for that key right after the answer, checked without `resync`.

### D17. The server arbitrates seats

- `PlayerHandlers.OnPlayerPresence`: when a record claims a seat (`SeatCarLoaderId`, `SeatLeft`) that another player in
  the garage already holds, the first holder keeps it. The second record is stored with no seat and no engine, and the
  server sends `SeatRefused { CarLoaderID, SeatLeft, HolderPlayerId }` to that client. The relay to the others carries
  the stored record, so nobody sees two players in one seat.
- The refused client leaves the seat with the game's own exit, `GameScript.ExitFromInterior(true)` (`dump.cs:432023`,
  started as a coroutine as the harness `stand` verb does). Row 6's prefix on it (`SeatEngine.BeforeExitFromInterior`)
  clears the pending and local seat, so the next presence record carries no seat. It shows "<name> is sitting there."
- A seat frees when its holder's record has no seat, on leave and on a scene change.
- The car's engine follows the seat: the refused client's engine flags are cleared with the seat.

## What the server stores and what it only relays

| State | Stored | Relayed |
|---|---|---|
| Part records | the normalised record, `Changed` cleared (D1) | the stored record with the groups written (D2); to the sender, the groups that differ |
| Car details | the merged, complete details (D5) | the incoming entries with their masks |
| Machine slots | as today; on a `Returned` refusal the kept copy goes back into the inventory | the `Add` of a returned item to the others |
| Removed items | the remover for the session (last 10 000 UIDs); a copy for 60 s | — |
| Parked cars | the blob, plus `Parking.Records` (server only, saved) | the blob only, as today |
| Digests | pending mismatches with their ask count, and stall series (runtime only) | — |
| Seats | the first holder in the presence record (D17) | the stored record; `SeatRefused` to the second |
| Refused, ignored or overridden actions | unchanged decisions | the authoritative result to that client (D16) |

## Late join and return to the garage

- Snapshots carry the stored state with `All`; nothing changes in the snapshot path.
- Details: a joiner gets full details; `lastKnown` is filled per entry from that apply. Send copies are cleared with
  `CarDetailsSync.Reset`.
- Parking: a joiner sees the blobs as today. Whoever unparks the car later gets the records applied; a client that
  joins after the unpark gets the car through the normal cars snapshot.
- Digests: no answer before the initial sync, as today.

## Packets

```
CarBodyPartUpdatePacket   + PartFields Changed            [OptionalField]
CarSubPartUpdatePacket    + PartFields Changed            [OptionalField]
CarDetailsUpdatePacket    + int WheelMask; AlignmentFields AlignmentMask   [OptionalField]
ToolSlotRejectedPacket    + SlotItemOutcome Item          [OptionalField]
[Flags] enum PartFields       { None, Mount, Bolts, Identity, Condition, Quality, Examined, Paint, Dust, Switched, All }
[Flags] enum AlignmentFields  { None, FL, FR, RL, RR, LampLH, LampLV, LampRH, LampRV }
enum SlotItemOutcome          { Unchanged, Returned, Gone }
SeatRefusedPacket { int CarLoaderID; bool SeatLeft; int HolderPlayerId; }                       S→client (D17)
```

One packet type is added, `SeatRefused`, appended to the end of `PacketTypes` at merge time (after whatever row 18
appended). `[OptionalField]` is moot while the protocol hash forces
equal builds, but harmless. Digest keys are strings in the existing digest packets.

## Migration

The protocol hash forces equal client and server builds. No save section version changes: `Parking.Records` is an
additive field. Rollback is reverting the change; an old server ignores the records.

## Risks / Trade-offs

- **A mask misses a real change** → the server keeps its value; the car digest shows the difference and the resend
  repairs it. `--check-merges` and spike 2 pin the groups.
- **`staleDropped` loses an examine** that was made on a stale view of an unmounted or replaced part. The examine
  result belonged to the old part, so dropping it is right; the player can examine again.
- **The parker waits up to 1 s or sees "Try again in a moment"** while its own change is still settling.
- **Per-entry flushes leave a stale entry on the server when a local change is lost before its flush** (disconnect) →
  as before.
- **Send copies grow** → bounded per loader (16 sends or 10 s).
- **Parked records and the blob disagree on the hierarchy** (an engine swap the server did not see) → keys that do not
  resolve are dropped and logged; the car digest checks the unparked car.
- **Unknown machine UIDs accepted** → spike 1 measures it; open question 6 can tighten it.
- **New digest keys raise false alarms** → log-only per key first, spike 3, `desync-soak`.
- **Contention runs are flaky** → the server order is forced and recorded; kinds with an open gap are reported, not
  failed.
- **Two branches rewrite the same methods as row 18** → the agreed ownership in Context and open question 5.

## Open questions / assumptions

The user's open questions are in proposal.md. Assumptions taken here without the user:

1. The server keeps relaying a detail update to its sender (D7), so clamps and later writes reach it.
2. Removed-item copies, pending mismatches and stall series are runtime only.
3. Contention kinds that need row 18 verbs are added in the second pass, after row 18 merges.
