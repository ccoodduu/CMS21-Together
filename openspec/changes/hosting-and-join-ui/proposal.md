# Proposal

## Why

There is no multiplayer UI at all. F5 connects to 127.0.0.1 and F6 connects over Steam to a server ID that is
hardcoded in `Client.DEV_TEST_STEAM_SERVER_ID`, so a friend cannot join anything but the developer's own server
without a rebuild. A failed connect is silent and leaves `Client.IsConnected = true`, so the next F5 does nothing;
a version mismatch only reaches the log; and the host has no way to start, stop or administer the server from the
game. M1 needs friends to join without typing IDs by hand, and M4 needs hosting from the game (ROADMAP row 8).

## What Changes

Part 1 (M1: join without a hardcoded server ID, status and errors):
- **UI technology spike first**: IMGUI (`MelonMod.OnGUI`) vs cloning the game's main-menu UI (0.4.17's `NewUI`). The
  menu logic is written against a view-independent model, so either outcome only changes the view layer.
- **Join targets**: a Multiplayer panel in the main menu joins by `host[:port]` (DirectIP) or by a typed Steam server
  ID; the last target is remembered. The hardcoded server ID is removed; F5/F6 become opt-in dev hotkeys.
- **Steam rich presence**: while in a session the client advertises a join string (`CMS21Together:steam:<id>` or
  `CMS21Together:ip:<host>:<port>`) as its Steam `connect` rich presence, so friends can use Steam's own
  "Join Game" and invites. A join request while the game runs, and a cold start with a join string on the command
  line, both join automatically once the main menu is ready.
- **Connection status and errors**: one client status (Idle, Connecting, Handshake, Loading, Syncing, InSession,
  Failed, Disconnected) shown as an overlay while joining; every failure and disconnect ends in a readable message
  in the main menu (unreachable, timeout, Steam unavailable, version mismatch with both versions, kicked, server
  shutdown, duplicate identity, sync failed). After any failure the client can join again without restarting.
- **Player name field**: the panel edits the display-name preference that `sync-players-and-scenes` owns.
- **Server info**: the server tells a joining client its Steam ID and optional public address/name, so a client
  that joined over DirectIP can still advertise a working join string.

Part 2 (M4: full in-game menu):
- **Host from the game**: start/stop the bundled dedicated server (`<game>\TogetherServer\`) as a separate process
  with chosen settings (new-session difficulty when no save exists, password, max players, Steam on/off), join it over
  loopback, stop it with a save when the host stops or quits.
- **Steam friends panel**: friends playing CMS21 with a join string can be joined directly; online friends can be
  invited.
- **DirectIP password**: optional server password checked during the handshake.
- **In-game session panel** (hotkey in any scene): player list with name, scene (from the presence roster) and
  ping; join/leave notifications; kick for the admin (the host, or whoever holds the server's admin key).

Hooks and packets:
- Hooks: none on game methods for part 1 (menu entry via the chosen view; scene/ready detection reuses
  `MainMod.OnSceneWasInitialized` and `NotificationCenter.IsGameReady`). Cloned-UI outcome only: postfix on
  `CMS.MainMenu.MainMenuManager.Start`/`MainSection.Prepare` to add the menu button. In-game notifications use
  `UIManager.Get().ShowPopup(string, string, PopupType)`.
- Steam (Facepunch): `SteamFriends.SetRichPresence/ClearRichPresence`, `SteamFriends.OnGameRichPresenceJoinRequested`,
  `SteamApps.CommandLine`, `SteamFriends.GetFriends`, `Friend.GetRichPresence`, `Friend.InviteToGame`.
- Packets: **new** `ServerInfo` (S→C), `PlayerPings` (S→C), `KickRequest` (C→S); **changed** `ConnectPacket`
  (+`password`, +`adminKey`, both `[OptionalField]`; `username` already filled by row 6), `HeartbeatPacket`
  (+`sentTicks`, echoed for RTT). `DisconnectPacket.reason` and the `DisconnectReason` enum are
  `session-persistence-and-rejoin`'s; this change appends `ServerFull` and `WrongPassword` to that enum and makes
  refusals reach the client (`playerID` of the refused slot, or -1 before a slot exists).

## Capabilities

### New Capabilities
- `session-join`: how a player joins a server (targets, Steam rich presence and invites, connection status, failure
  and disconnect messages, retry), and the server information a client receives on connect.
- `session-hosting`: starting and stopping a local dedicated server from the game, new-session settings and the
  DirectIP password.
- `session-admin`: the in-session player list with ping, join/leave notifications and kicking players.

### Modified Capabilities
<!-- none: openspec/specs/ is empty; row 6's `player-presence` (names, roster) is used, not changed -->

## Impact

- Core: `Network/Packets/StartPackets.cs` (`ConnectPacket`, `HeartbeatPacket`), new `Network/Packets/SessionPackets.cs`
  (`ServerInfo`, `PlayerPings`, `KickRequest`), `PacketTypes.cs` (appended), `DisconnectReason` (two values appended).
- Client: `MainMod.cs` (hotkeys, `OnGUI`/view hook-up, cold-start join), `Network/Client.cs` (no hardcoded ID, failure
  reset), `Network/Transport/ClientTCP.cs`/`ClientSteam.cs` (failure reporting, connect timeout),
  `Network/Handlers/AuthHandlers.cs` (status, `ServerInfo`, password), new `UI/` (model, views, notifications),
  new `Session/` (`JoinService`, `ConnectionStatus`, `RichPresence`, `LocalServerHost`).
- Server: `Program.cs` (command-line overrides), `Data/ServerConfig.cs` (`server_name`, `public_address`; part 2:
  `password`, `password_steam`, `admin_key`, `new_session_difficulty`; `port` exists already),
  `Network/Server.cs` (server-full refusal), `Network/Handlers/AuthHandlers.cs` (refusal fix, password, admin,
  `ServerInfo`), `Network/Client.cs` (RTT, `IsAdmin`, `Disconnect(reason)`), `Network/CommandSystem.cs` (`password`,
  `serverinfo`), new session-admin handler.
- Depends on: `session-persistence-and-rejoin` (row 7) contract and group 3 (`DisconnectReason`, `--command-file`,
  `/stop` with save, `StateLock`); `sync-players-and-scenes` (row 6) part 1 (display-name preference, presence roster);
  `mod-compatibility` (row 9) for the version/DLC checks whose results this change only displays;
  `release-and-docs` (row 12) for shipping the server inside the client zip (`TogetherServer\`).
- Test harness: `Features/SessionUiCommands.cs` (`mp-*` verbs), `StateDump` (`Status.joinStatus`,
  `Status.lastDisconnect`, `session` section), `HarnessClient.psm1` (`Start-TestServer -Arguments`), `Run-Session.ps1`
  (per-scenario launch arguments), four scenarios (`join-ui`, `join-coldstart`, `host-from-game`, `session-admin`).
