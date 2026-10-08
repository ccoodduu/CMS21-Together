# Proposal

Drafted 2026-10-08 (ROADMAP row 16). Documents only; nothing here is implemented yet.

## Why

CMS21 gets no more updates (CMS 2026 replaces it), so game logic that the server ports once never drifts again
(QUESTIONS.md, fourth round, 2026-10-06). Three rows took a client-side interim because the server "has no game code":

- **Row 3** generates orders on one elected client (`JobsService.Elect`, lowest id among `InSession` players in the
  garage). Orders stop while nobody is in the garage, the generator's installed DLC decides the car pool, and an
  order the generator sends just before a re-election is dropped.
- **Row 3** trusts the payout and XP the finishing client reports (`JobEndRequest`, bounds `0..1,000,000` and
  `0..9,999`). The client pays what the last `JobHelper.CheckJob` cached, which is stale when the order tab was
  opened before another player's repair.
- **Row 1** lets the client that spawns a car roll its damage, colour and dents ("whoever spawns a car decides its
  random values", accepted default 2026-10-06). In the garage that is only the job car (`<TakeJob>d__19` →
  `PrepareJob`): an aborted or lost take re-rolls the car, and nothing can check the result.
- **Row 10** trusts client-computed prices within bounds "until row 16" (QUESTIONS.md, 2026-10-06, answer 7): car
  sale price (bound 5,000,000), car purchase price, welder, interior detailing, fluid refill, part repair, crate
  cards, license plates.

The native-decompile spike (`docs/spikes/native-decompile.md`) and the orders spike (`docs/spikes/orders-and-jobs.md`)
read every formula involved, and row 15 proved that the game's own generators run identically on two clients when
the server supplies the seed (`Logic/Outdoor/Reseed.cs`, `outdoor-*` scenarios). That makes the move cheap where
the inputs exist on the server, and seedable where they only exist in the scene.

## What Changes

Per piece (design.md D1 has the full table and the reasons):

- **Moves to the server:**
  - **Order generation.** The server runs a port of `OrderGenerator.GenerateNewJob`, its timer and cap
    (`Update`, `GetMaxOrdersAmount`) and `GenerateMission`, and stores the orders as today. The elected generator
    stays only as the fallback when the order tables are missing (D5).
  - **Job payout and XP** (part 2). The server runs a port of `JobHelper.CheckJob`/`CheckJobTask` on its own part
    records and car details when a job ends, and pays that. The client's value is only compared and logged.
  - **The car sale price** (part 2), as a port of `CarSummaryTab.SetupForSell` over the server's records.
  - **Welder and interior detailing costs** (part 2), from the car's exported `uniqueMod`.
  - **The special case roll** after a finished job (`Inventory.TryAddSpecialCase`: always for a mission, 25 % with
    the `luck` skill).
- **Seeded instead of moved:** the job car's random preparation (`<TakeJob>d__19`, `PrepareJob`, `PreparePart`,
  `PrepareBodyParts`, `SetRandomColorPanels`, `SetRandomDent`). It walks live `PartScript`s and uses physics
  overlaps, so it stays the game's own code on the taking client. It now runs on a random stream seeded from the
  order's server-chosen `PrepSeed`, like row 15's outdoor cars. The server's seed decides the car, a re-take of
  the same order gives the same car, and two clients can prove it.
- **Checked against what the server already knows** (part 2): an outdoor car purchase is checked against the price
  in the instance's reference digest (row 15), and an auction win against the server's last bid.
- **Stays as it is:**
  - already server-authoritative and correct: item prices (`PricingCalculator`), travel fees (server rule
    `travel_fees`), parking levels, scrap formulas, skill reset, outdoor car selection (`BasicCarSelector`) and
    outdoor rolls (row 15);
  - deliberately left to the client, each bounded as today: fluid refill, part repair, crate cards, license plates,
    the salon price, Work XP, and the `EndJob` gate checks (D1).
- **Game data.** The server gets the data from tables exported from the game files the user owns, on the user's
  own machine. Developers export at build/deploy time from their own install. Release zips carry no game tables.
  A hosting player's game exports them before "Host" starts the bundled server, and a dedicated-server owner copies
  an export from a PC with the game. The committed upstream tables leave the repo (open question 1). Row 9's
  exporter (its task group 6) moves into this change (user decision 2026-10-06, fifth round).
- **Identical to single player.** The server's random source reproduces `UnityEngine.Random` (Xorshift128), so a
  port is proven value-for-value: the game's own method on a client and the server's port give the same result for
  the same seed and inputs. The deliberate differences are listed in design.md D7.
