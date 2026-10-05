# Review: sync-orders-and-jobs (2026-10-05)

Checked against ROADMAP integration notes, QUESTIONS.md (answered), the row 7 contract (groups 1–2, current
version), rows 1/2/4/6 drafts, the decompiled stubs and the current code. `openspec validate --strict` passes.

Hooks spot-checked in the stubs, all present with the used signatures: `OrderGenerator.{Update, GenerateNewJob,
GenerateMission(int,bool), TakeJob/TakeMission(int,bool) (IEnumerator), PrepareJob(CarLoader,Job), CancelJob(int),
Load, Save, jobs, selectedJobs, LastUId}`, `Job.{id, carLoaderID, timeToEnd, IsMission, CanDelete, TotalPayout, XP,
StartTimer, StopTimer, Timer(float)}`, `JobTask`/`JobPart` fields, `OrdersWindow.{currentJob, AcceptOrderAction,
DeclineOrderAction, UpdateJobs}`, `UIManager.{UpdateJobs, ShowInfoWindow, ShowFullGarageInfo}`,
`GameScript.{EndJob, EndJobCoroutine, _EndJobCoroutine_d__139}`, `JobHelper.CheckJob(CarLoader, ref Job)`,
`GlobalData.{Jobs, AddJob, GetMaxOrdersAmount, IsOrderSlotUnlocked, AddPlayerMoney, AddPlayerExp, mission fields}`,
`CarLoader.{customerCar, orderConnection, SetCustomerCar, IsCarLoaded, DeleteCar(), DeleteCar(bool)}`,
`ProfileData.FinishedTutorial`, `TutorialsWindow.RunTutorialAction`. Repo references (`CarSpawnHooks.Suppress`,
`CarSpawnManager.RequestCarSpawn`, `StatsHooks.AddPlayerExpPrefix`, `StatsHandlers`, `ClientData.IsServerUpdating`,
`Compare-HarnessDumps -Sections`) exist.

## Findings

| # | Sev | Finding | Action |
|---|-----|---------|--------|
| 1 | blocker | Initial-sync deadlock: D9 applied jobs only after `NotificationCenter.IsGameReady`, but `CustomLoad` sets that only after sync finishes, and row 7's `SyncTracker` waits for `Applied("jobs")`. | Gate is now "garage scene + `OrderGenerator.Load` ran"; D9, task 4.2 verifies `syncAcked`. |
| 2 | blocker | Contract mismatch (coordinator): `JobsState` was sent from `OnAskForSync` before the inventory and stored only as a `ModGameState` field. | `JobsSection` = `ISaveSection` `jobs` v1 + `ISnapshotProvider` at `SyncOrder` 400 (D3, D10, tasks 3.1). |
| 3 | blocker | No locking: expiry/claim-timeout tick runs on the server UI thread, handlers on transport threads. | All `JobsState` access under `GameDataManager.StateLock`; `Tick` takes it (D3, 3.2). |
| 4 | major | Generator election (coordinator + own): not garage-only, "lowest id" flapped when a lower id rejoined, `OrderGenerated` accepted from any client. | Eligible = `InSession` + presence scene `Garage`, sticky, re-elect on leave/`SceneChanged`; others' `OrderGenerated` dropped (D1, spec scenario, 3.3, 7.2). |
| 5 | major | Tutorial (user decision) was only a non-goal. Fresh session profile may lock order slots (`OrderSlotLockReason.Tutorial`); pause menu can start tutorials. | D13, new requirement, tasks 3.4/5.2/5.9, checked in 7.1. |
| 6 | major | Parked customer cars and lost job cars: `ActiveJobEntry.CarLoaderId` went stale; job end could delete another car on that loader; row 1 drops cars without baseline on load, leaving jobs that can never end. | D12 (loader follows the car, guard `LoadedCars[..].JobID == JobId`, lost car -> order reopens), spec scenarios, 3.8, park step in 7.1. |
| 7 | major | Story missions: only end counters were synced; `IsStoryMissionInProgress` is set on take. | D14, `ModMissionState` in `JobStarted`/`JobEndRequest`/`JobRemoved`/`JobsState`, mission step in 7.1. |
| 8 | major | Take failure paths: late `JobStarted` after claim timeout, `CarSpawnRejected` for a job spawn, full garage. | D4/D5: `TakeAborted` to sender, `AbortTake` on rejection and `ShowFullGarageInfo`; timeouts 60/45 s (two instances on one PC). |
| 9 | major | XP double-count window: an `AddPlayerExp` after the commit point would also go out as `StatsAction`; payout capture breaks if `AddPlayerMoney` is inlined. | Context captures until it ends; fallback money diff / `job.XP` (D8, 5.8). |
| 10 | major | Spike not drivable: "one instance offline" has no harness path (harness only reaches the menu), and the driving verbs came after the spike. | Verbs moved to 1.2; spike runs connected (job code still vanilla) as `jobs-trace.ps1`, also traces full garage, parking, missions, tutorial slot lock. |
| 11 | major | Scenarios missed restart, expiry pause, claim release, double end, tutorial; natural order generation made "3 orders" flaky. | `jobs-restart.ps1`, `orders-autogen off`, `net-hold` for the double accept, `job-end-dup`, tutorial checks. |
| 12 | minor | Vague `sync-car-parts` entry point. | `CarPartsSync.UploadBaseline` + `CarDetailsSync.MarkDirty` from the `PrepareJob` postfix. |
| 13 | minor | Fallback started an Il2Cpp `IEnumerator` with `MelonCoroutines.Start`. | `orderGenerator.StartCoroutine(...)`. |
| 14 | minor | `Compare-HarnessDumps -Sections` already exists; `Wait-HarnessDump` was added by rows 2, 3 and 6. | Reuse row 6's helper. |
| 15 | minor | `GlobalData.Jobs` assumed to be the open-order count; it is not saved and looks like a UI counter. | Spike confirms; cap check also works from `jobs.Count`. |
| 16 | minor | `ClientData.Reset()` runs on every garage load, not only on disconnect. | Mirror cleared there and refilled by the snapshot (D9). |
| 17 | minor | Restart: claims and claimed orders. | `Claimed` loads as `Open`; claimed orders do not count down (D6, D11, spec). |
| 18 | minor | The native `DeleteCar()` in `EndJob` also sends `CarSpawnDelete`. | Documented as idempotent (D8). |

