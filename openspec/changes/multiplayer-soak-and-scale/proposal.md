# Proposal

## Why

M5 promises "3–4 players, multi-hour session, crashes and rejoins without loss", but every scenario so far runs two
instances for a few minutes. Nothing has shown that four clients agree after an hour of mixed work, how much the
server sends per player (packets are `BinaryFormatter` objects, and nobody counts their bytes), how long a late join
takes once the garage and the parking are full, or whether a burst of crashes, rejoins and a server restart loses
anything. Upstream's reports of "after a while it desyncs" and "rejoin breaks the garage" were all found by players,
never by a test. The harness already has the parts (verbs for cars, parts, tools, jobs, travel, economy, presence,
`net-hold`/`net-delay`, digests, `Wait-HarnessDumpsEqual`) and row 7 part 2 brings identity, rejoin and server-loss
handling; what is missing is a way to run them with four clients, for hours, under measurement.

## What Changes

**Test lanes**
- A third test lane, the **scale lane** (lane 3): instances A, B, C and D against its own server `Server3` on port
  7797. It takes the locks of lanes 1 and 2 for the whole run, so it never shares an install with them. Every lane now
  holds a per-lane lock for its whole run (not only while its games start), and `Deploy-Mod.ps1` takes the same locks.
- Memory gates per lane (lane 3 needs about twice the commit headroom of a two-instance lane), a memory watchdog that
  stops a long run cleanly before the PC runs out of commit or RAM, and a per-instance relaunch helper
  (`Start-HarnessInstance`) for crash tests.
- `Run-All -Lanes 3` runs only scenarios marked `# run-all: lane 3`; lanes 1 and 2 skip them.
  `Run-Session -ScenarioArgs` passes parameters (minutes, seed) to a scenario; `Run-Soak.ps1` wraps a long soak.

**Measurement**
- Server: traffic counters per packet type (messages and bytes, sent and received, per client and transport),
  handler time and `StateLock` wait per packet type, main-loop tick time, process CPU and memory, save size and save
  time, log sizes. Written as one JSON line every `perf_log_interval_seconds` to `Log/perf.jsonl` (0 = off, the
  default); server command `perf` (`perf`, `perf reset`, `perf top`). One log line per snapshot with item counts,
  bytes and the time until `SyncAck`.
- Clients: harness verb `perf` (frame time over the last 10 s, managed and IL2CPP heap); harness verb `fps-cap`;
  a PowerShell sampler for CPU, private bytes and working set of every game and the server, and the growth of every
  log file.

**Scenarios**
- `scale-connect`: four clients join, see each other, and agree on every shared section.
- `soak`: a seeded, random mix of real player actions over N minutes (10 in a regression, hours by hand), with
  periodic equality checkpoints across all clients, a forced digest round (row 14), sampling, an action log that can
  be replayed, and pass/fail rules.
- `latejoin-full`: a client joins a session whose garage loaders and unlocked parking slots are all full; join time,
  snapshot size per section and the other players' frame times are measured against a budget.
- `storm`: client hard kills and relaunches, quick leave/rejoin, network stalls past the heartbeat timeout, several
  clients rejoining at once, a client killed while it holds a claim, and a graceful and a hard server restart in the
  middle of a session; nothing may be lost (bounded by the last save for a hard kill), identities and positions come
  back.
- `Show-SoakReport.ps1` summarizes a run folder (bandwidth per client and packet type, CPU, memory slope, join times,
  checkpoint and storm results).

**Packets**: none added or changed. **Hooks**: none on game methods (the harness `perf` verb reads Unity's
`Time.unscaledDeltaTime` and the GC counters).

**Out of scope**: fixing what the runs find in another row's sync (each finding is fixed in the owning row's code or
becomes a follow-up, see design D9); replacing `BinaryFormatter` or adding compression (follow-up if the numbers say
so); more than four players; Steam transport and real internet latency (the harness instances share one Steam account
and run DirectIP on localhost, so these are the user's hand checks); automatic reconnect.

## Capabilities

### New Capabilities
- `server-performance-metrics`: what the dedicated server measures about its own traffic, CPU, memory, handler time
  and saves, and how a host or a test reads it.
- `soak-and-scale-testing`: the four-instance test lane, long seeded sessions with equality checkpoints, late join
  with a full garage and parking, and disconnect storms, with their pass/fail rules.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Server: new `Diagnostics/Perf/` (`TrafficCounters`, `HandlerTimings`, `PerfLog`), counting in
  `Network/Server.SendToClient`, `RefuseUnassigned` and the dispatch sites of `Transport/TCP.cs`, `UDP.cs`,
  `SteamTransport.cs`; `Log/ServerWindow.TickServer` (tick time, perf line); `Data/GameDataManager.SaveSession`
  (save size and time); `Network/Handlers/AuthHandlers` (snapshot bytes and time to `SyncAck`);
  `Network/CommandSystem` (`perf`); `Data/ServerConfig` (`perf_log_interval_seconds`).
- Client mod: none.
- Test harness: `Features/PerfCommands.cs` (`perf`, `fps-cap`).
- `tools/test-env`: `TestLanes.psm1` (lane 3, gates, lane locks, `Start-HarnessInstance`), `Run-Session.ps1` (lane
  locks for the whole run, `-ScenarioArgs`, `-Deploy`, roles A–D, log slices after a relaunch, watchdog), `Run-All.ps1`
  (`# run-all: lane 3`), `Deploy-Mod.ps1` (locks, build stamp), `HarnessClient.psm1` (`Wait-HarnessDumpsAllEqual`,
  `Get-PerfSample`), new `PerfSampler.psm1`, `Run-Soak.ps1`, `Show-SoakReport.ps1`; scenarios `scale-connect`, `soak`,
  `latejoin-full`, `storm` (the existing `desync-soak` stays as row 14's two-instance check).
- Uses (not owns): row 7 identity, `PlayerRestore`, server-loss detection, `players` command, `save`/`stop`,
  autosave; row 14 `desync check` and the `[Desync]` log lines, `bug-report` (14d) when present; rows 1–6, 10, 13 harness
  verbs; row 2 parking levels (`parking-unlock`).
