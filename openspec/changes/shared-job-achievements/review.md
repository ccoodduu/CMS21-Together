# Review: shared-job-achievements (row 30)

**Verdict: ready after fixes** (one blocker: as drafted, the finisher's capture records nothing).

Checked against main `3645acc`: `JobEndContext.cs`, `JobHooks.cs`, `JobsService.cs`, `JobsState.cs`,
`WorldStatesPackets.cs` (client), `StatsGuard.cs`, `JobsTrace.cs`, and the decompiles `clean/GameScript._EndJobCoroutine_d__139$$MoveNext.c`,
`orders_clean/CMS.Platforms.{PlatformManager,Steam.SteamAchievements,Base.BaseAchievements}$$IncrementStat.c`.

What holds:

- The stat calls are where the design says. Three use the virtual at vtable +0x1D8 (`stat_finish_order`,
  `stat_bonus_exp`, `stat_bonus_money`, all only when `IsCompleted`). `stat_finish_allmissions` goes through
  `PlatformManager.IncrementStat`, which tail-jumps to the same virtual. So a hook on
  `SteamAchievements.IncrementStat` sees all four, and one on `PlatformManager.IncrementStat` misses three.
- `SteamAchievements.IncrementStat` runs `BaseAchievements.IncrementStat` (in-memory `Value`, `UpdateProgress`,
  `Unlock`), then `SteamUserStats.SetStat` and `SendStats` → `StoreStats`. `StatsGuard` blocks `SetStat`, `StoreStats`,
  `SetAchievement` and `IndicateAchievementProgress`, so a harness game can count calls without writing to the account.
  This makes the observation plan safe.
- On receivers the award can't double count with the job end. The only stat calls are inside the finisher's
  `EndJobCoroutine`, and receivers never run it.

## Blockers

**B1. The capture window has already closed when the stats are incremented.** `JobEndContext.Commit` sets
`jobId = -1` (so `IsActive` turns false) in the `OrderGenerator.CancelJob` prefix (`JobHooks.BeforeCancel`). In the
coroutine, `CancelJob(job.id)` comes *before* the mission bookkeeping, `DeleteCar`, `GarageOnFootWithoutFader`,
`GarageLoader.Save` and the four stat calls, all in the same `MoveNext` step (decompile lines ~256–335, then
`yield WaitForEndOfFrame`, state 2). A prefix that "records only inside `JobEndContext`" therefore records nothing, and
the report is always empty. The proposal also says "the existing `GameScript._EndJobCoroutine_d__139.MoveNext`
postfix", but no such client hook exists: only the harness `JobsTrace` patches that method. Fix, either:
- (a) Give the stat capture its own scope. Add a `MoveNext` prefix/postfix pair on `_EndJobCoroutine_d__139`. The
  prefix opens a capture keyed by `__instance.job.id` (`IsMission`, `IsCompleted`). The `SteamAchievements.IncrementStat`
  prefix records while that capture is open. The postfix closes it, and sends the report when the step recorded
  something or when `__1__state` became 2. Do not send from "the final `MoveNext` that returns false": if the scene
  changes or the `GameScript` dies, that call never comes.
- (b) Drop the client report entirely, which is simpler and removes the Harmony-on-override risk from the mod.
  `JobsService.OnJobEnd` already has `packet.IsCompleted`, `active.Job.BonusToExp/BonusToMoney` and
  `active.Job.IsMission`. Derive `stat_finish_order` (completed), `stat_bonus_exp`/`stat_bonus_money` (completed and
  bonus) and "mission finished" on the server. Send `JobStatsAward { JobId, Stats, MissionFinished }`, and have the
  receiver add `stat_finish_allmissions` when `GlobalData.MissionsAmount <= GlobalData.MissionsFinished` (row 3 keeps
  the counters equal). D2's report validation, the 60 s window, the "server restart loses the report" case and spike
  1.1 then go away. The draft already lists this as the fallback. I recommend making it the main path.

## Major

**M1. `stats-trace` counts twice.** Task 3.2 counts calls "from a prefix on `PlatformManager.IncrementStat` and the
platform override". `PlatformManager.IncrementStat` always ends in the override, so `stat_finish_allmissions` and every
award applied on a receiver (`PlatformManager.IncrementStat(id, 1)` per D3) would count 2. Step 1's "B's trace shows
the same two ids once each" then fails on correct code. Fix: count only at `SteamAchievements.IncrementStat`, the one
point every path reaches. If the override cannot be patched, count only at the outer call and say that the trace then
misses the three virtual calls.

**M2. Contributors are keyed by loader, but a job car can change loaders.** D1 records contributions "on the job's
loader" (Impact: "keyed by the job's loader"). `ActiveJobEntry.CarLoaderId` is set once in `OnJobStarted` and never
updated when the car moves (row 2/19 placement moves). Fix: when a change on loader L is accepted, resolve the job from
`CarState.LoadedCars[L].Spawn.JobID` (`Spawn.IsJob`), the same test `ClearsCar`/`DeleteJobCar` use, not from
`ActiveJobEntry.CarLoaderId`.

## Minor

- m1. `stat_level` noise: `WorldStatesPackets` calls `PlatformManager.IncrementStat("stat_level", diff)` on every
  world state, often with 0. The trace and any capture must filter to the four job ids. Say so in 3.2.
- m2. Sandbox difficulty: `SteamAchievements.IncrementStat` writes `sandbox_<id>` and `ValueSandbox` when the
  difficulty mode is 2. An award reproduces this only if every client runs the same difficulty in a session. State
  that it does (or why it does not matter).
- m3. Persisted contributor identities (`steam:`/`guid:` keys) in the `jobs` section reach bug reports.
  `Redaction.DropPlayerKeys` only scrubs the `players` section. Store `PlayerRecords.ShortKey` or another non-secret id,
  or extend the redaction, and add a `RedactionCheck` case.
- m4. Step 2 restarts the server just to change `job_stats_to`. That costs a rejoin cycle in the scenario. Add a
  runtime server command (`jobs stats-to garage`) and keep one restart check.
- m5. Step 4 ("B disconnects before A finishes → no award; B rejoins → nothing later") passes on the old code too. It
  is fine as a guard but is not part of "fails on old code". Only step 1 is the proof.
- m6. Spec "No award from a forged or repeated report" assumes the report exists. With fix B1(b) it becomes
  "the server awards a job's statistics once, when it accepts that job's end".

## Size

S (≈ 1–2) holds with B1(b). With B1(a) plus a Harmony spike on the override, plan for 2.

## Review resolution

Applied 2026-10-08. Checked on the branch: `JobsService.OnJobEnd` has `packet.IsCompleted`, `active.Job.IsMission`;
`ModJob` has `BonusToExp`/`BonusToMoney`; `JobsService` resolves job cars by `Spawn.IsJob`/`Spawn.JobID`;
`Redaction.DropPlayerKeys` only scrubs `players`; the difficulty is a server setting.

- **B1** Fixed with option (b), the review's recommendation: the server derives `stat_finish_order`, `stat_bonus_exp`,
  `stat_bonus_money` and `MissionFinished` in `OnJobEnd` (D2); `JobStatsReport`, the capture hook, the 60 s window and
  the report validation are gone; the receiver adds `stat_finish_allmissions` from the mission counters (D3).
- **M1** Fixed. `stats-trace` counts only at `SteamAchievements.IncrementStat` (task 3.2); spike 1.1 covers the case
  where the override cannot be patched.
- **M2** Fixed. D1 resolves the job from the car's spawn record; the scenario moves the car before the end; `JobsCheck`
  covers it.
- **m1** The trace filters to the four job ids. **m2** D3: the session difficulty is one server setting. **m3**
  contributors stored as `PlayerRecords.ShortKey`, `RedactionCheck` case. **m4** server command `jobs stats-to`, one
  restart check kept. **m5** step 3 labelled a guard; step 1 is the proof. **m6** spec requirement now "awarded once,
  when the server accepts that job's end".
- Size: S (≈ 1–2).
