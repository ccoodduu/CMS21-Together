# Race and drift audit

Date: 2026-10-07. Read-only review of `main` at `646355f`, with `change/part-locks` (row 18, `8d9b9bc`: lock packets,
server lock table, spikes, `locks-basic`) and `change/shared-outdoor-scenes` (row 15, `3db3487`) for planned coverage.
No code was changed and no game or test lane was run. File:line references are on `main` unless a branch is named.

## Summary

The mod is safe where the server decides first: money, trades, shop, warehouse, orders, away claims, outdoor loot,
parking slots, machine slots and lift steps. Each has a server check and, mostly, a race scenario. Risk sits in the
optimistic paths, where a client changes its own game first and the server only arbitrates afterwards: car parts,
car details (fluids, wheels, paint, cosmetics), lifts and moves relative to part work, and inventory items consumed
outside a part change.

Main findings:

1. **Car-level actions do not look at part work.** Lift, move, park, delete and job end check only the away claim
   (playtest finding 1). Row 18 closes this.
2. **Fluids have no reservation and travel separately from part changes.** Filling and removing the reservoir race
   (playtest), and the drain reaches others 1–1.5 s after the part change. Row 18 closes this.
3. **A part change carries full records, but only the sender's own mount flips are checked.** An examine, diagnostic
   or condition change made on a stale view writes the old mount state back over another player's unmount or mount.
   The server then disagrees with the actor, and an item can be duplicated. Row 18 does not close this.
4. **Connected parts are not related anywhere.** A claim covers one part and its `unmountWith` group, so the
   crankshaft and its bearing cap, or the crane and an engine part, can be worked on at once. Row 18 closes this.
5. **A part change for a car that is gone gets no answer.** The client keeps the change's inventory delta as
   "committed" forever. Its inventory digest and every car digest then report "not ready" for the rest of the
   session, so the item drift this causes is never detected. Not closed by row 18 (it only makes the situation
   rarer).
6. **Car details are sent as whole sections.** Two players changing different fluids, panels or wheels of one car
   overwrite each other (lost update). Car details are in no digest and in no soak checkpoint, so the loss is never
   detected. Not closed by row 18.
7. **The digests are blind exactly when it matters.** A car is skipped while anyone holds a claim on it, and a "not
   ready" round clears a pending mismatch. Details, machines, warehouse, skills and jobs are not compared at all.

Counts over the 80 scenarios in the area tables below:

| | Scenarios |
|---|---|
| Uncovered by code (**None**) | 15 |
| Only partly covered by code (**Partial**) | 13 |
| No existing scenario that would fail (**none**) | 36 |
| ... of those, with a planned row 18 scenario | 11 |
| Rated High or Medium-High | 8 |

Row 18 (`part-locks`) closes 4 of the top 10 gaps fully (1, 2, 4, 8) and 3 partly (5, 7, 10). Gaps 3, 6 and 9 need
their own fixes. See "Top gaps".

## Method

- Inventory of shared state from the server handlers (`Network/Handlers/*`, `Data/*`), the client hooks and sync
  classes, and the packets. For each, the authority model (server first, optimistic with server arbitration,
  last-write-wins, client-local) and the repair path (claim, precondition, rollback, digest and resend).
- Scenarios: two players on one target; one action invalidating another (parent and child, fluid and part, lift or
  move and work, machine and inventory item, sale or scrap and mount, job end and work); disconnect or late join
  mid-action; packets during a snapshot or reload; game side effects the mod does not hook; polled versus event
  state; values the digests do not compare; idempotence of retries.
- Test coverage counts a scenario only if it would fail when that specific race handling broke. A scenario where
  only one player acts does not count. Planned row 18 scenarios (design D13, tasks 5–11) are listed as "planned".
- Risk = likelihood in a real 2–4 player session (L/M/H) × impact (L/M/H): High, Medium-High, Medium or Low.
- Sources: STATUS.md (2026-10-07 entries), QUESTIONS.md "Playtest findings (2026-10-07)",
  `openspec/changes/part-locks/{proposal,design,tasks}.md`, the scenario headers in `tools/test-env/scenarios`,
  `soak.ps1`, `ScaleSession.psm1` and `HarnessClient.psm1`.

## Authority model in one table

| Shared state | Who decides | Sync path | Repair | In a digest |
|---|---|---|---|---|
| Car part mount state, condition, ids | Client acts, server arbitrates the commit | `CarPartsChange` after 3 stable 0.1 s polls (body parts scanned every 0.5 s) | Precondition reject + rollback; car digest + resend | `cars:<loader>`, one car per round |
| Part reservations | Server table, but advisory: the action runs before the answer | `CarPartClaim`/`CarPartClaimUpdate` | 120 s expiry, release on leave | no |
| Inventory items and groups | Client acts (hooks), server applies by UID | `InventoryItemAction`, inside `CarPartsChange` | Inventory digest + resend | `inventory` (id, condition, quality) |
| Warehouse | Server first | `WarehouseAction` | none needed | no |
| Money, scrap, XP, level | Server (fees after the fact, trades first) | `EconomyRequest`, absolute `WorldState` | World digest | `world` |
| Skills, upgrades, barns | Server first | `UpgradeRequest`, `GarageState` | none | no |
| Car details (fluids, wheels, alignment, tuning, paint, cosmetics, plates, info, dyno) | Last write wins per section | 1 Hz poll + hooks, 0.5 s flush delay, `CarDetailsUpdate` | none | no |
| Machine slots | Server compare-and-set on the held UID | `ToolSlotUpdate` | Loser compensation | no |
| Machine angle, active, tool positions | Last write wins | `ToolSlotProperty`, `ToolPosition` | none | no |
| Lifts | Server compare-and-set on the step | `LifterActionRequest`/`LifterState` | Resend lifter | `car-placement` |
| Car places, parking | Server | `CarPlaceChange*`, `CarPark*`, `CarUnpark*` | Resend | `car-placement` |
| Orders, jobs, payout | Server (payout reported by the finisher, bounded) | `OrderAction`, `JobStarted`, `JobEndRequest` | none | no |
| Away (test track, path, dyno) | Server first | `CarAwayRequest` | none needed | no |
| Outdoor loot, purchases, auction (branch) | Server first | outdoor packets | none needed | outdoor digest (branch) |
| Presence, seat, engine | Each client for itself | `PlayerPresence` | none | no |

## Scenarios per area

Markers: **None** = no mechanism prevents or repairs it; **Partial** = a mechanism exists but leaves a hole;
**none** = no existing scenario would fail if it broke.

