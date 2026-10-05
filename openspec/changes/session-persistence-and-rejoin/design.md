# Design

## Context

Observed in the code today:

- `GameDataManager` (server) writes the whole `ModGameState` with Newtonsoft to `Saves/server_save.json`, with no
  version. `SaveSession()` calls `RotateBackups()`, which **moves** the main file to `_bak1` before the new file is
  written; a crash in between leaves no main file, `TryLoadSession()` then calls `CreateNewSession()` (which saves
  at once), and the real data rotates out of `_bak3` three saves later. `Program.Main` calls `Server.Start` before
  `TryLoadSession`. `/stop` calls `Environment.Exit(0)` without saving.
- Threads: `Tcp.ReceiveCallback`/`Tcp.HandleData`, `Udp.HandleData` (from `Server.UDPReceiveCallback`) and
  `SteamTransport.OnMessage` (Steam callback thread) call `PacketRouter.Dispatch`; the autosave
  (`ServerWindow.TickServer`), `Client.Update` (heartbeat timeout → `Disconnect`) and `CommandSystem.Execute` run on
  the Terminal.Gui main loop. Nothing synchronizes them.
- Players are `Server.Clients[1..MaxPlayers]` slots. `Client.SteamID` is never set; the client sends
  `username = "TestUser{id}"`. `PlayerState` is keyed by slot, `[NonSerialized]`, never cleaned on disconnect.
- Join: `LoaderAddition.CustomLoad` sends `AskForSync`; `AuthHandler.OnAskForSync` sends `WorldState`,
  `GarageState`, inventory batches and `SyncEnd` without a lock. The client waits 10 s (`CustomLoad`) / 15 s
  (`WorldStatesPackets.WaitForSyncCompletion`) absolute. `Server.SendToClients` also reaches clients still loading.
- Client: `ModGameManager.StartGame` grows `GameDataManager.ProfileData` to 5 (`SaveUtils.ExtendProfileDataSize`),
  puts an empty profile in slot 4, sets `ProfileManager.selectedProfile = 4` **and**
  `RDGPlayerPrefs.SetInt("selectedProfile", 4)`, and never undoes either. Nothing blocks the game's own saving, so
  any save with `saveGame = true` writes `profile4.cms21b`. A dead server is not noticed.
- Game API (verified in the stubs): `GameDataManager.Save(int)`, `.ProfileData`, `.CurrentProfileData`;
  `ProfileManager.Awake/Load/Save/BackupSave/DeleteProfile/DeleteSelectedProfile`, `.selectedProfile`;
  `RDGPlayerPrefs.GetInt/SetInt/Save/CheckFile`; `GlobalStrings.SaveDirectory`; `PlatformManager.DeleteSave(string)`
  and generic `SendSave<T>` (not patchable in IL2CPP); `PlayerData.SetPosition/SetRotation/IsDefault`;
  `FPSInputController.SetCharacterControllerPosition(bool)`; `NotificationCenter.SelectSceneToLoad(string,
  SceneType, bool useFader, bool saveGame)`.
