# Proposal

## Why

The dedicated server writes `Saves/server_save.json` with no version, so every feature change can silently break
it; a crash between `RotateBackups()` and the write leaves no main file and the next start overwrites the session
with a new one; `/stop` exits without saving; and players are only known by slot number. Rows 1–6 each need to add
state to the save and to the join sync, and today they would all edit the same `OnAskForSync` and `ModGameState`
with no agreed order or lock. On the client, joining persists `selectedProfile = 4` and nothing stops the game from
writing `profile4.cms21b` or leaving the profile list broken after a crash; upstream 0.4 lost player saves this way
(#105, #96, #92).

## What Changes

Split in two parts that land at different times (see tasks.md):

**Contract (lands first, no gameplay change)**
- **BREAKING (save file)**: the server save becomes a versioned envelope (`SaveVersion` 2) with one versioned section
  per state area; today's file (v1) is migrated on load after a pre-migration copy.
- Server extension points for rows 1–6: `ISaveSection`, `ISnapshotProvider` (discovered by `[SessionSection]`),
  `SyncOrder` slots in Core, and one state lock `GameDataManager.StateLock` around every handler, command and tick.
- One join path for first join, late join, rejoin and garage return: `SyncBegin` → snapshot sections in
  `SyncOrder` → `SyncEnd` (item counts) → client `SyncAck`. Broadcasts skip clients that have not got their snapshot
  yet; the client's 10 s/15 s absolute timeouts become a 30 s no-progress timeout.
- `save` server command and a harness-only `--command-file`.

**Rest (lands last)**
- Crash-safe save (temp file + `File.Replace`), rolling backups and start copies, fallback to the newest loadable
  backup, quarantine, refusal to start over unloadable or newer saves, `--check-save`, autosave config, saves on
  leave, `/stop` and window close.
- Player identity: a per-install key (DirectIP) or the verified Steam ID. The server keeps only identity, name and
  last garage position/scene per player and restores the position on rejoin. Money, scrap, level, XP, skills, garage
  upgrades, inventory and cars stay shared (user decision).
- Server-side drop of state packets from clients that have not acknowledged their snapshot; client detects a lost
  server and returns to the menu without saving.
- Client save safety: Harmony prefixes block `GameDataManager.Save(int)`, `ProfileManager.Save()`,
  `ProfileManager.BackupSave()`, `ProfileManager.DeleteProfile()`, `ProfileManager.DeleteSelectedProfile()` and
  `PlatformManager.DeleteSave(string)` during a session; `selectedProfile` (field and `RDGPlayerPrefs`) and the
  4-slot `GameDataManager.ProfileData` are restored after the session and after a crash (prefix on
  `ProfileManager.Awake()`); local profiles are backed up before the first session of a run.

**Packets**: new `SyncBegin`, `SyncAck` (contract), `PlayerRestore` (rest); changed `SyncEnd` (+`snapshotId`,
`Items`, contract), `ConnectPacket` (+`playerKey`), `DisconnectPacket` (+`reason`).

**Out of scope**: importing a single-player save (backlog), per-player progression, automatic reconnect, several
saves per server.

## Capabilities

### New Capabilities
- `session-persistence`: server save format, versioning, migration, backups and autosave; the contract rows 1–6 use
  to persist and sync their state; player identity and per-player records; the join/rejoin/late-join sync order;
  behaviour on server restart; and the guarantee that a client never writes the player's local profiles during a
  session.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (appended), `Network/Packets/StartPackets.cs`, `Network/Packets/WorldStatesPackets.cs`,
  new `Data/SyncOrder.cs`, new `Data/PlayerRecord.cs`.
- Server: `Data/GameDataManager.cs` + new `Data/Persistence/` (sections, registry, README), `Data/ServerConfig.cs`,
  `Program.cs` (load before start, `--check-save`, `--command-file`, console close handler),
  `Network/CommandSystem.cs` (`save`, `players`, `stop` saves), `Network/Client.cs` (sync state, identity),
  `Network/Server.cs` (`SendToClients` filter), `Network/Transport/TCP.cs`, `UDP.cs`, `SteamTransport.cs` (lock,
  `SteamID`), `Network/Handlers/AuthHandlers.cs`, `Log/ServerWindow.cs` (save tick).
- Client: `Logic/LoaderAddition.CustomLoad`, `Network/Handlers/WorldStatesPackets.cs`, `Network/Handlers/AuthHandlers.cs`,
  `Network/Transport/ClientTCP.cs`/`ClientSteam.cs` (server loss), `Managers/ModGameManager.StartGame`,
  `Utils/SaveUtils`, new `Persistence/` (sync tracker, save guard, identity, profile backup), `MainMod`.
- Rows 1–6 register their state as `ISaveSection`/`ISnapshotProvider` in their `SyncOrder` slot instead of editing
  `OnAskForSync` or adding "synced" flags.
- Test harness: `Features/SessionCommands.cs`, `StateDump` (`snapshotId`, `syncAcked`), server control in
  `Run-Session.ps1`/`HarnessClient.psm1`, scenarios `server-restart`, `rejoin`, `latejoin`, `persistence-restart`,
  `profile-safety`, `duplicate-identity`; `Setup-TestInstalls.ps1` must not copy the player key between installs.
