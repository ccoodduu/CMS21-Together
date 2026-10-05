# Design

## Context

Observed in the code today (see proposal.md for why it matters):

- `GameDataManager` (server) serializes the whole `ModGameState` with Newtonsoft to `Saves/server_save.json`.
  No version field. `SaveSession()` calls `RotateBackups()`, which **moves** the main file to `_bak1` before the
  new file is written; a crash in between leaves no main file, `TryLoadSession()` then calls
  `CreateNewSession()`, and three autosaves later the real data has rotated out of `_bak3`.
- Autosave: `ServerWindow.TickServer()` every `AutoSaveInterval` = 300 s. `/stop` calls `Environment.Exit(0)`
  without saving; `Program.Exit()` → `Server.Stop()` does not save either.
- Packet handlers run on socket callback threads (`Tcp.HandleData` → `PacketRouter.Dispatch`), the autosave runs
  on the Terminal.Gui main loop; nothing synchronizes them.
- Players are `Server.Clients[1..MaxPlayers]` slots. `Client.SteamID` is never set. The client sends
  `username = "TestUser{id}"`. `PlayerState` (positions etc., keyed by slot) is `[NonSerialized]` and never
  cleaned when a slot is reused. Level, exp and skills (`WorldState.Level/Exp`, `GarageState.PlayerUpgradeLevels`)
  are shared by everyone.
- Join: `LoaderAddition.CustomLoad` sends `AskForSync` after the vanilla garage load; `AuthHandler.OnAskForSync`
  sends `WorldState`, `GarageState`, `InventorySyncPacket` batches, `SyncEnd`. The client waits 10 s
  (`CustomLoad`) / 15 s (`WaitForSyncCompletion`) absolute. Cars are not in the sync (row 1 adds them).
  Live `SendToClients` broadcasts also go to clients that are still loading.
- Client save handling: `ModGameManager.StartGame` grows `GameDataManager.ProfileData` to 5
  (`SaveUtils.ExtendProfileDataSize`), puts an empty `ProfileData` in slot 4, sets
  `ProfileManager.selectedProfile = 4` **and** `RDGPlayerPrefs.SetInt("selectedProfile", 4)` (persisted), and
  never undoes either. Nothing blocks the game's own saving, so a scene change or "exit to menu" with
  `saveGame = true` writes `profile4.cms21b`. The client does not notice a dead server (`ClientTCP` closes
  its socket on 0 bytes but nothing returns to the menu).
- Game API (verified in the decompiled stubs): `GameDataManager.Save(int profileID)`,
  `GameDataManager.ProfileData`, `ProfileManager.Load()`, `Save()`, `BackupSave()`, `DeleteProfile()`,
  `DeleteSelectedProfile()`, `selectedProfile`, `RDGPlayerPrefs.GetInt/SetInt`, `GlobalStrings.SaveDirectory`,
  `PlatformManager.SendSave<T>` (generic, not hookable in IL2CPP), `ProfileData.PlayerData.SetPosition/SetRotation`,
  `FPSInputController.SetCharacterControllerPosition`.
