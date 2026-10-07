# Design

## Context

Source: `docs/audits/race-and-drift-audit.md` (gaps 3, 6, 7, 9, 10 and "Soak contention mode"). State on `main`
(`d1dd908`), with row 18 (`part-locks`, branch `change/part-locks`) for what it changes:

- **Part changes (row 1).** `PartChangeTracker` sends a `CarPartsChange` after three stable 0.1 s polls. Every record
  is the full captured state of a part. A precondition (`WasUnmounted`) is added only for keys whose mount state
  differs from the client's last known record (`PartChangeTracker.cs:137-142`). The server checks preconditions and
  removed items (`CarPartsHandlers.FindConflict`), then stores the records as they came. The only field merge is
  `IsExamined` (kept true, `:63-67`), and those merged records go back to the sender in the accepted result. The
  server relays the sender's packet to the others (`:80`).
- **Remote apply (row 1).** `PartChanges.OnRemoteChange` aborts the local open transaction for every key in the
  change (`PartChanges.cs:46`) and applies every record through `PartApplier`, which flips mount state with the
  game's "from save" methods (`HideBySavegame`, `ShowBySaveGame`) and writes condition, quality, examined, dust and
  bolt state (`SetMountObjectData`).
- **Row 18.** Strict locks before part work. `FindConflict` adds "a record that flips a key another player holds in
  X is rejected"; flips of S keys are counted (`unlockedFlip`). The lock is released by the commit after which every
  X key reached its target state; attribute-only changes do not release. Examine and diagnostic tools take no lock
  (row 18 non-goal). Row 18 D4 adds `CarDetailsSync.FlushNow(loader, sections)`, which reuses `Flush`.
- **Car details (row 4).** A 1 Hz poll plus hooks mark sections dirty; `Flush` reads whole sections after a 0.5 s
  delay and sends every section whose signature changed, whole (`CarDetailsSync.cs:128-144`). The server replaces
  sections, except fluids, cosmetics and tuning modules, which it merges per entry (`CarDetailsStore.cs:70-114`),
  and relays the packet to every client including the sender. The sender skips echoes older than its latest send
  and applies the latest (`CarDetailsSync.cs:148`). After any apply, the client re-reads and remembers the whole
  section (`:172`), so a local edit of another entry of that section that was not flushed yet is remembered as known
  and never sent: a second lost-update path the audit did not list.
- **Machines (row 5a).** A put runs the game (which deletes the item or group from the inventory, so
  `InventoryHook` sends `Remove`) and then `ToolSync.SendLocal` sends `ToolSlotUpdate { State, ExpectedUid }`. The
  server's `ToolsStore.Check` compares the held UID and the balancer claim and refuses an item that is on another
  machine. It does not look at the inventory. A refused client runs `Compensate`, which re-adds the item locally if
  it is neither on a machine nor in its inventory; that `Add` goes through the hook to the server.
- **Parking (row 2).** `ParkFromGarage` stores `request.Car` (the parker's `NewCarData` blob, the game's binary
  format) and clears the loader. Unpark registers a spawn from the blob; the unparker loads it and uploads the
  baseline, which becomes the server's records; its full details snapshot follows.
- **Digests (row 14).** Keys `world`, `inventory` (warehouse passed as `null` on both sides), `cars:<loader>` (one car
  per round, round robin) and `car-placement`. A "not ready" answer clears a pending mismatch. A mismatch confirms
  after two answers with equal hashes; then the server asks for the detail, logs it and resends the section.
- **Soak (row 11).** `soak.ps1` runs one action per loop, skips keys in use, compares the dump sections `stats,
  inventory, cars, placement, jobs, tools, toolPositions` at quiesced checkpoints, and forces a digest round of
  `world`, `inventory` and `car-placement` only.

Game facts this design relies on (checked in `dump.cs` and the decompiles under `native/out/`):

- `FluidRefillLogic.Update` adds `deltaTime × 0.1` to `CarLoader.FluidsData` through `FluidsData.AddFluid` every
  frame while the button is held, and reads the level back through `GetLevel`. It keeps no local amount
  (`cardetails_clean/FluidRefillLogic$$Update.c`; fields `power`, `CurrentFluid`, `fluidId` only). A level written by
  an apply during a pour is where the pour continues from.
- `FluidsData` is a struct (`dump.cs:445429`) with `SetLevelAndCondition`, `GetLevel` and `AddFluid`.
  `WheelsAlignment` is a struct (`dump.cs:437140`); `CarDetailsIO.ApplyAlignment` already sets the headlamps by
  read-modify-write, and the wheel fields can be set the same way one at a time.