### Car parts (body, mechanical, bolts, groups, `unmountWith`)

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| P1 | Two players start on the same part within one round trip. Both pass the local claim check, both games run the action. | **Partial**: the claim is advisory. `PartClaims.AllowAction` sends the claim and lets the action run (`PartClaims.cs:119-125`), and the server never checks that a committer holds the claim. The safety net is the precondition (`CarPartsHandlers.cs:106`) and the loser's rollback (`f9557c0`). | `car-race` (unmount, claim skipped), `car-mount-race`; planned: `locks-race` | Medium |
| P2 | The same inventory item mounted into two slots | `RemovedByOther` (`CarPartsHandlers.cs:113,119`); the rejection restores only items the server still has | `car-mount-race`; planned: `locks-race` | Low |
| P3 | A group the game builds in the chooser (caliper + piston) is mounted while another player uses one of its members | Unknown removed UIDs are ignored, the members' own removals are still checked (`CarPartsHandlers.cs:110-121`, `035887c`, `e26d219`) | **none** (the harness cannot build a chooser group; the fix has no proving scenario); planned: `locks-race` group step (task 6.2) | Medium |
| P4 | Connected parts at once: crankshaft and bearing cap, a part and the part it is fixed to, a part and its parent | **None**. Claims cover the part and its `unmountWith` members only. The game's own blocking is checked on each player's stale local view, and the server has no relation between keys. | **none**; planned: `locks-connected` | High |
| P5 | An attribute-only change made on a stale view (examine, OBD or diagnostic tool, condition or dent change such as the welder) overwrites another player's mount or unmount of the same part | **Fixed (row 19 part 1): part records carry a `Changed` mask; the server merges per field group after a base check and drops a stale record (`car-stale-record`).** Before: **None**. Records are full state, but preconditions are only added for keys whose mount state the sender itself flipped (`PartChangeTracker.cs:139,142`). `FindConflict` (`CarPartsHandlers.cs:96-122`) accepts the stale `Unmounted`. Only `IsExamined` is merged. On the receiving side, `PartChanges.OnRemoteChange` aborts the local open transaction for every key in the change and applies the full record (`PartChanges.cs:46,127`), so a remote examine undoes a local unmount in progress and reverts its item. Row 18 rejects such a flip only while the other player still holds the key exclusively; after its commit the stale record is accepted again (branch `CarPartsHandlers.cs:108-114`). | **none** | High |
| P6 | A remote change lands on a part the local player is still unscrewing (the claim was lost after the action had started) | **Partial**: a toast after the fact; the remote record overwrites the part in the middle of the game's bolt coroutine (`PartChanges.cs:46,127`) | `car-race` (fast path only, no bolts); planned: `locks-race` | Medium |
| P7 | A part change is in flight when another player deletes, parks, sells or job-ends the car (or the loader gets a new `SpawnSeq`) | **Fixed (row 19 part 1): a car going away rolls the loader's open and remote-removed committed transactions back; the server answers every change for a gone car (`car-gone-inflight`: delete, park, job end).** Before: **None**. The server drops the change without any result (`CarPartsHandlers.cs:20`). The client keeps the inventory delta in `committed` forever (`PartTransactions.cs:147`). `HoldsInventoryChanges` and `HasOpen` (`PartTransactions.cs:75,77`) stay true, so the inventory digest and every car digest of that client answer "not ready" until it disconnects (`ClientDigests.cs:83,99`). The unmounted part's item never reaches the server. | **none** | High |
| P8 | A car is deleted, parked, sold or job-ended while a claim on it is held | **None**. `CarClaims.DropLoader` removes the claims without a broadcast (`CarClaims.cs:81`, called at `CarPartsStore.cs:125`). The client mirror is cleared only by `ClientData.Reset`. The next car in that loader shows "X is working on this part" for matching keys, and its car digest is skipped for the session (`ClientDigests.cs:99`). | **none** (the soak's "part claims still held" rule would catch it, but the soak never removes a car with an open part, and its fast verbs skip claims); planned: `locks-car` (row 18 D6 clears the mirror per loader) | Medium |
| P9 | The engine crane takes the engine out (or puts it in) while another player works on an engine part | **Partial**: only the local mirror is asked (`EngineCraneHooks.cs:86-94`); the precondition then rejects the whole crane change and rolls back the engine group, or the engine part change is accepted on an engine that is already out | **none** (`car-crane` has one actor); planned: `locks-connected` (crane-out denied) | Medium |
| P10 | Two players mount the same body panel | **Partial**: precondition only; the body mount is not claim-gated (only `TakeOffCarPart`, `PartClaims.cs:149`) | **none**; planned: row 18 task 5.1 gates the body mount entry, checked through `locks-race` | Low |
| P11 | A remote mount and a remote unmount of the same part arrive within 0.5 s (a race or a rejection restore) | **Fixed (row 19 part 1): `ShowMounted` returns when the part is unmounted again after its wait.** Before: **None**. `PartApplier.ShowMounted` waits 0.5 s and then unblocks neighbours and enables meshes and colliders without checking that the part is still mounted (`PartApplier.cs:92-96`). Visible parts and `blocked` flags go wrong locally; the digest does not compare `blocked`. | **none** | Low |
| P12 | A claim outlives the work: a long bolt job past 120 s, or a player idle in the chooser | **Partial**: a fixed 120 s server expiry (`CarClaims.cs:12`), release on leave and scene change; no renew, no idle cancel | `storm` K5 (holder killed); planned: `locks-leak`, `locks-latency` | Low |
| P13 | A remote mount also mounted the part's `unmountWith` members (game side effect) | Fixed (`91fbdd6`): members follow their own records | `car-live` | Low |
| P14 | TunePart and UpdateWheels rewrite tire and rim ids (game side effect) | Fixed (`c369763`): effective id in digests, ids kept on the wheel apply | `car-wheel-swap` | Low |
| P15 | A remote condition change on a part without a highlighter skipped the look update | Fixed (`1efb71c`, `PartApplier.cs:61-67`) | **none** (visual only) | Low |

### Inventory, warehouse, groups, UIDs

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| I1 | One item is consumed by two paths at once: put on a machine (inventory `Remove` + slot update) while another player mounts it, or sells or scraps it | **Fixed (row 19 part 1): a machine put of an item another player removed is refused (`Gone`, "<name> used this part."); a refused put of the own item is put back by the server (`Returned`) (`tools-item-race`).** Before: **Partial**. If the machine's `Remove` reaches the server first, the mount is rejected (`RemovedByOther`). If the mount comes first, the server drops the `Remove` silently with no answer (`InventoryHandlers.cs:33`), and `ToolsStore.Check` accepts a slot item the server no longer has (`ToolsStore.cs:67-77`). The item is then both on the car and on the machine, and taking it off the machine duplicates it. Tools are in no digest. | **none** (`tools-race` covers machine against machine only) | Medium |
| I2 | Two players move the same item to or from the warehouse | **Fixed (row 19 part 1): checked: moved once, the same on both (`tools-item-race` warehouse step).** Before: Server first; relayed only when the server really moved it (`InventoryHandlers.cs:97,118`) | **none** | Low |
| I3 | Two `Update`s of one item (repair table and part paint), or an update racing a mount | **Answered (row 19 part 3, D16): an update of a gone item gets its `Remove`, an add of a stored item with other values the stored copy.** Before: Last write wins on the whole item (`InventoryHandlers.cs:41-49`); an update of a gone item is ignored | `server-answers` | Low |
| I4 | Live inventory packets during a full inventory sync | Held during the sync and replayed by UID (client `InventoryHandlers.HoldDuringFullSync`) | `latejoin-full` | Low |
| I5 | An item is sold or scrapped (server first) while another player mounts it | **Fixed (row 19 part 1): checked in both orders: mounted or sold, never both (`tools-item-race`).** Before: Sale and scrap note the remover (`ShopHandlers`, `EconomyService.RemoveItem`), so the later part change is rejected and rolled back | **none** (`economy-trades` races sellers, not a seller against a mount) | Low |
| I6 | A reused player slot hands out client UIDs again | **Fixed (row 20): the inventory snapshot carries the highest stored UID of the player's range (inventory, warehouse, machines, items in groups); `UidRange.Apply` continues after it (`race-hardening`).** Before: **Partial**: `UidRange` continues after the highest UID of the slot's range in the local inventory only (`UidRange.cs:20-25`); warehouse and machine items with that range are not scanned, and the server's duplicate check only looks at the inventory | **none** | Low |
| I7 | A part transaction that never commits keeps its inventory change on this client for 10 s | **Answered (row 19 part 3, D16): each flushed packet is answered as I3; the dropped-transaction rollback waits for row 19 task 3.5.** Before: **Partial**: idle flush after 10 s (`PartTransactions.cs:18`); meanwhile the item exists only locally and the inventory digest is skipped | `server-answers` (dropped-transaction step after task 3.5) | Low |

### Shop, sell, scrap, economy, world state

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| E1 | Two fees at once (money changed locally, then by the server) | Server applies each amount and broadcasts the absolute `WorldState` | `economy-fees` (two fees in a race) | Low |
| E2 | Car sale while another player works on the car; two sellers | Refused `Busy`/`Gone` (`EconomyRules.cs:196-237`, claims at `:206`) | `economy-trades` | Low |
| E3 | Scrap, quality upgrade, barn map or crate on an item another player just used | Server first, `Gone`; crates keep a `Looted` flag | `economy-trades` | Low |
| E4 | Two players unlock the same skill or garage upgrade | **Answered (row 19 part 3, D16): a refused or no-op unlock gets `WorldState` and `GarageState`.** Before: Server first and idempotent (`GarageUpgradeHandlers.cs:29,47`); `GarageState` is not digested | `server-answers` | Low |
| E5 | A duplicated `EconomyRequest` (resend or replay) | **Not reachable (row 20, not built): each request is sent once on a reliable connection, nothing retries or resends after a reconnect; only the harness `econ-ledger resend` duplicates (`docs/design/race-hardening.md`).** Before: **Partial**: no `RequestId` deduplication in `EconomyService.Handle`; only crates are protected. The transport is reliable, so only the harness `Resend` produces duplicates today. | `economy-trades` (crate replay only) | Low |
| E6 | Buy, sell-single and sell-by-condition by two players | Server first (`ShopHandlers`), money checked under the lock | `purchases`, `junkyard-trip` (buys) | Low |

### Fluids and car details (wheels, tires, alignment, paint, tint, wash, detailing, welder, plates, tuning)

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| D1 | One player fills a fluid while the other removes its container (coolant reservoir; oil filter or drain plug against the oil bin) | **None**. Fluids are a polled section, last write wins, with no reservation. The drained level reaches the others only with the actor's next poll; the remote part apply (`HideBySavegame`) is not relied on to drain. | **none**; planned: `locks-fluid` | High |
| D2 | Two players change different entries of one section (brake fluid and coolant; paint and wash on different panels; two wheels; FL and RR alignment) | **Fixed (row 19 part 1): details travel as entries with wheel and alignment masks and merge per entry (`details-concurrent`).** Before: **None**. The client always sends the whole section (`CarDetailsIO.cs:49` all fluids, `:126` all panels; `CarDetailsSync.cs:128-144`). The server's per-entry merge (`CarDetailsStore.cs:77`) cannot help, and wheels and alignment are replaced whole (`:107-108`). The later sender reverts the earlier one's values on every client. | **none** (`car-details` has one actor) | Medium-High |
| D3 | The sender's own echo is applied during a continuous change (pouring) and rolls back progress made since the send | **Fixed (row 19 part 1): the own echo is decided per entry; a pour is never set back (`details-concurrent` pour step).** Before: **None**. Only older own echoes are skipped (`CarDetailsSync.cs:148`); the latest is re-applied. Needs a runtime check of how `FluidRefillLogic` accumulates. | **none** | Low |
| D4 | A part change and the fluid drain it causes reach the others in two packets, 0.3 s and 1–1.5 s after the action | **Partial**: both arrive, in no defined order; others see the reservoir gone with coolant still in it for about a second | **none**; planned: `locks-fluid` order check (row 18 `FlushNow`) | Medium |
| D5 | Two players change the same field (paint, tint, wash or detailing of one panel) | Last write wins by design (accepted default; row 18 open question 4: no lock) | `tools-car-effects` (one actor) | Low |
| D6 | A details update arrives while the car is away, still loading, or replaced | The server refuses non-owners while away and resends (`CarDetailsStore.cs:46`); the client waits for Ready and drops on a newer `SpawnSeq` | `test-drive`, `car-details-request` | Low |
| D7 | Tire tuned id drift | Fixed (`c369763`) | `car-wheel-swap` | Low |
| D8 | A full snapshot from one client replaces the stored details (server request, or `SendFull` after the job-take re-baseline) | By design; that client's view wins | `car-details-request` | Low |

### Workshop machines (tire changer, balancer, spring clamp, engine stands, brake lathe, battery charger, repair table, part painting, oil bin, crane)

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| M1 | Two puts or two takes on one machine | Compare-and-set on the held UID (`ToolsStore.cs:69`), loser compensation | `tools-race` | Low |
| M2 | The same item onto two machines | "is on another tool" (`ToolsStore.cs:77`) | `tools-race` | Low |
| M3 | Two players open the balancer minigame | Balancer claim | `tools-race`, `tools-slots` | Low |
| M4 | Two players unmount or mount the same engine-stand part | **Fixed (row 19 part 1): stand part records merge like car parts; two-player step in `tools-race` (skipped on headless games, like every stand step).** Before: Preconditions on stand parts (`ToolsStore.cs:140-146`) and rollback; row 18 keeps the stand optimistic (open question 3) | **none** (`tools-race` races taking the engine off, `tools-slots` has one actor) | Medium |
| M5 | A stand part change while another player takes the engine off the stand | **Fixed (row 19 part 1): stand part changes get the removed-by-other item check; step in `tools-race` (skipped on headless games).** Before: Rejected "engine is not on the tool" (`ToolsStore.cs:121`) | **none** | Low |
| M6 | Tool positions, stand angle, machine "active" by two players | Last write wins | `tools-slots` | Low |
| M7 | The holder of a machine claim disconnects | Released on leave and scene change | `tools-slots`, `storm` K5 | Low |
| M8 | Repair table or part paint on an item another player mounts meanwhile | **Answered (row 19 part 3, D16): the refused repair also gets the item's `Remove`, and the update of the gone item too.** Before: The update of a gone item is ignored; the repair fee is refused when the item is gone (`EconomyRules.PartRepair`) | `server-answers` | Low |
| M9 | The oil bin drains while another player fills oil or works on the oil filter | **None** (fluids, last write wins) | **none** (`tools-car-effects` has one actor); planned: `locks-fluid` (oil bin against oil filter) | Medium |

### Placement, lifts, parking, car moves

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| L1 | Two players press the same lift | Server compare-and-set on the step (`PlacementHandlers.cs:26-32`) | `car-placement-race` | Low |
| L2 | The lift moves while another player works on the car (playtest finding 1: floating ghost, invisible caliper) | **None**. The lift checks only the away claim (`PlacementHandlers.cs:21`). | `visual-lift` (fast mount only; the playtest failure used bolts); planned: `locks-car`, updated `visual-lift` | High |
| L3 | A car is moved or swapped while another player works on it | **None**. The move checks away and raised lifts only (`PlacementHandlers.cs:38-80`). | **none**; planned: `locks-car` | Medium-High |
| L4 | Two players move two cars to the same free place | **Fixed (`fix/place-same`): the target place is part of the move lock, so the second move is refused before its game moves anything ("<name> is moving a car there."); a move the server still refuses after the grant sends the refused game every car place and lift state, applied once its own move has ended.** Before: the second lock was granted, the server refused the move, and the mover's game kept its swap | `car-place-same`; soak kind `place-same` | Low |
| L5 | The window in which remote clients still animate a lift step the server already accepted | **None**. Local part work is allowed meanwhile. | `visual-lift`; planned: `locks-car` (`net-delay 150` step, row 18 M3) | Medium |
| L6 | A car deleted from a lift, the loader reused | Fixed (`f136eb9`) | `car-placement-reuse` | Low |
| L7 | Two parks into the last slot; two unparks of one car; a parking swap | Server (`ParkingService.TryAdd`, unpark checks) | `car-parking-full`, `car-placement-race` | Low |
| L8 | Parking stores the parker's local car blob (`ParkingHandlers.cs:39`), including state the parker has not received yet (a part change in flight, details polled at 1 Hz) | **Fixed (row 19 part 1): the server keeps its own part records and details with the parked car and overlays them on the unpark; the park waits up to 1 s for the parker's own change (`park-stale`).** Before: **None**. The parked car loses the other player's change, and that change's inventory side stays (item duplicated). | **none** | Medium |

### Car spawn, buy, sell, delete

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| C1 | A spawn request for a loader that already holds a car | **Fixed (row 20): refused with `CarSpawnRejected` ("Another car is already in that place."), the stored car stays and its snapshot goes to the refused client, which keeps the winner's car (`race-hardening`). Reached only by the plain spawn path (F6 developer spawn, harness).** Before: **Partial**: job and unpark paths check the loader; `RegisterSpawn` clears the existing car silently (`CarPartsStore.cs:45`) | `jobs-latejoin` (job path only) | Low |
| C2 | The spawner or unparker leaves before the baseline | **Fixed (row 19 part 1): the car goes back with the server's parked record (`park-stale`).** Before: Loader cleared, a parked car goes back to its slot (`PlacementRules.OnLoaderCleared`) | **none** | Low |
| C3 | Park, delete or job end while another player works on the car | **None**. Each checks only the away claim (`ParkingHandlers.cs:37`, server `CarHandlers.cs:65`, `JobsService.cs:204`). The worker's car disappears mid-action; see P7 and P8 for the follow-on drift. | **none**; planned: `locks-car` (park, delete, job end refused) | High |
| C4 | Two players delete, or park, the same car | **Answered (row 19 part 3, D16): the second park gets the parking state and the loader's delete; the second delete is not relayed.** Before: The second is harmless (`ClearLoader` returns false; the park is refused) | `server-answers` | Low |
| C5 | A second baseline replaces all records once one exists: any client may send it (`CarPartsHandlers.cs:160-166`), and the job take re-uploads after 0.5 s (`JobsSync.cs:151`) | **Not reachable (row 20, not built): the job taker's second baseline leaves 86–280 ms after the first (10 job runs), and another player can act only after the car is Ready on their client plus a lock round trip (`docs/design/race-hardening.md`).** Before: **Partial**: a change by another player in that short window is overwritten | **none** | Low |
| C6 | Delete while a player sits in the car with the engine running | `EnsureNotSeatedIn` before the delete | `seat-engine` | Low |

### Jobs, orders, payout

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| J1 | Two players accept the same order | Server; one approved, a second take refused `Busy` (`JobsService.cs:113-122`) | `jobs` | Low |
| J2 | The generator changes while an order is being generated | **Answered (row 19 part 3, D16): a dropped order gets the jobs snapshot.** Before: Orders from a non-generator are dropped (`JobsService.cs:82`) | `server-answers` | Low |
| J3 | The taker disconnects or stalls mid-take | Claim released on leave, 60 s timeout, car deleted | `jobs-latejoin` | Low |
| J4 | An order expires while it is being accepted | **Answered (row 19 part 3, D16): the refusal plus `JobRemoved { Expired }` and "This order is no longer available."**. Before: The server tick removes it; the accept gets `Unknown` | `server-answers` (server `jobs expire <id>`) | Low |
| J5 | Two players end the same job | **Answered (row 19 part 3, D16): `WorldState` plus the jobs snapshot.** Before: The second is ignored and gets the world state | `server-answers` | Low |

### Test drive, dyno, test path (away)

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| A1 | Away request while another player works on the car | `InUse` from the claims (`CarAwayRegistry.cs:58`) | `test-drive` | Low |
| A2 | Part, details, lift, move or delete while the car is away | `CarAwayRegistry.Blocks` on every path | `test-drive`, `diagnostics` | Low |
| A3 | The owner leaves or crashes while away | Released on leave or scene change, 60 s return grace | `test-drive-latejoin`, `storm` K5 | Low |

### Outdoor scenes (row 15, on `change/shared-outdoor-scenes`)

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| O1 | Two players take the same loot item | Server first (`LootService.Take`) | `outdoor-junkyard`, `outdoor-scale` | Low |
| O2 | Two players buy the same junkyard car | Server, `Taken` | `outdoor-junkyard` | Low |
| O3 | A holder leaves or crashes with items in hand | Items return after the grace | `outdoor-junkyard`, `outdoor-scale` | Low |
| O4 | Two players bid on one auction lot | One player runs the bidding; raises are forwarded | `outdoor-auction` | Low |

### Presence, seat, engine

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| S1 | Two players sit in the same seat | **Fixed (row 19 part 3, D17): the server keeps the first seat holder and sends `SeatRefused` to the second, who leaves the seat with "<name> is sitting there."**. Before: **None**: seats are presence data, not arbitrated (`PlayerHandlers.cs:42`) | `seat-engine` (seat race, both orders) | Low |
| S2 | The car is deleted while a player sits in it | `EnsureNotSeatedIn` | `seat-engine` | Low |

### Session (join, late join, rejoin, disconnect, server restart, autosave, F7)

| # | Scenario | Code coverage | Test coverage | Risk |
|---|---|---|---|---|
| X1 | Late join while another player works (claims, ghosts, away) | Claims and away claims in the snapshot (`SendActive`) | `visual-latejoin`, `latejoin` | Low |
| X2 | Live packets during the join snapshot | Ordered stream; `ClientScene.GarageBound` queue (`ClientScene.cs:20-33`); inventory packets held | `latejoin`, `latejoin-full` | Low |
| X3 | Packets during a garage reload (F7, return from outdoor) | Dropped while the garage is not ready, then a full snapshot; the WorldState error during a reload is fixed (`c81fd05`) | `resync-key`, `scenes` | Low |
| X4 | F7 while holding a claim or with an own change in flight | **Partial**: the local state is reset, but the server keeps the claim until commit or the 120 s expiry, so others stay blocked | **none** (`resync-key` holds no claim); planned: row 18 D8 releases own locks before the reload (no scenario named) | Low |
| X5 | Disconnect mid-action (claim, balancer, away, order take) | Released on leave | `storm` K5, `tools-slots`, `jobs-latejoin` | Low |
| X6 | Server restart or kill mid-activity | In-flight changes are lost; clients go to the menu and rejoin the saved state | `server-restart`, `persistence-restart`, `storm` K6–K8 | Low |
| X7 | Autosave during activity | Saved under `StateLock`, a consistent snapshot | `soak` (autosave every 30 s) | Low |

## Digests and desync detection

### What is compared

`DigestMappers` (`CMS21-Together-Core/Data/Digest/DigestMappers.cs`), the same projection on both sides:

| Key | Fields | Not compared |
|---|---|---|
| `world` (`:16`) | money, scrap, level, exp | barns, skills, upgrades (`GarageState`), story missions |
| `inventory` (`:22`) | per item: id, condition, quality; per group: id, size, member `id@condition` | warehouse (`null` on both sides, `ClientDigests.cs:91`, `ReconciliationService.cs:210`), paint and `IsPainted`, tuned id, plate data, wheel data, group member UIDs |
| `cars:<loader>` (`:56`) | body: unmounted, switched, tunedId, condition, dent, quality; mechanical: unmounted, effective id, condition, quality, examined | all car details (fluids, wheels, alignment, tuning, paint, cosmetics with tint, dust and wash, plates, mileage, dyno), sub-part dust, mount-object data (bolt and stuck state), `blocked` flags |
| `car-placement` (`:74`) | lift states, car place per loader, `carToLoad` per parking slot, parking levels | the parked cars' contents, tool positions |
| not a key | | machine slots and properties, claims, away claims, orders and active jobs, presence |

### When a comparison is skipped

- The client answers nothing before the initial sync, outside the garage, or during a snapshot (`ClientDigests.cs:21`).
  The server asks only clients whose presence is in the garage (`ReconciliationService.cs:57`).
- `inventory` is "not ready" while any part transaction holds inventory changes or any change is unconfirmed
  (`ClientDigests.cs:83`). Combined with P7, this can last for the whole session.
- `cars:<loader>` is "not ready" while that car has queued packets, a pending local change, an open transaction or
  *any* unconfirmed change on any car, or while *anyone* holds a claim on it (`ClientDigests.cs:98-99`). During
  shared work this is nearly always the case (playtest finding 6). Combined with P8, it can last for the session.
- `car-placement` is "not ready" while any lift moves or any car is not Ready (`ClientDigests.cs:116,124`).
- Only one car per client per 5 s round is asked, round robin (`ReconciliationService.cs:64`). A mismatch must repeat
  with equal hashes in the next round for that car (`:104`), so with four cars a confirmation takes about 40 s of
  quiet. A "not ready" answer clears the pending mismatch (`:94`), so a busy car never confirms.
- After a resend, the same key is not resent again for 60 s; a second confirmation in that window marks it
  persistent and asks the player to press F7.

### Judgement

The digest is sound for world, inventory and placement in a quiet session, and its two-round confirmation avoids
false alarms (`desync-soak`). It is weakest exactly where the races are: busy cars, and every value carried by car
details. The soak does not fill the gap. Its checkpoint compares clients with each other on
`stats, inventory, cars, placement, jobs, tools, toolPositions` (`HarnessClient.psm1:83`), not car details, and its
forced digest round asks only `world`, `inventory` and `car-placement` (`ScaleSession.psm1:151`). A drift that every
client shares (L8, I1, D2 after the relay) is invisible to the clients' comparison, and car digests are only checked
incidentally.

## Top gaps

Ranked by risk and by whether anything is in flight for them. "Row 18" says what the part-locks change does about it.

### 1. Car-level actions while another player works (L2, L3, L5, C3) — High

The lift, a move or swap, parking, delete and job end check only the away claim. This caused playtest finding 1, and
P7 and P8 follow from it.

- **Fix:** row 18 D7 and M4. A `car` key taken exclusively by lift and move and shared by every part and fluid lock;
  park, delete and job end refused `Busy` while another player holds a lock; a local refusal while a lift or move of
  that car is still animating on this client. Until it merges, a stopgap is to make `OnLifterAction`,
  `OnCarPlaceChange`, `ParkFromGarage`, `HandleCarSpawnDelete` and `OnJobEnd` refuse while
  `CarClaims.Held(loader)` has another owner. The sale already does this (`EconomyRules.cs:206`).
- **Test:** planned `locks-car` (B holds a part: A's lift, move, park, delete and job-finish are refused and nothing
  moves; A's lift granted with `net-delay 150` on B: B's unmount is refused until B's lift stops). Also the updated
  `visual-lift`: B on the bolt path with `vfx-unscrew` hold while A presses the lift. The lift must be refused, and
  no ghost or hidden renderer may remain.
- **Row 18:** closes it.

### 2. Fluid against part, and the drain order (D1, D4, M9) — High

- **Fix:** row 18 D4. The `f:<type>.<id>` keys, taken shared by parts that contain, gate or drain a fluid and
  exclusively by refill, extractor and oil bin; `FlushNow` before the release and before a draining part change.
  One correction for row 18: `FlushNow` reuses `Flush`, which sends the whole `Fluids` section, so a flush after a
  brake fill can overwrite a coolant fill another player is doing under its own lock. Flush only the locked fluid's
  entry (this is the fix of gap 6, applied to fluids).
- **Test:** planned `locks-fluid`, with one added step: A holds `f:Brake.0` and fills while B holds
  `f:EngineCoolant.0` and fills; after both releases, the server's details have both new levels.
- **Row 18:** closes D1 and D4, and M9 through the oil-bin lock. The concurrent fill of two fluids stays open until
  the flush sends only the entry.

### 3. Attribute-only part changes carry a stale mount state (P5) — High

**Fixed by row 19 part 1 (`state-merges-and-contention`, gap 3).**

An examine, diagnostic tool, welder or condition change sends full records. Keys whose mount state the sender did not
flip get no precondition. If another player's unmount is committed but not yet applied on the sender, the server
stores the part as mounted again, relays that to the actor (whose item stays: duplicate), and the actor's game
disagrees with the server. On the receiving side, the remote examine aborts the local open transaction for that key
and re-mounts the part under the player's hands.

- **Fix (server):** in `CarPartsHandlers.OnChange`, for every record without a precondition, keep the stored
  `Unmounted` (and `Switched` for body parts), then store and relay the merged record. Send merged records back to
  the sender in the accepted result, as the `IsExamined` merge already does (`merged`). Count it as `staleMountMerged`
  in the server log.
- **Fix (client):** `PartChanges.OnRemoteChange` aborts local transactions only for keys whose `Unmounted` differs
  between the remote record and the local `LoaderSync` record. `PartApplier.Apply` leaves the mount state alone for
  a key with an open local transaction and lets the commit's precondition decide.
- **Test:** new scenario `car-stale-record` (`# areas: parts, cars`). A and B see one car. `net-hold on` on A.
  B runs `part-fast-unmount <loader> <key>`. A runs `diag-examine <loader> <ToolType>` with a tool whose part list
  includes that key (or the harness `Examine(true)` on that part). `net-hold off` on A. Within 5 s: the server's part
  state of the key is unmounted, the A and B `cars` and `inventory` dumps are equal, exactly one item of that part's id
  was added, and a forced `desync check` reports `cars:<loader>` as a match for both. Then the reverse order: A
  examines while B unmounts with B's packets held.
- **Row 18:** narrows it to the time after the lock holder's commit (branch `CarPartsHandlers.cs:108-114` rejects the
  flip only while the key is held exclusively). Examine is not gated (design: "examine and diagnostic tools (read
  only)"), so the server merge is still needed.

### 4. Connected parts (P4, P9) — High

- **Fix:** row 18 D3. Exclusive keys for the work group; shared keys for ancestors (by path segment), blocking
  neighbours in both directions from `unblockOnUnmount`, held fluids, and `car`. The crane takes the engine root
  exclusively.
- **Test:** planned `locks-connected` (cap held → crankshaft denied; sibling cap granted; crankshaft held → cap
  denied; engine part held → `crane-out` denied; `lock_scope = part` grants the crankshaft).
- **Row 18:** closes it.

### 5. A part change for a car that is gone gets no answer (P7) — High

**Fixed by row 19 part 1 (`state-merges-and-contention`, P7 (rest)).**

The server drops a `CarPartsChange` with no result when the loader is empty, has another `SpawnSeq` or has no
baseline. The client's `committed` entry never clears. Inventory digests and all car digests of that client stop for
the session, and the item from the unmount exists only on that client.

- **Fix:** the server always answers. A dropped change gets
  `CarPartsChangeResult { Accepted = false, Reason = "car gone", RestoreUids = StillHeld(delta) }`, so the existing
  rollback runs. The client keeps the loader with each `committed` entry, clears entries and open transactions of a
  loader on `CarSpawnDelete` and on a `SpawnSeq` change, and makes `HasOpen(loader)` look only at that loader. As a
  backstop, drop `committed` entries older than 30 s with a warning.
- **Test:** new scenario `car-gone-inflight` (`# areas: parts, resync`). A and B see one car. `net-hold out` on B (its
  outgoing packets are held too). B runs `part-fast-unmount <loader> <key>`. A runs `car-delete <loader>`.
  `net-hold off` on B. Then: B's dump shows no open or committed part transaction (new field
  `parts.transactions` in the dump), A's and B's inventories are equal, and a forced `desync check` reports
  `inventory` as a match, not "not ready", for B. Repeat with `park` and `job-finish` instead of `car-delete`.
