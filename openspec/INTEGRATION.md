# Integration matrix (2026-10-06)

Result of the integration pass over the drafted changes. Row numbers are ROADMAP rows; 5a/5b are the two halves of
the former `sync-workshop-tools` (`sync-workshop-machines`, `sync-workshop-car-tools`). Landing order: 7 (groups 1–2)
→ 6 part 1 → 1 → 2 → 3 → 4 → 5a → 5b → 6 part 2 → 7 rest. "Owner" defines it; "Users" only call or subscribe.

## Packets (new or changed, all appended to `PacketTypes`)

| Packet | Dir | Owner | Users / notes |
|---|---|---|---|
| `SyncBegin`, `SyncAck`; `SyncEnd` + `snapshotId`, `Items` | S→C / C→S | 7 (g2) | every snapshot provider |
| `PlayerRestore` | S→C | 7 (g4) | 6 skips spawn placement when applied |
| `DisconnectPacket.reason` | S→C | 7 (g3) | — |
| `PlayerPresence`, `PlayerRoster`; `MovementPacket.Scene` | both / S→C | 6 | 7 copies name/scene/position into `PlayerRecord`; `PlayerPresence` is `[AllowBeforeSync]` |
| `CarPartsChange`, `CarPartsChangeResult` (renamed in place), `CarPartClaim`, `CarPartClaimUpdate`, `CarPartsSnapshot`, `CarPartsResyncRequest`, `CarSpawnAck`; `CarSpawnResponse.SpawnSeq` | both | 1 | 2 (unpark ack), 4 (`SpawnSeq`), 5a (`CarSubPartUpdatePacket` record) |
| `LifterActionRequest`, `LifterState`, `CarPlaceChangeRequest`, `CarPlaceChanged`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingSlotUpdate`, `ParkingState`, `ParkingLevelUnlockRequest`, `ParkingResyncRequest` | both | 2 | — |
| `CarParkRequest { RequestId, CarLoaderID, PreferredSlot, Car, Price }` → `CarParkResult { RequestId, Accepted, Reason (ParkingFull, NoMoney, Invalid), Slot }` | C→S / S→requester | 2 | 6 (purchases, `CarLoaderID = -1`); no `CarPurchase*` packets exist |
| `CarSpawnResponse` + `CarData`, `CarDataVersion` (+ `Place`) | S→C | 2 | 1's snapshot spawn info carries them |
| `JobsState`, `OrderGeneratorRole`, `OrderGenerated`, `OrderAdded`, `OrderAction`, `OrderActionResult`, `JobStarted`, `JobProgress`, `JobEndRequest`, `JobRemoved` (+ `IsCompleted`) | both | 3 | `CarSpawnRequest.IsJob/JobID` validated |
| `CarDetailsUpdate`, `CarDetailsRequest` | both | 4 | 3 and 5b trigger them only through 4's client API |
| `ToolSlotUpdate`, `ToolSlotRejected`, `ToolSlotProperty`, `ToolPartChange`, `ToolPosition`, `ToolsState`, `ToolClaim`, `ToolClaimUpdate` | both | 5a | — |
| `ToolAction` | C→S→others | 5b | relay only |
| `ItemActionType.Update` | both | 5a | UID-idempotent ADD is 1's |

## APIs and events

| Side | API / event | Owner | Users |
|---|---|---|---|
| server | `GameDataManager.StateLock`, `RequestSave()` | 7 | all (no row adds a lock) |
| server | `ISaveSection`, `ISnapshotProvider`, `[SessionSection]`, `SyncOrder` | 7 | 1, 2, 3, 4, 5a, 6 |
| client | `SyncTracker.Applied(key)`, `InSnapshot`; `ClientData.IsInitialSyncFinished` | 7 | all snapshot handlers |
| client | `ClientScene.LocalScene`, `IsGarageReady` (true from start of `CustomLoad`), `GarageBound(apply, mirrorOnly)`, `LeavingScene(from, to)` | 6 | 1, 2, 4, 5a, 5b (gate); 3 (`IsGarageReady`); 13 (`LeavingScene`) |
| server | `PresenceRegistry.InScene(scene)`, `PresenceEvents.SceneChanged/Left` | 6 | 1 (claims), 3 (generator, order claims), 5a (balancer), 13 |
| client | `PresenceManager.EnsureNotSeatedIn(loader)` | 6 | subscribed to 2's `BeforeRemoteCarMove`, called by `CarSpawnDelete` |
| server | `CarPartsStore.RegisterSpawn(record, clientId)`, `ClearLoader(loader, reason)`; events `SpawnRegistered`, `LoaderCleared(loader, removedRecord, reason ∈ Deleted, Parked, JobEnded, SpawnerLeft)` | 1 | 2 (unpark, park, lift reset, return to parking on `SpawnerLeft`), 3 (job spawn/claim release/job end; reopen on `SpawnerLeft`) |
| client | `CarPartsSync.UploadBaseline`, `RebuildRegistry`, `MarkDirty(loader, CarPart/PartScript)`, `IsReady`, `SpawnSeq`; events `BaselineUploaded`, `LocalPartsCommitted` | 1 | 2 (unpark), 3 (`PrepareJob`), 4 (snapshot, `SpawnSeq`, remount cosmetics), 5b (welder, interior, engine swap) |
| client | `PartRegistry.Build(root)`, `PartTransaction`/`InventoryDelta` for a non-car root; engine crane group transaction | 1 | 5a (engine stand), 5b (crane effect only) |
| server | `ParkingService.TryAdd/TryRemove/BroadcastSlot`, `PlacementState.OnLoaderCleared` | 2 | 10 later (sell) |
| client | `CarPlacementSync.BeforeRemoteCarMove`, `NewCarDataCodec.ToParkedCar`, `ParkingSync.ParkResultReceived` | 2 | 6 |
| server | `JobsService` (`OnClientLeft`, `Tick`), `StatsHandlers.ApplyExp` | 3 | — |
| client | `CarDetailsSync.MarkDirty(loader, sections, parts)`, `FlushNow` | 4 | 5b (paint shop, wash, interior, oil bin) |
| client | `ToolSync` (`ApplyingRemote`, `NeutralUids`), `ModToolId` | 5a | 5b appends ids |

## Save sections

| Key | Version | Owner | Content |
|---|---|---|---|
| `world`, `garage`, `inventory` | 1 | 7 | shared money/scrap/level/XP/skills, upgrades, inventory |
| `cars` | v1 (7) → v2 (1: loader entries, baseline, `EngineSwap`) → v3 (4: `Details`, no-op step) | 1 | 2 adds `Place`, `CarData` without a bump |
| `car-placement` | 1 | 2 | lifts, parking slots (blobs), levels |
| `workshop-tools` | 1 | 5a | slots, positions (reservations are runtime only) |
| `jobs` | 1 | 3 | orders, active jobs, `NextJobId`, missions |
| `players` | 1 | 7 (Part B) | `PlayerRecord { Key, Name, LastSeenUtc, Position, Rotation, Scene }` |

## SyncOrder slots

| Order | Key | Owner | Items counted |
|---|---|---|---|
| 0 / 10 / 20 | `world` / `garage` / `inventory` | 7 | 1 / 1 / batches |
| 100 | `cars` | 1 | cars |
| 150 | `car-details` | 4 | valid records (snapshot only) |
| 200 | `car-placement` | 2 | 1 + occupied slots + raised lifts |
| 300 | `workshop-tools` | 5a | 1 |
| 400 | `jobs` | 3 | 1 |
| 450 | `self` | 7 | 0/1 (`PlayerRestore`) |
| 500 | `players` | 6 | 1 (snapshot only) |

## Harness

Verbs are globally unique (`Commands.Discover` throws on a duplicate). Existing: `ping`, `connect`, `disconnect`,
`dump`, `screenshot`, `quit`.

| Owner | Verbs |
|---|---|
| 7 | `stats-add`, `to-menu` (g1); `player-key`, `send-early-stats`, `profile-pref` (later groups) |
| 6 | `travel`, `scene-list`, `teleport`, `set-name`, `leave-mark`, `sit`, `stand`, `engine`, `buy-car-here`, `junk-buy` |
| 1 | `part-trace` (spike only), `car-spawn`, `car-delete`, `car-ready`, `car-hold`, `part-list`, `part-unmount`, `part-mount`, `part-examine`, `part-switch`, `part-claim`, `part-release`, `part-corrupt`, `engine-swap` |
| 2 | `lift`, `car-move`, `net-hold`, `park`, `unpark`, `park-swap`, `parking-unlock`, `park-incoming` |
| 3 | `jobs-trace`, `orders-generate`, `orders-mission`, `orders-autogen`, `orders-list`, `order-slots`, `orders-accept`, `orders-decline`, `orders-reload`, `job-examine`, `job-check`, `job-finish`, `tutorial-run`, `job-spawn-unclaimed`, `job-end-dup` |
| 4 | `cardetails-probe`, `cardetails-roundtrip`, `cardetails-ui`, `cardetails-fluid`, `-wheel`, `-alignment`, `-headlamp`, `-gearbox`, `-tune`, `-paint`, `-tint`, `-wash`, `-plate`, `-mileage`, `-lights`, `-bonus`, `-randomize`, `-hold` |
| 5a | `tool-list`, `tool-trace` (5b adds hooks to it), `give-item`, `give-group`, `tool-put`, `tool-take`, `tool-hold`, `tool-local-put`, `tool-resync`, `tool-mount`, `tool-balance`, `tool-balance-open`, `tool-balance-cancel`, `tool-charger`, `tool-angle`, `tool-stand-part`, `tool-move`, `tool-repair`, `tool-paint-part` |
| 5b | `tool-engine-out`, `tool-engine-in`, `tool-paint-car`, `tool-use`, `tool-dyno` |

| PowerShell helper / server command | Owner (first to land) |
|---|---|
| `Send-ServerCommand`, `Wait-ServerLog`, `Stop-TestServer`, `Start-TestServer`, `--command-file`, server `save` | 7 (task 1.1, 2.5) |
| `Wait-HarnessDump` | 6 (task 2.5) |
| `Wait-HarnessDumpsEqual` (built on `Wait-HarnessDump`) | 5a (task 5.3) |
| `Compare-HarnessDumps` | existing; sections added by 2 (`lifters`, `placement`, `parking`), 3 (`jobs`) |
| server commands `players` / `cars` / `placement` / `jobs` / `cardetails` | 7 / 1 / 2 / 3 / 4 |

Scenarios (unique): 7 `server-restart`, `profile-safety`, `rejoin`, `latejoin`, `persistence-restart`,
`duplicate-identity`; 6 `presence-latejoin`, `scenes`, `presence`, `purchases`; 1 `car-parts`, `car-parts-latejoin`;
2 `car-placement`, `car-placement-latejoin`, `car-parking-full`; 3 `jobs-trace`, `jobs`, `jobs-latejoin`,
`jobs-restart`; 4 `car-details`, `car-details-latejoin`; 5a `tools-slots`, `tools-race`, `tools-latejoin`;
5b `tools-car-effects`.

## Settled in this pass (main ones)

- `IsGarageReady` deadlock: row 6 sets it before `AskForSync`; snapshot handlers never wait for the sync; live
  garage packets are queued between `SyncEnd` and `SyncAck` (`ClientScene.GarageBound`, row 7 `InSnapshot`).
- Row 6 purchases use row 2's `CarParkRequest`/`CarParkResult`; `CarPurchase*` removed.
- Rows 2/3 no longer call each other: row 1's `ClearLoader(reason)` + `LoaderCleared`/`SpawnRegistered` events.
- Customer cars cannot be parked while connected (user): row 3 D12 reduced to "lost car reopens", full garage
  aborts the take; row 2's `-1` branch serves row 6 only.
- Idempotent ADD → row 1; `ItemActionType.Update` → row 5a.
- Engine swap: row 1 stores and replays (re-baseline bumps `Revision`), row 5b only triggers; crane out/in hooks
  in row 1; fallback "blocked while connected" accepted.
- Interior detailing split (row 1 / row 4) consistent in rows 1, 4, 5b; row 3 drops a redundant `MarkDirty`.
- `cars` section v2 (row 1) → v3 (row 4).
- Steam achievements to all players (row 3), `LightsOn` and bonus parts (row 4), balancer lock (row 5a),
  workshop split (5a/5b) — user decisions applied.

## Not resolved here (recommendations)

1. **Row 13 is not drafted**, but rows 6 (`LeavingScene`), 5b (dyno trigger) and 4 (dyno non-goal) depend on it.
   Recommendation: draft row 13 before M3 starts; until then 5b's group 10 stays parked.
2. **Steam stats for non-finishers** depend on row 3's trace finding a callable game entry point for the job's
   stats/achievements. Recommendation: if none exists, accept "finisher only" as a known gap rather than calling
   Steamworks directly (the harness instances share one Steam account and cannot verify it).
3. **Balancer reservation entry points** (minigame open, take) are only known after row 5a's manual trace (task
   1.3, needs the user). Recommendation: schedule that session before row 5a group 7.
4. **Interim client-computed values** (spawner's roll in row 1, order generation and payout in row 3) are marked as
   interim; ROADMAP row 16 (`server-game-logic`, maintained by the coordinator) replaces them.
