# Proposal

## Why

The user wants Steam statistics and achievements for a finished job to count for everyone who worked on it, not only
for the player who hands the car back (2026-10-08, ROADMAP row 30). Today only the finisher's game increments them:
`GameScript.<EndJobCoroutine>d__139` calls the stat methods on the finishing client, and nothing tells the others.

Row 3's spec already says "every player connected at that moment SHALL receive the game's Steam statistics and
achievement progress for the finished job" (user decision 2026-10-05, design D8 `JobStatsAwarder`, task 4.3), but the
awarder was never built: no code sends or applies job stats (`rg JobStatsAwarder` finds only the documents), and
QUESTIONS.md still lists the older default "go to the player who finishes it". This change builds it, with the user's
new rule: the players who worked on the job.

What the game counts (static, `docs/spikes/orders-and-jobs.md` section 7 and the full `stat_*` list in
`docs/spikes/singleplayer-features.md` section 7):

| Stat | When | Achievements |
|---|---|---|
| `stat_finish_order` | a completed job ends | `achiv_finish_order_stage1` (1), `_stage2` (100) |
| `stat_bonus_exp` | completed, the order had the XP bonus | `achiv_bonus_exp_stage1` |
| `stat_bonus_money` | completed, the order had the money bonus | `achiv_bonus_money_stage1` |
| `stat_finish_allmissions` | the last story mission ends | `achiv_finish_allmissions_stage1` |
| `stat_level` | level-up | `achiv_level_stage1/2/3`; **already shared**: every client increments it by the level difference when the shared level changes (`WorldStatesPackets`) |

All of them end in the platform's `SteamAchievements.IncrementStat` (the job end calls it directly for three stats and
through `PlatformManager.IncrementStat(id, amount)` for `stat_finish_allmissions`), which runs
`BaseAchievements.IncrementStat`: it adds to the stat, unlocks the achievements whose threshold is reached and writes Steam (`SteamUserStats.SetStat`,
`StoreStats`). Calling `PlatformManager.IncrementStat` on another client reproduces the stat and its achievements
exactly.

## What Changes

- **Who worked on a job.** The server keeps, per active job, the set of players who contributed: the player who took
  the order, every player whose part change, car detail change, granted car lock (row 18) or examine on the job's car
  the server accepted while the job was active, and the finisher. The job is found from the car's spawn record
  (`CarState.LoadedCars[L].Spawn.JobID` with `Spawn.IsJob`), not from the loader the job started on, because a job car
  can move to another loader. Kept in memory with the active job and in the `jobs` save section (so a server restart in
  the middle of a job keeps it; old saves load with the taker only). Contributors are stored by
  `PlayerRecords.ShortKey`, a non-secret id, so the `jobs` section never carries identity keys into a bug report.
- **The server derives the job's stats.** When it accepts a job end (`JobsService.OnJobEnd`), it already knows
  everything the game's stat calls depend on: `packet.IsCompleted`, the job's `BonusToExp`/`BonusToMoney` and
  `IsMission`. It derives `stat_finish_order` (completed), `stat_bonus_exp` and `stat_bonus_money` (completed with that
  bonus), and whether a mission was finished. No client report and no hook on the finisher.
- **The server awards the others.** It sends `JobStatsAward { JobId, Stats[], MissionFinished }` to every contributor
  who is connected, except the finisher, after the `JobRemoved` broadcast. Each receiver calls
  `PlatformManager.IncrementStat(id, 1)` once per job id (a second award for the same job is ignored); for
  `MissionFinished` it adds `stat_finish_allmissions` when `GlobalData.MissionsAmount <= GlobalData.MissionsFinished`
  (row 3 keeps the counters equal and `JobRemoved` carries them before the award).
- **No double count.** The finisher's own game already counted; the server never sends it an award. The receivers'
  job-end path calls no stat (only the finisher runs `EndJobCoroutine`).
- **A contributor who is offline** at the end gets nothing (no queue).
- Server config `job_stats_to` = `contributors` (default) | `garage` (every player in the garage at the end) |
  `finisher` (today's behaviour), for players who prefer another rule; also settable at runtime with the server command
  `jobs stats-to <rule>`.
- The difficulty is one server setting for the session, so every client applies `sandbox_<id>` the same way
  (`SteamAchievements.IncrementStat` writes it in difficulty mode 2).

Hooks: none in game code (the receivers call `PlatformManager.IncrementStat`). Packets: new `JobStatsAward`.

## Capabilities

### New Capabilities
- `job-stats-sharing`: Steam statistics and achievement progress of a finished job reach every connected player who
  worked on it, once, and never twice for the finisher.

### Modified Capabilities
- None in `openspec/specs/`. This replaces the not-yet-archived row 3 requirement "every player connected at that
  moment" (`sync-orders-and-jobs` spec, "Ending a job") with "every connected player who worked on it" by the user's
  decision of 2026-10-08; task 4.1 updates that spec text.

## Impact

- Core: `Network/Packets/JobPackets.cs` (`JobStatsAward`), `PacketTypes` (appended), `JobsState` active job gains
  `Contributors` (`[OptionalField]`, short keys).
- Server: `Data/Jobs/JobContributors.cs` (record on part, detail, lock and examine paths; job resolved from the car's
  spawn record), `JobsService.OnJobEnd` (derive the stats, send the awards), `ServerConfig` (`job_stats_to`), server
  commands `jobs contributors <jobId>` and `jobs stats-to <rule>`, `RedactionCheck` case for the `jobs` section.
- Client: `Logic/Jobs/JobStats.cs` (apply awards, per-job dedupe).
- Harness: `stats-trace on|off|report` (counts calls at `SteamAchievements.IncrementStat` for the four job ids only;
  `StatsGuard` keeps Steam untouched), scenario `job-stats`.
- Depends on (merged): row 3 (`JobsService`, mission counters in `JobRemoved`), row 7 (identity, `ShortKey`), row 18
  (lock grants), row 19 part 1 (part and detail merges name their source client).

## Open questions

Each has the default the draft works with.

1. **Who counts as having worked on the job.** **Default:** the taker, the finisher, and every player with an accepted
   part change, detail change, examine or car lock on that car during the job. Alternatives: everyone in the garage at
   the end; everyone connected (row 3's old spec).
2. **Tiny contributions.** Opening the hood or examining one part counts. **Default:** yes, any accepted change counts;
   no minimum.
3. **Other stats.** Stats that belong to one player's action (buying parts, unscrewing, balancing wheels, painting,
   visiting the junkyard, `stat_finish_testtrack`, `stat_timeattack`, …) stay with that player. **Default:** yes, only
   the job stats above are shared.
4. **Offline contributor.** **Default:** no award later.
5. **Upgrade achievements** (`stat_full_garage`, `stat_unlock_allupgrade` from `UpgradeSystem.CheckForAchievements`):
   upgrades are shared, but only the buyer's game runs that check today. **Default:** out of scope; a note in
   QUESTIONS.md for a later decision. **Answered 2026-10-10:** every player gets them; see the design amendment.