- **Row 18:** narrows it, because park, delete and job end are refused while the worker holds a lock, but a change in
  flight right after the release, or for a replaced `SpawnSeq`, is still dropped silently.

### 6. Car details are whole sections (D2, D3) — Medium-High

**Fixed by row 19 part 1 (`state-merges-and-contention`, gap 6).**

- **Fix:** send entries, not sections. Keep `lastKnown` per entry (fluid `type.id`, body panel index, wheel index,
  tuning module key) and send only the changed entries in `CarDetailsSync.Flush`. On the server, merge wheels and
  alignment per index or field as fluids and cosmetics already are. Skip the own echo unless the server changed the
  value (keep the sent copy per `ClientSeq` and compare), so a pour in progress is never rolled back.
- **Test:** new harness verb `cardetails-set <loader> fluid <type> <id> <level>|cosmetic <index> dust <value>|wheel
  <index> …` and a new scenario `details-concurrent` (`# areas: details, cars`). `net-hold on` on both, A sets the
  brake fluid and B the coolant (then: A dust on the hood, B dust on a door), `net-hold off` on both. Within 5 s,
  both new values are on the server (`cardetails <loader>`) and on both clients.
- **Row 18:** does not close it. The fluid locks keep two writers of one fluid apart, not two fluids of one car.

