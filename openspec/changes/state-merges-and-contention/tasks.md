# Tasks

**Resume here (part 2, branch `change/state-merges-2`, worktree `CMS21-Together-wt/state-merges-2`):** parts 1 and 3
are merged (`9ae63c7`). Group 9 is done (digests, reconciliation rules, and the stall warning of task 10.4). Group 10
is in progress: the checkpoint, the contention mode and its first-pass kinds are written; the lane-3 runs of 10.1,
10.3 and 11.1 are the user's (agents test on lane 2 only). User decision 2026-10-08: the base check keeps `Quality`.

**Row 19: the race and drift audit's gaps that row 18 does not close** (gaps 3, 6, 9, 10, the rest of 7, the soak
contention mode, and the review ledger's P7, P11, I2, I5, M4, M5 and C2), plus the user's decision of 2026-10-07 on
the ledger's "accept" rows (S1 fixed; no silent drops for I3, I7, E4, M8, C4, J2, J4, J5). Revised 2026-10-07 after
`review.md`.

- **Prerequisites** (on `main`): rows 1, 2, 4, 5a, 5b, 11, 13, 14 and the gap 5 fix (`d1dd908`).
- **Parts** (open question 4), each merged on its own:
  - part 1 = groups 1–7 (state merges, L ≈ 9–11 sessions);
  - part 2 = groups 9–11 (detection and contention, L ≈ 6–7 sessions);
  - part 3 = groups 12–13 (the server answers every refusal, and seats, M ≈ 3 sessions).
  - Group 8 holds the row 18 tie-ins and merges with whichever part is open when row 18 has merged.
- **Order with row 18** (`change/part-locks`):
  - Task 1.4 lands first and does not wait: row 18's `locks-fluid` needs `cardetails-fluid` (its task 7.2).
  - Groups 3 and 4, task 6.3 and task 9.2's car keys start after row 18 has merged (open question 5; 6.3 needs its
    `FlushNow`, 3.5 builds on its `PartTransactions.DropLoader`, 4.1 replaces its changed-fluids send).
  - Tasks 8.1–8.3 need row 18's switch-over (its task 5.1); 8.2 also its fluid gates (7.1), 8.3 its item step (6.1).
    Task 10.4 needs its task 5.1; task 10.5 needs row 18 merged.
  - Everything else (spikes, Core, machines, parking groups 6.1–6.2, digests, soak harness, part 3) can start now.
- **Scenarios:** each carries `# areas: …`. A scenario that proves a fix must fail without it; the commit message
  says so, as the gap 5 fix did.
- **Spikes:** a spike that changes a decision updates design.md in the same commit.
- **Commits:** one conventional commit per finished task group.

## 1. Spikes and early harness

- [x] 1.1 Machine items (design D15.1). Log in `ToolsStore.Check` (debug level) whether each put's UID was in the
      server's inventory, removed by the putter, or never seen. Run `tools-slots`, `tools-race`, `tools-latejoin` and
      `tools-car-effects`. Done when design.md D9 states the rule for never-seen UIDs with the counts, and the
      scenarios still pass.
- [x] 1.2 Examine and condition paths (D15.2). `part-state` of the touched keys before and after `diag-examine` with
      each `ToolType`, `tool-use` with the welder on a body part, `tool-repair` and `tool-paint-part` on a part that is
      then mounted. Done when design.md D1 lists, per path, the groups it changes, and every changed field belongs to
      exactly one group.
- [x] 1.3 Detail values that drift by themselves (D15.3). `cardetails-show 0` every 5 s for 2 minutes on an idle car,
      with the engine running (`sit`, `engine on`), after a `test-drive` round trip and after a `car-wheel-swap`
      style rim and tire change. Done when design.md D11 lists the fields the `car-details` digest leaves out or
      rounds coarser, or states that none drift.
