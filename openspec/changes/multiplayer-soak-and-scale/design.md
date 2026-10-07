# Design

## Context

See proposal.md — Why. Observed in the code and the harness today (`main` = `ed3af35`):

- Test lanes (`tools/test-env/TestLanes.psm1`): lane 1 = A, B, `Server`, port 7777; lane 2 = C, D, `Server2`, port 7787.
  Every install has its own Unity company name, save folder and registry key, reset from the seed before each run.
  `Run-Session.ps1` holds the launch mutex `Global\CMS21TogetherGameLane` only while its games start and waits for
  22 GB commit headroom and 6 GB free RAM (`Run-All` asks 10 GB for the second lane). Nothing holds a lane for the
  whole run: a second runner on the same lane is refused only because the lane's game processes are running.
  `Deploy-Mod.ps1 -Lane` copies the build into the lane's installs and server without any lock.
- Batch mode (`Run-Session -Scenarios`) starts the games once, resets clients (`to-menu`, `harness-reset`) and
  restarts the server from the batch-start save per scenario; client logs are cut into slices by byte offset.
  Launch arguments per role come from `<scenario>.launch.psd1`; the role is `@("A", "B")[index]`, so a third instance
  has no role. Scenarios take `param($Ctx)` only and most start with `$a, $b = $Ctx.Instances`.
- Memory (STATUS 2026-10-06, `docs/spikes/generator-client.md`): a game commits 8–10 GB and touches 2–4 GB; four
  games commit about 37 GB of the 64 GB limit and leave about 10 GB of the 32 GB RAM free. Uncapped, one game uses
  about one core (117 %); `-batchmode -nographics` commits 2.7 GB and, at 15 fps, uses 5 % of a core (garage and
  order generation work; car loading untested). Claude Code's memory guard is off, so the harness is the only guard.
- Server: every send goes through `Server.SendToClient` (one `Packet`, length known after `WriteLength`) except
  `RefuseUnassigned`; receives dispatch in `TCP.HandleData`, `UDP.HandleData` and `SteamTransport.OnMessage`, each
  under `GameDataManager.StateLock`. No byte, message or time counters exist anywhere. The log has second resolution
  (`[HH:mm:ss]`). `OnAskForSync` logs `Client[n] snapshot <id>: key=count, …` and `SyncAck` logs `joined`.
- Harness verbs that a soak can drive (INTEGRATION.md): cars and parts (`car-spawn`, `car-ready`, `car-delete`,
  `part-fast-unmount`, `part-fast-mount`, `part-claim`), placement (`lift`, `car-move`, `park`, `unpark`,
  `parking-unlock`), car details (`cardetails-randomize`), machines (`tool-put`, `tool-take`, `tool-move`,
  `tool-balance-open`), jobs (`orders-generate`, `orders-accept`, `job-finish`), economy (`stats-add`, `econ-fee`,
  `sell-item`, `give-item`), presence (`teleport`, `sit`, `stand`, `engine`, `travel`), network (`net-delay`,
  `net-hold`), checks (`dump`, `digest-show`, `resync`), session (`connect`, `to-menu`, `quit`). `desync-soak` (row 14
  task 2.6) is a 10-minute two-instance loop of three of them.
