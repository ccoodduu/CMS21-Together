# Roadmap review (2026-10-05)

Scope: `openspec/ROADMAP.md` as a whole — completeness, order, risk, process, size. Inputs: the seven drafts
in `openspec/changes/` (read only), `QUESTIONS.md`, `STATUS.md`, the 0.4.17 code (`PacketTypes`, `ClientSide/`),
upstream issues #7–#105, and the current client/server code. The roadmap was edited in place; row names 1–12
are unchanged.

## Findings

### Blocker

**B1. Test drive / test path was not owned by any row.** Upstream's most reported car bug: repairs, examined
parts and job progress reset after a test drive (#18, #83, #85, #95). The drafts make it worse in one way: row 6
D6 turns every return to the garage into a late join, which throws away what the test drive discovered
(examined parts, mileage). Dyno results were also unowned (row 4 asks in its A5).
*Changed:* new row 13 `sync-test-drive-and-diagnostics` (claim the car while away, upload results before the
return snapshot is applied, own dyno results), an integration note, a spike at the start of M3.

**B2. Friends could not play before the very end.** The old order put hosting UI, mod compatibility, release
packaging and client save safety after all sync rows. Without them the user's friends cannot install, cannot
join (F6 has a hardcoded server ID), and risk their own single-player saves (profile slot 4; upstream lost saves
this way: #92, #96, #105).
*Changed:* milestones M0–M6. Client save safety and atomic save/backups move to M0; a minimal join path,
version/DLC/mod-list checks and a dev zip move to M1 (rows 8, 9, 12 split into part 1/part 2).

### Major

**M1. No way to play between milestones without desyncing.** Anything not yet synced (working on a car before
row 1, tools before row 5) silently diverges.
*Changed:* new row 14 `desync-detection-and-resync`: (a) a guard that blocks unsynced actions with a message
(M1), (b) server checksums per state section, (c) a manual resync key that reruns the late-join snapshot (M2),
(d) a bug-report bundle (M5). 0.4.17 had a resync packet; the drafts only have per-car resync.

**M2. Game updates break the server silently.** The handshake checks the mod version only (`gameVersion` is sent
and ignored), and the server's `Database/*.json` (4.8 MB item database) has no exporter in the repo. Upstream
#86: a game update ("new rims") broke hosting. A new item missing from the database breaks pricing.
*Changed:* row 9 now owns game-version check, DLC-set check and a game-data exporter; working rule for game
updates.

**M3. DLC ownership is not handled.** 0.4.17 exchanged owned content (`contentInfo`); the new code does not.
A car or part from a DLC that one player does not own (generated as an order by the elected client, or spawned)
cannot load on that player's client. Upstream #73 asks exactly this.
*Changed:* row 9 part 1 (M1) checks the DLC set on connect. Content filtering is a question (Q2).

**M4. Steam joining is the riskiest unknown for the user's real use and is invisible to the harness.** The
server logs on anonymously (`SteamServer.LogOnAnonymous`) and uses a relay socket; the server SteamID probably
changes each start, and whether friends can join from the friends list is unknown (#93, #97).
*Changed:* M1 spike with the user and one friend; row 8 now says "friends list / invite / rich presence instead
of a server ID".

**M5. Economy gap: crates.** Opening crates gives money/XP locally and desynced level/money upstream (#94).
*Changed:* added to row 10.

**M6. Process gaps for autonomous work.** No regression run before merge (only the feature's own scenario), no
rule for a change that grows too big, no milestone definition of done, no OpenSpec archive step, no rule for
game updates, no place for playtest bugs, and latency/packet loss only in row 11 (last) although claim races
in row 1 appear only with latency.
*Changed:* Working rules rewritten (one change in flight, append-only `PacketTypes`/`SyncOrder`, done = scenario +
full regression with a `Run-All` runner added in M0, flaky-test logging, split rule with guard/flag, archive
after merge, game-update rule, milestone definition of done, playtest findings). Latency/loss injection moved to
M2.

**M7. Row 5 is XL (52 tasks, 13 tools).** One branch for all tools would run for weeks.
*Changed:* implementation order lists common tools first; the split rule applies (merge passing tool groups,
follow-up change for the rest).

### Minor

- **m1. Order generator away from the garage.** Row 3 elects the lowest player id; if that player is at the
  junkyard or test track, orders stop (row 3 lists it as a risk). *Changed:* integration note — elect among
  garage clients once row 6's roster exists.
- **m2. Claim release on leaving the garage** was implied in row 6 only. *Changed:* integration note for
  rows 1, 5, 13.
- **m3. New-session settings.** Difficulty is hardcoded to Normal on the client (`ModGameManager.StartGame`)
  and on the server (`GameDataManager`). *Changed:* row 8 owns new-session settings; sandbox → backlog (Q4).
- **m4. Session admin.** Kick, DirectIP password, player list with ping, join/leave notifications were missing.
  *Changed:* added to row 8. Text chat → backlog.
- **m5. Moving a server save to another host** (upstream #96) is just copying `server_save.json` with a dedicated
  server. *Changed:* documented in row 12's guide.
- **m6. Seasonal garages** (`Christmas`/`Easter`/`Halloween` scenes in 0.4.17's `SceneManager`): multiplayer
  always loads `garage`, so they never appear. Row 6 already classifies by scene type. *Changed:* backlog entry.
- **m7. Pause menu while connected** — not mentioned anywhere. Single-player pause may stop timers on one
  client (order timers, coroutines). *Not changed:* suggest one verification task in row 3 (order timers run on
  the server anyway) — small enough not to need a row.
- **m8. Upstream #87 (wheel assembler shows the hood)** and **#89 (engine stand duplicate after the stand-2
  upgrade + restart)** are row 5 and row 7 test cases. *Not changed:* the row 5 agent should add them as
  scenario steps; mentioned here so they are not lost.
- **m9. #48 (two players examine different parts at once → error)**: row 1 scenario material. Not changed.
- **m10. Row "Depends on" fixes**: row 1 depends on row 7's contract; row 6 depends on row 2 only for purchases.
  *Changed.*

### Coverage checklist (asked for)

| Topic | Where |
|---|---|
| Moving cars in the garage area / parking | row 2 (no free driving in CMS21's garage) |
| Test track, test path, dyno | row 13 (new); visible driving → backlog |
| Garage customization, decorations | backlog |
| Seasonal event garages | backlog (always normal garage) |
| Sandbox / difficulty | row 8 (difficulty), sandbox backlog (Q4) |
| Chat / notifications | notifications row 8; chat backlog |
| Admin commands | server has `/save`, `/players`, `/stop` (row 7); kick/password row 8 |
| Crash recovery | server: row 7 (atomic save, backups, refuse to start); client: row 7 (profile restore); claims: expiry + disconnect |
| Steam Cloud | row 7 risk (save guard covers all profile writes) |
| Anti-desync resync button | row 14 (new) |
| Performance with many cars | row 11 (late-join time with full garage + parking) |
| 0.4.17 features (`toolMove`, `resync`, `contentInfo`, `carEngineSound`, `playerInCar`, `parkAdd/Remove`) | rows 5, 14, 9, 6, 6, 2 |

## Risk ranking (biggest first)

1. **Row 1 part identity across clients** — the foundation for rows 2–5 and 13. Spike in M0: load every car on
   both clients, diff part keys.
2. **Steam join over the internet** (row 8) — harness cannot test it; needs the user and a friend in M1.
3. **Row 5 workshop tools** — 13 UI/coroutine-driven flows; most hooks per row; XL.
4. **Row 3 order generation on an elected client** — native generator, handoff on leave, cap checks.
5. **Row 13 test-drive round trip** — what the game saves/loads around a scene change is not yet traced.
6. **IL2CPP / MelonLoader 0.5.7 limits** (every row): patches on methods inlined by IL2CPP never fire; patching a
   coroutine method only sees its start (patch the generated iterator's `MoveNext` or poll instead); some
   generic/struct-parameter methods cannot be patched; custom UI needs a spike (IMGUI vs. cloned game UI).
   Mitigation: hook-trace task first in every change (rows 3, 4 and 5 already do this).
7. **Game updates** (rows 9, 12) — regenerated Unhollower assemblies and stale server database.
8. **Snapshot size** (row 11) — full garage plus parking with every part record at late join.

Nothing in rows 1–14 looks infeasible on IL2CPP. The weakest parts are row 6's remote engine sound (one
singleton audio controller; the draft already falls back to state only) and anything that would share worlds
outside the garage (kept in the backlog).

## Size estimates

1 L · 2 M · 3 L · 4 L · 5 XL · 6 L · 7 L · 8 L · 9 M · 10 M · 11 M · 12 S+S · 13 M · 14 M. Roughly 75–110
sessions in total, with M0–M2 about a third.

## Open questions for the user

1. **When can you and a friend do a 30-minute Steam join test (M1 spike)?** It cannot be automated and decides
   how row 8 works. *Default:* schedule it as soon as M1's dev zip exists; until then DirectIP on LAN/VPN is the
   fallback.
2. **Do you and your friends own the same DLCs?** *Default:* the server refuses a join when the DLC set differs
   from the first player's (cheap, safe); filtering DLC content per player is a later change if needed.
3. **Which other mods will you and your friends run during sessions (QoLmod? which features)?** *Default:*
   everyone runs the same mod list (enforced in M1); QoLmod features that change money/parts/cars are
   untested until M6.
4. **Difficulty and sandbox.** *Default:* the host picks Easy/Normal/Expert when creating a session (row 8);
   sandbox is backlog.
5. **Do you want to playtest after each milestone (M1, M2, …) or only from M3 (jobs)?** *Default:* after each
   milestone; bugs from a playtest are fixed before the next milestone's rows start.
