# Review: server-game-logic (row 16)

Reviewed 2026-10-08 against `change/server-game-logic` (`51f0b21`), the code on it (the same as `main` `fbebf7d` for
everything outside `openspec/`), `dump.cs` and the decompiles under `%USERPROFILE%\CMS21-TestInstalls\native\out\`.
Only documents were read. No game runs or test lanes were used. One function, `Job..ctor` (`0x18186E4D0`), was
disassembled with `native/work/dasm.py`.

## Verdict

**Ready after fixes.** The split is sound: port the logic that is data-only, seed the logic that needs the scene, and
keep the small UI-bound amounts. The RVAs and most formulas match the decompiles. But four things must change before
implementation starts:

- **B1:** removing the committed tables, as task 2.4 plans it, stops every server without an export from starting.
- **B2:** the "tutorial = `MissionID 0`" premise is wrong. Row 3 on `main` refuses every story mission today.
- **M1–M4:** the mission port and the client hooks miss two native mission paths, and `PrepSeed` is lost when a job
  starts.
- **M5:** the proposal's main reason for part 2 (a stale `CheckJob`) is already fixed on `main`.

The ordering and the scope should also be reconsidered (section "What is worth doing first").

## Blockers

### B1. Without the committed tables the server exits, and there is no fallback for items or upgrades

**Evidence:**
- `Program.cs:135-139`: `GameDatabase.Initialize()`, then `if (!GameDatabase.isInitialized) { "Game Data
  Initialization failed. Closing.."; Exit(); }`.
- `GameDatabase.cs:22-46`: `isInitialized` is set only when the item, garage-upgrade and player-upgrade tables all load.
- `GarageSection.cs:32-34` (`Reset`) and `GarageUpgradeHandlers.cs:39/53/68/92` dereference `GameDatabase.PlayerUpgrades`
  without a null check.
- `PricingCalculator.cs:16-24`: an item missing from the table is priced at `100 × condition`. Row 10 made item
  prices server-authoritative, so every shop price and sale would be wrong. This is not "today's interim path".

**What the draft says:**
- Risks: "the server starts without tables. Every piece falls back (D5), and the item table's absence keeps today's
  fallback pricing".
- Task 2.4 verify: "A fresh server without `Database\` starts, logs one warning per piece and runs `connect`".

Neither is true of the code. D5's fallback table also covers only the new pieces (orders, payout, prices), not the
item and upgrade tables that rows 2, 10 and 12 already depend on.

**Recommendation:**
- Order: build the hosted export (2.5), the `tools/test-env` export (2.3) and `GameTables` (3.1) first. Remove the
  tables from the zips last, and only after a no-table mode exists.
- Decide what a server without item or upgrade tables does. Options:
  - (a) refuse to start, and say where to get an export (simple and honest for a dedicated server);
  - (b) a degraded mode: item prices and upgrade costs are trusted from the client within bounds, like row 10's
    interim. This needs an `EconomyRules` reason for each and is new design work.
  - Recommended: (a) for the dedicated server and the hosted export for "Host". Write it in D2/D5 and in the
    `game-data` spec. Its scenario "Server without an export … starts" must change.
- Open question 1: the public upstream repo (`Fozkais/CMS21-Together`) keeps these files, and history is not
  rewritten. Removing them from this fork's tree is cosmetic. What matters legally is that the release zips do not
  carry them. The draft's own alternative ("keep the committed files for development, only drop them from the zips")
  is cheaper and loses nothing. Recommend it unless the user wants the repo clean for its own sake.

### B2. Every generated mission has `MissionID == 0`, so row 3 refuses every story mission (mission 0 and all later ones)

**Evidence:**
- `GenerateMission` (`clean/OrderGenerator$$GenerateMission.c`) writes `Job` fields `0x10` (id), `0x88` (IsMission),
  `0x20`, `0x28`, `0x30–0x54`, `0x58`, `0x68`, `0x80` and `0xAD–0xB5`. It never writes `0x8C` (`MissionID`, `dump.cs`
  `Job`). It also never reads a `missionID` INI key (its string literals are listed in the file).
- `Job..ctor` (`0x18186E4D0`, disassembled) writes `0x20` (`""`), `0x30`, `0x34`, `0x44`, `0x54`, `0x60`
  (`new bool[6]`), `0x74`, `0x78` and `0x90` (`CanDelete = true`). It does not write `0x8C`.
- The only writers of `0x8C` are `OrderGenerator.Load` (`:116`, `:354`, from the save) and `MapWindow.LockDestinations`
  (`:111`), where the object is a `MapDestinationButton`, not a `Job`. So `Job.MissionID` is always 0 for a freshly
  generated mission, story or tutorial alike.
- What marks the tutorial is the `forTutorial` parameter (`MissionsTutorial/TutorialMission{n}`, `job.id = 0`, no
  `LastUId++`). A story mission loads `Missions/Mission{n}` with `n = GetMissionID() = MissionsFinished`, so the first
  story mission *is* `Mission0`.
  - Even `job.id == 0` is not a reliable tutorial marker. `LastUId` starts at −1, so the first story mission of a
    fresh profile also gets `id = 0`.
- Row 3's runtime trace ("a fresh profile generates mission 0 on load", sync-orders-and-jobs design.md:389/490) saw
  `Load` → `CanRegenerateMission` → `GenerateMission(GetMissionID() = 0, false)` (`OrderGenerator$$Load.c:292-300`).
  That is story mission 0, not the tutorial.
- Today on `main`:
  1. `JobHooks.cs:31-35` already blocks `GenerateMission(forTutorial: true)` while connected, and `JobHooks.cs:118-125`
     blocks starting a tutorial.
  2. `JobsService.cs:90-95` then refuses `IsMission && MissionID == 0`, which means every story mission.
  3. The generator drops it on the snapshot. `CurrentMissionDone` is already false on that client, so `Update` does
     not retry, and the next garage load regenerates it and gets it refused again.
  4. Story missions never appear in a multiplayer session. Nothing catches it: row 3 task 5.9 says "Not run in a
     scenario", `jobs-trace` skips its mission step when no mission shows up (`if ($mission)`), and the `JobsCheck`
     fixture uses `MissionID = 3`, which the game never produces.

**Recommendation:**
- Fix this on `main` now, as a separate `fix`, independent of row 16: delete the check at `JobsService.cs:90-95`. The
  client-side `forTutorial` block plus the tutorial window block are the real guards. Add a `jobs` step that makes
  `orders-mission 0` on the generator and sees it on both clients.
- Also on `main`: native `GenerateMission` is not capped by `GetMaxOrdersAmount` (it runs outside the cap check in
  `Update`). So `JobsService.cs:97` should not apply `MaxOpenOrders` to `IsMission` jobs.
- In the draft:
  - rewrite D6's last `MissionRules` bullet and spike 1.2's mission part as answered (above);
  - remove "mission 0" from the spike;
  - decide whether the server's mission `Job` keeps `MissionID = 0` (as native) or sets `n`. If it sets `n`, list
    it in D7 as a deliberate difference and leave it out of the exact comparison.

## Major

### M1. `MissionRules` misses the `CanRegenerateMission` path, so a new session never gets mission 0

D6 only builds a mission "when `CurrentMissionDone` and `GetMissionID() >= 0`" (the `Update` path,
`OrderGenerator$$Update.c`). Natively, the first mission and any mission lost on a reload come from `Load`:
- `GlobalData.CanRegenerateMission` (`clean/GlobalData$$CanRegenerateMission.c`) is true when `GetMissionID() <
  MissionsAmount`, `!CurrentMissionDone`, and no `IsMission` job is in `jobs` or `selectedJobs`;
- then `Load` calls `GenerateMission(GetMissionID())` (`OrderGenerator$$Load.c:292-300`).

A fresh session has `CurrentMissionDone = false`, so the server as designed never offers mission 0.

**Recommendation:**
- Port `CanRegenerateMission` too. Evaluate it at session start, after a load, and in `Tick`, against `State.Orders`
  and `State.ActiveJobs`.
- Port the mission state transitions (`EndJobCoroutine`: `MissionsFinished++`, `CurrentMissionDone = true`,
  `IsStoryMissionInProgress = false` whenever an `IsMission` job ends). In `orders = server` the server should own
  them, not copy `packet.Missions` from the finisher (`JobsService.cs:194`, `:223`).
- Note the native gate `MissionsFinished < PlayerLevel + 1` (`GetMissionID`).

### M2. In `orders = server`, clients still generate missions locally (the `Load` path), which D6 says is blocked

D6 says "Clients keep blocking `OrderGenerator.Update` and `GenerateMission` (row 3's prefixes)". But:
- the `GenerateMission` prefix blocks only `forTutorial` (`JobHooks.cs:31-35`);
- `Update` is blocked for non-generators (`JobHooks.cs:19`), but `Load` → `CanRegenerateMission` → `GenerateMission`
  is not.

So every garage load in server mode can make a local mission. That mission is sent as `OrderGenerated`, answered with
the snapshot and dropped, but the client's `GlobalData.CurrentMissionDone` has already been flipped to false. The
client's mission state then drifts from the server's.

**Recommendation:** in server mode, the `GenerateMission` prefix returns false while connected unless
`JobsSync.IsApplying`. Add a task line in 4.2 and a `jobs-latejoin` check that the client's mission fields equal the
server's after a reload.

### M3. `PrepSeed` is lost when the job starts, and a reopened job is not the original order

D8 says the seed is "kept when a lost car reopens the order (row 3 D12)". But:
- `OnJobStarted` stores `packet.Job` (`JobsService.cs:192-193`), which the client builds with
  `ModJobConverter.ToMod(nativeJob)` (`JobsSync.cs:148`). The native `Job` has no `PrepSeed`, so the active job has
  none;
- `ReopenActive` reopens `active.Job` (`JobsService.cs:246`). That is the job *after* `PrepareJob`, with `Parts`,
  `oilLevel` and the `Additionals` subtype filled in. Taking it again runs `PrepareJob` on different inputs from the
  first take.

So "a lost car gives the same car on the next take" fails twice over.

**Recommendation:**
- Keep the original order in `ActiveJobEntry` (`OrderJob`, with `PrepSeed`). Reopen that, not `packet.Job`.
- Copy `PrepSeed` into the active job on the server.
- D8 should also say where the taking client reads the seed: the `JobsSync` mirror by job id. The native `Job`
  cannot carry it.
- Add "lost car → reopen → retake gives the same digest" to 5.3. That is the promise the player actually sees.

### M4. D6's "native call order" for `GenerateNewJob` is incomplete, and following it would desync the stream

From `clean/OrderGenerator$$GenerateNewJob.c`:
- Every task draws a description variant `Range(0,3)` (`:412`/`:452`, `OD_{type}_{subtype}_{n}`), not only
  `Additionals`.
- The easy-mode roll is drawn per task *in the same loop*, right after that task's variant (`:488-499`), not after
  all variants.
- Easy mode is decided by `DifficultyManager+0x18`:
  - 3 (Easy) means always easy;
  - 1 (Expert) means never easy;
  - anything else draws `Range(1,101) <= GetChanceToEasyMode` (`dump.cs` `DifficultyLevel`: Normal 0, Expert 1,
    Sandbox 2, Easy 3). Sandbox draws; D6 does not say so.
- The colour is `Range(0,100) < 51` → HSV. Otherwise it draws an index into `allowedColors`, and an empty list falls
  back to HSV without that index draw (`:192-221`).
- The task count is `Range(1, k)`, with `k = 1` below level 5, so `Range(1,1)` (see m1).
- `globalCondition` is `Random.Range(Vector2)` after `GetGlobalCondition(year)`. `year` comes from the config
  through `FindCarConfig` with a 2000 default (`:531-534`).

**Recommendation:**
- Replace D6's bullet list with the exact draw sequence, or say plainly "port from the decompile; the list is a
  summary".
- Add `ColorHSV.ToColor`, `CarHelper.GetRandomMileage`, `CarHelper.ProcessAllowedColors` and
  `Helper.ParseAllowedPlacesLevel` to spike 1.3.
- Have the exporter store the *parsed* colours, so the server never parses config strings (culture and float parsing).

### M5. The proposal's main reason for moving the payout is already fixed on `main`

Proposal "Why" and D9's alternative say the client "pays what the last `JobHelper.CheckJob` cached, which is stale
when the order tab was opened before another player's repair". But `JobHooks.BeforeEndJob` calls
`JobHelper.CheckJob(carLoader, ref job)` right before the native end (`JobHooks.cs:113`). `JobEndContext` reports what
`EndJobCoroutine` then pays (`JobEndContext.cs:61-67`).

What is left is:
- (a) trust: a modified client can report anything within `0..1,000,000`;
- (b) the finisher's own view lagging behind another player's commit, which `FlushNow` does not change.

**Recommendation:**
- Correct the "Why" and D9.
- Re-weigh part 2. For a co-op mod among friends, (a) is worth little, and part 2 costs 6–7 sessions. A
  `shadow`-only ledger (the server computes and logs, never pays) shows whether (b) ever happens, for a fraction of
  the cost. See "What is worth doing first".

### M6. The payout port must include `EndJobCoroutine`'s pay rules, not only `CheckJob`

From `clean/GameScript._EndJobCoroutine_d__139$$MoveNext.c`:
- money is always `AddPlayerMoney(TotalPayout)` (`:244`);
- XP is added **only when `IsCompleted`** (`:248-250`);
- `TryAddSpecialCase(IsMission)` also runs **only when `IsCompleted`** (`:231-236`);
- none of it happens in the tutorial scene (`:227`);
- the mission counters change whether or not the job is completed.

The design says "paid even when not completed" and "+25 % XP on completion", but not that XP is 0 when the job is
incomplete. The special-case rule ("always for a mission, 25 % with luck", D8/D9 and the spec) misses the
completion gate.

**Recommendation:** add a `JobRules.Pay(check)` step with these rules to D9. Fix the special-case requirement ("when
a completed job ends …"), and add an incomplete-job checkpoint to 7.5.

### M7. A `jobs` v2 section breaks rollback, and its migration is not in the tasks

**Evidence:**
- `JobsSection.cs:29` throws on any `Migrate`.
- `GameDataManager.cs:314-315` throws `SaveTooNewException` ("Update the server") when a section is newer than the
  server supports.

The Migration Plan's "a v2 `jobs` section is read by row 7's unknown-version path as raw JSON, so an older server
keeps the orders" is wrong: row 7 keeps unknown *keys*, not newer versions. An older server refuses the whole save.

**Recommendation:** do not bump the version. Add `OrderClock` and `ModJob.PrepSeed` as optional members. Newtonsoft
ignores unknown members, and missing ones take their defaults:
- a missing clock means `NextOrderTime = 10`;
- a missing `PrepSeed` means "assign on load".

No migration is needed and rollback keeps working. If v2 is kept, add `Migrate(1)` to task 4.2 and fix the rollback
text.

### M8. The exporter's "refuse when a gameplay mod is loaded" now stops hosting altogether

Combined with B1, a host with a gameplay mod gets no export, so the bundled server cannot start. Row 9 already
classifies modded items (`modded_item_database.json` holds them separately).

**Recommendation:** export the base tables and leave out the ids the classifier marks as modded, with a log line,
instead of refusing. Refuse only when the base tables themselves cannot be trusted, for example a mod that patches
`GameInventory` loading.

## Minor

- **m1. RNG spike coverage (D4, task 1.1).** The Xorshift128 assumption is plausible:
  - Unity has used Xorshift128 since 5.4, `Reseed` already reads 16 bytes of state through
    `get_state_Injected`, and the IL2CPP code calls the `RandomRangeInt` and `Range` icalls directly.
  - The spike should compare the four state words right after `InitState` and after single calls. That is cheap and
    isolates seeding from the step function.
  - Its fixed mix must include `Range(int)` with `min == max` (`PrepareJob.c:729` calls `Range(100,100)`;
    `GenerateNewJob` calls `Range(1,1)` below level 5), `min > max`, and negative bounds, plus `Range(float)` with
    `min > max`. Whether `min == max` consumes a draw decides stream alignment.
  - `Random.ColorHSV` does not exist in this build's managed API (`dump.cs:217125`, the class is stripped to `InitState`,
    `Range`, `RandomRangeInt` and `value`). The game uses its own `OrderGenerator.GetRandomColorHSV` (three
    `Range(float)` calls) and `ColorHSV.ToColor`. Drop `ColorHSV` from `UnityRandom` and port `ColorHSV.ToColor`
    instead.
- **m2. `GenerateMission` draws.** It calls `RandomRangeInt(100000, 300000)` for the mileage before reading
  `carMileage` (`GenerateMission.c:123`). D1's table says "none". It matters only if a mission is ever replayed under
  `Reseed`.
- **m3. Seeded job cars: what can still differ (D8, spike 1.5).**
  - Good news: `PreparePart` draws the radial-fault radius *before* `OverlapSphereNonAlloc`, and draws nothing per
    hit (`PreparePart.c:231-242`, buffer of 40). So the overlap cannot shift the random stream. It can only change
    which neighbours get a radial fault, and the order of `JobPart`s. Have digests compare parts as a set, keyed by
    part.
  - `TakeJob` starts `CarLoader.LoadCar` as its own coroutine (`TakeJob MoveNext :183-184`). D8's "`CarLoader.LoadCar`
    is not wrapped unless spike 1.5 finds a draw" should name that iterator. Wrapping `LoadCar` the method does
    nothing, because the draws happen in its `MoveNext`.
  - Physics overlap results depend on synced transforms. Have spike 1.5 take the same order twice on the *same*
    loader too, not only on loaders 1 and 2.
- **m4. Native decline uses `IsMission`, the server uses `CanDelete`.** `OrdersWindow.DeclineOrderAction` refuses
  missions by `IsMission` (`orders_clean/...DeclineOrderAction.c:16`), but `JobHooks.BeforeDecline` sends every
  decline to the server first. The server checks only `CanDelete` (`JobsService.cs:135`), and native missions keep
  `CanDelete = true` from the constructor. D6's `CanDelete = false` for server missions fixes it in server mode. The
  server should also refuse declining any `IsMission` order, which fixes the fallback mode too.
- **m5. The pool's "open orders" must equal native `jobs`.** `GetExperience` and `RemoveCarInPreGeneratedJobs` read
  only `OrderGenerator.jobs`, including missions and the claimant's still-claimed order, and not `selectedJobs`. D7
  should define the server's input as `State.Orders` (Open + Claimed, missions included). The proof must run with no
  claim in flight, because non-claimants drop a claimed order from their `jobs`.
- **m6. `cars.json` depends on the exporter's DLC.** `GetAllAvailableCars` filters by installed DLC, and a non-owner
  may not even have the DLC files. So the exported list and its indices are machine-dependent, which contradicts the
  `game-data` spec's "Machine-dependent fields". Export the unfiltered list where the files exist, with the DLC id per
  car. State that DLC cars are missing from an export made without the DLC.
- **m7. Purchase checks are not always independent.** The outdoor reference digest is the generator's
  (`OutdoorInstances.cs:258-260`). When the buyer is the generator, the `price` check is self-attested. Say so in
  D10, or skip the check when buyer = reference author.
- **m8. Test plan gaps.**
  - `gl-order-native` must run under `JobsSync.Guard()`/`IsApplying`. Otherwise `JobHooks.AfterGenerate` sends
    `OrderGenerated` and the server answers with a snapshot. It must also undo `LastUId++`, `jobs.Add`, `StartTimer`
    and `UIManager.UpdateJobs`, not only `AddJob`.
  - Levels 1, 6 and 12 skip bands. The task count changes at levels 5, 8 and 10, and easy-mode chances change by
    band. Use the band edges (for example 1, 4, 5, 7, 8, 9, 10, 12, 20).
  - Also cover an electric car (category filter), a config with an empty `allowedColors`, Sandbox difficulty, and an
    empty pool (no order, no counter drift).
  - 7.4's "40 job ends give 6–16 cases" fails by chance about 5 % of the time (binomial with n 40 and p 0.25:
    P(X ≤ 5) + P(X ≥ 17) ≈ 0.05), unless the seeds are fixed. Assert the ledger's roll values for fixed seeds instead.
    That is also far cheaper than 40 scripted job ends.
  - 7.5: add the "Part not found" path, a job part with duplicate-named siblings (the last-match rule) and an
    incomplete end (M6).
