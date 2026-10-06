# Status

Newest first. One entry per work session.

## 2026-10-06 (11:25–11:45) — row 1 complete in code (branch `change/sync-car-parts`)

- Every task in `sync-car-parts/tasks.md` is checked. New since the last entry:
  - Engine crane out/in is one part transaction (`EngineCraneHooks`, scenario `car-crane`); engine swaps are refused
    while connected (a swap rebuilds the engine under `root` and shifts part keys). The crane needs the oil drained
    first, and draining is row 4 (`DrainTool` stays guarded), so players can use the crane only after row 4.
  - Server returns OR-merged `examined` flags in the accepted result (putting the engine back resets them).
  - `cars` server command; `car-race` round 2 forces a server rejection (`part-hold-remote`) and checks the inventory
    revert; `car-live` holds B in `Loading` (`car-hold-snapshot`) to exercise the D8 queue.
  - DLC rule: DLC ids are now positions in the game's DLC list; a DLC car's spawn is refused unless every connected
    player owns its DLC (scenario `car-dlc`; 91 DLC cars in the game).
  - Guard: row 1's modes, pie entries and windows are allowed while connected (engine swap and building engines stay
    blocked); the `guard` scenario uses the row 4 `BonusDisassemble` mode as its blocked example.
  - Harness fix: `Get-ServerLogMark` skipped the first new log line.
- Two checks need a person in the game (M2 playtest): event order and item IDs of a body-part unmount through the
  pie menu, and whether a second action can start before the first finishes.
- Running: a full lane-1 regression of `0f10bf9`; if green, row 1 is merged into `main`. An agent does row 2's
  static spikes (`docs/spikes/car-placement.md`).

## 2026-10-06 (11:00–11:25) — row 1: race fixed, API, dumps (branch `change/sync-car-parts`)

- `main` (M1) is merged into `change/sync-car-parts`. Lane-1 regression `20261006-105712` PASSED with every car
  scenario (`car-baseline`, `car-live`, `car-race`); `compat-refusal` and `join-presence` were FLAKY:
  - `compat-refusal`: the scenario read a stale `status.json` (B still `Failed` from the previous refusal); it now
    waits until B leaves `Failed` before the next join. Run `20261006-111147_L1_compat-refusal` PASSED.
  - `join-presence` (`20261006-110303`): B froze while loading the menu after "Join request → yes" (log and status
    stop at `SelectSceneToLoad(4) Menu`). First time in 5 runs; watch it.
- Race fix (`d6205a5`): the slower client aborts its own transaction before applying the winner's inventory.
- D11 API for other rows (`f318986`): `CarPartsSync.MarkDirty`, `RebuildRegistry`, `LocalPartsCommitted`.
- Dumps carry the full car part state (task 5.2) incl. `blocked`; `car-live` checks that A and B block the same
  parts after a remote unmount (task 3.3 done). `car-live` and `connect` PASSED with the larger dump.
- Your idea (server-hosted generator client) is a spike in `ROADMAP.md` at the start of M3, and under "Decide later"
  in `QUESTIONS.md`.
- Running: an agent maps the engine crane out/in path with the native decompile (`docs/spikes/engine-crane.md`).
  Next: engine crane group transaction (task 3.5), ordered change queue (3.2), then row 2.

## 2026-10-06 (11:00) — M1 done: dev build 0.6.0-dev.561

- All M1 rows merged to `main` (`58ad6e2`): presence/names/scenes (row 6 part 1), join menu with readable failures and
  Steam join (row 8 part 1), mod/version/DLC checks (row 9 part 1), release zips (row 12 part 1), feature guard
  (row 14a). Regressions: lane 1 `20261006-102833` (all M1 scenarios green), lane 2 `20261006-103205` from the merged
  tree (green; `join-coldstart` flaky because of a test-setup bug fixed during the run).
- Zips: `tools\release\out\CMS21-Together-0.6.0-dev.561-client.zip` and `-server.zip` (main checkout, not in git).
  Install and join: `docs/try-it.md` (also inside the zip).

