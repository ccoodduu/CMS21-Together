# Integration matrix (2026-10-06)

Result of the integration passes over the drafted changes (first pass: rows 1–7; second pass, same day: rows 8, 9,
12, 14 and the new 14a). Row numbers are ROADMAP rows; 5a/5b are the two halves of the former `sync-workshop-tools`
(`sync-workshop-machines`, `sync-workshop-car-tools`); 14a is `multiplayer-guard`, split from row 14
(`desync-detection-and-resync`, which keeps parts (b)–(d)). Landing order: 7 (groups 1–2) → M1: 6 part 1 → 8 part 1 →
9 part 1 → 14a → 12 part 1 → M2: 1 → 2 → 14 (b, c) → 3 → 4 → 5a → 5b → 6 part 2 → 8 part 2 → 7 rest → 14 (d) → 9/12
part 2. "Owner" defines it; "Users" only call or subscribe.

## Packets (new or changed, all appended to `PacketTypes`)

| Packet | Dir | Owner | Users / notes |
|---|---|---|---|
| `SyncBegin`, `SyncAck`; `SyncEnd` + `snapshotId`, `Items` | S→C / C→S | 7 (g2) | every snapshot provider |
| `PlayerRestore` | S→C | 7 (g4) | 6 skips spawn placement when applied |
| `DisconnectPacket.reason` | S→C | 7 (g3) | — |
| `PlayerPresence`, `PlayerRoster`; `MovementPacket.Scene` | both / S→C | 6 | 7 copies name/scene/position into `PlayerRecord`; `PlayerPresence` is `[AllowBeforeSync]` |
| `CarPartsChange`, `CarPartsChangeResult` (renamed in place), `CarPartClaim`, `CarPartClaimUpdate` (retired by 18: values kept, never sent), `CarPartsSnapshot`, `CarPartsResyncRequest`, `CarSpawnAck`; `CarSpawnResponse.SpawnSeq` | both | 1 | 2 (unpark ack), 4 (`SpawnSeq`), 5a (`CarSubPartUpdatePacket` record) |
| `LifterActionRequest`, `LifterState`, `CarPlaceChangeRequest`, `CarPlaceChanged`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingSlotUpdate`, `ParkingState`, `ParkingLevelUnlockRequest`, `ParkingResyncRequest` | both | 2 | — |
| `CarParkRequest { RequestId, CarLoaderID, PreferredSlot, Car, Price }` → `CarParkResult { RequestId, Accepted, Reason (ParkingFull, NoMoney, Invalid), Slot }` | C→S / S→requester | 2 | 6 (purchases, `CarLoaderID = -1`); no `CarPurchase*` packets exist |
| `CarSpawnResponse` + `CarData`, `CarDataVersion` (+ `Place`) | S→C | 2 | 1's snapshot spawn info carries them |
| `JobsState`, `OrderGeneratorRole`, `OrderGenerated`, `OrderAdded`, `OrderAction`, `OrderActionResult`, `JobStarted`, `JobProgress`, `JobEndRequest`, `JobRemoved` (+ `IsCompleted`) | both | 3 | `CarSpawnRequest.IsJob/JobID` validated |
| `CarDetailsUpdate`, `CarDetailsRequest` | both | 4 | 3 and 5b trigger them only through 4's client API |
| `CarAwayRequest`, `CarAwayUpdate`, `CarAwayRelease`, `TestDriveResult`, `TestDriveResultAck`; `CarSpawnResponse.SpecialState`; details section `Dyno` (`CarDetailSection.Dyno = 512`) | both | 13 | 1, 2, 3, 4 refuse edits of a car away for someone else (server `CarAwayRegistry.Blocks`) |
| `ToolSlotUpdate`, `ToolSlotRejected`, `ToolSlotProperty`, `ToolPartChange`, `ToolPosition`, `ToolsState`, `ToolClaim`, `ToolClaimUpdate` | both | 5a | — |
| `ToolAction` | C→S→others | 5b | relay only |
| `ItemActionType.Update` | both | 5a | UID-idempotent ADD is 1's |
| `ConnectPacket` + `playerKey` | C→S | 7 | sent through 9's `ConnectPacketFactory` |
| `ConnectPacket` fills `gameVersion`; + `protocolHash`, `dlc`, `mods` (`[OptionalField]`); welcome carries `protocolHash` | both | 9 | client sends only after checking the welcome, both transports |
| `ConnectPacket` + `password`, `adminKey` (`[OptionalField]`, never logged) | C→S | 8 (part 2) | — |
| `HeartbeatPacket` + `sentTicks`; `PlayerPings`, `KickRequest` | both | 8 (part 2) | — |
| `ServerInfo` | S→C | 8 (part 1) | rich presence (8) |
| `ServerInfo` + `SharedDlc` (`[OptionalField]`; resent to every client when the set changes) | S→C | 9 | 1, 2, 5a (block DLC content outside the set) |
| `StateDigestRequest`, `StateDigest`, `StateDetailRequest`, `StateDetail`, `DesyncNotice` | both | 14 (b, c) | — |
| `BugReportRequest`, `BugReportCollect`, `BugReportResult` | both | 14 (d) | — |
| `PlayerActivity { PlayerId, State }` (`PlayerActivityState { Kind, CarLoaderID, PartKey, ToolType, ModTool, Progress }`); `PlayerPresenceRecord.Activity` (`[OptionalField]`, never saved) | C→S→same scene | 17 | 6 (roster carries it to late joiners); server drops more than 8/s per client and clears it on scene change |
| `CarDriveStart { PlayerId, DriveId, Scene, CarLoaderID, CarToLoad, CarBlob, CarBlobVersion }`, `CarDriveState { PlayerId, DriveId, Seq, Payload }` (36-byte `DriveStateCodec`, unreliable, ≤ 15/s, server cap 20/s), `CarDriveStop { PlayerId, DriveId, FinalPose }` | C→S→same scene | 17 (part 2) | 13 (`CarLoaderID` = the away car; a start for a car away with someone else is dropped), 2 (`NewCarDataCodec` blob), 6 (scene relay; running drives sent on scene entry; stopped on scene change and leave). Test track only (no garage driving, spike 8.1); no `CarAwayKind.Driving` |
| `CarLockRequest { RequestId, CarLoaderID, SpawnSeq, Kind, X, S, Items, ExtendLockId, OtherLoaderID, OtherSpawnSeq }` → `CarLockResult { RequestId, LockId, Granted, Refusal, HolderPlayerId, ConflictKey }` (`RequestId = 0`: a refusal without a request, `CarBusy` for park, delete, job end, lift, move); `CarLockUpdate` (full record, `OwnerPlayerId = -1` = released, also in the `cars` snapshot), `CarLockRelease`, `CarLockRenew`; `ServerInfo` + `LockScope`, `LockExpirySeconds`; `ParkRefusal.Busy` | both | 18 | 1, 2, 3, 5b, 13, 17 (claims view) |
| `CoopPing { PlayerId, Scene, CarLoaderID, PartKey, Position }` | C→S→same scene | 22 | relay only, no state (not saved, not in a snapshot); the server drops a ping from a stale scene or a scene without avatars and more than one per 0.4 s per player, and turns a ping with an unknown loader or key into a spot ping |
| `ShopListChange { ClientSeq, Removed, Deltas }` → `ShopListState { Entries, Revision, SourcePlayer, SourceSeq, Refused }` (`ShopListEntry`: id, amount, the `ShopListItemDataEx` fields) | C→S / S→all (S→actor when nothing changed) | 23 | — (design `docs/design/shared-shopping-list.md`) |
| `SeatRefused { CarLoaderID, SeatLeft, HolderPlayerId }` | S→the refused client | 19 (part 3, D17) | 6 (`PlayerHandlers.OnPlayerPresence` keeps the first seat holder and stores the second record without seat and engine; the client leaves with `GameScript.ExitFromInterior(true)` and shows "<name> is sitting there.") |
| `InventoryGroupItemAction` with `ItemActionType.Update` | S→one client | 19 (part 3, D16) | the stored group as the answer to an `Add` of a group the server has with other content; the client replaces its group |
| `RideUpdate { DriverId, PassengerId, CarLoaderID, Active, Reason }` (`RideEndReason`: `DriverReturned`, `DriverLeft`, `PassengerLeft`, `PassengerDidNotArrive`, `DriveCancelled`) | S→all in session; running rides S→a client entering the test track | 21 | 13 (`CarAwayRegistry.Granted`/`Released`, new events), 6 (seat records), 17 part 2 (observer car, `RemoteCars.Of`); design `docs/design/ride-along.md` |

| `CarBodyPartUpdatePacket.Changed`, `CarSubPartUpdatePacket.Changed` (`PartFields`: `Mount`, `Bolts`, `Identity`, `Condition`, `Quality`, `Examined`, `Paint`, `Dust`, `Switched`, `All`; `[OptionalField]`) | both | 19 (part 1, D1/D2) | a change record never carries 0; relays carry the groups the server wrote, results the groups that differ from the send; snapshots, resyncs and rejections carry `All`; stored records keep 0 |
| `CarDetailsUpdatePacket.WheelMask` (bit per wheel index, 0 = all) and `AlignmentMask` (`AlignmentFields`, `None` = all) (`[OptionalField]`) | both | 19 (part 1, D4/D5) | a details update carries only the changed entries |
| `ToolSlotRejectedPacket.Item` (`SlotItemOutcome`: `Unchanged`, `Returned`, `Gone`; `[OptionalField]`) | S→the refused client | 19 (part 1, D9) | `Returned`: the server put the item back (and relayed its `Add`); `Gone`: another player used it ("<name> used this part.") |
| `InventorySyncPacket.UidFloor` (`[OptionalField]`, last batch only) | S→the syncing client | 20 | the highest stored UID of that player's range (inventory, warehouse, machine slots, items in groups); `UidRange.Apply` continues after it (audit I6, `docs/design/race-hardening.md`) |
| `CarLockKind.Tune`, `CarLockKind.BonusPart` (appended), `LockKeys.Bonus(slot)` = `x:<slot>`, `LockKeys.Tune` = `tune` (`IsWellFormed`, `CarLocks.Exists`); `CarLockRequestPacket.Expect` (slot state, `Stale` refusal with the stored entry) and `CarLockResultPacket.HolderKind` (both `[OptionalField]`); `ToolSlotUpdatePacket.Created` (`[OptionalField]`); `ModCarDetails.BonusSlots` (`ModBonusSlot` per slot, replaces the never-written `BonusParts`), detail entries `x:<slot>` | both | 25 (drafted) | 18 (lock rules), 4/19 (per-entry details), 5a (created engine on the stand) |
| `CarAwayKind.RaceTrack`, `CarAwayKind.SpeedTrack` (appended); `RideUpdate.Scene` (`[OptionalField]`); `TrackRecord { Scene, LapMs }` → `TrackRecordUpdate { Scene, PlayerId, LapMs, IsGroupRecord }` | both | 27 (drafted) | 13, 17 part 2, 21 (track set instead of the test track only) |
| `GarageLookUpdate`, `GarageLookClaim` → `GarageLookClaimResult`; `GarageState.Look` (`ModGarageLook`, `[OptionalField]`) | both | 28 (drafted) | 14/19 (`garage` digest includes the look) |
| `JobStatsReport { JobId, Stats }` (finisher → S), `JobStatsAward { JobId, Stats }` (S → contributors); active job `Contributors` (`[OptionalField]`) | both | 30 (drafted) | 3 (`JobEndContext`, `JobsService`), 18, 19 part 1 (sources of contributions) |

Rows 12 and 14a add no packets; rows 26 (`shared-salon`, drafted: guard entries and a proof only) and 29 (`seated-avatars`, drafted) add none either.

## `DisconnectReason` (Core `StartPackets.cs`, append-only like `PacketTypes`)

| Values, in enum order | Owner | Users |
|---|---|---|
| `None`, `ServerShutdown`, `Kicked`, `VersionMismatch`, `DuplicateIdentity`, `MissingIdentity`, `SyncFailed` | 7 (on `main`) | 8 (display, `Kicked` from `/kick`/`KickRequest`), 9 (`VersionMismatch` for the build/protocol check) |
| `ServerFull`, `WrongPassword` | 8 (task 2.4, M1; `WrongPassword` used from part 2) | — |
| `GameVersionMismatch`, `ModMismatch` | 9 (task 2.2, M1, after 8; no `DlcMismatch`: DLC never refuses) | 8 (`ConnectionMessages`) |

Rows 12, 14 and 14a add none. Client-only failures (`Unreachable`, `Timeout`, `SteamUnavailable`, `SteamFailed`) are
row 8's client enum `JoinFailure` and never travel.

## APIs and events

| Side | API / event | Owner | Users |
|---|---|---|---|
| server | `GameDataManager.StateLock`, `RequestSave()` | 7 | all (no row adds a lock) |
| server | `ISaveSection`, `ISnapshotProvider`, `[SessionSection]`, `SyncOrder` | 7 | 1, 2, 3, 4, 5a, 6 |
| client | `SyncTracker.Applied(key)`, `InSnapshot`; `ClientData.IsInitialSyncFinished` | 7 | all snapshot handlers |
| client | `ClientScene.LocalScene`, `IsGarageReady` (true from start of `CustomLoad`), `GarageBound(apply, mirrorOnly)`, `LeavingScene(from, to)` | 6 | 1, 2, 4, 5a, 5b (gate); 3 (`IsGarageReady`); 13 (`LeavingScene`) |
| server | `CarAwayRegistry.Blocks(loader, client, what)`, `IsOwner`, `OwnerOf`, `SendActive`; `CarDetailsStore.FoldTestDrive` | 13 | 1 (claims, part changes, delete), 2 (move, lift, park), 3 (job end), 4 (details updates) |
| server | `CarLocks` (replaces `CarClaims`): `HeldByOther(loader, client)`, `OtherOwnerOn`, `RefuseBusy(loader, client, what)`, `HoldsCar`, `ExclusiveOwner`/`SharedOwner(loader, key, except)`, `ReleaseCommitted`, `ReleaseOwnerOnLoader`, `Describe(now)` | 18 | 1 (`FindConflict`, release on commit), 2 (lift, move, park), 3 (job end), 6/10 (car sale), 13 (away `InUse`, grant releases own locks) |
| client | `CarLockMirror` (records, `Conflict(set)`, `Request`, `Release`, renew), `LockSets.ForPart/ForBody/ForFluid/ForCrane/ForCar`, `LockGate.Enter(GatedAction)`/`Reported`, `LockLifecycle`, `LockMessages`, `CarMotion.IsMoving`, `CarLockMirror.OtherOwnerOn(loader)`/`OwnFluidLockCovers`; gates `LockHooks` (parts, body, crane, chooser picks), `LockFluidHooks` (refill, extractor, oil bin), `LockCarHooks` (lift in `LifterSync`, move and swap in `CarPlacementSync`; `CarPlacementSync.IsMovingLocally`), `JobHooks` refuses a job end while another player holds a lock on the car; `OilBinHooks.IsDraining(carLoader)`; `PartClaims` is a view over the mirror (`Held`, `OwnerOf`, `HeldByOther`, `ClaimChanged`); `CarDetailsSync.FlushNow(loader, sections, done) → Sent/Unchanged/Deferred/NotReady` (`done` runs after the send, also a deferred one), `FlushBeforeChange(loader)` | 18 | 17 (`ClaimChanged`), 4 (fluids flushed before a release) |
| client | `Guard/PieOptionState` (the one owner of pie option state: a `PrepareIcons` prefix asks its sources, the guard first, and disables or restores options with `SetEnableOption`; `AddSource(PieBlockSource)`, `Blocks`, `Clicked(id)`), `LockPie` (car move options and `car_drive`), `LockPreviews` (`PrepareItemsToMount` postfix, `HidePreview` on locked slots), `LockChooser` (lock overlay on chooser rows, reopen after a refused pick), `CarLockMirror.ItemHolder(uid)`, `LockCarHooks.LoaderAtPlace` | 24 | 14a (the guard's pie blocks moved from its own `PrepareIcons` postfix into `PieOptionState`) |
| client | `CarAwaySync.LockedForMe`, `BlockIfLocked`, `Request`, `Release`, event `Released`; `DynoSync.Commit(carLoader)`, `IsOpenOn`; `TestDriveSync.HoldDeparture` (called from `SceneHooks`) | 13 | 1 (`PartClaims`, crane), 2 (move, lift), 3 (job end), 4 (poll skip), 5b (`MeasurePower`) |
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
| server | `CarsSnapshotProvider.SendCar(clientId, loader)` (single-car `CarPartsSnapshot` without a request packet) | 1 | 14 (resend) |
| server | `ParkingService.SendFullState(clientId)` (the `ParkingResyncRequest` answer without a request) | 2 | 14 (resend) |
| client | `ConnectionStatus` (`Failed(reason, detail)`, `Disconnected(reason, detail)`), `ConnectionMessages.For`, `Client.ResetAfterFailure()`, `JoinService.Join(target)` | 8 | 7 (server-loss detection, task 5.3, calls `Disconnected` instead of its own message), 9 (welcome check, reason texts) |
| client | `ModNotify.Toast(text)`, `ModNotify.Message(title, text)` | 8 | 14a (guard message), 14 (desync notice, bug-report popup) |
| server | `Server.Refuse(clientId, reason, message)` (send with `playerID = clientId`, close the slot incl. `SteamConnection` next tick) | 8 (task 2.4) | 9 (all compatibility refusals), 8 part 2 (password, kick); `/kick` moves to it |
| server | `Client.Disconnect(reason)`; row 6's leave broadcast carries the reason | 8 (part 2, additive) | 8 ("was kicked" toast) |
| client | `PresenceManager.PlayerAdded(record, fromSnapshot)`, `PlayerRemoved(record)` (added to row 6's code) | 8 (part 2) | 8 (toasts, session panel) |
| client | `ConnectPacketFactory.Build()` (the only `ConnectPacket` builder, sent from `AuthHandler.HandleConnect` after the welcome check, both transports) | 9 | 6 (name), 7 (`playerKey`), 8 (password, admin key) |
| Core / client / server | `ProtocolHash`, `ModClassifier` (+ `ModClassifierRules`), client `ModInventory`, server `CompatibilityPolicy.Evaluate` (first line of `AuthHandler.OnConnected`) | 9 | 7 (identity step runs after it), 14 (`mods.json` in bug reports) |
| server / client | server `SharedDlc.Shared` (DLC product ids owned by every connected player) and `SharedDlc.Changed`; client `ClientData.ServerInfo.SharedDlc` | 9 | 1, 2, 5a (see "DLC content" below) |
| Core | `BuildInfo` (`ModVersion`, `FullVersion`, `Commit`, `LoadedFullVersion`) | 12 | 7 (save envelope `ModVersion`), 8 (mismatch message), 9 (version check), harness `build-info` |
| client | `FeatureGuard` (`Decide`, `Bypass` scope, block ring buffer), `GuardRules` | 14a | every row adds its guard entries in its merge commit; 14 (`Bypass` for the resync reload, `guard.log`) |
| client / server / Core | `IClientDigest`, `IServerDigest`, `[DigestSection]`, `CanonicalHasher`, `Projection` | 14 | 3, 4, 5a register a digest when they land (see "Not resolved") |
| client | `ResyncController.Request()` | 14 | harness `resync` |
| client | `PartChanges.RemoteChangeApplying/RemoteChangeApplied(loader, body, sub)` (live remote changes only), `PartClaims.ClaimChanged(loader, keys, owner, fromSnapshot)` (added to row 1's code) | 17 | 17 (`PartGhosts`, `BoltReplay`, `ActivityCapture`) |
| client | `VisualScope.CheckLeak(what)` (called from 1's `PartChangeTracker.MarkDirty` and 4's `CarDetailsSync.MarkDirty`), `CancelLoader` (1's snapshot apply and car delete), `CancelFor` (1's `OnResult`) | 17 | 1, 4 |
| client | `CarToolActions.LocalWork`, `ToolSync.OwnClaims` (read-only views added to 5b/5a) | 17 | 17 (`ActivityCapture`) |
| client | `RemoteEngines.Create(carLoader, playerId, loader)` made public (row 6's engine sound prefab for an observer car); `TestDriveSync` reads the track car through `PrepareCarPhysics.Get().CarLoader` | 17 (part 2) | 17 (`RemoteCars`) |
| Core | `Diagnostics/Redaction` (shared redaction rule, below) | 14 | 12 (`Collect-Logs.ps1` mirrors it) |

| Core | `PartRecordMerge` (`Normalise(stored, incoming, precondition)`, `Differ`, `BaseDiffer`, `WithGroups`, `WithChanged`), `PartFields` | 19 (part 1) | server `CarPartsHandlers.OnChange`, `ToolsStore.OnPartChange`; client `PartMasks`, `PartChanges`, `EngineStandParts` |
| Core | `DigestMappers.Details` (per entry: the `Signature`, `info` only its mileage), `.Tools` (per non-empty machine slot: uid, item or group id, mounting, balanced; a slot with UID 0 counts as empty), `.Warehouse` (`wh:` items and groups as the inventory projects them), `.Garage` (unlocked levels of garage upgrades and skills, barn count), `.Jobs` (open order ids, active job id → loader); `GlobalKeys` (`world`, `inventory`, `car-placement`, `workshop-tools`, `warehouse`, `garage`, `jobs`) and `CarKeys` (`cars`, `car-details`, per loader). The server asks every global key each round and `cars:L` and `car-details:L` of one car (all cars in a forced round); "not ready" keeps a pending mismatch, which expires after four asks | 19 (part 2) | server `ReconciliationService`, `BugReportWriter`; client `ClientDigests`, `BugReport`; harness `digest-show` |
| Core | `CarDetailEntries` (entry ids `f:<type>.<id>`, `w:<i>`, `a:<field>`, `c:<panel>`, `t:<partKey>`, `t:gearbox`, `paint`, `plates`, `info`, `dyno`; `Split`, `Signatures`, `Signature`), `DetailsMerge` (`Merge` per entry with the masks, `Only`, `CarriedSignatures`) | 19 (part 1) | server `CarDetailsStore`; client `CarDetailsSync`, `CarDetailsIO`; harness `carDetails` dump; part 2's details digest |
| server | `InventoryChanges` (remover of every removed UID for the session, last 10 000; a copy of the item for 60 s: `NoteRemoved(item|group|uid, client)`, `RemovedByOther`, `RemovedBy`, `Remover`, `Seen`, `TryTakeCopy`, counters `unknownSlotItem`, `removeMissing` in `tools`); `EconomyOutcome.Effect` takes the client id | 19 (part 1, D9) | `ToolsStore.Check`, `CarPartsHandlers.FindConflict`, `ToolsStore.FindConflict`, economy effects |
| server | `ParkingLot.Records` (`ParkedRecord { CarId, Body, Sub, EngineSwap, Details }`, server only, in the `car-placement` save section without a version bump), `CarLoaderEntry.ParkedRecord`, `ParkingService.KeepRecord/TakeRecord`, `CarDetailsStore.StoreParked`; counter `parkedRecordsDropped` in `cars` | 19 (part 1, D10) | `ParkingHandlers`, `CarPartsHandlers.OnBaseline`, `PlacementRules.OnLoaderCleared` |
| client | `PartChangeTracker.SendNow(loader)`, `CarDrain.Settled(loader)`; the park coroutine waits up to 1 s for them ("Try again in a moment."), a local delete sends the pending change at once | 19 (part 1, D10) | `ParkingSync`, `CarSpawnHooks` |
| client | `PartTransactions` per loader (`HasOpen(loader)`, `DropLoader(loader, keepCommitted)`: a remote removal rolls the loader's transactions back, a local one keeps the committed ones for their result); `CarPartsSync.OnCarDeleted(loader, local)` | 19 (part 1, P7) | `CarHandlers`, `JobsSync`, `CarSpawnHooks` |

Prefix rule on guarded game methods: row 14a's prefixes run at `Priority.First`; every other prefix of this mod on the
same method (row 6's `SceneHooks` on `NotificationCenter.SelectSceneToLoad`, formerly `DisconnectHooks`) takes
`bool __runOriginal` and does nothing when it is false. Pie option state has one owner, `PieOptionState` (row 24):
a row that greys pie options adds a source to it instead of calling `SetEnableOption` itself.

## No silent drops (row 19 part 3, design D16 of `state-merges-and-contention`)

Rule (user, 2026-10-07): whenever the server refuses, ignores or overrides a client's action, it sends that client the
authoritative result, so the client equals the server right after, without F7 or a digest round. The server's
decisions do not change. A new handler that returns early must answer the same way.

| Server path | Answer to the acting client |
|---|---|
| `InventoryHandlers` `Update` of an item the server does not have | that item's `Remove` (logged with the remover); the client skips it while an open part transaction holds the item's `Add` |
| `InventoryHandlers` `Add` of an item or group the server has | nothing when equal; the stored copy as `Update` when it differs |
| `InventoryHandlers` `Remove` of a UID the server does not have | nothing (the client lacks it too) |
| `EconomyRules.PartRepair` refused (item gone) | the refusal and `WorldState`, plus the item's `Remove` (`EconomyOutcome.Answer`) |
| `GarageUpgradeHandler` refusal or no-op (unknown id, level out of range, already unlocked, not enough money or points) | `WorldState` and `GarageState` to the requester (a success still broadcasts both) |
| `ParkFromGarage` with no car on the loader (the second park) | `Invalid`, `ParkingService.SendState` and the loader's `CarSpawnDelete` |
| `HandleCarSpawnDelete` of an empty loader (the second delete) | not relayed |
| `HandleCarSpawnRequest` for a loader that holds a car (row 20, audit C1) | `CarSpawnRejected` ("Another car is already in that place."), plus the stored car's live snapshot and details when it has a baseline; the client keeps the loader's car when the winner's spawn reached it first (`CarSpawnManager.KeepAfterRejection`) |
| `OnCarPlaceChange` of an unknown loader | the loader's `CarSpawnDelete` |
| `JobsService.OnOrderGenerated` dropped (not the generator, tutorial mission, over the limit) | the jobs snapshot |
| `OnOrderAction` Accept or Decline of an unknown order | the refusal plus `JobRemoved { Expired }`; an accept shows "This order is no longer available." |
| `OnJobEnd` not active, out of bounds, car away, or refused `Busy` (row 18) | `WorldState` plus the jobs snapshot |
| `OnPlayerPresence` claiming a seat another player holds | `SeatRefused` (D17) |

Paths that stay without a reply, because the acting client already equals the server or another packet answers it:
details for a gone car, a dropped baseline, a removal of a UID nobody took, drive and visual rate limits.

## Save sections

| Key | Version | Owner | Content |
|---|---|---|---|
| `world`, `garage`, `inventory` | 1 | 7 | shared money/scrap/level/XP/skills, upgrades, inventory |
| `cars` | v1 (7) → v2 (1: loader entries, baseline, `EngineSwap`) → v3 (4: `Details`, no-op step) | 1 | 2 adds `Place`, `CarData` without a bump |
| `car-placement` | 1 | 2 | lifts, parking slots (blobs), levels |
| `workshop-tools` | 1 | 5a | slots, positions (reservations are runtime only) |
| `jobs` | 1 | 3 | orders, active jobs, `NextJobId`, missions |
| `shop-list` | 1 | 23 | the shared shopping list (`Entries`, `Revision`) |
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
| 420 | `shop-list` | 23 | 1 |
| 450 | `self` | 7 | 0/1 (`PlayerRestore`) |
| 500 | `players` | 6 | 1 (snapshot only) |

Rows 8, 9, 12, 14 and 14a add no save sections and no `SyncOrder` slots.

## Configuration

Server `server_config.ini` (missing keys take their defaults and are appended; existing on `main`: `max_players`,
`use_steam`, `GSLT_Token`, `log_level`, `port`):

| Keys (default) | Owner |
|---|---|
| `autosave_interval_seconds`, `backup_count`; `--command-file <path>` (supported for production too: row 8 stops a hosted server through it) | 7 |
| `server_name` (""), `public_address` (""); command line `--port`, `--max-players`, `--use-steam`, `--server-name`, `--public-address` | 8 part 1 |
| `password` (""), `password_steam` (False), `admin_key` (""), `new_session_difficulty` (Normal); `--password`, `--admin-key`, `--new-difficulty` | 8 part 2 |
| `game_version` (auto), `mods_required`, `mods_ignored`, `mods_gameplay` (empty); `Database/meta.json`, `Database/mod_rules.json` | 9 |
| `desync_check_interval_seconds` (5, 0 = off), `desync_autofix` (true) | 14 |
| `desync_resend_keys` (`world, inventory, cars, car-placement` plus the part 2 keys that `desync-soak` showed quiet; a confirmed mismatch of a key not listed is only logged, "log only"), `desync_stall_seconds` (120, min 5; a key not ready that long in a row is logged once and listed by `desync` and the bug report); console `desync interval <s>` (until the next restart) | 19 (part 2) |
| `perf_log_interval_seconds` (0 = off; > 0 writes `Log/perf_<start>.jsonl`) | 11 |
| `lock_scope` (`connected`; `part` = only the part and `car`), `lock_expiry_seconds` (90, min 5); server `--check-locks`, console command `locks` | 18 |

Client MelonPreferences — one scheme: category `CMS21Together` for everything, plus `CMS21Together_Guard` for the
guard. Key bindings end in `Hotkey` (the redaction rule skips them).

| Entry | Owner |
|---|---|
| `CMS21Together.PlayerName` | 6 |
| `CMS21Together.LastJoinTarget`, `DevHotkeys`, `AdvertisePresence`, `AdminKey` (secret), `ServerPath`, `SessionPanelHotkey` | 8 |
| `CMS21Together.EnableDevTools`, `DbExportHotkey` | 9 (part 2, exporter) |
| `CMS21Together.ResyncHotkey`, `BugReportHotkey` | 14 |
| `CMS21Together.RemoteVisuals` (default true) | 17 |
| `CMS21Together.PingHotkey` (default `Mouse2`, the middle mouse button) | 22 |
| `CMS21Together_Guard.Mode`, `Allow`, `Deny` | 14a |

Hotkeys (unique; row 14a's trace task 1.1 checks that the game binds none of F7–F9):

| Key | Action | Owner |
|---|---|---|
| F5 | join the last target, only with `DevHotkeys = true` (default false); F6 removed | 8 |
| F7 | resync (garage reload) | 14 |
| F8 | bug report | 14 |
| F9 | session panel | 8 |
| Mouse2 (middle mouse button) | ping the part or spot under the cursor (the game binds nothing to it; `ping` checks the Rewired maps) | 22 |
| unbound | database export (`EnableDevTools`) | 9 |

## Bug-report bundle (owner 14 (d); row 12's `Collect-Logs.ps1` produces the offline subset)

- Id `yyyyMMdd-HHmmss-<4 hex>`. In-game: client `UserData\CMS21Together\BugReports\<id>.zip` (root `client\`), server
  `<server>\BugReports\<id>.zip` (root `server\`), other in-session clients write theirs with the same id. Offline:
  `Desktop\CMS21Together-logs-<id>.zip` with both roots.
- `client\`: `info.json` (id, UTC time, mod/full version, game version, connection state, scene, slot/name; offline:
  `"offline"`), `MelonLoader\Latest.log` + the newest 5 `MelonLoader\Logs\*.log`, `MelonPreferences.cfg` reduced to the
  `CMS21Together*` categories, `UserData\CMS21Together\*.json` except `player.json`, `files.txt` (`Mods\`/`UserLibs\`
  with sizes); in-game only: `mods.json`, `guard.log`, `state\<key>.json`; offline only: the newest 3 in-game bundles
  as `client\BugReports\<id>.zip`.
- `server\`: `info.json`, `Log\Latest.txt` + the newest 5 `Log\Log_*.txt` (the server's folder is `Log\`, not `Logs\`),
  `server_config.ini`; in-game only: `save.json`, `state\`, `Log\desync\` (last hour), `players.json`; offline:
  `save.json` only with `-IncludeSave`, plus the newest 3 in-game bundles as `server\BugReports\<id>.zip`.
- Redaction: the value of every config or preference entry whose name contains `token`, `password`, `secret` or `key`
  (case-insensitive) becomes `<redacted>`, except names containing `Hotkey`; covers `GSLT_Token`, `password`,
  `admin_key`, `CMS21Together.AdminKey`. `player.json` is never included; every `save.json` drops `players[].Key`;
  `players.json` holds no identity keys.
- As written in code (Core `Diagnostics/Redaction`, `BugReportBundle`; the collector matches it): entry names use `/`;
  a redacted value keeps its quotes (`GSLT_Token = "<redacted>"`, `password = <redacted>`), an empty value stays empty;
  every known secret value of 4+ characters (config secrets, preference `AdminKey`, join passwords, `player.json`
  values) is also replaced by `<redacted>` in every text of the zip; `save.json` drops every secret-named field of the
  `players` section; files that cannot be read are listed in `<root>\errors.txt`; `state\<key>.json` holds
  `{hash, rows["Id|Field=Value"]}` (cars: one per loader id, `"not ready"` when the client cannot project it). The
  zip is written as `<id>.zip.tmp` and renamed, so an existing `<id>.zip` is complete.

## Harness

Verbs are globally unique (`Commands.Discover` throws on a duplicate). Existing: `ping`, `connect`, `disconnect`,
`dump`, `screenshot`, `quit`.

| Owner | Verbs |
|---|---|
| 7 | `stats-add`, `to-menu` (g1); `player-key`, `send-early-stats`, `profile-pref` (later groups) |
| 6 | `travel`, `scene-list`, `teleport`, `set-name`, `leave-mark`, `sit`, `stand`, `engine`, `seat-trace` (spike), `buy-car-here`, `junk-buy` |
| 1 | `car-spawn`, `car-delete`, `car-loaded`, `car-list`, `car-ready`, `car-baseline`, `car-hold-snapshot`, `car-dlc-cars`, `car-request`, `part-state`, `part-keys`, `part-unmount`, `part-fast-unmount`, `part-fast-mount`, `part-action-unmount`, `part-corrupt` (`part-claim` removed by 18: use `lock-take`), `part-hold-remote`, `crane-out` (gated since row 18: the engine group appears after the grant), `crane-group <loader>`, `crane-in`; playtest fixes: `part-twins`, `part-fast-mount <loader> <key> [itemUid]`, `wheel-parts`, `wheel-mount` |
| 2 | `lift`, `lifters`, `car-move`, `car-place`, `placement`, `net-hold` (`on`/`off`; `out` = full stall of both directions, heartbeats included, added by 11), `park`, `unpark`, `park-swap`, `parking`, `parking-unlock`, `park-incoming`, `dev-spawn`, `placement-trace` and `parking-probe` (spike) |
| 3 | `jobs-trace`, `orders-generate`, `orders-mission`, `orders-autogen`, `orders-list`, `order-slots`, `orders-accept`, `orders-decline`, `orders-reload`, `job-examine`, `job-check`, `job-finish`, `tutorial-run`, `job-spawn-unclaimed`, `job-end-dup` |
| 13 | `testdrive-trace`, `testdrive-go`, `testdrive-drive`, `testdrive-finish`, `testdrive-partnames`, `testdrive-hold`, `testdrive-skip-result`, `dyno-run`, `pathtest-run`, `diag-examine`, `away-try`; dump section `away`, car field `specialState`; scenarios `test-drive`, `test-drive-latejoin`, `diagnostics` (spikes: `test-drive-trace`, `diag-trace`, `departure-hold`) |
| 4 | `cardetails-probe`, `cardetails-roundtrip`, `cardetails-ui`, `cardetails-fluid`, `-wheel`, `-alignment`, `-headlamp`, `-gearbox`, `-tune`, `-paint`, `-tint`, `-wash`, `-plate`, `-mileage`, `-lights`, `-bonus`, `-randomize`, `-hold` (`cardetails-fluid`, `-wheel`, `-alignment` and `-wash` built by row 19 task 1.4, see row 19; `-gearbox` and `-bonus` were never built, row 25 adds `cardetails-gearbox`, `bonus-fit` and `bonus-remove`) |
| 5a | `tool-list`, `tool-trace` (5b adds hooks to it), `give-item`, `give-group`, `tool-put`, `tool-take`, `tool-hold`, `tool-local-put`, `tool-mount`, `tool-balance`, `tool-balance-open`, `tool-balance-cancel`, `tool-charger`, `tool-angle`, `tool-stand-part`, `tool-move`, `tool-repair`, `tool-paint-part` |
| 5b | `tool-engine-out`, `tool-engine-in`, `tool-paint-car`, `tool-use`, `tool-dyno` |
| 8 | `mp-ui`, `mp-status`, `mp-join`, `mp-join-string`, `mp-answer`, `mp-presence`, `mp-fake-version` (part 1); `mp-host`, `mp-players`, `mp-kick` (part 2); `connect` is rerouted through `JoinService` |
| 9 | `compat-report`, `compat-override` (no mod-version key: `mp-fake-version` covers it), `db-export` (part 2) |
| 12 | `build-info` |
| 14a | `guard-trace` (spike only), `guard-set`, `guard-allow`, `guard-try`, `guard-log`, `guard-rules` |
| 14 | `digest-show`, `inv-corrupt`, `digest-hold`, `resync [force]` (5a uses it instead of its former `tool-resync`), `bug-report [list]` (row 19 part 2: `digest-show` lists every key, `cars:0` and `car-details:0`; `digest-hold <key> notready` answers "not ready" for that key) |
| 11 | `perf` (frame time over 10 s, managed and IL2CPP heap, scene, `syncAcked`), `fps-cap <n>` |
| 17 | `vfx-trace`, `vfx-probe` (spike), `vfx-unscrew`, `vfx-tool`, `vfx-hold`, `vfx-enable`, `vfx-parts`, `vfx-switch`, `vfx-stand`; dump section `visuals`; part 2: `drive-trace`, `drive-pie`, `drive-probe`, `drive-input`, `drive-input-state`, `drive-stop`, `drive-history`, `drive-codec-check`, `drive-blob`, `drive-ghost-test`, `drive-start`; dump section `remoteCars` (`local`, `cars[]`) |
| 18 | `lock-take <loader> <kind> <key...> [bare] [items <uid...>] [release]` and `lock-take result <id>`, `lock-release`, `lock-renew on|off`, `lock-counters [reset]`, `lock-try <loader> unmount <key>|mount <key> [uid|group <uid...>]|body <index>|crane-out|fill <type> <id> [level <x>] [nocar]|drain <type> <id>|oil|lift <lifter> up|down [nogate]|move <place> [nogate] ... [finish|hold|release]` and `lock-try result <id>`, `lock-chooser <loader> <key> open|close`, `lock-hover <loader> <key>`, `lock-idle <bolt s> <chooser s>|default`, `lock-tracked`, `lock-watch <loader> <fluidKey>|report|off` (fluid level at each release), `lock-fluid <loader> <fluidKey>`, `lock-tool-end` (ends an active refill or extractor), `lock-reports [clear]` (every gate report: kind, result, `waitedMs`, prefetched), `lock-trace` (spike: `on|off|report|state|reinvoke|relations`), `lock-probe` (spike), `lock-click <loader> <key> hold <ms>` (input shim), `cardetails-flush <loader> [applying <ms>]`; dump section `locks` (`mirror`, `pending`, `counters`, `answers`, `lastMessage`); `lock-click status` reports `label`, `hoverFrames`, `partMouseOverFrames`; `LockSession.psm1` (`Get-ServerLocks`, `Request-Lock`, `Wait-LockMirror`) |
| 24 | `lock-preview <loader> arm|<key...>` (`arm` moves the car's unmounted parts from layer 28 to 26, as a player's game has them; per key `layer`, `collider`, `shown`, `message`), `lock-chooser-rows` (open chooser rows: `uid`, `locked`, `text`), `lock-chooser-pick <uid>` (the chooser's single-item accept: hide, then `SelectPartToMount`), `lock-pie <loader> open|state|close` (the car move submenu `!ChangeCarPosition` with the car pinned under the cursor; per option `enabled`, `available`, `source`, `reason`) |
| 19 | part 3: `inv-send <add|update|remove> <uid> [condition]` (a raw inventory packet for a local item; the condition is written into the local item first); part 1: `cardetails-fluid <loader> <type> <id> <level> [cond]`, `cardetails-wheel <loader> <index> <w> <rim> <tire> <et>`, `cardetails-alignment <loader> <FL> <FR> <RL> <RR>` (`-` leaves a field), `cardetails-wash <loader> <dust> <wash> [panelIndex]` (game setters plus `MarkDirty`), `part-condition <loader> <key> <value>` (a local condition edit), `part-domount <loader> <key> <itemUid>` (the game's `DoMount` with the item, then `ShowMounted`), `diag-examine <loader> <ToolType> keys` (the keys the tool examines, nothing examined), part 2: `cardetails-plate <loader> front|rear <texture>` (the game's plate texture change plus `MarkDirty`; plate textures are applied on receivers since part 2), dump `cars[].index` is the loader id (it was the `GameScript.carOnScene` index), `state-corrupt <car-details|workshop-tools|warehouse|garage> [loader]` (coolant level, a disc on the brake lathe, a disc in the warehouse, one barn more; changed locally without a packet), `net-hold status` (`holding`, `stalling`, `heldIn`, `heldOut`); `vfx-unscrew <loader> <key> status`, `give-item <id> [condition] [quality]`, `car-resync <loader>` (a raw `CarPartsResyncRequest`), `cardetails-pour <loader> <type> <id> <seconds>` and `cardetails-pour result` (`start`, `end`, `decreases`, `frames`), `vfx-parts <loader> all` (also parts with fewer than two bolts), `sell-item [uid]` (an item or a group), `item-where <uid>` (`inventory`, `warehouse`, `machine:<tool>` or `none`), `warehouse-move <uid> to|from`, spikes `part-records <loader> snap|diff <name>` and `cardetails-drift <loader> snap|diff <name>`; dump section `carDetails` (per Ready loader: `entries`, entry id → value rounded like `Signature`, sorted by id; ids `f:<type>.<id>`, `w:<i>`, `a:<field>`, `c:<panel>`, `t:<partKey>`, `t:gearbox`, `paint`, `plates`, `info`, `dyno`) and `parts` (`transactions`: open and committed part transactions per loader); server `cars` adds `part records: staleMerged, staleDropped, skippedNoMask`; `--check-merges` |
| 22 | `ping <loader> <key>` (no arguments: the status probe as before; with arguments: the part goes under the game's mouse-over and the hotkey's path runs), `ping-spot x,y,z`, `ping-burst <n> <loader> <key>` (raw packets past the client throttle), `ping-markers [clear]`, `input-bindings [binding]` (Rewired keyboard and mouse maps of every player); dump section `pings` (`hotkey`, `defaultHotkey`, `markers[]`, `counters`, `lastSent`) |
| 23 | `shoplist` (game list, server mirror, `outstanding`, `windowManagerSame`), `shoplist-add`, `shoplist-remove`, `shoplist-clear` (item arguments `<id> [tire\|rim] [width=] [size=] [profile=] [et=] [plate=] [bonus=]`); no dump section |
| 21 | `ride-state [driverId]` (phase, rides, camera `placed`/`toHead`/`maxDrift`/`customPos`, own track car `kinematic`/`inputsEnabled`/seats, drive capture, the copy's seats, avatars `toSeat`), `ride-probe` (spike: track camera, own car seats and head, copy seats and wheels); dump section `ride` |

| PowerShell helper / server command | Owner (first to land) |
|---|---|
| `Send-ServerCommand`, `Wait-ServerLog`, `Stop-TestServer`, `Start-TestServer`, `--command-file`, server `save` | 7 (task 1.1, 2.5) |
| `Wait-HarnessDump` | 6 (task 2.5) |
| `Wait-HarnessDumpsEqual` (built on `Wait-HarnessDump`) | 5a (task 5.3) |
| `Compare-HarnessDumps` | existing; sections added by 2 (`lifters`, `placement`, `parking`), 3 (`jobs`) |
| server commands `players` / `cars` / `placement` / `jobs` / `cardetails` | 7 / 1 / 2 / 3 / 4 |
| `Start-TestServer -Arguments` (optional, additive), `scenarios\<name>.launch.psd1` (per-instance launch arguments read by `Run-Session.ps1`) | 8 (tasks 2.3, 4.3) |
| harness status `Status.joinStatus`, `Status.lastDisconnect { reason, message }` (polled with `Wait-HarnessStatus`) | 8 (task 1.3); user 9 (replaces its former `session.lastError`) |
| `Run-All.ps1` skips scenarios with a `# run-all: skip` header line unless named in `-Scenarios` | 12 (task 2.5) |
| `tools/release/Build-Release.ps1`, `Install-ReleaseToTestEnv.ps1 -Lane`, `Collect-Logs.ps1` (+ `.bat`); `Deploy-Mod.ps1` removes release-only files | 12 |
| `tools/test-env/Compare-Database.ps1`, `tools/test-env/fixtures/mod-targets/` | 9 |
| server commands `password`, `serverinfo` (8); `compat` (9); `desync`, `bugreport` (14); `shoplist` (23); existing `kick`, `stop` (`kick` moves to `Server.Refuse`) | as listed |
| server command `jobs expire <id>` (expires an open order at once, as the tick does) | 19 (part 3) |
| server log lines `[Shop] Sale of …` and `[Inventory] Warehouse move of …` (the soak contention's server order) | 19 (part 2) |
| server command `away` also lists the rides | 21 |
| server command `perf` (`perf`, `perf top <n>`, `perf reset`), snapshot line `Client[n] snapshot <id> acked after …`; `tools/test-env/PerfSampler.psm1` (`Get-PerfSample`, `Add-PerfSample`, `Test-PerfWatchdog`, `Add-FrameSample`), `Show-SoakReport.ps1` | 11 |

Scenarios (unique): playtest fixes `car-wheel-swap`, `car-mount-race`; 7 `server-restart`, `profile-safety`, `rejoin`, `latejoin`, `persistence-restart`,
`duplicate-identity`; 6 `presence-latejoin`, `scenes`, `seat-engine`, `seat-engine-trace` (spike), `presence`, `purchases`; 1 `car-parts`, `car-parts-latejoin`;
2 `car-placement`, `car-placement-latejoin`, `car-parking-full`; 3 `jobs-trace`, `jobs`, `jobs-latejoin`,
`jobs-restart`; 4 `car-details`, `car-details-latejoin`; 5a `tools-slots`, `tools-race`, `tools-latejoin`;
5b `tools-car-effects`; 8 `join-ui`, `join-coldstart`, `host-from-game`, `session-admin`; 9 `compat-refusal`, `compat-mods-probe` (run-all: skip; needs real mods copied into A);
12 `release-smoke` (marked `# run-all: skip`, run after `Install-ReleaseToTestEnv.ps1`); 14a `guard`; 14
`desync-autofix`, `resync-key`, `bug-report`; 17 `visual-parts`, `visual-activity`, `visual-latejoin`, `visual-screens` (`# needs: graphics`, `# run-all: skip`), `visual-probe` (spike, `# run-all: skip`), `drive-track`, `drive-latejoin`, `drive-probe` (spike, `# run-all: skip`); 11 `scale-connect`, `soak`, `latejoin-full`, `storm` (all
`# run-all: lane 3`), `full-garage-fixture` and `perf-probe` (`# run-all: skip`); 22 `ping`; 23 `shopping-list`; 19
`server-answers` (part 3; `seat-engine` gains the seat race), `merges-probe` (part 1 spikes 1.2, 1.3 and the 1.4 setters, `# run-all: skip`), `car-stale-record`, `details-concurrent`, `tools-item-race`, `park-stale`, `car-snapshot-after-delete` (part 1; `car-gone-inflight` gains park and job end, `server-answers` the dropped transaction, `tools-race` two stand-part steps, `locks-fluid` two fills at once); 21 `ride-along`, `ride-probe` (spike, `# run-all: skip`); 24 `locks-select-2`; 20 `race-hardening`.

Scale lane and long runs (owner 11, design `multiplayer-soak-and-scale` D1-D9):

| Item | What |
|---|---|
| Lane 3 | A, B, C, D with `Server3` on port 7797 (`Test-ServerSaves.ps1` moved to 7807); memory gates per lane in `TestLanes.psm1` (22/6, 22/10, 44/16 GB commit/RAM) |
| Lane locks | `Global\CMS21TogetherLane1`/`Lane2`, held by `Run-Session.ps1` for the whole run (lane 3 takes 1 then 2, `-LaneWaitMinutes` 120); `Deploy-Mod.ps1` takes them and fails with "lane N is busy"; the launch mutex stays |
| `Run-Session.ps1` | `-ScenarioArgs <hashtable>` (splatted after `-Ctx`), `-Deploy [-NoBuild]` inside the locks, `-Headless C,D`; roles `A`-`D` in `.launch.psd1`; `$Ctx.Launch` (lane, window, sound, headless) for relaunches; log slices restart after a relaunch; `deployed.json` of every install and the server in `result.json` (warning on mixed builds) |
| `Deploy-Mod.ps1` | `-BuildOnly`, `-NoBuild`; writes `deployed.json` (repo, commit, dirty, time) into each `UserData\TestHarness` and the server folder. `Run-All.ps1` builds once and deploys per lane through `Run-Session -Deploy -NoBuild` |
| `Run-All.ps1 -Lanes 3` | runs only `# run-all: lane 3` scenarios and must be the only lane; lanes 1 and 2 skip them unless named; `-List` marks them |
| `TestLanes.psm1` | `Start-HarnessInstance -Lane -Instance [-Headless] [-NoWait]`, `Wait-HarnessInstances`, `Enter-LaneLocks`/`Exit-LaneLocks`, `Get-InstanceGameProcess`, `Get-LaneDeployedBuilds` |
| `HarnessClient.psm1` | `Wait-HarnessDumpsAllEqual -Instances -Sections -TimeoutSec` (N-way against the first), `Get-SharedDumpSections` (`stats`, `inventory`, `cars`, `carDetails` (row 19 part 2), `placement`, `jobs`, `tools`, `toolPositions`), `Get-LastHarnessDumps` |
| `ScaleSession.psm1` | connect with `DuplicateIdentity` retry, server config per run, `Invoke-ScaleCheckpoint` (quiesced N-way equality + forced `desync check` that expects every digest key and `cars:L`/`car-details:L` of every loaded car, `checkpoints.jsonl`), `Get-ServerPlayerRecords`, log error scan with `scenarios\soak-allow.txt` |
| `SoakContention.ps1` (row 19 part 2, dot-sourced by `soak`) | `soak -Contention [-ContentionWeight 15] [-ContentionKinds a,b]`: contention groups (kinds of design D13, first pass), `contention.jsonl`, markers `contend-start` (spec, release order) and `contend` (observed server order, verdict); rule 8 (item conservation) and rule 9 (outcome); `scenarios\soak-contention-known.txt` (`kind | gap | closing row`, reported as "known gap"); `-Replay` reruns each group and reports whether the server order was reproduced; known kinds stay out of the random draw unless `-ContentionIncludeKnown`; a group holds at most 7 s (the server drops a client after 10 s without a heartbeat). Soak rule 2 also fails a key that is not ready at two checkpoints in a row |
| `StormKinds.psm1` | storm kinds K1-K8 (`storms.jsonl`), used by `storm` and `soak -StormEveryMinutes` |
| `FullGarage.psm1` | the full-garage fill and its fixture `CMS21-TestInstallsixturesull-garage_L<levels>_<tag>.json` (outside the repo) |
| `Run-Soak.ps1` | `-Hours -Seed -Lane -Headless -StormEveryMinutes -Replay -Now`, contention on by default (`-NoContention`, `-ContentionKinds`); waits for an idle PC, then `Show-SoakReport.ps1` (rules 8 and 9 from `contention.jsonl`; rule 2 includes silent stalls) |

## Test areas

Policy (user, 2026-10-07): a change merges after the scenarios of its areas plus the smoke set, not a full
regression; the full set runs when a change touches mod code the path table cannot place or harness core, and
before a release. Every scenario, skipped ones included, carries a `# areas: a, b` header line (vocabulary and the
path → area table in `tools/test-env/TestAreas.psm1`: `connect`, `presence`, `guard`, `cars`, `parts`,
`placement`, `details`, `jobs`, `economy`, `tools`, `testdrive`, `persistence`, `resync`, `hosting`, `bugreport`,
`release`, `visuals`, `driving` (row 17), `outdoor` (row 15), `locks` (row 18), `ping` (row 22) and `shoplist` (row 23)); `smoke` in the list puts it in the smoke set (`latejoin`, `car-live`, `junkyard-trip`, `guard`,
`tools-latejoin`). A `# run-all: lane 3` line makes a scenario a scale-lane scenario (`Run-All -Lanes 3` only).
**A new scenario must carry `# areas:`**; a new source folder needs a row in the table, or its
changes run the full set.

| `Run-All.ps1` switch | Runs |
|---|---|
| `-Changed [<ref>]` | files changed since the merge base with `<ref>` (default `origin/main`, plus uncommitted and untracked files) mapped to areas, plus the smoke set; a changed scenario adds only itself; docs alone run nothing |
| `-Areas a,b` | scenarios with any of these areas, plus the smoke set (`-NoSmoke` leaves it out) |
| `-Smoke` | the smoke set |
| `-List` | prints the changed files with their areas and each selected scenario with its reason, then exits |

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

## Settled in the second pass (rows 8, 9, 12, 14, 14a)

- Row 14's guard (a) is its own change `multiplayer-guard` (row 14a, M1); row 14 keeps (b)–(d), including the bug
  report (not split further), with design decisions renumbered D1–D5 and task groups 1–4.
- `DisconnectReason`: 8 appends `ServerFull`, `WrongPassword`; 9 then `GameVersionMismatch`, `DlcMismatch`,
  `ModMismatch`; 12, 14, 14a none.
- Refusals: one helper `Server.Refuse` (row 8); refusal display and `Status.lastDisconnect` are row 8's, row 9 uses
  them (`session.lastError` dropped).
- Hotkeys F7 resync, F8 bug report (both 14), F9 session panel (8); preferences in category `CMS21Together` (+
  `CMS21Together_Guard`), key bindings named `*Hotkey`.
- One bug-report layout and redaction rule (above) for row 14 (d) and row 12's collector, including `admin_key`,
  `CMS21Together.AdminKey` and `players[].Key`; the server log folder is `Log\`.
- `release-smoke` is excluded from `Run-All` by a marker line.
- Row 5a's `tool-resync` (in-place `AskForSync`) is replaced by row 14's `resync force` (garage reload).

## DLC content (user decision 2026-10-06, row 9)

Players may join with different DLC sets; row 9 never refuses for DLC and only tracks the shared DLC set (DLC owned
by every connected player, `SharedDlc` on the server, `ServerInfo.SharedDlc` on clients). DLC content not in that set
must be blocked from shared use by the rows that own it — each adds this as a requirement and a guard entry when it
lands:

- Row 1 (`sync-car-parts`): spawning or mounting a DLC car or part (`PartProperty.DLC`) outside the set.
- Row 2 (`sync-car-placement-and-lifts`): parking, unparking or moving a DLC car outside the set.
- Row 5a (`sync-workshop-machines`): putting DLC tools or DLC parts on shared machines outside the set.
- Shared inventory (rows 1 and 5a, whoever touches it first): DLC items outside the set stay out of shared inventory.

When a player without a DLC joins while DLC content is already in shared use, the owning row decides what that player
sees (hidden, kept parked, or a guard message); rows that land before this is decided keep DLC content blocked.

## Not resolved here (recommendations)

1. ~~Row 13 is not drafted~~ — merged 2026-10-06: the away claim, test drive fold, dyno details section and test path
   `specialState` are on `main`; 5b's dyno trigger calls `DynoSync.Commit`.
2. **Steam stats for non-finishers** depend on row 3's trace finding a callable game entry point for the job's
   stats/achievements. Recommendation: if none exists, accept "finisher only" as a known gap rather than calling
   Steamworks directly (the harness instances share one Steam account and cannot verify it).
3. **Balancer reservation entry points** (minigame open, take) are only known after row 5a's manual trace (task
   1.3, needs the user). Recommendation: schedule that session before row 5a group 7.
4. **Interim client-computed values** (spawner's roll in row 1, order generation and payout in row 3) are marked as
   interim; ROADMAP row 16 (`server-game-logic`, maintained by the coordinator) replaces them.
5. **Digests for rows 3 (`jobs`), 4 (`car-details:<loader>`) and 5a (`workshop-tools`)** are not in their drafts.
   Recommendation: each adds one task when it lands after row 14 (b) ("register an `IClientDigest`/`IServerDigest`
   with a Core mapper for your DTO; `desync-autofix`-style check"); until then drift there is fixed only by the
   resync key.
6. **Row 7 follow-ups from rows 8/9/14a** (row 7's draft not edited here): task 5.3 calls row 8's
   `ConnectionStatus.Disconnected(reason)` instead of its own menu message; `--command-file` is a supported option; if
   row 14a's audit shows `GarageLoader.Save(bool)` bypasses `GameDataManager.Save(int)`, row 7 blocks it too.
7. **Row 9's milestone split** (exporter with row 16 in M3, tuning in M6) keeps row 9 open until M6 — a user question
   (move the exporter into row 16 and the tuning into a follow-up change?).