### 7. Digest blind spots and skip rules (finding 6, P7, P8, D2, I1, L8) — Medium-High

**Fixed by row 19 part 2 (`state-merges-and-contention`, group 9):** digest keys `car-details:<loader>`,
`workshop-tools`, `warehouse`, `garage` (skills, garage upgrades, barns) and `jobs`; "not ready" keeps a pending
mismatch (expiry after four asks); the forced check asks every car and key; a stall warning
(`desync_stall_seconds`); the soak checkpoint compares `carDetails`. Proof: `desync-autofix`, `desync-soak`.

- **Fix:**
  - Change the car skip rule to "this client holds a claim or has an unconfirmed change on this car" (row 18 D6).
  - Keep a pending mismatch across "not ready" rounds instead of clearing it (`ReconciliationService.cs:94`).
  - Ask every car in the forced check.
  - Add digest keys `details:<loader>` (rounded like `CarDetailsSync.Signature`), `tools` (slot UIDs, balanced,
    mounting) and `warehouse`; add barns and skills to `world`.
  - In the soak, compare a new `carDetails` dump section in the checkpoint.
- **Test:** `desync-autofix` gains `details-corrupt` and `tool-corrupt` steps (new harness verbs that change B's
  local state without a packet; the server confirms, writes a record and resends). `desync-soak` keeps proving there
  are no false alarms, with the new keys.
