# Tasks

> **Read first:** `docs/spikes/economy-paths.md` (static decompile, 2026-10-06) and design.md Context. Section 3 of
> the spike lists every caller; section 5 lists the gaps in order of exposure.

Prerequisites (merged): `session-persistence-and-rejoin` groups 1–2 (contract, `StateLock`, `world` section,
`Send-ServerCommand`, `Wait-ServerLog`, `Stop-TestServer`/`Start-TestServer`, `to-menu`, `stats-add`),
`sync-players-and-scenes` (`travel`, `Wait-HarnessDump`, barn/auction/salon scenes and the purchase capture of part
2, `buy-car-here`), `sync-car-parts` (`car-spawn`, `car-ready`, `part-unmount`, `part-claim`, `ClearLoader`,
`LoaderCleared`), `sync-car-placement-and-lifts` (`park`, `parking`, `parking-unlock`, `net-hold`,
`ParkingService.TryRemove`), `sync-orders-and-jobs` (`orders-accept`, `job-finish`, `JobEndContext`),
`sync-car-details` (tint hooks, `cardetails-*`), `sync-workshop-machines` (`give-item`, `tool-repair`,
`ItemActionType.Update`), `sync-workshop-car-tools` (`tool-paint-car`, `tool-use`), `sync-test-drive-and-diagnostics`
(`CarAwayRegistry`, `testdrive-go`), `desync-detection-and-resync` (world digest, `resync`, `desync` command),
`multiplayer-guard` (`guard-set`, `guard-allow`, `guard-log`, `guard-try`, `guard-rules`). If one is not merged,
its fee hook still ships (its window stays guarded) and its scenario step is skipped with a note in `STATUS.md`.
Work on branch `change/economy-audit`.

## 1. Spike: runtime trace of every money path (harness only, no behaviour change)

