# Design

## Context

See proposal.md for the why. Sources:

- the native decompiles under `%USERPROFILE%\CMS21-TestInstalls\native\out\` (`clean\`, `orders_clean\`,
  `orders2_clean\`, `economy*_clean\`, `outdoor*_clean\`); more come from `native/work/dec-queued.sh <targets> <outdir>`,
  with one `Class$$Method` per line matched against column 2 of `work/methods_sig.tsv`;
- `dump.cs` (Il2CppDumper);
- the spikes `docs/spikes/native-decompile.md` (§a payout, §b orders, §c prices, §e spawn rolls),
  `orders-and-jobs.md`, `economy-paths.md`, `generator-client.md` and `outdoor-runtime.md`;
- the code on `main` (`fbebf7d`).

### What the game computes today, and from what

| Piece | Native (RVA) | Inputs | Randomness |
|---|---|---|---|
| Order timer and cap | `OrderGenerator.Update` `0x180C5B920`, `GlobalData.GetMaxOrdersAmount` `0x180D7CD50` | First order 10 s after load, then one every 30 s of scaled time while `GlobalData.Jobs < max`. Max by `RealPlayerLevel`: 0–2 → 2, 3–4 → 3, 5–7 → 4, 8–11 → 5, 12–15 → 6, 16–19 → 7, ≥20 → 8. Accept (`<TakeJob>d__19` state 0) sets `orderTimer = 0, nextOrderTime = 30`; `CancelJob` of a taken job sets `orderTimer = 0` | none |
| Order | `GenerateNewJob` `0x180C5BB40` | Level, XP, difficulty; car pool (`CarBundleLoader.GetAllAvailableCars` filtered by installed DLC, `CarIsAvailableOnLevel` = config `other/allowedPlaces` contains `Order<lvl>` with lvl ≤ `PlayerLevel+1`, minus cars in open orders); the `ordersData` INI (categories and subtypes per level), `notAvailableForElectric`; config `year`, `allowedColors` (+ paint type), `engine/type` → engine INI `isElectric` | `UnityEngine.Random` only: DLC car (30 %, release day), car index, colour (51 % HSV, else an allowed colour), task count, categories, subtypes, `Additionals` variants, easy mode, `globalCondition`, mileage, bonus (30 %, 50/50 exp/money), `timeToEnd` 121–299 |
| Story mission | `GenerateMission` `0x180C5FDF0`, `GetMissionID` | `Missions/Mission{n}` TextAsset (car, config, colours, mileage, tasks), `MissionsFinished`, `MissionsAmount`, level | none |
| Job car preparation | `<TakeJob>d__19.MoveNext` `0x180A733E0` → `PrepareJob` `0x180C5D680`, `PreparePart`, `PrepareBodyParts`; `SetRandomColorPanels`, `SetRandomCarLivery`, `SetMountObjectsRandomCondition`, `SetRandomDent` (§e) | The loaded car's `PartScript`s and `CarPart`s, the item-fault table, `Physics.OverlapSphereNonAlloc` for radial faults, the shared upgrades (`wheelsAlignmentSystem`, `headlampAlignmentSystem`, `dyno`) | `UnityEngine.Random`, global state, across several frames |
| Payout and XP | `JobHelper.CheckJob` `0x18186E600`, `CheckJobTask` `0x18186EC10`, paid by `<EndJobCoroutine>d__139` `0x1808948D0` | Per job part: the `PartScript` whose `GetGameObjectPathWithoutRoot()` equals `JobPart.ID` (the **last** match in `partScriptCache` order wins; none found → `Done = Found = true` and a "Part not found" warning). Then `IsRepaired(globalCondition)`, `CarLoader.GetPartPrice`, `GameInventory.GetItemDifficultMod` (item-fault table, default 1.0); fluids (`GetAvgLevel`), both alignments' `IsCorrect`, `CalcTuningValue` (per part `(TuningProperty.value + tuningBonus) * Condition * (1 + Quality/10)`), `CalcTuningPartsValue`, `CarIsInFactoryColor`, body parts (`IsDefault`, `PartProperty(car-IDWithTuned).Price`), difficulty, `BonusToMoney`/`BonusToExp`. `JobPart.Found` is written: it is the part's `IsExamined` | none |
| Special case | `Inventory.TryAddSpecialCase` `0x180C7D1B0` | Mission → always; else the `luck` skill | `Range(0,101) < 25` |
| XP apply | `GlobalData.AddPlayerExp` `0x180D7BCC0` | ×2 on Expert, level loop | none (the server's `StatsHandlers.ApplyExp` already matches) |
| Item price | `Helper.GetPrice` (5 overloads), `GetTirePrice`, `GetRimPrice` | `PartProperty.Price`, special group, wheel sizes, condition, dent, quality | none (`PricingCalculator` matches, spike §c) |
| Car value and sale | `CarLoader.CalcCarValue` `0x180503B00`, `CalcBodyValue` `0x180502B20`, `CalcTuningCost`, `CarSummaryTab.SetupForSell` `0x180AE9120`, `CarHelper.GetMileagePriceMod`, `GetRestorationMod`, `GetPaintBonus`, `GetWashFactorBonus` | Every part's price, condition, quality and tuned id; body parts' condition, dent, colour and wash factor; wheel sizes; mileage; config `uniqueMod`; difficulty | none |
| Welder, interior detailing | `CarLoader.GetWelderCost` `0x1804C8480`; `GetInteriorDetailingCost` | `Convert.ToInt32((uniqueMod − 1) * 1000 + 500)`; `round((uniqueMod − 1) * 150 + 100)`, 0 with the car-wash upgrade | none |
| Outdoor car buy price | `SetupForBuy` `0x180AE7770` | `CalcCarValue` etc. of the generated car × `negotationMod` | the car's seeded generation (row 15) |
| Auction lot | `AuctionHelper.GetStartingPrice`, `GetPriceForRating` | config `logic/carValue`, rating | `Random.Range` (under the lot `Seed`) |
| Fluid refill, part repair, crate cards, plates | `FluidRefill.Hide`, `RepairPartWindow`, `UIHelper.SetCaseOpeningCardValue`, `ShopLicenseBuyWindow.BuyItem` | Levels before and after, the minigame result, level, plate count | crate cards: `Random.Range` |

### Code on `main` that owns these pieces today

- `Data/Jobs/JobsService.cs`:
  - the election (`Elect`, `OrderGeneratorRolePacket`) and `OnOrderGenerated`, which assigns `NextJobId`, refuses
    the tutorial mission and caps on the generator's `MaxOpenOrders`;
  - accept/decline/claim timeout, expiry in `Tick`;
  - `OnJobEnd`: bounds-checked client payout and XP, `StatsHandlers.ApplyExp`.
- `Data/Economy/EconomyRules.cs`: the reason table of row 10 (Fixed/Ranged/Trade); `ScrapFormulas.cs`;
  `PricingCalculator.cs`; `ParkingHandlers` (purchase bound `max_car_purchase_price`, parking level price).
- `Data/Outdoor/`: `CarCatalog` (intersection of the clients' reported catalogs, filtered by `SharedDlc`),
  `BasicCarSelector`, `OutdoorInstances` (seed per instance), `AuctionService` (lot values from the generator's
  report, `LastBid`).
- `Data/GameDatabase.cs`: loads the committed `Database/item_database.json`, `garage_upgrade_database.json` and
  `player_upgrade_database.json`, plus `modded_item_database.json` and an optional `meta.json`. No exporter exists:
  row 9's group 6 was never built.
- Client: `Logic/Jobs/JobHooks.cs` (Update/Generate prefixes, `EndJob` prefix with `CheckJob` refresh),
  `JobEndContext` (captures the payout), `Logic/Outdoor/Reseed.cs` (per-iterator random streams through the
  `get/set_state_Injected` icalls), `CatalogReporter`.

## Goals / Non-Goals

**Goals:**
- No game result depends on which client happened to be elected or happened to spawn a car.
- No money or XP amount that the server can compute is taken from a client.
- Every ported formula is proven against the game's own computation on a client, value for value where the game is
  deterministic (or seeded).
- The server never ships, and the repo never holds, game data; it reads tables exported from the user's own game.
- Every piece degrades to today's interim path when its tables are missing, so a server without an export still runs.

**Non-Goals:**
- Porting `PrepareJob`/`PreparePart`/`PrepareBodyParts`, `TakeMission` or any `SetRandom*` to the server. They need
  the scene's part hierarchy and physics. They run as the game's own code under the server's seed instead (D8).
- Porting the junkyard, barn or auction generators (row 15 seeds them; its non-goal stands).
- Server-side `EndJob` gate checks (missing bolts, body, oil, fluids, other parts' condition, wheel sizes): the native
  coroutine keeps running them on the finisher. They protect the player's car, not the money (vanilla pays an
  incomplete job too).
- Work XP (1 per part mount/unmount, `PartScript.<Hide>`/`<ShowMounted>`), crate card rolls, fluid refill and part
  repair amounts, license plates and the salon price (D10).
- Making the drag strip or "measure power" available (row 10 keeps them blocked).
- A headless generator client (superseded, see D6).

## Decisions

### D1. Per piece: move, seed, check or keep

| Piece | Decision | Why |
|---|---|---|
| Order list, timer, cap, missions | **Move** (part 1, D6) | Data-only (no car is loaded), about 16 small functions and 400–500 lines. Removes the election, the "nobody in the garage" gap and the generator's DLC pool |
| Job car damage, colour, dents | **Seed** (part 1, D8) | Bound to the scene (`PartScript`s, physics overlap); porting it would be L and could never be proven equal. A server seed makes the game's own code reproducible |
| Special case after a job | **Move** (part 2, D9) | One roll, and the server already adds inventory items |
| Payout and XP | **Move** (part 2, D9) | Deterministic. Its inputs are in the server's part records (row 1/19) and car details (row 4) |
| Item prices, travel fees, parking levels, scrap, skill reset | **Keep** (already server) | Already ported and checked by row 10 |
| Car sale price | **Move** (part 2, D10) | The biggest single amount (bound 5,000,000). Deterministic, and the inputs are in the records |
| Welder, interior detailing | **Move** (part 2, D10) | One exported number (`uniqueMod`) per car |
| Outdoor purchase, auction win | **Check** (part 2, D10) | The price comes from a seeded generation the server cannot run; the generator's digest and the server's last bid are independent witnesses |
| Fluid refill, part repair, crate cards, plates, salon price, Work XP | **Keep** client-computed and bounded (D10) | Small amounts, inputs only in the scene or UI (minigame result, card animation, configurator); moving each costs more than it protects |
| Outdoor car selection and rolls | **Keep** (row 15) | Already the server's picks and seed |
| `EndJob` gate checks | **Keep** on the client | Non-goal above |

*Alternative (all of it on a server-hosted generator client, QUESTIONS.md answer 11):* it needs the game, Steam
and 2.6 GB per instance on the server PC (`generator-client.md`). It inherits that account's DLC, and it cannot work on
a PC without the game. Ports plus seeds run anywhere the server runs.

### D2. Game data: exported on the user's machine, never redistributed

- **What the export holds.** Only the numbers and ids the server's ports need (D3). No meshes, textures, audio,
  TextAssets copied verbatim, or localized text. The existing item table drops `LocalizedName`, `Brand` and
  `ShopName`, which the server never reads (checked: no server code uses them).
- **Who runs it, and where it lands:**
  1. **Developers, at build/deploy time:** `tools/game-data/Export-GameData.ps1 [-Install A]` starts a test install
     headless (`-batchmode -nographics`, which the generator-client spike measured: garage in 14–20 s, 2.7 GB) with
     the harness verb `db-export`. The export goes to `%USERPROFILE%\CMS21-TestInstalls\gamedata\<gameVersion>-<exporterVersion>\`
     and is cached there. `tools/test-env` deploy copies it into the server output's `Database\`. The server project
     copies `Database\*.json` only from that local, git-ignored folder.
  2. **A hosting player:** `Session/LocalServerHost` runs the in-game export into `TogetherServer\Database\` before it
     starts the bundled server, when `meta.json` there is missing or names another game or exporter version. The
     export needs `GameInventory.PartPropertyListLoaded` and `CarBundleLoader` ready. Spike 1.4 confirms that both
     are ready in the main menu. If they are not, the host's first garage load exports and the server reloads its
     tables (`GameDataReady`, D14).
  3. **A dedicated server on a PC without the game:** the owner turns on `CMS21Together.EnableDevTools` and presses
     `CMS21Together.DbExportHotkey` (row 9's names, moved here) on a PC with the game. The export lands in
     `UserData\CMS21Together\ServerDatabase\` and is copied to the server's `Database\` (`docs/hosting.md`).
- **The export refuses:**
  - while connected;
  - when a gameplay mod is loaded (row 9's classifier, so modded items never enter the base tables);
  - when the game version is unknown.
- **Repo and release:**
  - the committed `Database/item_database.json`, `garage_upgrade_database.json` and `player_upgrade_database.json`
    leave the repo, and `Database/*.json` is git-ignored (open question 1). History is not rewritten;
  - `Build-Release.ps1` no longer lists them;
  - `release-smoke` fails if any zip holds a `Database/*.json` other than `mod_rules.json`.
  - The server's `--check-*` self-tests read hand-written fixtures under `CMS21-Together-Server/Tests/Fixtures/`
    (made-up ids and values).
- **Mod items:** `modded_item_database.json` (mod parts registered at runtime) stays a server-local file. It is never
  shipped.

*Alternatives:*
- **Keep the committed tables** — they are the game's data, published in a public repo and every release.
- **Read the game files from the server** (an asset parser such as AssetsTools.NET) — it needs the game on the server
  PC, a large dependency, and it parses the configs differently from the game.
- **Each connecting client uploads tables** — it trusts one client's tables, and every join sends megabytes. The
  runtime catalog (D7) keeps the small "can every player load this car" part of that idea.

### D3. Tables

All tables are written by `IDatabaseTable { FileName; SchemaVersion; object Export(); }` implementations (row 9's
D8 interface) in `Logic/GameData/`. They are read by `GameTables` on the server.

| File | Content | Source in the game | Part |
|---|---|---|---|
| `item_database.json` | as today minus the text fields | `GameInventory.PartPropertyList` | 1 |
| `garage_upgrade_database.json`, `player_upgrade_database.json` | as today | row 9 task 6.1 finds the sources (moved here) | 1 |
| `cars.json` | per car id and config: list index in `GetAllAvailableCars` order, DLC id, `year`, `allowedPlaces` tokens, `allowedColors` (rgba + paint type), `engine/type` and its `isElectric`, `uniqueMod`, `carValue`, `rarity`, `globalCondition`/`partsConditions`/`panelsConditions` | `CarBundleLoader`, the car config INIs through the game's own parser, `GameInventory` engine data | 1 |
| `orders.json` | `AviableCategorysOnLevel`, `AviableSubtypesOnLevel`, `notAvailableForElectric`, `c_typeName` | `OrderGenerator.ordersDataINI` and the constructor's arrays | 1 |
| `missions.json` | per mission id: the fields `GenerateMission` puts into the `Job` (car, config, colour and paint type, mileage, tasks, localization id), plus `MissionsAmount` | `Missions/Mission{n}` TextAssets through the game's INI parser | 1 |
| `item_faults.json` | id → `ItemFault` (difficulty mod and the fields `PreparePart` reads) | `GameInventory.itemFaults` | 2 |
| `tuning.json` | id → `GetTuningProperty` value | `GameInventory` | 2 |
| `meta.json` | `GameVersion`, `ExporterVersion`, `ExportedAtUtc`, per table `{ File, SchemaVersion, Rows, Sha256 }` | — | 1 |

Constants that are code, not data, stay in the ports with their RVA cited in the commit message. Examples: the order
cap table, the easy-mode chances, the task-count bands, the bonus factors.

### D4. The server's random source reproduces `UnityEngine.Random`

- The game's `UnityEngine.Random` is a Xorshift128 generator: the 16-byte state that row 15's `Reseed` reads through
  `get_state_Injected` is its four words. `Data/GameLogic/UnityRandom` ports `InitState(int)`, the next-word
  step, `Range(int, int)` (max exclusive), `Range(float, float)` (inclusive), `value` and `ColorHSV`, the way the
  community has described them.
- **Spike 1.1 checks it:** `gl-random <seed> <n>` on a client draws a fixed mix of calls after `Random.InitState(seed)`,
  and the server's `gamelogic random <seed> <n>` does the same. Five seeds × 10,000 draws must be equal.
- **When the spike passes:**
  - every server roll comes from a `UnityRandom` stream seeded per order (`OrderSeed`, drawn from the server's
    session stream);
  - a client can replay any order: `Reseed.WithSeed(OrderSeed, GenerateNewJob)` with the same inputs must produce
    the same `Job`. That is the order proof (D12).
- **When the spike fails:** the port uses `System.Random` with the same call order and distributions. The order proof
  becomes exact for the pool, categories and caps, and a sample comparison for the rolls (open question 2).

### D5. Modes, fallback and the ledger

- Server settings, appended with defaults (INTEGRATION.md config table):
  - `orders = server|client` (default `server`), part 1;
  - `orders_while_away = False` (open question 4), part 1;
  - `job_payout = server|shadow|client`, part 2;
  - `car_prices = server|shadow|client`, part 2. It covers the car sale price, welder, interior detailing and the
    purchase checks.
- What the modes mean:
  - `client` is today's interim (election, client payout, client price within bounds);
  - `shadow` computes on the server, logs the difference and applies the client's value;
  - `server` applies the server's value.
  - The seed (D8) needs no mode: the server assigns `PrepSeed` to every order, whoever generated it.
- **Fallback:** a piece whose tables are missing, have an older `SchemaVersion`, or were exported from another game
  version than the pinned one (row 9) runs as `client`. It logs one warning at start
  (`[GameLogic] orders: cars.json missing, falling back to the elected generator`), and `gamelogic` shows it.
- **`GameLogicLedger`** (runtime, not saved): per piece, the counts `computed`, `equal`, `different`, `fallback`, plus
  the last 50 differences with both values and the inputs' key (job id, loader, item uid). The console command
  `gamelogic` prints it. The scenarios assert `different = 0`.
- Test commands on the same console:
  - `gamelogic eval <piece> <args>` computes on demand for the harness;
  - `gamelogic random <seed> <n>` serves spike 1.1;
  - `gamelogic order-add <orderSeed>` adds the order that seed generates;
  - `gamelogic order-copy <jobId>` adds an open copy of a job with its `PrepSeed`.

### D6. Orders are generated by the server (part 1)

- `OrderClock` (in `JobsService.Tick`, under `StateLock`, real seconds; the pause menu does not pause a session,
  accepted default) ports `Update`:
  - `orderTimer` grows while the open count (`Open` + `Claimed` orders, as native `jobs` still holds a claimed order
    until the take's `CancelJob`) is below `GetMaxOrdersAmount(Level + 1)`;
  - the clock runs only while at least one `InSession` player is in the garage, unless `orders_while_away`;
  - past `NextOrderTime` it calls `OrderRules.Generate`, then `orderTimer = 0`, `NextOrderTime = 30`;
  - an approved accept sets `orderTimer = 0`, `NextOrderTime = 30`;
  - an ended job sets `orderTimer = 0`;
  - a session without a saved clock starts at `NextOrderTime = 10`.
- `OrderRules.Generate` ports `GenerateNewJob` in its native call order, so D4's exact proof holds:
  - `forXP` (`GetExperience`) and the car pool (D7);
  - `isElectric`, then `GetAvailableCategories(level, isElectric)`;
  - colour (`GetRandomColorHSV` or an allowed colour), task count, categories and subtypes (`GetTasksAmount`,
    `GetMaxJobSubTypes`), `Additionals` variants;
  - easy mode: Easy → always, Expert → never, else `GetChanceToEasyMode`;
  - `globalCondition` (`CarHelper.GetGlobalCondition(year)`), mileage (`GetRandomMileage(year, 4)`), bonus,
    `timeToEnd`;
  - the order gets `id = NextJobId++` and `CanDelete = true`, plus `OrderSeed` (runtime only) and `PrepSeed` (D8).
  - The XMM float bounds that Ghidra dropped are read from `asm_GenerateNewJob.txt` with `f32.py` (task 1.3) before
    the port.
- `MissionRules`:
  - when `CurrentMissionDone` and `GetMissionID() >= 0`, the server builds the mission `Job` from `missions.json`
    (`IsMission`, `MissionID`, no timer, `CanDelete = false`);
  - tutorial missions are never generated;
  - row 3's refusal of `IsMission && MissionID == 0` assumed that mission 0 is the tutorial. Spike 1.2 checks whether
    story mission 0 is `Missions/Mission0`. If it is, the server marks tutorial jobs by origin instead, so the first
    story mission is offered.
- Broadcast and apply are unchanged: `OrderAdded` to everyone. Clients keep blocking `OrderGenerator.Update` and
  `GenerateMission` (row 3's prefixes). In `server` mode `JobsService.Elect` is not called and no
  `OrderGeneratorRole { IsGenerator = true }` is sent; `OnOrderGenerated` answers any `OrderGenerated` with the jobs
  state (no silent drop, row 19 D16).
- **Stores:** `JobsState` as today plus `OrderClock { OrderTimer, NextOrderTime }`, as `JobsSection` v2 (vanilla saves
  both in `NewJobsData`). A v1 save loads with `NextOrderTime = 10`.
- **Relays:** nothing new.
- **Late join:** unchanged. `JobsStatePacket` carries the orders (with `PrepSeed`); the clock is server-only.
- *The election stays* as the `client` fallback (D5), so a server without `cars.json`/`orders.json` still gets orders.

### D7. The order car pool, and the deliberate differences

- **Pool** = `cars.json` configs whose `allowedPlaces` admit `Order` at `Level + 1` (`Helper.ParseAllowedPlacesLevel`
  port), **∩** the runtime order catalog (`CatalogReporter` adds an `Order` list: the client's
  `GetAllAvailableCars()` config keys; `CarCatalog` already intersects over connected clients), **∩** `SharedDlc`, **−**
  cars in open orders. The order of the list is `cars.json`'s list index, which is the game's own order. A pool that
  differs from a client's native pool (another DLC set) is logged with the difference.
- Deliberate differences from single player (open question 3):
  1. `GlobalData.AddJob(1)` runs before a car is found natively, so the count drifts when the pool is empty. The
     server counts its list.
  2. `Helper.CanGenerateDLCCar` (30 % DLC car on an owned DLC's release day) is dropped. DLC cars in the shared set
     are part of the uniform pool, as natively outside the release day.
  3. The pool leaves out cars that some connected player cannot load.
  4. Part 2: if spike 1.3 finds `PartScript.tuningBonus` is not derivable from the item and quality, the sub-part
     record gains an optional `TuningBonus`.
- Effect on the exact proof: a client replaying an order uses its own native pool. So the proof scenario runs two
  clients with equal installs, where both pools are equal (`gamelogic-orders` asserts that first).

### D8. Job cars are prepared from the server's seed (part 1)

- Every order gets `PrepSeed` (int from the session stream), stored in the `Job` (`ModJob.PrepSeed`, optional field),
  saved, and kept when a lost car reopens the order (row 3 D12). The elected fallback's orders get one too.
- Client: `Logic/Random/SeededStreams` generalises `Reseed`: `Begin(iterator, seed, kind, index)`/`End`, with a
  stream per iterator, swapped in by a `MoveNext` prefix and out by a finalizer. It is active when its owner's
  predicate says so. Outdoor keeps its use unchanged.
  - `OrderGenerator.<TakeJob>d__19.MoveNext`, for the job being taken: the fluid roll, `PrepareJob` and the colour,
    livery, bolt and dent rolls all run inside one stream seeded from `StreamSeed(PrepSeed, "job", 0)`.
  - `CarLoader.<SetRandomColorPanels>d__321.MoveNext` for that loader: its own stream (`"job-panels"`), as row 15
    found for outdoor cars.
  - `<TakeMission>d__22.MoveNext`: a stream too. The mission INI is fixed, but the take may still draw.
  - `CarLoader.LoadCar` is not wrapped unless spike 1.5 finds a draw in it.
- The baseline upload after `PrepareJob` (row 3 D5, row 1 D6) is unchanged. The baseline is still what every client
  applies, so late joiners and other players need no replay.
- **What changes for the player:** an aborted take, a claim timeout or a lost car gives the same car on the next take,
  so re-taking no longer re-rolls a hard job into an easy one. Two players taking the same order one after the other
  get the same car.
- `Physics.OverlapSphereNonAlloc` (radial faults) depends on positions. Within one car loader, the car's parts keep
  their relative positions, and neighbouring cars sit further away than the sphere's radius. Spike 1.5 compares a
  take on loader 1 and loader 2. If they differ, the radial step is a known limit: the baseline still wins, and the
  digest proof runs on the same loader.
- **Special case** (part 2, D9): `Inventory.TryAddSpecialCase` is skipped while connected in `job_payout = server`. The
  server rolls `Range(0,101) < 25` on the order's stream when the shared `luck` skill is unlocked (always for a
  mission) and adds the `specialCase` item to the shared inventory with an `InventoryItemAction Add` to everyone.

### D9. Payout and XP on the server (part 2)

- **Part keys:** at `JobStarted` (after `PrepareJob`), the taker resolves every `JobPart.ID` with the game's own
  rule (the last `PartScript` in `partScriptCache` order whose `GetGameObjectPathWithoutRoot()` equals the id). It sends
  the part's key (`s:<index path>`, row 1) in `ModJobPart.Key`; no match gives `null` (the server then copies the
  native "Part not found" result). The `Engine/Oil` dummy part and the body task (per body part) need no key: the
  server derives them from the details and the body records.
- `JobRules.Check(job, entry, details)` ports `CheckJob`/`CheckJobTask` over:
  - the server's `CarLoaderEntry` (sub-part records: `PartId`, `TunedID`, `Condition`, `Quality`, `Unmounted`,
    `IsExamined`; body records: `Unmounted`, `TunedID`, `Switched`, `State.Condition`/`Dent`/`Color`);
  - `ModCarDetails` (fluids, wheels, alignment, paint, tuning);
  - the tables (`item_database`, `item_faults`, `tuning`, `cars.json`'s `uniqueMod`) and `WorldState.Gamemode`.
  - Ported as they are: the running-total task bonus, Expert's halving of the running `TaskBonus` after each task,
    the `JobBonus` ×1.5 on Expert, the +25 % XP on completion, the `BonusToExp` ×1.25, and "paid even when not
    completed".
  - `GetPartPrice` and `Helper.GetPrice(PartScript)` (`Math.Round(Price*Condition*(1+q*0.02) + q)`, tuned id first;
    tires and rims from `CarLoader.GetWheelSize`/`GetET` with ×`(1 + Quality*4*0.02)`) join `PricingCalculator`.
- **The finisher's side:** the `GameScript.EndJob` prefix calls `CarDetailsSync.FlushNow(loader)` before `CheckJob`,
  so fluid levels reach the server before the `JobEndRequest` on the same reliable channel. Part commits already do.
  Row 18 refuses a job end while another player holds a lock, so the car is quiet while the server reads it.
- **Server, `OnJobEnd`:**
  - `server`: pay `JobRules` (money with vanilla's clamp, XP via `ApplyExp`) and record the client's numbers in the
    ledger;
  - `shadow`: pay the client's numbers and record the server's;
  - `client`: today's path.
  - `JobRemoved { Ended }` gains `Payout`, `Xp`. The finisher shows "Paid <amount>" when it differs from its
    prediction, and `WorldState` corrects the money (row 19 D16: no silent override).
  - If a job part's key points to a record the server does not have, the result is `fallback` for that job: the
    client's value is paid, and the ledger names the part.
- *Alternative (keep the client value, only validate):* a bound or a plausibility check cannot catch a stale
  `CheckJob`, which is exactly the multiplayer failure (another player repaired a part after the order tab opened).

### D10. Prices and fees (part 2)

`EconomyRules` reasons, per `car_prices` mode (`client` keeps today's rule):

| Reason | `server` | Inputs |
|---|---|---|
| `CarSale` (garage) | `CarValueRules.SellPrice(entry, details)` ports `SetupForSell`: `carValue = (int)(CalcCarValue*u) + (int)(CalcBodyValue*u)` (body ×u twice, as native), tuning `(int)(CalcTuningCost*u)`, `gc = clamp01((Parts+Panels)/2)`, mileage factor, restoration mod (×0.5 on Expert), paint and wash bonuses (`u*300` when their conditions hold) | records, details, `cars.json` `uniqueMod`, item table |
| `CarSale` (parking) | the same from the parked car's server records (row 19 gap 10 keeps them) | as above |
| `Welder` | `Fixed(−Convert.ToInt32((u − 1) * 1000 + 500))` for the loader's car | `uniqueMod` |
| `InteriorDetailing` | `Fixed(−round((u − 1) * 150 + 100))`, `Invalid` with the car wash (as today) | `uniqueMod`, garage upgrades |
| car purchase, outdoor (`CarParkRequest`, `CarLoaderID = -1`) | equal to the `price` field of the instance's reference digest row for that car (row 15 digest + `price`, written by the client after `SetupForBuy`) | `OutdoorInstance.Digest` |
| car purchase, auction win | equal to the server's `LastBid` amount for the lot | `AuctionService` |
| fluid refill, part repair, crate cards, plates, salon | **unchanged** (ranged/bounded) | — |

- Why the last row stays:
  - refill amounts are `(newLevel − start) * 20`, worth cents, and coupled to the poll timing of row 4;
  - repair costs come from the minigame result on the client, already bounded by `PricingCalculator`;
  - crate cards are rolled for the window's animation, already range-checked by level;
  - plates and the salon depend on UI state.
- The sale answer: the server pays its price. `EconomyResult.Money` carries it, and the seller sees "Sold for
  <amount>" when it differs from the window's price (open question 5).
- A purchase whose price the server cannot check (no digest yet, a local visit, an unknown lot) keeps today's bound
  and logs `fallback`.

### D11. Ported arithmetic

- Ports keep `float` where the game uses `float`, in the native order of operations, and the native conversion:
  - `(int)` truncates;
  - `Mathf.RoundToInt` and `Convert.ToInt32` round half to even;
  - `Mathf.FloorToInt`.
- The .NET Framework x64 JIT evaluates `float` in single precision (SSE), as IL2CPP's MSVC build does, so equal
  operations give equal bits. The proofs compare exact integers, never with a tolerance.

### D12. Proof: the game's own computation next to the server's

Each piece has a harness verb that runs the native method on a client, a server command that runs the port on the
same inputs, and a scenario that compares the two.

| Piece | Client (native) | Server | Comparison |
|---|---|---|---|
| RNG (spike 1.1) | `gl-random <seed> <n>` | `gamelogic random <seed> <n>` | equal draw lists |
| Order | `gl-order-native <seed>`: `Reseed.WithSeed(seed, () => GenerateNewJob())`, capture the new job, remove it again (and undo `AddJob`) | `gamelogic eval order <seed>` with the same level, XP, difficulty and open orders | equal `ModJob` JSON (id excluded), 200 seeds × three levels × three difficulties; the pool (D7) equal first |
| Order statistics (fallback) | `gl-orders-sample <n>` | `--check-gamelogic orders <n>` | pool and categories exact; rolls within sample bounds |
| Job car | take order X on A → `gl-jobcar-digest`; end X; `gamelogic order-copy X`; take the copy on B, same loader | server part-record digest per loader (row 14) | equal digests; another seed differs |
| Payout | `job-check <loader>` (row 3's verb, extended: `JobHelper.CheckJob` on a copy of the job, all `Job` totals and per-task `Done`/`moneySpent`) | `gamelogic eval payout <jobId>` | equal at every checkpoint of a scripted repair, on Easy, Normal and Expert |
| Car sale | `gl-sellprice <loader>` (the `SetupForSell` values without opening the window) | `gamelogic eval sellprice <loader>` | equal for five cars in different states |
| Fees and prices | `gl-price welder|detailing|item <args>` | `gamelogic eval …` | equal |

Regular scenarios (`jobs`, `economy-trades`, `outdoor-junkyard`) also assert `gamelogic` `different = 0` once their
piece is on `server`.

### D13. What stays on the client, and why that is not elected logic

- **Seeded native code** (D8): every client would compute the same result, so whichever client takes the job no longer
  matters.
- **The `EndJob` gates**, the `TakeMission` application and the outdoor generators (row 15): they protect the
  player's view or run under a server seed.
- **The bounded values** of D10.

### D14. Packets and hooks

- `ModJob.PrepSeed` (part 1), `ModJobPart.Key`, `JobRemovedPacket.Payout`, `.Xp` (part 2): `[OptionalField]`, so an
  old save and an old client still deserialize.
- `GameDataReady`: only if spike 1.4 shows the hosted export must wait for the garage (appended to `PacketTypes`; the
  host's own client tells its local server to reload `GameTables`).
- `OrderGenerated` and `OrderGeneratorRole` keep their values (append-only) and are used only by the `client` fallback.
- **Hooks:**
  - `<TakeJob>d__19.MoveNext`, `<TakeMission>d__22.MoveNext`, `<SetRandomColorPanels>d__321.MoveNext` (prefix and
    finalizer, part 1);
  - `GameScript.EndJob` (the existing prefix gains `FlushNow`, part 2);
  - `Inventory.TryAddSpecialCase` (prefix, part 2).
  - The exporter only reads.

### D15. Server restart and late join

- Orders, `PrepSeed` and the order clock are saved (`jobs` v2). The session stream is reseeded from the clock on start;
  only new orders use it.
- The ledger is runtime only.
- A late joiner gets orders, active jobs and baselines as today; nothing it receives depends on its own roll.
- A client that reconnects during a take: row 3's claim timeout reopens the order, and the next take uses the same
  `PrepSeed`.

### D16. Split

- **Part 1 (`server-game-logic`, tasks groups 1–6 and 9):** spikes, the exporter and data policy, the server
  foundation (`UnityRandom`, tables, modes, ledger), server orders and missions, the seeded job car, part 1's
  scenarios and docs. It removes the elected generator and the spawner's roll.
- **Part 2 (`server-game-logic-2`, groups 7, 8 and 10):** payout/XP and the special case; prices and fees; their
  scenarios. It removes client trust in money.
- Part 2 starts after part 1 merges (it needs the tables, the ledger and `PrepSeed`).

## Risks / Trade-offs

- [The RNG port does not match Unity bit for bit] → spike 1.1 decides before any port. The fallback is the
  distribution proof (open question 2). Payout and prices do not depend on it.
- [The native pool order differs from `cars.json`'s list index] → the export records `GetAllAvailableCars()` order
  directly, and `gamelogic-orders` compares the pools first.
- [A server record is missing or stale at job end] → `fallback` per job (client value, ledger entry). Rows 18/19
  quieten the car before the end, and `FlushNow` orders the details.
- [Physics overlap differs between loaders] → spike 1.5. The baseline stays authoritative either way.
- [The hosted export needs the garage] → spike 1.4. `GameDataReady` hot reload is the fallback, and the first
  session's orders use the election until the tables arrive.
- [Removing the committed tables breaks a fresh clone's server] → the server starts without tables. Every piece falls
  back (D5), and the item table's absence keeps today's fallback pricing with a warning. `tools/test-env` setup runs
  the export once.
- [Float differences between the JIT and IL2CPP] → D11. The exact comparisons in D12 would show any, and a port then
  mirrors the native operation (for example `(float)` casts of intermediates) rather than switching to a tolerance.
- [Part 2 money moves with real players] → `shadow` mode for one playtest is one setting away (open question 7).

## Migration Plan

- `jobs` section v1 → v2: adds `OrderClock` (`NextOrderTime = 10`, `OrderTimer = 0`). Orders without `PrepSeed` get
  one on load.
- New server settings are appended with defaults. An existing server without an export runs in fallback with a warning
  and keeps working.
- Rollback: revert the change. A v2 `jobs` section is read by row 7's unknown-version path as raw JSON, so an older
  server keeps the orders.

## Open Questions

See proposal.md "Open questions for the user" (8 questions, each with a default). Spikes that may change a decision
(each updates this file in its commit):

- 1.1 RNG exactness (D4);
- 1.2 vanilla orders while away and mission 0 (D6);
- 1.3 float bounds, `ColorHSV` order, the alignment `IsCorrect` thresholds, `CarPart.IsDefault`,
  `PartScript.tuningBonus`, `GetPartsGlobalCondition`/`GetPanelsGlobalCondition`, `CalcTuningCost` (D6, D9, D10);
- 1.4 when the exporter's sources are ready (D2);
- 1.5 draws in `LoadCar` and the overlap per loader (D8).