### What to try (M1 playtest checklist)

1. Host: run `TogetherServer\CMS21_Together_Server.exe` (from the client zip). For friends over the internet: forward
   TCP+UDP 7777, or set `use_steam = True` and join with the Steam server ID / Steam "Join Game".
2. Everyone: install the client zip into a game install **without gameplay mods** (QoLmod, TK, LvxBetterCarSpawns,
   QuickShop are refused; LoadOptimizer is fine). Set your name in the Multiplayer panel.
3. Join from the main menu (Multiplayer → address → Join). Try a wrong address and see the message.
4. Walk around the garage together: names over heads, a late joiner sees players standing still.
5. Shared money/XP/scrap: buy something in the shop, upgrade the garage, open the warehouse together.
6. Travel to the junkyard alone, come back: the garage is reloaded from the server.
7. Try things that are not shared yet (orders, disassembly mode, tire changer): the game refuses them with
   "... is not supported in multiplayer yet".
8. Leave and rejoin; close the server with `/stop` and start it again: money, level, inventory are kept.
9. Report: `MelonLoader\Latest.log` and the server's `Log\Latest.txt`, with what you did.

Known gaps in M1: working on cars is blocked (M2 is in progress on `change/sync-car-parts`); no shared junkyard;
the Steam join with a real friend is untested (needs you and a friend).

## 2026-10-06 (09:50–10:35) — mod-compatibility part 1 (row 9, agent, branch `change/mod-compatibility`)

- DLC decision applied first: a different DLC set never refuses; the server tracks the shared DLC set (intersection
  of connected players), logs it, shows it in `compat` and sends `ServerInfo.SharedDlc`; rows 1, 2, 5a block DLC
  content outside it (INTEGRATION.md "DLC content"). `DlcMismatch` and the `dlc` config key dropped.
- Groups 1–5 done: protocol hash + client welcome check, one `ConnectPacket` send path (`ConnectPacketFactory`),
  game version pinned from the first client (or configured / `meta.json`), mod classifier (fixtures of 8 real mods
  pass `--check-mods`), `CompatibilityPolicy`, `/compat`, harness `compat-report`/`compat-override`.
- Runs: `20261006-101337_L2_compat-refusal` PASSED; regression `20261006-101633_regression` PASSED on lane 2.
- Open (review.md Implementation notes): DLC product ids are not unique (11 × "-1"; proposed: use the
  `GetDLCs()` index, as `PartProperty.DLC`/`IsDLCInstalled` do — needs the user); Steam send path verified by code only.
## 2026-10-06 (10:40) — in flight (superseded by the 11:00 entry)

## 2026-10-06 (09:45–10:15) — M1 rows done except row 9; M2 started (cars)

- Row 8 part 1 complete: Steam rich presence join strings, join requests with a leave-and-join dialog, cold start
  from `+connect`; `join-coldstart` and `join-presence` pass. Open: the real Steam test with a friend (user).
- Row 12 part 1 merged and verified: `release-smoke` passes from the zips (`0.6.0-dev.548`); regression
  `20261006-094659` green afterwards; `main` at that point.
- Row 14a: single-player audit outcomes in its design (pause menu keeps running, save buttons hidden, no
  `Time.timeScale` writers, `GarageLoader.Save` blocked by row 7).
- Row 9 part 1 is being built by an agent on lane 2 (DLC decision applied: no refusal, only tracking).
- **Part identity spike: all 163 car models build identical part hierarchies on two clients** (`part-identity`,
  `20261006-100013`). Row 1's planned hooks all have direct native callers (design D2).
- Row 1 (`sync-car-parts`, branch `change/sync-car-parts`): records/packets, per-loader car state, cars section v2,
  `CarPartsStore`, baselines (spawner uploads, server stores and relays), late join for cars from the snapshot,
  no cars from the client's own profile. `car-baseline` passes: a late joiner gets the same hierarchy and part
  state, and the car survives a server restart. Live changes (tracker + hooks, server arbitration) are written and
  being tested (`car-live`); transactions with inventory deltas and claims are next.