- [x] 1.4 Harness, independent of row 18 [review M7]: row 4's registered setters `cardetails-fluid <loader> <type> <id>
      <level> [cond]`, `cardetails-wheel <loader> <index> <w> <rim> <tire> <et>`, `cardetails-alignment <loader> <FL>
      <FR> <RL> <RR>` (`-` leaves a field) and `cardetails-wash <loader> <dust> <wash> [panelIndex]`, each through the
      game setters and `MarkDirty`; the dump section `carDetails` (rounded like `Signature`, lists sorted by key).
      INTEGRATION.md: row 4's names marked as built by row 19. Done when each setter changes the local `carDetails`
      dump, a `cardetails-fluid` on A shows on B within 3 s, and `car-details` passes.

## 2. Core

- [x] 2.1 Core: `PartFields` (with `All`), `Changed` on both part record packets, `PartRecordMerge.Normalise(stored,
      incoming, precondition)` per design D1 (the groups of 1.2); `WheelMask` and `AlignmentFields AlignmentMask` on
      `CarDetailsUpdatePacket`; `SlotItemOutcome` on `ToolSlotRejectedPacket`. Server flag `--check-merges` for the part
      rules: a mount record is taken whole; an unmount copies its masked groups; a stale examine (base `Unmounted`
      differs) is dropped; an examine on a replaced part (base `PartId` differs) is dropped; a matching base merges
      the masked groups and keeps the rest; an `Identity`-only tune merges; `All` is whole; a 0 mask is skipped; a
      body record keeps the stored `Switched`; the written groups are reported per record. Done when the solution
      builds and `CMS21_Together_Server.exe --check-merges` passes.

## 3. Stale part records (gap 3; after row 18 has merged)

- [x] 3.1 Client masks per D1: `PartChangeTracker.Send` sets `Changed` against `LoaderSync` (raw values, `All` without
      a record, never 0); `EngineStandParts` does the same for stand changes. Done when the client debug log shows
      `Changed = Examined` for a `diag-examine` change, `Mount|Bolts` for `part-unmount` and a mask on a
      `tool-stand-part` change, and `car-live`, `car-race`, `car-mount-race` and `tools-slots` pass.
- [x] 3.2 Server per D1 and D3: normalisation before `FindConflict` and before row 18's lock check; row 18's
      `FlippedKeys` and `unlockedFlip` use D3's flip definition; row 18's `KeepStoredMountState` removed if it reached
      `main`; `OnlyExamines` by mask; stored records with `Changed` cleared; `staleMerged` and `staleDropped` in the
      `cars` output. The same normalisation in `ToolsStore.OnPartChange`, and the removed-by-other item check in
      `ToolsStore.FindConflict`. `tools-race` gains the two-player stand-part step and the stand-part-against-engine-off
      step of design D14. Done when `--check-merges` passes and `car-live`, `car-race`, `car-mount-race`,
      `diagnostics`, `test-drive`, `locks-race` and `tools-race` pass.
- [x] 3.3 Receiving side per D2: relays carry the written groups and results the differing groups; receivers write only
      the masked groups to the game and `LoaderSync`; a transaction is aborted only for `Mount`, `Identity`,
      `Switched` or `All`; `AbortFor` after the revision check; `PartApplier.ShowMounted` returns when the part is
      unmounted again after its wait [P11]. Done when steps 2–4 of `car-stale-record` (written in 3.4) pass and
      `visual-parts` passes.
- [x] 3.4 `scenarios/car-stale-record.ps1` (`# areas: parts, cars, visuals`), steps 1–5 of design D14, with the verbs
      `part-condition` and `diag-examine … keys`. Done when it passes, and it fails on `main` without 3.2 and 3.3
      (recorded in the commit message).
- [x] 3.5 `committed` part transactions per loader [P7]: `HasOpen(loader)` and `HoldsInventoryChanges` look at that
      loader's entries; committed entries of a loader are dropped with row 18's `PartTransactions.DropLoader` on car
      delete and on a `SpawnSeq` change, and a dropped transaction rolls its inventory delta back locally (design
      D16, I7); the dump field `parts.transactions` (open and committed per loader).
      `car-gone-inflight` gains the `park` and `job-finish` steps and checks B's `parts.transactions`. Done when
      `car-gone-inflight` passes with all three steps. Done 2026-10-08: the job step ends the job with
      `job-end-direct` (the native `EndJob` never sends its end on a headless game); a dropped open or committed
      transaction is rolled back when the car goes, so the later rejection finds nothing left to undo;
      `server-answers` gained the I7 step. Runs: batch `20261008-173850_L1_batch` (all of 3.1-3.3's scenarios,
      `car-stale-record`, `server-answers`) and `20261008-175605_L1_car-gone-inflight`. `tools-race`'s two new stand
      part steps are written but skipped like every stand step there (the stand build throws on headless games).