- Upstream 0.4 appended mod data to the game's `profileN.cms21b` and returned empty bytes from
  `ProfileData.SerializeToBytes`; with no migration that lost saves (#105, #96, #92). This design keeps server state
  out of game files and blocks writes at the entry points.

## Goals / Non-Goals

**Goals:** a small contract that rows 1–6 plug into, landed before them; no silent data loss on crash, corruption
or upgrade; a returning player gets their name and garage position back in any slot; zero writes to the player's
local profiles.

**Non-Goals:** per-player progression (user decision 2026-10-05: money, scrap, level, XP and skills are shared, as in
Dev); importing a single-player save (backlog; it would be one more producer of sections); automatic reconnect;
several named saves; authentication stronger than "has the key"; scene travel and avatars (row 6).

## Part A — contract (task groups 1–2, lands first)

### D1. Save envelope with versioned sections

```json
{ "Format": "cms21-together-server-save", "SaveVersion": 2, "ModVersion": "0.5.0", "SavedAtUtc": "…",
  "Sections": { "world": { "Version": 1, "Data": { } }, "garage": { "Version": 1, "Data": { } }, … } }
```

- `SaveVersion` versions the envelope; each section versions its own data. A file without `SaveVersion` that has
  `WorldState` is v1.
- Sections serialize the existing state objects; handlers keep using `GameDataManager.CurrentState.X`, a section is a
  thin adapter. Adding a field needs no version bump (Newtonsoft fills defaults); renaming, removing or reshaping
  does, with a `JToken` migration step.
- A section missing from the file gets `Reset()`; an unknown section is kept as raw `JToken`, logged and written back.
- Kept JSON + Newtonsoft (already used, diffable in tests). Rejected: BinaryFormatter (brittle across type changes),
  embedding in game saves (upstream's mistake), separate save DTOs (doubles every row's work).

### D2. Interfaces and sync order

`CMS21-Together-Server/Data/Persistence/`:

```csharp
public interface ISaveSection {
    string Key { get; }
    int Version { get; }
    JToken Save();                                // StateLock held
    void Load(JToken data);                       // already migrated to Version
    void Reset();                                 // new session, or section absent from the file
    JToken Migrate(JToken data, int fromVersion); // one step: fromVersion -> fromVersion + 1
}

public interface ISnapshotProvider {
    string Key { get; }
    int SyncOrder { get; }                        // constants in Core/Data/SyncOrder.cs
    int SendSnapshot(int clientId);               // StateLock held; Server.SendToClient only; returns item count
}
```

Classes with `[SessionSection]` are found by reflection at start (like `PacketRouter.Initialize`); a duplicate key
stops the server. A class may implement either interface or both. An *item* is whatever unit the client reports with
`SyncTracker.Applied(key)`; the provider returns how many it sent.

| Order | Key | Owner | Save section | Snapshot sends |
|---|---|---|---|---|
| 0 | `world` | row 7 | yes | `WorldState` (`updateGamemode = true`) |
| 10 | `garage` | row 7 | yes | `GarageState` (`AvailablePoints` computed) |
| 20 | `inventory` | row 7 | yes | `InventorySyncPacket` batches (1 batch = 1 item) |
| 100 | `cars` | row 1 | yes (also holds row 4's `CarState.Details`) | car replay + parts |
| 150 | `car-details` | row 4 | — | `CarDetailsUpdatePacket{IsFull}` per loaded car |
| 200 | `car-placement` | row 2 | yes | `ParkingState`, `ParkingSlotUpdate`, `LifterState` |
| 300 | `workshop-tools` | row 5 | yes | `ToolsState` |
| 400 | `jobs` | row 3 | yes | `JobsState` |
| 450 | `self` | row 7 (Part B) | `players` | `PlayerRestore` |
| 500 | `players` | row 6 | — | `PlayerRoster` |

Cars first because later sections reference car loaders; jobs after cars because row 3 marks customer cars
"whichever arrives last"; own position after cars and lifts so the player is not placed inside a car that spawns
afterwards; other players last because they are visual only.

### D3. State lock

One object, `GameDataManager.StateLock` (the name row 1 already uses; this change creates it, rows use it). It is
held around `PacketRouter.Dispatch` in `Tcp.HandleData`, `Udp.HandleData` and `SteamTransport.OnMessage`, around
`CommandSystem.Execute`, `Client.Disconnect`, every main-loop tick that reads state or broadcasts (`Client.Update`
timeouts, row 3's expiry timer) and around building the save. `Monitor` is re-entrant, so a row that also locks inside
its handler is harmless; no row adds a second lock for shared state. Sends inside the lock do not block
(`BeginWrite`, Steam `SendMessage`). The file write happens outside the lock and only on the main loop: other
threads call `GameDataManager.RequestSave()`, the next tick saves.

### D4. Join pipeline (first join, rejoin, late join and return to the garage are the same path)

```
client                                       server
ConnectPacket(modVersion, …)           →     version check                        SyncState = Connected
StartGame → garage load → CustomLoad
AskForSync                             →     lock(StateLock) {
                                               SyncBegin{snapshotId}
                                               providers by SyncOrder → item counts
                                               SyncEnd{snapshotId, Items}
                                               SyncState = Syncing }
apply, SyncTracker.Applied(key)
SyncAck{snapshotId} (counts met)       →     SyncState = InSession, log "joined"
```

- The snapshot is queued under the lock and every state change is made under it, so any change after the snapshot
  follows it on the same ordered stream (TCP / Steam reliable). `Server.SendToClients` skips clients in `Connected`.
- Client `SyncTracker`: `SyncBegin` resets it; handlers call `Applied(key)` when an item is really applied (a car
  when `CarLoader.IsCarLoaded()` and its parts are applied); `SyncAck` goes out once `SyncEnd` arrived and every count
  is met, and `ClientData.IsInitialSyncFinished` becomes true. Rows count items instead of adding their own
  "synced" flags. Packets of another `snapshotId` are ignored. A repeated `AskForSync` (garage return, row 6) gets a
  new snapshot.
- Timeout: 30 s without a received snapshot packet or an applied item (replaces the absolute 10 s/15 s; row 1's
  "10 s without progress" becomes this). On failure the client logs expected vs applied per key, disconnects and
  loads the menu with `saveGame = false`.
- The read-only phase and ready/ack handshake follow the idea of TogetherFixer's LateJoin; implemented
  independently. If code from `FixForTogether/Features/Lobby/Join/LateJoin.cs` is adapted, credit TogetherFixer.

### D5. Migration v1 → v2 and the contract's write path

`world` ← `WorldState` (Gamemode, Money, Scraps, Level, Exp; `updateGamemode` dropped); `garage` ← `GarageState`
(both upgrade dictionaries; `AvailablePoints` dropped, it is derived); `inventory` ← `InventoryState`; `cars` v1 ←
`CarState` as is (row 1 bumps it when it changes the shape). Before migrating, the file is copied to
`Saves/backups/premigration_v1_<ts>.json`, never deleted automatically.

The contract keeps today's write path (`RotateBackups` + `WriteAllText`) and adds a `save` command; Part B makes the
write crash-safe. For the harness, the server accepts `--command-file <path>`: the main loop runs each line written
there through `CommandSystem.Execute` and empties the file.

## Part B — rest (task groups 3–8, lands last)

### D6. Crash-safe write and backups

`SaveSession()` (main loop only): build the envelope under `StateLock` → hash of `Sections` equals the last saved
hash → skip → write `server_save.json.tmp` with `Flush(true)` → rotate `backups/server_save_bak{N-1}`…`bak1` up one →
`File.Replace(tmp, server_save.json, backups/server_save_bak1.json)` (first save: `File.Move`). A crash at any
step leaves the old main file in place. At every start, before loading, the main file is copied to
`backups/start_<ts>.json` (keep 3). `server_config.ini` gains `autosave_interval_seconds = 300` (0 = off) and
`backup_count = 5`.

Triggers: autosave tick, `RequestSave()` after a player leaves, `/save`, `/stop` (save, then
`DisconnectPacket{playerID = -1, reason = ServerShutdown}` to every connected slot, including ones still loading,
then exit), window close. Terminal.Gui consumes Ctrl+C and .NET Framework does not reliably raise `ProcessExit` on
console close, so window close is caught with
`SetConsoleCtrlHandler` (`CTRL_CLOSE_EVENT`), which saves synchronously within the OS's ~5 s; a `saveGate` lock keeps
it from racing a main-loop save. A hard kill loses at most the time since the last save.

### D7. Load, fallback, refusal

Load runs before `Server.Start`, so a refused load never accepts a client. Candidates: main, `bak1..N`, `start_*`
(newest first). Per candidate: parse → detect version → any version newer than supported: refuse to start, write
nothing → migrate → `Load` every section. A file that fails to parse or migrate is moved to `Saves/corrupt/` and the
next candidate is tried. A fallback that loads is saved as the new main at once. Files existed but none loaded →
log the reason per file and refuse to start. Only with no file at all does `CreateNewSession()` run.
`CMS21_Together_Server.exe --check-save <file>` runs load + migration before Terminal.Gui init, prints a summary per
section and exits 0/1.

### D8. Identity and player records

- DirectIP: the client creates `UserData/CMS21Together/player.json` = `{ "PlayerKey": "<Guid N>" }` once per install
  and sends it in `ConnectPacket.playerKey`; identity `guid:<key>`. Missing key → `reason = MissingIdentity`.
- Steam: `SteamTransport.OnConnectionChanged` stores `info.Identity.SteamId` in `Client.SteamID`; identity
  `steam:<id>`, the sent key is ignored.
- Rejected: username (not unique), IP (NAT), Steam ID over DirectIP (both harness installs share one Steam account).
  The key is a bearer token; acceptable for friends' co-op.
- Duplicate: an identity already bound to a connected slot → the new connection gets `reason = DuplicateIdentity`;
  the first stays. A crashed client's half-open slot frees after the 10 s heartbeat timeout. Rejected "newest
  wins": a copied key would make two installs kick each other in a loop.
- `PlayerRecord { Key, Name, LastSeenUtc, Position, Rotation, Scene }` in section `players`. Nothing else is per
  player: progression stays in `world`/`garage`, row 6's seat/engine are cleared on leave anyway. Name comes from
  `ConnectPacket.username` (row 6 fills it); position/rotation/scene are copied from the live per-slot state (row 6's
  presence record, or `PlayerState` before row 6) on leave and before each save; the slot's live entries are cleared
  in `Client.Disconnect`. `/players` lists records.
- Provider `self` (450) sends `PlayerRestore{Position, Rotation}` only when the record's scene is the garage. The
  client applies it after the garage load (transform + `FPSInputController.SetCharacterControllerPosition`; fallback:
  `CurrentProfileData.PlayerData.SetPosition/SetRotation` before the load); row 6's spawn placement skips then.

### D9. Join hardening

Server handlers without `[AllowBeforeSync]` drop packets from clients that are not `InSession` and log a warning
(allowed: Heartbeat, Connect, AskForSync, SyncAck, Movement, PlayerPresence (row 6), Disconnect). Client: every send path that checks
`ClientData.IsServerUpdating` today also checks `IsInitialSyncFinished`.

### D10. Server loss on the client

`ClientTCP.ReceiveCallback` 0 bytes / exception, Steam connection closed, or no server packet for 10 s (server
heartbeats every 3 s) → on the main thread (`ThreadManager`) `Client.Disconnect()` and load the menu with
`saveGame = false`, showing `DisconnectPacket.reason` when one arrived. The player rejoins by hand.

### D11. Client save safety (`CMS21-Together-Client/Persistence/`)

- `SessionGuard.Active` from the start of `ModGameManager.StartGame` until the Menu scene has loaded after the
  session (not on disconnect: after a server loss the garage with server state is still loaded).
- Harmony prefixes returning `false` while active, logged once per session: `GameDataManager.Save(int)`,
  `ProfileManager.Save()`, `BackupSave()`, `DeleteProfile()`, `DeleteSelectedProfile()`,
  `PlatformManager.DeleteSave(string)`. Entry points instead of `SerializeToBytes` (empty file risk) or `SendSave<T>`
  (generic).
- Profile slot: `StartGame` writes the original `selectedProfile` field and pref to `UserData/CMS21Together/session.json`
  before changing them. The pref is set back right after `ProfileManager.Load()` (the field stays 4). On Menu loaded
  and `OnApplicationQuit`: `ProfileData` back to 4 entries (new `SaveUtils.RestoreProfileDataSize`), field and pref
  restored, `session.json` deleted. A leftover `session.json` means a crash mid-session: the pref is restored before
  the game reads it — `OnInitializeMelon` is too early for `GameManager`, so a prefix on `ProfileManager.Awake()`
  does it (task 6.3 confirms the read order). If task 6.3 shows `StartGame` works without writing the pref, the pref
  write is dropped instead and recovery only cleans up `session.json`.
- `profile4.cms21b` is never deleted (upstream 0.4 used slot 4+ for its saves); its presence is logged.
- Before the first `StartGame` of a game run, `GlobalStrings.SaveDirectory/profile*.cms21b` is copied to
  `UserData/CMS21Together/ProfileBackups/<ts>/` (keep 5); if the copy fails the join is aborted.

### What the server stores vs relays

Stores: `world`, `garage`, `inventory`, `players` (identity, name, last garage position/scene) and every row's
sections. Keeps live positions per slot in memory only, copied into records on leave/save. Relays movement without
storing history. Nothing is stored on clients except the identity key, `session.json` and profile backups.

## Risks / Trade-offs

- [`GameDataManager.Save(int)` may not be the only path that writes profile files] → the profile-safety scenario
  hashes every file in the save folder after scene change, exit to menu and quit.
- [The game may read the `selectedProfile` pref again during the garage load] → the scenario reads it during the
  session; fallback is restoring it only at session end (crash recovery still covers it).
- [Blocking `ProfileManager.Save()` may break something the game expects] → watch client logs; drop that prefix if it
  errors and rely on the file check.
- [One global lock serializes all handlers] → state work is tiny next to I/O for 2–4 players; the file write is
  outside the lock.
- [A careless rename in a live state object breaks old saves] → section versions + the v1 fixture check.
- [Teleport on rejoin fights the character controller] → rejoin scenario checks within 0.5 m; fallback in D8.
- [`File.Replace` fails when another process holds the file (antivirus, editor)] → retry 3× with 200 ms, then log and
  keep the `.tmp`; the old main file stays valid.

## Migration Plan

1. Server and client ship together (mod version check).
2. First start of the contract build: pre-migration copy, migrate v1 → v2, save. Old `server_save_bak*.json` stay
   untouched.
3. Rollback: put `backups/premigration_v1_<ts>.json` back as `Saves/server_save.json` and run the old build (an old
   build fails on a v2 file and starts a new session; release notes say so).

## Open Questions / assumptions

1. Duplicate identity: first connection wins.
2. No automatic reconnect; a save that exists but cannot be loaded makes the server refuse to start.
3. Identity file per install; `Setup-TestInstalls.ps1` must not copy it between installs.