- [ ] 1.1 **In code (2026-10-06):** needs a game run. Add `tools/TestHarness/Features/EconomyCommands.cs`:
      - `econ-trace on|off|report`: logging-only prefix/postfix with fire counters on `GlobalData.{AddPlayerMoney,
        AddPlayerScraps, SetPlayerScraps, AddPlayerExp, SetPlayerMoney, set_BarnsAmount}` and every scope opener and
        Trade entry of design.md D4–D10, patched on demand by type name (nested coroutine and display-class types by
        their interop names); unpatchable targets are reported, not thrown. Each mutator log line carries amount,
        money/scrap/exp/barns before and after, scene, `GameMode` current mode, top window and the innermost traced
        caller that is still running.
      - native-only verbs that drive each path through the game's own method: `econ-map-travel
        Junkyard|Auction|Barn` (sets `MapWindow.currentSelectedDestination` and calls `SubmitPanelAction`);
        `econ-fee spill <loader>` (unmounts a part that still holds fluid through row 1's `part-unmount` path),
        `econ-fee refill <loader>`, `econ-fee wash-paint|wash-tint <loader>`, `econ-fee tint <loader> <windows>`
        (`TintAction` on that many windows); `econ-skill-reset` (opens `SkillsTab`, `ResetSkillsAction`);
        `econ-sell-car <loader>` (`CarSummaryTab` for that car, `<SellCar>g__SellCarAction|32_0(true)`);
        `econ-scrap <uid> <grade>` (`ScrapProduction` with that item, `ProcessGameResult(BarType)`);
        `econ-scrap-condition <percent>` (`ScrapPerConditionWindow` slider + `AcceptAction`); `econ-scrap-upgrade <uid>
        <quality>` (`ScrapUpgrade.UpgradeItem`); `econ-license <amount> [text]` (`ShopLicenseBuyWindow.BuyItem`);
        `econ-crate <uid> <card 0|1|2|3>` (`NotificationCenter.UseItem` → `SetItem`, then `TakeLoot` on the card of that
        type); `econ-crate-close <uid>` (open and close before a card is taken); `econ-barn-map <uid>` (the
        `ShowMapOpeningAskWindow|1(true)` lambda). Paint, welder, interior detailing and repair use row 5b's
        `tool-paint-car`/`tool-use` and row 5a's `tool-repair`.

      Verify: the harness builds with `tools/test-env/Deploy-Mod.ps1`; in a `-KeepRunning` session `econ-trace report`
      lists every target with count 0 and each verb answers (or reports a missing precondition, such as no item).
- [ ] 1.2 **In code (2026-10-06):** needs a game run. Scenario `tools/test-env/scenarios/economy-trace.ps1` (instance A only, connected, guard `Enforce` with
      `guard-allow` for the row 10 entries and any still-planned window of the paths): `econ-trace on`, `give-item` a
      case, a barn map and three scrap-able parts, `car-spawn` two cars, then drive each path once in this order:
      `econ-map-travel Junkyard` (and back with `travel Garage`), `Auction`, `Barn`; `econ-fee spill`, `refill`,
      `wash-paint`, `wash-tint`, `tint 4`; `tool-paint-car`; `tool-use` welder and interior; `tool-repair`; `econ-crate`
      with each card; `econ-crate-close`; `econ-skill-reset`; `econ-sell-car`; `econ-scrap`; `econ-scrap-condition 30`;
      `econ-scrap-upgrade`; `econ-license 2 TEST`; `econ-barn-map`; `econ-trace report`; `guard-log`. Record in
      design.md ("Runtime trace results"): for each mutator call the opener that was running; any mutator call with
      no traced caller; whether `NotificationCenter.ButtonAccept` ("ChangeScene", `TravelFeeLegacy`) is reachable;
      `FluidRefill.Hide`'s amount per litre and which mode opens it; every window/mode the guard blocked. Verify: every
      row of design.md D4 has a confirmed opener or a fallback written into Risks.
- [ ] 1.3 Welder and interior detailing: in the same scenario, log `CarLoader.uniqueMod` for 10 spawned cars and the
      charged amounts. Verify: the `Welder` and `InteriorDetailing` bounds in design.md D3 cover the observed values
      with a margin, or are corrected.
- [ ] 1.4 Car sale: list the callers of `GameScript.SellCar(CarLoader, int)` hit with the trace (garage car info,
      parked car info if the parking window offers a sale) and what `<SellCarCoroutine>d__135` does besides
      `AddPlayerMoney` and the delete (Steam stat, `GlobalData.Save`, fade, window close). Verify: design.md D6 names
      the entry points and the seller's after-accept steps, or is corrected.
- [ ] 1.5 Skill reset and parking: record `SkillsTab.CanReset`'s `pointsToReset` against the unlocked point skills
      and their costs; apply a `GarageState` with fewer unlocked skills through `GarageUpgrades.SyncUpgrades` and check
      whether `UpgradeItem` state and `upgradeSystem.availablePoints` follow; read the `cheaper_parking` upgrade id from
      `GameDatabase.PlayerUpgrades`. Verify: D7 and D11 in design.md are confirmed or corrected.
- [ ] 1.6 Scrap: map `BarType` values to `GetScrapFromItem`'s `scrapType`; compare the game's
      `GetScrapFromItem(item, t)` for 10 items and t = 0…2 with a C# port fed with the server's `Database` price (dump
      both); record the items `Inventory.ScrapPerCondition` removes for a slider value (special groups included) and
      whether it calls `Inventory.Delete`; record what `ScrapProduction.MakeScrap` does besides the delete and the
      scrap call. Verify: D8 in design.md is confirmed (port equal on all 30 values) or corrected.
- [ ] 1.7 Crates: record the item ids of cases, special cases and barn maps; `CaseOpeningWindow.originalItem` at
      `TakeLoot`; which close paths of `Hide` call `Inventory.Add` with the case; card values for 20 rolls each at two
      player levels. Verify: D9's id list and ranges (including the XP card's lower bound) are filled in design.md and
      every observed value is inside them.
- [ ] 1.8 License plates: record where `ShopLicenseBuyWindow.currentPrice` comes from (per plate, custom text
      surcharge). Verify: D10 states a fixed rule or a bound.

## 2. Core: packets and DTOs

- [ ] 2.1 **In code (2026-10-06):** needs a game run. Append `EconomyRequest`, `EconomyResult` to the end of `PacketTypes`; add
      `CMS21-Together-Core/Network/Packets/EconomyPackets.cs` with the fields of design.md D16 and the enums
      `EconomyReason` (explicit values, append-only) and `EconomyRefusal`. Verify: `dotnet build CMS21-Together.sln`
      succeeds and `PacketRouter.Initialize` logs 2 more packets on client and server start.
- [ ] 2.2 **In code (2026-10-06):** needs a game run. `WorldStatesPackets.cs`: `WorldState.Barns` (`[OptionalField]`). Verify: `--check-save
      tools/test-env/fixtures/server_save_v1.json` exits 0, and a server started on a save without `Barns` logs
      `barns 0` in its `world` load line (add `barns` to the line in `GameDataManager`).

## 3. Server

- [ ] 3.1 **In code (2026-10-06):** needs a game run. `CMS21-Together-Server/Data/Economy/EconomyService.cs`: `[PacketHandler(EconomyRequest)]` under
      `GameDataManager.StateLock`; `Apply(outcome)` with the money clamp 0 … 900,000,000, scrap floor 0,
      `StatsHandlers.ApplyExp`; `WorldState` (`updateGamemode = false`) to everyone when a value changed;
      `EconomyResult` to the requester for Trades and refusals, `WorldState` to the requester on a refused Fee; the
      ledger ring buffer (500) and one log line per request (D3, D13). Verify: `stats-add` still works (the
      `server-restart` scenario passes) and an `econ-fee spill` on A in 7.1 shows one `[Economy]` line in
      `Log\Latest.txt` (`Wait-ServerLog`).
- [ ] 3.2 **In code (2026-10-06):** needs a game run. `Data/Economy/EconomyRules.cs`: the Fee and Work rows of D3 (fixed table, ranges, `Invalid` on mismatch with
      the client amount logged; `InteriorDetailing` refused when the garage has the car-wash upgrade). Verify: in 7.1
      every Fee step changes the server's money by the table amount (`economy 1` server command after each step), and
      the `econ-fee tint 0` step is refused with `Invalid`.
- [ ] 3.3 **In code (2026-10-06):** needs a game run. `Data/Economy/ScrapFormulas.cs` (ports of `GetScrapFromItem`, `GetCostForScrapUpgrade`, with the rounding
      group 1.6 found) and the scrap Trades of D8 in `EconomyRules`: `ScrapItem` (remove item, add computed scrap),
      `ScrapPerCondition` (vanilla's filter from 1.6, `scrapType 0`), `ScrapUpgrade` (`NoScraps`, set `Quality`,
      `ItemActionType.Update` to everyone). Verify: in 7.2 the scrap steps change the server's scrap by the amounts the
      group-1.6 port gives, and an upgrade with too little scrap is refused (`economy` shows `NoScraps`).
- [ ] 3.4 **In code (2026-10-06):** needs a game run. Car sale (D6): checks (`SpawnSeq`, `Spawn.IsJob`, `CarAwayRegistry`, `CarClaims.Held` of another player,
      `max_car_sale_price`), `ClearReason.Sold` appended in `Data/Cars/CarPartsStore.cs` and handled like `Deleted` in
      `PlacementState.OnLoaderCleared` and `JobsService`'s subscriber; parked car via `ParkingService.TryRemove` +
      `BroadcastSlot`. Verify: in 7.2 the server command `cars` no longer lists the sold loader, `placement` shows the
      lift/place free, and the refused sales log `Busy`/`Invalid` with the car still listed.
- [ ] 3.5 **In code (2026-10-06):** needs a game run. Skill reset (D7) in `GarageUpgradeHandlers.cs`: count reset points per 1.5, `NoMoney`, clear every point
      skill, recompute `AvailablePoints`, `GarageState` + `WorldState`. Verify: in 7.2 the saved `garage` section
      (`Send-ServerCommand save`, read `save.json`) has no point skill unlocked and money dropped by the cost.
- [ ] 3.6 **In code (2026-10-06):** needs a game run. License plates (D10) and barn map / barn trip (D5): plates get new UIDs and are broadcast like
      `ShopAction.Buy`; `BarnMap` removes the map item and adds a barn; `TravelFee` with `Arg2 = 1` floors `Barns` at 0.
      Verify: in 7.2 the server's `world` log line after `save` shows the expected `barns`, and the inventory dump of
      B contains the plates with the custom text.
- [ ] 3.7 **In code (2026-10-06):** needs a game run. Crates (D9) in `Network/Handlers/InventoryHandlers.cs`: `RecentCases` filled on the removal of a case id
      (1.7), cleared on re-add or after 30 min; `Crate*` rules check the entry, the requester, "not looted" and the
      level-based range. Verify: in 7.2 a second loot request for the same case (`econ-crate` replayed by the harness
      with `econ-ledger resend`) is refused `Invalid`, and `econ-crate-close` leaves no entry (`economy cases`).
- [ ] 3.8 **In code (2026-10-06):** needs a game run. `ParkingHandlers` (D11): server-computed level price with `cheaper_parking`; `ParkArrival` refuses
      `Price < 0` or `> max_car_purchase_price`; config keys `travel_fees` (default `true`, the user's server rule for travel fees, D5), `max_car_sale_price`, `max_car_purchase_price` (default
      5,000,000, appended to `server_config.ini` when missing); server command `economy [n] | cases | reasons`; debug server command `gamemode <name>` (sets `WorldState.Gamemode`,
      broadcasts `WorldState` with `updateGamemode = true`).
      Verify: the `car-placement` and `car-parking-full` scenarios still pass, and in 7.2 a `parking-unlock` with a
      faked price is applied at the server's price (log line shows the mismatch).

## 4. Client: scopes and mutator hooks

- [ ] 4.1 **In code (2026-10-06):** needs a game run. `CMS21-Together-Client/Logic/Economy/EconomyScope.cs` (stack, `Push(reason, mode, arg, itemUid, loader)`
      returning an `IDisposable`, `Current`), `EconomyHooks.cs` (prefixes at `Priority.First` on `AddPlayerMoney`,
      `AddPlayerScraps`, `SetPlayerScraps`, `AddPlayerExp` per D1), `EconomyAudit.cs` (counters, last 20 unattributed
      calls); delete `Logic/Hook/StatsHooks.cs` and `JobHooks.BeforeAddMoney`; `JobEndContext.Begin/Commit` push and pop
      a `Covered` scope that calls `CaptureMoney`/`CaptureExp`. Verify: the `jobs` scenario still passes (payout and XP
      once), and `econ-unattributed 500` (6.1) leaves A's money unchanged and `economy.unattributed` at 1.
- [ ] 4.2 **In code (2026-10-06):** needs a game run. `ActivateDifficultyLevel` prefix/postfix sets `ClientData.IsServerUpdating`; `HandleWorldState` writes
      `GlobalData.BarnsAmount` and `globalDataWrapper.BarnsAmount`. Verify: after a connect, A's client log has no
      `EconomyRequest` with `Exp = 67000` (`econ-ledger`), and `dump` shows `stats.barns` equal to the server's.
- [ ] 4.3 **In code (2026-10-06):** needs a game run. `Logic/Economy/FeeHooks.cs`: the scope openers of D4 (prefix push, postfix pop, finalizer pop), with the
      names group 1 confirmed; `Covered` scope on `NotificationCenter.NewButtonAccept`; row 6's purchase prefix replaced
      by a `Suppressed` `CarPurchase` scope; remove the dead `SellCondition` branch in `Network/Handlers/ShopHandlers.cs`.
      Verify: `economy-trace` (1.2) rerun with the hooks shows a scope on every mutator call (`econ-trace report`:
      0 unattributed) and A's `econ-ledger` lists one `EconomyRequest` per Fee step.

## 5. Client: Trades

- [ ] 5.1 **In code (2026-10-06):** needs a game run. `Logic/Economy/CarSaleSync.cs` (D6): prefix on `GameScript.SellCar(CarLoader, int)` (after the guard,
      `__runOriginal`) sends `CarSale` with `CarLoaderId`/`SpawnSeq` or `ParkingSlot`/`CarId` and skips vanilla; on
      `EconomyResult` accepted: close the car info window, `AddMoney` sound, the stats from 1.4; refused: info window
      with the reason. Verify: `econ-sell-car` on A removes the car on A and B (`Wait-HarnessDump` on `cars`) and
      `stats.money` rises once on both.
- [ ] 5.2 **In code (2026-10-06):** needs a game run. Skill reset (D7) in `Logic/Garage/GarageUpgrades.cs`: prefix on `SkillsTab.ResetSkillsAction` (vanilla when
      `CanReset` is false, else request and skip); if 1.5 showed `SyncUpgrades` only unlocks, apply a reset of
      `UpgradeItem` state and `upgradeSystem.availablePoints` from `GarageState`. Verify: after `econ-skill-reset` on A,
      the harness dump field `skills` (6.1) shows no point skill on A and B and equal available points.
- [ ] 5.3 **In code (2026-10-06):** needs a game run. Scrap (D8) in `Logic/Economy/TradeHooks.cs`: postfix capture of `ScrapProduction.ProcessGameResult(BarType)`,
      prefix `MakeScrap(int)`, prefix `ScrapPerConditionWindow.AcceptAction`, prefix `ScrapUpgrade.UpgradeItem`; each
      sends its request and skips vanilla; the scrap window refreshes on the server's inventory and stats updates.
      Verify: `econ-scrap`, `econ-scrap-condition 30` and `econ-scrap-upgrade` on A give equal `inventory` and
      `stats.scrap` on A and B (`Compare-HarnessDumps -Sections inventory,stats`).
- [ ] 5.4 **In code (2026-10-06):** needs a game run. Crates (D9): the `TakeLoot` scope reads `TypeOfCard`, `itemValue` and `originalItem.UID`. Verify: three
      `econ-crate` runs on A with cards 0, 1, 2 give equal `stats` on A and B and three ledger entries on the server.
- [ ] 5.5 **In code (2026-10-06):** needs a game run. License plates (D10): prefix on `ShopLicenseBuyWindow.BuyItem(bool, bool, string)` builds the plate
      `ModItem`s, sends `LicensePlates`, skips vanilla; the bought message on accept, `GUI_BrakKasy` on `NoMoney`.
      Verify: `econ-license 2 TEST` on A adds two plates with text `TEST` to A's and B's inventory and money drops once.
- [ ] 5.6 **In code (2026-10-06):** needs a game run. Barn map and barn trip (D5): prefix on the `ShowMapOpeningAskWindow|1` lambda (`wasAccepted` only) sends
      `BarnMap` and skips vanilla, `AddMap` sound on accept; `MapWindow.SubmitPanelAction` postfix sends the barn use
      when `BarnsAmount` dropped during the call and no fee was sent. Verify: `econ-barn-map` on A makes `stats.barns`
      one higher on A and B; `econ-map-travel Barn` makes it one lower on both.

## 6. Harness and guard

- [ ] 6.1 **In code (2026-10-06):** needs a game run. Add to `EconomyCommands.cs`: `econ-unattributed <amount>` (calls `GlobalData.AddPlayerMoney` with no scope),
      `econ-ledger [resend]` (the client's `EconomyAudit` counters and last requests; `resend` sends the last
      `EconomyRequest` again for the duplicate check); `StateDump` section `economy` (`unattributed`, `sent`,
      `covered`, `suppressed` per reason), `stats.barns` and `skills` (point skills unlocked, available points); add
      `economy` and `skills` to the sections `Compare-HarnessDumps` knows (`economy` is per-client and only compared for
      `unattributed`). Verify: each verb and section answers in a `-KeepRunning` session and the `connect` scenario
      still passes.
- [ ] 6.2 **In code (2026-10-06):** needs a game run. `GuardRules`: `Allow` with owner `"row 10"` for `Window CaseOpening`, `Scrap`, `ScrapPerCondition`,
      `ShopLicenseBuy`, `Action SellCar`, plus any window 1.2 found; the scenarios of group 7 drop their `guard-allow`
      for them. Verify: `guard-rules` lists them as allowed, `guard-try Action:SellCar` is allowed, `guard-try
      Scene:DragStrip` is still denied, and the `guard` scenario passes with its `SellCar` expectation changed to
      allowed.

## 7. Integration: harness scenarios in two instances (guard on `Enforce`)

- [ ] 7.1 **In code (2026-10-06):** needs a game run. Scenario `tools/test-env/scenarios/economy-fees.ps1`: both connect; `Send-ServerCommand "money set
      200000"`; A and B each `car-spawn` a car. Steps, each followed by `Wait-HarnessDumpsEqual -Sections stats` and a
      check that the server's money (`economy 1`) changed by the expected amount and `economy.unattributed` is 0 on
      both: A `econ-map-travel Junkyard` (−500, then `travel Garage`), B `econ-map-travel Auction` (−200), A `econ-fee
      spill` (−50), B `econ-fee refill`, A `econ-fee wash-paint` (−100) and `tool-paint-car` (−1000), B `econ-fee
      wash-tint` (−100) and `econ-fee tint 4` (−200), A `tool-use` welder and interior detailing (ranged), B
      `tool-repair` (ranged); Expert: `Send-ServerCommand "gamemode Expert"`, A `part-unmount` → exp +2 on both, then `gamemode Normal`. Race:
      `net-hold on` on both, A and B pay a fee each, `net-hold off` → both fees applied once. Default deny: A
      `econ-unattributed 500` → money unchanged on A and B, A's `economy.unattributed` is 1 (then reset by the
      verb). Short money: `money set 30`, A `tool-repair` → money 0 on both (clamp). Pass = every check true and no
      `Invalid` in the server log except the deliberate `econ-fee tint 0` step. Verify with `Run-Session.ps1 -Scenario
      economy-fees`.