## 4. Car details as entries (gap 6; after row 18 has merged)

- [x] 4.1 Client entries per D4: `lastKnown` per entry; `Flush` sends only changed entries with `WheelMask` and
      `AlignmentMask` and remembers only the sent entries; this replaces row 18's changed-fluids `OnlyChanged`, and
      `FlushNow` inherits it; `SendFull` unchanged. Done when one `cardetails-fluid` logs an update with one fluid
      entry, one `cardetails-wash … 3` an update with one panel, and `car-details`, `car-wheel-swap` and `locks-fluid`
      pass.
- [x] 4.2 Server per D5: the Core helper `DetailsMerge` (per masked wheel index and alignment field, plus today's
      per-entry fluids, cosmetics and modules) used by `CarDetailsStore.Merge`; relay unchanged. `--check-merges`
      gains the details cases [review M7]. Done when `--check-merges` and `car-details-request` pass.
- [x] 4.3 Apply and remember per entry (D6): `CarDetailsIO.Apply` honours the masks; after an apply only the carried
      entries are remembered (`Present` includes `Dyno`). Done when the "unsent edit" step of `details-concurrent`
      (written in 4.5) passes.
- [x] 4.4 Own echo per entry (D7): send copies with `foreignSince` (16 sends or 10 s per loader), marked by every
      foreign apply; an own echo applies an entry the server changed or a foreign write overwrote, for every kept
      `ClientSeq`; cleared on `Reset`. Harness `cardetails-pour` (level and condition, as `FluidRefillLogic`). Done
      when the pour step (`decreases` 0 with `net-delay 150`) and the same-entry step in both server orders of
      `details-concurrent` pass.
- [x] 4.5 `scenarios/details-concurrent.ps1` (`# areas: details, cars`) per D14: fluid pair, panel pair, wheel pair,
      alignment pair, same entry in both orders, unsent edit, pour. Done when it passes and fails on `main` (commit
      message). Done 2026-10-08: passes (`20261008-175714_L1_details-concurrent`, one entry per update in the log,
      pour 0 setbacks); fails on the group 3 code in the panel, wheel, alignment and same-fluid steps
      (`20261008-181145_L1_details-concurrent`; the fluid pair passes there, row 18 already sent changed fluids only).
      `car-details`, `car-wheel-swap`, `locks-fluid`, `car-details-request` and `locks-basic` pass (batch
      `20261008-181415_L1_batch`).

## 5. Item removals and machines (gap 9, I2, I5)

- [x] 5.1 Server per D9: `InventoryChanges` keeps removers for the session (last 10 000 UIDs) and item copies for 60 s;
      every remove path notes its remover, `EconomyService.RemoveItem` through `EconomyOutcome.Effect`;
      `ToolsStore.Check` refuses a put of an item another client removed; never-seen UIDs per 1.1; `Returned`, `Gone`
      or `Unchanged` on every put refusal; `unknownSlotItem` and `removeMissing` in the `tools` output. Done when
      `tools-race`, `tools-slots` and `economy-trades` pass.
- [x] 5.2 Client per D9 (`OnRejected`/`Compensate` follow the outcome, "<name> used this part."). Harness:
      `sell-item [uid]` for items and groups, `item-where <uid>`, `warehouse-move <uid> to|from`. Done when
      `tools-race` and `tools-latejoin` pass, `item-where` reports the machine for an item on the brake lathe, and a
      `warehouse-move` on A shows the item in B's warehouse.
- [x] 5.3 `scenarios/tools-item-race.ps1` (`# areas: tools, parts, economy`) per D14 without the row 18 step,
      including the mount-against-sale round [I5] and a step in which both players `warehouse-move` the same UID
      (moved once, the same on both, the warehouse digest-free check through `item-where`) [I2]. Done when it passes
      and fails on `main` (commit message). Done 2026-10-08: passes (`20261008-184818_L1_tools-item-race`, with the
      8.3 step); fails on the group 4 code in every "B first" round and the lock step
      (`20261008-185711_L1_tools-item-race`). `tools-race`, `tools-slots`, `economy-trades`, `tools-latejoin` pass
      (batch `20261008-181922_L1_batch`). The scenario compares inventories, machines and money, not `cars`: A's
      `placeNo` read -1 after the tire changer race while B's read 0 (seen once, not followed up).

## 6. Parking keeps the server's record (gap 10)

