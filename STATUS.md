# Status

Newest first. One entry per work session.

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