- **m9. Task order and size.**
  - 2.4 (remove the tables) comes before 2.5 (hosted export) and 3.1 (`GameTables`). The lanes and a fresh clone break
    in between (B1). Move 2.4 to the end of part 1.
  - Spike 1.2's mission half is answered here (B2, M1). Only the vanilla "orders while away" part remains.
  - Part 1 at 9–10 sessions looks about 3–4 short. B1's no-table policy, M1–M3, the cars and missions exporter
    (spike 1.4 is unknown) and `Export-GameData.ps1` headless are all extra. Expect 12–14.
  - Part 2 at 6–7 is plausible if the parking sale has a native formula to copy. D10 names none (only the garage
    `SetupForSell`). Spike 1.3 should find the parking path.
- **m10. A dedicated-server owner exports through dev tools.** `EnableDevTools` + `DbExportHotkey` is a developer
  switch, but the draft makes it a normal hosting step (D2.3). Offer a menu button or a documented launch option
  instead.

## Nits

- D5's "exported from another game version than the pinned one": when `game_version = auto`, the pinned version *is*
  `meta.json`'s (`CompatibilityPolicy.cs:30-34`). The check only matters with a configured version. Say so.
- D11: tie the float claim to the shipped runtime (net472, x64, `CMS21-Together-Server.csproj:4-5`). Add one
  `--check-gamelogic` fixture with known bit patterns, so a runtime change (Mono, x86) shows up.
