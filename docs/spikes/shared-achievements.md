# Spike: achievements from shared save state

User decision 2026-10-10 (QUESTIONS.md, row 30 question 7): every achievement and Steam stat that follows from shared
save state is earned by every player, not only by the one who triggered it. This note lists every stat the game sets,
sorts them, and describes how the other players' games get the state ones.

## Where the game counts

Every stat ends in `SteamAchievements.IncrementStat(id, amount)` (the virtual at vtable +0x1D8, also reached through
`PlatformManager.IncrementStat`). It runs `BaseAchievements.IncrementStat` (in-memory `Statistic.Value`, or
`ValueSandbox` in difficulty mode 2; unlocks the achievements whose `ProgressMax` is reached), then
`SteamUserStats.SetStat` and `StoreStats`. `GetStatValue` reads `Value`; `ClearStat` sets `Value` to 0 in memory only.
Achievements are never unlocked directly: `dxref.py` finds no `achiv_` literal in code, and
`PlatformManager.UnlockAchievement` has no caller. Achievements come from the stat thresholds in the game's
`AchievementsFile` (INI in `resources.assets`, `RelatedStatID`/`ProgressMax`).

Callers, from `dxref.py strings.tsv '^(stat_|achiv_|sandbox_)'` and `xref.py` (decompiles in
`CMS21-TestInstalls\native\out\clean`, `orders_clean`, `ach_decomp`):

## Classification

(a) shared state: every player, by re-running the game's own check against the shared state. (b) personal action:
stays with the player who did it (job stats: row 30 contributors). (c) unclear: listed with the default used.

| Stat | Achievements (`ProgressMax`) | Set by | Class | How |
|---|---|---|---|---|
| `stat_full_garage` | 1 | `UpgradeSystem.CheckForAchievements(Money)`, from `UnlockUpgrade` and `LoadSave` | a | every client re-runs the check after a garage state that unlocks a garage upgrade, and on the first garage state of a session |
| `stat_unlock_allupgrade` | 1 | `UpgradeSystem.CheckForAchievements(Points)`, same callers | a | same, for skills |
| `stat_unlock_parking` | 1 | `ParkingManagementWindow.UnlockParkingLevelAction` when `UnlockedParkingLevels` becomes 10 | a | every client, when a parking state brings the shared levels to 10 or more, or on the first one of a session |
| `stat_level` | 5, 20, 50 | `GlobalData.AddPlayerExp` raises it to `RealPlayerLevel` | a | already shared: every world state raises it on every client; now never lowered (below) |
| `stat_finish_allmissions` | 1 | `EndJobCoroutine` when `MissionsAmount <= MissionsFinished` | a | every client, when the shared mission counters reach `MissionsAmount` (or on joining such a save); was a row 30 contributor award |
| `stat_finish_order` | 1, 100 | `EndJobCoroutine` | b | row 30: the job's contributors |
| `stat_bonus_exp`, `stat_bonus_money` | 1 | `EndJobCoroutine` | b | row 30: the job's contributors |
| `stat_unscrew` | 10000 | `PartScript.Update` | b | |
| `stat_shed_oil` | 1 | `PartScript.CheckMessageOnHide` | b | |
| `stat_fix_parts`, `stat_fix_body` | 50, 150 | `RepairPartWindow.ProcessGameResult` | b | |
| `stat_paint_car` | 5, 60 | the three paintshop `PaintCar` coroutines | b | |
| `stat_swap` | 1 | `NotificationCenter.<ActionInsertEngineToCar>` | b | |
| `stat_wheels_balanced` | 5, 50 | `WheelBalancerLogic.<Clear>` | b | |
| `stat_visit_junkyard`/`_barn`/`_salon` | 1 (barn also 30) | the scene generators | b | the player who travels |
| `stat_finish_testtrack`, `stat_finish_testpath` | 1 (path also 25) | `TestTrackManager.DoneTest`, `PathTestManager.<ExitFromCar>` | b | the driver |
| `stat_timeattack` | 1 | `RaceTrackManager.LastTime` | b | the driver |
| `stat_dragrace_all`/`_win`/`_moneywon` | none in the file | drag strip, `DragResultsWindow`, `DragChampionshipLadderWindow` | b | |
| `stat_buy_carsalon`/`_carjunkyard`/`_barn` | 1 (junkyard, barn also 30) | `CarSummaryTab.<BuyCar>g__BuyCarAction` | c | **default: the buyer.** The car and the money are shared, but this counts a purchase event; no other client can tell a purchase from its state, so sharing needs a server award (protocol change) |
| `stat_sell_car` (amount), `stat_sell_fix_car` | 5000, 50000; 1, 50 | `CarSummaryTab.<SellCar>g__SellCarAction` | c | **default: the seller**, same reason |
| `stat_win_car` | 5, 25 | `AuctionBidding.ReceiveCarAction` | c | **default: the bidder**, same reason |
| `stat_scrap` (amount, also negative) | 500, 2000 | `GlobalData.AddPlayerScraps` | c | **default: the player whose action changes the scrap** (the shared scrap total moves for many reasons; a delta on other clients would also count resyncs) |
| `stat_buy_parts` | 100, 3000 | `ShopBuyWindow.BuyItem` | c | **default: the buyer** |
| `stat_sell_junk` | 1000 | `Inventory.SellPerCondition`, `NotificationCenter.NewButtonAccept` | c | **default: the seller** |