Not changed: generation on a client (no server-side generator) and trusting client payout. Both are reasonable for
co-op and are already listed in QUESTIONS.md.

## Cross-change conflicts to fix elsewhere

- `sync-car-placement-and-lifts`: the unpark path (`CarUnparkRequest` -> `CarSpawnResponse`) must carry `IsJob`/`JobID`
  from the car's `customerCar`/`orderConnection`. Its park handler must call `JobsService.OnJobCarRemoved(loader)`.
  Its A5 ("row 3 adds cars through `CarParkRequest{-1}`") only applies if the spike shows that a full garage parks
  the customer car.
- `sync-car-parts`: when it drops a car on save load (no baseline) or deletes a car because the spawner left before
  its baseline, it must notify `JobsService` so the job reopens (D12).
- `sync-players-and-scenes`: the integration note "every garage-bound handler uses `ClientScene.IsGarageReady`"
  conflicts with snapshot handlers, because `IsGarageReady`/`LocalScene = Garage` only become true after `SyncEnd`.
  Any snapshot handler gated on it deadlocks the `SyncTracker`. The note should exempt snapshot apply, or row 6
  should set `LocalScene` at garage load. Row 3 also needs `PresenceRegistry` to expose a client's scene to server
  modules (it has `InScene(scene)`, which is enough).
- `session-persistence-and-rejoin`: it owns `ModGameManager.StartGame` changes (profile slot). D13 may add
  `ProfileData.FinishedTutorial = true` there, so the two changes need to coordinate.
- Rows 2 and 5: `Wait-HarnessDump` is now owned by row 6. Row 5's `Wait-HarnessDumpsEqual` could build on it.

## Open questions for the user

1. While nobody is in the garage (everyone at the junkyard or test track), should new orders stop arriving?
   **Default: yes**, because the generator must be in the garage. Open orders still expire while anyone is connected.
2. If an active job's customer car is lost (a server save problem, or a crash during the take), should the job go back to the
   open list or be dropped? **Default: back to the open list**, with its original time.