- `PartScript.IsExamined` is a plain field (`dump.cs:438752`) with `SetExamined(bool)`.
- None of the methods this change calls is patched, and nothing here takes `NewCarData` or `FluidsData` by value.

## Goals / Non-Goals

**Goals:**
- A change that does not flip a part's mount state never changes that part's mount state, identity or bolts on the
  server or on any client, however stale the sender's view.
- Changes by several players to different entries of one car's details are all kept, on the server and on every
  client.
- A player's own change is never rolled back by its own echo.
- An inventory item ends up in exactly one place: the inventory, the warehouse, one machine, or mounted on one car.
- A parked car holds every change the server accepted before the park.
- Every piece of shared state the server owns is in a digest, and a key that is busy for a while does not hide a
  mismatch forever.
- The soak can make players contend on purpose, replayably, and fails when an item is duplicated or lost.

**Non-Goals:**
- Locks for car tools, machines or examine (row 18 open question 4 and its non-goals stand).
- Two players writing the same detail entry: the last write wins (accepted 2026-10-06; open question 1).
- The further audit gaps below the top 10: P11 (`ShowMounted` recheck), M4 (engine-stand race in `tools-race`), X4
  (F7 with a lock; row 18 D8), C1 (`RegisterSpawn` on an occupied loader), E5 (fee `RequestId` deduplication), I6
  (UID ranges), D8 (a full details snapshot replaces the stored details by design), C5 (second baseline). They stay
  in the audit's list for a later row.
- Engine-stand parts in a digest (the slot is compared, its part overlay is not).
- A server-built parking blob: the server has no game code, and the blob is the game's native format.

## Decisions

### D1. Part records carry a changed-field mask; the server merges per field group

Every `CarBodyPartUpdatePacket` and `CarSubPartUpdatePacket` gets `PartFields Changed` (`[Flags]`, Core):

| Group | Mechanical record fields | Body record fields |
|---|---|---|
| `Mount` | `Unmounted` | `Unmounted` |
| `Bolts` | `MountObjectData` | — |
| `Identity` | `PartId`, `TunedID` | `TunedID` |
| `Condition` | `Condition` | `State.Condition`, `State.Dent` |
| `Quality` | `Quality` | `State.Quality` |
| `Examined` | `IsExamined` | — |
| `Paint` | `IsPainted`, `Color`, `PaintType`, `PaintData` | — |
| `Dust` | `Dust` | — |
| `Switched` | — | `Switched` |

Other `State` fields of a body record follow the `Condition` group. `None` (0) means "whole record": baselines,
snapshots, the harness's corrupt verbs and anything that does not set a mask.

**Client.** `PartChangeTracker.Send` sets `Changed` per record from the difference between the captured record and
`LoaderSync`'s last known record of that key, group by group, with the epsilon of `PartRecords.SameState` (0.001).
A key without a last known record gets `None`. Preconditions are built as today.