- Row 7 part 2 (`change/session-persistence-part2`, waiting for its runs): identity per install (`player.json`),
  `DuplicateIdentity` (first connection wins; a crashed client's slot frees after the 10 s heartbeat timeout),
  `PlayerRestore`, server-loss detection on the client (menu within 12 s), `players` command, scenarios `rejoin`,
  `latejoin`, `persistence-restart`, `duplicate-identity`. Row 14 (b): `desync check`, `[Desync]` log lines and diff
  records. Row 14 (d): `bug-report` (branch `change/bug-report`).

## Goals / Non-Goals

**Goals:** four clients in one session on the harness PC without starving the PC or the other lanes; a seeded,
replayable multi-hour session whose clients are proved equal at checkpoints; numbers for bandwidth, CPU, memory and
late-join time that a host can also read in a real session; crashes, rejoins and server restarts with no loss; every
finding reproducible from its run folder.

**Non-Goals:** fixing the findings inside this change when they belong to another row's sync (D9); a new serializer or
compression; more than four players; Steam transport, NAT and real internet latency (hand checks); load tests with
fake clients that do not run the game.

## Decisions

### D1. The scale lane and lane locks

- `TestLanes.psm1` gets lane 3 = A, B, C, D with server `Server3` on port 7797. Its own server folder keeps lane 1's
  save, backup marker and `Restore-InterruptedRun` untouched; `Deploy-Mod.ps1 -Lane 3` creates it like the others.
  *Rejected:* lane 3 on lane 1's `Server` — a lane-3 run would restore over lane 1's interrupted-run marker and mix
  two-instance and four-instance saves.
- Lane locks: named mutexes `Global\CMS21TogetherLane1` and `Global\CMS21TogetherLane2`. A lane-1 or lane-2 run holds
  its own for the whole run (batch and the single sessions after it; `Invoke-FreshSession` calls the script on the same
  thread, so the mutex is re-entrant). Lane 3 takes lane 1, then lane 2 (fixed order, no deadlock), waits up to
  `-LaneWaitMinutes` (default 120) and keeps lane 1 while it waits for lane 2, so a queue of lane-1 runs cannot starve
  it. The launch mutex and the "lane's games already running" check stay as they are.
- Deploy under the lock: `Run-Session -Deploy` runs `Deploy-Mod.ps1` after taking the lane locks; `Run-All` passes
  `-Deploy` instead of deploying before its jobs (a job is another process and cannot own the caller's mutex).
  A standalone `Deploy-Mod.ps1` takes the same locks and fails at once with "lane N is busy" instead of waiting.
  Each deploy writes `deployed.json` (`repo`, `commit`, `dirty`, `time`) into every install's `UserData\TestHarness`
  and the server folder; `result.json` records it, and a run warns when one lane's installs carry different builds
  (lane 2 after a lane-3 deploy).
- Roles: launch-argument roles become `A`–`D` by index. Scenarios for lane 3 use `$Ctx.Instances` as a list (never
  `$a, $b = …`) and work with two to four instances, so each can also run on lane 1 by hand.
- `Run-All`: scenarios whose header has `# run-all: lane 3` run only with `-Lanes 3`, which must be the only lane
  given; lanes 1 and 2 skip them. Batch mode works unchanged on lane 3 (its reset loops over all instances).

### D2. Memory gates, frame cap, watchdog, headless option

- Gates move into the lane table: lane 1 `22 GB commit / 6 GB RAM`, lane 2 `22 / 10`, lane 3 `44 / 16` (four normal
  games ≈ 37 GB commit and 8–16 GB touched). `-MinCommitHeadroomGb`/`-MinFreeMemoryGb` still override.
- `Start-HarnessInstance -Lane -Instance [-Headless]` (TestLanes) starts one game with the lane's arguments under the
  launch mutex with a one-instance gate (11 GB commit, 4 GB RAM), checks KickingOtherSession like `Start-LaneGames`,
  and waits for the menu. `Start-LaneGames` uses it. Storms (D8) relaunch killed clients with it.
- Frame cap: new harness verb `fps-cap <n>` (`QualitySettings.vSyncCount = 0`, `Application.targetFrameRate = n`).
  Lane-3 scenarios cap every client at 30 fps after it reaches the menu, so four games share the CPU evenly. Frame
  times on one PC with four games are not a player's experience; they are used only to find stalls (max frame time
  during a join or a snapshot), not as throughput.
- Watchdog (`PerfSampler.psm1`, D4): every 10 s; below 4 GB commit headroom or 1.5 GB free RAM the scenario stops
  issuing actions, records `aborted: memory` with the last samples, and lets `Run-Session` shut the lane down normally.
- Headless clients (option, decided by task 1.6): `-Headless C,D` starts those instances with `-batchmode -nographics`
  and `lowfx 15`. If they pass `scale-connect` and the car steps of `soak`, long soaks may use them to save about
  12 GB commit. They take no screenshots and are excluded from frame-time numbers. Default stays four normal games,
  closest to real players.

### D3. Server metrics

New `CMS21-Together-Server/Diagnostics/Perf/`:

- `TrafficCounters`: per `PacketTypes` value × direction (sent, received) × transport (TCP, UDP, Steam): messages and
  bytes, plus per client slot totals. Counted in `Server.SendToClient` (`packet.Length()` after `WriteLength`; Steam
  the array length), `RefuseUnassigned`, and at the three dispatch sites (the frame length read). `Interlocked` adds on
  fixed arrays sized from `PacketTypes`; no allocation per packet, no lock.
- `HandlerTimings`: per packet type, the time from entering the dispatch site to acquiring `StateLock` (lock wait)
  and the handler time inside it (`Stopwatch` ticks: count, total, max, and a 1 ms-bucket histogram up to 1 s for p99).
  The main-loop tick (`ServerWindow.TickServer`) gets the same pair under the key `tick`, the save build under
  `save-build`, `OnAskForSync` under `snapshot-build`.
- Process: CPU % since the last line (`TotalProcessorTime` delta / wall time; 100 % = one core), private bytes,
  working set, managed heap (`GC.GetTotalMemory(false)`), thread and handle count, GC counts per generation.
- Session: connected / syncing / in-session clients, last save size and duration, `Log/` size, `Log/desync` count.
- `PerfLog`: with `perf_log_interval_seconds` > 0 (config, default 0 = off, appended if missing), one JSON line per
  interval to `Log/perf_<start>.jsonl` with all of the above as deltas since the previous line (traffic rows only for
  packet types that moved). Written on the main loop outside `StateLock`.
- Command `perf`: summary since the last reset (uptime, CPU, memory, bytes/s per client in and out, top 10 packet
  types by bytes, worst handler and lock wait); `perf top <n>`; `perf reset`.
- Snapshot line: `OnAskForSync` records the snapshot's bytes (traffic of that slot between `SyncBegin` and `SyncEnd`)
  and build time; on `SyncAck` the server logs `Client[n] snapshot <id> acked after <ms> ms, <bytes> bytes, built in
  <ms> ms (key=count, …)`. Late-join numbers (D7) are read from this line, not from second-resolution timestamps.
- Cost: the counters are on every packet, so they are always on (a few `Interlocked` adds); only the file is optional.
  Task 2.4 measures the overhead with `perf` itself (handler time with and without, 10-minute `soak`).

Hosts can use the same `perf` command and `perf_log_interval_seconds` in a real session; row 12's guide mentions it
and row 14 (d)'s bundle includes the newest `perf_*.jsonl` (one line in its file list, owned there).

### D4. Client and process sampling

- Harness verb `perf` (`tools/TestHarness/Features/PerfCommands.cs`): frame time over the last 10 s from a ring buffer
  of `Time.unscaledDeltaTime` filled in the harness's `OnUpdate` (avg, p95, max), managed heap
  (`GC.GetTotalMemory(false)`), IL2CPP heap (`Il2CppSystem.GC.GetTotalMemory(false)`), scene, `syncAcked`.
  It is harness-only; the mod itself does not change.
- `PerfSampler.psm1`: `Get-PerfSample` reads every game and the server with `Get-Process` (CPU seconds,
  `PagedMemorySize64` = private commit, working set, handles; the sampler of `Run-GeneratorSpike.ps1` moves here),
  the system's commit headroom and free RAM, and the sizes of every client `Latest.log`, `Player.log` and the server
  log. A scenario calls it every 10 s from its loop and appends to `perf_processes.csv`; every 30 s it also calls
  `perf` on each client (`frames.csv`). Polling stays in the scenario's loop (no background job), so a stopped loop
  also stops sampling.
- `Show-SoakReport.ps1 <run folder>` prints and writes `summary.json`: per client average and peak (10 s window)
  download and upload, top packet types by bytes, server CPU avg/p95, handler and lock-wait max/p99, memory slopes
  (MB/h, least squares over the samples after the first 10 minutes) for the server and each game, log growth per hour,
  join times, checkpoint and storm results, budget verdicts (D6).

### D5. Soak driver

`scenarios/soak.ps1` (`# run-all: lane 3`), parameters `-Minutes` (10), `-Seed` (from the run timestamp, printed),
`-CheckEveryMinutes` (2; 10 when `-Minutes` ≥ 60), `-StormEveryMinutes` (0; 20 in long runs), `-Replay <actions.jsonl>`.

- Setup: every client connects, `fps-cap 30`, `guard-set Off` like the other scenarios (the harness calls game methods
  below the guarded UI; the guard has its own scenario). Server config for the run: `perf_log_interval_seconds = 10`,
  `autosave_interval_seconds = 30`, `desync_check_interval_seconds = 5`, `desync_autofix = true`.
- Loop: one step every 2–5 s (seeded). The driver keeps a small model of the session (loaded loaders, parked slots,
  unmounted parts per car and who holds them, machines in use, who is away) from verb results and from the dumps at
  each checkpoint, and picks a weighted action whose precondition holds, for a seeded actor:

| Weight | Action (existing verbs) | Bounds |
|---|---|---|
| 25 | unmount a random part, mount it again 2–20 s later (`part-fast-unmount`/`part-fast-mount`, sometimes by another client) | ≤ 3 open per car |
| 10 | spawn a car on a free loader / delete a non-job car (`car-spawn`, `car-ready`, `car-delete`) | 2–4 loaded |
| 8 | lift up/down, move a car between places (`lift`, `car-move`) | — |
| 8 | park / unpark (`park`, `unpark`) | ≤ unlocked slots |
| 8 | money and items (`stats-add`, `econ-fee`, `give-item`, `sell-item`) | money stays ≥ 0 |
| 6 | machines (`tool-put`/`tool-take` on the tire changer, `tool-move`) | one user per machine |
| 5 | car details (`cardetails-randomize`) | — |
| 5 | presence (`teleport`, `sit`/`stand`, `engine`) | — |
| 3 | jobs (`orders-generate`, `orders-accept`, `job-finish`) | ≤ 1 active job |
| 2 | travel to the junkyard and back (`travel`) | ≤ 1 client away |
| 5 | network: `net-delay` 0–250 ms on one client; `net-hold` 2–5 s | one client at a time |

- Each step appends `{step, t, actor, verb, args, ok, error, ms}` to `actions.jsonl`. A verb error is expected
  sometimes (a part that cannot come off) and counted per verb; a verb above 20 % errors is listed in the report (a
  driver bug or a real block). `-Replay` runs the same actions in the same order with the same actors and spacing; it
  reproduces the sequence, not the timing, which is enough to bisect most findings.
- Checkpoint (quiesce): stop actions, clear `net-delay`/`net-hold`, bring every client back to the garage, wait until
  every loaded car is `Ready` on every client and no part is held, wait 5 s, then `Wait-HarnessDumpsAllEqual` (new in
  `HarnessClient.psm1`: N-way, each instance against the first) over every shared dump section (`stats`, `inventory`,
  `cars`, `placement`, `jobs`, `tools`, `toolPositions` and any later shared section; `local`, `roster`, `status`
  excluded) with 60 s timeout, then `desync check` and a match for every (client, section) in the server log. Result
  in `checkpoints.jsonl`; dumps are kept only for a failed checkpoint and the first and last one (an overnight run
  would otherwise write hundreds of MB).
- On the first failure: `bug-report` on one client when the verb exists (row 14 d), all dumps saved, and the soak goes
  on to the end (later failures are counted; one failure must not hide a second bug) unless `-StopOnFailure`.
- `Run-Soak.ps1 -Hours -Seed -Lane (3) -Headless`: wraps `Run-Session -Lane 3 -Scenario soak -ScenarioArgs` (new:
  a hashtable splatted into the scenario after `-Ctx`), then `Show-SoakReport.ps1`. Long runs are started by the user
  or with the user's go-ahead (open question 2); they never start while the user is at the PC
  (`Get-UserIdleSeconds.ps1` ≥ 600 s) unless `-Now`.

### D6. Pass/fail rules and budgets

Fail (any of):
1. A checkpoint whose dumps still differ after 60 s, or a digest mismatch in its forced `desync check`.
2. A confirmed desync (`[Desync] … resending` or `is persistent`) at any time: row 14 repaired it, but it means a
   missed hook; the diff record is copied into the run folder.
3. A client that leaves the session other than by a storm step (status `connected` false, or a `lastDisconnect`).
4. A server `[ERROR]` line, a client `HarmonyException`, or an exception logged by the mod, except entries on the
   scenario's allow-list (each with a reason and the owning row).
5. A storm step whose checks fail (D8).
6. Watchdog stop (D2).
7. Runs ≥ 60 min: server private bytes at the end > 1.5 × the value at minute 10; the server log or any client's
   `Latest.log` growing more than 50 MB/h.

Budgets (initial values, reported as `WARN` and noted in STATUS until the user confirms them, open question 1):
average download per client ≤ 50 kB/s and peak 10 s window ≤ 1 MB/s outside snapshots; average upload ≤ 20 kB/s;
server CPU ≤ 25 % of one core with four players; handler max ≤ 50 ms and lock wait max ≤ 100 ms outside snapshots;
late join ≤ 60 s with a full garage and parking (D7); game private bytes slope ≤ 200 MB/h.

### D7. Late join with a full garage and parking

`scenarios/latejoin-full.ps1` (`# run-all: lane 3`), parameters `-ParkingLevels` (2 = 20 slots), `-Rebuild`.

- Fill (first instance): unlock parking to `-ParkingLevels` (`parking-unlock`, with `stats-add` money), put a car on
  every garage loader (count from the dump) using at least three models, each with 20 random parts unmounted into the
  inventory and `cardetails-randomize`; spawn and park cars until every unlocked slot is full (the
  `car-parking-full` loop); an item on every slot machine; inventory ≥ 300 items; one accepted job. About 15 minutes.
- Fixture: the server's save after the fill is copied to `%USERPROFILE%\CMS21-TestInstalls\fixtures\
  full-garage_L<levels>_<save and section versions>.json` (outside the repo: several MB) and reused while
  `--check-save` loads it and the versions match; `-Rebuild` forces a new fill. A run with a fixture stops the
  server, copies the fixture into the lane's `Saves`, and starts it again (`Run-Session` restores the save afterwards).
- Measure, three joins each: (a) the first client joins the loaded session alone; (b) with all but one client in
  session and working (soak actions continue on them), the last one joins; (c) the same client leaves and rejoins.
  From the snapshot line (D3): time to `SyncAck`, bytes, build time, item counts per key; from the client: `connect`
  to `syncAcked` and `playable`; from `perf`: the other clients' max frame time and the server's max lock wait during
  the join; traffic per packet type of the joining slot. Also the server's start time with the fixture (load +
  migration) and its save time. Medians go to `latejoin.json`.