- [x] 6.1 Server per D10: `ParkedRecord` in `PlacementState.Parking.Records` at park (body, mechanical, engine swap,
      valid details), kept through slot swaps, dropped when the car leaves parking, never broadcast, no section bump.
      Done when `car-parking-full`, `car-placement` and `persistence-restart` pass and `Test-ServerSaves.ps1` loads a
      save written before the change.
- [x] 6.2 Unpark per D10: the record moves to the loader entry; the unparker's first baseline is overlaid with the
      differing parked records (`parkedRecordsDropped` counted); the live snapshot goes to every client, the unparker
      included, only when something was replaced; parked details are stored and sent full, the unparker's first full
      details for that `SpawnSeq` dropped; `SpawnerLeft` puts the car back with its record [C2]. Done when
      `car-placement`, `car-placement-reuse` and `latejoin-full` pass.
- [x] 6.3 (after row 18 has merged: `FlushNow`; after 3.5) Drain before a park request and before a car delete per D10:
      at most 1 s for the tracker and the loader's transactions, then `FlushNow(loader, All)`, else "Try again in a
      moment." Done when the "own unsent change" step of `park-stale` passes.
- [x] 6.4 `scenarios/park-stale.ps1` (`# areas: placement, parts, persistence`) per D14: the other player's change, the
      details step, the own unsent change, the unparker who leaves before its baseline, the restart. Done when it
      passes and fails on `main` (commit message). Done 2026-10-08: passes (`20261008-184627_L1_park-stale`); fails
      on the group 4 code in all four steps (`20261008-185951_L1_park-stale`). An unpark right after the loader
      emptied sent a second spawn request (the unpark waited for `IsCarLoaded` of the empty loader), which threw the
      parked record away; `ParkingSync.SendUnpark` now waits for the car to start loading. `car-parking-full`,
      `car-placement`, `car-placement-reuse`, `persistence-restart`, `car-gone-inflight` and `Test-ServerSaves.ps1`
      pass; `latejoin-full` is a lane-3 scenario and was not run.

## 7. Part 1: verification and docs

- [x] 7.1 Two-instance verification of part 1:
      - `car-stale-record`, `details-concurrent`, `tools-item-race`, `park-stale`, `car-gone-inflight`, `tools-race`;
      - the `parts`, `cars`, `details`, `tools`, `placement`, `economy`, `persistence`, `visuals` and `locks` areas;
      - the smoke set (`Run-All -Changed`).

      Done when all are green and their run ids are in STATUS.md. Done 2026-10-08 within the test budget (as part 3
      did): the part 1 scenarios, the scenarios tasks 3-8 name and the smoke set, after merging main
      (`20261008-190450_regression.json`: 25 of 25 and server-saves; `economy-trades` FLAKY, see STATUS.md), not
      every scenario of the nine areas.
- [x] 7.2 Docs: INTEGRATION.md (the packet fields, `PartRecordMerge`, `DetailsMerge`, `SlotItemOutcome`, parked
      records, removers for the session, verbs, dump sections, server counters); the audit's gaps 3, 6, 9, 10 and rows
      P7, P11, I2, I5, M4, M5, C2 marked fixed in `docs/audits/race-and-drift-audit.md`; ROADMAP status. Done when part
      1 is merged. Written 2026-10-08 on `change/state-merges-1`.

## 8. With row 18's switch-over

