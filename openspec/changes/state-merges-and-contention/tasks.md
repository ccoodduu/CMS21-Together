# Tasks

**Row 19: the race and drift audit's gaps that row 18 does not close** (gaps 3, 6, 9, 10, the rest of 7, and the
soak contention mode).

- **Prerequisites** (on `main`): rows 1, 2, 4, 5a, 5b, 11, 13, 14 and the gap 5 fix (`d1dd908`).
- **Parts** (open question 5): part 1 = groups 1–7 (state merges, L ≈ 7–8 sessions), merged on its own; part 2 =
  groups 9–11 (detection and contention, M ≈ 4–5 sessions). Group 8 holds the row 18 tie-ins and merges with
  whichever part is open when row 18's switch-over lands.
- **Waits for row 18** (`change/part-locks`): tasks 8.1–8.3 wait for its switch-over (its task 5.1); 8.2 also for its
  fluid gates (its task 7.1), 8.3 also for its item step (its task 6.1); 10.4 waits for its task 5.1 (digests keep
  running while locks are held). By open question 6, groups 3 and 4 and task 9.2's car keys start after row 18 has
  merged; everything else can start now.
- **Scenarios:** each carries `# areas: …`. A scenario that proves a fix must fail without it; the commit message
  says so, as the gap 5 fix did.
- **Spikes:** a spike that changes a decision updates design.md in the same commit.
- **Commits:** one conventional commit per finished task group.

## 1. Spikes

- [ ] 1.1 Machine items (design D15.1). Log in `ToolsStore.Check` (debug level) whether each put's UID was in the
      server's inventory, removed by the putter, or never seen. Run `tools-slots`, `tools-race`, `tools-latejoin` and
      `tools-car-effects`. Done when design.md D9 states the rule for never-seen UIDs (accept and count, or refuse)
      with the counts, and the scenarios still pass.
- [ ] 1.2 Examine and condition paths (D15.2). `part-state` of the touched keys before and after `diag-examine` with
      each `ToolType`, `tool-use` with the welder on a body part, `tool-repair` and `tool-paint-part` on a part that is
      then mounted. Done when design.md D1 lists, per path, the field groups it changes, and every changed field
      belongs to exactly one group.
- [ ] 1.3 Detail values that drift by themselves (D15.3). `cardetails-show 0` every 5 s for 2 minutes on an idle car,
      with the engine running (`sit`, `engine on`), and after a `test-drive` round trip. Done when design.md D11 lists
      the fields the `car-details` digest leaves out or rounds coarser, or states that none drift.

## 2. Core

- [ ] 2.1 Core: `PartFields`, `Changed` on both part record packets, `PartRecordMerge.Merge(stored, incoming,
      isMount)` per D1 (the groups of 1.2); `WheelMask` and `AlignmentFields AlignmentMask` on
      `CarDetailsUpdatePacket`; `SlotItemOutcome` on `ToolSlotRejectedPacket`. All `[OptionalField]`. Server flag
      `--check-merges`: a stale examine keeps the stored mount, identity and bolts; a mount record is taken whole; a
      masked condition change keeps the stored mount; `None` is whole; a body record keeps the stored `Switched`;
      wheels merge per masked index; alignment per masked field. Done when the solution builds and
      `CMS21_Together_Server.exe --check-merges` passes.

## 3. Stale part records (gap 3; starts after row 18 has merged, open question 6)

- [ ] 3.1 Client mask: `PartChangeTracker.Send` sets `Changed` per record against `LoaderSync`'s last known record
      (epsilon of `PartRecords.SameState`; `None` without a last record). Done when the client debug log shows
      `Changed = Examined` for a `diag-examine` change and `Mount|Bolts` for `part-unmount`, and `car-live`,
      `car-race` and `car-mount-race` pass.