Not a stat: money, cars owned, barn maps (`BarnsAmount`) and garage looks have no stat or achievement, so there is
nothing to share for them. `sandbox_<id>` is the same stat in difficulty mode 2.

## Design

Each client re-runs the game's own check when the shared state it depends on changes, so there is no new packet and
no double count: the checks are the game's (or mirror the game's trigger), and each is guarded by the stat value.

- **Upgrades and skills** (`GarageUpgrades.SyncUpgrades`): after the garage state is written into the game's
  `UpgradeSystem`, call `UpgradeSystem.CheckForAchievements(Money)` when a garage upgrade went from locked to unlocked
  in this state, and `(Points)` when a skill did; both on the first garage state of a session (the game's `LoadSave`
  does the same at load). The game's check returns at once when `GetStatValue` is not 0, so a repeat adds nothing.
  The buyer's game is no exception: the mod sends the purchase to the server and skips the game's `UnlockUpgrade`, so
  today nobody, not even the buyer, gets these two.
- **Parking** (`ParkingSync.OnState`): when the shared levels reach 10 or more (from below, or on the first parking
  state of a session) and `GetStatValue("stat_unlock_parking") == 0`, `IncrementStat("stat_unlock_parking", 1)`.
  Today the mod skips the game's `UnlockParkingLevelAction` too, so nobody gets it.
- **Missions** (`JobsSync`, where the mirror's counters are written to `GlobalData`): when `MissionsAmount > 0` and
  `MissionsFinished >= MissionsAmount`, the stat is 0 and no own job end is in flight (`JobEndContext.IsActive`; the
  finisher's `EndJobCoroutine` counts it itself), `IncrementStat("stat_finish_allmissions", 1)`, once per session.
  `JobStats.OnAward` no longer adds it for `MissionFinished` (the field stays in the packet).
- **Level** (`WorldStatesPackets`): the world state handler raised `stat_level` by `RealPlayerLevel - GetStatValue`,
  also when that was negative (a session below the player's own level lowered the Steam stat). Now it only raises, as
  the game's `AddPlayerExp` does, and remembers the level it raised to, so difficulty mode 2 (where `Value` does not
  move) does not add the difference on every world state.
- Each "once per session" memo is cleared with `ClientData.Reset`.
- `StatsGuard` is untouched: in test games all of the above runs to `IncrementStat`, whose Steam writes stay blocked.
  The harness `stats-trace` counts every stat id; `stats-trace zero <id>...` clears in-memory values (refused unless
  `StatsGuard` blocks `SetStat`, `StoreStats` and `SetAchievement`), because the user's own account may already have
  the stat and the game would skip the check.

Server-side awards are not used: every (a) stat has a check the client can run from state it already has. The (c)
stats would need one (for example a `StatAward { Stats }` the server sends after an accepted car purchase, sale or
auction win); left for a decision.
