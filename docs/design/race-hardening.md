# Design: race hardening

ROADMAP row 20 `race-hardening`, size S. The four rows of the race and drift audit
(`docs/audits/race-and-drift-audit.md`) that rows 18 and 19 left: I6, E5, C1, C5. For each the race was checked in
code first; two are fixed, two cannot happen with the current code and are left as they are.

| Row | Reachable | Change |
|---|---|---|
| I6 reused UID ranges | yes, by any player who leaves and joins again (or a server restart) while an item of their range is in the warehouse or on a machine | server sends the highest stored UID of the player's range; the client continues after it |
| E5 duplicated economy request | no: nothing sends a request twice | none |
| C1 spawn into an occupied loader | yes, through the plain spawn path (the F6 developer spawn, `DevHotkeys`, and the harness); job and unpark spawns were already checked | the server refuses it, keeps its car and sends it to the refused client |
| C5 second baseline replaces records | no: the only second baseline comes 0.1–0.3 s after the first, before another player can act on the car | none |

## I6: UID ranges

- Each client hands out item UIDs from its own range: `UidRanges.Start(playerId) = playerId × 10¹²` (Core
  `Data/UidRanges.cs`, moved from the client). Server UIDs are `DateTime.UtcNow.Ticks` (≈ 6·10¹⁷) and never meet them.
- Player ids are slots: `Server.TcpConnectCallback` gives a new connection the first free slot, so a player who joins
  again, or another player, gets a slot whose UIDs are already in the save.
- The client set its counter after every garage sync (`UidRange.Apply`) to the highest UID of its range **in the
  inventory**. Warehouse items, items and groups on machines and items inside groups were not seen. The next item
  made in the game (an unmounted part, a crate, `new Item(id)`) then got a UID the warehouse still held. The server
  accepts such an `Add` (it checks the inventory only), and a later move from the warehouse puts a second item with the
  same UID into the inventory: `Remove`, `Update` and mounts then act on the wrong copy.
- **Fix:** the server knows every stored item. `InventorySection.HighestUidInRange(playerId)` takes the highest UID
  of the player's range over the inventory and warehouse (items, groups and the items inside groups) and the machine
  slots (item, group and its items), and sends it on the inventory snapshot's last batch (`InventorySyncPacket.UidFloor`,
  `[OptionalField]`). `UidRange.Apply` continues after the highest of: the local inventory scan, the server's floor
  and the game's own counter (`UIDManager.GetDataForSave()`, when it is already in the range; a garage reload no
  longer lowers it). Every sync (join, F7, return from the junkyard) sends a fresh floor.
- No new packet, no refusal: the client simply never makes the clash.

## E5: economy request deduplication

Not built. A duplicated `EconomyRequest` cannot happen today:

- `EconomyRequests.Send` sends each request once with `Client.Send(…, reliable: true)`: TCP for direct IP,
  `SendType.Reliable` for Steam. Both deliver each message once and in order on a connection. `Client.Send` and
  `ClientSteam.Send` have no queue and no retry; a failed Steam send is logged, not repeated.
- Nothing resends after a reconnect: a disconnect clears the pending requests (`ClientData.Reset` →
  `EconomyRequests.Reset`).
- The only resend is the harness verb `econ-ledger resend` (`EconomyRequests.Resend`, `EconomyLedgerCommands.cs`), used
  by `economy-trades` for the crate replay, which the server refuses (`Invalid`, the crate is already looted).

If a retry is ever added, it needs its own deduplication: `RequestId` restarts at 1 in every game process and slots
are reused, so the key must be the connection plus the id, and the duplicate must get the first result again rather
than a refusal.

## C1: a spawn into an occupied loader

- `CarHandlers.HandleCarSpawnRequest` checked the loader only for job cars; unparks check it in `ParkingHandlers`.
  A plain spawn (`CarSpawnHooks` → `CarSpawnManager.RequestCarSpawn` with `IsJob` false) went to
  `CarPartsStore.RegisterSpawn`, which cleared the loader's car without a word (`ClearReason.Deleted`, so its locks,
  details, parts and an active job's car went with it) and sent the new car to everybody else.
- In normal play every spawn into a garage loader is a job take or an unpark. The plain path is reached by the F6
  developer spawn (`DevCarSpawner`, setting `DevHotkeys`) and by the harness. Two players spawning into the same free
  loader at once is the race: the later request used to replace the earlier car.
- **Fix, server:** a spawn request for a loader that holds a car is refused with `CarSpawnRejected` ("Another car is
  already in that place."); the stored car stays. When that car has a baseline, the refused client also gets its live
  snapshot and details (the snapshot carries the spawn, so a client that lacks the car loads it). Without a baseline
  the car's spawn was sent to every other client when it was registered, before the refused request was handled.
- **Fix, client:** the refused client usually has the other car on that loader already, because the winner's spawn
  reaches it before the refusal. `CarSpawnManager` remembers a plain spawn request until its `CarSpawnAck`; a remote
  spawn for that loader meanwhile marks it overridden, and the refusal then keeps the loader's car instead of
  deleting it (the same rule `ParkingSync.KeepAfterRejection` has for unparks). Job spawns keep their old handling.
- `RegisterSpawn` keeps its clear as a guard; both callers now check the loader first.

## C5: a second baseline

Not built. A second baseline cannot overwrite another player's change:

- The server accepts a baseline only for the current `SpawnSeq`; the first one only from the spawner.
- Only two senders exist: the spawner's `UploadWhenSettled` (once per spawn) and the job taker's re-upload
  (`JobsSync.WatchTake`, 0.5 s after `JobStarted`, after `PrepareJob` changed the car). The harness verb
  `car-baseline` is the only other caller of `UploadBaseline`.
- In the job runs on record (10 runs of `jobs`, `jobs-latejoin` between 2026-10-06 and 2026-10-08) the two baselines
  of one car left the taker 86–280 ms apart, the job's first.
- Another player can change a part only after the car is Ready on their client (`LockGate.Enter` refuses with
  "notReady"), which needs the snapshot the server sends after the first baseline, and then a lock round trip and the
  change itself. That does not fit in 0.3 s, so the second baseline never meets a change by someone else.

## Proof

Scenario `race-hardening` (areas `cars`, `parts`):

- I6: B makes an item and puts it in the warehouse, leaves and joins again with the same player id, makes another
  item: its UID must be above the warehouse item's, and after A takes the warehouse item out both items exist once
  with their own condition. On the old code the new item got the warehouse item's UID.
- C1: B (stalled with `net-hold out`) spawns a car on an empty loader; A spawns another car there first; B is
  released. The server must keep A's car, refuse B's request, and B must end with A's car (same `SpawnSeq` and state)
  and the toast. On the old code the server replaced A's car with B's.

Runs: old code `20261008-201149_L1_race-hardening` (8 failures: the new item got UID 2000000000001 again, the server
replaced A's car with B's); fixed `20261008-201423_L1_race-hardening` (passed; B's counter after joining again was 1,
the server's floor 2000000000001); `Run-All -Lanes 1 -Smoke -Scenarios race-hardening,car-placement-race,car-dlc` →
`20261008-201608_regression.json`, 9/9 passed.
