# Spike: every money, scrap, XP and level path (static part of row 10 `economy-audit`)

Static decompile only (setup in `native-decompile.md`), game not launched. Date: 2026-10-06.
Roadmap row 10: make every money/scrap/XP path server-authoritative. Job payout (row 3) and the outdoor purchases
(barn, junkyard, salon, auction, row 6 part 2) are researched by other spikes; they appear below only as rows of
the inventory.

Outputs: `%USERPROFILE%\CMS21-TestInstalls\native\out\economy_clean` and `economy2_clean` (targets
`work\targets\economy.txt`, `economy2.txt`), plus the older `out\clean`. The field-write scanner is
`out\economy_tools\swrite.py` (run with `uv run --no-project --with capstone --with numpy python swrite.py`); its
result is `out\economy_tools\writes.tsv`. Callers come from `xref.py` (E8 `call` and E9 `jmp` rel32). VAs are
`0x180000000 + RVA`. "Hookable" means the native function entry is reached by a call or a tail `jmp`, so a Harmony
detour on it fires.

## Result (read first)

1. **Nothing writes money, XP or level outside `GlobalData`'s own methods.** A capstone scan of all 341 methods that
   load `GlobalData_TypeInfo` tracks `static_fields` pointers and lists every write to the 13 economy statics
   (`PlayerMoney` 0x2A0 … `LevelCap` 0x2D0). A looser scan (any write with those displacements in the same methods) finds
   nothing more. Outside `AddPlayerMoney`/`SetPlayerMoney`/`AddPlayerScraps`/`SetPlayerScraps`/`AddPlayerExp`/`Load`/`.cctor`
   only two methods write a real economy field, both in `DifficultySettings` (scraps = 900, see §2). So **one prefix on
   each of `AddPlayerMoney`, `AddPlayerScraps` and `AddPlayerExp` sees every gameplay change.** Several callers reach them
   through a tail `jmp` (Ghidra then shows the callee body merged into the caller, e.g. `Inventory.SellPerCondition`,
   `DifficultySettings.MaxXP`). The detour still fires.
2. **The mod hooks scraps and XP centrally, but not money.** `StatsHooks` prefixes `AddPlayerExp`, `AddPlayerScraps`
   and `SetPlayerScraps`, sends a delta and lets the local call run. Money is handled only per feature: shop buy,
   taken-items buy (junkyard and barn parts), single and per-condition sell, garage upgrades. Every other
   `AddPlayerMoney` caller (28 methods, plus 4 dev or profile-load paths) changes money only locally, and the next `WorldState` overwrites it.
3. **Two of those paths can already be reached in a session** (their window or mode is allowed by `GuardRules`):
   - `PartScript.<Hide>` fluid-spill fine (−50), in the allowed unmount modes;
   - `SkillsTab.ResetSkillsAction` (−1000 per point, and it resets the skills locally), in the allowed `Upgrades` window.
   Selling a car from `CarInfo` (allowed, "row 1") is the known row-6 gap. All other unhandled paths sit behind
   guarded windows or scenes today.
4. **XP drifts on Expert.** Native `AddPlayerExp` doubles `exp` when `DifficultyManager.currentDifficulty == 1`
   (Expert). The client prefix sends the undoubled argument, and the server's `StatsHandlers` does not double it. The
   client shows the doubled value until the server's `WorldState` arrives, and then XP and level fall back.
