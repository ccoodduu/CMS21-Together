# Spike: draws during a job take (server-game-logic spike 1.5)

Roadmap row 16 `server-game-logic`, task 1.5 and task group 5 (seeded job cars), on `feat/seeded-job-cars`.
Date: 2026-10-08. Method: a static call-graph scan of `GameAssembly.dll`, the decompiles under
`%USERPROFILE%\CMS21-TestInstalls\native\out\` (`clean\`, `seededjobs_clean\`, `seededjobs2_clean\`), and runtime traces
and digests on lane 1 (scenario `jobs-seeded`, harness `jobcar-trace` and `jobcar-digest`).

## Short answers

1. **Every `UnityEngine.Random` draw of a job take runs in one of four iterators:** `OrderGenerator.<TakeJob>d__19`,
   `OrderGenerator.<TakeMission>d__22`, `CarLoader.<LoadCar>d__215` and `CarLoader.<SetRandomColorPanels>d__321`.
   `CarLoader.<LoadAndPrepareModel>d__250` draws nothing. Seeded per iterator from the order's `PrepSeed`, every part
   (body and mechanical, conditions, dents, bolts), the colour, the paint, the livery, the oil roll, the wheel and
   headlamp alignment, the mileage and the job's regular tasks come out equal on every take. That holds across
   clients and across loaders.
2. **`LoadCar` does draw**, so it is wrapped too (stream `"job-load"`). It must be the iterator `<LoadCar>d__215`;
   `TakeJob` starts it with `StartCoroutine` (state 1), and every draw is in `MoveNext` (states 2 → 7 and 7 → 9).
3. **Two sources are not `UnityEngine.Random`, so seeding does not reach them.** They are what still differs between
   two takes of the same order:
   - **Which `Additionals` tasks the job gets, and which fluids are drained.** `PrepareJob` picks them with
     `EnumerableExtension.PickRandom(list, n)` = `Shuffle(list).Take(n)`, and `Shuffle` sorts by `Guid.NewGuid()`
     (`<>c__2<object>.<Shuffle>b__2_0`). Three call sites in `PrepareJob` (`0x180C5EE29`, `0x180C5F031`,
     `0x180C5F40A`). The count `n` is a Unity draw and is equal; the chosen items are not. Example from the run:
     `CoolantChange,PowerSteeringChange` on the first take, `BrakeChange,PowerSteeringChange` on the retake, with the
     matching fluid levels.
   - **The licence plate number.** `CarHelper.GetRandomLicensePlate` (called by `CarLoader.SetLicensePlateNumber` in
     `LoadCar`) creates `new System.Random(QueryPerformanceCounter ms)` for the letters and digits. Its Unity draws
     (letter count, letter or digit) are equal. Missions: the plate on the car comes from the mission INI and is
     equal; only `FactoryLicensePlateNumber` differs.
4. **The radial-fault overlap is equal across loaders 3 and 4.** The body and sub-part digests of a take on loader 3
   equal those of the take on loader 4. `PreparePart` draws the radius before `OverlapSphereNonAlloc` and nothing per
   hit (review m3), and `PlaceAtPosition` drew exactly one word on loaders 2, 3 and 4.

## Where the draws are

Static scan: every method reachable by direct calls from each iterator (depth 6) that calls a `UnityEngine.Random`
method or resolves a `UnityEngine.Random::` icall. Virtual calls are not followed; the runtime trace covers them.

| Iterator | State | Draws |
|---|---|---|
| `<TakeJob>d__19` | 1 | `StartCoroutine(LoadCar)`: `LoadCar`'s state 0 runs inside this step and draws nothing |
| | 3 | `PlaceAtPosition` → `SetupCarSupport` → `CarSupport.SetupRandomLook` (`RandomRangeInt(0, meshes)`, maybe `(0,100)`); `SetAdditionalCarRot` only on a place with `CarPlaceRotation`; then the oil roll `Range(0.8,1) ×2` |
| | 4 | `GameInventory.GetRandomLicensePlate` (plate style), `PrepareJob` (`PreparePart`, `CalcRadialFaultsCondition`, `PrepareBodyParts`, `SetRustRandomParts`, `WheelsAlignment.SetRandom` → `Helper.StepRandom`, `SetRandomHeadlampAlignment`, `GetRandomIncreaseTuneValue`, the `Additionals` counts), the factory-colour roll and the `GetRandomCarColor` loop |
| | 5 | starts `SetRandomColorPanels(45, 0.2, 0.7)` when `globalCondition < 0.55`; its state 0 runs inside this step |
| | 6 | `SetRandomCarLivery`, `SetMountObjectsRandomCondition`, `SetRandomDent` |
| `<LoadCar>d__215` | 2 → 7 | `PreparePartScriptCuller` → `PartScriptCuller.Init` (`Random.value` per culler), `SetLicensePlateNumber` → `CarHelper.GetRandomLicensePlate` |
| | 7 → 9 | one draw |
| `<SetRandomColorPanels>d__321` | 0, 1… | one batch per frame, over many frames |
| `<TakeMission>d__22` | 3, 4 | `PlaceAtPosition` (twice), `WheelsAlignment.SetRandom`, `SetRandomHeadlampAlignment`, `SetRandomPartsConditions`, `SetRustRandomParts`, `SetMountObjectsRandomCondition`, `SetNewLicensePlateNumber`; `GenerateMission` only on its car-file mismatch fallback |

The global random state also moves between frames while `TakeJob` waits (other game code draws), so a single stream
for the whole take would not be reproducible. Each iterator keeps its own stream across its steps (as row 15 found
for the outdoor generators); nested starts (`LoadCar`, `SetRandomColorPanels` inside a `TakeJob` step) push and pop
correctly.

## What was built (task group 5)

- `Logic/Seeding/SeededStreams` (moved out of `Logic/Outdoor/Reseed`; outdoor keeps its predicate and step count).
- `ModJob.PrepSeed`, `ActiveJobEntry.OrderJob` (optional fields, no `jobs` version bump). The server assigns
  `PrepSeed` to every accepted order and to any loaded order without one, keeps the original order on start, copies
  the seed into the active job, and reopens the original order when a car is lost.
- `JobSeedHooks`: the taking client reads the seed from the `JobsSync` mirror by job id and seeds `TakeJob` (`"job"`),
  `TakeMission` (`"mission"`), and for the job's loader `LoadCar` (`"job-load"`) and `SetRandomColorPanels`
  (`"job-panels"`). Log line: `[Jobs] Order <id>: preparing the car from seed <seed>.`

## Runs (lane 1)

Scenario `jobs-seeded`: A takes order X, the server reopens it (`jobs reopen`), B takes it, it is reopened again with
another car on X's loader and A takes it on another loader; a story mission is taken, reopened and retaken; order Y
must differ. The digest has 250–300 rows: every body part, every sub part, the car details sections and the job's
tasks.

| Run | Code | Result |
|---|---|---|
| `20261008-221308_L1_jobs-seeded` | `d8d7755` (main + test tooling) | fails: mission retake 226 of 283 rows differ, retake by B 282 of 299, retake on loader 3 282 of 299 |
| `20261008-221514_L1_jobs-seeded` | `af49fa0` (seeded) | fails on the two sources above only: mission retake 1 row (`details:Plates`, factory plate), retake by B and retake on loader 3 3 rows each (`details:Fluids`, `details:Plates`, `job:task1` = the `Additionals` picks); body, sub, car and the other details equal |

`20261008-220747_L1_jobs-seeded` is an earlier run of `181aa8a`; its mission step could not start (the garage was
full), which `d8d7755` fixed by taking the mission first.

## Open: making the two remaining sources deterministic

Not built; it needs a decision, because both are outside `UnityEngine.Random`:

- **(a) Accept them as the known limit.** The car itself is the same; the `Additionals` tasks and drained fluids and
  the plate number may change on a retake. The digest would leave out `details:Plates` and the `Additionals` row.
- **(b) Patch the two non-Unity sources during a seeded take:** a prefix on `Il2CppSystem.Guid.NewGuid` that, while a
  job stream is active, returns a Guid made of four draws from that stream; and either a prefix on
  `CarHelper.GetRandomLicensePlate` that reproduces its format from the stream, or a prefix on the `System.Random`
  constructor that replaces the clock seed with a stream draw. Global patches on mscorlib methods (Harmony on
  Il2Cpp `Guid.NewGuid` and the `Random` constructor is untested here).
- **(c) Replace the picks:** prefix `PrepareJob`'s `PickRandom` call path. `EnumerableExtension.Shuffle<T>` is a
  shared generic instantiation, which MelonLoader 0.5.7's Harmony may not be able to patch.