- **Row 18:** closes the skip rule (D6) only.

### 8. Stale claim mirror after the car is removed (P8) — Medium

- **Fix:** row 18 D6 resets the mirror per loader on car delete and on a `SpawnSeq` change, and D5 releases on
  `LoaderCleared` with a broadcast. If row 18 slips, two lines do it: `CarClaims.DropLoader` broadcasts the release,
  and the client's `CarSpawnDelete` handler clears that loader's claims.
- **Test:** planned `locks-car` with one added check: after `car-delete` of a car on which B holds a lock, A's and B's
  mirrors have no record for that loader, and a new car spawned into it is digest-ready on both.
- **Row 18:** closes it.

### 9. An item consumed by a machine and by a mount (or sale) at once (I1) — Medium

**Fixed by row 19 part 1 (`state-merges-and-contention`, gap 9).**

- **Fix:** `ToolsStore.Check` refuses a slot item or group the server's inventory no longer holds, unless the same
  client removed it in the last few seconds (`InventoryChanges.removedBy` already records this). The client's
  existing loser compensation then puts the item back. Better still, make the machine put one transaction that
  carries its inventory removal, as `CarPartsChange` does. Reply to a `Remove` of a missing UID with a resend of that
  entry, so the sender learns it was gone.
- **Test:** new scenario `tools-item-race` (`# areas: tools, parts`). `give-group wheel`. `net-hold on` on both.
  A runs `tool-put TireChanger <uid>` and B runs `wheel-mount <loader> <uid>`. `net-hold off` on both. Then the wheel
  is either on the changer or on the car on both clients and the server, `tools` and `inventory` are equal, and after
  `tool-take TireChanger` no wheel group with that id exists twice. A second round races the put against `sell-item`.
