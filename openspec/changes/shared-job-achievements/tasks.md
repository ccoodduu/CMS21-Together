# Tasks

Prerequisites (all merged): rows 3, 7, 18, 19 part 1.

## 1. Spike

- [ ] 1.1 Check that `SteamAchievements.IncrementStat` (`0x180D99D80`) has its own native body (`work\at.py`) and that a
      logging prefix fires for all four job stats when A finishes a completed job with a bonus (`jobs-trace` already
      lists the stat calls; extend it with the override). Done when design.md Context names the hook that fires, or the
      D2 fallback is chosen.

## 2. Core and server

- [ ] 2.1 Append `JobStatsReport`, `JobStatsAward` (with `ModStatIncrement { string Id; int Amount }`) to `PacketTypes`
      and `JobPackets.cs`; `ActiveJob.Contributors` (`[OptionalField]`), `jobs` section version bump with the D1 default.
- [ ] 2.2 `JobContributors` (D1): record from `JobStarted`, part and detail acceptance, lock grants, job end; server
      command `jobs contributors <jobId>`; `ServerConfig` `job_stats_to`. Verify: the server unit check
      (`--check-merges` style, `JobsCheck`) adds a contributor per path and none for a refused change.
- [ ] 2.3 Report check and award (D2, D3). Verify with 3.2.

## 3. Client and harness

- [ ] 3.1 `Logic/Jobs/JobStats.cs`: capture inside `JobEndContext`, send at the coroutine end, apply awards once per
      job id.
- [ ] 3.2 Harness `stats-trace on|off|report` (calls per stat id since `on`, from a prefix on
      `PlatformManager.IncrementStat` and the platform override; `StatsGuard` stays). Scenario `job-stats` (two
      clients), `stats-trace on` on both:
      1. A takes an order with a money bonus; B unmounts and mounts one part of that car; A finishes the completed job
         (`job-finish`) → A's report has `stat_finish_order` and `stat_bonus_money` once each; B's trace shows the same
         two ids once each; A's trace shows them once each (no second count);
      2. A takes another order and finishes it alone, B stays in the garage without touching the car → B's trace shows
         no job stat; with `job_stats_to = garage` (server restart with the setting) the same run awards B;
      3. a report sent twice (`job-stats-resend`) or by B for A's job → dropped, B's trace unchanged;
      4. B touched the car, then disconnects before A finishes → no award, no error; B rejoins → nothing awarded later.
      Fails on the old code at step 1 (B gets nothing). Verify: `Run-Session.ps1 -Scenario job-stats` passes and
      `Run-All -Changed` (areas `jobs`, `economy`, smoke) passes.

## 4. Docs

- [ ] 4.1 Update `sync-orders-and-jobs`' spec requirement "Ending a job" (stats to contributors) and design D8; QUESTIONS.md:
      replace both old stat defaults with this rule; ROADMAP row 30 status; STATUS entry.
