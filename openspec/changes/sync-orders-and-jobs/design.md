# Design

## Context

See proposal.md for the why. Current state on `main`:

- Nothing touches orders. Each client's `OrderGenerator` (`Singleton<GameManager>.Instance.OrderGenerator`)
  loads `jobs`/`selectedJobs` from its own profile in `OrderGenerator.Load()` (called from
  `LoaderAddition.VanillaLoad` before `AskForSync`) and generates new ones in `Update()`.
- Every garage load while connected (first join and every return from another scene) runs
  `LoaderAddition.CustomLoad`: `VanillaLoad` (sets `NotificationCenter.IsGameReady = false`, calls
  `OrderGenerator.Load()`), then `ClientData.Reset()`, `AskForSync`, wait for `IsInitialSyncFinished`, and only
  then `IsGameReady = true`. Anything initial sync waits for must therefore not wait for `IsGameReady`.
- `ModGameManager.StartGame` creates a fresh `ProfileData` and loads the garage directly; the tutorial scene is
  never loaded by that path, but it can be started from the pause menu (`PauseQuitWindow.CreateTutorialsButton`
  -> `TutorialsWindow.RunTutorialAction()`), and `ProfileData.FinishedTutorial` of the fresh profile may lock
  order slots (`GlobalData.IsOrderSlotUnlocked(..., out OrderSlotLockReason)` has a `Tutorial` reason).
- `CarSpawnHooks.LoadCarHook` already forwards `CarLoader.LoadCar` to the server, and the native
  `TakeJob` coroutine keeps running on the sender. `CarSpawnManager` sends `IsJob = carLoader.customerCar`
  and always `JobID = -1`. The server stores the spawn in `CarState.LoadedCars` without checking if the
  loader is free.
- Money is server-authoritative but there is no generic `AddPlayerMoney` hook: a job payout added
  locally is overwritten by the next `WorldState`. Exp goes through `StatsHooks.AddPlayerExpPrefix` ->
  `StatsActionPacket` -> `StatsHandlers` (shared level/exp in `WorldState`).
- The server has no game code. It ticks from `ServerWindow` -> `Server.Update()` on the UI thread while packet
  handlers run on transport threads; `ServerTime.Time` is a stopwatch; `GameDataManager` saves `ModGameState` as
  JSON. `session-persistence-and-rejoin` task groups 1–2 (implemented first) add `GameDataManager.StateLock`,
  `ISaveSection`/`ISnapshotProvider` with `[SessionSection]` discovery, `SyncOrder` (`jobs` = 400) and
  `SyncBegin`/`SyncEnd`/`SyncAck`; this change plugs into that contract.

Game API used (all verified in the decompiled stubs, `Assembly-CSharp-firstpass`):
`OrderGenerator.{jobs, selectedJobs, LastUId, orderTimer, nextOrderTime, Update(), GenerateNewJob(),
GenerateMission(int,bool), TakeJob(int,bool), TakeMission(int,bool), PrepareJob(CarLoader,Job), CancelJob(int),
GetJobForCarLoader(int), Load(), Save()}`, `Job.{id, carLoaderID, carFile, configVersion, timeToEnd, jobTasks,
IsMission, MissionID, CanDelete, TotalPayout, XP, MoneySpent, IsCompleted, StartTimer(), StopTimer()}`,
`JobTask.{type, subtype, Parts, Done, moneySpent}`, `JobPart.{ID, Done, Found}`,
`OrdersWindow.{currentJob, AcceptOrderAction(), DeclineOrderAction(), UpdateJobs(List<Job>)}`,
`UIManager.{OrdersWindow, UpdateJobs(List<Job>, Job), ShowInfoWindow(string), ShowFullGarageInfo()}`,
`GameScript.{EndJob(Job,CarLoader), EndJobCoroutine(Job,CarLoader), CurrentSceneType}` (iterator class
`GameScript._EndJobCoroutine_d__139`), `JobHelper.CheckJob(CarLoader, ref Job)`, `GlobalData.{Jobs, AddJob(int),
GetMaxOrdersAmount(), IsOrderSlotUnlocked(int, out OrderSlotLockReason, out int), AddPlayerMoney(int),
AddPlayerExp(int,bool), MissionsFinished, CurrentMissionDone, IsStoryMissionInProgress}`,
`CarLoader.{customerCar, orderConnection, SetCustomerCar(bool,int), IsCarLoaded(), DeleteCar(), DeleteCar(bool)}`,
`NotificationCenter.IsGameReady`, `ProfileData.FinishedTutorial`, `CMS.MainMenu.Windows.TutorialsWindow.RunTutorialAction()`.
`OrderGenerator` and `GameScript` are `MonoBehaviour`s, so game coroutines (`TakeJob`, `TakeMission` return
`Il2CppSystem.Collections.IEnumerator`) are started with their `StartCoroutine`, not `MelonCoroutines.Start`.
`GlobalData.Jobs` is not in the profile save (`NewGlobalDataWrapper`) and follows the `Prev*/Add*` UI-counter
pattern; it is assumed to be the open-order count shown in the HUD (task 1 confirms).
The stubs have no method bodies, so the call order inside `TakeJob`, `AcceptOrderAction`,
`EndJob`/`EndJobCoroutine` and the job timer is unknown, and small methods (`AddPlayerMoney`, `SetCustomerCar`,
`CancelJob`) may be inlined by IL2CPP so a Harmony patch never fires on the real path; task 1 traces both before
any behaviour hook is written.