5. **The sell prices on the server look 2× too high (likely).** Native single sell pays `Helper.GetPrice(item, 0.5)`
   (group: `GetPrice(group) * 0.5`), and `SellPerCondition` sums `GetPrice(item, 0.5)`. The server's `ShopHandlers`
   pays `PricingCalculator.GetPrice(item)` with the default `mod = 1`. Also, `NotificationCenter.NewButtonAccept`
   still calls `AddPlayerMoney(price)` locally after the hooked `SellItem` returns, so the local money briefly
   double-counts until the next `WorldState`. (Outside row 10's list, but an economy bug; verify in game.)

## 1. The mutators

All are static methods of `GlobalData`. Fields are `public static int` (dump.cs offsets in `static_fields`).

| Method (VA) | What it does | Mod today |
|-------------|--------------|-----------|
| `AddPlayerMoney(int)` `0x180D7B580` | No-op when `money == 0` or `GameSettings.UnlimitedMoney`. Sets `AddMoneyAmount`, `PrevPlayerMoney`, `PlayerMoney += money`, clamps 0…900,000,000, logs, `RefreshStatsUICoroutine`, plays `AddMoney`/`SubMoney` (`…Big` above 10,000) | **Not hooked** |
| `SetPlayerMoney(int)` `0x180D7B850` | `PlayerMoney = money` and refreshes the stats UI. **Ignores 0** (cannot set money to 0). Only callers: `DifficultySettings` | Not hooked |
| `AddPlayerScraps(int)` `0x180D7B930` | No-op when `GameSettings.UnlimitedScraps`. `PrevPlayerScraps`, `PlayerScraps += amount` (**no clamp**), `AddPlayerScrapsAmount`, `IncrementStat("stat_scrap", amount)` (also for negative amounts), UI refresh | Prefix sends `ScrapsDelta`, local call runs |
| `SetPlayerScraps(int)` `0x180D7BBA0` | `PlayerScraps = scraps`, ignores 0. **No callers in the game** | Prefix sends the difference |
| `AddPlayerExp(int exp, bool instant)` `0x180D7BCC0` | `exp *= 2` on Expert. `AddExpAmount += instant ? 0 : exp`, `PrevPlayerExp/PrevPlayerLevel`, `CurrentAddExp`. Loop: `GetDiffToNextLvl(PlayerLevel)` (`0x180D7C390`); on reaching it, `UpgradeSystem.AddPoints()`, `PlayerLevel++`, `PlayerExp = 0`. Raises `stat_level` to `RealPlayerLevel` (`PlayerLevel + 1`). `LevelCap` (55) is **not** applied here | Prefix sends `ExpDelta` when `exp > 0`, local call runs |
| `Load()` `0x180D7CB90` | Copies `ProfileData.globalDataWrapper` (`NewGlobalDataWrapper`: money 0x10, level 0x14, exp 0x18, scraps 0x1C, barns, parking levels, missions) into the statics. Called by every scene loader: `GarageLoader.<Load>`, `JunkyardGenerator.<Generate>`, `AuctionManager.<Generate>`, `SalonManager.<Generate>`, `ParkingManager.<PrepareScene>`, `PhotoLocationLoader.<Generate>`, `Dragstrip.<Start>`, `GameDataManager.LoadProfile` | — |
| `Save()` `0x180D7C990` | Statics → wrapper. Called by `GarageLoader.Save`, `GameScript.BuyCar`, `NotificationCenter.<SelectSceneToLoad>` | — |

Skill points are not a `GlobalData` field. `UpgradeSystem.availablePoints` (instance 0x30) is written by
`AddPoints` (`0x180B37200`, only from `AddPlayerExp`), `LoadSave`, `ClearData`, `UnlockAll` (= 0) and
`GetAvailablePoints` (`0x180B37000`), which subtracts the cost of every unlocked point upgrade each time it is called.
The server computes `AvailablePoints` itself (`GarageUpgradeHandler.ComputeAvailablePoints`).

## 2. Direct field writes outside the mutators (complete list)

| Writer (VA) | Write site | Field | When |
|-------------|------------|-------|------|
| `DifficultySettings.UpdateSettings` `0x18087C010` | `0x18087C6A6` | `PlayerScraps = 900` | `unlimitedMoney` set and scraps < 900 (sandbox) |
| `DifficultySettings.UnlimitedScraps` `0x18087CF10` | `0x18087D037` | `PlayerScraps = 900` | same rule |
| `StatsContainer.<UpdateExp>d__26.MoveNext` `0x180B72430` | `0x180B72B0D` | `AddExpAmount` | UI animation counter only |
| `GlobalData..cctor` `0x180D80AE0` | — | all, `LevelCap = 55` | static init |

The mod writes the four statics directly in `WorldStatesPackets.HandleWorldState`. That is the right low level (no
sound, stat or level-up side effects), but it copies only money, level and exp into `globalDataWrapper`, **not
`PlayerScraps`**. Because `NotificationCenter.<SelectSceneToLoad>` calls `GlobalData.Save()` before a scene change,
this only matters when `Load()` runs without a `Save()` before it (for example `GameDataManager.LoadProfile`). Low
impact, to verify.

## 3. Inventory of callers

"Guard" is the current `GuardRules` state of the window, scene or mode that reaches the path ("blocked" = `Planned`,
so blocked while a session runs). "Hookable" is always yes for the callee: every site is a direct call or a tail jump.

### Money (`AddPlayerMoney` callers, 37 methods)

| Feature | Caller (VA) | Amount | Mod today | Guard |
|---------|-------------|--------|-----------|-------|
| Shop buy | `ShopBuyWindow.BuyItem` `0x180A44FB0` | `-(int)currentPrice` | **Handled** (prefix → `ShopAction.Buy`, skips original) | allowed |
| Junkyard/barn parts | `TakenItemsWindow.BuyPartsAction` `0x180ADB190` | `-priceAfterDiscount` | **Handled** (`ItemsExchange`) | allowed |
| Sell one item | `NotificationCenter.NewButtonAccept` `0x1809DC9D0` ("SellItem" branch) | `GetPrice(item, 0.5)` / `(int)(GetPrice(group) * 0.5)` | Partly: `SellItem` hooked and skipped, but this `AddPlayerMoney` still runs locally; server pays mod 1.0 (see Result 5) | allowed |
| Sell per condition | `Inventory.SellPerCondition` `0x180C7A000` (tail jmp) | Σ `GetPrice(item, 0.5)` (lambda `<SellPerCondition>b__2` `0x180EAE8F0`) | **Handled** at `SellPerConditionWindow.AcceptAction`; server pays mod 1.0 | allowed |
| Garage (money) upgrade | `GarageAndToolsTab.UnlockCurrentSelectedSkillAction` `0x180E9E540` | `-UpgradeSystem.GetUpgradeCost(id, lvl, Money)` | **Handled** (`UpgradeRequest`) | allowed |
| Skill reset | `SkillsTab.ResetSkillsAction` `0x180DC7950` | `-pointsToReset * 1000` | **Not handled**; also resets skills locally | **allowed** |
| Fluid spill on unmount | `PartScript.<Hide>d__159.MoveNext` `0x180D2A530` | `-_cost` (50 with `GUI_FluidWarning`) | **Not handled** | **allowed** (unmount modes) |
| Fluid refill | `FluidRefill.Hide` `0x1812887E0` (virtual, no direct caller) | `-(int)((newLevel - startFluidAmount) * 20)` | Not handled | unclear which mode opens it (likely row 4 `DrainTool`); to check |
| Job payout | `GameScript.<EndJobCoroutine>d__139.MoveNext` `0x1808948D0` | `+job.TotalPayout` (also XP, case item) | Not handled | Orders blocked (row 3, other spike) |
| Sell car | `GameScript.<SellCarCoroutine>d__135.MoveNext` `0x1808970D0` | `+sellPrice` | Not handled (known row-6 gap) | `CarInfo` allowed |
| Buy car (salon, barn, junkyard) | `GameScript.BuyCar` `0x180E95560` ← `CarSummaryTab.<BuyCar>g__BuyCarAction` | `-buyPrice`, then `GlobalData.Save()` | Not handled | scenes blocked (row 6 part 2) |
| Auction win | `AuctionBidding.ReceiveCarAction` `0x180CED590` | `-currentBid`; bidding itself (`PlayerBid`, `BidAction`) charges nothing | Not handled | blocked (row 6 part 2) |
| Travel fee | `MapWindow.SubmitPanelAction` `0x180D0D650` (3 sites) | Auction −200, Junkyard −500, Barn −100 when `TravelHaveCost` and not Sandbox | Not handled | `Map` blocked (row 1 M2) |
| Travel fee, legacy | `NotificationCenter.ButtonAccept` `0x1809DBA20` ("ChangeScene") | `-hash["Price"]` for scene types 1, 2, 5, 8 | Not handled | via map |
| Dyno | `MapWindow.MeasurePowerForSelectedCarLoader` `0x180D0DD60` | −500 | Not handled | blocked (row 13) |
| Parking level | `ParkingManagementWindow.UnlockParkingLevelAction` `0x180A00FF0` | `-parkingLevelPrice` (`index * 50000`, ×0.5 with `cheaper_parking`) | Not handled | blocked (row 2) |
| Paint car | `PaintshopManager.TryGetMoneyForPaint` `0x1809F5040` ← the three `<PaintCar>` coroutines | −1000 (type 0) or −100 (type 1), only if money ≥ cost | Not handled | blocked (row 5b) |
| Wash before paint | `PaintshopWindow.<ShowCoroutine>d__10` `0x180A7C820` | −100 with `SetWashFactor(0, 1)` | Not handled | blocked (row 5b) |
| Wash before tint | `TintingWindow.<ShowCoroutine>d__18` `0x180B7FF90` | −100 | Not handled | blocked (row 4) |
| Tinting | `TintingWindow.TintAction` `0x1810C9530`, `TryGetMoneyForTint` `0x1810C8F00` | `-50 * windows` | Not handled | blocked (row 4) |
| Welder | `WelderLogic.<>c__DisplayClass5_0.<Use>g__UseWelderAction` `0x1818CD1D0` | `-CarLoader.GetWelderCost()` = `round((uniqueMod-1)*1000 + 500)` | Not handled | `equipment_use` blocked (row 5b) |
| Interior detailing | `InteriorDetailingToolkitLogic.<>c__DisplayClass6_0.<Use>g__…Action` `0x180EAB520` | `-GetInteriorDetailingCost()` = `round((uniqueMod-1)*150 + 100)`, free with the car-wash upgrade | Not handled | blocked (row 5b) |
| Part repair | `RepairPartWindow.ProcessGameResult` `0x181B00FC0` (3), `RepairItem` `0x181B01370`, `BreakItem` `0x181B01480`, `JustTakeMyMoney` `0x181B01580` | `-RepairCost` | Not handled | blocked (row 5a) |
| License buy | `ShopLicenseBuyWindow.BuyItem` `0x180A4AE00` | `-(int)currentPrice` | Not handled | blocked (row 10) |
| Crate money card | `CaseOpeningWindow.TakeLoot` `0x180AF5DB0` | `+itemValue` | Not handled | blocked (row 10) |
| Drag entry fee | `DragWindow.<>c__DisplayClass29_0.<StartChampionship>g__Start` `0x180781630` | `-entryFee` | Not handled | blocked (backlog) |
| Drag bets | `DragChampionshipLadderWindow.StartRaceAction` `0x180E2E830` | `-GetAllBetsInCurrentStage()` | Not handled | blocked (backlog) |
| Drag prize | `…<FinishSimulation>d__45` `0x1807801E0`, `…<CloseAction>g__Close` `0x18077FFB0` | `+playerPrize (+bets)` / `+prize`, not in sandbox | Not handled | blocked (backlog) |
| Promo/stage profiles | `GarageLoader.<Load>d__14` `0x180899ED0` (4) | +500,000/4,000/50,000/150,000 (with XP) when profile is `cms2021promo`/`stage1-3` and exp is 0 | n/a | n/a |
| Test mode | `GarageLoader.<LoadTestMode>d__15` `0x18089BEF0` | dev | n/a | n/a |
| Debug | `TestScript.AddMoney`/`RemoveMoney` | dev | n/a | n/a |

`SetPlayerMoney` callers: `DifficultySettings.UpdateSettings` (900,000,000 for `unlimitedMoney` when exp is 0; 10,000
for `moreMoney` unless the profile is Easy), `UnlimitedMoney`, `MoreMoney`. These run when `HandleWorldState`
calls `ActivateDifficultyLevel()`, **before** it writes the server values, so the server values win.

### Scrap (`AddPlayerScraps` callers)

| Feature | Caller (VA) | Amount | Mod today | Guard |
|---------|-------------|--------|-----------|-------|
| Scrap a part | `ScrapProduction.MakeScrap` `0x180BC60C0` ← `ProcessGameResult` | `+amount` (`GetScrapFromItem`); item removed via `Inventory.Delete` (hooked) | Delta sent | `Scrap` blocked (row 10) |
| Scrap per condition | `Inventory.ScrapPerCondition` `0x180C7A830` (tail jmp) | Σ `GetScrapFromItem(item, 0)` | Delta sent; items are removed by a LINQ filter, probably **not** via `Inventory.Delete` (uncertain) | blocked (row 10) |
| Quality upgrade | `ScrapUpgrade.UpgradeItem` `0x180BCA9C0`, `UpgradeAction`, `UpgradeFromOutside` | `-upgradeCost`; sets `Item.Quality` locally | Delta sent; the quality change is not synced | blocked (row 10) |
| Crate scrap card | `CaseOpeningWindow.TakeLoot` | `+itemValue` | Delta sent | blocked (row 10) |
| Sandbox | `DifficultySettings` (direct write, §2) | = 900 | Bypasses the hook (harmless: `WorldState` follows) | — |
| Debug | `TestScript.AddScraps` | dev | — | — |

### XP and level (`AddPlayerExp` callers)

| Feature | Caller (VA) | Amount | Mod today |
|---------|-------------|--------|-----------|
| Unmount / mount a part | `PartScript.<Hide>d__159` `0x180D2A530`, `<ShowMounted>d__155` `0x180D2C8A0` | +1 each, not for special group 1 | Delta sent |
| Examine | `InteractiveObject.ExamineRandomPart` `0x180C71850` ← `GameScript.ClickIO` | +1 per newly examined part | Delta sent |
| Diagnostic tools | `CompoundMeter` `0x180774A60`, `CompressionTester` `0x180775450`, `Multimeter` `0x180A69060`, `ObdScanner` `0x180A6F860`, `TireTreadDepthTester` `0x180B82240` (`<UseAnim>`) | +1 per unexamined part | Delta sent (tools mode blocked, row 13) |
| Job end | `EndJobCoroutine` | `+job.XP` if completed | Orders blocked (row 3) |
| Crate XP card | `CaseOpeningWindow.TakeLoot` | `+itemValue` | Delta sent (window blocked) |
| Max XP setting | `DifficultySettings.MaxXP` `0x18087CB30`, `UpdateSettings` | 67,000 (instant) when exp is 0 | Sent, but the server drops it (`ExpDelta < 10000`) |
| Promo/stage profiles | `GarageLoader.<Load>d__14` | 50,000/2,000/8,000/13,000 (instant) | n/a |
| Debug | `TestScript.AddExp` | dev | — |

Level changes **only** inside `AddPlayerExp` (`PlayerLevel++` at `0x180D7C06D`), `Load` and the `.cctor`. No other path
sets the level, so syncing XP through `AddPlayerExp` covers the level too.

## 4. Upstream #94 (money/level desync after opening crates)

Issue text: "opened a few crates … after getting xp and credits multiple times our money and lvl was desynced". The
upstream owner answered that XP and level were per player in that version, and money was a real desync.

Native crate flow:

- `CaseOpeningWindow.SetItem` (`0x180AF5A60`) stores `originalItem` and removes the case through a direct call or jump into
  `Inventory.Delete` (hooked). `Hide` (`0x180AF4C40`) adds `originalItem` back with `Inventory.Add` in some close paths (condition not read; likely closing before
  opening).
- `UIHelper.SetCaseOpeningCardValue` (`0x1808593C0`) rolls the cards with `UnityEngine.Random`, using `RealPlayerLevel`
  (`L`):
  - money card: `Range((L + n) * 15, (L + n) * 45)`, where `n = round(L * 0.4)` (the rounding helper is unnamed;
    round or floor);
  - XP card: `Range(lo, round(GetCurrentExpToNextLevel(L) * 0.35))`, where the lower bound was lost in the XMM
    decompile;
  - scrap card: `Range(L * 5, L * 15)`;
  - special case/map: an item.
- `TakeLoot` pays the picked card. The rolls are client-local, so a server cannot check them; it can only check the
  range.

Against this fork:

- the money card goes through the unhooked `AddPlayerMoney`. It is lost at the next `WorldState`, which the server
  sends right after any XP or scrap delta. This is the money half of #94, and it is still present;
- XP and scrap go to the server as deltas, so level and scraps are shared now. The XP card stays far below the
  server's 10,000 limit (about 1,050 at the level cap of 55, from the server's own curve). On Expert the client
  still shows doubled XP until the server snaps it back (Result 4);
