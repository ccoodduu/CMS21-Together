# Proposal

## Why

The connect handshake compares the mod version string only. `ConnectPacket.gameVersion` is sent empty and never
checked, nobody compares owned DLC or installed gameplay mods, and two dev builds with the same version string but
different packets connect and then fail in odd ways. The refusal itself is broken: the server's `DisconnectPacket`
leaves `playerID = 0`, so the client treats it as "another player left", never shows the message and the slot stays
open. Friends join from M1, so a mismatch has to be refused up front with a reason they can act on.

The server also needs game data it cannot read itself (`Database/*.json`); the three files in the repo were exported
once upstream with a tool that is not in the repo, and row 16 (`server-game-logic`) needs more tables.

## What Changes

Two parts that land at different times (see tasks.md):

**Part 1 — handshake checks (M1)**
- Both sides compare mod version and a **protocol hash** (computed from Core's packet types and their fields). The
  client also checks the server's welcome, so a mismatch is reported even when the other side's packet cannot be
  read.
- **Game version**: the client sends `GameSettings.BuildVersion` (today `1.0.40`); the server compares it with
  `game_version` from its config (`auto` = `Database/meta.json`, otherwise the first accepted client of the run).
- **DLC set**: the client sends the product ids of owned DLC (`PlatformManager.GetDLCs()`, `DLC.Owned`); the server
  compares the set with `dlc` from its config (`auto` = first accepted client of the run, `none` = no DLC).
- **Gameplay-mod check**: the client reports every loaded MelonLoader mod/plugin with the game methods its Harmony
  patches target (`Harmony.GetAllPatchedMethods()` + `Harmony.GetPatchInfo`, attributed to a mod by the patch
  method's assembly). A classifier in Core sorts each mod into *gameplay*, *visual* or *unknown* from those targets;
  the server applies its `mods_required`, `mods_ignored` and `mods_gameplay` lists and refuses a client whose
  gameplay mods do not match. Visual mods are ignored. This mod and the test harness are never counted.
- Refusal with a clear reason: `DisconnectReason` (row 7) gains `GameVersionMismatch`, `DlcMismatch`,
  `ModMismatch` (appended); the message names expected vs. actual values and the offending mods. The refusal reaches
  the refused client (fixes the `playerID = 0` bug) and frees the slot.
- `compat` server command; harness verbs `compat-report`, `compat-override`.

**Part 2 — tuning and game data (M6, exporter earlier, see design D8)**
- Tune the classifier on real mod lists (the user's and friends' installs) and add a table of known mods.
- Game-data exporter: a dev-only client command writes the server's `Database/*.json` in the current schema plus
  `Database/meta.json` (game version, exporter version, time). Row 16 adds its tables to the same exporter.

**Packets**: changed `ConnectPacket` (fills `gameVersion`; adds `protocolHash`, `dlc`, `mods` as optional fields,
both directions use the welcome too); `DisconnectPacket.reason` gains three appended values. No new packet types.

**Hooks**: none on gameplay. Reads `GameSettings.BuildVersion`, `PlatformManager.GetDLCs()`, `GameInventory.PartPropertyList`
(exporter), `MelonHandler.Mods/Plugins`, Harmony's patch registry.

**Out of scope**: running with gameplay mods (modded items/parts, TK Aftermarket, `RegisterModItem`, QoLmod support —
backlog), unloading or disabling mods while connected, verifying that a client tells the truth (friends' co-op).

## Capabilities

### New Capabilities
- `session-compatibility`: which differences between a joining client and the server (mod build, game version,
  DLC, gameplay mods) are refused, how gameplay mods are told apart from visual ones, how the server operator
  configures it, and what the refused player is told.
- `game-data-export`: producing the server's game database from an installed game and recording which game version
  it came from.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `Network/Packets/StartPackets.cs` (`ConnectPacket` fields, `[OptionalField]`), new `Data/Compatibility/`
  (`ModReport`, `PatchTarget`, `ModClassifier`, `ProtocolHash`), `DisconnectReason` (appended values; enum owned by
  row 7).
- Server: `Network/Handlers/AuthHandlers.cs` (`OnConnected` runs the checks before row 7's identity step),
  `Network/Server.cs` / `Network/Transport/SteamTransport.cs` (welcome carries `protocolHash`; `Server.Refuse` closes
  the slot, Steam connection included), `Network/Transport/TCP.cs` (undeserializable `ConnectPacket`), new
  `Data/CompatibilityPolicy.cs`, `Data/ServerConfig.cs` (`game_version`, `dlc`, `mods_required`, `mods_ignored`,
  `mods_gameplay`), `Network/CommandSystem.cs` (`compat`), `Data/GameDatabase.cs` (reads `meta.json`).
- Client: `Network/Handlers/AuthHandlers.cs` (check the welcome, then send for both transports) and
  `Network/Transport/ClientSteam.cs` (stops sending its own `ConnectPacket`), new `ConnectPacketFactory`, new
  `Compatibility/` (`LocalEnvironment`, `ModInventory`), dev-only `Tools/DatabaseExporter/`.
- Test harness: `Features/CompatCommands.cs` (`compat-report`, `compat-override`, `db-export`), scenario
  `compat-refusal.ps1`.
- Release/docs (row 12): the install guide says gameplay mods must be removed (or listed by the host).