Lessons from the old 0.4.17 implementation (`upstream-MainMod/ClientSide/Data/Garage/Campaign/JobHooks.cs`,
`JobManager.cs`): the host generated orders and relayed whole `ModJob`s; clients blocked `GenerateNewJob` and
`Update`. It desynced because ids came from each client's `LastUId`, accept was optimistic (no exclusivity),
`EndJob` was re-implemented in a prefix (brittle), and nobody re-applied jobs when the garage reloaded from
the profile: TogetherFixer's `SceneReturnCarSync` exists only to restore `selectedJobs` after a test-track
or junkyard return. Its `LateJoin` sends the open and selected jobs to late joiners, which we do from the
server instead.

## Goals / Non-Goals

**Goals:**
- Server is the only source of truth for open orders, active jobs, ids, expiry and job completion.
- Use the game's own generation, take, prepare and end logic; hooks observe and gate, they do not
  re-implement game code.
- Every state change converges: a client that misses a delta or reloads its garage ends up equal to the
  server after the next full `JobsState`.

**Non-Goals:**
- Part damage, examined state and mounting on the customer car (`sync-car-parts`), lifts and moving
  the car (`sync-car-placement-and-lifts`), fluids (`sync-car-details`), scene tracking
  (`sync-players-and-scenes`), save format versioning and stopping the client from saving shared jobs into
  its own profile (`session-persistence-and-rejoin`).
- Generating orders on the server.
- A multiplayer tutorial (backlog). This change only keeps the tutorial out of multiplayer games (D13).
- Moving customer cars to/from parking: refused while connected (row 2, user decision 2026-10-05), so a job's car
  stays on its loader until the job ends or the car is lost (D12).

## Decisions

### D1. Orders are generated by one elected client, not by the server

Current plan (interim): order generation by an elected client (D1) and client-reported payout/XP (D8) are replaced
by server-side generation and payout in ROADMAP row 16 `server-game-logic` once its decompile spike confirms it.

The server elects one "order generator" among eligible clients: `InSession` (`Client.SyncState`,
`session-persistence-and-rejoin` D4) and in the garage according to `sync-players-and-scenes`' presence
registry (`PresenceRegistry` scene `Garage`; that change lands in M1, before this one). The role is sticky: it
moves only when the generator leaves, times out or raises `PresenceEvents.SceneChanged` away from the garage;
then the lowest eligible client id takes it. If no client is eligible, nobody generates (orders pause, expiry
keeps running while anyone is connected), and the next client that becomes eligible (`SceneChanged` to
`Garage`) gets the role. The server sends `OrderGeneratorRole { IsGenerator }` to the old and new generator on
every change, and refuses `OrderGenerated` from any other client (a packet sent just before a re-election is
dropped and logged). On the generator, `OrderGenerator.Update` runs natively; on everyone else a prefix returns
`false` while connected (task 1 checks that `Update` does nothing non-generators need). The new generator keeps
its own frozen `orderTimer`/`nextOrderTime`, so the next order arrives at most one order interval later.

On the generator, a prefix on `GenerateNewJob`/`GenerateMission` records `jobs.Count`; the postfix takes
the jobs added since, sends each as `OrderGenerated { Job }`, and removes them locally again (under the
apply guard, D9). The order shows up on the generator only when the server's `OrderAdded` echo arrives.
That keeps one code path (server echo) for every client and lets the server rewrite the id.

The generator's native cap check (against `GetMaxOrdersAmount()`, on `jobs.Count` or `GlobalData.Jobs`) stays
correct because every apply rebuilds `jobs` from the server and sets `GlobalData.Jobs` to the open-order count.
The server also refuses `OrderGenerated` when its open-order count is already at the `MaxOpenOrders` the
generator reports in the packet, which stops a burst from a stale count (for example during the generator's own
garage reload, before its new snapshot is applied).