- Lessons from upstream 0.4 (`SaveSystem.cs`, `SaveSystemHooks.cs`): mod data was appended to the game's own
  `profileN.cms21b`, a `ProfileData.SerializeToBytes` prefix returned `false` (empty bytes) on clients, and slots
  ≥ 4 were mod saves. Mixing mod data into game files and having no migration is what lost saves (#105, #92).
  This design keeps server state out of game files entirely and blocks writes at the entry point instead of
  corrupting the serializer's output.

## Goals / Non-Goals

**Goals:** one save format that all rows plug into; no silent data loss on crash, corruption or upgrade; the
same player gets their own data back in any slot; one join path whose order rows 1–6 can rely on; zero writes to
the player's local profiles.

**Non-Goals:** importing a single-player profile (needs the DTOs of rows 1–6 plus a client-side exporter that
reads `ProfileData` in game; a later change can add it as one more producer of sections); automatic reconnect;
several named saves per server; authentication stronger than "possession of the key"; scene travel and what
other players look like (row 6).

## Decisions

### D1. Save format: JSON envelope with versioned sections

```json
{
  "Format": "cms21-together-server-save",
  "SaveVersion": 2,
  "ModVersion": "0.5.0",
  "SavedAtUtc": "2026-10-05T21:14:03Z",
  "Sections": {
    "world":     { "Version": 1, "Data": { "Gamemode": 1, "Money": 12500, "Scraps": 0 } },
    "garage":    { "Version": 1, "Data": { "GarageUpgradeLevels": { } } },
    "inventory": { "Version": 1, "Data": { "InventoryItems": [ ], "...": [ ] } },
    "players":   { "Version": 1, "Data": { "NewPlayerTemplate": { }, "Records": { "guid:3f2a…": { } } } },
    "cars":      { "Version": 1, "Data": { "LoadedCars": { }, "BodyParts": { }, "SubParts": { } } }
  }
}
```

- `SaveVersion` versions the envelope only; each section versions its own data. A row can change its data
  without touching anyone else's migrations. A file without `SaveVersion` that has `WorldState` is v1.
- Kept JSON + Newtonsoft (already used, human-readable, diffable in tests). Rejected: BinaryFormatter (same as
  packets, but brittle across type changes and unreadable), embedding in game saves (upstream's mistake).
- Sections serialize the existing state objects (`InventoryState`, `CarState`, …) directly instead of separate
  save DTOs. Renaming a field therefore requires a section version bump and a `JToken` migration; the v1 fixture
  check (task 4.5) catches accidental breaks. Rejected separate DTOs: doubles every row's work.
- Unknown sections are kept as raw `JToken` and written back unchanged (a feature switched off, or a file
  touched by a newer build that only added a section).

### D2. Extension contract for rows 1–6

Server, `CMS21-Together-Server/Data/Persistence/`:

```csharp
public interface ISaveSection {
    string Key { get; }                       // "cars", "car-placement", "jobs", "car-details", "workshop-tools"
    int Version { get; }
    JToken Save();                            // called with GameDataManager.StateLock held
    void Load(JToken data);                   // data already migrated to Version
    void Reset();                             // new session, or section absent from the file
    JToken Migrate(JToken data, int fromVersion);   // one step: fromVersion -> fromVersion + 1
}

public interface ISnapshotProvider {
    string Key { get; }
    int SyncOrder { get; }                    // constants in Core/Data/SyncOrder.cs
    int CountItems(int clientId);             // what the client will report as applied
    void SendSnapshot(int clientId);          // sends packets with Server.SendToClient only
}
```

Classes marked `[SessionSection]` are discovered by reflection at start (same pattern as `PacketRouter`), so a
row adds one class and registers nothing by hand. A row may implement either or both interfaces (positions are
snapshot-only; per-player data lives in `players`). Per-player fields of other rows go in
`PlayerRecord.Extra[<key>]` (a `JToken` bag owned by that key) rather than new `PlayerRecord` fields.

`SyncOrder` (in Core so the client can show progress by name):

| Order | Key | Owner | Sends |
|---|---|---|---|
| 0 | `world` | row 7 | `WorldState` (shared + receiver's level/exp, `updateGamemode = true`) |
| 10 | `garage` | row 7 | `GarageState` (garage upgrades + receiver's skills and `AvailablePoints`) |
| 20 | `inventory` | row 7 | `InventorySyncPacket` batches (1 item = 1 batch) |
| 100 | `cars` | row 1 | loaded cars and their parts |
| 150 | `car-details` | row 4 | fluids, wheels, paint, … |
| 200 | `car-placement` | row 2 | lifts, loader places, parking |
| 300 | `workshop-tools` | row 5 | machine states |
| 400 | `jobs` | row 3 | order list, active jobs |
| 450 | `self` | row 7 | `PlayerRestore` (own position/rotation if the saved scene is the garage) |
| 500 | `players` | row 6 | other players' positions, scenes, names |

Cars first because every later section references a car loader; own position after cars and lifts so the
player is not placed inside a car that spawns afterwards; other players last because they are visual only.

**Sequencing with the roadmap:** rows 1–6 are implemented first. Group 2 of tasks.md (the contract and the state
lock, no behaviour change) is small and can land before row 1 so rows 1–6 plug in directly; if a row
lands earlier and adds sends straight into `OnAskForSync`, this change moves them into a provider without
changing their packets.

### D3. Crash-safe write and backups

`SaveSession()`:
1. Under `StateLock`: build the envelope and serialize to a string (milliseconds; keeps handlers blocked only
   for that time).
2. Outside the lock: if the SHA-256 of the string equals the last saved hash → return.
3. Write `Saves/server_save.json.tmp`, `Flush(true)`.
4. Rotate `backups/server_save_bak{N-1}` → `bak{N}` … `bak1` → `bak2`, then
   `File.Replace(tmp, server_save.json, backups/server_save_bak1.json)` (atomic on NTFS; first save:
   `File.Move`).

At every start, before loading: copy the main file to `backups/start_<yyyyMMdd-HHmmss>.json`, keep 3.
Before a migration: `backups/premigration_v<old>_<ts>.json`, never deleted automatically.
`server_config.ini` gains `autosave_interval_seconds = 300` (0 = off) and `backup_count = 5`.

Save triggers: autosave tick, player leaves (`Client.Disconnect` and timeout), `/save`, `/stop`, `Program.Exit`
(window closed), `AppDomain.ProcessExit` and `Console.CancelKeyPress` (best effort). A hard kill loses at
most the time since the last of these.

### D4. Load, fallback, quarantine, refusal

Candidates in order: main, `bak1..N`, `start_*` (newest first). For each: parse → detect version → if any version
is newer than supported, stop and refuse to start (a downgrade is not corruption; nothing is moved or written) →
migrate → `Load` every section. Any exception moves on to the next candidate; files that failed to parse or
migrate are moved to `Saves/corrupt/<ts>_<name>`. If a candidate loads and it was not the main file, it is
saved as the new main immediately. If files existed but none loaded, the server logs the reason per file and
refuses to start (no new session over existing data; upstream #105 is exactly "my save is gone"). Only when no
file exists at all does `CreateNewSession()` run.

`Program.Main` currently starts the network before loading; the order becomes load first, then `Server.Start`,
so a refused load never accepts a client.

`CMS21_Together_Server.exe --check-save <file>` runs the same load + migration without the network, prints a
summary per section and exits 0/1. The harness uses it on a committed v1 fixture.

### D5. Migration v1 → v2

`world` ← `WorldState.{Gamemode, Money, Scraps}`; `garage` ← `GarageState.GarageUpgradeLevels`;
`inventory` ← `InventoryState`; `cars` (v1) ← `CarState` as-is (row 1 bumps it if it changes the shape);
`players.NewPlayerTemplate` ← `{Level, Exp, Skills = PlayerUpgradeLevels}`; `players.Records` empty.
`AvailablePoints` and `updateGamemode` are dropped (derived / transient). Every player joining a migrated
session starts with the progression the shared save had.

### D6. Identity

- DirectIP: the client creates `UserData/CMS21Together/player.json` = `{ "PlayerKey": "<Guid N>" }` once per
  install and sends it in `ConnectPacket.playerKey`; identity is `guid:<key>`.
- Steam transport: `SteamTransport.OnConnectionChanged` stores `info.Identity.SteamId` in `Client.SteamID`;
  identity is `steam:<id>` and the sent key is ignored.
- Rejected: username (not unique, `TestUser{id}` today), IP (NAT, changes), Steam ID over DirectIP (both
  harness installs run under the same Steam account, and it is unverified there anyway).
- A key is a bearer token; anyone who copies `player.json` is that player. Acceptable for friends' co-op.
- Duplicate: if the identity is already bound to a connected slot, the new connection gets
  `DisconnectPacket{reason = DuplicateIdentity}` and is closed; the first stays. A crashed client whose socket
  is half-open can rejoin after the 10 s server timeout. Rejected "newest wins": a copied key would let two
  installs kick each other in a loop.
- `/players` lists records (key, name, last seen, connected slot) for the operator.

### D7. Per-player records

`PlayerRecord { Key, Name, LastSeenUtc, Level, Exp, Skills (Dictionary<string,bool[]>), Position, Rotation,
Scene, Extra }` in `ModGameState.Players.Records`. Server changes:
- `StatsHandlers.HandleStatsAction`: exp/level on the sender's record; scrap stays shared. Sends the sender a
  personalized `WorldState`; a scrap change sends every synced client its own personalized `WorldState`.
- `GarageUpgradeHandler`: skill requests change the sender's record; `ComputeAvailablePoints` uses that
  record; `GarageState` is built per receiver. Money upgrades stay shared.
- `CommandSystem` `level`/`exp` take a player slot.
- Position/rotation/scene are copied from `PlayerState` (live, by slot) into the record on leave and before
  every save; `PlayerState` entries for a slot are cleared on disconnect.
- `WorldState`/`GarageState` keep their packet shape; Level/Exp/PlayerUpgradeLevels in them now mean "yours".

### D8. Join pipeline (first join, rejoin and late join are the same path)

A dedicated server has no lobby, so every join is a late join.

```
client                                   server
TCP connect                        →     slot assigned, ConnectPacket(playerID)
ConnectPacket(modVersion, playerKey) →   version check, identity bind (reject duplicate)   [Identified]
StartGame → garage load (vanilla) → CustomLoad
AskForSync                         →     lock(StateLock) {
                                           SyncBegin{snapshotId, items per key}
                                           providers by SyncOrder
                                           SyncEnd{snapshotId}
                                           client.SyncState = Syncing }
apply sections, count items
SyncAck{snapshotId} (all counts met) →   SyncState = InSession, log "joined"
```

- The whole snapshot is queued under `StateLock`, and every handler runs under the same lock, so any change
  made after the snapshot is queued behind it on the same ordered stream (TCP / Steam reliable channel).
- `Server.SendToClients` skips clients that are not yet `Syncing`/`InSession` (they get everything in their
  snapshot). Unreliable movement may arrive early; the client already ignores it until synced.
- Client read-only: the existing `ClientData.IsInitialSyncFinished` gate is extended so no state-changing
  packet is sent before `SyncAck`. Server side, handlers without `[AllowBeforeSync]` drop packets from clients
  that are not `InSession` and log a warning. Same idea as TogetherFixer's LateJoin read-only phase and
  ready/complete handshake; implemented independently for the new architecture — if any code is adapted from
  `FixForTogether/Features/Lobby/Join/LateJoin.cs`, credit TogetherFixer as its license requires.
- Client `SyncTracker`: `SyncBegin` sets the expected count per key; handlers call `Applied(key)` when an item
  is really applied (a car counts when `CarLoader.IsCarLoaded()`). `SyncAck` goes out when `SyncEnd` arrived and
  every count is met. Timeout: 30 s without a received snapshot packet or an applied item (replaces the
  absolute 10 s/15 s). Count mismatch at `SyncEnd` + timeout → log the key, disconnect, menu.
- A repeated `AskForSync` from an `InSession` client (e.g. garage reload after travel, row 6) gets a new
  snapshot with a new `snapshotId`; late packets of an old snapshot are ignored by id.

**What the server stores vs relays:** stores `world`, `garage`, `inventory`, `players` (records incl. last
position) and every row's sections; keeps live positions per slot in memory (`PlayerState`) and only copies them
into records on leave/save; relays movement (UDP) without storing history. Nothing is stored on clients.

### D9. Server restart while clients are connected

- Graceful (`/stop`, window closed): save → `DisconnectPacket{playerID = -1, reason = ServerShutdown}` →
  exit. Clients show "server closed" and return to the menu.
- Crash/kill: client detects a closed socket (`ClientTCP.ReceiveCallback` 0 bytes / exception, Steam
  connection closed) or no packet for 10 s (server heartbeats every 3 s); it calls `Client.Disconnect()` on
  the main thread and loads the menu with `saveGame = false`.
- After restart the server loads the last save; players join again by hand and get D8.

### D10. Client save safety

New `CMS21-Together-Client/Persistence/`:

- `SessionGuard.Active` is set at the start of `ModGameManager.StartGame` and cleared only when the Menu scene
  has finished loading after the session (not on disconnect: after a server loss the garage with server state is
  still loaded and "exit to menu" would save it).
- Harmony prefixes returning `false` while active: `GameDataManager.Save(int)` (all scene-change, sleep and
  exit saves go through it), `ProfileManager.BackupSave()`, `ProfileManager.Save()`,
  `ProfileManager.DeleteProfile()`, `ProfileManager.DeleteSelectedProfile()`. Each block is logged once per
  session. The entry point is blocked instead of `SerializeToBytes` (upstream's empty-bytes approach risks
  writing an empty file) or the generic `PlatformManager.SendSave<T>` (cannot be patched in IL2CPP).
- Profile slot: `StartGame` records the original `ProfileManager.selectedProfile` and
  `RDGPlayerPrefs.GetInt("selectedProfile", 0)` in `UserData/CMS21Together/session.json` before touching them,
  then does what it does today; the pref is set back to the original right after `ProfileManager.Load()` (the
  in-memory field stays 4). On Menu loaded / `OnApplicationQuit`: `ProfileData` array back to 4 entries,
  field and pref restored, `session.json` deleted. On `OnInitializeMelon`, an existing `session.json` means the
  last run crashed mid-session → restore the pref from it, then delete it.
- `profile4.cms21b` is never deleted by the mod: upstream 0.4 used slots ≥ 4 for its own saves, so the file
  may belong to the player. Its presence is only logged.
- Before the first `StartGame` of a game run, copy `GlobalStrings.SaveDirectory/profile*.cms21b` to
  `UserData/CMS21Together/ProfileBackups/<ts>/`, keep 5. If the copy fails, the join is aborted with a message
  (we do not enter a session without the safety net).

## Risks / Trade-offs

- [`GameDataManager.Save(int)` may not be the only path that writes profile files (e.g. Steam Cloud sync,
  `ProfileManager.Save()` writing more than names)] → harness check that every file in the save folder is
  byte-identical and no file is added, after a session with scene change, exit to menu and quit.
- [Restoring the `selectedProfile` pref right after `ProfileManager.Load()` may be too early if the game reads
  it again during the garage load] → harness reads the stored value during the session; fall back to restoring
  only at session end (crash recovery still covers it).
- [`RDGPlayerPrefs` may store in a file (`CheckFile()`) rather than the registry] → task 7.1 finds where it
  stores `selectedProfile`; the profile-safety scenario reads whichever it is, read-only.
- [Blocking `ProfileManager.Save()` could break something the game expects during a session] → watch the
  client logs in every scenario; drop that one prefix if it causes errors and rely on the file check.
- [One global `StateLock` serializes all handlers] → state work is tiny next to network I/O for 2–4 players;
  serialization under the lock is a string build only.
- [Sections serialize live state objects; a careless rename breaks old saves] → section versions + the v1
  fixture check in the harness.
- [Per-player progression changes gameplay that rows 1–6 may assume is shared (e.g. job payout exp)] → API
  `PlayerProgression.AddExp(identity, amount)`; row 3 decides who gets job exp. See open questions.
- [Teleporting the local player on rejoin (`transform` + `FPSInputController.SetCharacterControllerPosition`)
  may fight the character controller] → verify in the rejoin scenario; fall back to setting
  `CurrentProfileData.PlayerData.SetPosition/SetRotation` before the garage load.
- [Bearer key in plain text over DirectIP] → accepted for co-op; Steam transport is verified.
- [Progress-based timeout can wait long on a stuck section] → per-key logging of expected vs applied makes the
  stuck section visible.

## Migration Plan

1. Deploy server and client together (enforced by the mod version check).
2. First start of the new server: start copy + pre-migration copy of `server_save.json`, migrate v1 → v2, save.
   Old `server_save_bak*.json` files stay where they are and are not touched.
3. Rollback: stop the new server, put `backups/premigration_v1_<ts>.json` back as `Saves/server_save.json`, run
   the old build. A v2 file makes an old server fail to deserialize and create a new session (old behaviour) —
   the release notes must say to restore the pre-migration copy before downgrading.

## Open Questions / assumptions

Decisions made without asking the user; each can be changed before implementation:

1. **Per-player level/exp/skills** (brief asked for it; today they are shared). Money, scrap, garage upgrades,
   inventory and cars stay shared. If the user prefers shared progression, `players` keeps only
   position/rotation/scene/name and D7's handler changes are dropped.
2. **Single-player import is out of scope** — needs every row's DTOs and a client exporter.
3. **Duplicate identity: first connection wins**; the second is rejected.
4. **No automatic reconnect** after a server restart; the player rejoins from the menu.
5. **Corrupt/unloadable save: refuse to start** rather than quarantine-and-start-fresh.
6. **Identity file per install** (`UserData/CMS21Together/player.json`); `Setup-TestInstalls.ps1` must not copy
   it between installs, or A and B collide as one player.
7. Task group 2 may land ahead of row 1 so rows 1–6 can plug in (roadmap order is otherwise kept).
