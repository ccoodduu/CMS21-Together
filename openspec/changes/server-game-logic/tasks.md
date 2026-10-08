# Tasks

**Row 16, split in two (design.md D16):**

- **Part 1** (`server-game-logic`, this change): groups 1–6 and 9. L ≈ 9–10 sessions.
- **Part 2** (`server-game-logic-2`): groups 7, 8 and 10. L ≈ 6–7 sessions. When part 1 merges, these groups move
  into their own change folder (`openspec/changes/server-game-logic-2/`) with the part-2 parts of design.md and of
  `specs/server-payout-and-prices`.

Rules:

- **Prerequisites** (all on `main`): rows 1, 3, 4, 9 part 1, 10, 13, 15, 18, 19 parts 1 and 3.
- **Spikes:** a spike that changes a decision updates design.md in the same commit. Findings go to
  `docs/spikes/server-game-logic.md`. Native reads use `native/work/dec-queued.sh <targets> <outdir>` (one
  `Class$$Method` per line, matched against column 2 of `work/methods_sig.tsv`), with output in
  `native/out/gamelogic_clean/`.
- **Proof:** every port is compared with the game's own computation on a client (design.md D12). A scenario counts
  only when the server's `gamelogic` command shows `different = 0` for its piece.
- **Scenarios:** each carries `# areas: gamelogic, …`.
- **Commits:** one conventional commit per finished task group.

## 1. Spikes (part 1)

- [ ] 1.1 **RNG exactness.**
      - Build `Data/GameLogic/UnityRandom` (Xorshift128: `InitState`, next, `Range(int,int)`, `Range(float,float)`,
        `value`, `ColorHSV`).
      - Add the harness verb `gl-random <seed> <n>`: a fixed mix of calls after `Random.InitState(seed)` inside
        `Reseed.WithSeed`, written to the dump.
      - Add the server command `gamelogic random <seed> <n>`.

      Done when five seeds × 10,000 draws are equal (scenario `gamelogic-random`, one instance), or the first
      difference is recorded and design.md D4 switches to the fallback (open question 2).
- [ ] 1.2 **Vanilla orders while away, and mission 0.**
      - Static: does `OrderGenerator` live on a persistent `GameManager`; do `GameSettings.CanGenerateOrders`,
        `NotificationCenter.IsGameReady` and the `Job.<Timer>` host (`UIManager`) exist in the junkyard scene?
      - Runtime, single player on a test install: note the order count and the timers, travel to the junkyard for
        3 minutes, return.
      - Read `GetMissionID` and `GenerateMission`: is `Missions/Mission0` a story mission with `MissionID == 0`, and
        what marks the tutorial mission?

      Done when design.md D6 states the vanilla behaviour (open question 4's default stays unless you change it) and
      how the server tells the tutorial from story mission 0.
- [ ] 1.3 **Remaining native reads.**
      - Read the float bounds `GenerateNewJob` and `PrepareJob` pass in XMM registers, with
        `out/asm_GenerateNewJob.txt` and `f32.py`.
      - Read `OrderGenerator.GetRandomColorHSV` (the call order inside `Random.ColorHSV`), `GetExperience`,
        `GetTasksAmount`, `GetMaxJobSubTypes`, `Helper.ParseAllowedPlacesLevel`, `CarBundleLoader.CarIsAvailableOnLevel`
        and `CarHelper.GetGlobalCondition`.
      - Read `HeadlampAlignment.IsCorrect`, `WheelsAlignment.IsCorrect`, `CarPart.IsDefault` (what sets it),
        `PartScript.tuningBonus` (writers), `CarLoader.GetPartsGlobalCondition`/`GetPanelsGlobalCondition`,
        `CalcTuningCost`, `CarIsInFactoryColor`, `FluidsData.GetAvgLevel`, `CarLoader.GetInteriorDetailingCost` and
        the conditions of `CarHelper.GetPaintBonus`/`GetWashFactorBonus`.

      Done when each has a pseudo-code block in `docs/spikes/server-game-logic.md` with its RVA, and design.md D7/D9/D10
      name any input the server's records lack (with the field to add, or the piece's fallback).
- [ ] 1.4 **When the exporter's sources are ready.**
      - In the main menu and in the garage, log the readiness of `GameInventory.PartPropertyListLoaded`,
        `CarBundleLoader` (`GetAllAvailableCars` count), the car config parser, `OrderGenerator.ordersDataINI` and the
        `Missions/Mission{n}` TextAssets.
      - Do this on a normal launch and on a headless one (`-batchmode -nographics`).

      Done when design.md D2 says where the hosted export runs (menu, or garage plus `GameDataReady`), and
      `Export-GameData.ps1`'s wait condition is known.