- **Row 18:** does not close it (design: "item locks do not cover machines, sale and scrap").

### 10. Parking stores the parker's local car blob (L8) — Medium

**Fixed by row 19 part 1 (`state-merges-and-contention`, gap 10).**

- **Fix:** the park request carries the parker's `Revision` of that car and its details `ClientSeq`. The server
  refuses the park (`Busy`, try again) if the car's server revision is newer, or if another client's details update
  for the car arrived within the last 2 s. The client retries once after the next applied change. The same check
  fits the car sale, which also trusts the client's price.
- **Test:** new scenario `park-stale` (`# areas: placement, parts`). `net-hold on` on A. B runs `part-fast-unmount
  <loader> <key>`. A runs `park <loader>`. `net-hold off` on A. Then the park is refused, or after `unpark` the
  car has the key unmounted. Either way the inventory holds that part's item exactly once.
- **Row 18:** narrows it (park is refused while a lock is held), but a committed change still in flight to the parker,
  and polled details, are not covered.

### Further gaps (below the top 10)

- P11 `ShowMounted` does not recheck the mount state after its 0.5 s wait: recheck `script.IsUnmounted` and skip
  when it is unmounted. Test: release two held remote changes (mount, then unmount) in one frame with
  `part-hold-remote`; the dump's `blocked` flags and renderers must equal the actor's.
