# Review: hosting-and-join-ui (2026-10-06)

Self-review against ROADMAP (row 8, milestones M1/M4, integration notes, working rules), INTEGRATION.md, QUESTIONS.md,
the drafts of rows 6 and 7, and the code on `main` (`MainMod`, `Client`, `ClientTCP`, `ClientSteam`, `ModGameManager`,
server `Program`, `ServerConfig`, `SteamTransport`, `AuthHandlers`, `Server`, `CommandSystem`).

## Conflicts and integration points (for the next integration pass)

1. **`DisconnectReason` is row 7's (task 3.6, M0).** This change appends `ServerFull` and `WrongPassword` (task 2.4).
   Recommendation: declare the enum append-only in INTEGRATION.md like `PacketTypes`, with row 8 as user of those two
   values.
2. **Reason display overlaps row 7.** Row 7 D10 / task 5.3 (M5) loads the menu "showing `DisconnectPacket.reason`".
   Row 8 part 1 (M1) lands first and owns the display (`ConnectionStatus`, `ConnectionMessages`, menu message).
   Recommendation: row 7 task 5.3 detects server loss and calls `ConnectionStatus.Disconnected(reason)` instead of
   showing its own message.
3. **`--command-file` is "harness-only" in row 7's proposal.** Row 8 uses it in production to stop a hosted server
   (`/stop` with save). Recommendation: row 7 calls it a supported option (nothing changes in its code). Alternative
   if rejected: a named pipe in row 8 (≈ +1 session).
4. **Row 6 client roster events.** Row 6 publishes server events only; row 8 adds `PresenceManager.PlayerAdded(record,
   fromSnapshot)` / `PlayerRemoved(record)` to row 6's client code (task 7.2, additive). Add to the API matrix.
5. **Row 6 owns the name.** The join panel edits row 6's `Together.PlayerName`; no second setting (D6). Consistent with
   row 6 D2 ("in-game UI is ROADMAP row 8").
6. **Row 9 (not drafted) owns version/game/DLC checks.** Row 8 only displays them, but task 2.4 makes today's mod-version
   reject also close the slot, because row 8 lands before row 9 in M1. Row 9 should keep that and supply both versions
   in `DisconnectPacket.message`; row 9 reuses the harness verb `mp-fake-version` instead of adding its own. The
   password check (part 2, M4) runs after row 9's checks.
7. **Row 7's `world` section replaced `CreateNewSession`.** New-session difficulty (task 5.1) goes into that section's
   `Reset()`; "Start over" moves the whole `Saves` folder so row 7's fallback candidates (`bak*`, `start_*`) cannot
   reload the old session (D7).
