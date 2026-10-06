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
| `CarPartsChange`, `CarPartsChangeResult` (renamed in place), `CarPartClaim`, `CarPartClaimUpdate`, `CarPartsSnapshot`, `CarPartsResyncRequest`, `CarSpawnAck`; `CarSpawnResponse.SpawnSeq` | both | 1 | 2 (unpark ack), 4 (`SpawnSeq`), 5a (`CarSubPartUpdatePacket` record) |
| `LifterActionRequest`, `LifterState`, `CarPlaceChangeRequest`, `CarPlaceChanged`, `CarUnparkRequest`, `ParkingMoveRequest`, `ParkingSlotUpdate`, `ParkingState`, `ParkingLevelUnlockRequest`, `ParkingResyncRequest` | both | 2 | — |
| `CarParkRequest { RequestId, CarLoaderID, PreferredSlot, Car, Price }` → `CarParkResult { RequestId, Accepted, Reason (ParkingFull, NoMoney, Invalid), Slot }` | C→S / S→requester | 2 | 6 (purchases, `CarLoaderID = -1`); no `CarPurchase*` packets exist |
| `CarSpawnResponse` + `CarData`, `CarDataVersion` (+ `Place`) | S→C | 2 | 1's snapshot spawn info carries them |
| `JobsState`, `OrderGeneratorRole`, `OrderGenerated`, `OrderAdded`, `OrderAction`, `OrderActionResult`, `JobStarted`, `JobProgress`, `JobEndRequest`, `JobRemoved` (+ `IsCompleted`) | both | 3 | `CarSpawnRequest.IsJob/JobID` validated |
| `CarDetailsUpdate`, `CarDetailsRequest` | both | 4 | 3 and 5b trigger them only through 4's client API |
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

Rows 12 and 14a add no packets.

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
| Core | `Diagnostics/Redaction` (shared redaction rule, below) | 14 | 12 (`Collect-Logs.ps1` mirrors it) |

