# Design

## Context

See proposal.md for the motivation. Today's code (observed, `main`):

- `MainMod.OnUpdate`: F5 → `Client.ConnectToServer("127.0.0.1")`, F6 → `Client.ConnectToSteamServer()` with
  `Client.ServerID = DEV_TEST_STEAM_SERVER_ID`. Both set `IsConnected = true` before the transport connects and
  subscribe `OnConnectionValidated`; nothing resets them when `ClientTCP.ConnectCallback` throws (connection refused)
  or `ClientSteam` reaches `Dead`/`ClosedByPeer`, so a failed attempt blocks every later one until restart.
- Handshake: on TCP accept / Steam `Connected` the server sends `ConnectPacket{playerID, modVersion}`; the client
  answers with its own `ConnectPacket` (DirectIP from `AuthHandler.HandleConnect`, Steam from
  `ClientSteam.OnConnected`); the server's `AuthHandler.OnConnected` compares `modVersion` and on mismatch only sends a
  `DisconnectPacket{message}` without closing and with `playerID = 0`, which the client's `HandleDisconnect` reads as
  "another player left" (the welcome already set `Client.ID` to the slot, ≥ 1), so the refusal is never shown. The
  first server `Heartbeat` triggers `OnConnectionValidated` → `ModGameManager.StartGame` → garage load →
  `AskForSync`. A full server closes the TCP socket without a message before any slot is assigned (DirectIP) or
  closes the Steam connection with end code 0 and the debug string "Max Connection Exceeded".
- `HeartbeatPacket` is empty; the server sends one every 3 s (unreliable), the client echoes it; 10 s timeout.
- `MainMod.IsSteamAvailable` needs `UserLibs\steam_api64.dll`; the game ships its own copy in
  `Car Mechanic Simulator 2021_Data\Plugins\x86_64`, the harness installs have none in `UserLibs` (Steam is off there).
- Server: `ServerConfig` reads `max_players`, `use_steam`, `GSLT_Token`, `log_level` and `port` (default
  `NetworkConstants.DEFAULT_PORT` = 7777; landed with row 7 task 1.3) from `server_config.ini`; `Server.Start(max,
  Config.Port)` binds TCP and UDP on `IPAddress.Any` (IPv4 only) and the Steam game server uses `GamePort = port`,
  `QueryPort = port + 1`. The client's `Client.ConnectToServer(address)` already parses `host:port` (default 7777) and
  the harness `connect <ip:port>` uses it; test lanes run servers on 7777 (lane 1) and 7787 (lane 2) in parallel.
  Anonymous Steam logon gives a server Steam ID that probably changes per start (ROADMAP spike). `/kick <id>` exists
  as a console command; `/stop` is `Environment.Exit(0)` without a save until row 7 task 3.6. After a failed start
  (database or save load) `Program.Exit` waits for a key press, so the process does not exit by itself.
  Difficulty is `Gamemode.Normal` in the `world` section's `Reset()` (row 7, replaced `CreateNewSession`'s defaults);
  the client applies `WorldState.Gamemode` via `updateGamemode`.
- Game UI (stubs): `UIManager.Get().ShowPopup(string title, string text, PopupType)`, `ShowInfoWindow(string)`,
  `ShowAskWindow(...)` (garage scenes); `CMS.MainMenu.MainMenuManager` (`mainSection`, `OpenPlaySection`,
  `StartWithDifficulty`), `CMS.MainMenu.Sections.MainSection` (`buttons: MainMenuButton[]`, `Prepare`). 0.4.17's
  `NewUI` (MIT, same project) cloned `MainMenuButton`, the main input field and a `StringSelector` as templates
  (~1450 lines). `UnityEngine.IMGUIModule` is referenced by the client; FixForTogether uses `OnGUI` on this game.
- Owned elsewhere: display name preference `CMS21Together.PlayerName` and the presence roster (row 6);
  `DisconnectPacket.reason`/`DisconnectReason`, `playerKey`, `--command-file`, `/stop` with save and server-loss
  detection on the client (row 7); mod/game/DLC handshake checks (row 9); zip layout (row 12).

## Goals / Non-Goals

**Goals:**
- One join path (`JoinService`) used by the menu, rich-presence joins, dev hotkeys and the harness, with one status
  model that always ends in Idle, InSession, Failed or Disconnected — never stuck.
- A view-independent menu model so the UI spike's outcome changes only the view layer, and scenarios drive the model.
- Joining a friend without exchanging IDs by hand (Steam rich presence), with typed IP/ID as the fallback.