- `CaseOpening` is blocked by the guard (row 10), so none of this happens in a session today.

## 5. Not handled yet, in order of exposure

1. **Reachable now:** the fluid-spill fine in `PartScript.<Hide>` and `SkillsTab.ResetSkillsAction` (money and local
   skill reset). Car sale from `CarInfo` (the known gap).
2. **Bugs on handled paths:** Expert XP doubling missing on the server; server sell price at mod 1.0 instead of 0.5;
   the local `AddPlayerMoney` still runs in `NewButtonAccept` after the hooked `SellItem`; `globalDataWrapper.PlayerScraps`
   is not refreshed.
3. **Behind guards, for their rows:** travel, legacy travel and dyno (rows 1 M2 and 13), parking levels (row 2), job
   payout (row 3), tint and its wash (row 4), repair (row 5a), paint and its wash, welder, interior detailing (row 5b),
   car buy and auction win (row 6 part 2), crates, scrap production, scrap per condition, quality upgrade, license
   buy (row 10), drag strip (backlog), fluid refill (to check).

## 6. Options for row 10 (for discussion, not decided)

- **A. Central money hook.** Prefix `AddPlayerMoney` like the scrap and XP hooks, and send a `MoneyDelta`. It is one
  hook for all 28 paths, and the guard keeps blocking the paths whose feature is not synced. But the server must
  trust a client delta: it has no context to check it, and the existing per-feature handlers (shop, sell, upgrades)
  would count twice unless their local call is skipped or marked.