- Pass: every join reaches `SyncAck` without row 7's no-progress timeout and in ≤ 120 s; dumps of all clients equal
  afterwards. The 60 s budget (D6) is a `WARN`. The numbers are recorded in this design's "Measurements" section and
  STATUS.

The late-join path itself is row 7's (`AskForSync` → providers in `SyncOrder` under `StateLock` → `SyncEnd` →
`SyncAck`) and does not change here. What this change adds is the measurement of its three costs: the snapshot is
built under `StateLock` (every other player's handlers wait meanwhile, seen as lock wait), its bytes (the largest
burst in a session), and the joining client's apply time (frame-bound).

### D8. Disconnect storms

`scenarios/storm.ps1` (`# run-all: lane 3`) runs each kind once (seeded order), followed by a checkpoint (D5);
`soak -StormEveryMinutes` picks one kind at random. Before each kind the scenario saves the state it must get back
(dumps at a checkpoint, the server's `players` list).

| Kind | Steps | Checks |
|---|---|---|
| K1 crash | `Stop-Process -Force` on one game while others work; copy its `Latest.log` to `client_<X>_<n>.log`; `Start-HarnessInstance`; reconnect, retrying `DuplicateIdentity` for 20 s (half-open slot) | server logs the leave within 15 s; others' rosters drop and regain the avatar; same identity record (count unchanged); position within 0.5 m if it was in the garage; dumps equal |
| K2 flap | one client `to-menu` + `connect` three times in a row, 2 s apart | each rejoin gets a new snapshot and acks; no stray avatar; dumps equal |
| K3 stall | `net-hold` past the 10 s heartbeat timeout (task 6.1 checks it stops both directions, and extends `net-hold` with an `out` mode if not) | server times the client out; client reaches the menu (row 7 D10); rejoin as K1 |
| K4 burst | three clients leave within 1 s (two killed, one `to-menu`) while the fourth works; all three reconnect at the same moment | three snapshots back to back; the fourth's changes made meanwhile reach everyone; dumps equal; lock wait recorded |
| K5 held | a client takes a part claim (`part-claim`), opens the balancer (`tool-balance-open`) or starts a test drive, and is killed | claims and the balancer lock are released within 15 s (row 6 `Left`); the test-drive car returns (row 13 fallback); another client can use them |
| K6 stop | `Send-ServerCommand stop` with everyone in session; `Start-TestServer`; all reconnect | every client reaches the menu with `ServerShutdown`; the save is newer; dumps equal the checkpoint before the stop exactly |
| K7 kill | (i) checkpoint + `save` + `Stop-TestServer` at once; (ii) `Stop-TestServer` mid-activity | (i) equal to the checkpoint exactly; (ii) all clients equal to each other after the restart, the server loaded the main file (no fallback, no quarantine), loss bounded by `autosave_interval_seconds` |
| K8 mid-join | kill the server while a client is between `SyncBegin` and `SyncAck` (after `net-delay 2000` on it) | the client leaves through row 7's timeout or server-loss path without a garage half-applied; rejoin works |

"Without loss" therefore means: exact for a graceful stop (K6), exact to the last save for a hard kill (K7), and exact
for every client crash (K1–K5), since the server keeps all shared state. In batch mode a relaunched game writes a new
`Latest.log`; `Save-ClientLogSlices` resets the offset when the file is shorter than the offset or its creation time
changed.

### D9. Findings, reports and regression use

- A failure is reproduced from its run folder (`actions.jsonl` + `-Replay`, seed, `deployed.json`), then fixed in the
  owning row's code on a `fix/<topic>` branch with a step added to that row's scenario, and logged in STATUS with the
  run id. A performance finding over a budget goes to QUESTIONS.md with the numbers and options (snapshot batching,
  movement rate, compression, serializer); anything larger than S becomes a follow-up change in the ROADMAP.
- Run folder (`tools\runs\<ts>_L3_<scenario>`): `actions.jsonl`, `checkpoints.jsonl`, `storms.jsonl`,
  `perf_server.jsonl`, `perf_processes.csv`, `frames.csv`, `latejoin.json`, `summary.json`, diff records, kept dumps,
  client logs per instance and launch.
- Regression: `Run-All -Lanes 3` (`scale-connect`, `soak` 10 min, `latejoin-full` with its fixture, `storm`; about
  45 minutes, both lanes blocked) runs when server, Core or session code changed, and before each milestone from M5
  on. A long soak (≥ 2 h, with storms) runs before M5 closes and before each release. Two-lane regressions stay as
  they are.

### What the server stores vs relays

Stores nothing new in the save and relays nothing new. Keeps counters and timings in RAM; writes
`Log/perf_<start>.jsonl` only when `perf_log_interval_seconds` > 0, plus the snapshot log line.

## Risks / Trade-offs

- [Four games on one PC are CPU-bound, so timing numbers mix the mod's cost with contention] → frame cap at 30, the
  server's own numbers (handler time, lock wait, bytes) are contention-free; late-join times are tracked as a trend on
  this PC, not promised to players; the friend playtest gives real numbers.
- [Lane 3 blocks both lanes for 45 minutes or a whole night] → only on the triggers in D9; long soaks by the user's
  go-ahead.
- [Memory: four games plus a leak over hours can exhaust commit with Claude Code's guard off] → gates, watchdog at
  4 GB headroom, the headless option.
- [A random soak finds a bug once and never again] → seed + action log + replay; failures don't stop the run.
- [The soak's model drifts from the game (a verb silently did nothing)] → refreshed from the dumps at each checkpoint;
  error rates per verb in the report.
- [Counting on every packet slows the server] → `Interlocked` on fixed arrays, measured in task 2.4.
- [`BinaryFormatter` overhead dominates the bandwidth] → expected; numbers go to QUESTIONS.md, a serializer change is
  a follow-up, not this row.
- [Storm kinds depend on row 7 part 2 (identity, `PlayerRestore`, server loss), still waiting for its runs] → group 6
  starts after row 7 part 2 is merged; groups 1–5 do not need it.

## Migration Plan

No save change. `perf_log_interval_seconds = 0` is appended to `server_config.ini` if missing. `Server3` is created by
`Deploy-Mod.ps1 -Lane 3`; `Setup-TestInstalls.ps1` already creates A–D. Existing scenarios and two-lane runs keep
working; `Run-All` now deploys inside `Run-Session -Deploy`.

## Hand checks (the user)

1. The M5 playtest: 3–4 friends, at least two hours over Steam, the host's server with
   `perf_log_interval_seconds = 10`; one player quits the game hard once, one rejoins, the host restarts the server
   once. Send `Log/perf_*.jsonl`, the server log and F8 bundles (row 14 d) of anything odd. Compare bandwidth, CPU and
   join times with the harness numbers.
2. Upload of the host's line: the summed server upload of that session against the host's real upload speed.
3. One join from a friend over the internet into a full garage and parking (the late-join time with real latency).

## Open Questions

1. Budgets in D6 (bandwidth, CPU, handler time, late join 60 s, memory slope): default as written; exceeded = `WARN`
   until confirmed.
2. Overnight soaks on this PC (four games for hours, power and heat): default — only when the user starts them or
   says yes for that night; never while the user is at the PC.
3. Headless C and D for long soaks if task 1.6 shows they work: default — allowed for runs ≥ 2 h, normal games for
   the regression runs.

## Measurements

(Filled in by tasks 5.3, 7.1 and 7.2.)