- [ ] 3.2 Server merge per D1 in `CarPartsHandlers.OnChange` (after `FindConflict` and row 18's lock rule): mount
      records whole, the rest merged; store, relay the stored records, return merged records (body and mechanical) in
      the accepted result; `staleMerged` in the `cars` output. The same merge in `ToolsStore.OnPartChange`. Done when
      `--check-merges` passes and `car-live`, `car-race`, `car-mount-race`, `tools-slots` and `tools-race` pass.
- [ ] 3.3 Receiving side per D2: `PartChanges.OnRemoteChange`/`Apply` abort only on a `Mount`, `Identity` or
      `Switched` difference to `LoaderSync`; `PartApplier.Apply` writes only attribute groups for a key in local work
      (open transaction, or an unsent local flip). Done when step 3 of `car-stale-record` (the scenario of 3.4) passes: B's transaction survives A's examine and B's commit is accepted.
- [ ] 3.4 `scenarios/car-stale-record.ps1` (`# areas: parts, cars`), steps 1–3 of design D14. Done when it passes, and
      it fails on `main` without 3.2 and 3.3 (recorded in the commit message).

## 4. Car details as entries (gap 6; starts after row 18 has merged, open question 6)

- [ ] 4.1 Harness: row 4's setters `cardetails-fluid`, `cardetails-wheel`, `cardetails-alignment` (`-` leaves a field)
      and `cardetails-wash [panelIndex]`, each through the game setters and `MarkDirty`; `cardetails-pour`; the dump
      section `carDetails` (rounded like `Signature`). Register them in INTEGRATION.md (owner 19, row 4's names marked
      as built by 19). Done when each setter changes the local `carDetails` dump and `car-details` passes.
- [ ] 4.2 Client entries per D4: `lastKnown` per entry, `Flush` sends only changed entries with `WheelMask` and
      `AlignmentMask`, remembers only the sent entries; `SendFull` unchanged. Done when one `cardetails-fluid` logs an
      update with one fluid entry, one `cardetails-wash … 3` an update with one panel, and `car-details` and
      `car-wheel-swap` pass.
- [ ] 4.3 Server per D5: `CarDetailsStore.Merge` per masked wheel index and alignment field; relay unchanged. Done when
      `--check-merges` covers it and `car-details-request` passes.
- [ ] 4.4 Apply and remember per entry (D6): `CarDetailsIO.Apply` honours the masks; after an apply only the carried
      entries are remembered. Done when the "unsent edit" step of `details-concurrent` passes (A's brake set within
      its flush delay still reaches the server after B's coolant was applied on A).
- [ ] 4.5 Own echo per `ClientSeq` (D7): keep the sent entries' signatures (16 sends or 10 s per loader), drop echoed
      entries that equal them, apply the rest; clear on `Reset`. Done when the pour step of `details-concurrent`
      passes (`cardetails-pour 0 Brake 0 3` with `net-delay 150` on A reports `decreases` 0).
- [ ] 4.6 `scenarios/details-concurrent.ps1` (`# areas: details, cars`) per D14: fluid pair, panel pair, wheel pair,
      alignment pair, same entry, unsent edit, pour. Done when it passes and fails on `main` (commit message).

## 5. Machine items (gap 9)

- [ ] 5.1 Server per D9: `InventoryChanges` keeps remover, time and a copy of each removed item or group for 60 s (all
      remove paths); `ToolsStore.Check` refuses a put of an item another client removed; never-seen UIDs per 1.1;
      every put refusal carries `Returned` (the server re-adds its copy, clears the remover, relays the `Add`), `Gone`
      or `Unchanged`; `unknownSlotItem` and `removeMissing` in the `tools` output. Done when `tools-race`, `tools-slots`
      and `economy-trades` pass.
- [ ] 5.2 Client per D9: `ToolSync.OnRejected`/`Compensate` follow the outcome (`Returned` re-adds with the hooks off,
      `Gone` drops and shows "<name> used this part.", `Unchanged` as today). Harness `sell-item [uid]` and
      `item-where <uid>`. Done when `tools-race` and `tools-latejoin` pass and `item-where` reports the machine for an
      item on the tire changer.
- [ ] 5.3 `scenarios/tools-item-race.ps1` (`# areas: tools, parts, economy`) per D14 without the row 18 step. Done when
      it passes and fails on `main` (commit message).

## 6. Parking keeps the server's record (gap 10)

- [ ] 6.1 Server per D10: `ParkedRecord` in `PlacementState.Parking.Records` at park (body, mechanical, engine swap,
      valid details); kept through slot swaps, dropped when the car leaves parking; `car-placement` section v2 with a
      v1 migration (no records); never broadcast. Done when `car-parking-full`, `car-placement` and
      `persistence-restart` pass and `Test-ServerSaves.ps1` loads a v1 save.
- [ ] 6.2 Unpark per D10: the record moves to the loader entry; the unparker's first baseline is overlaid with the
      parked records (`parkedRecordsDropped` counted) and the live snapshot goes to every client, the unparker
      included; parked details are stored and sent full, and the unparker's first full details for that `SpawnSeq`
      are dropped; `SpawnerLeft` puts the car back with its record. Done when `car-placement`, `car-placement-reuse`
      and `latejoin-full` pass.
- [ ] 6.3 `scenarios/park-stale.ps1` (`# areas: placement, parts, persistence`) per D14, including the details step and
      the restart step. Done when it passes and fails on `main` (commit message).

## 7. Part 1: verification and docs

- [ ] 7.1 Two-instance verification of part 1:
      - `car-stale-record`, `details-concurrent`, `tools-item-race`, `park-stale`;
      - the `parts`, `cars`, `details`, `tools`, `placement`, `economy`, `persistence` and `visuals` areas;
      - the smoke set (`Run-All -Changed`).

      Done when all are green and their run ids are in STATUS.md.
- [ ] 7.2 Docs: INTEGRATION.md (the packet fields, `PartRecordMerge`, details masks, `SlotItemOutcome`, parked records
      and the `car-placement` v2 section, verbs, dump sections, server counters); the audit's gaps 3, 6, 9 and 10
      marked fixed in `docs/audits/race-and-drift-audit.md`; ROADMAP status. Done when part 1 is merged.

## 8. After row 18's switch-over

- [ ] 8.1 (waits for row 18 task 5.1) "In local work" (D2) also covers keys in this client's own X locks
      (`CarLockMirror`). `car-stale-record` gains step 4 of D14 (an examine during B's lock is accepted, the key stays
      mounted, the lock stays held; B's `finish` commits). Done when the scenario passes with row 18's `locks-race`.
- [ ] 8.2 (waits for row 18 tasks 5.1 and 7.1) Row 18's `FlushNow` uses the per-entry `Flush` (D8), whichever change
      merged second adapting it. `locks-fluid` gains the step: A holds `f:Brake.0` and fills while B holds
      `f:EngineCoolant.0` and fills; after both releases, the server's details and both clients have both levels.
      Done when `locks-fluid` and `details-concurrent` pass.
- [ ] 8.3 (waits for row 18 tasks 5.1 and 6.1) `ToolsStore.Check` refuses a put of a UID in another owner's `CarLocks`
      item lock (`Returned`, row 18's item message). `tools-item-race` gains the lock step of D14. Done when it passes
      and `locks-race` still passes.

## 9. Digests (part 2)

- [ ] 9.1 Core mappers `DigestMappers.Details`, `Tools`, `Warehouse`, `Garage`, `Jobs` per D11, with the exclusions of
      1.3. Done when the solution builds and `--check-merges` gains one case per key that projects the same sample
      from the server's stored types and from the client's DTOs to the same hash.
- [ ] 9.2 Client projections and "not ready" rules per D11 in `ClientDigests` (`car-details:<loader>` after row 18 has
      merged, since row 18 D6 rewrites `ClientDigests.Car`). `digest-hold <key> notready`. Done when `digest-show`
      lists the five keys on both clients and `desync-soak` reports no mismatch for them.
- [ ] 9.3 Server projections and resends per D11 in `ReconciliationService`; `car-details` rides with the car cursor.
      `state-corrupt <car-details|workshop-tools|warehouse|garage> [loader]`. `desync-autofix` gains one corrupt step
      per key: the server confirms, writes a record and resends, and the client matches again. Done when
      `desync-autofix` passes.
- [ ] 9.4 Reconciliation rules per D12: "not ready" keeps a pending mismatch, which expires after 60 s; the forced
      round asks every car and every key. `desync-autofix` gains a step: `inv-corrupt` on B, then `digest-hold
      inventory notready` for one round, then off: the mismatch is confirmed and repaired within three rounds. Done
      when `desync-autofix` and `desync-soak` pass.

## 10. Soak contention (part 2)

- [ ] 10.1 Checkpoint per D13: `carDetails` in `Get-SharedDumpSections`; `Invoke-ForcedDigestCheck` expects every
      loader's `cars` and `car-details` and the D11 keys; the "no silent stalls" check under rule 2; the dump field
      `parts.transactions`. Done when `soak` (10 min, lane 3, no `-Contention`) passes with the new checks.
- [ ] 10.2 `-Contention` and `-ContentionWeight` per D13: the group runner (`net-hold on`/`out` on every member, the
      members' verbs, a seeded release order with 0–300 ms gaps), the `contend` marker, `-Replay`, the settle wait.
      Done when a 5-minute contention run replayed with `-Replay` writes the same `contend` markers (kinds, members,
      targets, release order).
- [ ] 10.3 The kinds of D13, rule 7 (conservation) and rule 8 (outcome), and `scenarios/soak-contention-known.txt`
      with the kinds whose gap is open at that time (each line: kind, gap id, the row that closes it). Done when a
      10-minute lane-3 run with `-Contention` fails no rule except as "known gap", and a run with the gap 9 fix
      reverted fails rule 7 on `machine-same-item`.
- [ ] 10.4 (waits for row 18 task 5.1) Stall warning per D12: per client and key, a warning after 120 s of "not
      ready", listed in the `desync` output and the bug report. `desync-autofix` gains a step with `digest-hold
      inventory notready` for 130 s: one warning, and none after the hold ends. Done when `desync-autofix` passes.

## 11. Part 2: verification and docs

- [ ] 11.1 Two-instance and lane-3 verification of part 2:
      - `desync-autofix`, `desync-soak`, the `resync` area, the smoke set;
      - `soak` 10 minutes on lane 3 with and without `-Contention`, and `Run-All -Lanes 3`.

      Done when all are green and their run ids are in STATUS.md.
- [ ] 11.2 Docs: INTEGRATION.md (digest keys, reconciliation rules, soak rules 7 and 8, the known list, verbs);
      `docs/audits/race-and-drift-audit.md` gap 7 and the contention mode marked done; ROADMAP status; the known list
      trimmed to the gaps still open. Done when part 2 is merged.