- Proposal "Game hooks", part 1: name the iterator for `LoadCar` if spike 1.5 adds it (see m3).
- `server-orders` spec "Limit follows the shared level": say whether "displayed level" is `RealPlayerLevel`
  (`PlayerLevel + 1`), which is what `GetMaxOrdersAmount` uses.

## Checked and correct

- **RVAs:**
  - `GenerateNewJob` `0x180C5BB40`, `PrepareJob` `0x180C5D680`, `<TakeJob>d__19.MoveNext` `0x180A733E0`;
  - `CheckJob` `0x18186E600`, `CheckJobTask` `0x18186EC10`;
  - `GenerateMission` `0x180C5FDF0`, `Update` `0x180C5B920`, `GetMaxOrdersAmount` `0x180D7CD50`;
  - `SetupForSell` `0x180AE9120`, `CalcCarValue` `0x180503B00`, `GetWelderCost` `0x1804C8480`;
  - `TryAddSpecialCase` `0x180C7D1B0`, `<EndJobCoroutine>d__139` `0x1808948D0`.
- **`GetMaxOrdersAmount`:** the cap bands are right. It returns 0 in scene 15 (the tutorial).
- **`Update`:** the timer is strict (`orderTimer > nextOrderTime`), and accepts and job ends reset it.
- **`CanGenerateDLCCar`:** it draws only on a release day (`orders2_clean/Helper$$CanGenerateDLCCar.c`), so dropping
  it does not shift the stream on other days.
