# Design

## Context

See proposal.md for the why and specs/economy/spec.md for the required behaviour. Facts that shape the approach,
from `docs/spikes/economy-paths.md` (static decompile, 2026-10-06; VAs and the full caller tables are there),
`docs/spikes/workshop-car-tools.md`, `docs/spikes/workshop-machines.md`, the native dump and the code on `main`:

- **Three mutators see everything.** `GlobalData.AddPlayerMoney(int)` (no-op for 0 or `GameSettings.UnlimitedMoney`;
  clamps 0 … 900,000,000; sound and UI), `AddPlayerScraps(int)` (no clamp; `IncrementStat("stat_scrap")`),
  `AddPlayerExp(int, bool)` (doubles on Expert; levels up; `UpgradeSystem.AddPoints`). Outside them only
  `DifficultySettings` writes an economy field (sandbox scraps = 900), and `SetPlayerMoney`/`SetPlayerScraps` have no
  gameplay callers. Several callers reach a mutator through a tail `jmp`; the detour still fires.
- **No managed stack.** Under IL2CPP a prefix on a mutator cannot see its caller. Attribution needs a hook on the
  calling method. Some callers are compiler-generated (coroutine `MoveNext`, display-class lambdas). Their interop
  names can differ from the mangled names (the welder accept is `WelderLogic.__c__DisplayClass5_0.
  Method_Internal_Void_Boolean_PDM_0` in the interop assembly). Some small callees are inlined
  (`RepairPartWindow.RepairItem`/`BreakItem`/`JustTakeMyMoney` are inside `ProcessGameResult`;
  `TintingWindow.TryGetMoneyForTint` is dead, `TintAction` has its own copy).
- **Today on `main`:**
  - `StatsHooks` prefixes `AddPlayerExp` (sends `StatsAction.ExpDelta` when > 0, unless `JobEndContext` captures
    it), `AddPlayerScraps` and `SetPlayerScraps` (send `ScrapsDelta`); the local call runs.
  - `StatsHandlers.HandleStatsAction` adds scraps (floor 0), applies XP below 10,000 (doubled on Expert since
    `033aec2`) and broadcasts `WorldState`.
  - Money is handled per feature, all on the server: `ShopBuyWindowHook` (skip original, `ShopAction.Buy`),
    `TakenItemsWindowHook` (skip, `ItemsExchange`), `NotificationCenterItemsHook.SellItem` (skip, `SellSingle`; the
    caller `NotificationCenter.NewButtonAccept` still runs its own local `AddPlayerMoney(price)`),
    `SellPerConditionWindowHook` (skip, `SellCondition`), `GarageUpgrades` (skip, `UpgradeRequest`), `JobHooks`
    (`EndJob` prefix + `AddPlayerMoney` prefix capture → `JobEndRequest`), `ParkingSync` (level unlock → skip,
    `ParkingLevelUnlockRequest`; the server accepts the full price or half of it).
  - `WorldStatesPackets.HandleWorldState` writes the four statics directly (no side effects) and the profile wrapper
    (money, level, exp, scraps). `ActivateDifficultyLevel()` runs before that when `updateGamemode` is set; it can
    call `SetPlayerMoney` and `DifficultySettings.MaxXP` → `AddPlayerExp(67000, true)`, which `StatsHooks` sends and
    the server drops (≥ 10,000).
  - Row 14's digest compares `world` (money, scraps, level, exp) and resends it on a confirmed mismatch, so a
    local-only change is undone within about 10 s even without a new `WorldState`.
  - The guard blocks `Action SellCar`, the barn/auction/salon scenes ("row 6 part 2"), `Window CaseOpening`, `Scrap`,
    `ScrapPerCondition`, `ShopLicenseBuy` ("row 10"). It allows `Window Upgrades` (skill reset reachable), the
    unmount modes (spill fine reachable), `Window Map` and `Scene Junkyard` (junkyard travel fee reachable).
- **Paths and amounts** (spike §3), grouped by who can compute the amount:
  - Fixed, known to the server: travel (Auction −200, Junkyard −500, Barn −100, only when the server rule
    `travel_fees` is on and the difficulty is not Sandbox); fluid spill −50; paint
    −1000 (`paintshopType` 0) / −100 (1); wash before paint or tint −100; tint −50 per window; skill reset −1000 per
    reset point; parking level `UnlockedLevels × 50,000`, halved with `cheaper_parking`.
  - Computable from data the server has: scrap from an item (`GlobalData.GetScrapFromItem`: `min(Condition, Dent) ×
    ItemProperty price × (Quality + 1)` scaled by the minigame grade 0/1/2); quality upgrade cost
    (`GetCostForScrapUpgrade(targetQuality, itemValue)`); repair cost bounded by the item's price.
  - Client-computed, the server can only bound them: welder (`round((uniqueMod − 1) × 1000 + 500)`), interior
    detailing (`round((uniqueMod − 1) × 150 + 100)`, free with the car-wash upgrade), fluid refill
    (`(newLevel − start) × 20`), car sale price (`CarSummaryTab.sellPrice`), car purchase price (row 6), crate cards
    (`UIHelper.SetCaseOpeningCardValue`, rolled with `UnityEngine.Random` from `RealPlayerLevel`: money
    `Range((L+n)×15, (L+n)×45)` with `n = round(L × 0.4)`, scrap `Range(L×5, L×15)`, XP up to
    `round(GetCurrentExpToNextLevel(L) × 0.35)`).