8. **Row 12 ships the server inside the client zip** (`TogetherServer\`), which part 2 relies on. Same decision recorded
   in row 12's review.
9. **Row 14 (a) guard messages** should use `ModNotify` (D1) rather than a second UI path.
10. **Harness registration** (not yet in INTEGRATION.md — this draft did not edit shared files while another agent works
    in the tree): verbs `mp-ui`, `mp-status`, `mp-join`, `mp-join-string`, `mp-answer`, `mp-presence`,
    `mp-fake-version`, `mp-host`, `mp-players`, `mp-kick` (owner 8; checked unique against the table); server commands
    `password`, `serverinfo`; scenarios `join-ui`, `host-from-game`, `session-admin`; `Run-Session.ps1` gains
    per-instance extra arguments (task 4.3). Packets appended: `ServerInfo`, `PlayerPings`, `KickRequest`; changed:
    `ConnectPacket` (+`password`, +`adminKey`), `HeartbeatPacket` (+`sentTicks`).
11. **No snapshot section, no `SyncOrder` slot, no save section** — nothing to register there (D12).

## Open questions (default in use)

1. Password applies to DirectIP only; Steam joins skip it unless `password_steam = true`. *(Roadmap says "DirectIP
   password"; the Steam join string reaches friends only.)*
2. The dedicated server ships inside the client zip as `TogetherServer\` so hosting from the game needs no second
   download. *(Default yes; ≈ 5 MB for joiners.)*
3. A hosted server keeps running when the host only leaves the session; it stops on Host → Stop or game quit.
4. Admin = whoever holds the server's admin key; hosting from the game makes you admin. Kick only, no bans.
5. Rich presence advertises the join string by default (toggle `Together.AdvertisePresence`).
6. Session panel hotkey F8; dev hotkeys F5 (join last target) off by default, F6 removed.

## Risks

- **UI spike outcome unknown** (IMGUI stripping on IL2CPP, text input leaking to the game). Mitigated by the model/view
  split; a cloned-UI menu adds ≈ 1 session to part 1.
- **Steam cannot be tested in the harness** (no `steam_api64.dll` in the test installs, one Steam account). Rich presence
  logic is tested through `ip:` join strings; the Steam calls themselves and relay over the internet need the user and a
  friend (tasks 1.2, 4.5, 7.5). Part 1 cannot close without task 4.5.
- **Game and Facepunch both using Steam in one process** may overwrite rich presence (spike 1.2, re-publish fallback).
- **Leftover server process after a game crash** blocks the next Host start; handled by "Stop it" and the harness kill.
- **Plain-text password** — accepted for friends' co-op; documented by row 12.

## Size

Part 1 (groups 1–4): M, ≈ 4–5 sessions. Part 2 (groups 5–8): M, ≈ 5 sessions. Total L (≈ 10), at the top of the
roadmap's L; split per working rules if part 2 runs over.

## Second review (2026-10-06)

Independent review against the code on `main` (row 7 groups 1–2 landed: `port` key, `connect ip:port`, test lanes,
`--command-file`, `Start-/Stop-TestServer`), the game stubs, and the drafts of rows 6, 7, 9, 12 and 14.

### Fixed in this pass

1. **Stale code facts.** `port` and `Config.Port` already exist and `Client.ConnectToServer(address)` already parses
   `host:port`: removed "`Program.PORT` → `Config.Port`" and the `port` key from D4/task 2.3/proposal; `JoinService`
   reuses `ConnectToServer`. Context now notes `/stop` = `Environment.Exit` until row 7 task 3.6, `QueryPort = port + 1`
   and that a failed server start waits for a key press.
2. **IPv6 dropped.** The server listens on `IPAddress.Any` (IPv4 only) and `ResolveHost` picks IPv4, so `[v6]:port`
   could never connect; the parser rejects IPv6 with a reason (D2, task 2.1, spec).
3. **Refusals never reached the client.** Today's version refusal has `playerID = 0`, which the client reads as
   "another player left". D3 + task 2.4 now set `playerID` to the slot (or -1 before a slot exists, server full) and
   close the slot on the next tick — the same fix as row 9 task 4.4; whichever lands first owns it. Without this,
   task 2.6's `VersionMismatch` check could not pass.
4. **Harness status field clash with row 9.** Row 9 adds `Status.lastDisconnect {reason, message}`, this change had
   `session.lastError {reason, detail}`. Now this change (lands first in M1) adds `Status.joinStatus` and
   `Status.lastDisconnect` (pollable via `Wait-HarnessStatus`); row 9 reuses it.
5. **Hotkey clash with row 14.** Session panel F8 = row 14's bug-report key (F7 = resync). Now
   `Together.SessionPanelKey`, default F9.
6. **Test lanes.** Hardcoded `127.0.0.1:7777`, `7801` and `<TestRoot>\Server` replaced by `$Ctx.Lane`/`$Ctx.ServerDir`
   values (tasks 2.2–2.5, 4.1, 4.4, 6.1, 6.2, 8.1, 8.2); `mp-host` gains `port=`; server-process checks are per exe path
   so lane 2 does not block lane 1.
7. **Server overrides in scenarios.** `Start-TestServer` takes no arguments; task 2.3 adds an optional `-Arguments`
   (additive to row 7's helper). Cold start (4.3) gets a concrete mechanism: `scenarios\<name>.launch.psd1` read by
   `Run-Session.ps1`, new scenario `join-coldstart`.
8. **Hosting readiness probe consumed a slot.** A TCP connect probe takes a server slot until the 10 s heartbeat
   timeout (breaks `max_players = 1`). D7/6.1 now wait for the "Server started. Listening port" log line and test the
   port with a bind; a failed start (process waits for a key) is killed on timeout and shows the log tail.
9. **"Was kicked" had no data path.** Row 6's leave broadcast carries no reason; D9/5.3 add
   `Client.Disconnect(reason)` copied into that broadcast.
10. **Part 1 trimmed to M1 needs.** `password`, `password_steam`, `admin_key`, `new_session_difficulty` and their
    arguments moved from task 2.3 to 5.1–5.3. New `ConnectPacket` fields are `[OptionalField]` (row 9's rule);
    password/admin key never logged; `Together.AdminKey` flagged as a secret for bug bundles.
11. **[user] steps.** 1.2 runs on test install A with `steam_api64.dll` (not the user's main install); only the
    second-account check needs the user. 4.5 and 7.5 are playtest items, not merge blockers (playtest date unknown).
12. Spec `session-join`: failure list includes row 9's game-version/DLC/mod refusals; address input is IPv4/host name.

### Remaining (for the integration pass / user)

1. **INTEGRATION.md registration** (not edited here): verbs `mp-ui`, `mp-status`, `mp-join`, `mp-join-string`,
   `mp-answer`, `mp-presence`, `mp-fake-version`, `mp-host`, `mp-players`, `mp-kick`; `Start-TestServer -Arguments`
   and `*.launch.psd1` (owner 8); `Status.joinStatus`/`lastDisconnect` (owner 8, user 9); scenarios `join-ui`,
   `join-coldstart`, `host-from-game`, `session-admin`; `DisconnectReason` append-only (7: base set, 8: `ServerFull`,
   `WrongPassword`, 9: `GameVersionMismatch`, `DlcMismatch`, `ModMismatch`); row 6 API additions
   `PresenceManager.PlayerAdded/PlayerRemoved`, `Client.Disconnect(reason)`.
2. **Row 9's draft** still says "row 7's menu display shows them (row 8 part 1 later replaces that)"; in M1 row 8 lands
   first, so row 9 should call `ConnectionStatus.Failed` and reuse `lastDisconnect` and the refusal fix. Row 9's
   `compat-override` must not add a mod-version key (`mp-fake-version` covers it).
3. **MelonPreferences category names differ** across drafts (`Together.*` in rows 6/8, `CMS21Together_Guard` in row
   14). Proposed default: one category `CMS21Together` with entries `PlayerName`, `LastJoinTarget`, `AdminKey`,
   `SessionPanelKey`, `DevHotkeys`, … and row 14's guard in `CMS21Together_Guard`; row 14's bundle redacts any entry
   named `*Key`/`*Password` except key bindings.
4. **Size.** Part 1 is closer to 5–6 sessions (two-way UI spike, refusal fix, cold-start harness, scenario); part 2 ≈ 5.
   Total ≈ 11, just over L. Proposed default: spike IMGUI first and build the cloned-UI prototype only if IMGUI fails a
   criterion (saves ≈ 1 session); split part 2 per the working rules if it runs over.
5. Open defaults unchanged from the first review (password DirectIP-only, server inside the client zip, host leaving
   keeps the server running, admin = key holder, rich presence on by default); hotkey default is now F9.

## Integration pass (2026-10-06)

Applied: `Server.Refuse(clientId, reason, message)` is this change's (D3, task 2.4; row 9, password and `/kick` use
it); `DisconnectReason` order `ServerFull`, `WrongPassword` before row 9's three values; `Status.lastDisconnect` kept
here and adopted by row 9; preferences moved to category `CMS21Together` and the panel key renamed
`CMS21Together.SessionPanelHotkey` (F9; F7/F8 are row 14's); guard messages are row 14a's (`multiplayer-guard`); the
admin key is covered by the shared redaction rule. All verbs, helpers, scenarios, packets and keys are registered in
INTEGRATION.md. Open questions above are unchanged.
