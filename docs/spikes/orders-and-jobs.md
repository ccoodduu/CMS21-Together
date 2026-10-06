# Spike: orders and jobs (native flow, hookability, server port size)

Roadmap: row 3 `sync-orders-and-jobs` (answers most of its task 1 "deferrable unknowns" statically) and the order/payout
part of row 16 `server-game-logic`. Method: static decompile only (setup in `native-decompile.md`), game not launched.
Date: 2026-10-06.

Sources, all under `%USERPROFILE%\CMS21-TestInstalls\native`:

- `out\clean\` (first spike: `OrderGenerator.*`, `JobHelper.*`, `GameScript.<EndJobCoroutine>`, `GlobalData.*`);
- `out\orders_clean\` and `out\orders2_clean\` (this spike; targets in `work\targets\orders*.txt`);
- `out\orders_xref.txt` (per-method callers from `xref.py`);
- the achievement and `ordersData` INIs, read from `Car Mechanic Simulator 2021_Data\resources.assets`.

Call and jump targets that matter were checked against raw x86 with capstone, because Ghidra merges tail-jump targets
into the caller. `Accept/DeclineOrderAction` and `Job.StartTimer` look as if they contain other methods' bodies, but they
end in `jmp`. Payout formulas are in `native-decompile.md` §a. This file adds the flow, the storage, the hook points and
the port size.

## Short answers

1. **Generation:** `OrderGenerator.Update` builds orders with `UnityEngine.Random` only. Timer: the first order comes
   10 s after start, then one every 30 s (scaled time), only while `GlobalData.Jobs < GetMaxOrdersAmount()`. What it
   reads: player level and XP, difficulty, the car pool (installed DLCs, `allowedPlaces=Order<lvl>`), the tiny
   `ordersData` INI, and the car config's `year`, colours and engine type. Generation does **not** read upgrades; the
   taker's upgrades are read later, in `PrepareJob`. **An open order has tasks but no parts.** `PrepareJob` fills the
   parts at take time.
2. **Storage and expiry:** there are two lists, `OrderGenerator.jobs` (open orders) and `selectedJobs` (taken jobs).
   `GlobalData.Jobs` is the open-order count. Each order has its own `Job.<Timer>` coroutine on `UIManager`, which
   counts `timeToEnd` (121–299 s) down and then calls the real `OrderGenerator.CancelJob(id)`. Missions get no timer.
3. **Accept/decline:** both build a `NewHash` and go through `NotificationCenter.NewButtonAccept`. Accept starts the
   `<TakeJob>d__19` iterator directly. **The `OrderGenerator.TakeJob` and `TakeMission` builders are never called on
   the UI path.**
4. **Customer car:** `<TakeJob>d__19` sets `customerCar`/`orderConnection` (`SetCustomerCar` is inlined) **before**
   `CarLoader.LoadCar`. It then calls `PrepareJob` (real call), adds the job to `selectedJobs` and calls `CancelJob(id)`.
   A full garage is refused (`ShowFullGarageInfo`); the car is not sent to parking.
5. **Task progress:** `Done` and `moneySpent` are derived from the car by `JobHelper.CheckJob`. **No writer of
   `JobPart.Found` was found** (it is only copied and saved). Row 3's `JobProgress` packet can very likely be dropped.
6. **EndJob:** pays the values that the **last `CheckJob` cached** (run when the car's order tab opened), not freshly
   computed ones. It uses the real calls `AddPlayerMoney(TotalPayout)` and `AddPlayerExp(XP)`. Stats: `stat_finish_order`,
   `stat_bonus_exp`, `stat_bonus_money`, `stat_finish_allmissions`, plus `stat_level` from `AddPlayerExp`. The details
   and the achievements tied to them are below.
7. **Server port:** order generation is about 16 functions and 10 KB native, **M**. Payout is about 20 functions,
   **M–L**, because it needs the per-part state. Car preparation should stay on the client.

## 1. Order generation

`OrderGenerator` fields (`dump.cs`): `orderTimer` 0x20, `nextOrderTime` 0x24, `jobs` 0x28, `selectedJobs` 0x30,
`LastUId` 0x38, `ordersData` (TextAsset) 0x40, `ordersDataINI` 0x58. The constructor sets `nextOrderTime = 10`,
`LastUId = -1`.

`Update` (`0x180C5B920`):

```c
if (NotificationCenter.IsGameReady && GameSettings.CanGenerateOrders) {
    max = GlobalData.GetMaxOrdersAmount();
    if (GlobalData.Jobs < max) orderTimer += Time.deltaTime;
    if (GlobalData.Jobs < max && orderTimer > nextOrderTime) { GenerateNewJob(); orderTimer = 0; nextOrderTime = 30; }
    if (GetMissionID() < MissionsAmount && CurrentMissionDone) GenerateMission(GetMissionID(), false);  // every frame while true
}
```

Order cap. `GetMaxOrdersAmount` (`0x180D7CD50`) uses `RealPlayerLevel = PlayerLevel + 1`, and returns 0 in the
Tutorial scene (`CurrentSceneType == 15`):

| RealPlayerLevel | 0–2 | 3–4 | 5–7 | 8–11 | 12–15 | 16–19 | ≥20 |
|---|---|---|---|---|---|---|---|
| max orders | 2 | 3 | 4 | 5 | 6 | 7 | 8 |

The slots in `IsOrderSlotUnlocked` use the same thresholds.

`GenerateNewJob` (`0x180C5BB40`). In order:

1. `GlobalData.AddJob(1)` runs **first**. If no car qualifies it returns without a job, and the count drifts by one.
   This is a native quirk.
2. `forXP = GetExperience(n, 6)`, where `n` counts the open jobs with `forXP <= PlayerExp`. If `n < 6` the result is
   `PlayerExp`. Otherwise it is `PlayerExp + 1500`, or `PlayerExp + 3000` when some open job already has `forXP > PlayerExp`.
3. Car pool:
   - `CarBundleLoader.GetAllAvailableCars()` (filtered by `PlatformManager.IsDLCInstalled` **on the generating client**);
   - minus the cars where `CarIsAvailableOnLevel(car, cfg, PlayerLevel+1)` is false: the config's
     `other/allowedPlaces` must contain `Order`, and `Helper.ParseAllowedPlacesLevel` must be `<=` the level;
   - minus `RemoveCarInPreGeneratedJobs` (cars already in open orders).
4. Pick: `Helper.CanGenerateDLCCar(out dlc)` gives a 30 % chance (`Range(0,101) > 70`) of a DLC car, only for an owned DLC
   where `DLC.IsReleaseDay()` is true (uncertain: the exact DLC fields at +0x20/+0x28). Otherwise `Random.Range(0, n)`.
5. Categories: `GetAvailableCategories(lvl, isElectric)` reads `ordersData`. That is the whole table:

   ```ini
   [AviableCategorysOnLevel]  1=Brakes 2=Susp 3=Engine 7=Exhaust 9=Gearbox 12=Body
   [AviableSubtypesOnLevel]   1=Brakes,General  2=Susp,Control  3=Susp,General,Engine,Oil,Engine,Filters
                              4=Engine,Timing,Susp,Knocking  6=Engine,General,Engine,Power,Engine,Noise,Engine,DontStart
                              7=Exhaust,General  9=Gearbox,General,Gearbox,Unit,Gearbox,Clutch  12=Body,General
   ```

   `notAvailableForElectric` filters the result. `Additionals` is always added.
6. Task count: `GetTasksAmount` = `Random.Range(1, k)`, with k = 1/3/4/5 for levels <5, <8, 8–9 and ≥10.
   `GetMaxJobSubTypes` gives the subtypes per task.
7. Easy mode per task: **Easy difficulty means always easy and Expert means never easy.** Otherwise
   `Range(1,101) <= GetChanceToEasyMode(lvl)`, which is 100/50/20/25/0 by level band.
8. Colour: 51 % HSV (`H 0–256, S 0.1–1, V 0.1–0.8`). Otherwise a random entry from the config's `allowedColors`.
9. `globalCondition = Random.Range(CarHelper.GetGlobalCondition(year))`; `year` defaults to 2000.
   `Mileage = CarHelper.GetRandomMileage(year, 4)`.
10. `id = ++LastUId`. A 30 % chance of a bonus, `BonusToExp` or `BonusToMoney` at 50/50.
    `timeToEnd = (float)Random.Range(121, 300)`. Then `Job.StartTimer()`, `jobs.Add(job)` and
    `UIManager.UpdateJobs(jobs, job)`.

What an open order contains: `id, forXP, carFile, configVersion, carColor/PaintType, Mileage, globalCondition, jobType[],
jobTasks[] (type, subtype, easyMode, desc)`, the bonus flags, `timeToEnd` and `CanDelete` (true from the `Job` constructor). It
has **no `Parts`, `oilLevel` or `IncreaseTuneValue`, and no `Additionals` subtype choice**. `PrepareJob` creates all of
these at take time from the taker's car and the taker's upgrades (`wheelsAlignmentSystem`, `headlampAlignmentSystem`,
`dyno` decide which `Additionals` are possible). So row 3's `JobStarted` must carry the job **after** `PrepareJob`.

Randomness: everything comes from the global `UnityEngine.Random` (`RandomRangeInt` and `Range(float,float)`), with no
seed of its own.

## 2. Story missions and the tutorial

- `GenerateMission(id, forTutorial)` (`0x180C5FDF0`):
  - It returns at once when `!forTutorial && id == -1`.
  - It sets `CurrentMissionDone = false` and parses the TextAsset `Missions/Mission{id}`, or
    `MissionsTutorial/TutorialMission{id}` for the tutorial.
  - **Tutorial marker:** a tutorial mission gets `job.id = 0` and does not advance `LastUId`. Normal missions get
    `++LastUId`. Both get `IsMission = true`.
  - The data comes entirely from the INI: car, colours, mileage, fixed per-part conditions and fluid levels. That is 30
    mission files (`missionID` 0–29) plus 6 tutorial files in `resources.assets`.
  - No `StartTimer`, so missions never expire (`timeToEnd` 0). It ends with `jobs.Add`, `AddJob(1)` and `UpdateJobs`.
- `GetMissionID` = `MissionsFinished` if `< MissionsAmount` and `< PlayerLevel+1`, else -1. At most one mission per level.
- `<TakeMission>d__22` (`0x180A74590`, 13.8 KB) applies the INI. **It does not call `PrepareJob` or `CancelJob`.** It
  removes the job from `jobs` with an inlined `List.Remove`, then calls `AddJob(-1)`, `selectedJobs.Add`, sets
  `IsStoryMissionInProgress = true`, calls `GarageLoader.Save()` (outside the tutorial) and invokes `OnTakeMission`.
  If the mission INI's `carToLoad` no longer matches `job.carFile`, it drops the job and regenerates the mission.
- Mission fields:

  | Step | `CurrentMissionDone` | `IsStoryMissionInProgress` | `MissionsFinished` |
  |---|---|---|---|
  | `GenerateMission` | false | — | — |
  | `TakeMission` end | — | true | — |
  | `EndJobCoroutine` (if `IsMission`, not in the tutorial) | true | false | +1 |

  When `MissionsFinished` reaches `MissionsAmount`, it also sets `stat_finish_allmissions`. The next `Update` then
  generates the next mission.
- Tutorial order-slot lock: `OrderSlotLockReason.Tutorial` (2) is set **only when `CurrentSceneType == 15`**. Nothing on
  the order path reads `ProfileData.FinishedTutorial`. So row 3's D13 one-liner (`FinishedTutorial = true`) is not
  needed for order slots.
- `TutorialsWindow.RunTutorialAction` (`0x1808506D0`, a UI delegate) opens window `0x24` with the item's
  `TutorialContentType`. It may only show tutorial content, not start the tutorial scene (uncertain; check in game).

## 3. Storage, save and expiry

- Open orders are in `jobs`, taken jobs in `selectedJobs`. `GlobalData.Jobs` is the open-order count:
  - `+1` in `GenerateNewJob` (before success) and `GenerateMission`;
  - `-1` in `CancelJob` (only when the id is found in `jobs`) and in `TakeMission`;
  - set to the loaded count in `Load`.

  It is not saved.
- Save format: `ProfileData+0xE0` is `NewJobsData { jobs, selectedJobs, nextOrderTime, orderTimer, LastUId,
  CurrentMissionDone }`. Each entry is a `NewJobWrapper`, and its tasks keep `moneySpent`, `_done` and the parts'
  `ID/Done/Found`.
- **Not saved:** `CanDelete`, `oilLevel`, `TaskBonus/JobBonus/TotalPayout/XP`, `carFactoryColor`. After a reload,
  `job.oilLevel` is 0, which weakens EndJob's oil check.
- `Load` (`0x180C64250`):
  - It returns early when `!CanGenerateOrders`.
  - It drops non-mission orders with `timeToEnd ≈ 0`.
  - It calls `StartTimer` only on non-missions.
  - It restores a `selectedJobs` entry only if the loader at `carLoaderID` holds a car whose name matches `carFile`.
  - At the end it may call `GenerateMission` (`CanRegenerateMission`).
- Timer:
  - `Job.StartTimer` (`0x18186DCD0`): if `timeToEnd ≈ 0` it does a **tail jump to `CancelJob(id)`** (a real call).
    Otherwise it builds `<Timer>d__51(timeToEnd)` itself (the `Job.Timer` builder is inlined and has no callers) and
    starts it **on `UIManager`**.
  - `<Timer>d__51` (`0x180EAF380`): every `YieldInstructions.WaitForSecond`, which is `WaitForSeconds(1)` in **scaled
    time**, it does `timeToEnd -= 1`. **The unit is seconds, and the timer pauses at `timeScale 0`.** At 0:
    - if `!CanDelete`, it waits one frame at a time forever;
    - otherwise it calls `CancelJob(id)` (real call at `0x180EAF5BA`). If `Jobs < 1`, it then hides the orders window
      and shows `GUI_Orders_Nojobs`.
  - `StopTimer` sets `haveToStopTimer` and stops the coroutine.
- `CancelJob(id)` (`0x180C5F9F0`):
  - first it looks in `jobs`: `StopTimer`, `RemoveAt`, `AddJob(-1)`;
  - if the id is not in `jobs`: it looks in `selectedJobs` (`StopTimer`, `RemoveAt`) and sets **`orderTimer = 0`**;
  - in both cases it then refreshes the orders window if it is open.

## 4. Accept and decline

| Step | Method (VA) | Notes |
|---|---|---|
| button | `OrdersWindow.AcceptOrderAction` `0x1809E4730` | No direct callers (UI delegate). Builds `NewHash{WindowType=MenuOrders, Type=TakeJob, Job=currentJob}` and **tail-jumps** to `NotificationCenter.NewButtonAccept`. Does not check whether the window is visible: only `currentJob` matters |
| button | `OrdersWindow.DeclineOrderAction` `0x1809E49F0` | Returns at once for `IsMission`. Same hash with `Type=DeclineJob`, then a real call to `NewButtonAccept` |
| dispatch | `NotificationCenter.NewButtonAccept(NewHash)` `0x1809DC9D0` | `DeclineJob`: `OrderGenerator.CancelJob(job.id)` (real); if `Jobs <= 0`, hide window 7 and `GUI_Orders_Nojobs`. `TakeJob`: **`job.CanDelete = false`**, then `new <TakeJob>d__19` or `new <TakeMission>d__22` with `movePlayerToCar = GameMode.mode < 2`, started on **NotificationCenter** |

Side effects worth knowing:

- `CanDelete = false` is set before the take can fail. A take refused for a full garage leaves the order in `jobs` with
  a timer that never removes it.
- A direct `orderGenerator.StartCoroutine(orderGenerator.TakeJob(id, true))` (row 3's fallback) skips that line. Call
  `job.StopTimer()` or set `CanDelete = false` first, or the native timer can cancel the job in the middle of the take.

## 5. Taking a job: `<TakeJob>d__19.MoveNext` (`0x180A733E0`)

State 0:

1. Find the job in `jobs`. Set `orderTimer = 0` and `nextOrderTime = 30`. **Accepting resets the generator countdown.**
2. If `CarPlaceManager.GetAmountOfFreePlacesInGarage() == 0`, call `UIManager.ShowFullGarageInfo()` (real,
   `0x180A7393E`) and stop. **There is no parking fallback.**
3. If `CarLoaderPlaces.GetPlaceForLoadCar()` is null, call `ShowInfoWindow("GUI_RemoveCarFromArrivalPlace")` and stop.
4. Hide window 7, change the input, fade in.

Then:

1. `CheckOrdersFilesCompatibility`.
2. `loader.customerCar = true; loader.orderConnection = job.id; CarInfoData.CarFrom = 3; ConfigVersion = job.configVersion`.
   **These are written before `LoadCar`**, so a `CarLoader.LoadCar` hook can read the job id from `orderConnection`.
3. `StartCoroutine(CarLoader.LoadCar(job.carFile))` (real call `0x180A73A8F`). Wait until the car is loaded.
4. `PlaceAtPosition`, then all fluids `Range(0.8,1)` for level and condition, then the license plates.
5. **`OrderGenerator.PrepareJob(loader, job)`**, a real call at `0x180A73D6E`. Its only caller is this method. Then the
   colour, the panels, the livery, the bolt conditions and `SetRandomDent` (see `native-decompile.md` §e).
6. `job.otherPartsCondition = GetOtherPartsCondition`, then `job.carLoaderID = GetCarLoaderId(loader)`,
   `selectedJobs.Add(job)`, **`CancelJob(id)`** (real, `0x180A7433D`), `UpdateJobs`, fade out, input back.

`TakeJob` does not call `GarageLoader.Save`; `TakeMission` does.

## 6. Task progress

- `JobHelper.CheckJob(CarLoader, ref Job)` (`0x18186E600`) resets the job totals.
  `CheckJobTask` resets `task.moneySpent = 0` and recomputes `Done`/`moneySpent` from part condition and mount state,
  fluids, alignment, tuning and factory colour. **Both are derived and reproducible on any client with the same car state.**
- `MoneySpent` is the **value of the repaired parts**, not the money spent in shops.
- `JobPart.Found` (struct field 0x11): the only reference in every decompiled method is a copy in `CheckJobTask`, and
  `Load`/`Save` serialise it. **No writer was found** (uncertain: only ~720 methods were decompiled). It is probably
  vestigial.
- Callers of `CheckJob`: only `OrderTab.SetupData` (`0x180C6AC80`) and `OrderSummaryPageManager.PrepareFirstPage`
  (`0x180C67B27`), so it runs when the car's order UI opens.

## 7. Ending a job and the payout

Entry points:

- `OrderTab`/`OrderSummaryTab` `.FinishJob`, `.FinishMission` and `.SubmitAction`, and the ask-window lambda
  `<FinishJob>g__EndJobAskWindowAction`, all call or tail-jump to `GameScript.EndJob(job, loader)` (`0x180E95830`).
- `EndJob` builds `<EndJobCoroutine>d__139` itself. **The `GameScript.EndJobCoroutine` builder (`0x180E959A0`) has no
  callers, so a patch on it never fires.**
- The coroutine is `GameScript.<EndJobCoroutine>d__139.MoveNext` (`0x1808948D0`).

The coroutine, verified with raw calls:

1. If `GameScript.OnEndJob` has subscribers: invoke them and **return**, with no payout. The tutorial uses this
   (uncertain who subscribes). Do not subscribe to it from the mod.
2. Gate checks: missing bolts, `CheckIfHaveBody`, oil, `CheckCarHaveFluids`, `GetOtherPartsCondition < job.otherPartsCondition - 0.005`,
   and front/rear wheel sizes. If one fails, it shows an info window and ends. Nothing is paid and no further calls are
   made, so a commit-point hook simply never fires.
3. Outside the Tutorial scene:
   - `Inventory.TryAddSpecialCase(job.IsMission)` (`0x180C7D1B0`) if `IsCompleted`. A mission always gives one
     `specialCase` item. Otherwise there is a `Range(0,101) < 25` chance when the `luck` upgrade is unlocked.
   - **`GlobalData.AddPlayerMoney(job.TotalPayout)`**, a real call at `0x1808955B4`, even when not completed.
   - **`GlobalData.AddPlayerExp(job.XP, false)`**, a real call at `0x1808955FA`, only if `IsCompleted`.
4. `OrderGenerator.CancelJob(job.id)` (real, `0x180895658`). The job is in `selectedJobs`, so this also resets `orderTimer`.
5. Mission bookkeeping (§2). Then `CarLoader.DeleteCar(true)`, `GameScript.SetCarLoaderOverNull`,
   `GarageOnFootWithoutFader`, `GarageLoader.Save()` and re-enabling the raycast.
6. Stats, through `PlatformManager.platform(+0x18).AchievementSystem(+0x18)` and the virtual `IncrementStat` at vtable
   +0x1D8:
   - `stat_finish_order` if completed;
   - `stat_bonus_exp` if completed and `BonusToExp`;
   - `stat_bonus_money` if completed and `BonusToMoney`.

The payout uses the **cached** `TotalPayout`/`XP`/`IsCompleted` of the last `CheckJob` on this client. In
multiplayer, a player who finishes while the order tab shows stale data gets a stale payout. Running
`JobHelper.CheckJob(loader, ref job)` in a `GameScript.EndJob` prefix makes it fresh.

Steam stats and achievements of a finished job:

| Stat | Set by | Achievements (`RelatedStatID`, threshold `ProgressMax`) |
|---|---|---|
| `stat_finish_order` | `EndJobCoroutine`, virtual call | `achiv_finish_order_stage1` (1), `achiv_finish_order_stage2` (100) |
| `stat_bonus_exp` | `EndJobCoroutine`, virtual call | `achiv_bonus_exp_stage1` (1) |
| `stat_bonus_money` | `EndJobCoroutine`, virtual call | `achiv_bonus_money_stage1` (1) |
| `stat_finish_allmissions` | `EndJobCoroutine` → `PlatformManager.IncrementStat` (`0x1812E1110`) | `achiv_finish_allmissions_stage1` (1) |
| `stat_level` | `GlobalData.AddPlayerExp` on level-up | `achiv_level_stage1/2/3` (5, 20, 50) |

How the stat calls work:

- `PlatformManager.IncrementStat` tail-calls the same virtual.
- On Steam that virtual is `SteamAchievements.IncrementStat` (`0x180D99D80`). It calls `BaseAchievements.IncrementStat`
  (`0x180B4D650`), which adds to `Statistic.Value` (or to `ValueSandbox` on Sandbox difficulty), then runs `UpdateProgress`
  and `Unlock` for every related achievement whose threshold is reached.
- After that, `SteamUserStats.SetStat` writes the stat, with the prefix `sandbox_` on Sandbox, and `SendStats` follows.
- A hook on `SteamAchievements.IncrementStat` sees every path.
- Row 3's `JobStatsAwarder` can call the public `GameManager.Instance.PlatformManager.IncrementStat(id, 1)`. That
  reproduces the stats and the achievement unlocks exactly.

## 8. Hookability

"Real" means a direct `call`/`jmp` to the method exists on the gameplay path, so a Harmony patch fires.

| Method | VA | Verdict |
|---|---|---|
| `OrderGenerator.Update` | `0x180C5B920` | Unity message, hookable |
| `OrderGenerator.GenerateNewJob` | `0x180C5BB40` | Real (only from `Update`) |
| `OrderGenerator.GenerateMission` | `0x180C5FDF0` | Real (`Update`, `Load`, `TakeMission`, tutorial `TakeJob.Init`) |
| `OrderGenerator.TakeJob` / `TakeMission` (builders) | `0x180C5D5D0` / `0x180C5FD40` | **Never called. Hooks never fire.** Use `<TakeJob>d__19.MoveNext` `0x180A733E0` / `<TakeMission>d__22.MoveNext` `0x180A74590`, or the accept action |
| `OrderGenerator.PrepareJob` | `0x180C5D680` | Real, jobs only. **Missions never call it** |
| `OrderGenerator.CancelJob` | `0x180C5F9F0` | Real from decline, timer, `StartTimer`, take, end, `DeleteCorruptedOrders`. **Not from `TakeMission`** |
| `OrderGenerator.Load` / `Save` | `0x180C64250` / `0x180C65460` | Real (`GarageLoader.<Load>d__14` / `GarageLoader.Save`) |
| `OrderGenerator.AddJob(Job, List)` | `0x180C5D020` | No callers (dead code) |
| `OrderGenerator.Start` / `Prepare` | `0x180C5B8B0` | **Same address** (identical bodies): patching one patches both |
| `OrdersWindow.AcceptOrderAction` / `DeclineOrderAction` | `0x1809E4730` / `0x1809E49F0` | Only called through UI delegates. Expected to fire, because the delegate calls the patched native entry; confirm with a trace |
| `NotificationCenter.NewButtonAccept` | `0x1809DC9D0` | Real (a shared dispatcher, so filter on `Type`) |
| `Job.StartTimer` / `StopTimer` | `0x18186DCD0` / `0x18186DED0` | Real. The `Job.Timer` builder is inlined into `StartTimer` |
| `GameScript.EndJob` | `0x180E95830` | Real (all finish buttons) |
| `GameScript.EndJobCoroutine` | `0x180E959A0` | **Inlined into `EndJob`. Never fires** |
| `GameScript.<EndJobCoroutine>d__139.MoveNext` | `0x1808948D0` | Hookable (iterator, runs once per resume) |
| `GlobalData.AddPlayerMoney` / `AddPlayerExp` / `AddJob` | `0x180D7B580` / `0x180D7BCC0` / `0x180D7C430` | Real on the job paths |
| `JobHelper.CheckJob` | `0x18186E600` | Real, but **only from the UI** (not from `EndJob`) |
| `CarLoader.SetCustomerCar` | `0x180505800` | **No callers. Inlined into `d__19`/`d__22`** |
| `CarLoader.LoadCar` | `0x1804CF010` | Real from `d__19` (`orderConnection` is already set) |
| `UIManager.ShowFullGarageInfo` / `UpdateJobs` | `0x180B29140` / `0x180B29CE0` | Real |
| `Inventory.TryAddSpecialCase` | `0x180C7D1B0` | Real |
| `SteamAchievements.IncrementStat` | `0x180D99D80` | Hookable. Reached through the vtable and `PlatformManager.IncrementStat` |

Impact on row 3's design:

- **D4:** re-invoking `AcceptOrderAction` with only `currentJob` set should work. Do **not** rely on a `TakeJob`
  prefix.
- **D5:**
  - `PrepareJob`'s postfix works for jobs. Missions need a `<TakeMission>d__22.MoveNext` postfix that checks for
    `selectedJobs` growth, or an `OnTakeMission` subscription.
  - The `SetCustomerCar` hook can be dropped. Read `loader.orderConnection` in the `LoadCar` hook instead of keeping a
    separate `PendingTake`.
- **D6:** expiry goes through `CancelJob`, so the planned prefix sees it. The unit is scaled seconds.
- **D7:** drop `JobProgress` unless a runtime trace finds a `Found` writer.
- **D8:**
  - The capture of `AddPlayerMoney` and `AddPlayerExp` works.
  - The natural commit point is the real `CancelJob(job.id)` call inside `d__139`, which comes right after the payout.
  - Add a `CheckJob` refresh in the `EndJob` prefix.
- **D13:** no slot lock outside the Tutorial scene. The tutorial-mission marker is `IsMission && id == 0`, or
  `GenerateMission(_, forTutorial: true)`.

## 9. Server-side port (row 16): size and data

**Order generation (recommended to port), M.**

Functions, about 16 and about 10 KB native, or 400–500 lines of C#:

- `Update`'s timer and `GetMaxOrdersAmount`;
- `GenerateNewJob`;
- `GetExperience`, `GetChanceToEasyMode`, `GetTasksAmount`, `GetMaxJobSubTypes`;
- `GetAvailableCategories`, `GetRandomColorHSV`, `RemoveCarInPreGeneratedJobs`;
- `CarBundleLoader.GetAllAvailableCars`, `CarIsAvailableOnLevel` and `Helper.ParseAllowedPlacesLevel`;
- `Helper.CanGenerateDLCCar`/`GetRandomDLCCar`;
- `CarHelper.GetGlobalCondition`/`GetRandomMileage`;
- the expiry timer.

Missions add `GenerateMission` (INI → `Job`, about 150 lines) and `GetMissionID`/`CanRegenerateMission`.

Data the exporter (row 9) has to provide:

- the `ordersData` INI (15 lines, above) and `notAvailableForElectric`/`c_typeName` from the `OrderGenerator`
  constructor;
- per car and config: `allowedPlaces` (Order level), `year`, `allowedColors` (with paint type), `engine/type` → the
  engine INI's `isElectric`, the DLC id, and the config count;
- the 30 `Missions/Mission{n}` INIs; `MissionsAmount`;
- **DLC ownership of every connected player**. The native pool is the generating client's installed DLCs. A shared pool
  must be the intersection, otherwise another player cannot load the car. This also applies to row 3's elected generator.

The server reads its inputs from the shared state: level, XP, difficulty and the open-order list.

**Car preparation (`PrepareJob`, `PreparePart`, `PrepareBodyParts`, `TakeMission`): keep it on the taking client.**
It walks live `PartScript`s, uses physics overlap for radial faults and reads the taker's upgrades. The server sends
the order; the taker's `PrepareJob` result goes back as the job's parts plus the car baseline.

**Payout (`CheckJob`), M–L.** About 20 functions, 300–400 lines:

- `CheckJob`, `CheckJobTask`, `GetXPFromDifficultyMod`;
- `CalcTuningPartsValue`/`IsTuningTaskDone`;
- `CarLoader.GetPartPrice`, `Helper.GetPrice(PartScript)`, the tire and rim price;
- `GetItemDifficultMod`;
- the fluid averages, both alignments, `CarIsInFactoryColor`;
- the six `EndJob` gate checks;
- `TryAddSpecialCase`.

The formulas are small. Every input, though, is per-part car state (condition, mount state, `GameObject` path),
fluids, alignment, tuning and paint, and the server has that only once rows 1 and 4 store full per-part state. It also
needs the item database (price, special group, `ItemFault.DifficultMod`) and the body `PartProperty` prices.

Recommended staging:

1. The client runs `CheckJob` in the `EndJob` prefix and reports the result.
2. The server bounds-checks the result.
3. Later, the server compares it against its own `CheckJob` port.

## 10. Still needs a runtime trace

- Whether the `Accept`/`DeclineOrderAction` patches fire on a real button press (UI delegate path).
- `JobPart.Found`: confirm that nothing sets it, for example by watching the saved `Found` flags after examining every
  part of a job car.
- Who subscribes to `GameScript.OnEndJob` (tutorial only?). If anything subscribes in a normal garage, `EndJob` pays
  nothing.
- `RunTutorialAction`: does it start the tutorial scene or only show content?
- The DLC release-day branch in `CanGenerateDLCCar` (the fields at DLC+0x20/+0x28).
- The take duration for row 3's claim timeouts. With no full garage, the take is one `LoadCar` plus `PrepareJob`.
- The `GarageLoader.Save` skip condition in `d__139` (`bVar11`; not resolved).