Prefix rule on guarded game methods: row 14a's prefixes run at `Priority.First`; every other prefix of this mod on the
same method (row 6's `SceneHooks` on `NotificationCenter.SelectSceneToLoad`, formerly `DisconnectHooks`) takes
`bool __runOriginal` and does nothing when it is false.

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

Client MelonPreferences — one scheme: category `CMS21Together` for everything, plus `CMS21Together_Guard` for the
guard. Key bindings end in `Hotkey` (the redaction rule skips them).

| Entry | Owner |
|---|---|
| `CMS21Together.PlayerName` | 6 |
| `CMS21Together.LastJoinTarget`, `DevHotkeys`, `AdvertisePresence`, `AdminKey` (secret), `ServerPath`, `SessionPanelHotkey` | 8 |
| `CMS21Together.EnableDevTools`, `DbExportHotkey` | 9 (part 2, exporter) |
| `CMS21Together.ResyncHotkey`, `BugReportHotkey` | 14 |
| `CMS21Together_Guard.Mode`, `Allow`, `Deny` | 14a |

Hotkeys (unique; row 14a's trace task 1.1 checks that the game binds none of F7–F9):

| Key | Action | Owner |
|---|---|---|
| F5 | join the last target, only with `DevHotkeys = true` (default false); F6 removed | 8 |
| F7 | resync (garage reload) | 14 |
| F8 | bug report | 14 |
| F9 | session panel | 8 |
| unbound | database export (`EnableDevTools`) | 9 |

## Bug-report bundle (owner 14 (d); row 12's `Collect-Logs.ps1` produces the offline subset)

- Id `yyyyMMdd-HHmmss-<4 hex>`. In-game: client `UserData\CMS21Together\BugReports\<id>.zip` (root `client\`), server
  `<server>\BugReports\<id>.zip` (root `server\`), other in-session clients write theirs with the same id. Offline:
  `Desktop\CMS21Together-logs-<id>.zip` with both roots.
- `client\`: `info.json` (id, UTC time, mod/full version, game version, connection state, scene, slot/name; offline:
  `"offline"`), `MelonLoader\Latest.log` + the newest 5 `MelonLoader\Logs\*.log`, `MelonPreferences.cfg` reduced to the
  `CMS21Together*` categories, `UserData\CMS21Together\*.json` except `player.json`, `files.txt` (`Mods\`/`UserLibs\`
  with sizes); in-game only: `mods.json`, `guard.log`, `state\<key>.json`; offline only: the newest 3 in-game bundles.
- `server\`: `info.json`, `Log\Latest.txt` + the newest 5 `Log\Log_*.txt` (the server's folder is `Log\`, not `Logs\`),
  `server_config.ini`; in-game only: `save.json`, `state\`, `Log\desync\` (last hour), `players.json`; offline:
  `save.json` only with `-IncludeSave`, plus the newest 3 in-game bundles.
- Redaction: the value of every config or preference entry whose name contains `token`, `password`, `secret` or `key`
  (case-insensitive) becomes `<redacted>`, except names containing `Hotkey`; covers `GSLT_Token`, `password`,
  `admin_key`, `CMS21Together.AdminKey`. `player.json` is never included; every `save.json` drops `players[].Key`;
  `players.json` holds no identity keys.

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
| 5a | `tool-list`, `tool-trace` (5b adds hooks to it), `give-item`, `give-group`, `tool-put`, `tool-take`, `tool-hold`, `tool-local-put`, `tool-mount`, `tool-balance`, `tool-balance-open`, `tool-balance-cancel`, `tool-charger`, `tool-angle`, `tool-stand-part`, `tool-move`, `tool-repair`, `tool-paint-part` |
| 5b | `tool-engine-out`, `tool-engine-in`, `tool-paint-car`, `tool-use`, `tool-dyno` |
| 8 | `mp-ui`, `mp-status`, `mp-join`, `mp-join-string`, `mp-answer`, `mp-presence`, `mp-fake-version` (part 1); `mp-host`, `mp-players`, `mp-kick` (part 2); `connect` is rerouted through `JoinService` |
| 9 | `compat-report`, `compat-override` (no mod-version key: `mp-fake-version` covers it), `db-export` (part 2) |
| 12 | `build-info` |
| 14a | `guard-trace` (spike only), `guard-set`, `guard-allow`, `guard-try`, `guard-log`, `guard-rules` |
| 14 | `digest-show`, `inv-corrupt`, `digest-hold`, `resync [force]` (5a uses it instead of its former `tool-resync`), `bug-report` |

| PowerShell helper / server command | Owner (first to land) |
|---|---|
| `Send-ServerCommand`, `Wait-ServerLog`, `Stop-TestServer`, `Start-TestServer`, `--command-file`, server `save` | 7 (task 1.1, 2.5) |
| `Wait-HarnessDump` | 6 (task 2.5) |
| `Wait-HarnessDumpsEqual` (built on `Wait-HarnessDump`) | 5a (task 5.3) |
| `Compare-HarnessDumps` | existing; sections added by 2 (`lifters`, `placement`, `parking`), 3 (`jobs`) |
| server commands `players` / `cars` / `placement` / `jobs` / `cardetails` | 7 / 1 / 2 / 3 / 4 |
| `Start-TestServer -Arguments` (optional, additive), `scenarios\<name>.launch.psd1` (per-instance launch arguments read by `Run-Session.ps1`) | 8 (tasks 2.3, 4.3) |
| harness status `Status.joinStatus`, `Status.lastDisconnect { reason, message }` (polled with `Wait-HarnessStatus`) | 8 (task 1.3); user 9 (replaces its former `session.lastError`) |
| `Run-All.ps1` skips scenarios whose first line is `# run-all: skip` unless named in `-Scenarios` | 12 (task 2.5) |
| `tools/release/Build-Release.ps1`, `Install-ReleaseToTestEnv.ps1 -Lane`, `Collect-Logs.ps1` (+ `.bat`); `Deploy-Mod.ps1` removes release-only files | 12 |
| `tools/test-env/Compare-Database.ps1`, `tools/test-env/fixtures/mod-targets/` | 9 |
| server commands `password`, `serverinfo` (8); `compat`, temporary `kick-test` (9); `desync`, `bugreport` (14); existing `kick`, `stop` (`kick` moves to `Server.Refuse`) | as listed |

Scenarios (unique): 7 `server-restart`, `profile-safety`, `rejoin`, `latejoin`, `persistence-restart`,
`duplicate-identity`; 6 `presence-latejoin`, `scenes`, `presence`, `purchases`; 1 `car-parts`, `car-parts-latejoin`;
2 `car-placement`, `car-placement-latejoin`, `car-parking-full`; 3 `jobs-trace`, `jobs`, `jobs-latejoin`,
`jobs-restart`; 4 `car-details`, `car-details-latejoin`; 5a `tools-slots`, `tools-race`, `tools-latejoin`;
5b `tools-car-effects`; 8 `join-ui`, `join-coldstart`, `host-from-game`, `session-admin`; 9 `compat-refusal`;
12 `release-smoke` (marked `# run-all: skip`, run after `Install-ReleaseToTestEnv.ps1`); 14a `guard`; 14
`desync-autofix`, `resync-key`, `bug-report`.

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

1. **Row 13 is not drafted**, but rows 6 (`LeavingScene`), 5b (dyno trigger) and 4 (dyno non-goal) depend on it.
   Recommendation: draft row 13 before M3 starts; until then 5b's group 10 stays parked.
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
