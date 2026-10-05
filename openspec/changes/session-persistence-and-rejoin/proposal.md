# Proposal

## Why

The dedicated server already writes `Saves/server_save.json`, but the file has no version, every feature
change can silently break it, a crash between `RotateBackups()` and the write leaves no main file (the next
start then creates a fresh session and three autosaves later the real data has rotated out), `/stop` exits
without saving, and players are only known by their slot number, so nothing per player survives a rejoin. On
the client, joining a server points the game at a temporary profile slot 4 and persists `selectedProfile = 4`;
any game save during the session (scene change, return to menu) writes server state to `profile4.cms21b` and a
crash leaves the game pointing at a slot that does not exist in single player. Upstream 0.4 lost player saves
this way (issues #105, #96, #92). Row 7 is the last roadmap item: it makes the state from rows 1–6 survive a
restart and a rejoin, and guarantees the player's own saves are never touched.

## What Changes

- **BREAKING (save file)**: the server save becomes a versioned envelope (`SaveVersion` 2) with one versioned
  section per state area; a v1 file (today's raw `ModGameState` JSON) is migrated on load after a pre-migration
  backup. Rows 1–6 add their state as new sections through a registry instead of editing the save code.
- Atomic save (temp file + `File.Replace`), rolling backups plus a snapshot at server start, fallback to the
  newest loadable backup, quarantine of unreadable files, and refusal to start rather than silently starting
  a new session when a save exists but nothing loads (or it is from a newer version).
- Autosave interval in `server_config.ini`; additional saves when a player leaves, on `/save`, on `/stop` and
  when the server window closes. Unchanged state is not rewritten.
- Player identity: a random per-install player key (DirectIP) or the server-verified Steam ID (Steam
  transport). The server keeps a player record per identity: level, exp, skills (become per player), last
  position/rotation/scene and name. Money, scrap, garage upgrades, inventory and cars stay shared.
- One join path for first join, rejoin and late join: `SyncBegin` (manifest) → ordered snapshot sections →
  `SyncEnd` → client `SyncAck`. Live updates reach a joining client only after its snapshot; the client sends
  no state changes until the snapshot is applied. Timeout is progress-based instead of a fixed 10/15 s.
- Server loss is detected on the client (closed socket or missing heartbeat) and it returns to the menu
  without saving; the player rejoins by hand after a server restart.
- Client save safety: while a session is active, `GameDataManager.Save(int)`, `ProfileManager.BackupSave()`,
  `ProfileManager.Save()`, `ProfileManager.DeleteProfile()` and `DeleteSelectedProfile()` are blocked by
  Harmony prefixes; the original `selectedProfile` (field and `RDGPlayerPrefs`) and the 4-slot
  `GameDataManager.ProfileData` array are restored when the session ends, and recovered on next start after a
  crash; local profiles are copied to a mod backup folder before the first session.
- Packets: **new** `SyncBegin`, `SyncAck`, `PlayerRestore`; **changed** `ConnectPacket` (+`playerKey`),
  `DisconnectPacket` (+`reason`), `SyncEnd` (+`snapshotId`). `WorldState`/`GarageState` carry the receiving
  player's own level/exp/skills.
- Out of scope: importing a single-player save as the initial server state (needs the DTOs of rows 1–6 and a
  client-side exporter; left for a later change that plugs into the same section registry), automatic
  reconnect, multiple named saves per server.

## Capabilities

### New Capabilities
- `session-persistence`: the server save format, versioning, migration, backups and autosave; player
  identity and per-player records; the join/rejoin/late-join sync order; behaviour when the server
  restarts; and the guarantee that a client never writes the player's local profiles during a session.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (new entries appended), `Network/Packets/StartPackets.cs`,
  `Network/Packets/WorldStatesPackets.cs`, `Data/ModGameState.cs`, `Data/PlayerState.cs`, new
  `Data/PlayerRecord.cs`, new `Data/SyncOrder.cs`.
- Server: `Data/GameDataManager.cs` (rewritten around a section registry, new `Data/Persistence/`),
  `Data/ServerConfig.cs`, `Program.cs` (shutdown save, `--check-save`), `Network/CommandSystem.cs`
  (`/save`, `/players`, `/stop` saves), `Network/Client.cs` (identity, sync state), `Network/Server.cs`
  (broadcast only to synced clients), `Network/Transport/SteamTransport.cs` (set `SteamID`),
  `Handlers/AuthHandlers.cs`, `Handlers/StatsHandlers.cs`, `Handlers/GarageUpgradeHandlers.cs`,
  `Handlers/PlayerHandlers.cs`, `Log/ServerWindow.cs` (autosave tick).
- Client: `Managers/ModGameManager.StartGame`, `Utils/SaveUtils`, new `Persistence/` (save guard hooks on
  `GameDataManager.Save`, `ProfileManager.BackupSave/Save/DeleteProfile/DeleteSelectedProfile`, profile slot
  and `selectedProfile` restore, profile backup, identity file), `Network/Handlers/AuthHandlers.cs`,
  `WorldStatesPackets.cs`, `Logic/LoaderAddition.CustomLoad`, `Network/Client.cs` (server-loss detection),
  `MainMod` (`OnInitializeMelon` crash recovery, `OnApplicationQuit`).
- Rows 1–6 register their state as save sections and snapshot providers (contract in design.md).
- Test harness: `tools/TestHarness/Features/PersistenceCommands.cs`, `StateDump` split of shared vs per-player
  stats, scenarios `persistence-restart.ps1`, `rejoin.ps1`, `latejoin.ps1`, `profile-safety.ps1`;
  `Setup-TestInstalls.ps1` must not copy a player key between installs.