- M4 engine-stand parts stay optimistic (row 18 open question 3): add a two-player stand-part race to `tools-race`.
- X4 F7 with a claim held: release own claims (row 18: locks) before the reload; add the step to `resync-key`.
- C1 `RegisterSpawn` silently clears an occupied loader: refuse it instead (`CarSpawnRejected`). **Done in row 20.**
- E5 no `RequestId` deduplication for fees: keep the last 64 request ids per client. **Row 20: not reachable, not built.**
- I6 UID ranges: scan the warehouse and machine slots too, or let the server hand out UID blocks. **Done in row 20 (server floor per range).**

### Notes for row 18 while it is being built

- `FlushNow` should send only the locked fluid's entry (gap 2). Otherwise it adds a new whole-section write at every
  release.
- The commit check rejects a flip of another player's exclusive key only while the lock is held. Stale attribute
  records after the release need the server merge of gap 3.
- The client's lock mirror reset on car delete and `SpawnSeq` change (D6) should also clear part transactions and
  `committed` entries of that loader (gap 5).

## Soak contention mode

**Built by row 19 part 2 (`state-merges-and-contention` D13, `tools/test-env/SoakContention.ps1`):** the first-pass
kinds, rules 8 (conservation) and 9 (outcome) — the soak's rule 7 is memory —, `soak-contention-known.txt`, and a
replay that reports whether the server order was reproduced. Row 18's kinds (task 10.5) are not built yet.

