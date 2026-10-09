# Tasks

Prerequisites (all merged): rows 3, 7, 18, 19 part 1.

## 1. Spike

- [x] 1.1 Harness trace only: check that Harmony can patch `SteamAchievements.IncrementStat` (`0x180D99D80`, `work\at.py`
      for a shared native body) and that a counting prefix there fires for all four job stats when A finishes a
      completed job with a bonus. If it cannot be patched, `stats-trace` counts at `PlatformManager.IncrementStat` only
      and the scenario's finisher checks are dropped (that trace misses the three virtual calls). The mod itself hooks
      nothing (D2).

## 2. Core and server

- [x] 2.1 Append `JobStatsAward { JobId, Stats (string[]), MissionFinished }` to `PacketTypes` and `JobPackets.cs`;
      `ActiveJob.Contributors` (`[OptionalField]`, short keys), `jobs` section version bump with the D1 default.
- [x] 2.2 `JobContributors` (D1): record from `JobStarted`, part and detail acceptance, lock grants, job end, job
      resolved from the car's spawn record; server commands `jobs contributors <jobId>` and `jobs stats-to <rule>`;
      `ServerConfig` `job_stats_to`; `RedactionCheck` case. Verify: a server self-check (`JobsCheck`, `--check-merges`
      style) adds a contributor per path, none for a refused change, and the right job after the car moved to another
      loader.
- [x] 2.3 Stats and award (D2, D3). Verify with 3.2.

## 3. Client and harness

- [x] 3.1 `Logic/Jobs/JobStats.cs`: apply awards once per job id, mission rule.
- [x] 3.2 Harness `stats-trace on|off|report` (calls per stat id since `on`, counted only at
      `SteamAchievements.IncrementStat` and only for `stat_finish_order`, `stat_bonus_exp`, `stat_bonus_money`,
      `stat_finish_allmissions`, so `stat_level` noise and the outer `PlatformManager` call never count twice;
      `StatsGuard` stays). Scenario `job-stats` (two clients), `stats-trace on` on both:
      1. A takes an order with a money bonus; B unmounts and mounts one part of that car; B moves the car to another
         lift; A finishes the completed job (`job-finish`) → B's trace shows `stat_finish_order` and `stat_bonus_money`
         once each; A's trace shows them once each (no second count);
      2. A takes another order and finishes it alone, B stays in the garage without touching the car → B's trace shows
         no job stat; server `jobs stats-to garage`, A finishes a third job alone → B is awarded; one server restart
         with `job_stats_to = garage` in the config keeps that rule;
      3. B touched a car, then disconnects before A finishes → no award, no error; B rejoins → nothing awarded later
         (a guard; it passes on the old code too).
      Fails on the old code at step 1 (B gets nothing). Verify: `Run-Session.ps1 -Scenario job-stats` passes and
      `Run-All -Changed` (areas `jobs`, `economy`, smoke) passes.
      **Done (2026-10-09):** the jobs end through `job-end-direct` like in the other job scenarios: the game's own
      `EndJob` stops at its car checks (bolts, fluids, wheels) on a test job car, so the finisher's vanilla count does
      not run. The finisher check is therefore "A is sent no award" (A's trace stays 0), and `job-finish ... complete`
      was dropped. Spike 1.1: the prefix on `SteamAchievements.IncrementStat` patches and counts the awarded stats.

## 4. Docs

- [x] 4.1 Update `sync-orders-and-jobs`' spec requirement "Ending a job" (stats to contributors) and design D8; QUESTIONS.md:
      replace both old stat defaults with this rule; ROADMAP row 30 status; STATUS entry.