- **Proof.** For every piece, a scenario runs the game's own computation on a client next to the server's (exact
  where deterministic, same seed for the random parts). A `shadow` mode logs every mismatch without paying the
  server's value.

Game hooks (Harmony):

- **Part 1:** `OrderGenerator.Update` and `GenerateMission` stay blocked on every client in server mode (row 3's
  prefixes, no election). Seeded streams around `OrderGenerator.<TakeJob>d__19.MoveNext`,
  `<TakeMission>d__22.MoveNext` and `CarLoader.<SetRandomColorPanels>d__321.MoveNext`, using the stream helper
  generalised from `Logic/Outdoor/Reseed.cs`.
- **Part 2:** a `GameScript.EndJob` prefix that flushes the car details before the end
  (`CarDetailsSync.FlushNow`), and an `Inventory.TryAddSpecialCase` prefix that skips the local roll.
- **Exporter, in the menu or the garage:** `GameInventory`, `CarBundleLoader`, `OrderGenerator.ordersDataINI`, the
  car configs, the mission TextAssets and the item-fault table. These are read, never patched.

Packets:

- **Part 1, changed (additive optional fields):** `ModJob.PrepSeed`. `OrderGenerated`/`OrderGeneratorRole` are only
  sent in the fallback mode.
- **Part 2, changed:** `ModJobPart.Key` (the server's part key, resolved by the taker); `JobRemoved` +
  `Payout`/`Xp` (what the server paid, row 19 D16's answer).
- **No new packet types**, unless spike 1.4 makes the hosted export's hot reload need `GameDataReady` (appended).

## Capabilities

### New Capabilities
- `game-data`: the server's game tables are exported from the user's own install, versioned, never shipped, and every
  game-logic piece falls back to its interim path when its tables are missing.
- `server-orders`: the server generates, times and caps orders and story missions, and gives each order the seed its
  car is prepared with.
- `server-payout-and-prices` (part 2): the server computes job payout and XP, car sale prices and car-dependent fees,
  checks purchases, and logs every difference from the client's value.

### Modified Capabilities
<!-- none: openspec/specs/ is empty. When archived, game-data replaces mod-compatibility's game-data-export spec
(its export requirements move here), server-orders replaces sync-orders-and-jobs' "orders generated by an elected
client", and server-payout-and-prices replaces its "payout reported by the finishing client". -->

## Impact

- **Core:** `ModJob.PrepSeed`, `ModJobPart.Key`, `JobRemovedPacket.Payout/Xp` (optional fields); `GameTables`
  DTOs for the new tables (`CarConfigRow`, `OrdersTable`, `MissionRow`, `ItemFaultRow`, `TuningRow`) and the
  `TableMeta` per-table schema version in `DatabaseMeta`.
- **Server:**
  - new `Data/GameLogic/`: `UnityRandom`, `GameTables` (load, versions, fallback), `GameLogicModes`, `GameLogicLedger`,
    `OrderRules`, `OrderClock`, `MissionRules` (part 1); `JobRules`, `CarValueRules`, `FeeRules` (part 2); the console
    command `gamelogic` and the self-test `--check-gamelogic` with synthetic fixtures;
  - changes in `JobsService` (server generation, seeds, timer resets, the election only as the fallback),
    `JobsSection` v2 (order clock), `EconomyRules` (CarSale, Welder, InteriorDetailing), `ParkingHandlers` (purchase
    checks), `OutdoorDigest` rows (+ `price`), `GameDatabase` (new tables, no fixed files), `ServerConfig` (`orders`,
    `job_payout`, `car_prices`);
  - `CMS21-Together-Server.csproj`: no committed game tables.
- **Client:**
  - new `Logic/GameData/` (exporter: `IDatabaseTable` and the tables, `GameDataExport`, the hosted-export step in
    `Session/LocalServerHost.cs`);
  - `Logic/Random/SeededStreams` (from `Outdoor/Reseed`); `JobHooks`/`JobsSync` (seeded take, part keys, flush
    before the end, special-case prefix);
  - `CatalogReporter` gains the order pool.
- **Harness:** `Features/GameLogicCommands.cs`:
  - `gl-random <seed> <n>` (part 1 spike), `gl-order-native <seed>`, `gl-orders-sample <n>`, `gl-jobcar-digest <loader>`
    (part 1);
  - `job-check <loader>` extended with the job totals, `gl-price <kind> <args>`, `gl-sellprice <loader>` (part 2);
  - `db-export <dir>` (moved from row 9).
- **Scenarios:** `gamelogic-random`, `gamelogic-orders`, `gamelogic-jobcar`, `gamelogic-export` (part 1);
  `gamelogic-payout`, `gamelogic-prices` (part 2); `jobs`, `jobs-latejoin`, `economy-trades` and `outdoor-junkyard`
  are updated.
- **Tools and docs:**
  - `tools/game-data/Export-GameData.ps1` (headless export from a test install, cached per game version);
    `tools/test-env` deploy copies the export into the server's `Database\`;
  - `tools/release/Build-Release.ps1` drops the tables, and `release-smoke` fails on any game table in a zip;
  - `docs/hosting.md` (where the data comes from), `CMS21-Together-Server/Database/README.md`.
- **Depends on** (all merged): rows 1, 3, 4, 9 part 1 (`meta.json`, game version), 10, 13, 15 (`Reseed`,
  `CarCatalog`, `SharedDlc`, digests), 18 (job end lock, `FlushNow`), 19 part 1 (part record merges) and 19 part 3 (D16
  answers).
  - Row 19 part 2's `jobs` digest key covers server-made orders unchanged.
  - Supersedes the opt-in generator client (QUESTIONS.md answer 11, not built): with server orders and seeded job cars,
    nothing is left for it to generate in the garage.
- **Size:** XL ≈ 16–17 sessions as one piece, so it is split by task groups.
  - **Part 1** (`server-game-logic`, groups 1–6 and 9): data, server RNG, orders, seeded job cars. L ≈ 9–10.
  - **Part 2** (`server-game-logic-2`, groups 7, 8 and 10): payout/XP, prices and fees. L ≈ 6–7.

## Open questions for the user

Each has the default the draft works with.

1. **Game tables in the repo and the release zips.** Today `CMS21-Together-Server/Database/item_database.json`
   (4.8 MB, inherited from upstream, with localized part names), the two upgrade tables and the release zip carry
   game data.
   - **Default:** remove them from the repo (from now on, git history is not rewritten) and from every zip.
   - Developers export at build/deploy time from their own install.
   - The hosting player's game exports before "Host" starts the server.
   - A dedicated-server owner copies an export made by the mod on a PC with the game.
   - The server's self-tests use small hand-written fixtures, not game data.
   - Alternative: keep the committed upstream files for development, only drop them from the zips.
2. **What "identical" means for orders.**
   - **Default:** value-for-value. The server's random source reproduces `UnityEngine.Random`, and for the same seed
     and inputs the server's order equals the one the game's own `GenerateNewJob` makes (spike 1.1).
   - If the spike shows the algorithm cannot be matched, orders are proven equal in distribution instead (car pool,
     categories and caps exactly; rolls by sample statistics), and that is accepted.
   - Payout and prices are deterministic and must always match value-for-value.
3. **Deliberate differences from single player** (design.md D7). **Default:** accept all four.
   - The native `GlobalData.Jobs` drift (it is counted before a car is found) is not copied.
   - The DLC release-day boost (30 % DLC car on an owned DLC's release day) is dropped.
   - The car pool is limited to cars every connected player can load, within the shared DLC set.
   - The tuning task's `tuningBonus` comes from a new optional part-record field if spike 1.3 cannot derive it.
4. **Orders while nobody is in the garage.**
   - **Default:** row 3's accepted rule stays (no new orders while nobody is in the garage; expiry runs while anyone
     is connected).
   - Spike 1.2 records what single player does while the player is away. If vanilla keeps generating, you can
     switch it on with `orders_while_away = True`.
5. **Car sale price** (part 2): what happens when the server's price differs from the price the sell window showed.
   - **Default:** the server's price is paid, and the seller sees "Sold for <amount>" with the paid amount.
   - Alternative: refuse the sale and let the seller reopen the window.
6. **Fees that stay client-computed:** fluid refill, part repair, crate cards, license plates, salon price and
   Work XP.
   - **Default:** yes, they stay as today (bounded). Each is small and the cost of moving it is large (D10).
   - Work XP could move later (1 XP per mounted or unmounted part, which the server sees in its part commits).
7. **Shadow first or straight to the server's value** (part 2).
   - **Default:** `job_payout` and `car_prices` default to `server` once their proof scenarios pass with zero
     mismatches.
   - `shadow` (the server computes and logs, the client's value is paid) stays as a setting for the first playtest
     if you want it.
8. **Split.** **Default:** two changes. Part 1 first (orders and seeded job cars remove elected-client logic),
   then part 2 (payout and prices remove client trust in money).