**Non-Goals:**
- A server browser / master server (friends only).
- Bans, admin roles beyond "holds the admin key", text chat (backlog).
- New handshake checks other than the password (row 9 owns version/game/DLC/mod checks; this change displays them).
- Server-loss detection while in session (row 7 group 5); this change shows its result.
- Running the server inside the game process (the dedicated server stays a separate exe).

## Decisions

### D1. UI technology: spike first, model/view split either way
Task 1.1 spikes both options on IL2CPP/MelonLoader 0.5.7 in the main menu and the garage: (a) IMGUI from
`MelonMod.OnGUI` (`GUILayout` window with a text field, buttons, a list; check stripped IMGUI overloads, text input
not leaking to the game's key bindings, mouse cursor visible in the garage), (b) cloning `MainMenuButton` /
the menu input field / `StringSelector` like `NewUI`. Criteria: works in Menu and Garage, text input usable, effort,
and survival of game updates (none expected). The result is recorded here.
- All behaviour lives in `UI/MultiplayerMenuModel` (panels, fields, commands: `Join(target)`, `HostStart(settings)`,
  `HostStop()`, `Kick(id)`, `SetName(name)`; read-only: status, last error, roster rows, friends) and in services
  (`JoinService`, `LocalServerHost`, `RichPresence`). Views only render the model and call its commands.
- **Outcome IMGUI:** `UI/Imgui/` views drawn from `MainMod.OnGUI`: a "Multiplayer" button in the menu corner, the
  panel window, the status overlay and toasts. In the garage the session panel opens with a hotkey (MelonPreferences
  `CMS21Together.SessionPanelHotkey`, default F9; F7 and F8 are row 14's resync and bug-report keys) and
  frees the cursor while open (`Cursor.lockState/visible`, restored on close); the game's input is blocked while a
  text field has focus via a prefix on the game's input polling only if the spike shows keys leak.
- **Outcome cloned UI:** the menu entry is a cloned `MainMenuButton` added in a postfix on `MainSection.Prepare` and the
  menu panels are built from the cloned templates (adapted from `NewUI`, MIT, keep upstream copyright). The in-game
  session panel and the status overlay stay IMGUI (the game has no reusable in-game window template; `NewUI` never
  had one) unless the spike finds one.
- Notifications use one API `ModNotify.Toast(text)` / `ModNotify.Message(title, text)`: in garage scenes
  `UIManager.Get().ShowPopup(title, text, PopupType.Normal)` if the spike shows it renders and queues; otherwise and
  in the menu an IMGUI toast stack. Row 14a's guard messages (`multiplayer-guard` D3) and row 14's desync notice use the same API.
- *Alternative:* UIElements/uGUI from scratch — rejected: no asset pipeline for IL2CPP-injected uGUI prefabs, more
  code than either option.

### D2. Join targets and `JoinService`
- `JoinTarget` (client, `Session/`): `Ip(host, port)` or `Steam(serverId)`. Parsed from user text or a join string:
  `host`, `host:port` (IPv4 address or host name; the server listens on IPv4 only, so an IPv6 literal is rejected
  with that reason), a 17-digit Steam ID, or `CMS21Together:ip:<host>:<port>` / `CMS21Together:steam:<id>`. Invalid
  input never starts a connection (message in the panel); a host name that does not resolve fails as `Unreachable`.
- `JoinService.Join(target)`: refuses while not Idle/Failed/Disconnected; calls the existing
  `Client.ConnectToServer("<host>:<port>")` (its `ParseAddress` stays the single address parser for the transport) or
  `Client.ConnectToSteamServer(id)` (new parameter; `DEV_TEST_STEAM_SERVER_ID` and the field default are removed);
  stores the target as `CMS21Together.LastJoinTarget` (MelonPreferences) on reaching InSession.
- F5/F6 move behind `CMS21Together.DevHotkeys` (default false): F5 = join `LastJoinTarget` (or 127.0.0.1), F6 removed.
- The harness `connect` verb calls `JoinService.Join(Ip(...))` so scenarios exercise the same path.

### D3. Connection status
`ConnectionStatus` (client, main thread only; transport callbacks marshal through `ThreadManager`):

```
Idle → Connecting (transport) → Handshake (server ConnectPacket received) → Loading (OnConnectionValidated → StartGame)
     → Syncing (row 7 SyncBegin) → InSession (row 7 SyncAck sent)
any → Failed(reason, detail)        before InSession
any → Disconnected(reason, detail)  after InSession
Failed/Disconnected → Idle          when the menu has shown the message (or on the next Join)
```

- Failure sources: TCP connect exception or refused → `Unreachable`; no server `ConnectPacket` within 10 s (DirectIP)
  / 20 s (Steam, relay setup is slower) → `Timeout`; `!IsSteamAvailable` for a Steam target → `SteamUnavailable`;
  Steam `ConnectionState.Dead/ClosedByPeer/ProblemDetectedLocally` before InSession → `SteamFailed` with
  `info.EndReason`; app end code 1001 → `ServerFull`; a received `DisconnectPacket` → its row 7 `DisconnectReason`
  plus `message`; row 9's client-side welcome check (version/protocol mismatch found locally, M1 after this change)
  calls `ConnectionStatus.Failed(VersionMismatch, text)` directly. Row 7's no-progress sync timeout (30 s,
  `SyncTracker.HasTimedOut`) reports `SyncFailed` through the same call.
- Refusals reach the client: a refusal sent before a slot exists (DirectIP server full) carries `playerID = -1`; a
  refusal of an assigned slot goes through one server helper `Server.Refuse(clientId, reason, message)` (owner: this
  change, task 2.4, M1 before row 9): `DisconnectPacket{playerID = clientId, reason, message}` (today `playerID` is 0,
  see Context), then on the next server tick `Client.Disconnect()` plus `SteamConnection.Close()` for a Steam slot, so
  the packet is flushed first. Row 9's compatibility refusals, the password check (part 2) and `/kick`/`KickRequest`
  (D9) all call it.
- Every failure runs one `Client.ResetAfterFailure()` (close transports, `IsConnected = false`,
  `IsConnectionValid = false`, unsubscribe, `ClientData.Reset()`), and if the garage or a loading scene is active,
  loads the menu with `saveGame = false` (row 7 D10's rule; `SessionGuard` keeps local saves safe).
- Messages: `ConnectionMessages.For(reason, detail)` (client) maps each reason to one English sentence plus detail,
  e.g. `VersionMismatch` → "This server runs Together <server>; you have <client>." (both versions come from the
  server's `message`, or from the welcome when row 9's client-side check refuses). Row 9's reasons
  (`GameVersionMismatch`, `DlcMismatch`, `ModMismatch`) show the server's `message` under a fixed headline. The menu
  shows the last Failed/Disconnected message once after it loads.
- Harness: `StateDump.Status` (written continuously, polled by `Wait-HarnessStatus`) gains `joinStatus` and
  `lastDisconnect { reason, message }` — the field row 9's tasks already name, owned here because this change lands
  first in M1; row 9 reuses it instead of adding it.
- Client-only reasons (`Unreachable`, `Timeout`, `SteamUnavailable`, `SteamFailed`) live in a client enum
  `JoinFailure`; they never travel over the network, so row 7's `DisconnectReason` gets only server-sent values.
- *Why the client owns display:* row 7's server-loss detection lands in M5, after this; it calls
  `ConnectionStatus.Disconnected(reason)` instead of showing its own message.

### D4. `ServerInfo` and new server settings
- Packet `ServerInfo { ServerName, SteamId (0 = none), PublicAddress ("" = none), Port, MaxPlayers, PasswordRequired,
  IsAdmin, Difficulty }`, server → client, sent right after the client's `ConnectPacket` is accepted (before
  `AskForSync`; `Server.SendToClient` targets the slot directly, so row 7's `Connected`-state broadcast filter does
  not apply). Stored in `ClientData.ServerInfo`, cleared by `ClientData.Reset()`.
- `server_config.ini` (which already has `port`) gains `server_name = ""`, `public_address = ""` (part 1) and
  `password = ""`, `password_steam = False`, `admin_key = ""`, `new_session_difficulty = Normal` (Easy/Normal/Expert;
  Sandbox skipped, user decision) (part 2). Keys missing from an existing file take their defaults. Every key can be
  overridden from the command line (`--port`, `--max-players`, `--use-steam`, `--server-name`, `--public-address`
  in part 1; `--password`, `--admin-key`, `--new-difficulty` in part 2) so the game can host without editing the
  host's config file. The server logs the effective settings with password and admin key masked.
- **Server stores** (config only, not in the save): name, public address, password, admin key, difficulty for new
  sessions. **Per connected client (runtime):** `IsAdmin`, last RTT. Nothing new is saved in `server_save.json`.

### D5. Steam rich presence and join requests
- When `IsSteamAvailable` and status reaches InSession, `RichPresence.Publish()` sets `connect` to the join string and
  `status` to "Together – <server name or host's name> (n/max)": `steam:<ServerInfo.SteamId>` if non-zero, else
  `ip:<PublicAddress>:<Port>` if set, else `ip:<host>:<port>` when the joined host is not a loopback/private address,
  else no `connect` (status only; the panel says why friends cannot join). Updated on roster size change; cleared on
  leaving InSession and in `OnApplicationQuit`. `CMS21Together.AdvertisePresence` (default true) turns it off.
- Warm join: `SteamFriends.OnGameRichPresenceJoinRequested(friend, connect)` → parse (D2) → if already in a session,
  ask in the panel ("Leave this session and join <friend>?"), else `JoinService.Join`.
- Cold start: at `OnLateInitializeMelon`, read `SteamApps.CommandLine`, falling back to `Environment.GetCommandLineArgs()`
  (`+connect <string>` or an argument containing `CMS21Together:`). The target is held until the Menu scene is ready
  (`OnSceneWasInitialized("Menu")` + `NotificationCenter.IsGameReady`) and then joined once.
- Invites (part 2): `Friend.InviteToGame(joinString)`; the receiver gets the same warm/cold paths.
- The same parser and join path run in the harness without Steam (`mp-join-string`), so everything except the Steam
  calls themselves is scenario-tested; the real friend test is a user step.
- Idea and prefix follow FixForTogether's `SteamInviteReceiver`/`LobbySteamInvite`; implemented independently. If code
  is adapted from them, credit TogetherFixer (license).

### D6. Player name field
The panel's name field reads and writes row 6's `CMS21Together.PlayerName` preference (no second setting) and is
disabled while connected (the name is sent once in `ConnectPacket.username`; renaming in session is out of scope).
Row 6's server-side sanitising stays the only validation; the field trims to 24 characters as a hint.

### D7. Hosting from the game (part 2)
- `LocalServerHost` finds the server at `CMS21Together.ServerPath` (default `<game>\TogetherServer\CMS21_Together_Server.exe`,
  where row 12's client zip puts it). Missing exe → the Host panel shows the path it looked for.
- Start: refuse if a `CMS21_Together_Server` process with the same exe path already runs (other paths — e.g. another
  test lane's server — are ignored) or the port is taken (try to bind a `TcpListener` and a `UdpClient` on it and
  release them; a connect probe would occupy a server slot until its 10 s heartbeat timeout); generate an admin
  key (GUID, memory only); start the exe with `--port`, `--max-players`, `--use-steam`, `--password`, `--admin-key`,
  `--command-file <UserData\CMS21Together\host_commands.txt>` (row 7's mechanism) and, when no save exists in
  `<server>\Saves\`, `--new-difficulty <choice>`; `UseShellExecute = true`, minimized window (Terminal.Gui needs a
  console; the host can open it).
- Ready: delete `<server>\Log\Latest.txt` before the start, then wait up to 45 s (database load + Steam logon) for
  its line "Server started. Listening port" (the same signal as the harness's `Start-TestServer`). The process
  exiting first, or the timeout (a failed start waits for a key press in `Program.Exit`, so it never exits by
  itself) → kill it and Failed with the last 20 lines of `Latest.txt`. Then `JoinService.Join(Ip(127.0.0.1, port))`
  with the admin key. Rich presence then advertises the server's Steam ID from `ServerInfo` (D5).
- Stop: write `/stop` to the command file (row 7: save, `ServerShutdown` to all, exit), wait up to 15 s for exit,
  then kill and log a warning. The game leaves the session first (`saveGame = false`). Quitting the game while hosting
  runs the same stop from `OnApplicationQuit` (bounded 10 s). Closing the console window is row 7's
  `CTRL_CLOSE_EVENT` save; the game then sees `Disconnected(ServerShutdown)`.
- Continue vs new: the panel shows "Continue" when `Saves\server_save.json` exists, else "New session" with difficulty.
  Starting over is a separate, confirmed action that moves the whole `Saves` folder to `Saves_old_<ts>` before start
  (never deletes), so row 7's backup fallback cannot reload the old session.
- *Alternative:* in-process server — rejected (Terminal.Gui, separate Steam game-server identity, crash isolation).

### D8. DirectIP password (part 2)
`ConnectPacket.password` and `ConnectPacket.adminKey` (D9) are `[OptionalField]` like row 9's new fields, so a
client of another build still deserializes and gets the version refusal. Neither value is ever written to a log on
either side. `ConnectPacket.password` is plain text over TCP (friends' co-op, documented). `AuthHandler.OnConnected` checks it after
the version check (row 9's checks run first so a mismatched client sees the version message): config password
non-empty and connection type DirectIP and mismatch → `DisconnectPacket{reason = WrongPassword}`, then close the slot.
Steam connections skip the check by default (`password_steam = false`; the join string reaches friends only). The
client shows a password field when the last failure was `WrongPassword` and retries with it; the password is
remembered per target only for the running game.

### D9. Admin and kick (part 2)
- Admin = a client whose `ConnectPacket.adminKey` equals the configured/argument admin key (non-empty). The game that
  hosts passes its generated key; a dedicated-server owner can put the same key in `CMS21Together.AdminKey`. That
  preference is a secret: bug-report bundles (row 14 (d)) and the offline log collector (row 12) redact it by the shared redaction rule
  (INTEGRATION.md: names containing `key`, except `*Hotkey*`).
- `KickRequest { PlayerId }` (C→S): accepted only from an admin client in `InSession` and not for itself; the server
  runs the same code as `/kick <id>`: `DisconnectPacket{playerID, reason = Kicked}` to the target, then
  `Client.Disconnect(DisconnectReason.Kicked)` — a new optional parameter that row 6's leave broadcast
  (`DisconnectPacket{playerID}` to the others) copies into `reason`, so D11 can say "was kicked". Non-admin requests
  are dropped with a warning. A kicked player can rejoin (no bans).

### D10. Ping and player list (part 2)
- `HeartbeatPacket.sentTicks` (server `Stopwatch` ticks) is echoed by the client; the server stores
  `Client.RttMs` (EMA, α = 0.3). Every 3 s, after the heartbeat round, the server sends
  `PlayerPings { Dictionary<int, int> Ms }` (unreliable) to all `InSession` clients.
- RTT updates and the `PlayerPings` broadcast run in `Client.Update` and the heartbeat handler, both already under
  row 7's `GameDataManager.StateLock`; `KickRequest` runs under the dispatch lock. No new lock.
- Player rows = row 6's client roster (name, scene) + own entry + `PlayerPings`; no new presence data. The admin sees a
  kick button per remote row.

### D11. Join/leave notifications (part 2)
Toasts "<name> joined" / "<name> left" (/ "was kicked" when the leave carried `Kicked`) from a client event on row 6's
roster: this change adds `PresenceManager.PlayerAdded(record, fromSnapshot)` / `PlayerRemoved(record)` to row 6's
code (additive). Roster entries from the join snapshot (`fromSnapshot`) raise no toast; the joining player gets one
summary toast "<n> players online".

### D12. Late join
Nothing is replayed by this change: `ServerInfo` is sent per connection; `PlayerPings` arrive within 3 s; the roster
and names come from row 6's `players` snapshot; rich presence is set locally on InSession. No `ISnapshotProvider`,
no `SyncOrder` slot, no save section.

## Risks / Trade-offs

- [IMGUI stripped or broken on IL2CPP (missing overloads, window callbacks)] → spike first; cloned UI for the menu,
  and the session panel falls back to a minimal list drawn with `GUI.Label`/`GUI.Button` only.
- [Facepunch `SteamClient` and the game's own Steam API in one process fight over rich presence or callbacks] →
  spike logs `SteamFriends.GetRichPresence` of the local user after setting it; if the game overwrites it, re-publish
  on change (poll every 10 s).
- [Anonymous server Steam ID changes per start] → harmless with rich presence (the ID is read from `ServerInfo` each
  session); a typed ID is only the fallback. GSLT stays optional.
- [Relay/NAT: Steam join fails over the internet] → user test with a real friend (task 4.5, during the M1 playtest);
  DirectIP with port forwarding documented by row 12 as fallback.
- [Password in plain text] → accepted for friends' co-op, documented; not reused for anything else.
- [Hosting leaves a server process behind after a game crash] → next Host start detects the running process and offers
  "Stop it" (sends `/stop` through the command file, then kills); harness `finally` already kills the lane's server.
- [Text input leaks to game hotkeys (WASD, Tab)] → spike checks; block input only while a field is focused.

## Migration Plan

No save data changes. `ConnectPacket`, `HeartbeatPacket` and `DisconnectReason` change shape and `PacketTypes` gains
entries, so client and server must run the same build (existing version check). `server_config.ini` keys are
optional with defaults; old config files keep working. Rollback = revert the change.

## Open Questions

Decisions taken without the user (recorded in review.md as defaults):
- Password checked for DirectIP only (`password_steam = false`).
- A hosted server keeps running when the host only leaves the session (others keep playing); it stops on Host → Stop
  or when the host quits the game.
- Dev hotkeys off by default in release builds.
- Session panel hotkey F9 (F7/F8 are taken by row 14).
