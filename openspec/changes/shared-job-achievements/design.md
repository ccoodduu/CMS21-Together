# Design

## Context

- Row 3: the finisher's client runs the vanilla `EndJob`; `JobEndContext` covers the payout (money and XP go to the
  server as `JobEndRequest`) and closes in the `OrderGenerator.CancelJob` prefix; the server ends the job once
  (`JobsService.OnJobEnd`, which has `packet.IsCompleted` and the active job's `BonusToExp`, `BonusToMoney`,
  `IsMission`), clears the car and broadcasts `JobRemoved { Reason = Ended, IsCompleted, Missions }`. Its spec promised
  stats for every connected player through a `JobStatsAwarder` that was never built.
- In `GameScript.<EndJobCoroutine>d__139`, `CancelJob(job.id)` comes before the four stat calls in the same `MoveNext`
  step, so a capture tied to `JobEndContext` would record nothing. Three stats use the virtual at vtable +0x1D8
  (`stat_finish_order`, `stat_bonus_exp`, `stat_bonus_money`, only when `IsCompleted`); `stat_finish_allmissions` goes
  through `PlatformManager.IncrementStat`, which tail-jumps to the same virtual (`SteamAchievements.IncrementStat`,
  `0x180D99D80`). It runs `BaseAchievements.IncrementStat` (in-memory `Value`, `UpdateProgress`, `Unlock`; in difficulty
  mode 2 also `sandbox_<id>`/`ValueSandbox`), then `SteamUserStats.SetStat` and `StoreStats`. Only the finisher runs the
  coroutine, so receivers never count a job stat themselves.
- The harness blocks the Steam writes (`StatsGuard` skips `SetAchievement`, `SetStat`, `StoreStats`,
  `IndicateAchievementProgress`), so calls can be counted in test games without touching the account.
  `WorldStatesPackets` calls `PlatformManager.IncrementStat("stat_level", diff)` on every world state (often 0).
- The server sees who changed what: part records and detail entries carry the sender (row 19 part 1), lock grants
  carry the holder (row 18), examines are part record changes. `ActiveJobEntry.CarLoaderId` is set once when the job
  starts; the car's spawn record (`Spawn.IsJob`, `Spawn.JobID`) follows the car (`ClearsCar`, `DeleteJobCar` use it).
- The session difficulty is a server setting (`new_session_difficulty`, the `world` section), the same on every client.

## Goals / Non-Goals

**Goals:** the job stats reach every connected contributor once; never twice for the finisher; the rule is
configurable.

**Non-Goals:** sharing per-player action stats; awarding offline players; upgrade achievements (open question 5).

## Decisions

### D1. Contributors per active job

`JobContributors` maps an active job id to a set of player short keys (`PlayerRecords.ShortKey`, stable across a rejoin
and not secret). When a change on loader L is accepted, the job is `CarState.LoadedCars[L].Spawn.JobID` if
`Spawn.IsJob` and that job is active. Added:

- on `JobStarted` (the taker);
- in `CarPartsHandlers` when a part record or transaction from client X on a job car is accepted;
- in `CarDetailsStore.OnUpdate` when an entry from X on that car is accepted;
- in `CarLocks` when a lock on that car is granted to X;
- in `JobsService.OnJobEnd` (the finisher).

Stored with the active job (`ActiveJob.Contributors`, `[OptionalField]`, `jobs` section version bump, old saves: taker
only). All writes under `GameDataManager.StateLock`. `RedactionCheck` gains a case that a bug report's `jobs` section has
no identity key.

### D2. Stats derived on the server

In `JobsService.OnJobEnd`, after the end is accepted: `Stats` = `stat_finish_order` if `packet.IsCompleted`, plus
`stat_bonus_exp` if completed and `Job.BonusToExp`, plus `stat_bonus_money` if completed and `Job.BonusToMoney`;
`MissionFinished` = `Job.IsMission`. This mirrors the game's own conditions in `EndJobCoroutine`. A job end that is
refused (row 3) awards nothing, and a second end of the same job is refused, so a job's stats are awarded at most once.
No client report and no game hook: this removes the capture window problem and the Harmony-on-override risk.

### D3. Award

After the `JobRemoved` broadcast, the server sends `JobStatsAward { JobId, Stats, MissionFinished }` to each connected
contributor except the finisher (or, per `job_stats_to`, to everyone in the garage except the finisher, or to nobody).
An award with no stats and no mission is not sent. The receiver calls
`Singleton<GameManager>.Instance.PlatformManager.IncrementStat(id, 1)` for each id, and for `MissionFinished` adds
`stat_finish_allmissions` when `GlobalData.MissionsAmount <= GlobalData.MissionsFinished` (the counters arrived with
`JobRemoved`). Once per job id (a small set of awarded job ids, cleared on disconnect). The award is applied wherever the
player is (garage or away): it touches only the platform stats. Every client runs the session's difficulty, so the
sandbox variant is written the same way as on the finisher.

### D4. What the server stores and relays; late join

Stored: the contributor set of each active job (`jobs` section). Nothing about an ended job is kept. Relayed: nothing;
the award is computed. A player who joins after a job ended gets nothing for it; a contributor who rejoins during the job
keeps the contribution (short key). A server restart during a job keeps the contributors.

## Risks / Trade-offs

- [The game's stat conditions differ from D2 in a case not seen in the decompile] → the scenario compares B's counted
  stats with A's for a completed bonus job.
- [A contributor gets an achievement the user considers unearned] → `job_stats_to` lets the host choose.
- [Steam rate limits on `StoreStats`] → one award per job; the game itself stores after each increment.

## Migration Plan

New packet appended; `jobs` section version bump with a default (taker only). Client and server update together (the
version check enforces it). Rollback: revert; saved contributor lists are ignored by older servers.
