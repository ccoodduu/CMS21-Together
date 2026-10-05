# Tasks

## 1. Harness infrastructure (used by every later group)

- [ ] 1.1 `Setup-TestInstalls.ps1`: never copy `UserData/CMS21Together/player.json` (or `session.json`, `ProfileBackups/`) into the installs; verify a fresh setup has no `UserData/CMS21Together` folder in A or B.
- [ ] 1.2 `Run-Session.ps1`: back up and restore `%USERPROFILE%\CMS21-TestInstalls\Server\Saves` and `server_config.ini` around a run and kill every `CMS21_Together_Server` process in `finally`; `HarnessClient.psm1`: `Start-TestServer`, `Stop-TestServer -Kill|-Graceful` (`-Graceful` = `CloseMainWindow()` on the server process, which must reach the `Program.Exit`/`ProcessExit` save) and `Get-ServerSave` (parsed `server_save.json`); verify `connect` still passes and the server's `Saves` folder is identical before and after the run.
- [ ] 1.3 `tools/TestHarness/Features/PersistenceCommands.cs`: `persist-mark <scrap> <exp>` (sends a real `StatsActionPacket`), `teleport <x> <y> <z>`, `to-menu` (exit to menu through the game's pause-menu path with its save flag, to exercise the guard), `profile-pref` (reads `selectedProfile` read-only); verify each command replies `ok` in a manual `-KeepRunning` session.
- [ ] 1.4 `StateDump`: split `stats` into `shared` (money, scrap) and `self` (level, exp, skills, local position/rotation); `Compare-HarnessDumps` compares `shared`, `inventory`, `cars` by default; verify `connect` still passes.

## 2. Core contract (can land before row 1, no behaviour change)

- [ ] 2.1 Add `Core/Data/SyncOrder.cs` with the keys and order values from design D2 (`world` 0 … `players` 500); verify the solution builds (`dotnet build CMS21-Together.sln -c Release`).
- [ ] 2.2 Add `Core/Data/PlayerRecord.cs` (`Key, Name, LastSeenUtc, Level, Exp, Skills, Position, Rotation, Scene, Extra`) and `PlayersState { NewPlayerTemplate, Records }`, referenced from `ModGameState.Players`; verify build.
- [ ] 2.3 Append `SyncBegin`, `SyncAck`, `PlayerRestore` to the end of `PacketTypes`; add `SyncBegin { snapshotId, Dictionary<string,int> items }`, `SyncAck { snapshotId }`, `PlayerRestore { Position, Rotation }`, `snapshotId` on `SyncEnd`, `playerKey` on `ConnectPacket`, `DisconnectReason reason` (`None, ServerShutdown, Kicked, VersionMismatch, DuplicateIdentity, MissingIdentity, SyncFailed`) on `DisconnectPacket`; verify build and the packet count logged by `PacketRouter.Initialize` on server start.
- [ ] 2.4 Add server `Data/Persistence/ISaveSection.cs`, `ISnapshotProvider.cs`, `[SessionSection]` and `SessionRegistry.Initialize(Assembly)` (reflection discovery; a duplicate key stops the server at start); verify the server logs the discovered keys at start.
- [ ] 2.5 Add `GameDataManager.StateLock` and hold it around `PacketRouter.Dispatch` in `Tcp.HandleData`, the UDP receive path and `SteamTransport.OnMessage`, and around `CommandSystem.Execute`; verify `connect` still passes.
- [ ] 2.6 Document the contract (interfaces, `SyncOrder` table, `PlayerRecord.Extra`, how items are counted for `SyncBegin`) in `CMS21-Together-Server/Data/Persistence/README.md`; verify the file exists and matches design D2.

## 3. Save sections for existing state

- [ ] 3.1 Implement sections `world` (Gamemode, Money, Scraps), `garage` (GarageUpgradeLevels), `inventory` (InventoryState) and `players` (PlayersState), all `Version = 1`, with `Reset()` reproducing today's `CreateNewSession` defaults (the level-8 values become `NewPlayerTemplate`); verify with `Get-ServerSave` that a fresh server writes all four.
- [ ] 3.2 Wrap the existing `CarState` as section `cars` v1 (shape unchanged; row 1 owns later versions); verify a car spawned in a `-KeepRunning` session is in the saved `cars` section after `/save`.

## 4. Save file, backups, load and migration (server)

- [ ] 4.1 Rewrite `GameDataManager.SaveSession()` per D3: envelope built under `StateLock`, hash skip, `.tmp` + `Flush(true)`, rotation in `Saves/backups/`, `File.Replace`; verify two saves without changes write once (log says skipped), and 10 × `Stop-TestServer -Kill` during autosave with `autosave_interval_seconds = 1` each restart loads.
- [ ] 4.2 Add `autosave_interval_seconds` (default 300, 0 = off) and `backup_count` (default 5) to `ServerConfig` and the default file, used by `ServerWindow.TickServer()`; verify with 10 s that saves appear only when state changed.
- [ ] 4.3 Start copy `backups/start_<ts>.json` before loading, keep 3; verify four restarts leave the three newest.
- [ ] 4.4 Load per D4: candidate order, version detection (no `SaveVersion` + `WorldState` = v1), newer-version refusal, section-by-section migrate + load, quarantine to `Saves/corrupt/`, resave when a backup was used, refusal when files exist but none load; move `TryLoadSession` before `Server.Start` in `Program.Main`; verify with prepared files: truncated main + valid bak1 → loads bak1 and quarantines main; all garbage → refuses to start and accepts no connection; `SaveVersion: 99` → refuses, no file moved or written.
- [ ] 4.5 v1 → v2 migration (D5) with `backups/premigration_v1_<ts>.json`; capture today's `server_save.json` from the current build (with a car and inventory) as `tools/test-env/fixtures/server_save_v1.json`; verify `--check-save` on it reports the fixture's money, inventory count, car count and template level.
- [ ] 4.6 Keep unknown sections as raw JSON and write them back; verify a hand-added `"zzz-test"` section survives a load + save with a warning in the log.
- [ ] 4.7 `--check-save <file>` in `Program.Main` before Terminal.Gui init (per-section summary, exit 0/1); verify exit 0 on the fixture and 1 on a garbage file.
- [ ] 4.8 Save triggers: `/save`; `/stop` and `Program.Exit` save before `Server.Stop()` (which now sends `reason = ServerShutdown`); `AppDomain.ProcessExit` and `Console.CancelKeyPress` best effort; save after a player leaves or times out; verify each by the save log line and `Get-ServerSave` timestamp.

## 5. Identity and per-player records

- [ ] 5.1 Client: create/read `UserData/CMS21Together/player.json` and send `playerKey` in `ConnectPacket` from `AuthHandler.HandleConnect` and `ClientSteam`; add harness `player-key <key>` (in-memory override for the next connect); verify the file is created once and the same key is logged by the server on two connects.
- [ ] 5.2 Server: set `Client.SteamID` in `SteamTransport.OnConnectionChanged`; in `AuthHandler.OnConnected` resolve identity (`steam:` / `guid:`), reject a missing DirectIP key (`MissingIdentity`) and a key already bound to a connected slot (`DuplicateIdentity`), bind `Client.Identity`, create the record from `NewPlayerTemplate` if new, store `Name`/`LastSeenUtc`; verify with the harness: B connects with `player-key` set to A's key and is rejected with the duplicate reason while A stays connected.
- [ ] 5.3 Clear the slot's `PlayerState` entries in `Client.Disconnect`; copy position/rotation/scene into the record on leave and before every save; verify the record in `Get-ServerSave` after A leaves matches A's dumped position.
- [ ] 5.4 Per-player progression (D7): `StatsHandlers` exp/level on the sender's record, scrap shared; `GarageUpgradeHandler` skills and `ComputeAvailablePoints` per record; per-receiver `WorldState`/`GarageState` builders used by sync, stats, upgrades and `CommandSystem`; `/level` and `/exp` take a slot; add `/players`; verify with the harness: `persist-mark 0 300` on A changes A's `self` only, `persist-mark 5 0` changes `shared.scrap` on both.
- [ ] 5.5 Client: apply `PlayerRestore` after the garage load (transform + `FPSInputController.SetCharacterControllerPosition`; fall back to `CurrentProfileData.PlayerData.SetPosition/SetRotation` before the load if the teleport misbehaves); verify A's position after leave + rejoin is within 0.5 m of where it left.

## 6. Join pipeline

- [ ] 6.1 Server: `AuthHandler.OnAskForSync` becomes the snapshot pipeline (D8): under `StateLock` send `SyncBegin` with item counts, each `ISnapshotProvider` by `SyncOrder`, `SyncEnd{snapshotId}`; world/garage/inventory/self become providers (`self` sends `PlayerRestore` only when the record has a garage position); verify `connect` passes and the server log lists the sections in order.
- [ ] 6.2 Server: `Client.SyncState` (`Connected, Identified, Syncing, InSession`); `SendToClients` skips clients before `Syncing`; `SyncAck` sets `InSession`; handlers without `[AllowBeforeSync]` drop packets from clients not `InSession` (Heartbeat, Connect, AskForSync, SyncAck, Movement, Disconnect allowed); add harness `send-early-stats`; verify it produces a dropped-packet log line and no state change.
- [ ] 6.3 Client: `SyncTracker` (expected counts from `SyncBegin`, `Applied(key)` from handlers, `SyncAck` when `SyncEnd` arrived and counts are met, other `snapshotId`s ignored); world/garage/inventory/self handlers report applied; `StateDump` adds `snapshotId` and `syncAcked`; verify `SyncAck` in both logs and `syncAcked` true in `connect`.
- [ ] 6.4 Client: replace the absolute timeouts in `LoaderAddition.CustomLoad` and `WorldStatesPackets.WaitForSyncCompletion` with a 30 s no-progress timeout; on failure log the incomplete keys, disconnect (`SyncFailed`), menu; verify with a debug-only server delay of 20 s between sections that the join completes.
- [ ] 6.5 Client: no state-changing packets before `SyncAck` (extend the gate to every send path that checks `ClientData.IsServerUpdating` today); verify no dropped-packet warnings in the server log during `connect`.

## 7. Client save safety

- [ ] 7.1 Find, read-only, where `RDGPlayerPrefs` stores `selectedProfile` (registry value name or the file behind `CheckFile()`); make `profile-pref` read exactly that; document it in `CMS21-Together-Client/Persistence/README.md`; verify `profile-pref` matches the value the game shows as the selected profile.
- [ ] 7.2 `SessionGuard` + Harmony prefixes on `GameDataManager.Save(int)`, `ProfileManager.BackupSave()`, `ProfileManager.Save()`, `ProfileManager.DeleteProfile()`, `ProfileManager.DeleteSelectedProfile()`, active from `ModGameManager.StartGame` until the Menu scene has loaded; verify the client log shows a block on `to-menu` and no `profile4.cms21b` appears.
- [ ] 7.3 Profile slot: write original field + pref to `UserData/CMS21Together/session.json` before `StartGame` changes them; restore the pref right after `ProfileManager.Load()`; on Menu loaded and `OnApplicationQuit` restore the 4-slot `ProfileData` array (`SaveUtils.RestoreProfileDataSize`), field and pref and delete `session.json`; in `OnInitializeMelon` recover from a leftover `session.json`; verify `profile-pref` equals the pre-session value during and after a session, and after killing the game mid-session and restarting it.
- [ ] 7.4 Copy `GlobalStrings.SaveDirectory/profile*.cms21b` to `UserData/CMS21Together/ProfileBackups/<ts>/` before the first `StartGame` of a run (keep 5; abort the join with a message if the copy fails); verify the folder exists before the garage loads and the oldest is removed on the sixth run.
- [ ] 7.5 Never delete `profile4.cms21b` (may be an upstream 0.4 save); only log it; verify by `rg "File.Delete|DeleteSave" CMS21-Together-Client` that no mod code deletes in the save folder.

## 8. Server loss on the client

- [ ] 8.1 Client: on `ClientTCP` 0-byte read or exception, Steam connection closed, or no server packet for 10 s, marshal to the main thread, `Client.Disconnect()`, load the menu with `saveGame = false`, show the reason; show `DisconnectPacket.reason` messages (`ServerShutdown`, `DuplicateIdentity`, `MissingIdentity`, `SyncFailed`); verify with `Stop-TestServer -Kill` that a client in the garage reaches the menu within 12 s.

## 9. End-to-end scenarios (two instances)

- [ ] 9.1 `scenarios/profile-safety.ps1`: before launch hash every file under the game's `Save` folder and read `selectedProfile` (read-only); A and B connect, `to-menu`, reconnect, quit; verify identical hashes, no added files, unchanged `selectedProfile`, and a `ProfileBackups` folder in each install.
- [ ] 9.2 `scenarios/rejoin.ps1`: A and B connect; A `persist-mark 7 300` and `teleport`; A `to-menu`; B stays; A reconnects; verify A's `self` (level, exp, skills, position) is restored, B's `self` is unchanged, `shared.scrap` is +7 on both, and `shared`/`inventory` dumps are equal.
- [ ] 9.3 `scenarios/latejoin.ps1`: A connects and changes state (`persist-mark`, plus a car spawn and part change when rows 1–2 are merged); then B connects; verify B's `shared`, `inventory`, `cars` equal A's, both see each other, B `syncAcked`, no dropped-packet warnings.
- [ ] 9.4 `scenarios/persistence-restart.ps1`: A and B connect and `persist-mark`; B leaves (save); `Stop-TestServer -Kill` with A connected; verify A reaches the menu; `Start-TestServer`; both reconnect; verify `shared`, `inventory` and each player's `self` equal the values before the kill; then with both connected `Stop-TestServer -Graceful`, verify both reach the menu with the shutdown reason and the save timestamp is newer; finally `--check-save tools/test-env/fixtures/server_save_v1.json` exits 0.