- **Effects next to the money:** a car sale deletes the car; a skill reset clears the point skills
  (`UpgradeItem` state 0, `availablePoints` refunded); scrapping deletes the item (`Inventory.Delete`, hooked by row
  7's `InventoryHook`); scrap per condition deletes with a LINQ filter (probably not through `Inventory.Delete`); the
  quality upgrade writes `Item.Quality` in place; license plates are `Inventory.Add`ed one by one before the money
  call; a barn map (`NotificationCenter.<UseItem>g__ShowMapOpeningAskWindow|1`) does `BarnsAmount + 1` and
  `Inventory.Delete(map)`; a barn trip does `BarnsAmount − 1` (`MapWindow.SubmitPanelAction` case 7); a crate
  (`CaseOpeningWindow.SetItem`) deletes the case item, `TakeLoot` pays the picked card (0 money, 1 XP, 2 scrap, 3+
  an item via `Inventory.Add`), and `Hide` puts the case back on some close paths.
- `BarnsAmount` lives in `GlobalData` and `NewGlobalDataWrapper.BarnsAmount` (0x20). A new profile starts at 0
  (`ProfileData.InitGlobalData`).

## Goals / Non-Goals

**Goals:**
- Every change to shared money, scrap, XP/level, point skills and the barn count is applied once, by the server.
- The server computes each amount it can and bounds the rest. Each change has a reason in the server's log.
- A money or scrap change that no feature claims cannot change a client's local value (default deny), and the
  harness can prove that every reachable path is claimed.
- Upstream #94 is closed: crates keep money, XP and level equal for everyone.
- The windows this row owns are opened in the guard; the reachable gaps (spill fine, skill reset, junkyard fee) are
  fixed.

**Non-Goals:**
- Moving client-rolled values to the server (crate rolls, car sale and purchase prices, `uniqueMod`): row 16
  (`server-game-logic`). This row bounds them.
- The drag strip (entry fee, bets, prizes), the map's "measure power" (−500, drag strip only), photo locations: they
  stay blocked (backlog).
- Promo/stage profile bonuses (`GarageLoader.<Load>d__14`) and test-mode money: unreachable while connected (the
  session never loads a promo profile).
- Sandbox and `UnlimitedMoney`/`UnlimitedScraps`: sandbox is skipped (user decision); the hooks treat a no-op call as
  nothing to send.
- Starting money per difficulty for a new session: row 8 part 2 (`new_session_difficulty`).
- The effects themselves where another row owns them (paint, tint, welder and repair results; part and detail
  state; parking slots). This row only adds the money side and the effects listed under Trades.
- Per-player money or a "who may spend" permission: money is shared (user decision).

## Decisions

### D1. One prefix per mutator, attribution by scope, default deny for money and scrap

`Logic/Economy/EconomyHooks.cs` replaces `StatsHooks` and `JobHooks.BeforeAddMoney` with one prefix per mutator:
`GlobalData.AddPlayerMoney(int)`, `AddPlayerScraps(int)`, `SetPlayerScraps(int)` (converted to a delta) and
`AddPlayerExp(int, bool)`. Each runs at `Priority.First`. It does nothing when not connected,
`ClientData.IsServerUpdating` is set, or the amount is 0.

Attribution: `EconomyScope` is a small stack on the main thread. A feature hook pushes `(Reason, Mode, Arg,
ItemUid, Loader)` in a prefix on the calling game method and pops it in a postfix (plus a Harmony finalizer, so an
exception cannot leave a scope open). For a coroutine the scope wraps each `MoveNext` step, which is where the
mutator call happens. The mutator prefix reads the top scope and acts by its `Mode`:

| Mode | Local call | Sent |
|---|---|---|
| `Fee` | runs (instant UI and sound) | `EconomyRequest { Reason, Money/Scraps/Exp = amount, Arg, ItemUid, Loader }` |
| `Covered` | runs | nothing; a feature packet already carries it (job payout capture, shop-sale prediction) |
| `Suppressed` | skipped | nothing; the server's result sets the value (Trades, row 6 purchase) |
| none (money, scrap) | **skipped** | nothing; `EconomyAudit.Unattributed(kind, amount)` logs scene, `GameMode`, top window and amount, and counts |
| none (XP) | runs | `EconomyRequest { Reason = Work, Exp }` (today's behaviour) |

XP stays a trusted delta because its paths are many +1s (mount, unmount, examine, diagnostic tools) and the
server already bounds it. Money and scrap with no scope are dropped, so a missed path shows up as a counter and a
log line, not as a silent local change that row 14's digest later undoes.

`ActivateDifficultyLevel` gets a prefix/postfix that sets `ClientData.IsServerUpdating` around it, so
`SetPlayerMoney`, the sandbox scrap write and `MaxXP` never send anything (the `WorldState` that triggered it writes
the server values right after).

*Alternatives:* (a) attribute by amount and open window (−500 while `MapWindow` is open = travel): breaks when two
paths share an amount or a window and cannot see coroutines; (b) one prefix per caller only, no central hook: a
missed caller stays a silent local change, and nothing can count it; (c) let unattributed money run locally (today's
behaviour): the digest undoes it 10 s later, which looks like a desync and hides the gap.

### D2. Three kinds of paths: Fee, Trade, Work

- **Fee**: the game has already done the thing (traveled, painted, welded, repaired) when it charges. The local
  call runs. The server applies the amount it computes (fixed) or the client's amount when it is in range (ranged),
  with vanilla's clamp. It never refuses for "not enough money": vanilla charges these after the fact and clamps at
  0, and the effect cannot be taken back. It refuses only an amount outside the rule (`Invalid`); the requester then
  gets `WorldState` back and its prediction is corrected.
- **Trade**: money (or scrap) and an effect that the server can apply together: car sale, skill reset, scrap item,
  scrap per condition, quality upgrade, license plates, barn map. The client asks first and skips the vanilla
  method. The server checks (money, scrap, item exists, car free) and applies both, or refuses with a reason and a
  message. Existing Trades: shop buy/sell, taken items, upgrades, parking level, car purchase (row 6).
- **Work**: XP from work (D1).

Vanilla's own money checks (`TryGetMoneyForPaint`, the pie menu's welder check, `ShopLicenseBuyWindow.BuyItem`)
read the local value, which equals the server's except during a race. The race only matters for Trades, where the
server checks again.

### D3. One request packet and one rule table on the server

`EconomyRequest { RequestId, Reason, Money, Scraps, Exp, Arg, ItemUid, CarLoaderId, SpawnSeq, ParkingSlot,
CarId, Items }` C→S and `EconomyResult { RequestId, Reason, Accepted, Refusal, Money, Scraps }` S→requester
(D16). Server `Data/Economy/EconomyService.cs` handles it under `GameDataManager.StateLock`:

1. `EconomyRules.Evaluate(reason, request, state)` returns `EconomyOutcome { Money, Scraps, Exp, Refusal, Effect }`
   (amounts are deltas the server decided on).
2. Apply: `Money = clamp(Money + money, 0, 900,000,000)`, `Scraps = max(0, Scraps + scraps)` (vanilla does not clamp
   scrap; the floor keeps today's server rule), `StatsHandlers.ApplyExp(exp)`, then the effect (D6–D10).
3. One ledger entry (D13), one log line (`[Economy] client 2 TravelFee(Junkyard) money −500 → 12000`).
4. `WorldState` to everyone when a value changed (`updateGamemode = false`); `EconomyResult` to the requester for
   Trades and refusals.

The rule table (option C). "Client amount" is the amount the client's game computed; the server uses it only when the
rule says so.

| Reason | Kind | Amount the server applies | Check |
|---|---|---|---|
| `TravelFee` (`Arg` = `SceneType`) | Fee, fixed | Auction −200, Junkyard −500, Barn −100 | client amount equals the table; Barn also D5 |
| `TravelFeeLegacy` | Fee, fixed | same table by scene type | only if task 1.2 finds it reachable |
| `FluidSpill` | Fee, fixed | −50 | — |
| `FluidRefill` | Fee, ranged | client amount | −2000 ≤ amount < 0 (task 1.2 sets the bound from the units) |
| `PaintCar` (`Arg` = paint type) | Fee, fixed | −1000 (0), −100 (1) | — |
| `WashBeforePaint`, `WashBeforeTint` | Fee, fixed | −100 | — |
| `Tint` (`Arg` = windows) | Fee, fixed | −50 × `Arg` | 1 ≤ `Arg` ≤ 16 and amount = −50 × `Arg` |
| `Welder` | Fee, ranged | client amount | −5000 ≤ amount ≤ −1 (task 1.3 records `uniqueMod` range) |
| `InteriorDetailing` | Fee, ranged | client amount | −1000 ≤ amount ≤ −1; refused (`Invalid`) when the server's garage has the car-wash upgrade (vanilla charges 0 then) |
| `PartRepair` (`ItemUid`) | Fee, ranged | client amount | item in the server's inventory; 1 ≤ −amount ≤ `PricingCalculator.GetPrice(item)` |
| `CrateMoney`, `CrateScrap`, `CrateExp` (`ItemUid` = case) | Fee, ranged | client amount | D9 |
| `SkillReset` | Trade | −1000 × points the server counts | D7 |
| `CarSale` | Trade, ranged | client price | D6 |
| `ScrapItem`, `ScrapPerCondition`, `ScrapUpgrade` | Trade | server-computed scrap | D8 |
| `LicensePlates` | Trade, ranged | client price | D10 |
| `BarnMap` | Trade | barns + 1 | D5 |
| `Work` | Work | client XP | 0 < exp < 10,000 (today's rule) |

`Invalid` refusals are logged with the client amount, so a wrong table entry shows up in the scenario logs instead of
silently costing nothing.

### D4. Fee hooks (scope openers)

Prefix push / postfix pop, `Mode = Fee`, all gated on connected. Names are the interop names; task 1.1 confirms
each one fires on the real UI path.

| Reason | Hook | `Arg` / context |
|---|---|---|
| `TravelFee` | `MapWindow.SubmitPanelAction` | `Arg` from `currentSelectedDestination` (4 Auction, 5 Junkyard, 7 Barn) |
| `TravelFeeLegacy` | `NotificationCenter.ButtonAccept` ("ChangeScene") | scene type from the hash |
| `FluidSpill` | `PartScript._Hide_d__159.MoveNext` | — (the same step's `AddPlayerExp(1)` stays Work) |
| `FluidRefill` | `FluidRefill.Hide` | — |
| `PaintCar` | `PaintshopManager.TryGetMoneyForPaint` | `Arg` = `paintshopType` |
| `WashBeforePaint` | `PaintshopWindow._ShowCoroutine_d__10.MoveNext` | — |
| `WashBeforeTint` | `TintingWindow._ShowCoroutine_d__18.MoveNext` | — |
| `Tint` | `TintingWindow.TintAction` | `Arg` = windows counted (amount / −50) |
| `Welder` | `WelderLogic.__c__DisplayClass5_0` accept (`Method_Internal_Void_Boolean_PDM_0`) | `Loader` |
| `InteriorDetailing` | `InteriorDetailingToolkitLogic.__c__DisplayClass6_0` accept | `Loader` |
| `PartRepair` | `RepairPartWindow.ProcessGameResult(BarType)` | `ItemUid` = `currentItemInfo.Item.UID` |
| `Crate*` | `CaseOpeningWindow.TakeLoot(CaseOpeningItem)` | reason from `TypeOfCard`, `ItemUid` = the case (D9) |

`Covered` scopes: `NotificationCenter.NewButtonAccept` ("SellItem" branch; the server's `SellSingle` pays, the local
call stays a prediction); `JobEndContext` while active (row 3's capture moves into the scope: the mutator prefix
calls `JobEndContext.CaptureMoney/CaptureExp` and sends nothing). `Suppressed` scopes: row 6's purchase capture
(replaces its own `AddPlayerMoney` prefix), and every Trade entry point (D5–D10) for the case where vanilla is
replayed after the server's answer.

*Alternative:* one prefix per fee that sends a request and skips vanilla's `AddPlayerMoney` (option B without
prediction) — the UI would show the old money until the round trip, and a missed path would still be invisible.

### D5. Travel fees and the shared barn count

- `WorldState.Barns` (int, additive; a save without it loads 0, which is vanilla's start). `HandleWorldState` writes
  `GlobalData.BarnsAmount` (setter) and `globalDataWrapper.BarnsAmount`, like scraps.
- **Barn trip:** the `TravelFee(Barn)` request also carries `Arg2 = 1` ("uses a barn"). The server applies the fee
  (when the client charged one) and `Barns = max(0, Barns − 1)`. Vanilla decrements locally before the scene change
  (`set_BarnsAmount`); the next `WorldState` confirms it. In Sandbox (difficulty 2) vanilla neither charges nor
  decrements; sandbox is skipped anyway. A barn trip with the fee setting off still sends the request (amount 0,
  `Arg2 = 1`): the scope sees no mutator call, so the `SubmitPanelAction` postfix sends it when the barn count
  dropped during the call.
- **Barn map (Trade):** prefix on `NotificationCenter.__c__DisplayClass17_0.<UseItem>g__ShowMapOpeningAskWindow|1(
  bool)` with `wasAccepted`: send `EconomyRequest { BarnMap, ItemUid = map }`, skip vanilla. Server: the map item is
  in the inventory → remove it (`InventoryItemAction Remove` to everyone), `Barns + 1`, `WorldState`, result. The
  client plays vanilla's `AddMap` sound on the accepted result.
- **Travel fee and the setting (user decision 2026-10-06: a server rule):** server config `travel_fees` (default
  `true`, vanilla's default) decides for everyone. The client sends the trip's `EconomyRequest { Travel, Scene }`
  whatever its own `TravelHaveCost` says and suppresses its local charge; the server charges the table amount when
  `travel_fees` is on and the difficulty is not Sandbox, else nothing, and answers with the new money.
- A trip whose fee the server refuses (`Invalid`) still happens; the log shows it.

### D6. Selling a car: ask first, the server deletes and pays

- Prefix on `GameScript.SellCar(CarLoader, int sellPrice)` (the one entry from `CarSummaryTab.<SellCar>g__
  SellCarAction|32_0`; task 1.4 lists any other caller, for example a parked car's info). Connected: send
  `EconomyRequest { CarSale, Money = sellPrice, CarLoaderId, SpawnSeq }` (or `ParkingSlot`, `CarId` for a parked
  car), return false. Row 14a's `Action SellCar` guard prefix runs first and is opened by this change (D14).
- Server checks: the record exists with that `SpawnSeq` (or the parking slot holds that car id); not a job car
  (`Spawn.IsJob`); no row 13 away claim (`CarAwayRegistry`); no part claim of another player (`CarClaims.Held`);
  `0 < sellPrice ≤ MaxCarSalePrice` (config `max_car_sale_price`, default 5,000,000). Refusals: `Busy`, `Invalid`,
  `Gone`.
- Accept: garage car → `CarPartsStore.ClearLoader(loader, ClearReason.Sold)` (new value, appended), which raises
  `LoaderCleared` and sends row 1's delete to everyone; parked car → `ParkingService.TryRemove(slot, id)` +
  `BroadcastSlot`. Then `Money += sellPrice`, `WorldState`, `EconomyResult`.
- Client on accept: the car disappears through row 1's/row 2's normal apply (the seller too). The seller closes the
  car info window, plays vanilla's `AddMoney` sound and raises the stat the coroutine raises (task 1.4 reads
  `<SellCarCoroutine>d__135`; if it does more, for example `GlobalData.Save`, that is listed and skipped). Refused:
  info window with the reason.
- Row 2's `PlacementState.OnLoaderCleared` and row 3's subscriber treat `Sold` like `Deleted`.

*Alternatives:* replay the vanilla coroutine after the accept — it deletes the car locally while the server's
delete broadcast arrives, so the two race on the same loader; optimistic (sell locally, then tell the server) — the
car could be gone for the seller while the server refuses because another player holds a part claim on it.

### D7. Skill reset: the server resets the shared skills

- Prefix on `SkillsTab.ResetSkillsAction`: connected → `SkillsTab.CanReset(out points)` as vanilla does; if false,
  run vanilla (it shows its own message); else send `EconomyRequest { SkillReset, Money = −1000 × points }`, skip
  vanilla.
- Server: `points` = point skills unlocked in `GarageState.PlayerUpgradeLevels` (`CalculateSpentPoints` counts cost;
  task 1.5 records whether vanilla's `pointsToReset` counts unlocked levels or spent points, and the rule follows
  it). Money ≥ cost, else `NoMoney`. Accept: clear every point skill, `AvailablePoints = ComputeAvailablePoints`,
  `Money −= cost`, `GarageState` + `WorldState` to everyone, result.
- Client: `GarageUpgrades.SyncUpgrades` must turn levels off as well as on (task 1.5 checks it; if it only unlocks,
  task 5.2 adds the reset of `UpgradeItem` state and `upgradeSystem.availablePoints`, the fields vanilla's reset
  writes).

### D8. Scrap: the server computes from its own item

- `ScrapFormulas` (server) ports `GetScrapFromItem(item, scrapType)` and `GetCostForScrapUpgrade(targetQuality,
  itemValue)` from the decompile. The item price is the `ItemProperty` field at 0x18 (task 1.6 confirms it is the
  `Database` price the server already uses in `PricingCalculator`).
- **Scrap item:** prefix on `ScrapProduction.MakeScrap(int amount)` → `EconomyRequest { ScrapItem, ItemUid =
  currentItem.UID, Arg = grade, Scraps = amount }`, skip vanilla. The grade comes from a postfix-captured
  `ProcessGameResult(BarType)` argument (task 1.6 maps `BarType` to `scrapType`). Server: item in inventory → remove
  it (broadcast), `Scraps += GetScrapFromItem(item, grade)`. A client amount that differs is logged, the server's
  wins.
- **Scrap per condition:** prefix on `ScrapPerConditionWindow.AcceptAction` → `EconomyRequest { ScrapPerCondition,
  Arg = slider }`, skip vanilla (the same shape as row 7's `SellPerConditionWindowHook`). Server: the items vanilla's
  filter selects (task 1.6 reads `<ScrapPerCondition>b__1/b__2/b__3`, including the special-group rule) are removed
  and summed with `scrapType 0`.
- **Quality upgrade:** prefix on `ScrapUpgrade.UpgradeItem` (also reached from `UpgradeAction` and
  `UpgradeFromOutside`; task 1.1 checks that all three pass through it) → `EconomyRequest { ScrapUpgrade, ItemUid,
  Arg = target quality }`, skip vanilla. Server: cost = `GetCostForScrapUpgrade(target, itemValue)`; scraps ≥ cost,
  else `NoScraps`; `Quality = target`, `Scraps −= cost`, row 5a's `ItemActionType.Update` to everyone, `WorldState`.
- The scrap window refreshes from the server's inventory and stats updates (row 7's `InventoryHandlers.Refresh*`
  plus `RefreshStatsUICoroutine(StatType.Scraps)`).

### D9. Crates (upstream #94)

- Opening: `CaseOpeningWindow.SetItem(Item)` deletes the case through the hooked `Inventory.Delete`, so the server
  removes it as today. The server remembers `RecentCases[uid] = (clientId, UtcNow)` when an item with a case id
  (task 1.7 lists them: `case_*`, `specialCase`) is removed, and forgets it when the same UID is added back (`Hide`
  on close-before-open, UID-idempotent ADD) or after 30 minutes.
- Loot: the `TakeLoot` scope sends `EconomyRequest { CrateMoney | CrateExp | CrateScrap, ItemUid = case, amount }`.
  Item cards (type ≥ 3) go through the hooked `Inventory.Add` and need nothing here. The case UID comes from
  `CaseOpeningWindow.originalItem` (task 1.7 confirms the field holds the opened case at `TakeLoot`).
- Server: the case is in `RecentCases` for that client and not looted yet; the amount is in the card's range,
  computed from the server's level `L` with the formulas in Context (money `(L+n)×15 … (L+n)×45`, scrap `L×5 …
  L×15`, XP `1 … round(GetCurrentExpToNextLevel(L) × 0.35)`; task 1.7 fixes the XP lower bound and the rounding).
  Accept → apply, mark looted. Else `Invalid` + `WorldState` to the requester.
- "Open next case" (`OpenNextCaseAction`) repeats `SetItem`; each case is its own entry.

### D10. License plates

Prefix on `ShopLicenseBuyWindow.BuyItem(bool custom, bool blank, string text)`: connected → build `currentAmount`
`ModItem`s the way vanilla does (`LicensePlate`, condition 1, `itemID`, text), send `EconomyRequest {
LicensePlates, Money = −currentPrice, Items }`, skip vanilla. Server: `0 < currentPrice ≤ currentAmount ×
MaxPlatePrice` (task 1.8 reads where `currentPrice` comes from; if it is the item's `Database` price × amount, the
rule becomes fixed); money ≥ price, else `NoMoney`. Accept: new UIDs, add to the inventory, broadcast like
`ShopAction.Buy`, `WorldState`, result. The client shows vanilla's bought message on the result.

### D11. Existing paths folded into the scopes

- `StatsHooks` is removed; its behaviour lives in `EconomyHooks` (Work XP; scrap only with a scope).
- `JobHooks.BeforeAddMoney` is removed; `JobEndContext.Begin` pushes a `Covered` scope with the capture callbacks,
  `Commit` pops it. Behaviour unchanged (`JobEndRequest` carries payout and XP).
- Row 6's purchase `AddPlayerMoney` prefix is removed; its capture pushes a `Suppressed` `CarPurchase` scope.
- `NotificationCenter.NewButtonAccept` gets a `Covered` `SellItem` scope (prediction kept).
- The client's `ShopHandlers` `SellCondition` branch (calls native `SellPerCondition`, which pays locally) is
  dead: the server never sends `ShopAction.SellCondition`. It is removed.
- `ParkingHandlers.OnUnlock`: the server computes the price (`UnlockedLevels × 50,000`, halved when
  `GarageState.PlayerUpgradeLevels["cheaper_parking"]` has a level; task 1.5 confirms the upgrade id) and ignores
  `request.Price` except for a log line on mismatch. The client's prefix is unchanged.
- `ParkingHandlers.ParkArrival` (row 6's `CarLoaderID = −1`): refuse `Invalid` when `Price < 0` or `Price >
  max_car_purchase_price` (config, default 5,000,000).

### D12. Paths that stay blocked or out of scope

Drag strip (`DragWindow` start, `DragChampionshipLadderWindow` bets and prizes), `MapWindow.
MeasurePowerForSelectedCarLoader`, promo profiles, test mode, `TestScript`. Their scenes or windows stay denied by the
guard. If one is reached anyway, D1's default deny drops its money and the audit counts it.

### D13. Ledger and audit

- Server: `EconomyService.Ledger`, a ring buffer of the last 500 entries `{ UtcTime, ClientId, Reason, Requested,
  Applied (money, scraps, exp), Refusal, MoneyAfter }`. Runtime only. Server command `economy [n]` prints the last n
  entries and a count per reason since start.
- Client: `EconomyAudit` keeps counters per reason (sent, covered, suppressed) and `Unattributed` with the last 20
  unattributed calls. Harness dump section `economy` reads it.
- The `economy-fees` and `economy-trades` scenarios require `unattributed == 0` on both clients after every step.

### D14. Guard entries

In the merge commit (`GuardRules`): `Allow` with owner `"row 10"` for `Window CaseOpening`, `Window Scrap`, `Window
ScrapPerCondition`, `Window ShopLicenseBuy` and `Action SellCar` (owner changes from "row 6 part 2"). Any window
the trace (task 1.2) shows as blocked on these paths (for example a scrap upgrade sub-window) is added too. The
drag strip, `RaceTrack` and other backlog entries stay denied. The scenarios run with the guard on `Enforce` and use
`guard-allow` only in group 1.

### D15. Server stores vs. relays

| Data | Server | Packets |
|---|---|---|
| Money, scraps, level, exp | **stores** in `WorldState` (`world` section) | `EconomyRequest` → `WorldState` (everyone), `EconomyResult` (requester) |
| Barn count | **stores** `WorldState.Barns` | same |
| Point skills after a reset | **stores** `GarageState.PlayerUpgradeLevels`, `AvailablePoints` | `GarageState` |
| Items removed or changed by a Trade | **stores** in `InventoryState` | `InventoryItemAction` Remove/Add/`Update` |
| Sold car | **removes** the loader record (row 1) or parking slot (row 2) | row 1 delete, row 2 `ParkingSlotUpdate` |
| Open crates | **stores** runtime-only `RecentCases` | — |
| Ledger | **stores** runtime-only ring buffer | — |
| Sounds, toasts, window refreshes | relays nothing | client-local |

### D16. Packets

Appended to the end of `PacketTypes` (never renumbered): `EconomyRequest { int RequestId; EconomyReason Reason; int
Money; int Scraps; int Exp; int Arg; int Arg2; long ItemUid; int CarLoaderId; int SpawnSeq; int ParkingSlot; string
CarId; List<ModItem> Items }` C→S; `EconomyResult { int RequestId; EconomyReason Reason; bool Accepted;
EconomyRefusal Refusal; int Money; int Scraps }` S→requester. Enums with explicit values, append-only:
`EconomyReason { Work = 0, TravelFee, TravelFeeLegacy, FluidSpill, FluidRefill, PaintCar, WashBeforePaint,
WashBeforeTint, Tint, Welder, InteriorDetailing, PartRepair, CrateMoney, CrateScrap, CrateExp, SkillReset, CarSale,
ScrapItem, ScrapPerCondition, ScrapUpgrade, LicensePlates, BarnMap }`, `EconomyRefusal { None, Invalid, NoMoney,
NoScraps, Busy, Gone }`. Changed: `WorldState.Barns` (`[OptionalField]`). Unchanged and kept: `StatsAction` (the
harness's `stats-add`; no client hook sends it after this change).

### D17. Late-join path

1. A client joins and sends `AskForSync`. The `world` snapshot (`SyncOrder` 0) carries `Money`, `Scraps`, `Level`,
   `Exp`, `Barns`; `garage` (10) carries the skills. No new section and no new `SyncOrder` slot.
2. `HandleWorldState` writes `BarnsAmount` and the profile wrapper with the other values.
3. Runtime-only state is not sent: a late joiner cannot loot another player's open crate (the entry belongs to the
   opener), and the ledger is server-side.
4. A Fee prediction made while the joiner's snapshot is in flight is corrected by the next `WorldState` (the server
   sends one per applied request).
5. Persistence: `Barns` is saved with the `world` section (additive field, no version step). Skills and inventory
   changes from Trades are saved by their sections.

### D18. If the user picks option A or B instead of C

The default is C (`QUESTIONS.md`, "How row 10 syncs money"). What changes:

- **A (central hook, the server trusts the client's amount):** D3's table collapses to one bound per kind (money
  delta within ±max, scrap within ±max). D4's fixed amounts, D8's ported formulas and D9's ranges go away. D1's scopes
  stay (they are still needed to tell Covered/Suppressed paths from new ones and to drop unattributed calls). The
  Trades (D5 barn map, D6, D7, D8, D10) stay, because they are about applying an effect together with the money,
  not about the price. Task groups 3 and 4 shrink; about one session less.
- **B (the server computes every price):** the ranged rows become computed, which needs data the server does not
  have: `uniqueMod` per car (welder, interior detailing; rows 1/4 would have to store it), the repair cost from the
  repair window's `PartInfo`, the car sale formula (`CarSummaryTab`), the plate price, and server-rolled crate cards
  (crate opening becomes ask-first: the server rolls the three cards and sends them). This overlaps row 16 and adds
  about two to three sessions; the crate and sale parts would wait for row 16.

## Risks / Trade-offs

- [A scope hook never fires on the real path (inlined caller, coroutine, interop name)] → task 1.1 traces every scope
  opener with a fire counter on the real UI path; a dead one shows as an unattributed call in the trace. Fallback per
  path: hook the caller one level up (for example `RepairPartWindow.RepairPartAction` and `StopMiniGame` for
  `ProcessGameResult`), or the mutator prefix's window/mode check for that single amount (logged as a heuristic).
- [Dropping unattributed money hides a vanilla path the spike missed (the player gets something for free or loses
  nothing)] → the audit counter and log make it visible; the trace scenario walks every reachable window. That is
  the same outcome as today (the digest undoes it), only earlier and logged.
- [Two clients predict fees at the same time; each `WorldState` briefly shows the other's view] → the next
  `WorldState` converges within one round trip; row 14 confirms a mismatch only over two unchanged rounds.
- [A Fee is applied although the shared money is short] → vanilla does the same (clamp at 0) for paths that check
  nothing (repair); paths with a vanilla check read the same value as the server.
- [The client-computed prices (welder, sale, plates, crates) are trusted within bounds] → co-op only (no
  anti-cheat goal); bounds stop runaway values and bugs. Row 16 can replace them.
- [`SetItem`/`Hide` crate paths add the case back in a way the server does not see (a re-add outside the garage)] →
  task 1.7 traces close-before-open and close-after-open; a case stays in `RecentCases` for 30 minutes at most.
- [Skipping `ScrapProduction.MakeScrap` leaves the scrap window in a half state (current item still shown)] → task
  1.6 checks what `MakeScrap` does besides the item and the scrap call; if the window needs it, vanilla is replayed on
  the accept with a `Suppressed` scope and `InventoryHandlers.IgnoreInventoryHooks`.
- [The barn trip decrement and the barn map race between two players (two trips with one barn)] → the server floors
  at 0 and logs; both trips happen (co-op, rare).
- [Removing `StatsHooks` changes the XP path that many scenarios depend on] → the XP path keeps the same packet
  semantics (Work XP), and the full regression (task 7.5) covers it.

## Migration Plan

Clients and server must run the same build (row 9's protocol hash). Old saves have no `Barns` → 0 (vanilla's start
value). No section version changes. Rollback = previous build; it ignores `Barns` (the barn count is lost) and
drops `EconomyRequest` packets (fees become local-only again).

## Open Questions / Assumptions

Decided without the user (each can be overridden; listed in QUESTIONS.md under row 10 when the change starts):

- **A1** Option C (`QUESTIONS.md` default). D18 lists what A or B would change.
- **A2** Unattributed money and scrap calls are dropped locally and logged (not applied and later undone).
- **A3** Decided (user, 2026-10-06): travel fees follow the server rule `travel_fees`, not the player's setting.
- **A4** Fees charged after the fact are applied with vanilla's clamp (money can reach 0); they are never refused for
  lack of money.
- **A5** A car cannot be sold while another player holds a part claim on it, it is away (row 13) or it is a job car.
- **A6** Client-computed values (car sale and purchase price, welder, detailing, refill, repair, plates, crate cards)
  are trusted within bounds until row 16.
- **A7** One shared barn count; a barn map used by anyone adds a barn for everyone.
- **A8** Any player can reset the shared skills (vanilla's confirmation dialog only); others get a toast.
- **A9** The drag strip and the map's "measure power" stay blocked.

Deferrable unknowns (answered by group 1; they do not change the approach): interop names and fire counts of every
scope opener; other callers of `GameScript.SellCar` (parked cars); what `<SellCarCoroutine>d__135` does besides
money and the delete; `pointsToReset` semantics and the `cheaper_parking` id; whether `SyncUpgrades` turns skills
off; the `BarType` → `scrapType` mapping and the 0x18 price field; the per-condition scrap filter; case item ids,
`originalItem` at `TakeLoot`, the XP card's lower bound; where `ShopLicenseBuyWindow.currentPrice` comes from; the
refill units; the `uniqueMod` range.
