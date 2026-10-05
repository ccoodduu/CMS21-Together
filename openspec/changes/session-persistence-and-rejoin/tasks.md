# Tasks

**Contract (now): groups 1–2.** They land before every other change, change no gameplay, and end with a passing
two-instance scenario. **Rest (last): groups 3–7**, after rows 1–6.

## 1. Contract — harness support

- [ ] 1.1 `Run-Session.ps1`: back up `%USERPROFILE%\CMS21-TestInstalls\Server\Saves` and `server_config.ini` before the run and restore them afterwards; start the server with `--command-file <run>\server_commands.txt`; in `finally` stop every `CMS21_Together_Server` process. `HarnessClient.psm1`: `Send-ServerCommand <line>`, `Wait-ServerLog <regex>`, `Stop-TestServer` (kill), `Start-TestServer`. Verify `connect` passes and the server's `Saves` folder is byte-identical before and after the run.
- [ ] 1.2 `tools/TestHarness/Features/SessionCommands.cs`: `stats-add <scrap> <exp>` (sends a real `StatsActionPacket`), `to-menu [save]` (`NotificationCenter.SelectSceneToLoad("Menu", SceneType.Menu, true, <save>)`, default `false`); `StateDump.Status` adds `snapshotId` and `syncAcked`. Verify each command replies in a `-KeepRunning` session.

## 2. Contract — code