- [ ] 1.5 **Draws during a job take.**
      - Log the Unity random state at each `<TakeJob>d__19` state transition and around `CarLoader.LoadCar` with the
        `lock-trace`-style logging verb `gl-trace on|off|report`.
      - Take the same order (same seed, by hand-forced `Reseed`) on loader 1 and on loader 2, and compare the
        part-record digests.

      Done when design.md D8 lists every iterator that draws, and states whether the radial-fault overlap is equal
      across loaders.

## 2. Game data: exporter and no redistribution (part 1; row 9 group 6 moves here)

- [ ] 2.1 Row 9 task 6.1: find the sources of the garage-upgrade and player-upgrade tables and map them to the
      current files. Done when design.md D3's table names the source of each.
- [ ] 2.2 `Logic/GameData/`: `IDatabaseTable { FileName; SchemaVersion; Export() }`, `GameDataExport.Run(dir)` and the
      tables of design.md D3 for part 1 (`item_database` without its text fields, the two upgrade tables, `cars`,
      `orders`, `missions`, `meta`).
      - The export refuses while connected, with a gameplay mod loaded, or with an unknown game version.
      - Harness verb `db-export <dir>`. `CMS21Together.EnableDevTools` binds `CMS21Together.DbExportHotkey` (default
        unbound) and writes to `UserData\CMS21Together\ServerDatabase\`.

      Verify on lane 1: an export on A equals the committed tables in every field the server reads (old files kept
      aside for this one diff, `tools/test-env/Compare-Database.ps1`). `cars.json` has as many configs as row 15's
      junkyard + barn + order catalogs, and `missions.json` has `MissionsAmount` rows.
- [ ] 2.3 `tools/game-data/Export-GameData.ps1 [-Install A]`: headless start with the harness, `db-export`, quit;
      cache per `<gameVersion>-<exporterVersion>` under `%USERPROFILE%\CMS21-TestInstalls\gamedata\`. The `tools/test-env`
      deploy copies the cache into the server's `Database\` and runs the export when the cache is missing. Verify a
      clean cache: one deploy exports once and the server logs `Database exported from game …` with every table; a
      second deploy reuses the cache.
- [ ] 2.4 **No redistribution:**
      - remove the three committed tables and git-ignore `CMS21-Together-Server/Database/*.json`;
      - make the csproj copy `Database\*.json` only when present;
      - drop the tables from `Build-Release.ps1`, and make `release-smoke` fail on any game table in a zip;
      - add `CMS21-Together-Server/Tests/Fixtures/` (hand-written, made-up ids) for the self-tests.

      Verify: `Build-Release.ps1` builds both zips, `release-smoke` passes, and an injected
      `Database/item_database.json` makes it fail. A fresh server without `Database\` starts, logs one warning per
      piece and runs `connect`.
- [ ] 2.5 Hosted export. `Session/LocalServerHost` runs `GameDataExport.Run(TogetherServer\Database)` before
      starting the server when `meta.json` is missing or stale. If spike 1.4 found the garage is needed, the host's
      first garage load exports and sends `GameDataReady`, and the server reloads `GameTables`.

      Verify with the harness `mp-host` path on lane 1: delete `TogetherServer\Database`, host, and check that the
      server log shows the tables loaded and `gamelogic` shows no fallback for orders.
- [ ] 2.6 `CMS21-Together-Server/Database/README.md` and `docs/hosting.md` "Where the server's game data comes from":
      the three paths of design.md D2, that nothing is shipped, and what a missing table falls back to. Verify that the
      documented commands run as written.

## 3. Server foundation (part 1)

- [ ] 3.1 `Data/GameLogic/GameTables`:
      - loads the D3 tables with schema versions and checks `meta.json` against the pinned game version (row 9);
      - exposes `Has(piece)`;
      - `GameDatabase` delegates the item and upgrade tables to it.

      Verify with `--check-gamelogic tables` on the fixtures (missing, old schema, wrong version → the right fallback
      per piece).
- [ ] 3.2 `GameLogicModes` (`orders`, `orders_while_away`; `job_payout`, `car_prices` reserved for part 2) in
      `ServerConfig`, appended with defaults, printed in the start line. `GameLogicLedger` and the console command
      `gamelogic` (`gamelogic`, `gamelogic eval <piece> <args>`, `gamelogic random`). Verify that an old
      `server_config.ini` gains the keys, and that `gamelogic` prints the modes and the zero counts.
- [ ] 3.3 Harness area `gamelogic`: the dump section `gamelogic` (the client's last native results); the helper
      `Assert-GameLogicEqual` in `tools/test-env` (compares a harness dump value with the server command's output and
      the ledger). Verify with `gl-random` against `gamelogic random` (task 1.1's scenario uses it).

## 4. Orders on the server (part 1)

- [ ] 4.1 `OrderRules` (port of `GenerateNewJob` and its helpers, design.md D6) and `MissionRules` (D6), on
      `UnityRandom` order streams.
      - Pool per D7, with `CatalogReporter` adding the `Order` list and `CarCatalog` intersecting it.
      - `--check-gamelogic orders` on the fixtures: the cap table, easy-mode bands, task-count bands, the electric
        filter, the mission sequence, and a fixed seed giving a fixed order.
- [ ] 4.2 `OrderClock` in `JobsService.Tick` (D6: 10 s first, 30 s, the cap, reset on accept and on job end,
      `orders_while_away`); `JobsSection` v2. In `orders = server`, no election is made and `OnOrderGenerated` answers
      with the jobs state.
      - Verify with `--check-jobs` (v1 → v2 load), and on lane 1 with the `jobs` scenario changed:
        - two clients in the garage see the first order about 10 s after the session starts, then one per 30 s up to
          the cap for the level;
        - with both players in the junkyard (`travel`), no order appears;
        - after the return, orders resume;
        - the server log shows no election.
- [ ] 4.3 Fallback: with `cars.json` removed, the server elects as today, logs the fallback, and `gamelogic` shows
      `orders: client (cars.json missing)`. Verify with the same `jobs` scenario under the `-ServerDatabase` switch
      (`Start-TestServer` deploys a database without `cars.json`).
- [ ] 4.4 **Proof:** scenario `gamelogic-orders` (two instances, equal installs).
      1. A and B report equal native order pools, and the server pool equals them.
      2. For 200 seeds at levels 1, 6 and 12 (`stats-add` sets the shared level and XP) on Easy, Normal and Expert
         (`gamemode`), B's `gl-order-native <seed>` equals `gamelogic eval order <seed>` (job JSON without `id`).
      3. If spike 1.1 fell back: `gl-orders-sample 2000` against `--check-gamelogic orders 2000` within the bounds of
         design.md D12.
      4. The ledger shows `different = 0`.

## 5. Seeded job cars (part 1)

- [ ] 5.1 `Logic/Random/SeededStreams` (from `Logic/Outdoor/Reseed`, with outdoor's behaviour unchanged).
      `outdoor-junkyard` and `outdoor-barn` still pass with equal digests.
- [ ] 5.2 `ModJob.PrepSeed` (optional field). The server assigns it to every order (server-made, elected fallback,
      missions) and keeps it when an order reopens. Streams are wrapped around `<TakeJob>d__19.MoveNext`,
      `<TakeMission>d__22.MoveNext` and `<SetRandomColorPanels>d__321.MoveNext` for the job's loader (plus any
      iterator spike 1.5 found). Verify with `--check-jobs` (`PrepSeed` survives BinaryFormatter and JSON) and a
      `jobs` run where the take's log shows the stream seed.
- [ ] 5.3 **Proof:** scenario `gamelogic-jobcar` (two instances).
      1. A takes order X on loader 1, and `gl-jobcar-digest 1` plus the server's part digest are recorded.
      2. A ends X with `job-end-direct` (the car leaves loader 1). The server command `gamelogic order-copy X` adds an
         open order with X's job and `PrepSeed`.
      3. B takes the copy on loader 1, and the digests are equal on both clients and the server.
      4. Order Y (another seed) gives a different digest.
      5. If spike 1.5 found the overlap equal across loaders, the copy is taken once more on loader 2 and must give
         the same digest too.
      6. Late join: A goes to the menu and joins again; its digest of B's job car equals the server's.

## 6. Part 1 regression

- [ ] 6.1 Update `jobs-latejoin` (orders made by the server, `PrepSeed` in the snapshot) and `economy-trace` (no
      change expected; it proves the job payout path still runs in `client` mode until part 2). Run `Run-All -Lanes 1`
      for the jobs, economy and outdoor areas, plus the smoke set. Record the run ids in `STATUS.md`.

## 7. Payout and XP on the server (part 2)

- [ ] 7.1 Item-fault and tuning tables (D3, part 2) in the exporter and `GameTables`. Verify an export and `gamelogic`
      showing both tables.
- [ ] 7.2 `ModJobPart.Key`: the taker resolves job part ids at `JobStarted` with the native last-match rule
      (D9). Verify in `jobs` that every key of a taken job exists in the server's records, and that the server logs
      unresolved ids.
- [ ] 7.3 `JobRules` (port of `CheckJob`/`CheckJobTask`, `GetPartPrice`, `Helper.GetPrice(PartScript)`, the wheel
      prices, `CalcTuningValue`, `CalcTuningPartsValue`, the alignment and fluid checks, `CarIsInFactoryColor`, the
      body task). `--check-gamelogic payout` on the fixtures covers the running-total bonus, the Expert halving, the
      ×1.5 job bonus, the +25 %/×1.25 XP and the incomplete payout. The harness `job-check` also reports the job
      totals (`MoneySpent`, `MoneySpentWithDifficultyMod`, `TaskBonus`, `JobBonus`, `TotalPayout`, `XP`,
      `IsCompleted`, per part `Done`/`Found`).
- [ ] 7.4 `job_payout` modes in `JobsService.OnJobEnd`, `JobRemoved.Payout/Xp`, the finisher's "Paid <amount>" message,
      `FlushNow` in the `EndJob` prefix, and the server's special-case roll with the `TryAddSpecialCase` prefix (D8,
      D9).
      - Verify on lane 1 with `jobs` in `server` mode: the payout equals the client's `CheckJob`, and the ledger
        shows one `equal`.
      - A mission end adds one `specialCase` to the shared inventory on both clients.
      - With `luck` unlocked, 40 scripted job ends give 6–16 cases (25 % ± noise), and no client adds one locally.
- [ ] 7.5 **Proof:** scenario `gamelogic-payout` (two instances).
      1. A takes a job with brake, suspension, `Additionals` (fluids, alignment) and body tasks.
      2. At five checkpoints, A's `job-check` (extended with the job totals) equals `gamelogic eval payout <job>`:
         before any work, after B repairs one part, after A replaces a part with a higher-quality one, after fluids
         are filled, and after the alignment is set.
      3. The run repeats on Easy, Normal and Expert, with `BonusToMoney` and `BonusToExp` jobs added by
         `gamelogic order-add <seed>`.
      4. The end pays the server's value: money and XP on both clients equal the expected numbers.
      5. The ledger shows `different = 0`.

## 8. Prices and fees on the server (part 2)

- [ ] 8.1 `CarValueRules.SellPrice` (D10) and `car_prices` modes on `CarSale`, with "Sold for <amount>" on a
      difference. `--check-gamelogic sellprice` on the fixtures.
- [ ] 8.2 `FeeRules` for `Welder` and `InteriorDetailing` from `uniqueMod` (`Fixed` in `server` mode). Verify with
      `economy-fees` in `server` mode: the welder and detailing charges equal the server's values for two cars with
      different `uniqueMod`.
- [ ] 8.3 Purchase checks:
      - the outdoor digest row gains `price` (`SetupForBuy`'s value), and `ParkingHandlers` compares a
        `CarLoaderID = -1` purchase from a shared instance with it;
      - an auction win is compared with `AuctionService`'s `LastBid`;
      - `fallback` applies when no reference exists.

      Verify with `outdoor-junkyard` and `outdoor-auction`: a purchase passes, a harness-tampered price (`econ-fee`-style
      verb `outdoor-buy-price <n>`) is refused with `Invalid` and the buyer gets its money back through `WorldState`.
- [ ] 8.4 **Proof:** scenario `gamelogic-prices` (two instances).
      1. Five cars in different states (new from the salon, a junkyard car bought into parking, a job car half
         repaired, a car with tuned parts, a repainted and washed car): A's `gl-sellprice` equals `gamelogic eval
         sellprice`, on Normal and on Expert.
      2. `gl-price welder` and `gl-price detailing` equal the server.
      3. Every inventory item's `gl-price item` equals `PricingCalculator`.
      4. Selling one garage car and one parked car pays the server's price on both clients.

## 9. Part 1 docs and merge

- [ ] 9.1 ROADMAP row 16 status, INTEGRATION.md:
      - config keys `orders`, `orders_while_away`;
      - preference owner of `EnableDevTools`/`DbExportHotkey` → row 16;
      - the scenario list;
      - integration note 4 (interim values) updated.
      - row 9's group 6 marked as moved.

      Then `STATUS.md` with the run ids, and the "what to try" lines for the user (orders arrive without anyone
      generating; re-taking an order gives the same car).
- [ ] 9.2 Merge part 1. Move groups 7, 8 and 10 to `openspec/changes/server-game-logic-2/` with their design and spec
      parts, and add ROADMAP row 16b.

## 10. Part 2 regression and docs

- [ ] 10.1 `economy-trades`, `economy-fees`, `jobs` and `outdoor-*` in `server` modes with `different = 0`. Then the
      full regression `Run-All` and the scale lane (server and Core changed). Record the run ids in `STATUS.md`.
- [ ] 10.2 `docs/hosting.md` settings (`job_payout`, `car_prices`), INTEGRATION.md config keys, QUESTIONS.md answer 7
      ("trusted within bounds until row 16") closed for the moved prices, and ROADMAP status.
