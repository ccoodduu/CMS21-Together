# Design

## Context

- Row 3: the finisher's client runs the vanilla `EndJob`; `JobEndContext` covers the coroutine (money and XP go to the
  server as `JobEndRequest`); the server ends the job once (`JobsService.OnJobEnd`), clears the car and broadcasts
  `JobRemoved { Reason = Ended }`. Its spec promised stats for every connected player through a `JobStatsAwarder` that
  was never built.
- The coroutine's stat calls come after `GameScript.EndJob` sent the request: payout and XP first, then the car delete,
  then `stat_finish_order`, `stat_bonus_exp`, `stat_bonus_money` (virtual `IncrementStat` on the achievement system) and
  `stat_finish_allmissions` (`PlatformManager.IncrementStat`). `PlatformManager.IncrementStat` tail-calls the same
  virtual, so a prefix on the virtual target sees all four; a prefix on `PlatformManager.IncrementStat` alone does not
  (the coroutine calls the virtual directly for three of them). The hook is therefore on
  `CMS.Platforms.Steam.SteamAchievements.IncrementStat(string, int)` (the platform's override, `0x180D99D80`), with
  `PlatformManager.IncrementStat` as fallback if Harmony cannot patch the override (task 1.1).
- The harness blocks the Steam writes (`StatsGuard` skips `SteamUserStats.SetAchievement/SetStat/StoreStats/…`), but
  `BaseAchievements.IncrementStat` still runs, so calls can be counted in test games without touching the account.
- The server sees who changed what: part records and detail entries carry the sender (row 19 part 1), lock grants
  carry the holder (row 18), examines are part record changes.

## Goals / Non-Goals

**Goals:** the job stats reach every connected contributor once; never twice for the finisher; the rule is
configurable.

**Non-Goals:** sharing per-player action stats; awarding offline players; upgrade achievements (open question 5).

## Decisions

### D1. Contributors per active job

`JobContributors` maps an active job id to a set of player keys (row 7 identity keys, so a rejoin keeps a player's
contribution). Added:

- on `JobStarted` (the taker);
- in `CarPartsHandlers` when a part record or transaction from client X on the job's loader is accepted;
- in `CarDetailsStore.OnUpdate` when an entry from X on that loader is accepted;
- in `CarLocks` when a lock on that loader is granted to X;
- in `JobsService.OnJobEnd` (the finisher).

Stored with the active job (`ActiveJob.Contributors`, `[OptionalField]`, `jobs` section version bump, old saves: taker
only). All writes under `GameDataManager.StateLock`.

### D2. Report after the coroutine

The finisher records the stat calls while `JobEndContext.IsActive` and the coroutine runs, and sends
`JobStatsReport { JobId, Stats = [{ Id, Amount }] }` from the coroutine's final `MoveNext` (returns false). A report
with no stats (job not completed, no bonus) is still sent, so the server's log shows the end-to-end path. The server
accepts a report only from the client that ended that job, within 60 s, once, with ids in
`{stat_finish_order, stat_bonus_exp, stat_bonus_money, stat_finish_allmissions}` and amount 1; anything else is dropped
with a log line (no state changes, nothing to answer: D16 does not apply, the client's own state already equals the
server's).

### D3. Award

The server sends `JobStatsAward { JobId, Stats }` to each connected contributor except the finisher (or, per
`job_stats_to`, to everyone in the garage or nobody). The receiver calls
`Singleton<GameManager>.Instance.PlatformManager.IncrementStat(id, amount)` for each, once per job id (a small set of
awarded job ids, cleared on disconnect). The award is applied wherever the player is (garage or away): it touches only
the platform stats.

### D4. Late and repeated paths

- A second end of the same job is refused by row 3 (D16 answer), so no second report is accepted.
- A server restart between the end and the report loses the report (rare; accepted).
- Missions: the finisher's game decides `stat_finish_allmissions` from its mission counters, which row 3 keeps equal on
  every client; the report carries it.

## Risks / Trade-offs

- [Harmony cannot patch the platform override (shared native body)] → task 1.1 checks with `work\at.py`; fallback:
  derive the three job stats on the server from `IsCompleted`, `BonusToExp`, `BonusToMoney` and the mission counter.
- [A contributor gets an achievement the user considers unearned] → `job_stats_to` lets the host choose.
- [Steam rate limits on `StoreStats`] → one award per job; the game itself stores after each increment.

## Migration Plan

New packets appended; `jobs` section version bump with a default (taker only). Client and server update together (the
version check enforces it). Rollback: revert; saved contributor lists are ignored by older servers.