Alternatives:
- Server generates from exported data: needs the orders INI (`OrderGenerator.ordersData`), the car and
  config list, per-car part lists, difficulty and level rules exported and reimplemented. Large and
  drifts from the game. Rejected.
- Every client generates and the server dedupes/keeps the first N: wastes orders and makes the order
  timer depend on the number of players. Rejected.

### D2. The server assigns job ids

`JobsState.NextJobId` starts at 1 (or one above the highest stored id) and is the only id source. The
generator's local id is discarded. Every apply also sets `OrderGenerator.LastUId = NextJobId` so a native
id that slips through cannot collide with a live one.

### D3. What the server stores and what it only relays

Stored in `ModGameState.JobsState`, saved through `JobsSection` (`[SessionSection]`, `ISaveSection` key `jobs`,
`Version = 1`, `Reset()` = empty state) from `session-persistence-and-rejoin`'s contract:

| Field | Content |
|---|---|
| `Orders` | `List<OrderEntry>`: `ModJob Job`, `float RemainingSeconds`, `OrderStatus Status` (`Open`, `Claimed`), `long ClaimedBy`, `float ClaimedAt` (the claim fields are `[JsonIgnore]`; a loaded `Claimed` order becomes `Open`) |
| `ActiveJobs` | `List<ActiveJobEntry>`: `ModJob Job` (task/part flags, `MoneySpent`), `int CarLoaderId` (`-1` while the car is parked, D12), `float OriginalSeconds` (for D12's return to open) |
| `NextJobId` | next id |
| `Missions` | `ModMissionState { MissionsFinished, CurrentMissionDone, IsStoryMissionInProgress }`, applied to `GlobalData` (D14) |
| `GeneratorClientId` | not saved, recomputed |

Every read and write of `JobsState` holds `GameDataManager.StateLock` (`session-persistence-and-rejoin` D3):
handlers and `Client.Disconnect` already run under it; `JobsService.Tick` (expiry, claim timeout) runs on the
server UI thread and takes it itself. Broadcasts are sent inside the lock so their order matches the state order.

`ModJob` is an opaque DTO for the server; it reads only `id`, `timeToEnd`, `IsMission`, `CanDelete`,
`carFile`. The server never computes payout or progress.

Relayed (stored and forwarded): `JobStarted` (stored as the active job), `JobProgress` (merged into the
active job, then forwarded). Not relayed: `OrderGenerated`, `OrderAction`, `JobEndRequest` are requests;
the server answers with its own packets.

### D4. Accept is a server-approved request (pessimistic)

`OrdersWindow.AcceptOrderAction` prefix, when connected and not in bypass: send
`OrderAction { JobId, Accept }`, show nothing yet, return `false`. Server: if the order is `Open` and no
other take is in flight, set `Claimed`, `ClaimedBy`, `ClaimedAt`, reply
`OrderActionResult { Approved }` and broadcast `JobRemoved { JobId, Taken }` to the others; otherwise
reply `OrderActionResult { Approved = false, Reason }` (`AlreadyTaken`, `Busy`, `Unknown`), which the
client shows with `UIManager.Get().ShowInfoWindow`.

On approval the client re-runs the native accept with a bypass flag set: `OrdersWindow.currentJob` is set
to the local job and `AcceptOrderAction()` is called again. If the trace (task 1) shows that
`AcceptOrderAction` needs the window visible, the fallback is
`orderGenerator.StartCoroutine(orderGenerator.TakeJob(id, true))` (or `TakeMission` for missions).
If the native take refuses because the garage is full (`UIManager.ShowFullGarageInfo()` fires while a take is
pending), the client sends `OrderAction { AbortTake }` at once. If task 1 shows that a full garage sends the
customer car to parking instead, the take is refused while connected the same way (`AbortTake` and an info
message), because customer cars cannot be parked while connected (user decision 2026-10-05).

Only one take is in flight on the server at a time (`Busy`). Native `TakeJob` picks the first free car
loader locally; two takes of different orders at once could pick the same loader on two clients. The
take lock removes that race without the server having to know the game's placement rules.

Alternative: optimistic accept with rollback (delete car, put the order back). Rejected: the rollback has
to undo a native coroutine that also moves the player into the car, and the round trip costs < 100 ms.

### D5. Customer car spawn reuses `CarSpawnRequest`

While a take is in flight, the client keeps `PendingTake { JobId }`. `CarSpawnManager.RequestCarSpawn`
sends `IsJob = true, JobID = PendingTake.JobId` when a take is pending (instead of `customerCar`/`-1`,
because `SetCustomerCar` may run after `LoadCar`). `CarHandlers.HandleCarSpawnRequest` on the server
rejects (`CarSpawnRejected`) a job spawn whose `JobID` is not claimed by that client or whose loader is
already in `CarState.LoadedCars` (a parallel spawn from parking or a purchase won the loader). A client that
gets `CarSpawnRejected` for its pending take deletes the car (existing behaviour) and sends `AbortTake`.

`OrderGenerator.PrepareJob(carLoader, job)` postfix is the "job started" point: the job now has its
`carLoaderID`, its damage is applied and fields like `oilLevel`/`otherPartsCondition` are set. The client
sends `JobStarted { JobId, CarLoaderId, Job }` and clears `PendingTake`. The server moves the order to
`ActiveJobs` and forwards `JobStarted` to the others, who add the job to `selectedJobs` and, once
`CarLoader.IsCarLoaded()`, call `carLoader.SetCustomerCar(true, jobId)`. `CarHandlers.ProcessCarSpawnResponse`
does the same when `IsJob` is set and the job is already known, so the arrival order of
`CarSpawnResponse` and `JobStarted` does not matter.

The damage and random values `PrepareJob` applies are part and detail state. The `PrepareJob` postfix calls
`CarPartsSync.UploadBaseline(loaderId)` (`sync-car-parts` D6; its 1 s settle default is only a fallback); once
`sync-car-details` exists, its `BaselineUploaded` handler sends the full detail snapshot right after (its D7), so
no extra `MarkDirty` is needed. The other players get the damaged car, not a pristine one. If the trace shows `PrepareJob` does not fire (inlined), the fallback is a
per-frame check of `selectedJobs` for the pending job's `carLoaderID` while a take is pending.

Claim timeout: if `JobStarted` does not arrive within 60 s of `ClaimedAt`, or the claimer disconnects, the
server sets the order back to `Open`, broadcasts `OrderAdded` again (same id, remaining time) and, if
`LoadedCars` holds a car spawned for that `JobID`, removes it with `CarPartsStore.ClearLoader(loader, Deleted)`
(`sync-car-parts`; drops its part records, resets the lift through row 2's subscriber) and broadcasts `CarSpawnDelete`. The client sends
`OrderAction { AbortTake }` itself if `PrepareJob` has not happened 45 s after approval (native refusal).
A `JobStarted` for an order that is no longer claimed by the sender (it timed out) is answered with
`JobRemoved { TakeAborted, CarLoaderId }` to the sender only, which removes the job from `selectedJobs` and
deletes the car locally. The timeouts are generous because both harness instances load cars on one PC;
task 1 measures the real take duration.

### D6. Decline and expiry are server decisions

Decline: `OrdersWindow.DeclineOrderAction` prefix sends `OrderAction { Decline }` and returns `false`.
The server removes an `Open` order whose `CanDelete` is true and broadcasts `JobRemoved { Declined }` to
all, including the decliner. No local removal before the echo.

Expiry: the server decrements `RemainingSeconds` of `Open` orders by the tick delta while at least one
client is connected (user decision: expiry pauses while the server is empty; `Claimed` orders do not count
down), and broadcasts `JobRemoved { Expired }` at zero. Missions (`IsMission`) do not
expire. Clients set `job.timeToEnd` from the server's remaining time when they apply an order and start
the native timer (`Job.StartTimer()`) for the UI. Any local removal of an order that does not come from
the apply path is blocked by a prefix on `OrderGenerator.CancelJob` (returns `false` unless the apply
guard or the end-job flow is active), so a client's own timer cannot remove an order early. If the trace
shows that expiry removes jobs without `CancelJob`, the client re-applies its mirror after the removal
instead (D9).

The server's count is wall-clock seconds; the game's `timeToEnd` is assumed to be seconds too (task 1
checks this against `Job.Timer(float seconds)`).

### D7. Task progress: derived where possible, relayed where not

`JobTask.Done` / `JobPart.Done` are recomputed by the game (`JobHelper.CheckJob`) from the car's part
state, which `sync-car-parts` keeps equal on all clients, so they are not authoritative over the network.
`JobPart.Found` and `JobTask.moneySpent`/`Job.MoneySpent` may live only on the job object of the player
who examined the part or bought it. A postfix on `JobHelper.CheckJob` diffs the job against the client's
mirror and sends `JobProgress { JobId, Found[] (task, part index pairs), MoneySpentDelta per task }` for
changes that did not come from an apply. The server merges (`Found` is OR, money spent is summed) into the
stored active job and forwards the merged flags. Receivers OR the flags into their local job and set
`moneySpent` to the server's value. Task 1 confirms where `Found` and `moneySpent` are written; if both
turn out to be derived from synced car state, `JobProgress` is dropped and the requirement is met by
`sync-car-parts` alone. Progress a player makes away from the garage (test drive / test path results) reaches
the server through `sync-test-drive-and-diagnostics` (ROADMAP row 13) before the returning garage snapshot.

### D8. Ending a job: native flow, captured once, deduplicated by the server

The finisher runs the native `GameScript.EndJob` unchanged (its checks, messages, car deletion, fader).
A `JobEndContext` captures what the game pays:

1. `GameScript.EndJob` prefix: `JobEndContext.Begin(job.id, carLoaderId)`, remember `PlayerMoney`.
2. `GlobalData.AddPlayerMoney` prefix while the context is active: record the amount (the payout), let it
   run (local prediction).
3. `StatsHooks.AddPlayerExpPrefix` while the context is active: record the XP, do **not** send
   `StatsActionPacket`, let it run. Every money/XP call until the context ends is captured this way, so job
   XP can never also travel as `StatsAction` (shared XP pool, user decision).
4. Commit point: the last native step of a successful end, found by task 1 (candidates: the job's removal
   via `OrderGenerator.CancelJob(job.id)`, allowed in this context; `DeleteCar`; the last `MoveNext` of
   `GameScript._EndJobCoroutine_d__139`). It sends `JobEndRequest { JobId, CarLoaderId, Payout, Xp,
   IsCompleted, IsMission, Missions }` and ends the context. If no payout call fired (inlined), `Payout` falls
   back to the `PlayerMoney` difference and `Xp` to `job.XP`; task 1 compares both with `job.TotalPayout`/`job.XP`.
   If the commit point is not reached within 10 s, the checks failed and the context is dropped without sending.

The finisher's native `DeleteCar()` still fires the existing `CarSpawnHooks.DeleteCarHook`; the resulting
`CarSpawnDelete` and the job-end delete below are both idempotent on the server and on receivers.

Server: if the job is in `ActiveJobs`, remove it, add `Payout` to `WorldState.Money`, apply `Xp` to the shared
level/exp with the level-up loop `StatsHandlers` uses (refactored into a shared `StatsHandlers.ApplyExp`), store
`Missions` when `IsMission` (D14), delete the job's car only if `LoadedCars[CarLoaderId].JobID == JobId`
(`CarPartsStore.ClearLoader(loader, JobEnded)`, which drops part records and resets the lift), broadcast
`JobRemoved { Ended, CarLoaderId, IsCompleted, Missions }` and `WorldState`. Otherwise reply nothing but
`WorldState` to the sender, which overwrites its predicted money and exp. Receivers of `JobRemoved { Ended }`
remove the job and delete the car on that loader under `CarSpawnHooks.Suppress` if it is still there.

Steam stats and achievements of a finished job go to every connected player (user decision 2026-10-05). The
finisher's native `EndJob` already sets them locally; every other client that applies `JobRemoved { Ended }` calls
the same game stat/achievement methods for that job (`JobStatsAwarder`), which task 1 identifies in the
`EndJob`/`EndJobCoroutine` trace. Players who are not connected at that moment get nothing. If the trace finds no
callable entry point, this is recorded as a known gap and only the finisher gets them.

Payout and XP are trusted from the client (no game code on the server), with sanity bounds
(`0 <= Payout <= 1_000_000`, `0 <= Xp < 10_000`). The server's job is to apply them exactly once.

Alternative: re-implement `EndJob` in a prefix, as 0.4.17 did. Rejected: it copies game logic that has
already changed (`EndJobCoroutine` exists now) and skips whatever the coroutine does.

### D9. Client apply path and mirror

`Logic/Jobs/JobSyncState` keeps the client's mirror of the server's orders and active jobs, updated by
every `JobsState`, `OrderAdded`, `JobRemoved`, `JobStarted`, `JobProgress`. Applying to the game happens
in one place, `JobApplier`, under a `JobApplyGuard` that every hook checks (same pattern as
`CarSpawnHooks.Suppress` and `ClientData.IsServerUpdating`):

- `ApplyFull()`: stop timers on the current `jobs`, clear `jobs`/`selectedJobs`, rebuild them from the
  mirror (`ModJob.ToGame()`), set `timeToEnd` from remaining time and call `StartTimer()` on open orders,
  set `GlobalData.Jobs` to the open count and `OrderGenerator.LastUId`, set the mission counters, call
  `UIManager.Get().UpdateJobs(jobs, null)`, and mark customer cars on loaded loaders.
- Deltas add/remove one job and call `UpdateJobs`.
- Applying to the game requires the garage scene and that `OrderGenerator.Load()` has run in this garage load
  (flag set by the `Load` postfix, cleared when `sync-players-and-scenes`' scene prefix reports leaving the
  garage). The scene check is `ClientScene.IsGarageReady` (row 6 sets it at the start of `CustomLoad`, before
  `AskForSync`; equivalent to `GameScript.Get().CurrentSceneType == SceneType.Garage`). It must **not** wait for
  `NotificationCenter.IsGameReady` or `IsInitialSyncFinished`: both become true only after initial sync, which
  waits for `jobs`. Live job handlers (deltas) use the same gate; they need no queue of their own because the
  mirror and `ApplyFull()` are synchronous and the snapshot's `JobsState` arrives before any live delta.
  Packets arriving earlier or in other scenes only update the mirror; returning to the garage reloads it and
  gets a fresh snapshot anyway.
- The mirror is replaced, not merged, by every `JobsState`. `ClientData.Reset()` (every garage load) clears it;
  the `JobsState` of the following snapshot refills it.
- `OrderGenerator.Load` postfix (garage load, return from test track/junkyard): when connected and the
  mirror has been filled once, call `ApplyFull()`. This replaces the profile's jobs with the server's and
  covers the desync TogetherFixer's `SceneReturnCarSync` works around.
- If the orders window is open on the job that was removed, it is refreshed through `UpdateJobs`; an
  approval for a job that is no longer local is ignored and answered with `AbortTake`.

### D10. Late join

`JobsSection` is also the `ISnapshotProvider` for key `jobs` at `SyncOrder` 400 (after `cars` 100,
`car-details` 150 and `car-placement` 200, so every loader it names already has its car). `SendSnapshot` runs
with `StateLock` held, sends one `JobsState` (orders with remaining time, active jobs, mission state,
`IsGenerator` for that client) and returns 1. The client stores `JobsState` in the mirror, runs `ApplyFull()` (the
gate in D9 is open: `OrderGenerator.Load` ran in `VanillaLoad` before `AskForSync`) and then reports
`SyncTracker.Applied("jobs")`. Customer cars of active jobs come from `sync-car-parts`' snapshot replay;
`ApplyFull()` and the `CarSpawnResponse` handler both mark `SetCustomerCar(true, jobId)` once the car is loaded,
whichever happens last. The generator role of a first client is assigned on its `SyncAck` and sent as
`OrderGeneratorRole`; a returning generator (garage reload) gets `IsGenerator = true` in its new snapshot. Live
job broadcasts skip clients still in `Connected` (`Server.SendToClients`), so a joiner sees only the snapshot and
the deltas queued after it.

### D11. Players leaving, server restart

`Client.Disconnect` on the server (and `PresenceEvents.Left`) calls `JobsService.OnClientLeft(id)` under
`StateLock`: release a claim by that client (D5 timeout path), re-elect the generator if it was the generator.
`PresenceEvents.SceneChanged` away from the garage does the same (integration note: claims are released when the
holder leaves the garage). Active jobs are untouched; anyone can
finish them. A `JobEndRequest` that arrived before the disconnect is processed normally. On a server restart
claims are dropped (claimed orders load as `Open`), the generator is elected anew, and expiry resumes when the
first client connects.

### D12. The job's car leaves its loader

Customer cars cannot be parked while connected (row 2 refuses `IsJob` records; user decision 2026-10-05), so a
job's car stays on the loader of its `JobStarted` until the job ends. If the car disappears any other way, the job
returns to the open list with `OriginalSeconds` (user decision: reopen with the original time; broadcast as
`OrderAdded` with the same id, receivers drop it from `selectedJobs`), so it can be taken again instead of being
stuck without a car:
- the spawner left before the car's baseline: `JobsService` subscribes to `sync-car-parts`' server event
  `CarPartsStore.LoaderCleared` and reopens the job whose car was on that loader for reason `SpawnerLeft`
  (`Deleted` and `JobEnded` are ignored: the finisher's own `CarSpawnDelete` may arrive before its `JobEndRequest`);
- the `cars` section dropped the car on load (no baseline): no event fires during load, so the first
  `JobsService.Tick` after start checks every active job against `LoadedCars[CarLoaderId].JobID` and reopens the
  ones without their car.

### D13. Tutorial disabled in multiplayer (user decision)

While connected: a prefix on `OrderGenerator.GenerateMission` returns `false` when `forTutorial` is true; the
server refuses an `OrderGenerated` whose job is a tutorial mission (task 1 finds the marker, e.g. `MissionID` or
`LocalizationID`); a prefix on `TutorialsWindow.RunTutorialAction()` blocks starting a tutorial from the pause
menu and shows `UIManager.Get().ShowInfoWindow("Tutorials are not available in multiplayer")`. If task 1 shows
that the fresh session profile locks order slots with `OrderSlotLockReason.Tutorial`, `ModGameManager.StartGame`
sets `ProfileData.FinishedTutorial = true` on the session profile (one line; the session profile is never
written to disk, `session-persistence-and-rejoin`).

### D14. Story missions

Story missions are orders with `IsMission` and are synced like orders (user decision): generated only by the
generator (`GenerateMission` capture, D1), not declinable (`CanDelete` false), no expiry, taken with
`TakeMission`. The mission state the game keeps in `GlobalData` (`MissionsFinished`, `CurrentMissionDone`,
`IsStoryMissionInProgress`) is read by the client after the native take (`JobStarted`) and end
(`JobEndRequest`) of a mission and sent as `Missions`; the server stores it and puts it into the forwarded
`JobStarted`, `JobRemoved { Ended }` and `JobsState`; receivers set the three `GlobalData` fields. The game's own
rules (`GlobalData.CanRegenerateMission`, `GetMissionID`) then pick the next mission on the generator.

### D15. Corrections from the static spike (2026-10-06, `docs/spikes/orders-and-jobs.md`)

These override D4–D8 and D13 where they differ; the `jobs-trace` scenario (task 1.3) confirms them at runtime.
- Hooks that never fire: `OrderGenerator.TakeJob`/`TakeMission` (builders inlined; use `<TakeJob>d__19.MoveNext` /
  `<TakeMission>d__22.MoveNext` or the accept action), `GameScript.EndJobCoroutine` (use `EndJob` or
  `<EndJobCoroutine>d__139.MoveNext`), `CarLoader.SetCustomerCar` (inlined; `customerCar`/`orderConnection` are set
  before `LoadCar`, so the `LoadCar` hook reads the job id and no `PendingTake` lookup is needed).
  `OrderGenerator.Start` and `Prepare` share one native body: never patch either.
- Generation: first order after 10 s, then one per 30 s while open orders are below a level-based cap (2–8), all from
  the global `UnityEngine.Random`. The car pool follows the generating client's installed DLCs, so the generator
  must only offer DLC cars in the shared DLC set (row 9).
- Open orders have no parts list: `PrepareJob` builds it at take time from the taker's upgrades, so `JobStarted` must
  carry the job after `PrepareJob`. Missions never call `PrepareJob`; their start is the `<TakeMission>d__22` step
  that adds to `selectedJobs` (or an `OnTakeMission` subscription).
- Expiry runs a per-job timer in scaled game seconds (it stops while paused) and removes the order through a real
  `CancelJob` call, so the D6 prefix sees it.
- Progress: `Done` and `moneySpent` are recomputed from car state; nothing was found that writes `JobPart.Found`, so
  `JobProgress` is dropped unless the trace finds a writer (D7).
- Payout: `EndJob` pays the totals of the last `CheckJob`, which only the orders UI runs; the `EndJob` prefix runs
  `CheckJob` first. The commit point is the real `CancelJob(job.id)` inside `d__139` right after the payout. One hook on
  `SteamAchievements.IncrementStat` sees every stat; other clients award the same stats through
  `PlatformManager.IncrementStat` (seven job achievements, listed in the spike).
- Tutorial (D13): order slots are locked only inside the Tutorial scene; the tutorial mission is `IsMission && id == 0`
  (or `GenerateMission(_, forTutorial: true)`).

### Packets

Appended to `PacketTypes` (end of the enum, so existing values keep their numbers):

| Packet | Direction | Content |
|---|---|---|
| `JobsState` | S -> C | `Orders` (job + remaining), `ActiveJobs`, `NextJobId`, `Missions`, `IsGenerator` |
| `OrderGeneratorRole` | S -> C | `IsGenerator` |
| `OrderGenerated` | C -> S | `ModJob Job`, `int MaxOpenOrders` |
| `OrderAdded` | S -> all | `ModJob Job` (server id), `float RemainingSeconds` |
| `OrderAction` | C -> S | `int JobId`, `OrderActionType` (`Accept`, `Decline`, `AbortTake`) |
| `OrderActionResult` | S -> requester | `int JobId`, `OrderActionType`, `bool Approved`, `string Reason` |
| `JobStarted` | C -> S -> others | `int JobId`, `int CarLoaderId`, `ModJob Job`, `ModMissionState Missions` (missions only) |
| `JobProgress` | C -> S -> all | `int JobId`, found flags, per-task money spent (absolute from server, delta from client) |
| `JobEndRequest` | C -> S | `int JobId`, `int CarLoaderId`, `int Payout`, `int Xp`, `bool IsCompleted`, `bool IsMission`, `ModMissionState Missions` |
| `JobRemoved` | S -> all (S -> sender for a late `TakeAborted`) | `int JobId`, `JobRemovedReason` (`Taken`, `Declined`, `Expired`, `Ended`, `TakeAborted`), `int CarLoaderId`, `bool IsCompleted` (Ended only), `ModMissionState Missions` |

DTOs `ModJob`, `ModJobTask`, `ModJobPart`, `ModMissionState` go in `Core/Data/GameType/` (fields as in `Job`,
`JobTask`, `JobPart`; Unity `Color` fields as `ModColor`, `PaintType` as `ModPaintType`, both already exist).
Conversion to and from game types lives in the client (`ModJobConverter`), because Core has no game references.
Packets are BinaryFormatter-serialized like the existing ones; a `JobsState` with 10 orders stays well below
Steam's reliable message limit.

## Risks / Trade-offs

- [Unknown native call order in `AcceptOrderAction`/`TakeJob`/`EndJob`/job timer] -> Task 1 traces it with
  logging-only Harmony patches before any behavior hook; D4, D6 and D8 name their fallback.
- [Re-invoking `AcceptOrderAction` after approval may fail if the window closed] -> fallback to starting
  `TakeJob`/`TakeMission` directly.
- [Payout and XP are client-reported] -> bounds check and once-only application; acceptable for co-op
  among friends, noted for later hardening.
- [IL2CPP inlining: a patched method never fires on the real path] -> task 1 records fired/not fired per hook
  on the real UI path; each behaviour hook names its fallback (D5 `PrepareJob` poll, D6 re-apply, D8 money
  diff / `job.XP`, D7 poll instead of the `CheckJob` postfix with its `ref Job` argument).
- [Everyone is away from the garage] -> nobody is eligible, so no orders are generated until someone returns;
  open orders still expire while anyone is connected. Accepted (ROADMAP integration note: the generator
  must be in the garage).
- [Scene report lags the real scene by a frame or a packet] -> an order generated in that window still carries
  the generator's id and is accepted; the generator's own capture code runs in any scene.
- [Generator leaves mid-generation] -> an `OrderGenerated` still in flight from it is dropped (D1); at worst
  one order is lost and the new generator produces the next one.
- [Take lock serializes accepts] -> a second accept in the same ~second gets `Busy` and must retry; rare
  with 2-4 players.
- [Job damage not replicated if `UploadBaseline` runs before `PrepareJob` finished its random rolls] -> the
  scenario compares the job car's parts and details on both clients right after the take.
- [`EndJob` fluid check (`job.oilLevel`) on a non-taker depends on `sync-car-details`] -> until then a
  non-taker may get "no oil"; scenario lets the taker finish in the first run and the other player in a
  second step once fluids sync.
- [Native `OrderGenerator.Save` writes the shared jobs into the player's own profile] -> owned by
  `session-persistence-and-rejoin`; harmless meanwhile because `Load` is overridden by `ApplyFull()`.
- [Dropping a client's pre-existing profile orders on connect] -> intended: the server is the source of
  truth; a new session starts with no orders and the generator fills it.

## Migration Plan

A save without a `jobs` section loads through `JobsSection.Reset()` (empty state), which is the new session
state; the section starts at `Version = 1`. Rollback is reverting the change; row 7 keeps an unknown `jobs`
section as raw JSON, so an older server does not lose it.

## Open questions / assumptions

User decisions applied (2026-10-05): money, XP and level are shared (job XP goes to the shared pool through
`JobEndRequest`, never `StatsActionPacket`); expiry pauses while the server is empty; story missions sync like
orders; the tutorial is disabled in multiplayer games (D13).

Decided without asking (please object if wrong):

- **Generation by an elected garage client (D1)** rather than server-side generation. Orders pause while
  no client is in the garage.
- **Payout/XP reported by the finishing client is trusted** (bounds-checked, applied once).
- (Answered 2026-10-05) Steam stats/achievements for a finished job go to all connected players (D8).
- Claim timeout 60 s, client-side abort 45 s, end-context timeout 10 s; tune after the trace.

Deferrable unknowns (answered by task 1, do not change the approach):

- Whether `JobPart.Found`/`moneySpent` are derived from car state (D7 may shrink).
- The exact commit point inside `EndJob`/`EndJobCoroutine` (D8 step 4).
- Whether the job timer removes expired jobs through `CancelJob` (D6), and the `timeToEnd` unit.
- Whether `GlobalData.Jobs` is the open-order count, and whether the fresh session profile locks order slots
  for the tutorial (D13).
- Whether a full garage refuses the take or sends the car to parking (D4; either way the take is refused while
  connected).
- Which game calls set the Steam stats/achievements of a finished job (D8, `JobStatsAwarder`).
- Which native step sets each mission field (D14).
