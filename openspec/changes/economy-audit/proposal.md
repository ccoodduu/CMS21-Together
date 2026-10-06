# Proposal

## Why

Money, scrap, level and XP are one shared pool (user decision 2026-10-05). The server owns it in `WorldState`, but
most of the game's money paths never reach the server. The static spike (`docs/spikes/economy-paths.md`) found:

- Every gameplay change goes through three static mutators: `GlobalData.AddPlayerMoney(int)`,
  `AddPlayerScraps(int)` and `AddPlayerExp(int, bool)`. Nothing else writes the economy fields (apart from sandbox
  settings), so one prefix on each mutator sees every path.
- The mod hooks scrap and XP centrally (`StatsHooks`, a trusted delta) but money only per feature (shop buy,
  taken-items buy, item sales, garage upgrades, job payout, parking levels). 28 other `AddPlayerMoney` callers change
  money only locally. The next `WorldState` undoes them, so travel fees, paint, welder and the like are free in a
  session.
- Two of those paths are reachable on `main` today: the fluid-spill fine in `PartScript.<Hide>d__159` (−50) and
  `SkillsTab.ResetSkillsAction` (−1000 per point; it also resets the shared skills on one client only).
- Selling a car (`GameScript.SellCar` → `<SellCarCoroutine>d__135`) is blocked by the guard, because it deleted the
  car for everyone and paid only locally (the known row-6 gap).
- Upstream #94 ("money/level desync after crates"): the crate's money card goes through the unhooked
  `AddPlayerMoney`, so the money half is still there. XP and scrap are shared now.
- The barn count (`GlobalData.BarnsAmount`) is not in `WorldState`: a barn map from the shared inventory adds a barn
  on one client only, and a barn trip uses one up on one client only.
- Scrapping, scrap per condition, the quality upgrade and buying license plates are blocked by the guard ("row 10").

Rows 2, 3 and 6 made their own money paths server-authoritative. Rows 4, 5a, 5b and 13 left their costs to this row.
This row closes the rest and makes "every money path is attributed" checkable.

## What Changes

The design follows option C from `QUESTIONS.md` ("How row 10 syncs money", the default; design.md says what changes
for A or B):

- **One prefix per mutator** (`EconomyHooks`) on `GlobalData.AddPlayerMoney`, `AddPlayerScraps`, `SetPlayerScraps` and
  `AddPlayerExp`. Each call is attributed to the `EconomyScope` that a feature hook opened around the calling game
  method (for example `MapWindow.SubmitPanelAction` → `TravelFee`). A money or scrap call with no scope is dropped
  locally and counted (default deny). XP keeps today's trusted delta, now with a reason.
- **Fees** (the game charges after the fact; the server computes or range-checks the amount): travel fees, fluid
  spill and refill, car paint, wash before paint or tint, tint, welder, interior detailing, part repair, crate cards.
  The local call still runs (instant UI). The client sends `EconomyRequest`; the server applies the amount with
  vanilla's clamp (0 … 900,000,000) and broadcasts `WorldState`.
- **Trades** (the server applies money and effect together, and refuses when money, scrap or the item is missing):
  selling a car (garage or parking), skill reset, scrapping an item, scrap per condition, quality upgrade, license
  plates, using a barn map. The client asks first; the effect comes back from the server.
- **Barn count** becomes shared: `WorldState.Barns` (additive), changed only by barn maps and barn trips.
- **Folded in**: shop-sale prediction in `NotificationCenter.NewButtonAccept`, job payout capture (row 3),
  car-purchase capture (row 6) and parking levels (row 2) move onto the same scopes. The server computes the parking
  level price from the `cheaper_parking` upgrade. The server bounds car purchase prices. `ActivateDifficultyLevel` runs
  without sending stats.
- **Audit**: the server keeps an economy ledger (command `economy`); the client counts unattributed calls; a harness
  scenario drives every path and requires zero unattributed calls.
- **Guard**: the merge commit opens `Window CaseOpening`, `Scrap`, `ScrapPerCondition`, `ShopLicenseBuy` and
  `Action SellCar`. The drag strip (entry fee, bets, prizes) and the map's "measure power" stay blocked.
- Packets: **new** `EconomyRequest`, `EconomyResult` (appended to `PacketTypes`); **changed** `WorldState` + `Barns`.
  `StatsAction` stays for the harness (`stats-add`), but client hooks no longer send it.