- **`CheckJob`/`CheckJobTask`:**
  - the last match in `partScriptCache` wins;
  - "Part not found" sets `Done = Found = true`;
  - `Found` is `IsExamined`;
  - `MoneySpentWithDifficultyMod += (int)(price × mod)` and XP `+= GetXPFromDifficultyMod(mod) × 4` per repaired part;
  - `JobBonus = (int)(MSWDM × 0.5)`, ×1.5 on Expert;
  - XP `−= (int)(XP × −0.25)` on completion and ×1.25 with `BonusToExp`;
  - `BonusToMoney` and Expert's halving of the task bonus are in `CheckJobTask` (`:867-882`).
- **Job-part keys:** the `s:<index path>` key resolved at take time is the right choice. `CarPackets.cs` already notes
  that name paths are ambiguous for identical siblings, and the native last-match rule picks one of them.
- **`SetupForSell`:** matches D10, including body ×`uniqueMod` twice (`CalcBodyValue` already multiplies), the mileage
  bands and `restorationBonus`.
- **Fees:** the welder is `Convert.ToInt32((uniqueMod − 1) × 1000 + 500)`.
- **`TryAddSpecialCase`:** missions always get one; otherwise the `luck` upgrade unlocked and
  `RandomRangeInt(0,101) < 25`.