**Server** (`CarPartsHandlers.OnChange`, after `FindConflict` and row 18's lock check), per record:

1. A record that **mounts** (it has a precondition with `WasUnmounted = true` and `Unmounted = false`) is taken
   whole. It describes a new item: its id, condition, quality and paint belong to that item, and a field that
   happens to equal the sender's old view is still the item's value. The existing `IsExamined` rule stays.
2. Every other record is merged: the stored record with the masked groups copied from the incoming one. No stored
   record, or `Changed = None`: whole record.
3. The merged record is what the server stores, relays (the relay is rebuilt from the stored records, not the
   sender's packet) and, when it differs from what the sender sent, returns in the accepted result (the existing
   `merged` list, now for body records too).
4. A record whose unmasked groups differed from the stored ones was made on a stale view; it is counted as
   `staleMerged` and logged at debug level with the key and the groups kept.

The helper `PartRecordMerge.Merge(stored, incoming, isMount)` lives in Core and is also used by
`ToolsStore.OnPartChange` for engine-stand parts, which carry the same records. `--check-merges` (in the pattern of
`--check-locks`) runs it on synthetic records.

Example (the audit's gap 3): B's unmount of `s:3.22.4` is stored; A, who has not received it, runs an examine. A's
record has `Changed = Examined`, so the server keeps `Unmounted = true`, sets `IsExamined`, relays the merged record
to B, and returns it to A, whose game then hides the part. B's item stays B's, and nobody's item is duplicated.

**Alternatives considered:**
- *Keep only the stored `Unmounted`/`Switched` (the audit's proposal).* Misses the case where B unmounted the old part
  and mounted a new one: A's stale record carries the old part's id and condition and would overwrite the new
  part's.
- *Reject a stale attribute record* (open question 2). Simple on the server, but A loses the examine result and runs
  the rollback path that caused playtest finding 4.
- *A base revision per record and a three-way merge on the server.* Needs the base record, so the server would keep
  a history per key. The mask carries the same information with no history.

### D2. Receiving side: abort only on a real mount change; keep local work's mount state

- `PartChanges.OnRemoteChange` and `Apply` abort a local part transaction only for keys whose relayed record differs
  from `LoaderSync`'s record in `Mount` or `Identity` (or `Switched` for body parts). An attribute-only change no
  longer aborts the work of the player who is unscrewing that part.
- `PartApplier.Apply` treats a key as **in local work** when `PartTransactions` has an open transaction containing it
  or the game's current mount state differs from `LoaderSync`'s record (a local flip that is not sent yet). For such
  a key it writes only `Condition`, `Quality`, `Examined`, `Paint` and `Dust`, and leaves mount state, bolts and
  identity alone. `LoaderSync` still stores the relayed record (it is the server's state); the local commit's
  precondition is then checked against it and decides.
- A relayed record that does flip mount state on a key in local work keeps today's behaviour: the transaction is
  aborted and the record applied (that is another player's committed unmount or mount, which is what the
  precondition would have rejected anyway).
- The same two rules apply to the merged records of the player's own accepted result.

### D3. Parts and row 18's locks

- Row 18's `FindConflict` rule (reject a flip of another player's X key) runs before the merge and is unchanged. Its
  counter `unlockedFlip` is computed on the incoming record's `Mount` group.
- An attribute change on a key another player holds in X (an examine while B unscrews) is accepted. Its mask never
  contains `Mount`, `Bolts` or `Identity`, so the merge cannot touch B's work.
- The lock holder's own commit is merged too: B's unmount record has `Changed = Mount|Bolts`, so A's examine or a
  condition change made during the lock survives B's commit.
- Row 18's release on commit ("every X key reached its target state") is evaluated on the merged, stored records.
- After the switch-over, "in local work" (D2) also covers keys in this client's own X locks (task 8.1). Before it, the
  transaction and unsent-flip checks cover the same cases.
- Gap 3 does not wait for row 18: the merge is the part of the fix that no lock provides, since examine is not gated.

### D4. Car details travel as entries

`CarDetailsSync.lastKnown` becomes `loader → entry id → signature`:

| Section | Entry id | Wire form |
|---|---|---|
| Fluids | `f:<type>.<id>` | `Fluids` holds only the changed entries |
| Wheels | `w:<index>` | `Wheels` is the whole small array; `WheelMask` (bit per index) says which entries count |
| Alignment | `a:<FL|FR|RL|RR|LampLH|LampLV|LampRH|LampRV>` | `Alignment` whole; `AlignmentMask` (`AlignmentFields`) says which fields count |
| BodyCosmetics | `c:<partIndex>` | `BodyCosmetics` holds only the changed panels |
| Tuning | `t:<partKey>`, `t:gearbox` | `Tuning.Modules` holds only the changed modules; `Gearbox` is `null` unless it changed |
| Paint, Plates, Info, BonusParts, Dyno | `paint`, `plates`, `info`, `bonus`, `dyno` | whole, when changed |

- `Flush` reads the dirty sections as today, computes each entry's signature (the rounding of
  `CarDetailsSync.Signature`), and sends one `CarDetailsUpdate` with only the changed entries. It still sends at most
  one packet per flush.
- After the send, only the sent entries are remembered.
- A full snapshot (`SendFull`, `IsFull`) is unchanged: every entry, masks "all".
- A mask of 0 with a non-null `Wheels` or `Alignment` means "all" (full snapshots and anything that does not set
  it).

**Alternatives considered:**
- *Nullable fields in `ModAlignment` and `null` entries in `Wheels`.* The same information, but `ModCarDetails` is
  also the saved and stored form, which must stay complete. The masks live on the packet only.
- *Per-entry packets.* More packets for a multi-entry change such as a wash, for no gain over one packet with the
  changed entries.

### D5. Server: merge per entry, relay entries

- `CarDetailsStore.Merge` merges wheels per masked index (growing the stored array if needed) and alignment per masked
  field, and keeps its per-entry merge of fluids, cosmetics and tuning modules. Paint, plates, info, bonus parts and
  dyno stay whole.
- The relay is the clamped incoming packet with its masks, as today; receivers apply only what it carries (D6).
- The server stores the merged details; snapshots and resends send the stored, complete details.

### D6. Apply and remember per entry

- `CarDetailsIO.Apply` honours the masks: `ApplyWheels` skips unmasked indices; `ApplyAlignment` writes only the
  masked fields (read the struct, change those fields, write it back, as it does for the headlamps). Lists apply the
  entries they hold, as today.
- After an apply, the client remembers only the entries the packet carried, read back from the game (so the game's
  own normalisation is what is remembered). A local edit of another entry stays "changed" and goes out with the next
  flush. This closes the lost-update path described in Context.

### D7. The own echo is skipped per entry, by `ClientSeq`

- The sender keeps, per loader, the signatures of the entries it sent under each `ClientSeq` (the last 16 sends or
  10 s, whichever is shorter).
- An own echo (`SourceClientId` = own id) older than the latest send is skipped, as today.
- For an own echo with a kept `ClientSeq`, each entry whose signature equals the kept one is dropped; only entries the
  server changed (clamping) are applied. If nothing is left, nothing is applied. The kept copy is then discarded.
- The server keeps relaying to the sender: the echo is how a clamp reaches the sender, and the snapshot counting path
  stays as it is.
- A pour is therefore never set back to the level of its last send (see the `FluidRefillLogic.Update` fact in
  Context). The harness verb `cardetails-pour` proves it with `net-delay 150`.

### D8. Fluids under row 18's locks

- Row 18's `FlushNow(loader, sections)` reuses `Flush`. With D4, it sends only the changed entries of those
  sections. At a fill's release that is the filled fluid (and any other fluid this client changed itself). This is
  the audit's correction for row 18 ("flush only the locked fluid's entry") without a second API.
- Whichever of the two changes merges second adapts: if row 18 is first, task 4.2 changes `Flush` and `FlushNow`
  inherits it; if this change is first, row 18 rebases its `FlushNow` onto the per-entry `Flush`.
- Task 8.2 adds the audit's step to `locks-fluid`: A holds `f:Brake.0` and fills while B holds `f:EngineCoolant.0`
  and fills; after both releases, the server's details and both clients have both new levels.

### D9. Machine items: the removed-by-other rule, and the refusal says what happened to the item

**Server.**
- `InventoryChanges` keeps, for each removed UID, the remover, the time and a copy of the removed item or group, for
  60 s. Every remove path notes it: the inventory `Remove`, a part change's delta, sale and scrap
  (`EconomyService.RemoveItem` and the shop already note the remover).
- `ToolsStore.Check`, for a put (incoming not empty, a UID other than the current one), after today's checks:
  - the UID is in the server's inventory, or this client removed it: accepted (today's path);
  - another client removed it (mounted, sold, scrapped, put elsewhere): refused, "used by player N";
  - the server never saw the UID: accepted, logged and counted (`unknownSlotItem`), unless spike 1.1 shows machines
    never use such items, in which case it is refused (open question 4).
- Every refusal of a put carries `ToolSlotRejectedPacket.Item`:
  - `Returned`: this client had removed the item (its `Remove` came first). The server puts the kept copy back into
    its inventory, clears the remover entry, and relays the `Add` to the others.
  - `Gone`: another client removed it. Nothing is restored anywhere.
  - `Unchanged`: no item was involved (a take or an empty slot), today's path.
- A `Remove` of a UID the server does not have stays without a reply. It is logged with the recorded remover and
  counted (`removeMissing`). The machine path is answered by the slot refusal; a generic reply would have nothing to
  restore.

**Client.** `ToolSync.OnRejected`/`Compensate` follow the outcome: `Returned` re-adds the item locally with the
inventory hooks off (the server already has it); `Gone` drops it and shows "<name> used this part."; `Unchanged`
keeps today's compensation.

**Why `Returned` is done by the server.** If the client re-added the item through the hook (today), a third player's
mount commit that reaches the server between the refusal and that `Add` is rejected by `RemovedByOther`. With row
18's item locks that window becomes the normal case: B holds the item for a mount, A's put is refused, and B's
commit must find the item in the inventory.

**After row 18's switch-over** (task 8.3): `ToolsStore.Check` also refuses a put of a UID in another owner's
`CarLocks` item lock (outcome `Returned`). The message is row 18's item text, "<name> is mounting this part."

**Alternatives considered:**
- *Make the machine put one transaction that carries its inventory removal* (the audit's "better still"). It needs a
  put window on the client to capture the game's `Inventory.Delete`/`DeleteGroup` per machine, five machine hooks in
  all. The server-side rule gives the same guarantee with the packets as they are.
- *Machines take row 18 item locks before a put.* A round trip for every put, for a race that the server can decide
  on arrival.

### D10. Parking stores the server's record of the car

- **At park** (`ParkFromGarage`, accepted, before `ClearLoader`): if the loader has a baseline, the server copies its
  body and mechanical records, its engine-swap flag and its valid details into `ParkedRecord { CarId, Body, Sub,
  EngineSwap, Details }` under `PlacementState.Parking.Records[ParkedCar.Id]`. The blob is stored as today: the game
  needs it to load the car, and the parking window reads it.
- **Server only.** Records are never broadcast; parking state messages carry the blobs as today. A parking swap keeps
  them (they are keyed by the car id). A car removed from parking drops its record.
- **At unpark** the record moves to the new loader entry (`entry.Parked`). When the unparker's first baseline arrives
  (`OnBaseline`, from the spawner):
  - the server stores the uploaded records, then replaces each one whose key exists in the parked record with the
    parked record (keys that do not resolve are counted and logged: `parkedRecordsDropped`);
  - it sends the live snapshot to every client, **the unparker included** (today `except` the unparker), so the
    unparker's game takes the server's state through `PartApplier`, as a late joiner's does;
  - if the parked record has details, the server stores them for the new `SpawnSeq` and sends them full to everyone,
    and drops the unparker's first full details snapshot for that `SpawnSeq` (logged).
- **The unparker leaves before the baseline** (`PlacementRules.OnLoaderCleared`, `SpawnerLeft`): the car goes back to
  parking with its record.
- **After the park**, a part change for that car is rejected as "the car is gone" and rolled back (the gap 5 fix), so
  its item is never duplicated. Details that another player had changed but not flushed yet never reached the server
  and are lost, as before; with row 18, park is refused while a fluid lock is held, and a fill's release flushes
  first.
- **Save:** `car-placement` section v2 adds `Parking.Records`; v1 migrates with no records, and such a car unparks
  from its blob as today.

**Alternatives considered:**
- *A revision guard (the audit's proposal; open question 3).* The park request carries the parker's revision and the
  server refuses it while a newer change exists or another player's details arrived in the last 2 s; the client
  retries once. Smaller (no save change), but the player sees a refusal, the 2 s window is a guess, and the parked
  blob still holds the parker's view of the details.
- *Have the server write the blob.* Impossible without the game's native serializer, and patching `NewCarData`
  handling is forbidden.

### D11. New digest keys

Projected by one Core mapper per key on both sides (`DigestMappers`), using the existing `SyncOrder` keys where one
exists. Values are rounded like `CarDetailsSync.Signature` (3 decimals); spike 1.3 lists fields to leave out.

| Key | Fields | Client "not ready" while | Server resend |
|---|---|---|---|
| `car-details:<loader>` | fluids (type, id, level, condition); wheels (index, width, rim, tire, ET, tire and rim ids); alignment (8 fields); tuning modules (tuned, values, tuning value, ECU stage) and gearbox ratios; paint; cosmetics per panel (colour, paint type, livery, strength, tint, dust, wash); plates; mileage; dyno measured values | the car is not Ready or is away; the loader is dirty, awaiting or applying in `CarDetailsSync`; an own send has no echo yet; a refill, extractor or oil drain runs on that car | the stored details, full, to that client |
| `workshop-tools` | per machine: slot UID, item or group id, mounting, balanced | `ToolSync.IsBusy`, a pending slot update, the balancer minigame open | the slot states, as the snapshot sends them |
| `warehouse` | items and groups as the inventory digest projects them (`wh:` prefix) | a full inventory sync runs, a warehouse move is in flight | the inventory snapshot (it carries the warehouse) |
| `garage` | unlocked skills, garage upgrades, barn count | never (server first) | `GarageState` to that client |
| `jobs` | open order ids; active jobs (id, loader, order id) | an order take of this client is in flight | the jobs snapshot to that client |

- `car-details:<loader>` rides with `cars:<loader>`: the same car per round, from the same round-robin cursor.
- The other keys are asked every round; each is a few hundred bytes at most.
- The inventory digest keeps passing `null` for the warehouse, so a warehouse mismatch resends only once, under its
  own key.

### D12. Reconciliation rules

- **"Not ready" keeps a pending mismatch.** It neither confirms nor clears it. A match clears it. A pending mismatch
  that is not confirmed within 60 s of its first sighting expires. Confirmation still needs two mismatch answers with
  equal hashes, so a key that is busy between the two mismatches still confirms, and a passing state does not.
- **The forced round asks everything.** `desync check` (verbose) asks every loaded car (`cars` and `car-details`) and
  every key for every client in the garage. The 5 s poll keeps its one-car cursor. This replaces the audit's
  separate `desync check all` command.
- **Stall warning** (task 10.4, waits for row 18's switch-over). Per client and key, the server notes the first "not
  ready" of an unbroken series. At 120 s it logs `[Desync] <key> for client N has not been ready for 120 s` once per
  series, and lists open stalls in the `desync` output and in the bug report. Before row 18's D6, a car is "not
  ready" whenever anyone holds a claim on it, which is all of shared work, so the warning would fire in normal play.

### D13. Soak contention mode

`soak.ps1 -Contention [-ContentionWeight 15]` adds a catalogue row `contention`. The existing rules stay: (1)
checkpoint equality, (2) confirmed desyncs, (3) unexpected disconnects, (4) log errors, (5) storms, (6) watchdog.

**A contention group.**
- The row picks a seeded kind and 2–4 actors (two on lanes 1 and 2, up to four on lane 3).
- It snapshots what rule 7 needs: the touched part keys, item ids and UIDs, on the server (`cars`, `cardetails`) and
  through `item-where`.
- It sets `net-hold on` on every member (`net-hold out` for the in-flight kinds), issues each member's verb through
  `Invoke-Step`, then sets `net-hold off` per member in a seeded order with 0–300 ms gaps. Each member acts on a
  stale view, as two players within one round trip do.
- It writes a `contend` marker to `actions.jsonl` (kind, members, targets, release order), so `-Replay` repeats it
  exactly. `Invoke-Quiesce` already releases holds.
- **Settle:** up to 10 s for the members' dumps to agree on the touched sections, then the outcome check.

**Kinds** (from the audit; verbs that exist, or are added in D14):

| Kind | Members do | Expected outcome | Gap |
|---|---|---|---|
| `part-same` | all `part-fast-unmount L k` | one accepted change, the rest rejected; one new item of that part's id | P1 |
| `part-claim` | all `part-claim L k` (after row 18: `lock-try unmount L k finish`) | one holder; after row 18 one grant | P1, P12 |
| `mount-same-item` | `part-twins L`, then each `part-fast-mount` of one item UID into a twin slot | one slot mounted, the item gone once, nothing restored twice | P2 |
| `parent-child` | cap and crankshaft from a fixed table per model (after row 18: `lock-trace relations`) | known gap until row 18; then one denied | P4 |
| `examine-vs-unmount` | A `diag-examine L <tool>`, B `part-fast-unmount L k` (k in A's list) | k unmounted on the server and every client; one item | P5 |
| `gone-inflight` | B `part-fast-unmount L k` with `net-hold out`; A `car-delete L` / `park L` / `job-finish` | B's `parts.transactions` empty; B's inventory digest a match | P7, C3 |
| `details-pair` | A and B `cardetails-fluid`/`-wash`/`-wheel` on two entries of one section | both values on the server and every client | D2 |
| `fluid-vs-part` | A `cardetails-fluid` coolant (after row 18: `lock-try fill`), B unmounts the reservoir | known gap until row 18; then one refused, level 0 with the reservoir off | D1, D4 |
| `lift-vs-work` | A `lift i up`, B `vfx-unscrew L k pause 0.5` on the car on lift i | no ghost or hidden renderer left (`visuals`); after row 18 the lift is refused | L2, L5 |
| `move-vs-work`, `park-vs-work`, `delete-vs-work` | A `car-move`/`park`/`car-delete`, B mid-unmount | known gap until row 18; then refused | L3, C3 |
| `machine-same-item` | A `tool-put TireChanger uid`, B `wheel-mount L uid` (or `sell-item uid`) | the item in exactly one place | I1 |
| `machine-same-slot` | all `tool-put` or all `tool-take` on one machine | one winner, no item lost or duplicated | M1, M2 |
| `item-trades` | all `sell-item uid`, `econ-scrap uid 0` or a warehouse move of one UID | applied once; money or scrap changed once | E3, I2, I5 |
| `lift-same` | all `lift i up` | one step | L1 |
| `place-same` | two `car-move` of different cars to one free place | the same places on every client and the server | L4 |
| `park-stale` | A held, B `part-fast-unmount L k`, A `park L` | after `unpark`, k unmounted; the item once | L8 |

**New rules.**
- **Rule 7, conservation.** For each contended part: items of that id in the inventory = before + (1 if the key is now
  unmounted on the server and was not before, −1 for the reverse). For each contended UID: in at most one of
  inventory, warehouse and machine slots, and in none of them if it was mounted. This catches drift every client
  shares, which rule 1 cannot see.
- **Rule 8, outcome.** The kind's expected outcome. `scenarios/soak-contention-known.txt` lists kinds whose gap is
  still open (kind and gap id). A listed kind is reported as "known gap" and counted, not failed, so the mode can
  land before the fixes; each fix removes its line.

**The checkpoint gains:**
- the dump section `carDetails` in the compared sections;
- a forced digest round that expects `cars:<n>` and `car-details:<n>` for every loader and the D11 keys for every
  client (`Invoke-ForcedDigestCheck` reads the keys the server asked);
- **no silent stalls**, under rule 2: a client whose digest answers "not ready" for the same key at two checkpoints
  in a row fails. Checkpoints are quiesced and the existing rule already demands that no claim is left, so this does
  not need row 18;
- after row 18: `overlapViolations` 0 from the server's `locks` (row 18 D13).

### D14. Harness verbs, dump and scenarios

**Verbs** (owner row 19, registered in INTEGRATION.md):
- Row 4's registered setters that were never built, with row 4's signatures: `cardetails-fluid <loader> <type> <id>
  <level> [cond]`, `cardetails-wheel <loader> <index> <w> <rim> <tire> <et>`, `cardetails-alignment <loader> <FL>
  <FR> <RL> <RR>` (`-` leaves a field), `cardetails-wash <loader> <dust> <wash> [panelIndex]`. Each goes through the
  game setters and then `MarkDirty`, as a player's change does. They replace the audit's `cardetails-set`, so there
  are not two verbs for one thing, and they are the names row 18's `locks-fluid` already uses.
- `cardetails-pour <loader> <type> <id> <seconds>`: adds to the level every frame as `FluidRefillLogic` does
  (`FluidsData.AddFluid(dt × 0.1, …)` on the car's field) and marks the loader dirty; reports `{ start, end,
  decreases }`, where `decreases` counts frames in which the level fell.
- `state-corrupt <car-details|workshop-tools|warehouse|garage> [loader]`: changes local state without a packet, for
  `desync-autofix`.
- `digest-hold <key> notready` (row 14's verb gains a mode): that key answers "not ready".
- `item-where <uid>`: `inventory`, `warehouse`, `machine:<tool>` or `none`.
- `sell-item [uid]`: sells that item (today it sells the first item).

**Dump:** section `carDetails` (per loader, rounded like `Signature`) and `parts.transactions` (open and committed
counts per loader). **Server:** `cars` gains `staleMerged`; `tools` gains `unknownSlotItem` and `removeMissing`;
`desync` lists open stalls; `--check-merges`.

**Scenarios** (each with `# areas:`):

| Scenario | What it checks |
|---|---|
| `car-stale-record` (`parts, cars`) | (1) `net-hold on` on A; B `part-fast-unmount L k`; A `diag-examine L <tool>` whose list contains k; A `net-hold off`. Within 5 s the server has k unmounted, A's and B's `cars` and `inventory` are equal, exactly one item of k's id was added, and a forced `desync check` reports `cars:L` as a match for both. (2) The reverse: B's packets held while A examines and B unmounts. (3) B `vfx-unscrew L k pause 0.5`; A examines; B's `parts.transactions` still shows the open transaction; B `vfx-unscrew L k resume` commits; k unmounted everywhere, one item. (4) After row 18 (task 8.1): B `lock-try unmount L k hold`; A examines; the change is accepted, k stays mounted, B's lock is still held; B `finish`. |
| `details-concurrent` (`details, cars`) | `net-hold on` on both; A `cardetails-fluid` brake, B coolant; release; within 5 s both values on the server (`cardetails L`) and both clients. The same for `cardetails-wash` on two panels, `cardetails-wheel` 0 and 3, `cardetails-alignment` FL and RR. Same entry: A and B set brake to different values; afterwards every side has one of them. Unsent edit: A sets brake, and B's coolant arrives at A within A's 0.5 s flush delay; A's brake still reaches the server. Pour: `net-delay 150` on A, `cardetails-pour L Brake 0 3`: `decreases` is 0, and B's level equals A's at the end. |
| `tools-item-race` (`tools, parts, economy`) | `give-group wheel`; `net-hold on` on both; A `tool-put TireChanger <uid>`, B `wheel-mount L <uid>`; release in both orders (two rounds). Afterwards the group is on the changer or on the car, the same on every side; `item-where` agrees on both clients; `tools` and `inventory` are equal; after `tool-take TireChanger` no group with that id exists twice. Then `tool-put` against `sell-item <uid>` and against `econ-scrap <uid> 0`. After row 18 (task 8.3): B `lock-try mount <key> <uid> hold`, A `tool-put`: refused `Returned`, the item stays in the inventory, B's `finish` commits. |
| `park-stale` (`placement, parts, persistence`) | `net-hold on` on A; B `part-fast-unmount L k`; A `park L`; A off. After `unpark` k is unmounted on every client and the server, the inventory holds that item once, and the dumps are equal. B `cardetails-fluid` coolant before A's park (with A held): after `unpark`, coolant equals B's value. Then park, restart the server, unpark: the same. |

**Updated scenarios:** `desync-autofix` (a `state-corrupt` step per new key; a step in which a mismatch confirms across
a `digest-hold … notready` round), `desync-soak` (the new keys, no false alarm), `soak` (contention mode),
`locks-fluid` (task 8.2), `car-details` (must still pass).

### D15. Spikes

Static facts are in Context. Runtime facts that decide a rule:

1. **Machine items** (decides D9's unknown-UID rule): log in `ToolsStore.Check` whether each put's UID was in the
   server's inventory, removed by the putter, or never seen, and run `tools-slots`, `tools-race`, `tools-latejoin` and
   `tools-car-effects`.
2. **What examine and condition paths change** (confirms D1's groups): `part-state` of the touched keys before and
   after `diag-examine` with each `ToolType`, the welder (`tool-use`) on a body part, `tool-repair` and
   `tool-paint-part` on a part that is then mounted.
3. **Detail values that change by themselves** (decides D11's exclusions): `cardetails-show` every 5 s for 2 min on an
   idle car, with the engine running (`engine on` while seated), and after a test drive; list fields that drift.

## What the server stores and what it only relays

| State | Stored | Relayed |
|---|---|---|
| Part records | the merged record (D1) | the merged record, rebuilt from the store |
| Car details | the merged, complete details (D5) | the incoming entries with their masks |
| Machine slots | as today; on a `Returned` refusal the kept item copy goes back into the inventory | the `Add` of a returned item to the others |
| Removed items | remover, time and a copy for 60 s (runtime only) | — |
| Parked cars | the blob, plus `Parking.Records` (server only, saved) | the blob only, as today |
| Digests | pending mismatches with their first sighting, and stall series (runtime only) | — |

## Late join and return to the garage

- Snapshots carry the stored state, which is already merged; nothing changes in the snapshot path.
- Details: a joiner gets full details; `lastKnown` is filled per entry from that apply. The echo copies are cleared
  with `CarDetailsSync.Reset`.
- Parking: a joiner sees the blobs as today. The records stay on the server; whoever unparks the car later gets them
  applied, and a client that joins after the unpark gets the car through the normal cars snapshot.
- Digests: no answer before the initial sync, as today; the new keys follow the same rule.

## Packets

```
CarBodyPartUpdatePacket   + PartFields Changed            [OptionalField]
CarSubPartUpdatePacket    + PartFields Changed            [OptionalField]
CarDetailsUpdatePacket    + int WheelMask; AlignmentFields AlignmentMask   [OptionalField]
ToolSlotRejectedPacket    + SlotItemOutcome Item          [OptionalField]
[Flags] enum PartFields       { None, Mount, Bolts, Identity, Condition, Quality, Examined, Paint, Dust, Switched }
[Flags] enum AlignmentFields  { None, FL, FR, RL, RR, LampLH, LampLV, LampRH, LampRV }
enum SlotItemOutcome          { Unchanged, Returned, Gone }
```

No packet type is added, so `PacketTypes` is unchanged. Digest keys are strings in the existing digest packets.

## Migration

The protocol hash forces equal client and server builds. `car-placement` goes to v2 (`Parking.Records`); v1 loads
with no records. Nothing else is saved differently. Rollback is reverting the change; a v2 save then fails to load
on the old server, as every section bump does, and the backups of row 7 cover it.

## Risks / Trade-offs

- **A mask misses a real change** (a value moved by less than the epsilon, or a field outside the groups) → the
  server keeps its value, and the car digest shows the difference; the resend repairs it. `--check-merges` and
  spike 2 pin the groups.
- **"In local work" keeps a mount state that the server has already changed** → only until the local commit, whose
  precondition then fails and runs today's rejection path; the digest skips the car while the transaction is open.
- **Per-entry flushes leave a stale entry on the server when a local change is lost before its flush** (the player
  disconnects) → as before; the digest of the other clients sees no mismatch because they share the server's value.
- **Echo copies grow** → bounded per loader (16 sends or 10 s).
- **Parked records and the blob disagree on the hierarchy** (an engine swap the server did not see) → keys that do
  not resolve are dropped and logged, and the unparked car is checked by its car digest.
- **Unknown machine UIDs accepted** → a duplicate stays possible for items the server never saw; spike 1 measures how
  often that happens, and open question 4 can tighten it.
- **New digest keys raise false alarms** → spike 3 and `desync-soak` with the new keys before part 2 merges.
- **Contention runs are flaky** → seeded, replayable, and kinds with an open gap are reported, not failed.
- **Two branches rewrite the same methods as row 18** → the ordering in open question 6.

## Open questions / assumptions

The user's open questions are in proposal.md. Assumptions taken here without the user:

1. The server keeps relaying a detail update to its sender (D7), so clamped values reach it.
2. Removed-item copies and stall series are runtime only; a server restart forgets them.
3. The stall warning threshold is 120 s and the pending-mismatch expiry 60 s; both are constants, not settings.
4. Contention kinds that need row 18 verbs use the row 1 verbs until row 18 merges, and their outcomes stay in the
   known list until then.