- Game hooks (Harmony), scope openers: `MapWindow.SubmitPanelAction`, `NotificationCenter.ButtonAccept`,
  `NotificationCenter.NewButtonAccept`, `PartScript._Hide_d__159.MoveNext`, `FluidRefill.Hide`,
  `PaintshopManager.TryGetMoneyForPaint`, `PaintshopWindow._ShowCoroutine_d__10.MoveNext`,
  `TintingWindow.TintAction`, `TintingWindow._ShowCoroutine_d__18.MoveNext`, the welder and interior-detailing accept
  lambdas (`WelderLogic.__c__DisplayClass5_0`, `InteriorDetailingToolkitLogic.__c__DisplayClass6_0`),
  `RepairPartWindow.ProcessGameResult`, `CaseOpeningWindow.TakeLoot`. Trade entry points (prefix, skip original):
  `GameScript.SellCar(CarLoader, int)`, `SkillsTab.ResetSkillsAction`, `ScrapProduction.MakeScrap`,
  `ScrapPerConditionWindow` accept, `ScrapUpgrade.UpgradeItem`, `ShopLicenseBuyWindow.BuyItem`, the barn map's
  `NotificationCenter.<UseItem>g__ShowMapOpeningAskWindow` lambda. Plus `DifficultyManager.ActivateDifficultyLevel`
  (prefix/postfix, no stats sent).

## Capabilities

### New Capabilities
- `economy`: every change to the shared money, scrap, XP, level, skills and barn count is applied by the server once,
  with an amount the server computed or checked; local-only changes cannot happen; crates, car sales, scrapping,
  skill resets and fees work while connected.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `PacketTypes.cs` (2 values appended), new `Network/Packets/EconomyPackets.cs` (`EconomyReason`,
  `EconomyRefusal`), `WorldStatesPackets.cs` (`WorldState.Barns`).
- Server: new `Data/Economy/EconomyService.cs` (handler, ledger, apply with clamp), `EconomyRules.cs` (per-reason
  amounts and ranges), `ScrapFormulas.cs` (ported `GetScrapFromItem`, `GetCostForScrapUpgrade`);
  `CarPartsStore.ClearReason.Sold` (appended); `ParkingHandlers` (server-computed level price, purchase price bound);
  `GarageUpgradeHandler` (skill reset); `InventoryHandlers` (crate tracking); server command `economy`.
- Client: new `Logic/Economy/EconomyScope.cs`, `EconomyHooks.cs` (the four mutator prefixes), `FeeHooks.cs`,
  `TradeHooks.cs`, `CarSaleSync.cs`, `EconomyAudit.cs`; `StatsHooks` removed (merged into `EconomyHooks`);
  `JobHooks.BeforeAddMoney` and row 6's purchase prefix replaced by scopes; `WorldStatesPackets.HandleWorldState`
  (`Barns`, profile wrapper); `GuardRules`.
- Harness: `tools/TestHarness/Features/EconomyCommands.cs` (`econ-trace`, `econ-map-travel`, `econ-skill-reset`,
  `econ-sell-car`, `econ-scrap`, `econ-scrap-condition`, `econ-scrap-upgrade`, `econ-license`, `econ-crate`,
  `econ-barn-map`, `econ-fee`, `econ-unattributed`, `econ-ledger`), dump section `economy` and `stats.barns`;
  scenarios `economy-trace.ps1`, `economy-fees.ps1`, `economy-trades.ps1`, `economy-latejoin.ps1`.
- Depends on (merged): rows 7 (contract, `StateLock`, `world` section), 1 (`ClearLoader`, `CarClaims`, delete
  broadcast), 2 (`ParkingService.TryRemove`, `ParkingHandlers`), 3 (`JobEndContext`), 4 (tint hooks), 5a (repair
  table, `ItemActionType.Update`), 5b (paint, welder, interior detailing), 6 part 2 (barn/auction/salon scenes,
  purchase capture), 13 (`CarAwayRegistry`), 14 (world digest, `resync`), 14a (guard). The roadmap lands row 10 after
  all of them (M4: 5a → 5b → 6 part 2 → 10). If one is not merged yet, its fee hook ships anyway (its window stays
  guarded) and its scenario step is skipped with a note in `STATUS.md`.