- **Legal and data:** exporting ids and numbers at runtime on the user's own install, never redistributing them, is
  sound. Dropping `LocalizedName`, `Brand` and `ShopName` is safe: no server code reads them. Only
  `PartProperty.cs:22-24` declares them.

## What is worth doing first

1. **Now, on `main`, outside row 16:** B2's fix (delete the `MissionID == 0` refusal, don't cap missions, refuse
   declining missions). S, and it brings back the whole story line in multiplayer.
2. **Spike 1.1 (RNG) and spike 1.5 (take draws):** one session each. They decide everything else.
3. **Seeded job cars (`PrepSeed`, with M3's fix):** S–M, and needs no game tables. The player sees the gain: a retake
   or a lost car gives the same car. Of everything here it has the best value for its cost.
4. **Orders: consider a cheaper variant before the full port.** Open question 4's default keeps "no orders while
   nobody is in the garage", so the port's main advantage (orders without a garage client) is switched off anyway.
   A *server-clocked, server-seeded native generation* would cover:
   - the timer, cap, seed and pool list stay on the server;
   - a garage client runs `Reseed.WithSeed(orderSeed, GenerateNewJob)`, with a `GetAllAvailableCars` postfix that
     limits the pool to the server's shared list;
   - any other client can replay the seed to verify;
   - a re-election cannot lose or change an order (the server asks again with the same seed).

   That removes the election's real defects without `cars.json`, `orders.json`, `missions.json` or a port, at about
   M instead of L. Keep the full port for when `orders_while_away` is wanted.
5. **Data policy (D2):** keep the committed tables for development, drop them from the zips, and only after the hosted
   export works and B1's no-table behaviour is decided.
6. **Part 2:** start with `job_payout = shadow` only, which computes and logs and never pays, and look at the ledger
   after a playtest.
   - The car sale price is the only large amount worth moving, and only if trust matters to the user.
   - Welder and interior detailing (one formula each, already bounded) and the special case (one 25 % roll on the finisher;
     this review did not check how its item reaches the shared inventory today) are not worth their share of a 6–7 session change on their own.