- [ ] 2.1 `Core/Data/SyncOrder.cs` with the keys and values of design D2; verify `dotnet build CMS21-Together.sln -c Release`.
- [ ] 2.2 Append `SyncBegin`, `SyncAck` to `PacketTypes`; add `SyncBegin { snapshotId }`, `SyncAck { snapshotId }`, and `snapshotId` + `Dictionary<string,int> Items` on `SyncEnd`; verify build and the packet count logged by `PacketRouter.Initialize`.
- [ ] 2.3 Server `Data/Persistence/`: `ISaveSection`, `ISnapshotProvider`, `[SessionSection]`, `SessionRegistry.Initialize(Assembly)` (reflection; duplicate key stops the server); verify the server logs the discovered keys at start.
- [ ] 2.4 `GameDataManager.StateLock` held per design D3 (`Tcp.HandleData`, `Udp.HandleData`, `SteamTransport.OnMessage` dispatch; `CommandSystem.Execute`; `Client.Disconnect`; `Client.Update`; save build); `GameDataManager.RequestSave()` and saves only from `ServerWindow.TickServer`; verify `connect` passes.
- [ ] 2.5 Save/load through sections (D1, D5): `world`, `garage`, `inventory` (row 7) and `cars` v1 (wraps today's `CarState`) with `Reset()` = today's `CreateNewSession` defaults; envelope write over today's write path; v1 detection + migration with `backups/premigration_v1_<ts>.json`; unknown sections kept and written back; `save` command; `--command-file` polling in `TickServer`. Verify: the test server's existing v1 save is migrated with a pre-migration copy, money/level/inventory unchanged in the server log, and a hand-added `"zzz-test"` section survives a load + `save` with a warning.
- [ ] 2.6 Server pipeline (D4): `OnAskForSync` under `StateLock` sends `SyncBegin`, every provider by `SyncOrder`, `SyncEnd{snapshotId, Items}`; `world`/`garage`/`inventory` become providers; `Client.SyncState` (`Connected, Syncing, InSession`); `SendToClients` skips `Connected`; `SyncAck` sets `InSession` and logs "joined"; verify the server log lists the keys in order during `connect`.
- [ ] 2.7 Client `SyncTracker`: reset on `SyncBegin`, `InSnapshot` true from `SyncBegin` until `SyncEnd` of the current `snapshotId` (row 6's garage gate uses it), `Applied(key)` from the world/garage/inventory handlers, `SyncAck` + `IsInitialSyncFinished = true` once `SyncEnd` arrived and counts are met, other `snapshotId`s ignored; replace the absolute timeouts in `LoaderAddition.CustomLoad` and `WorldStatesPackets.WaitForSyncCompletion` with a 30 s no-progress timeout that logs expected vs applied per key, disconnects and loads the menu with `saveGame = false`; verify `syncAcked` is true in `connect`.
- [ ] 2.8 `CMS21-Together-Server/Data/Persistence/README.md`: the interfaces, the `SyncOrder` table, what an item is, when to bump a section version, the lock rules; verify it matches design D2–D3.
- [ ] 2.9 `scenarios/server-restart.ps1` (two instances): A connects, `stats-add 7 300`, `Send-ServerCommand save`, dump A; `Stop-TestServer`; A `to-menu`; `Start-TestServer`; A connects, then B connects; verify A's and B's `stats` and `inventory` equal A's dump before the kill, both `syncAcked`, and the server log shows the migration copy only on the first start. `connect` still passes.

## 3. Save robustness (server)

- [ ] 3.1 Crash-safe `SaveSession()` per D6 (hash of `Sections`, `.tmp` + `Flush(true)`, rotation in `Saves/backups/`, `File.Replace` with 3 retries); verify two saves without changes write once, and 10 × `Stop-TestServer` during autosave with `autosave_interval_seconds = 1` each restart loads.
- [ ] 3.2 `autosave_interval_seconds` (300, 0 = off) and `backup_count` (5) in `ServerConfig` and the default file; verify with 10 s that files are written only when state changed.
- [ ] 3.3 Start copy `backups/start_<ts>.json`, keep 3; verify four restarts leave the three newest.
- [ ] 3.4 Load per D7 before `Server.Start` in `Program.Main`: candidate order, newer-version refusal, quarantine to `Saves/corrupt/`, resave after a fallback, refusal when files exist but none load; verify with prepared files: truncated main + valid bak1 → loads bak1 and quarantines main; all garbage → refuses and accepts no connection; `SaveVersion: 99` → refuses, no file moved or written.
- [ ] 3.5 `--check-save <file>` before Terminal.Gui init; commit a v1 save from the current build (with a car and inventory) as `tools/test-env/fixtures/server_save_v1.json`; verify exit 0 with the fixture's money, level, inventory and car counts, exit 1 on garbage.
- [ ] 3.6 `DisconnectReason reason` on `DisconnectPacket` (`None, ServerShutdown, Kicked, VersionMismatch, DuplicateIdentity, MissingIdentity, SyncFailed`); triggers per D6: `/stop` saves then sends `ServerShutdown`, `RequestSave()` after a leave or timeout, `SetConsoleCtrlHandler` save on window close; verify each by the save log line and the file timestamp.

## 4. Identity and player records

- [ ] 4.1 Client: create/read `UserData/CMS21Together/player.json`, send `ConnectPacket.playerKey` from `AuthHandler.HandleConnect`; harness `player-key <key>` (in-memory override); `Setup-TestInstalls.ps1` never copies `UserData/CMS21Together`; verify the file is created once and the server logs the same key on two connects.
- [ ] 4.2 Server: set `Client.SteamID` in `SteamTransport.OnConnectionChanged`; in `AuthHandler.OnConnected` resolve `steam:`/`guid:`, reject `MissingIdentity` and `DuplicateIdentity`, bind `Client.Identity`, create/update the `PlayerRecord` (name, last seen) in section `players` v1; `/players`; verify B with `player-key` = A's key is rejected while A stays connected.
- [ ] 4.3 Copy position/rotation/scene into the record on leave and before every save; clear the slot's live state in `Client.Disconnect`; verify the record after A leaves matches A's dumped position.
- [ ] 4.4 Provider `self` (450) + `PlayerRestore` packet; client applies it after the garage load (D8), counts it, and row 6's spawn placement skips; reuse row 6's harness `teleport` verb (task 2.4 there; verbs must be unique); verify A's position after leave + rejoin is within 0.5 m.

## 5. Join hardening and server loss

- [ ] 5.1 Server drops packets without `[AllowBeforeSync]` from clients not `InSession` (D9); harness `send-early-stats`; verify a dropped-packet warning and no state change.
- [ ] 5.2 Client: every send path that checks `ClientData.IsServerUpdating` also checks `IsInitialSyncFinished`; verify no dropped-packet warnings in the server log during `connect`.
- [ ] 5.3 Client server-loss detection (D10) and reason messages; verify with `Stop-TestServer` that a client in the garage reaches the menu within 12 s.

## 6. Client save safety

- [ ] 6.1 Find, read-only, where `RDGPlayerPrefs` stores `selectedProfile` (registry or the file behind `CheckFile()`); harness `profile-pref` reads it; note it in `CMS21-Together-Client/Persistence/README.md`; verify `profile-pref` matches the profile the menu shows as selected.
- [ ] 6.2 `SessionGuard` + prefixes on `GameDataManager.Save(int)`, `ProfileManager.Save/BackupSave/DeleteProfile/DeleteSelectedProfile`, `PlatformManager.DeleteSave(string)`; verify the client log shows a block on `to-menu save` and no `profile4.cms21b` appears.
- [ ] 6.3 Log when the game reads `selectedProfile` (prefix on `RDGPlayerPrefs.GetInt` for that key) from start to garage; then implement D11's profile slot handling: `session.json`, pref restore after `ProfileManager.Load()` (or no pref write at all if `StartGame` works without it), `SaveUtils.RestoreProfileDataSize` on Menu loaded and `OnApplicationQuit`, crash recovery in a `ProfileManager.Awake()` prefix; verify `profile-pref` is unchanged during and after a session and after killing the game mid-session and starting it again.
- [ ] 6.4 Profile backup to `UserData/CMS21Together/ProfileBackups/<ts>/` before the first `StartGame` of a run (keep 5, abort the join if it fails); verify the folder exists before the garage loads and the oldest goes on the sixth run.
- [ ] 6.5 Never delete `profile4.cms21b`, only log it; verify with `rg "File.Delete|DeleteSave" CMS21-Together-Client` that no mod code deletes in the save folder.

## 7. End-to-end scenarios (two instances)

- [ ] 7.1 `scenarios/profile-safety.ps1`: hash every file in the game's save folder and read `profile-pref` before launch; A and B connect, `to-menu save`, reconnect, quit; verify identical hashes, no added files, unchanged pref, a `ProfileBackups` folder in each install.
- [ ] 7.2 `scenarios/rejoin.ps1`: A and B connect; A `stats-add 7 300` and `teleport`; A `to-menu`; A reconnects; verify A's position is restored and `stats` (scrap, exp, level shared) and `inventory` are equal on A and B.
- [ ] 7.3 `scenarios/latejoin.ps1`: A connects and changes state (`stats-add`, plus car spawn and part change once rows 1–2 are merged); B connects; verify B's `stats`, `inventory`, `cars` equal A's, both see each other, both `syncAcked`, no dropped-packet warnings.
- [ ] 7.4 `scenarios/persistence-restart.ps1`: A and B connect, `stats-add`; B leaves; `Stop-TestServer` with A connected → A reaches the menu; `Start-TestServer`; both reconnect; verify state and A's position equal the values before the kill; then `Send-ServerCommand stop` with both connected → both reach the menu with the shutdown reason and the save is newer; `--check-save` on the v1 fixture exits 0.
- [ ] 7.5 `scenarios/duplicate-identity.ps1`: A connects; B `player-key` = A's key and connects; verify B shows the duplicate reason in the menu and A stays in session.
