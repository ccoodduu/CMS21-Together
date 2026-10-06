# Review — mod-compatibility (2026-10-06, self-review of the first draft)

Checked against `openspec/ROADMAP.md` (row 9, M1, M6, row 16, integration notes, working rules),
`openspec/INTEGRATION.md`, `config.yaml` rules and `QUESTIONS.md` (nobody owns DLC; no gameplay mods for now; the
mod check covers gameplay mods only). `openspec validate mod-compatibility --strict` passes.

## Verified while drafting

- Code: `ConnectPacket { playerID, username, message, gameVersion, modVersion }`; welcome sent from
  `Server.TcpConnectCallback` and `SteamTransport.OnConnectionChanged`; client answers from `AuthHandler.HandleConnect`
  (DirectIP) and `ClientSteam.OnConnected` (Steam), both with `gameVersion = ""`; `AuthHandler.OnConnected` checks
  `modVersion` only. **Bug found:** the refusal `DisconnectPacket` has `playerID = 0`, so the client's
  `HandleDisconnect` takes the "other player left" branch, never shows the message, and the server never closes the
  slot — fixed by task 4.4.
- Stubs: `GameSettings.BuildVersion` (static), `PlatformManager.GetDLCs()` → `DLC.ProductId/Name/Owned`,
  `GameManager.PlatformManager`, `GameInventory.PartPropertyList`.
- Installed mods decompiled with `ilspycmd` (main Steam install, MelonLoader log: game version 1.0.40): QoLmod 4.4,
  TK Aftermarket 0.7.4, TK Basics 0.5.2, LvxBetterCarSpawns 1.0.0, LvxOwnedCarsOnly, QuickShop 1.04, AutosaveMod,
  CMS21LoadOptimizer 0.5.0. The heuristic (design D5) classifies the first five as gameplay and the last three as
  visual. Two facts shaped D4: QuickShop and LoadOptimizer patch through their own `Harmony` instances (so
  `MelonBase.HarmonyInstance.GetPatchedMethods()` would miss them; attribution goes by the patch method's assembly),
  and LvxOwnedCarsOnly/LoadOptimizer patch other mods' methods (ignored for the patcher's own class).
- Harness verbs `compat-report`, `compat-override`, `db-export` and scenario `compat-refusal` are not used by any
  other change.

## Conflicts / coordination with other changes

1. **Exporter timing vs. row 16.** ROADMAP puts the exporter in row 9 part 2 (M6), but row 16's prices/payouts land
   with M3 and are "built from … game data exported to `Database/*.json` (shared exporter with row 9)". *Proposed:*
   task group 6 (exporter) lands when row 16 starts (M3 at the latest), only group 7 (heuristic tuning) stays M6.
   ROADMAP M3/M6 rows need that note.
2. **Row 7 owns `DisconnectReason` and edits `AuthHandler.OnConnected`.** This change appends three values (task 2.2)
   and puts one `CompatibilityPolicy.Evaluate` call at the top of `OnConnected`; row 7's identity resolution (group 4,
   M5) runs after it. Depends on row 7 group 3 (M0) for `reason` and the menu display of the reason.
3. **Row 7's save guard may not cover mod autosaves.** AutosaveMod (on the user's install) calls
   `GarageLoader.Save(false)`; row 7 blocks `GameDataManager.Save(int)` and profile methods. Whether
   `GarageLoader.Save` ends there is unverified (row 14 task 3.1 checks it). The classifier calls AutosaveMod visual
   (no patches), so this is the only protection. *Proposed:* row 7 adds `GarageLoader.Save(bool)` to its blocked
   entry points if the check shows it bypasses `GameDataManager.Save(int)`.
4. **Row 8 part 1** shows connection errors; until then row 7's menu display shows them. The reason texts (D7) live in
   one place so row 8 reuses them.
5. **INTEGRATION.md** needs: `ConnectPacket` changed by row 7 (`playerKey`) and row 9 (`protocolHash`, `dlc`, `mods`,
   filled `gameVersion`); `DisconnectReason` appended by row 9; harness verbs and scenario above; server command
   `compat` (owner row 9).