## 2026-10-06 (09:00–09:45) — scenes, join UI, guard; two real bugs

- **Bug: sessions ran on the player's own profile 0.** The game reads the selected profile from PlayerPrefs on
  every `GetSelectedProfile` call (native decompile); once the pref write was dropped, `ProfileManager.Load` picked
  "My Save". `SessionGuard` now answers slot 4 while active; the pref is never touched. `scenes` and
  `profile-safety` assert slot 4 during a session.
- **Bug: joins failed randomly with a socket access error.** The client bound UDP to the TCP local port; Windows
  reserves UDP ranges inside the dynamic port area (`netsh ... excludedportrange`). UDP now binds any free port.
  This also explains the "connected but never valid" hangs from the morning.
- Row 6 group 4: scene tracking, away/return as late join, `GarageBound` gating; `scenes` passes (car steps wait
  for `sync-car-parts`).
- Row 8 part 1: `JoinTarget`/`JoinService`/`ConnectionStatus`, readable failures (Unreachable, Timeout,
  VersionMismatch with both versions, ServerFull, Kicked, server lost), `Server.Refuse`, `ServerInfo`, server
  overrides, IMGUI join panel + overlay + message (spike 1.1 = IMGUI works). `join-ui` passes. Not done: Steam
  rich presence and join strings (group 4), the friend test.
- Row 14a `multiplayer-guard` (agent wrote it, I tested on lane 1): `guard` passes; pie options are locked in a
  `PrepareIcons` postfix. Open: runtime trace spike, the single-player audit (3.1).
- Row 12 part 1 is on `change/release-and-docs` (agent; `release-smoke` not run yet).
- Memory: two instances commit up to 31 of 40 GB (each game commits 8–9 GB while loading, working set 2–3.5 GB);
  four instances exceed the commit limit. A larger page file would allow two lanes (user's decision).

## 2026-10-06 (late morning) — M0 done, M1 started: presence

- **M0 complete** and on `main`: regression `20261006-083824_regression` PASSED on lane 1 (connect,
  presence-latejoin, profile-safety, server-restart, server-saves) in 3½ min.
- `sync-players-and-scenes` slice 1 + names (tasks 1.2–1.8, 2.1–2.6, 3.1–3.3): server presence records, roster
  snapshot (`players`), avatars driven by the roster, spawn slots, player names (preference, de-duplicated on the
  server), IMGUI name tags. **The idle late-join bug is fixed**: `presence-latejoin` passes, `connect` now requires
  both players to see each other.
- Seen in a log: on the way to the menu the game called `GameDataManager.Save(0)` (the player's real profile 0 in a
  normal install) during a session; `SessionGuard` blocked it.
- User allowed game tests while they are at the PC; answers to the M1 questions recorded (DLC: shared use only for
  content all players own; gameplay mods dropped in multiplayer for now; everything in English).
- `release-and-docs` part 1 is being built by an agent in its own worktree (no game runs there).
- Next: row 6 hook trace (1.1) and scene tracking (group 4: travel, away/return), then `multiplayer-guard`.

## 2026-10-06 (morning, autonomous loop) — M0 contract, test lanes, save robustness

- `session-persistence-and-rejoin` groups 1–3 done and merged/pushed to `main`:
  - Contract: versioned save sections, `SessionRegistry`, `SyncBegin/SyncEnd{Items}/SyncAck` + client
    `SyncTracker`, `GameDataManager.StateLock`, `RequestSave`, `--command-file`, 30 s no-progress sync timeout.
  - Group 3: crash-safe writes, rotating backups, start copies, fallback + quarantine + refusal, `--check-save`,
    `DisconnectReason`, save on `/stop` and window close.
- Harness: **isolated parallel test lanes** (taken as the first M0 harness task, as proposed on 2026-10-05).
  Each test install patches its own Unity company name (`RDGTogether-<X>`) into its own `globalgamemanagers`, so it
  has its own save folder and registry key; the real save and registry are never written (each run fingerprints
  them and fails if they changed). Lane 1 = A, B, `Server`, port 7777; lane 2 = C, D, `Server2`, port 7787.
  KickingOtherSession aborts a run. Server port is configurable; the client's UDP no longer always targets 127.0.0.1.
- Runs (all PASSED): `20261006-074033_L1_connect`, `074125_L1_server-restart` and `074125_L2_connect` (at the same
  time), `Test-ServerSaves.ps1` (14 server-only checks). `connect` notes the known idle late-join bug (row 6).
- Found: before group 6, a session wrote `profile4.cms21b` and left the `selectedProfile` pref at 4 (seen in lane A).
- Drafted M1 rows: `hosting-and-join-ui`, `release-and-docs`, `mod-compatibility`, `desync-detection-and-resync`
  (second reviews running). Native decompile spike running in the background.
- Unattended rule used: the game is only started after 10 min without keyboard/mouse input
  (`tools/test-env/Get-UserIdleSeconds.ps1`).
- Group 6 (client save safety) done: `profile-safety` passed twice (`080402_L2`, `080549_L2`). The session no
  longer writes the `selectedProfile` pref at all, so no crash recovery is needed. M0 is complete except a clean
  regression run. Native decompile spike done: `docs/spikes/native-decompile.md`.
- **Memory limit:** the first `Run-All` with both lanes (four game instances + server checks) ran the 32 GB PC low on
  memory; Claude Code stopped the run, and two clients had hung during the garage load (`connect` and
  `server-restart` each failed once and passed on rerun = flaky under memory pressure). Fix: lanes take turns with
  the game (global mutex in `Run-Session.ps1`, refuses below 12 GB free). Lanes still let a second agent work in
  its own worktree and deploy without overwriting the first lane's build. Game tests paused until the user OKs a
  rerun (Claude Code asks not to restart a run it stopped for memory on its own).
- Client logs now go to MelonLoader's `Latest.log` (they only reached the mod's own window before); not yet
  checked in game.
