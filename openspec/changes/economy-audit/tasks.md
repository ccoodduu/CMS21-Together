# Tasks

> **Read first:** `docs/spikes/economy-paths.md` (static decompile, 2026-10-06) and design.md Context.
>
> TODO (draft cut short by a reboot): groups 2–7 are outlines; expand each item to the level of group 1 (exact
> files, and a "Verify:" line using the harness).

Prerequisites (merged): rows 7, 1, 2, 3, 4, 5a, 5b, 6 part 2, 13, 14, 14a (see proposal.md Impact). Work on branch
`change/economy-audit`.

## 1. Spike: runtime trace of every money path (harness only, no behaviour change)

- [ ] 1.1 Add `tools/TestHarness/Features/EconomyCommands.cs` with `econ-trace on|off|report`: logging-only
      prefix/postfix with fire counters on the three mutators and every scope opener and Trade entry of design.md
      D4–D10 (by interop name; unpatchable targets reported, not thrown). Each mutator log line carries amount, money/
      scrap/exp before and after, scene, `GameMode`, top window and the innermost traced caller. Verify: the harness
      builds with `tools/test-env/Deploy-Mod.ps1` and `econ-trace report` lists every target with count 0.
- [ ] 1.2 Scenario `tools/test-env/scenarios/economy-trace.ps1` (instance A only, connected, guard `Enforce` with
      `guard-allow` for the row 10 entries): drive each path once (verbs from 6.1: map travel to junkyard, spill
      fine, refill, paint, washes, tint, welder, detailing, repair, crate with each card, skill reset, car sale,
      scrap, scrap per condition, quality upgrade, plates, barn map + barn trip). Record in design.md ("Runtime trace
      results"): which opener fired around each mutator call; any mutator call with no traced caller; whether
      `TravelFeeLegacy` is reachable; refill units; windows the guard blocked. Verify: every row of design.md D4 has a
      confirmed opener or a fallback in Risks.
- [ ] 1.3 Welder/detailing: record `uniqueMod` for the cars the spawner produces and the charged amounts. Verify: D3
      bounds updated.
- [ ] 1.4 Car sale: callers of `GameScript.SellCar`; what `<SellCarCoroutine>d__135` does besides money and delete
      (stats, `GlobalData.Save`, fades). Verify: D6 confirmed or corrected.
- [ ] 1.5 Skill reset and parking: `SkillsTab.CanReset`/`pointsToReset` semantics; whether `GarageUpgrades.SyncUpgrades`
      turns skills off; the `cheaper_parking` upgrade id. Verify: D7, D11 confirmed.
- [ ] 1.6 Scrap: `BarType` → `scrapType`; the `ItemProperty` 0x18 field vs. the server's `Database` price (compare
      `GetScrapFromItem` with the port for 10 items); the per-condition filter; what `MakeScrap` does besides the item
      and scrap. Verify: D8 confirmed.
- [ ] 1.7 Crates: case item ids, `originalItem` at `TakeLoot`, `Hide` re-add paths, card values for 20 rolls at two
      levels vs. the D9 ranges. Verify: D9 ranges fixed in design.md.
- [ ] 1.8 License plates: source of `ShopLicenseBuyWindow.currentPrice`. Verify: D10 rule fixed or ranged.

## 2. Core

- [ ] 2.1 Append `EconomyRequest`, `EconomyResult` to `PacketTypes`; `Network/Packets/EconomyPackets.cs` with D16's
      fields and enums; `WorldState.Barns` (`[OptionalField]`). TODO: Verify line.

## 3. Server

- [ ] 3.1 `Data/Economy/EconomyService.cs` (handler under `StateLock`, apply with clamp, `WorldState`, result, ledger)
      and `EconomyRules.cs` (D3 table). TODO: Verify line.
- [ ] 3.2 Trades: car sale (`ClearReason.Sold`, `ParkingService.TryRemove`), skill reset, scrap item/per condition/
      upgrade (`ScrapFormulas.cs`), plates, barn map; crate tracking in `InventoryHandlers`. TODO: Verify line.
- [ ] 3.3 `ParkingHandlers`: server-computed level price, purchase price bound; config keys `max_car_sale_price`,
      `max_car_purchase_price`; server command `economy [n]`. TODO: Verify line.

## 4. Client: scopes and mutator hooks

- [ ] 4.1 `EconomyScope`, `EconomyHooks` (replaces `StatsHooks`, `JobHooks.BeforeAddMoney`), `EconomyAudit`;
      `ActivateDifficultyLevel` under `IsServerUpdating`; `HandleWorldState` writes `Barns`. TODO: Verify line.
- [ ] 4.2 Fee openers of D4; `Covered`/`Suppressed` scopes of D11; remove the dead `SellCondition` branch. TODO.

## 5. Client: Trades

- [ ] 5.1 `CarSaleSync` (D6), 5.2 skill reset (D7), 5.3 scrap (D8), 5.4 crates (D9), 5.5 plates (D10), 5.6 barn map
      and barn trip (D5). TODO: split into items with Verify lines.

## 6. Harness and guard

- [ ] 6.1 Verbs `econ-map-travel`, `econ-skill-reset`, `econ-sell-car`, `econ-scrap`, `econ-scrap-condition`,
      `econ-scrap-upgrade`, `econ-license`, `econ-crate`, `econ-barn-map`, `econ-fee`, `econ-unattributed`,
      `econ-ledger`; dump section `economy` and `stats.barns`. TODO.
- [ ] 6.2 `GuardRules` per D14. TODO.

## 7. Integration: harness scenarios in two instances (guard on `Enforce`)

- [ ] 7.1 `economy-fees.ps1`: A and B each drive every Fee path; after each step money/scrap/exp equal on A, B and
      the server ledger's expected value, `economy.unattributed == 0` on both; `econ-unattributed 500` on A leaves
      money unchanged and counts 1. TODO: full steps.
- [ ] 7.2 `economy-trades.ps1`: car sale (B holds a part claim → refused; job car → refused; free car → gone on both,
      money once), race with `net-hold` (A and B sell the same car → one sale), skill reset (skills off on both),
      scrap, scrap per condition, quality upgrade, plates with too little money (refused), barn map + barn trip
      (barns equal), crates (#94: A and B open 3 cases each; money, level, exp equal). TODO: full steps.
- [ ] 7.3 `economy-latejoin.ps1`: late join after trades (barns, skills, money equal); server restart keeps `Barns`.
      TODO.
- [ ] 7.4 Full regression `Run-All.ps1`; STATUS.md, ROADMAP status, INTEGRATION.md (packets, `ClearReason.Sold`,
      config keys, verbs, scenarios). TODO.
