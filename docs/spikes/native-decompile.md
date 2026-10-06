# Spike: native decompile of the IL2CPP game

Roadmap: "Early spikes" → native decompile, feeding rows 3 (`sync-orders-and-jobs`), 10 (`economy-audit`) and
16 (`server-game-logic`). Game: Car Mechanic Simulator 2021, Unity 2020.3.49f1, IL2CPP metadata v27.1.
Date: 2026-10-06.

**Result:** Il2CppDumper and headless Ghidra (no auto-analysis, names and types from Il2CppDumper) decompile any
method to readable pseudo-C in seconds, and the questions below are answered from that code. Cpp2IL was downloaded but
not needed.

All tooling and output live outside the repo, under `%USERPROFILE%\CMS21-TestInstalls\native`:

| Path | What |
|------|------|
| `tools\` | Il2CppDumper, Ghidra, Cpp2IL (unused) |
| `work\` | Ghidra project, the scripts below, the inputs derived from `script.json` |
| `out\il2cppdumper\` | `dump.cs` (every type with field offsets and method RVAs), `il2cpp.h`, `script.json`, `stringliteral.json`, `DummyDll\` |
| `out\decomp\` | raw Ghidra pseudo-C, one `.c` per method (~660 methods) |
| `out\clean\` | the same files with il2cpp runtime noise removed and icalls named. **Read these.** |
| `out\asm_*.txt` | disassembly dumps, used where Ghidra lost float arguments |

## Setup (reproducible)

Tool versions:

- Il2CppDumper **v6.7.46** (`Il2CppDumper-net6-win-v6.7.46.zip`, GitHub Perfare/Il2CppDumper). It targets .NET 6,
  which is not installed, so it runs with `DOTNET_ROLL_FORWARD=Major` on .NET 8/9/10.
- Ghidra **12.1.4** (`ghidra_12.1.4_PUBLIC_20260921.zip`, GitHub NationalSecurityAgency/ghidra), on Java 23.
- Cpp2IL 2022.1.0-pre-release.21 (`Cpp2IL-2022.1.0-pre-release.21-Windows.exe`). Downloaded but not used.
- Python 3.13.9 from pyenv-win, called by its full path
  `~/.pyenv/pyenv-win/versions/3.13.9/python.exe`. The `python` shim is a `.bat`: it loses stdin and lets cmd
  interpret `|` in arguments.

Steps. The shell is Git Bash, and the scripts are in `work\`:

1. `work/prep.sh` runs Il2CppDumper on `CMS21-TestInstalls\A` (read-only) and writes `out\il2cppdumper\`. From
   `script.json` it then derives `methods.tsv`, `methods_sig.tsv`, `meta*.tsv` and `strings.tsv`. It also builds
   `il2cpp_ghidra.h` with Il2CppDumper's `il2cpp_header_to_ghidra.py`, and `patch_header.py` fixes one struct.
   Il2CppDumper's header flattens the explicit-layout `UnityEngine.Color32` (an `rgba` int plus four bytes, which
   makes it 8 bytes instead of 4). That moves every field after it 0x10 bytes, for example in `GlobalData`'s statics.
   `verify.py` checks the parsed structs against `dump.cs`. After the patch, GlobalData statics, CarLoader, PartScript,
   CarPart, Item, Job and GameInventory have 0 mismatches.
2. `work/setup.sh` (about 1.5 min) imports `GameAssembly.dll` into a Ghidra project with `-noanalysis`. Two
   post-scripts follow:
   - `scripts/LabelIl2cpp.java` adds labels for 129,614 methods, 9,298 TypeInfos, 25,807 MethodInfos and 15,176
     string literals (`StringLiteral_<text>`).
   - `scripts/ParseIl2cppHeader.java` parses `il2cpp_ghidra.h` with Ghidra's `CParser` and types the 8,993
     `*_TypeInfo` globals. Static fields then decompile as `GlobalData_TypeInfo->static_fields->PlayerMoney`.
3. `work/dec.sh <targets.txt> <outdir>` runs `scripts/DecompileTargets.java` in read-only mode. The targets file holds
   regexes over method names (examples in `work/targets/*.txt`). For each match, the script creates the function and
   its direct callees, applies the Il2CppDumper signature (typed `this` and parameters, so fields get names) and
   decompiles to `<Name>.c`. Overloads get an `_<rva>` suffix. 650 methods take about 3 minutes.
4. `python work/clean.py out/decomp out/clean` removes the noise: class-init checks, method-init blocks, GC write
   barriers and icall-resolution blocks. Resolved icalls print as `ICALL<UnityEngine.Random::RandomRangeInt>(…)`.

Helpers. There is no Ghidra auto-analysis, so the cross-reference helpers scan the binary directly:

- `xref.py <regex>…` lists the methods that call the matched methods. It scans for direct `E8`/`E9` rel32 calls and
  takes about 1 s.
- `dxref.py strings.tsv <regex>` lists the methods that reference a string literal or global through RIP-relative
  addressing. With it, all `stat_*` users were found in about 1 s.
- `calls.py <glob>` lists the distinct callees of each decompiled function, as a side-effect overview.
- `fnsize.py <regex>` gives the byte size of each matched method.
- `cstr.py`/`f32.py` read a C string or float constant at an address. `scripts/DumpAsm.java` dumps a function's
  disassembly with call targets named.

Pitfalls when reading the output:

- Shared generic code carries the first instantiation's name. `Singleton<CameraCarInput>__get_Instance(Method_Singleton<GameManager>_get_Instance__)`
  is `Singleton<GameManager>.Instance`, and `List<List<GroupItem>>__Add` is any `List<T>.Add`.
- `func_0x000181a89780` is `String.Equals` and `func_0x000181a89bf0` is `String.op_Inequality`. String compares are
  often inlined as `m_firstChar` loops.
- Virtual calls go through vtable offsets. `(**(code **)(*plVar + 0x1d8))(plVar, "stat_…", 1, …)` is
  `BaseAchievements.IncrementStat`.
- Float arguments passed in XMM registers are sometimes dropped, as in `UnityEngine_Random__Range()`. In those cases
  the values were read from `out\asm_*.txt` together with `f32.py`.
- Calls on return values that are not typed show raw offsets (`*(int *)(lVar + 0x18)`). Look the offsets up in
  `dump.cs`.

## a. Job payout

**The payout is computed before the job ends, not in `EndJob`.** `JobHelper.CheckJob(CarLoader, ref Job)`
(`0x18186E600`) fills `Job.MoneySpent`, `MoneySpentWithDifficultyMod`, `TaskBonus`, `JobBonus`, `TotalPayout`, `XP` and
`IsCompleted`. Its callers, found with `xref.py`, are the car-info UI (`OrderTab.SetupData` and
`OrderSummaryPageManager.PrepareFirstPage`). `GameScript.EndJob` (`0x180E95830`) only starts `EndJobCoroutine`,
which pays the cached numbers.

`JobHelper.CheckJob`:

```c
job.IsCompleted = true; MoneySpent = MoneySpentWithDifficultyMod = TaskBonus = JobBonus = TotalPayout = XP = 0;
foreach task in job.jobTasks: JobHelper.CheckJobTask(carLoader, ref job, ref task, ref partScriptCache);
if (job.IsCompleted) {
    job.JobBonus = (int)(job.MoneySpentWithDifficultyMod * 0.5f);
    job.XP = job.XP - (int)(job.XP * -0.25f);                          // +25 %, truncated
    if (DifficultyManager.currentDifficulty == Expert) job.JobBonus = (int)(job.JobBonus * 1.5f);
}
job.TotalPayout = job.MoneySpent + job.JobBonus + job.TaskBonus;
if (job.BonusToExp) job.XP = (int)(job.XP * 1.25f);
```

`JobHelper.CheckJobTask` (`0x18186EC10`, 6.4 KB) handles these task types and part lists:

| Task | Done when | Money (to `MoneySpent`, `…WithDifficultyMod`, `task.moneySpent`) | XP |
|------|-----------|-------------------------------------------------------------------|----|
| Ordinary (type not `Additionals`/`Body`), per `JobPart` | The `PartScript` whose `GetGameObjectPathWithoutRoot()` equals `JobPart.ID` has `IsRepaired(job.globalCondition)`, which means `Condition >= round(globalCondition*100)/100` and not `IsUnmounted` | `p = CarLoader.GetPartPrice(part)`. `MoneySpent += p`. `MoneySpentWithDifficultyMod += (int)(p * GameInventory.GetItemDifficultMod(id))`, where the mod comes from the item-fault table (default 1.0) | `GetXPFromDifficultyMod(mod) * 4`, with mod 0.5→1, 1.0→2, 1.5→5, otherwise 0 |
| `Additionals/HeadlampAlignment` | Both `HeadlampAlignment.IsCorrect` | +50 | +5 |
| `Additionals/WheelsAlignment` | `WheelsAlignment.IsCorrect` | +200 | +10 |
| `Additionals/IncreaseTuneValue` | `CalcTuningValue()` in `[IncreaseTuneValue, +5]` | +`CalcTuningPartsValue(carLoader)` | +50 |
| `Additionals/*Refill` (brake 2, coolant 4, washer 5, power steering 6) | `FluidsData.GetAvgLevel(type) >= 0.65` | +15 | +1 |
| `Additionals/*Change` | Level and condition `>= 0.65` | +25 | +2 |
| `Engine/Oil` (dummy part added) | Oil level and condition `> 0.65` | +25 | +3 |
| `Body/General`, per body part except benches | Mounted, `Condition >= 1.0`, `IsDefault` | `(int)(PartProperty(carToLoad + "-" + IDWithTuned).Price * Condition)` | +4 |
| `Body/PaintOriginal` | `CarIsInFactoryColor()` | +1000 | +10 |

After each task:

```c
task.Done = allPartsDone;
if (!task.Done) job.IsCompleted = false;
else {
    job.TaskBonus += (int)(job.MoneySpentWithDifficultyMod * (task.type == "Body" ? 0.15f : 0.25f));
    if (job.BonusToMoney) job.TaskBonus += (int)(job.MoneySpentWithDifficultyMod * 0.25f);
    if (Expert) job.TaskBonus = (int)(job.TaskBonus * 0.5f);
}
```

Two quirks to copy as they are:

- The bonus uses the job's running total, not the task's own money, so earlier tasks are counted again.
- On Expert, the halving applies to the running `TaskBonus` after every task.

`CarLoader.GetPartPrice` (`0x180503C70`) returns 0 for unmounted parts and for the oil drain/check/fill special
group. Tires (special group 6) and rims (7) use the wheel's size, width, profile and ET from `CarLoader.GetWheelSize`/`GetET`. The
tire/rim price is then multiplied by `Condition` and by `(1 + Quality*4*0.02)`, so +8 % per quality level. Every
other part uses `Helper.GetPrice(PartScript)`, which applies +2 % per quality level plus `+Quality` (see c).

Inputs: the task list, live part condition and mount state, fluid levels and conditions, alignment, tuning value,
factory colour, item prices and fault difficulty mods, difficulty level, and the job flags `BonusToMoney`/`BonusToExp`.
**No skills are involved.** The only skill on this path is the `luck` upgrade in `TryAddSpecialCase`.

Ending the job: `GameScript.<EndJobCoroutine>d__139.MoveNext` (`0x1808948D0`).

1. It invokes `GameScript.OnEndJob` if anything is subscribed, and returns. The mod can hook here.
2. It logs a DLC check, then shows an info window and stops when one of these checks fails:
   - a part has missing bolts (`checkCarPartsBoltsMissingPartID`);
   - `CarLoader.CheckIfHaveBody()` fails;
   - oil is missing (`CheckCarHaveOil`, or below `job.oilLevel` for jobs with an `Oil` subtype);
   - `CheckCarHaveFluids` fails;
   - `GetOtherPartsCondition(job) < job.otherPartsCondition - 0.005`;
   - the front or rear wheels differ in size.
3. It disables input and fades the screen. Then, unless the scene is the Tutorial (15):
   - if `IsCompleted`: `Inventory.TryAddSpecialCase(job.IsMission)`. A mission always gives a `specialCase` item.
     Otherwise the `luck` upgrade gives a 25 % chance.
   - `GlobalData.AddPlayerMoney(job.TotalPayout)`. **This is paid even when the job is not completed.**
   - if `IsCompleted`: `GlobalData.AddPlayerExp(job.XP)`.
4. `OrderGenerator.CancelJob(job.id)`.
5. For a mission: `IsStoryMissionInProgress = false`, `MissionsFinished++`, `CurrentMissionDone = true`. If
   `MissionsAmount <= MissionsFinished`, it calls `PlatformManager.IncrementStat("stat_finish_allmissions", 1)`.
6. `CarLoader.DeleteCar(true)`, `GameScript.GarageOnFootWithoutFader()`, `GarageLoader.Save()`.
7. Stats, through the virtual `BaseAchievements.IncrementStat(…, 1)`:
   - `stat_finish_order` if `IsCompleted`;
   - `stat_bonus_exp` if completed and `BonusToExp`;
   - `stat_bonus_money` if completed and `BonusToMoney`.

`GlobalData.AddPlayerMoney` (`0x180D7B580`) does nothing when `GameSettings.UnlimitedMoney` is set. Otherwise it
clamps the money to 0…900,000,000, refreshes the stats UI and plays the `AddMoney`/`SubMoney(Big)` sound.

`GlobalData.AddPlayerExp` (`0x180D7BCC0`):

- **XP is doubled on Expert** (`exp*2` when `currentDifficulty == 1`).
- It levels up in a loop: `GetDiffToNextLvl` → `UpgradeSystem.AddPoints` → `PlayerLevel++`.
- It sets `stat_level` (via `GetStatValue`) when `RealPlayerLevel` is higher than the stored value.

Every `stat_*` user, from `dxref.py`, for row 10 and the achievement guards:

| Stat | Set by |
|------|--------|
| `stat_finish_order`, `stat_bonus_exp`, `stat_bonus_money`, `stat_finish_allmissions` | `EndJobCoroutine` |
| `stat_level` | `GlobalData.AddPlayerExp` |
| `stat_scrap` | `GlobalData.AddPlayerScraps` |
| `stat_sell_car`, `stat_sell_fix_car` | `CarSummaryTab.<SellCar>g__SellCarAction` |
| `stat_buy_barn`, `stat_buy_carjunkyard`, `stat_buy_carsalon` | `CarSummaryTab.<BuyCar>g__BuyCarAction` |
| `stat_buy_parts` | `ShopBuyWindow.BuyItem` |
| `stat_sell_junk` | `Inventory.SellPerCondition`, `NotificationCenter.NewButtonAccept` |
| `stat_unlock_parking` | `ParkingManagementWindow.UnlockParkingLevelAction` |
| `stat_fix_body`, `stat_fix_parts` | `RepairPartWindow.ProcessGameResult` |
| `stat_paint_car` | the three paintshop `PaintCar` coroutines |
| `stat_swap` | `NotificationCenter.<ActionInsertEngineToCar>` |
| `stat_shed_oil` | **`PartScript.CheckMessageOnHide`** |
| `stat_unscrew` | **`PartScript.Update`** |
| `stat_wheels_balanced` | `WheelBalancerLogic.<Clear>` |
| `stat_win_car` | `AuctionBidding.ReceiveCarAction` |
| `stat_visit_barn`/`_junkyard`/`_salon` | the scene generators |
| `stat_full_garage`, `stat_unlock_allupgrade` | `UpgradeSystem.CheckForAchievements` |
| `stat_dragrace_*`, `stat_finish_testpath`, `stat_finish_testtrack`, `stat_timeattack` | the track scenes |

Size: porting the payout to the server is **M**. `CheckJob` and `CheckJobTask` together are about 7 KB native, roughly
300 lines of C#. The formulas are simple, but every input is live car state: per-part condition, mount state and
`GameObject` path; fluids; alignment; tuning value; factory colour. The server needs rows 1 and 4's per-part state
plus the item database (price, special group, fault difficulty mod). A cheaper interim: the client that ends the job
reports the `Job` fields after `CheckJob`, and the server checks them for plausibility and applies them.

## b. Order generation

`OrderGenerator` has 27 own methods plus 14 lambda and coroutine helpers. Measured with `fnsize.py` over
`(OrderGenerator|JobHelper)[.$].*`, the biggest are:

| Method | Bytes |
|--------|-------|
| `<TakeMission>d__22.MoveNext` | 13,776 |
| `PrepareJob` | 9,072 |
| `GenerateNewJob` | 5,344 |
| `PreparePart` | 5,216 |
| `GenerateMission` | 4,912 |
| `Load` | 4,624 |
| `<TakeJob>d__19.MoveNext` | 4,464 |
| `Save` | 3,024 |
| `GetAvailableCategories` | 2,304 |
| `PrepareBodyParts` | 1,760 |
| `AddJob` | 1,456 |

The rest are small tables.

Flow:

- `Update` (`0x180C5B920`): while `NotificationCenter.IsGameReady`, `GameSettings.CanGenerateOrders` and
  `GlobalData.Jobs < GetMaxOrdersAmount()`, it accumulates `deltaTime`. When the accumulated time passes
  `nextOrderTime`, it calls `GenerateNewJob()` and sets `nextOrderTime = 30` s. It also calls
  `GenerateMission(GetMissionID())` when `CurrentMissionDone` and missions are left.
- `GenerateNewJob` (`0x180C5BB40`) does the following:
  - `playerLvl = PlayerLevel + 1`; `GlobalData.AddJob(1)`.
  - `forXP = GetExperience(jobsWithForXP<=PlayerExp, 6)`: `PlayerExp` when fewer than 6 such jobs, otherwise
    `PlayerExp + 1500` or `+3000`.
  - Car: `CarBundleLoader.GetAllAvailableCars()`, filtered by `CarIsAvailableOnLevel(car, config, playerLvl)`, then
    `RemoveCarInPreGeneratedJobs`. A DLC car is picked when `Helper.CanGenerateDLCCar`, otherwise
    `Random.Range(0, n)`.
  - `isElectric` comes from the car config `engine/type` → `GameInventory` engine INI `isElectric`.
  - `GetAvailableCategories(playerLvl, isElectric)` reads `ordersDataINI` (the `ordersData` TextAsset) keys
    `AviableCategorysOnLevel`/`AviableSubtypesOnLevel` for the level. `notAvailableForElectric` filters the result.
  - Colour: 51 % `GetRandomColorHSV()` (H 0–256, S 0.1–1, V 0.1–0.8). Otherwise a random entry from the car config's
    `other/allowedColors`, with its paint type.
  - Task count `Random.Range(1, k)`, where k = 1 for level <5, 3 for <8, 4 for 8–9 and 5 for ≥10. Distinct
    categories are drawn with `Random.Range(0, count)`. The subtype count per task comes from `GetMaxJobSubTypes`.
    `Additionals` tasks draw `Random.Range(0, 3)` variants. `OD_{0}_{1}` localisation keys.
  - Easy mode is rolled per task with `Random.Range(1, 101)` against `GetChanceToEasyMode(lvl)`: 100 below level 5,
    50 below 8, 20 below 10, 25 below 12, then 0.
  - `globalCondition = Random.Range(CarHelper.GetGlobalCondition(configYear))`. By car age: <3 y 0.8–0.9, <11 y
    0.6–0.8, <21 y 0.5–0.7, ≤30 y 0.4–0.6, >30 y 0.3–0.7. `year` defaults to 2000.
  - `Mileage = CarHelper.GetRandomMileage(year, 4)`.
  - 30 % chance of a bonus, split 50/50 between `BonusToExp` and `BonusToMoney`.
  - `timeToEnd = Random.Range(121, 300)`. `id = ++LastUId`.
- `TakeJob` (coroutine, `0x180A733E0`) spawns the car and calls `PrepareJob` (see e).
- `GenerateMission` and `TakeMission` handle story missions, from a separate data path. `TakeMission` is the biggest
  method.
- `Save`/`Load` persist the job list.

Randomness: everything goes through `UnityEngine.Random.Range` (the icalls `RandomRangeInt` and `Range(float,float)`),
with no seed of its own. A server port cannot reproduce it bit for bit and does not need to. It needs its own RNG with
the same distributions.

Data tables a port needs (row 9's exporter):

- the `ordersData` INI;
- per car config: `year`, `engine/type`, `other/allowedColors`, the `logic` section, and `CarIsAvailableOnLevel`
  (DLC and level gating);
- `GlobalData.GetMaxOrdersAmount` and the upgrade-dependent order slots;
- `GameInventory` item faults (`GetItemFault`/`GetItemDifficultMod`), used by `PreparePart`.

Estimate:

- **Generating the list** (`GenerateNewJob`, `AddJob`, `GetAvailableCategories`, the level tables): **M**, about
  12 KB native, or 400–500 lines of C# plus the data export.
- **Preparing the car** (`PrepareJob`, `PreparePart`, `PrepareBodyParts`): **L**. It works on live `PartScript`s and
  uses `Physics.OverlapSphereNonAlloc` for radial faults, so it is bound to the scene. It should stay on the client
  that spawns the car, with the server sending the `Job` and a seed/fault list. The alternative is to port the rolls
  and send per-part results.
- **Missions:** keep them client-side for now (`TakeMission` is 13.8 KB).

So row 3's elected generator can be replaced by a server-side list generator (M). Car preparation stays on a client.

## c. Prices and fees

Item price: `Helper.GetPrice` has five overloads. It applies quality only to the base price, and the server's
`PricingCalculator` matches it:

| Native (RVA) | Formula | Server |
|--------------|---------|--------|
| `GetPrice(Item, mod)` `0x180CC93D0` and `(BaseItem, mod)` `0x180CC92B0` | `LicensePlate`: `RoundToInt(min(Condition, Dent)*100)`. Otherwise base = tire, rim or `PartProperty.Price` by special group 6/7. `v = min(Condition, Dent) * base`. If `Quality>0`: `v += v*Quality*2*0.01`. Result `max(1, RoundToInt(v*mod + Quality))` | Matches (`ConditionToShow` is `min(Condition, Dent)`). The server's fallback for unknown IDs has no native equivalent: native throws NRE |
| `GetPrice(GroupItem)` `0x180CC9E40` | Sum of `GetPrice(item, 1.0)`. **The group's `mod` is ignored**, also in the `BaseItem` overload | Matches |
| `GetRimPrice` `0x180CC9040` | `(et + (int)(Price/100) * (max(size,12)-11) * 4) * 5` | Equal to the server's `(int)(Price/100f)*((max(size,12)-11)*20) + 5*et` |
| `GetTirePrice` `0x180CC9150` | `(int)Math.Round((max(width,135) + (max(size,12)-39)*5 + profile) * Price/100f)` | Equal |
| `GetPriceWithQualityMod` `0x180CC9970` | `+quality*2 %` | Equal |
| `GetPrice(PartScript)` `0x180CC96A0` | `Math.Round(Price*Condition*(1+q*0.02) + q)`, using `tunedID` if set. License plate = `Condition*100` | Not on the server. Needed for job payout (a) |
| `GetPrice(string id, float cond, int q)` `0x180CC9850` | `Math.Round((int)(Price*cond)*(1+q*0.02) + q)` | Not on the server. Used by car value |

Car value and sale:

- `CarLoader.CalcCarValue` (`0x180503B00`) is the sum of `GetPartPrice` over mounted, untuned parts, excluding oil
  drain/check/fill.
- `CarLoader.CalcBodyValue` (`0x180502B20`):
  - `body`: `min(Condition, Dent) * 0.5 * 2000 * uniqueMod`;
  - `details`: `… * 0.5 * 400 * uniqueMod`;
  - license plates: `min(…) * 100`;
  - other body parts: `Helper.GetPrice(carID + "-" + IDWithTuned, min(Condition, Dent), Quality)`.
- `uniqueMod` is the car config's `logic/uniqueMod` (default 1), set in `CarLoader.LoadConfig`.
- `CarLoader.GetCarPrice` (`0x180504390`) = `(CalcCarValue + CalcBodyValue) * uniqueMod`. It has **no callers**.
- The sell window computes its own price. `CarSummaryTab.SetupForSell` (`0x180AE9120`):
  ```c
  carValue   = (int)(CalcCarValue*uniqueMod) + (int)(CalcBodyValue*uniqueMod);   // body: uniqueMod counted twice
  tuning     = (int)(CalcTuningCost*uniqueMod);
  gc         = clamp01((PartsGlobalCondition + PanelsGlobalCondition) / 2);
  mileageF   = Mileage==0 ? 30 : <=50k ? 25 : <=100k ? 15 : <=200k ? 5 : 0;      // times gc
  restMod    = CarHelper.GetRestorationMod(gc): gc>=1 →30, >=0.9 →20, >=0.8 →10, else 0; ×0.5 on Expert
  restBonus  = (int)(WashFactorBonus + (carValue+tuning)*(mileageF*gc + restMod)/100 + PaintBonus);
  sellPrice  = carValue + restBonus + tuning;
  ```
  The paint and wash bonuses are `uniqueMod*300` when the conditions hold (`CarHelper.GetPaintBonus`/`GetWashFactorBonus`).
- `SellCarCoroutine` pays `AddPlayerMoney(sellPrice)`, then `DeleteCar` and `GarageLoader.Save`.
- `SetupForBuy` (`0x180AE7770`) uses the same value. For a salon car it uses
  `RoundToInt(baseCarPrice * negotationMod)` or `SalonCarData.BaseCarPrice`. Auctions use `AuctionHelper.GetStartingPrice`
  and `GetPriceForRating`. The car-config value `logic/carValue` (`CarBundleLoaderExtension.GetCarValue`, default 50000)
  feeds those lists.

Fees (every `AddPlayerMoney` caller is listed with `xref.py`):

- **Travel**, `MapWindow.SubmitPanelAction` (`0x180D0D650`): Auctions −200, Junkyard −500, Barn −100 (and
  `BarnsAmount--`). Charged only when `GameSettings.TravelHaveCost` is set and the difficulty is not Sandbox. Test
  track, race track, speedtrack, salon, custom track and photo locations are free.
- `MapWindow.MeasurePowerForSelectedCarLoader`: dyno −500.
- The legacy `NotificationCenter.ButtonAccept` `ChangeScene` path charges the `Price` value from the notification
  hash, for scene types 1, 2, 5 and 8 when `TravelHaveCost` is set.
- **Parking levels**, `ParkingManagementWindow.OnSourceParkingLevelChange` (`0x180A03510`):
  `price = index * 50000 * (upgrade "cheaper_parking" unlocked ? 0.5 : 1)`. `UnlockParkingLevelAction` charges it,
  does `UnlockedParkingLevels++`, and sets `stat_unlock_parking` at 10 levels.
- **No per-move parking fee was found.** No `AddPlayerMoney` caller sits on the move path.
- `GlobalData.GetCommissionForScene(scene)` returns 5 for Junkyard/Barn, 20 for Salon and 1 otherwise. It has no
  direct caller, so it is inlined or only used by UI. Open point.
- Other money sinks, each a row-10 item:
  - paintshop (`PaintshopManager.TryGetMoneyForPaint`) and tinting;
  - welder and interior detailing toolkit;
  - `RepairPartWindow` (repair, break, `JustTakeMyMoney`);
  - skill unlock and reset;
  - case opening (`CaseOpeningWindow.TakeLoot`);
  - drag championship;
  - `FluidRefill.Hide`;
  - **`PartScript.<Hide>` fluid-spill cost** (see d);
  - `Inventory.SellPerCondition`;
  - shop buy (`ShopBuyWindow`, `ShopLicenseBuyWindow`, `TakenItemsWindow.BuyPartsAction`);
  - `GameScript.BuyCar`;
  - auction win.

Estimate: the item price formulas are done (S, a few tests to add). The car sale and buy price is **M**: it needs
`CalcCarValue`, `CalcBodyValue`, `CalcTuningCost` and the condition averages over the per-part state.
Travel, dyno and parking are **S** constants.

## d. Side-effect-free setters for applying remote state

The lowest levels found, and the side effects that the normal player paths add:

| Want | Use | It does | Avoids (side effects of the normal path) |
|------|-----|---------|------------------------------------------|
| Part condition | `PartScript.SetConditionNormal(float)` (`0x180FF8EF0`), or `SetCondition(float, bool updateSolid)` (`0x180FF8F60`), which returns early unless `canUpdate` | Clamps to 0..1 (oil drain/check/fill forced to 1, and `SetCondition` also sets `IsExamined`). Writes `Condition`, calls `UpdateShaderParams` and re-reads the highlighter's renderers | Nothing to avoid; neither one has sounds, inventory or stats. `PrepareJob` writes `Condition` directly and then calls `UpdateShaderParams(true)` and `CMS_Highlighter.ReinitMaterials` |
| Body panel condition, dent, paint | `CarPart` has no setters, only fields. `CarLoader.SetCondition(CarPart, float)` (`0x1804F2720`) only writes `part.Condition`. Then `CarLoader.UpdateCarBodyPart(CarPart)` (`0x1804F2BC0`) or `SetConditionOnBody`/`OnDetails`, which update materials only. `SetDent(CarPart, float)`, `SetCarColorAndPaintType`, `SetCarPaintType`, `SetCarLivery` and `SetWashFactor` are visual or data only | Materials and shader values | `TweenCondition*` adds tweens. `<TakeOffCarPart>` (859 lines) does sound, `Inventory.Add`, item creation, tween movement and rust map |
| Body panel mounted state | `CarLoader.TakeOnCarPartFromSave(name)` (`0x1804FC370`) and `TakeOffCarPartFromSave(name)` (`0x1804FC010`) | Toggle `CarPart.Unmounted` on the part and its `ConnectedParts`, and set layer and shader on the `InteractiveObject` | `TakeOffCarPart`/`SwitchCarPart` coroutines: `SoundManager.PlaySFX`, `Inventory.Add` (removed panel as an item), LeanTween animation, bonus parts, `GameScript` raycast updates |
| Part unmount | `PartScript.HideBySavegame(bool withUnmountWith, CarLoader)` (`0x180FF5CD0`) | Returns early for special group 1 or when already unmounted. Saves position and rotation, sets `IsUnmounted=true`, layer 16, `ReplaceShader(1)`, `UnblockBlockParts`, toggles `enableOnUnmount`/`disableOnUnmount`, and moves `MountObjects` to full unmount position. For fluid parts it also writes `FluidsData` levels (it drains) | `PartScript.Hide()` → `<Hide>d__159` (`0x180D2A530`): dissolve tween and move, `Inventory.Add`/`AddGroup` of the removed item, `SoundManager.PlaySFX("PartTakeOff")`, **`AddPlayerExp(1)`**, `GameMode.SetCurrentMode`, `CheckMessageOnHide` (drains fluids, `WheelsAlignment.SetRandom`, **`stat_shed_oil`**), and a fluid-spill info window plus **`AddPlayerMoney(-cost)`** (50, …) when fluid was not drained. `FastUnmount` just starts `Hide`, so it has the same side effects |
| Part mount | No single silent native method. `PartScript.ShowBySaveGame()` (`0x180FF6820`), despite its name, also sets `IsUnmounted=true` and the unmounted layer and shader, so it is not a mount. A mount needs `IsUnmounted=false` plus `MountByGroup(true)` on `unmountWith`, `UnblockBlockParts(false)`, reversed `enableOnUnmount`/`disableOnUnmount`/`hideWhenUnmontingMounting`, and `MountObject.SetFullMountPosition`. That is what upstream's `CustomPartScriptMethod.ShowMounted` already replicates in managed code | — | `<ShowMounted>d__155` (`0x180D2C8A0`): **`Inventory.Delete`/`DeleteGroup`** of the item, **`AddPlayerExp(1)`**, `GameMode.SetCurrentMode`, `Examine`. `<DoMount>d__151`: also sound, colour and paint from the item, `TunePart`, mount animation. `FastMount` = `Show` + `ShowMounted` |
| Fluids | `FluidsData.SetLevel(level, type, id)` (`0x18128B6F0`), `SetCondition(cond, type, id)` (`0x18128B8D0`), `SetLevelAndCondition(…)`, `SetLevelAndConditionOnAll(…)` | Clamp and write `FluidData`. Pure data, no rendering | `FluidRefill.Hide` (money), the `PartScript` drain paths above |
| Wheels | `CarLoader.SetTire(id, isFront, setOriginal)` (`0x18050D640`) and `SetRim(…)` write only `WheelsData.Wheels[]` (and `OriginalWheels`). `SetWheelSize(w, s, p, WheelType)` (`0x1804EE730`), `SetET`, `SetWheelSizes()` rescale the existing meshes. `CarLoader.UpdateWheels` (1,938 lines) / `PartScript.ResizeWheel` rebuild meshes | Data, transforms, `CapSize`/`WheelBolts` | None found: no inventory, sound or money. `ResizeWheel` loads assets and instantiates objects, which is expensive |
| Inventory | `Inventory.Add(Item, showPopup:false)` (`0x180C79640`): list add plus a debug log; the popup only when `showPopup`. `Add(List<BaseItem>)` (`0x180C79A70`), `Delete(Item)` (`0x180C79B80`), `AddGroup(GroupItem)` (`0x180C7DEB0`), `DeleteGroup(long UID)` (`0x180C7DF10`) are plain list operations | — | `SellPerCondition` (money, sound, stat), `TryAddSpecialCase` (random), `Load` |

Also of interest: `PartScript.Update` sets `stat_unscrew` and starts `Hide`/`ShowMounted` when a bolt sequence
completes. That is why remote mounts must never go through the interactive path on a non-acting client.
`MountObject.UpdateCondition`, `SetFullMountPosition` and `SetFullUnmountPosition` are the per-bolt visual setters.

Size: **S–M**. The setters exist for condition, fluids, wheels, inventory and body panels. Only "mount a part
silently" has to be composed in managed code; upstream already does this, and the `<ShowMounted>` decompile shows
exactly what to leave out.

## e. Random damage and colour of a spawned car

Callers found with `xref.py`:

- customer job: `OrderGenerator.<TakeJob>d__19` and `PrepareJob`
- junkyard: `JunkyardGenerator.<CreateCar>d__19` (`0x180EAF7D0`)
- barn: `ShedManager.<CreateCar>d__30` (`0x180972690`)
- auctions and salvage: `AuctionHelper.<GenerateAuctionCar>`, `<GenerateAdditionalAuctionCar>`, `<GenerateSalvageCar>`
- salon: `SalonManager.<LoadCar>` (colour only)

Customer car, in `TakeJob` order:

1. `LoadCar`, `PlaceAtPosition`.
2. All fluids: `FluidsData.SetLevelAndCondition(Range(0.8,1), Range(0.8,1), CarFluidType.All)`.
3. License plate texture.
4. **`PrepareJob(carLoader, job)`** (`0x180C5D680`):
   - `SetRustRandomParts(gc, gc + Range(0, 0.1))`, where `gc = job.globalCondition`;
   - every `PartScript.Condition = clamp01(gc + Range(0, 0.2))`, written directly, then `UpdateShaderParams` and
     `CMS_Highlighter.ReinitMaterials`;
   - per task subtype, `PreparePart(parts, faultNo, percent, task, easyMode, gc)` with percents such as
     `Range(20,50)`, `Range(10,20)` or `Range(30,50)`. It uses the item fault table (`GameInventory.GetItemFault`)
     and `Physics.OverlapSphereNonAlloc` with `CarHelper.CalcRadialFaultsCondition` for neighbouring parts, and
     sets the faulty parts with `SetConditionNormal`;
   - fluids for `*Refill` (level 0.1–0.5) and `*Change` tasks;
   - `SetRandomHeadlampAlignment`, `WheelsAlignment.SetRandom`, `GetRandomIncreaseTuneValue` (`Range(5,16)`);
   - `PrepareBodyParts`;
   - a chance multiplier from `forXP` bands (0.75…2.0).
5. Colour: the job's `carColor` and `PaintType` from `GenerateNewJob`. On a roll `< 20` it is replaced by
   `GetRandomCarColor()` (the roll's upper bound is lost in the decompile and is likely 100): a random `AllowedColors` entry, or `CarHelper.GenerateRandomCarColor()` when the car has none.
6. When `gc < 0.55`, `SetRandomColorPanels(45, 0.2, 0.7)`.
7. `SetRandomCarLivery()` (`Random.value`, `LiveriesManager.GetLiveriesForCar`).
8. `SetMountObjectsRandomCondition(GetPartsGlobalCondition())` (bolts).
9. `SetRandomDent(DentCarOrderChance, DentPartOrderChance, DentValueOrder)`, all `GlobalData` static `Vector2`
   tables:
   - the car is dented when `Range(0,100) <= Range(carChance)`;
   - then each mounted `CarPart` is dented when `Range(partChance)` passes, with `Range(dentValue)`.
10. `job.otherPartsCondition = GetOtherPartsCondition(job)`.

Junkyard and barn (`CreateCar`):

- `SetRandomCarColor`, `CarHelper.SetRandomFactoryColor`;
- `FluidsData.SetRandomLevel`/`SetRandomCondition`;
- `SetRandomMissingPanels`, `SetRandomPartsConditions(min, max)` (`Range` per part via `PartScript.SetCondition`,
  except special group 1), `SetRustRandomParts`, `SetRandomMissingParts`, `SetRandomMissingSuspension`
  (`InteractiveObject.GenerateForAuction`);
- `SetRandomColorPanels`, `SetRandomCarLivery`, `SetMountObjectsRandomCondition`;
- `SetRandomDent` with the Junkyard/Barn tables, `EnableDust`, `SetWashFactor`, `EnableRustOutside`,
  `WheelsAlignment.SetRandom`, `SetWheelsColorsFromConfig`, `GetRandomMileage`.

The global condition comes from `CarLoader.LoadConfig`, which reads `logic/globalCondition`, `partsConditions` (default
(0, 0.7)) and `panelsConditions` (default (0.05, 0.6)) into `l_*`, and from `CarHelper.GetGlobalCondition(year)`.

Everything reads `UnityEngine.Random` (global state) and the car's own config, and writes straight into scene objects.
Server-authoritative rolls (row 16) can work like this:

- the server picks the scalar inputs: `globalCondition`, the colour index or HSV, the dent rolls, a seed;
- the spawning client runs the game's functions (or ports of them);
- the client uploads the resulting per-part baseline, which `CarPartsSync.UploadBaseline` already does.

Porting every `SetRandom*` to the server is **L**, because they walk the part hierarchy, which only exists in the
scene. Passing the scalars is **S–M**.

## Open points

- Row 3: decide whether the server only generates `Job`s (M, recommended) or also ports `PrepareJob`/`PreparePart`
  (L). Either way, `TotalPayout` should come from a server-side `CheckJob` port, or be validated against one.
- `GetCommissionForScene` has no direct caller. Look for inlined uses (`dxref.py` on its constants is not possible),
  for example in the auction and junkyard sell UI.
- `CarHelper.GetRandomMileage`, `GetMileagePriceMod`, `GetPaintBonus`/`GetWashFactorBonus` conditions,
  `CalcTuningCost`, `PreparePart`'s fault table and `GenerateMission`/`TakeMission` were decompiled (`out\clean\`)
  but not yet read in detail.
- `PartScript.ShowBySaveGame` sets `IsUnmounted = true`. Check in game whether the save loader calls it for
  unmounted parts only (that is what its body suggests), and remove it from any planned "mount" path.
- Verify in game two Expert-mode findings that look surprising: `AddPlayerExp` doubles XP when
  `currentDifficulty == 1` (Expert), and `CheckJobTask` halves the running `TaskBonus` after each task.
- XMM float arguments are lost in some decompiles (`Random.Range` bounds in `GenerateNewJob`/`PrepareJob`). Read
  them with `DumpAsm.java` and `f32.py` before porting exact ranges.
- `AuctionHelper.GetStartingPrice`/`GetPriceForRating`/`GetBidAmount` and `DragHelper.GetEntryFee` are decompiled
  but not summarised (row 10).
- Cpp2IL's ISIL and diffable-cs outputs were not tried; they are not needed while this route works.