- Next: `Run-All` on lane 1 → merge to `main`; then M1: `sync-players-and-scenes` slice 1 (presence roster,
  fixes the idle late-join bug) here, and one M1 row per extra agent in its own worktree + lane 2.

## 2026-10-06 (night, with the user) — planning done

- Seven changes drafted, each reviewed, then one integration pass; workshop split into `sync-workshop-machines`
  and `sync-workshop-car-tools` → 8 changes, all `openspec validate --all --strict`. Cross-change matrix in
  `openspec/INTEGRATION.md`; each change has a `review.md`.
- Roadmap reviewed: milestones M0–M6, sizes, spikes, working rules; new rows 13–16.
- User decisions in `QUESTIONS.md` (four rounds). Key: shared progression, game logic moves server-side
  (row 16, game gets no more updates), outdoor scenes via adapted LvxBetterCarSpawns (row 15).
- Next: M0 — `session-persistence-and-rejoin` groups 1–2 (contract), then groups 6 and 3; spikes in the
  background (part identity, native decompile).

## 2026-10-05 (evening, with the user)

- Forked upstream `Dev` into our `main`; OpenSpec 1.14 set up with project context and `openspec/ROADMAP.md`.
- Test harness works end to end: two hard-linked test installs, local dedicated server, `TogetherTestHarness`
  mod (command file, status/dump JSON, screenshots, mute, skips intro + start screen), `Run-Session.ps1`
  with save/registry backup + restore. A connect run takes ~2 min.
- `connect` scenario: both clients reach the garage with identical stats/inventory/cars.
- Bug found: a late joiner never sees an idle player. `Movement.UpdateMovement` only sends on change and
  remote players spawn on the first `MovementPacket`. Belongs to `sync-players-and-scenes`.
- Seven OpenSpec changes being drafted (one per roadmap row).