6. **ROADMAP wording**: row 9 says no `Database/*.json` exists in the repo; three files exist (exported once upstream),
   the exporter is what is missing. Status line "`gameVersion` is sent but not checked": it is sent empty.

## Open questions (with proposed defaults)

1. **The user's own main install has five gameplay mods** (QoLmod, TK Aftermarket, TK Basics, LvxBetterCarSpawns,
   QuickShop) and would be refused like any friend's. *Default:* play multiplayer from an install without them (e.g.
   a second copy of the Mods folder); the host may list a mod in `mods_ignored` at their own risk. Do you want a
   built-in "disable gameplay mods for multiplayer" helper instead? (Not planned: mods cannot be unloaded safely.)
2. **Reference game version and DLC set**: `auto` = first accepted client of the server run pins them (DLC), or the
   database's export version (game version). *Default:* yes; the host can set them explicitly in `server_config.ini`.
3. **Mods the heuristic calls "unknown"** (they patch a window's logic, not only its drawing) are refused like gameplay
   mods. *Default:* refuse (safe); the host can ignore them by name.
4. **Exporter lands with row 16 (M3)**, not M6 (conflict 1). *Default:* yes.

## Risks

- False positives refuse harmless visual mods → the refusal names the mod and its targets; `mods_ignored` fixes it
  without a build; group 7 tunes with real mod lists (needs friends' mod lists — the user collects them).
- False negatives (no patches, direct calls, patches applied after connect, LoadOptimizer's opt-in profiler) →
  `mods_gameplay` list, row 7's save guard, row 14's reconciliation.
- BinaryFormatter version skew between builds → `[OptionalField]` and the client-side welcome check (task 3.3); an old
  client still sees the old (broken) message once.
- `GameSettings.BuildVersion` or DLC ownership not ready at connect → spike 1.1 logs them in the menu and the garage.
- No protection against a client lying about its mods — accepted (friends' co-op).

## Size

Part 1 (groups 1–5): M (≈3–4 sessions). Exporter (group 6): S (1–2). Tuning (group 7): S (1, plus the user's time
to collect mod lists). Total M, as in the roadmap.

## Second review (2026-10-06)

Independent review against the code on `main`, the stubs (`members.sh`), row 7's implemented contract and the other
M1 drafts (`hosting-and-join-ui`, `release-and-docs`). `openspec validate mod-compatibility --strict` passes after the
fixes.

### Verified

- **Refusal bug confirmed, and worse than described.** `AuthHandler.OnConnected` sends `DisconnectPacket` with
  `playerID = 0`; slots start at 1, so the client's `HandleDisconnect` takes the "other player left" branch. The slot is
  never freed: `Client.Update` checks the heartbeat timeout only once `ConnectionValid`, which a refused client never
  reaches. On Steam, `Client.Disconnect()` would not help either: it closes TCP/UDP only, never `SteamConnection`.
- **Steam send order.** `ClientSteam.OnConnected` is the client's own connection callback and sends `ConnectPacket`
  before the server's welcome is read (with `playerID` unset). The draft's "client checks the welcome before sending"
  could not hold on Steam. Side effect found: row 7 task 4.1 sends `playerKey` only from `HandleConnect`, so a Steam
  join would never carry it — fixed by the single send path below, since this change (M1) lands before row 7 group 4.
- Stubs match: `GameSettings.BuildVersion`, `PlatformManager.GetDLCs()`/`DLC.ProductId/Name/Owned`,
  `GameManager.PlatformManager`, `GameInventory.PartPropertyList`/`PartPropertyListLoaded`. BinaryFormatter
  version tolerance works as D1 assumes (extra fields ignored, missing ones need `[OptionalField]`); Core's
  `AssemblyVersion` is fixed at 1.0.0.0, so type resolution does not break between builds.
- Classifier and D4 design are sound; the protocol hash complements row 12's `0.6.0-local` versions (equal strings,
  different packets).

### Fixed in this pass

1. design D1/Context, tasks 3.3, proposal Impact: **one send path** — `ConnectPacketFactory.Build()`, sent from
   `HandleConnect` after the welcome check for both transports; `ClientSteam.OnConnected` stops sending.
2. design D1, task 4.4: **`Server.Refuse(clientId, reason, message)`** — sets `playerID`, closes on the next tick
   including `SteamConnection.Close()`; shared with row 8 task 2.4 (lands first) and `/kick`; undeserializable
   `ConnectPacket` caught in `Tcp.HandleData`/`SteamTransport.OnMessage`.
3. design D7, tasks 3.3/3.4: display through row 8's `ConnectionStatus`/`ConnectionMessages.For` (row 8 lands
   first in M1), row 7's menu display only as fallback.
4. tasks 1.2/3.3/4.4/5.1: `lastDisconnect` dropped in favour of row 8's `session.lastError`; plain mod-version mismatch
   tested with row 8's `mp-fake-version` (row 8 review item 6 already expects this).
5. task 5.1 scenario bug: `FakeVisual` was added while `FakeGameplay` was still set (B would have been refused); added
   `mod-clear`, re-adding `FakeGameplay` before the restart, and the lane-aware config path `$Ctx.ServerDir`.
6. tasks header: dependency on row 8 part 1.

### Remaining (questions with proposed defaults)

1. **Milestone split / archiving.** Part 1 is M1, group 6 lands with row 16 (M3), group 7 is M6, so the change could
   not be archived before M6. *Default:* move group 6 (exporter, `game-data-export` spec) into row 16
   `server-game-logic`, its main consumer, and group 7 into a small M6 follow-up `mod-compatibility-tuning`; row 9 then
   archives after M1. `game_version = auto` falls back to the pinned value until the exporter exists.
2. **`DisconnectReason` append order.** Row 8 appends `ServerFull`, `WrongPassword` first; row 9 appends after them on
   rebase. Not an issue as long as nobody renumbers; the coordinator records both in INTEGRATION.md.
3. **Row 8 note (not edited here):** row 8's `ServerFull` `DisconnectPacket` on DirectIP is sent before the welcome, so
   the client's `ID` and the packet's `playerID` are both 0 and match by accident. *Proposed:* row 8 sends
   `playerID = -1` there.
4. **Steam path is not covered by the harness** (test server runs `use_steam = False`). *Default:* task 3.3 checks it
   by log order; the M1 Steam spike with a friend confirms it.
5. INTEGRATION.md needs `Server.Refuse` (first of 8/9) and `ConnectPacketFactory` (owner 9; users 6, 7, 8) in "APIs".

Size: Part 1 is closer to 4–5 sessions with the handshake consolidation; still M.

## Integration pass (2026-10-06)

Applied: `Status.lastDisconnect { reason, message }` (row 8) replaces `session.lastError`; `Server.Refuse` is row 8's
(task 4.4 uses it, creates it only if row 8 has not landed); the three reasons are appended after row 8's
`ServerFull`, `WrongPassword`; the dev-tools flag is `CMS21Together.EnableDevTools` with `CMS21Together.DbExportHotkey`;
`ConnectPacketFactory`, `CompatibilityPolicy`, `ModInventory`, verbs, scenario, server command and config keys are
registered in INTEGRATION.md; ROADMAP notes that the exporter lands with row 16. The milestone-split question stays
open for the user.

## User decision (2026-10-06) — to apply when the change starts

DLC: do not refuse a client for a different DLC set. The server tracks the DLC set owned by every connected
player; DLC content not owned by all (cars, parts, tools) is blocked from shared use (spawn, shared inventory,
parking). Update the DLC requirement, design and tasks accordingly before implementing. Gameplay mods stay refused.

Applied when the change started (2026-10-06): spec requirement "Same DLC set" replaced by "Shared DLC set" (no
refusal; the server keeps the intersection of the connected players' DLC sets, logs it, shows it in `compat` and
sends it in `ServerInfo.SharedDlc`); `DlcMismatch` and the `dlc` config key dropped; design D3 rewritten; task 4.6
added and 5.1 changed (two clients with different DLC sets both join and see the intersection). Blocking DLC
content outside the shared set is recorded for rows 1, 2, 5a in INTEGRATION.md ("DLC content").