- [ ] 7.2 **In code (2026-10-06):** needs a game run. Scenario `tools/test-env/scenarios/economy-trades.ps1`: both connect; `give-item` cases, a barn map and
      scrap-able parts. Car sale: A `car-spawn`; B `part-claim` a part of it, A `econ-sell-car` → refused, car on both;
      B releases; `net-hold on` on both, A and B `econ-sell-car` the same car, `net-hold off` → car gone on both,
      money up once; a job car (`orders-accept`) → refused; a parked car (`park`, then sale from the parking if 1.4
      found the entry) → slot free on both. Skill reset: unlock two skills, A `econ-skill-reset` → `skills` equal and
      empty, money −1000 × points. Scrap: A `econ-scrap`, B `econ-scrap-condition 30`, A `econ-scrap-upgrade` (and once
      with too little scrap → `NoScraps`). Plates: A `econ-license 2 TEST`; with `money set 10` → refused. Barns: A
      `econ-barn-map` → barns +1 on both; B `econ-map-travel Barn` → barns −1 on both, then `travel Garage`. Crates
      (upstream #94): A and B each `econ-crate` three cases with cards 0, 1, 2 → `stats` (money, scrap, exp, level)
      equal on A, B and the server; `econ-ledger resend` on A → refused; `econ-crate-close` → the case is back once.
      After every step `Compare-HarnessDumps -Sections stats,inventory,skills,cars,parking` is empty and
      `economy.unattributed` is 0. Verify with `Run-Session.ps1 -Scenario economy-trades`.
- [ ] 7.3 **In code (2026-10-06):** needs a game run. Scenario `tools/test-env/scenarios/economy-latejoin.ps1`: only A connects; A sells a car, resets the skills,
      uses a barn map and pays a travel fee; B connects → B's `stats` (incl. `barns`) and `skills` equal A's. Restart:
      dump A, `Send-ServerCommand save`, `Stop-TestServer`, A and B `to-menu`, `Start-TestServer`, both reconnect →
      `stats` and `skills` equal the pre-restart dump. Verify with `Run-Session.ps1 -Scenario economy-latejoin`.
- [ ] 7.4 **In code (2026-10-06):** needs a game run. Desync check: `desync-soak` with an extra fee step per round (A `econ-fee spill` every 30 s). Verify: the
      server's `desync` command shows no confirmed `world` mismatch after the run.
- [ ] 7.5 Full regression `tools/test-env/Run-All.ps1` with the four new scenarios; record run ids and results in
      `STATUS.md`; update the ROADMAP status, `INTEGRATION.md` (packets `EconomyRequest`/`EconomyResult`,
      `WorldState.Barns`, `ClearReason.Sold`, `EconomyScope` API for later rows, config keys, verbs, scenarios) and move
      the answered row 10 questions in `QUESTIONS.md`. Verify: `Run-All` green.

## Implementation status (2026-10-06, branch `change/economy-audit-impl`)

Code for 1.1, 2–6 and the scenarios of 1.2 and 7.1–7.4 is in; nothing has run in the game. Static answers to 1.3–1.8
and the deviations are in design.md, "Static findings and implementation notes". Still open for the first game run:
1.3 (`uniqueMod` range, no verb logs it yet), 1.4 (parked-car sale entry), 1.6 (port vs. game for 30 values,
`RoundCondition`, `GetPriceForQuality`), 1.7 (XP card lower bound, `Hide` close paths), 1.8 (blank-plate text), 7.5.
Rows 5a, 5b and 6 part 2 are not on `main`: their fee hooks ship, their windows stay guarded, and the scenarios skip
those steps with a note (`tool-paint-car`, `tool-use`, `tool-repair`, auction and barn trips, the paint shop, tinting).
Extra harness verbs: `econ-unmount <loader>` (native unmount coroutine, for the Expert XP step), `econ-send <Reason>
<money> [arg] [scraps] [exp] [itemUid]` (raw request, for the `Invalid` step), `econ-skill-unlock <id> <level>`.