- **B. Per-feature requests.** Each feature sends "I did X" and the server computes the price. This matches shop,
  sell and upgrades today, and the server checks every price. It is more work, and the server needs formulas such as
  `GetWelderCost` (uses `uniqueMod`), the sell price and the repair cost.
- **C. Mixed.** B for prices the server can compute from state it has (fixed fees: travel −200/−500/−100, dyno −500,
  paint −1000/−100, wash −100, tint −50/window, spill −50, skill reset −1000/point, parking `index*50000`). A with a
  reason code and a range check for client-rolled values (crate cards, drag prizes, job payout until row 3 ports
  `CheckJob`).

Whichever option is picked, a deny path is needed: a prefix that cancels `AddPlayerMoney` must also cancel the effect it
pays for. For example, `TryGetMoneyForPaint` returns `false` when money is short, but `RepairItem` charges after the
repair, with no check of its own.

## Open points / uncertain

- `FluidRefill.Hide` units (`level * 20`) and which mode opens it in a session.
- `Inventory.ScrapPerCondition`/`SellPerCondition` remove the items with LINQ (`Where(b__3)`), probably not via
  `Inventory.Delete`; this matters only if those paths run unhooked.
- The crate XP card's lower bound and the rounding helper `func_0x180322c10` (read with `DumpAsm.java`/`f32.py`).
- The server level curve (`StatsHandlers.GetCapToNextLvl`) was not compared with native `GetCapToNextLvl`
  (`0x180D7C360`) here.
- Result 5 (sell price) and Result 4 (Expert XP) are read from code; confirm both in game.