Today `soak.ps1` avoids contention on purpose:

- One action per loop with 2–5 s pauses (`soak.ps1:517-518`).
- Keys in use are skipped (`:160-161`), and cars with open parts are not deleted, parked or used for presence.
- Parts move only through `part-fast-unmount` and `part-fast-mount` (`:163`, `:176`), which skip claims and bolts.
- Details change only through `cardetails-randomize` on one client, and there are no fluid actions.
- Network faults hit one actor at a time.

A `-Contention` switch adds deliberate contention without changing the checkpoint rules.

### How a contention group runs

- A new catalogue row `contention` (weight `-ContentionWeight`, default 15 when the switch is on) picks a seeded
  *kind* and 2–4 actors (two on lane 1, up to four on lane 3).
- **Determinism:** like the race scenarios, the group sets `net-hold on` on every member, issues each member's verb
  (each acts on a stale view, as two players do within one round trip), then sets `net-hold off` in a seeded order
  with 0–300 ms gaps. A `net-hold out` variant (outgoing held too) covers the "change in flight" kinds.
- **Logging:** each verb goes through `Invoke-Step`. The group writes a `contend` marker to `actions.jsonl` with its
  kind, members, targets and release order, so `-Replay` repeats it exactly. `Invoke-Quiesce` (`:376`) already
  releases holds.
- **Settle:** after the release, the group waits up to 10 s for the members' dumps to agree on the touched sections.
  It then runs its own outcome check (below) before the loop goes on.

### Kinds

| Kind | Members do | Expected outcome (checked right after the group) | Gap |
|---|---|---|---|
| `part-same` | all `part-fast-unmount L k` on one key | one accepted change, the rest rejected; one new item of that part's id | P1 |
| `part-claim` | all `part-claim L k` (after row 18: `lock-try unmount L k finish`) | one holder; after row 18, one grant and the rest denied | P1, P12 |
| `mount-same-item` | `part-twins L`, then each `part-fast-mount` of the same item UID into a twin slot | one slot mounted, the item gone once, nothing restored twice | P2 |
| `parent-child` | cap and crankshaft keys from `lock-trace relations` (row 18) or a fixed table per model | before row 18: recorded as a known gap; after: one denied | P4 |
| `examine-vs-unmount` | A `diag-examine L <tool>`, B `part-fast-unmount L k` (k in A's list) | the key unmounted on the server and both clients; one item | P5 |
| `gone-inflight` | B `part-fast-unmount L k` with `net-hold out`, A `car-delete L` / `park L` / `job-finish` | B's transactions empty; B's inventory digest a match | P7, C3 |
| `details-pair` | A and B `cardetails-set` on two different entries of one section | both values on the server and both clients | D2 |
| `fluid-vs-part` | A `cardetails-set` coolant (after row 18: `lock-try fill`), B unmounts the reservoir | before row 18: recorded as a known gap; after: one refused, and the level is 0 when the reservoir is off | D1, D4 |
| `lift-vs-work` | A `lift i up`, B `vfx-unscrew` hold or `part-unmount` on the car on lift i | no ghost or hidden renderer left (`visuals` dump); after row 18: the lift is refused | L2, L5 |
| `move-vs-work`, `park-vs-work`, `delete-vs-work` | A `car-move` / `park` / `car-delete`, B mid-unmount | after row 18: refused; before: recorded as a known gap | L3, C3 |
| `machine-same-item` | A `tool-put TireChanger uid`, B `wheel-mount L uid` (or `sell-item uid`) | the item in exactly one place | I1 |
| `machine-same-slot` | all `tool-put` or `tool-take` on one machine | one winner, no item lost or duplicated | M1, M2 |
| `item-trades` | all `sell-item uid`, `econ-scrap uid` or a warehouse move of one UID | applied once; money or scrap changed once | E3, I2, I5 |
| `lift-same` | all `lift i up` | one step | L1 |
| `place-same` | two `car-move` of different cars to one free place | consistent places on every client and the server | L4 |

### Checks that fit the existing rules

The existing rules stay: (1) checkpoint equality, (2) confirmed desyncs, (3) unexpected disconnects, (4) log errors,
(5) storms, (6) watchdog. Contention adds:

- **Rule 7, conservation.** The group snapshots the touched part keys and item ids or UIDs before it starts.
  Afterwards, for each contended part: items of that id in the inventory = before + (the key is now unmounted on the
  server ? 1 : 0). For each contended UID: it appears in at most one of inventory, warehouse and machine slots, and in
  none of them if it was mounted. This catches shared drift that the client-to-client comparison cannot see (I1, L8,
  P5).
- **Rule 8, outcome.** The kind's expected outcome from the table. Kinds whose gap is still open are listed in
  `scenarios/soak-contention-known.txt` (kind and gap id). A known kind is reported as "known gap" and counted, not
  failed, so the mode can land before the fixes, and each fix removes its line.
- **The checkpoint gains:**
  - a `carDetails` dump section (rounded like `CarDetailsSync.Signature`) in the soak's section list;
  - a forced digest of every loaded car, through a new server command `desync check all` that asks every
    `cars:<loader>` in one round (`Invoke-ForcedDigestCheck` then expects `cars:<n>` for each loader);
  - "no part claim or lock left" (exists), plus `overlapViolations` 0 from the server's `locks` command once row 18
    is in.
- **No silent stalls.** At each checkpoint, a client whose digests answer "not ready" for a key in two checkpoints in
  a row fails rule 2. This is the symptom of P7 and P8.

### Harness additions this needs

- `cardetails-set <loader> <section> <entry> <value>`.
- A `parts.transactions` dump field (open and committed counts per loader).
- A `carDetails` dump section.
- `desync check all` on the server.
- Optionally `item-where <uid>` (inventory, warehouse or machine) for rule 7.

Everything else uses existing verbs (`net-hold`, `part-fast-*`, `part-twins`, `diag-examine`, `tool-put`,
`wheel-mount`, `lift`, `car-move`, `park`, `car-delete`, `job-finish`, `sell-item`, `econ-scrap`, `vfx-unscrew`) or
row 18 verbs (`lock-try`, `lock-trace relations`).