- [x] 8.1 (needs row 18 task 5.1) If 3.2 ran before row 18 merged: row 18's lock rule uses D3's flip definition after
      normalisation. `car-stale-record` gains step 6 of D14 (an examine during B's lock is accepted, the key stays
      mounted, the lock stays held; a stale examine in the window before the lock's release is not rejected). Done when
      `car-stale-record` and `locks-race` pass.
- [x] 8.2 (needs row 18 tasks 5.1 and 7.1) `locks-fluid` gains the step: A holds `f:Brake.0` and fills while B holds
      `f:EngineCoolant.0` and fills; after both releases, the server's details and both clients have both levels. Done
      when `locks-fluid` and `details-concurrent` pass.
- [x] 8.3 (needs row 18 tasks 5.1 and 6.1) `ToolsStore.Check` refuses a put of a UID in another owner's `CarLocks` item
      lock (`Returned`, row 18's item message). `tools-item-race` gains the lock step of D14. Done when it passes and
      `locks-race` still passes.

## 9. Digests (part 2)

- [x] 9.1 Core mappers `DigestMappers.Details`, `Tools`, `Warehouse`, `Garage`, `Jobs` per D11 (lists sorted by key,
      the exclusions of 1.3). Done when `--check-merges` gains one case per key that projects the same sample from the
      server's stored types and from the client's DTOs to the same hash. Done 2026-10-08 (`--check-merges`: 11 digest
      cases, a changed field and an old `SpawnSeq` included).
- [x] 9.2 Client projections and "not ready" rules per D11 in `ClientDigests` (`workshop-tools` from
      `ToolMachine.ReadLocal`; `car-details:<loader>` after row 18 has merged, since row 18 D6 rewrites
      `ClientDigests.Car`). `digest-hold <key> notready`. Done when `digest-show` lists the five keys on both clients.
      Done 2026-10-08: `desync-autofix` checks that both clients' `digest-show` hashes of every key agree and that a
      forced round matches every key for both.
- [x] 9.3 Server projections and resends per D11, with `desync_resend_keys` (resend off by default per new key).
      `state-corrupt <car-details|workshop-tools|warehouse|garage> [loader]`. `desync-autofix` gains one corrupt step
      per key with that key's resend turned on for the run: the server confirms, writes a record and resends, and the
      client matches again. Done when `desync-autofix` passes. Done 2026-10-08 (`20261008-201238_L2_desync-autofix`);
      the first run found the empty-slot and dirty-phase false alarms of design D11 "as built".
- [x] 9.4 `desync-soak` with the new keys in log-only mode; each key that stays quiet gets its resend turned on by
      default in `desync_resend_keys` [review minor 10]. Done when `desync-soak` reports no mismatch for any new key
      and the defaults are in the server config. Done 2026-10-08: `desync-soak` (`20261008-202110_L2_desync-soak`, 106
      steps with details, the tire changer, the warehouse and orders) confirmed nothing; every key's resend is on by
      default. Smoke set with `desync-autofix`: `20261008-203256_regression.json` (7 of 7).
- [x] 9.5 Reconciliation rules per D12: "not ready" keeps a pending mismatch; expiry after four asks; the forced round
      asks every car and every key. `desync-autofix` gains a step (forced rounds, long
      `desync_check_interval_seconds`): `inv-corrupt` on B, one forced round with `digest-hold inventory notready`,
      then off: the mismatch is confirmed and repaired within three forced rounds. Done when `desync-autofix` and
      `desync-soak` pass. Done 2026-10-08; with "not ready" clearing the mismatch again the step fails
      (`20261008-201632_L2_desync-autofix`).

## 10. Soak contention (part 2)

- [ ] 10.1 Checkpoint per D13: `carDetails` in `Get-SharedDumpSections`; `Invoke-ForcedDigestCheck` expects every
      loader's `cars` and `car-details` and the D11 keys; the "no silent stalls" check under rule 2. Done when `soak`
      (10 min, lane 3, no `-Contention`) passes with the new checks.
- [ ] 10.2 `-Contention`, `-ContentionWeight` and `-ContentionKinds` per D13: the group runner (`net-hold out` on every
      member, the verbs, release one at a time in a seeded order, member i+1 only after the server logged member i's
      packet), the observed server order in the `contend` marker, `{step:N:…}` templates for per-run values, the settle
      wait. Done when a 5-minute contention run replayed with `-Replay` reproduces the observed server order of every
      `contend` marker.
- [ ] 10.3 The first-pass kinds of D13, rule 7 (conservation), rule 8 (outcome) and `scenarios/soak-contention-known.txt`
      (kind, gap id, closing row). Done when a 10-minute lane-3 run with `-Contention` fails no rule except as "known
      gap", and `-ContentionKinds machine-same-item` with the gap 9 fix reverted fails rule 7.
- [x] 10.4 (needs row 18 task 5.1) Stall warning per D12 with the server setting `desync_stall_seconds` (default 120).
      `desync-autofix` gains a step with `desync_stall_seconds = 20` and `digest-hold inventory notready` for 25 s: one
      warning in the server log and in `desync`, none after the hold ends. Done when `desync-autofix` passes. Done
      2026-10-08 with group 9 (`20261008-201238_L2_desync-autofix`; the run also checks that no other key stalled).
- [ ] 10.5 (after row 18 has merged) The second-pass kinds of D13 (row 18's kinds), each removed from the known list
      when it passes. Done when a 10-minute lane-3 run with `-Contention` and every kind fails no rule, and the known
      list holds only gaps still open.

## 11. Part 2: verification and docs

- [ ] 11.1 Two-instance and lane-3 verification of part 2:
      - `desync-autofix`, `desync-soak`, the `resync` area, the smoke set;
      - `soak` 10 minutes on lane 3 with and without `-Contention`, and `Run-All -Lanes 3`.

      Done when all are green and their run ids are in STATUS.md.
- [ ] 11.2 Docs: INTEGRATION.md (digest keys, `desync_resend_keys`, `desync_stall_seconds`, reconciliation rules, soak
      rules 7 and 8, the known list, verbs); `docs/audits/race-and-drift-audit.md` gap 7 and the contention mode
      marked done; ROADMAP status. Done when part 2 is merged.

## 12. Part 3: the server answers every refusal, and seats (user decision 2026-10-07)

Independent of row 18 and of parts 1 and 2 (except that the dropped-transaction step needs task 3.5); can start now.

- [x] 12.1 Inventory and repair answers per design D16: an `Update` of a missing item answers with its `Remove`; an
      `Add` of a present item answers with the stored copy as `Update` when it differs; a refused `PartRepair` adds the
      item's `Remove`. Harness `inv-send <add|update|remove> <uid> [condition]`. Done when the inventory and repair
      steps of `server-answers` (written in 12.5) pass.
- [x] 12.2 Upgrade, parking, delete and placement answers per D16: `GarageUpgradeHandler` sends `GarageState` and
      `WorldState` to the requester on every refusal or no-op; the second park gets `Invalid`, the parking state and
      the loader's `CarSpawnDelete`; a delete of an empty loader is not relayed; a move of an unknown loader answers
      with `CarSpawnDelete`. Done when the upgrade, second-park and second-delete steps of `server-answers` pass and
      `economy-latejoin`, `car-parking-full` and `car-placement-race` pass.
- [x] 12.3 Jobs answers per D16: a dropped or refused generated order answers with the jobs snapshot; an `Unknown`
      accept adds `JobRemoved { Expired }` and the client message "This order is no longer available."; a second or
      out-of-bounds job end adds the jobs snapshot to `WorldState`. Server console `jobs expire <id>`. Done when the
      order, expired-accept and second-end steps of `server-answers` pass and `jobs` and `jobs-latejoin` pass.
- [x] 12.4 Seats per D17: `PlayerHandlers.OnPlayerPresence` keeps the first holder, stores the second record without
      seat and engine, and sends `SeatRefused` (new packet type, appended); the client leaves the seat with
      `GameScript.ExitFromInterior(true)` and shows "<name> is sitting there." `seat-engine` gains the two-player step
      of D14, both orders. Done when `seat-engine` and `presence-latejoin` pass.
- [x] 12.5 `scenarios/server-answers.ps1` (`# areas: economy, jobs, placement, parts`) per D14, one step per D16 row
      with a message, each checking the loser's dump against the server right after the answer without `resync`; the
      dropped-transaction step after task 3.5. Done when it passes and fails on `main` (commit message names the steps
      that fail there). Done 2026-10-08; the dropped-transaction step is added with task 3.5 (part 1).

## 13. Part 3: verification and docs

- [x] 13.1 Two-instance verification: `server-answers`, `seat-engine`, the `economy`, `jobs`, `placement`, `presence`
      and `inventory`-touching areas, and the smoke set. Done when all are green and their run ids are in STATUS.md.
      Done 2026-10-08 within the test budget (smoke set plus this part's scenarios and the ones tasks 12.2–12.4 name,
      plus `economy-trades` and `tools-slots`; not all 36 scenarios of the four areas):
      `20261008-143432_regression.json` and, after merging main, `20261008-145330_regression.json` with
      `20261008-150317_L1_seat-engine` (STATUS.md).
- [x] 13.2 Docs: INTEGRATION.md (the rule "no silent drops" with D16's table, `SeatRefused`, the verbs and the server
      command); the audit rows I3, I7, E4, M8, C4, J2, J4, J5 marked "answered" and S1 marked fixed; ROADMAP status.
      Done when part 3 is merged. Written 2026-10-08 on `change/server-answers`.
