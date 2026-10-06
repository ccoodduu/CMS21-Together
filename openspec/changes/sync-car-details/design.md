# Design

## Context

- The server can spawn and delete cars (`CarHandlers`, `CarState.LoadedCars`). `sync-car-parts` adds part
  records, a per-spawn `SpawnSeq`, a part baseline uploaded by the spawner, a client loader state machine
  (`Empty → Loading → AwaitingBaseline → Ready`) and the late-join car snapshot (`SyncOrder` 100).
  `session-persistence-and-rejoin` groups 1–2 add the server contract every row plugs into: `ISaveSection`,
  `ISnapshotProvider`, `SyncOrder`, `SyncBegin/SyncEnd/SyncAck` with the client `SyncTracker`, and
  `GameDataManager.StateLock`, which is held around every packet handler and console command.
- The game keeps the detail state on the live `CarLoader`. All names below were checked against the
  Unhollower stubs in `decomp/Assembly-CSharp-firstpass`:
  - `FluidsData FluidsData`: `FluidData Oil` plus `List<FluidData>` `Brake`, `EngineCoolant`,
    `PowerSteering`, `WindscreenWash`; `FluidData{float Level, Condition; CarFluid CarFluid}`,
    `CarFluid{CarFluidType FluidType; int ID}`
  - `CarWheelsData WheelsData.Wheels[]`: `Wheel{string Tire, Rim; float Width, Size, Profile; int ET}`.
    The setter takes ints: `SetWheelSize(int wheelWidth, int rimSize, int tireSize, WheelType)`, and
    `NewCarData` saves `wheelsWidth/rimsSize/tiresSize/tiresET` as `int[]`.
  - structs `WheelsAlignment{FL, FR, RL, RR}` and `HeadlampAlignment HeadlampLeftAlignment/HeadlampRightAlignment
    {Horizontal, Vertical}`, `CarInfoData{Mileage, BuyPrice, CarFrom}`. Structs are read, changed as a copy
    and assigned back.
  - `color`, `factoryColor`, `factoryPaintType`, `paintData`, `IsCustomPaintType`
  - `List<CarPart> carParts`, each with `Color`, `PaintType`, `PaintData`, `Livery`, `LiveryStrength`,
    `IsTinted`, `TintColor`, `Dust`, `WashFactor`
  - `LicensePlatesData{LicensePlateNumberFront, LicensePlateNumberRear, FactoryLicensePlateNumber,
    LicensePlateFrontTex, LicensePlateRearTex}`
  - `bonusParts` (`BonusPartsData{IDs, IsPainted, Color, PaintType, PaintData}`), set with `SwapBonusPart` /
    `TakeOffBonusPart`; headlights via `SwitchCarLights` (the readable lights field is found by probe 1.2)

  Tuning lives on part components: `GearboxHandle{float[] gearRatio; float finalDriveRatio}` and
  `CMS.PartModules.PartModule{PartScript partScript; TuningData data}` with `EcuModule{byte Stage}` and
  `CarbModule`. `NewCarData` also has car-level `gearRatio`, `finalDriveRatio` and `ecuData`, so where the
  live value sits is checked by the probe (task 1.2).
- Server handlers run on network threads under `StateLock`. Client handlers run on the main thread (as the
  existing car handlers, which start coroutines). Packets are BinaryFormatter-serialized (`Packet.cs`).
- Lessons from the 0.4.x mod (`upstream-MainMod`): one packet per `FluidsData.SetLevel` call (fires every
  frame while pouring); one global `listen` flag against echoes that raced; `SetLevelAndCondition` applied
  without the reservoir id; loader id parsed from `gameObject.name[10]`; `ModCarFrom` in a different order
  from `CarFrom`; a late joiner got the spawn-time `ModNewCarData`, never updated (background of #85).
- FixForTogether (findings only, no code adopted): final values are reliable only at commit points (tuning
  `ApplyAction`, alignment windows `HideAction`, `TintingWindow.TintAction`, paint shop `IsPainting` turning
  false); the paint shop and tint window change the car live while previewing and restore on cancel; it read
  state through `CarLoader.SaveCarToFile()`, which writes the player's profile, and we avoid that.

## Goals / Non-Goals

**Goals:**
- One server-owned record per loaded car holding the nine sections in `specs/car-details-sync/spec.md`,
  saved with the session.
- Change detection that never sends preview values and never echoes or floods.
- Spawn values become shared, and a late join is complete.
- A small client API and a stable packet that `sync-workshop-car-tools` reuses.

**Non-Goals:**
- Mounting and unmounting parts, part condition, dent, quality, and dust/condition on mechanical
  `PartScript`s (`sync-car-parts`).
- Engine swap (`EngineParams.EngineSwap`, `NewCarData.engineSwap`): it changes the part hierarchy, so the
  stored state belongs to `sync-car-parts` and the trigger to `sync-workshop-car-tools` (engine crane), as
  `sync-workshop-car-tools` D2 already says.
- Dyno results (`EngineData`, `MeasuredDragIndex`): owned by ROADMAP row 13 `sync-test-drive-and-diagnostics`
  (decision 2026-10-05).
- Tool interaction, animations, costs and occupancy (`sync-workshop-machines`, `sync-workshop-car-tools`).
- Wheel balance (`WheelData.IsBalanced` exists only on items and the balancer, so it is
  `sync-workshop-machines` + inventory) and tire pressure (not modelled by the game).
- Moving cars between places, lifts and parking (`sync-car-placement-and-lifts`).
- The save envelope and its version (`session-persistence-and-rejoin`).

## Decisions

### D1: Section snapshots, keyed entries merged per entry
Each update carries whole sections; a null section means "unchanged". Three sections are lists of keyed
entries and merge per entry: Fluids by `(Type, Id)`, BodyCosmetics by `PartIndex`, Tuning modules by
`PartKey`. An update carries only the entries that changed.
- *Why:* applying a snapshot twice gives the same result, the same packet serves live updates, the spawn
  snapshot and the late-join replay, and concurrent edits to different sections or different entries are all
  kept (A pours oil while B pours coolant).
- *Rejected: per-field delta packets* (0.4.x): many packet types, flood while values change, no full-state
  recovery.
- *Rejected: one blob per car:* two players editing different sections overwrite each other.
- *Rejected: `NewCarData.Serialize` blob applied with `LoadCarFromFile(NewCarData)`:* the server cannot
  validate or merge it, it ties us to the game's save version, it reloads the whole car (stepping on
  `sync-car-parts`), and reading it needs `SaveCarToFile`, which writes the player's profile.

### D2: Data model (Core)
`Data/GameType/ModCarDetails.cs`:
- `[Flags] enum CarDetailSection {Fluids, Wheels, Alignment, Tuning, Paint, BodyCosmetics, Plates, Info, BonusParts}`
- `ModCarDetails{int SpawnSeq; bool HasSnapshot; …one nullable field per section}`:
  - `List<ModFluidLevel>`, `ModFluidLevel{ModCarFluidType Type; int Id; float Level; float Condition}`
  - `ModCarWheel[4]` indexed by `WheelType`: `{int Width, RimSize, TireSize, ET; string Tire, Rim}`. The
    ints are `SetWheelSize`'s arguments; Tire and Rim are diagnostics only.
  - `ModAlignment{float FL, FR, RL, RR; float LampLH, LampLV, LampRH, LampRV}`
  - `ModCarTuning{string GearboxPartKey; ModGearboxData Gearbox; List<ModPartTuning> Modules}`,
    `ModPartTuning{string PartKey; ModTuningData Data; int EcuStage}` (`-1` when not an ECU)
  - `ModCarPaint{ModColor Color, FactoryColor; ModPaintType FactoryPaintType; ModPaintData PaintData; bool IsCustom}`
  - `List<ModBodyCosmetics>`, `ModBodyCosmetics{int PartIndex; ModColor Color; ModPaintType PaintType;
    ModPaintData PaintData; string Livery; float LiveryStrength; bool IsTinted; ModColor TintColor; float
    Dust; float WashFactor}`
  - `ModLPData`, filled in with the five `LicensePlatesData` strings
  - `ModCarInfo{int Mileage, BuyPrice; ModCarFrom CarFrom; bool LightsOn}`
  - `ModBonusParts{string[] IDs; bool IsPainted; ModColor Color; ModPaintType PaintType; ModPaintData PaintData}`
    (one whole section, mirrors `BonusPartsData`)
- `ModGearboxData` gets `float[] GearRatio; float FinalDriveRatio` (matches the game's `GearboxData`, so
  `ModItem.GearboxData` can carry it too).
- `ModCarFluidType` mirrors `CarFluidType` (`None, All, Brake, EngineOil, EngineCoolant, WindscreenWash,
  PowerSteering`) and `ModCarFrom` mirrors `CarFrom` (`None, Junkyard, Barn, Order, Mission, Auction,
  Salon`). The probe checks the ordinals by name (no unit test project).
- Part keys are `sync-car-parts`' keys: `PartIndex` is the body index (`b:<i>`), `PartKey` the sub-part key
  (`s:<path>`), both resolved through its `PartRegistry`. Loader ids come only from
  `CarLoaderPlaces.Get().GetCarLoaderId`.

### D3: Packets
Both `PacketTypes` values are appended at the end of the enum.
- `CarDetailsUpdatePacket{int CarLoaderID; int SpawnSeq; bool IsFull; int SourceClientId; int ClientSeq;
  ModCarDetails Details}`, both directions. Tools reuse it through D6.
- `CarDetailsRequestPacket{int CarLoaderID; int SpawnSeq}`, server → one client: "send a full snapshot".

`SpawnSeq` is `sync-car-parts`' per-spawn number, so an update for a replaced car is recognised even when the
new car is the same model.

### D4: Change detection: commit hooks plus value diff, polling only where previews cannot happen
Each loader starts "awaiting snapshot" when its car loads. The client keeps `lastKnown[loader][section]`,
the last values sent or applied (re-read from the game after applying). A hook or the poll only marks
`(loader, section[, entries])` dirty. A flusher in `OnUpdate` sends a loader's dirty sections 0.5 s after the
last mark, only the entries that differ from `lastKnown` (floats differ by more than 5e-4).

| Section | Marked dirty by | Applied with |
|---|---|---|
| Fluids | 1 Hz poll | `FluidsData.SetLevelAndCondition(level, cond, type, id)` per changed reservoir |
| Wheels | 1 Hz poll (sizes change along mount paths such as `PartScript.ResizeWheel`, not only `SetWheelSize`) | `SetWheelSize(width, rim, tire, WheelType)`, `SetET(WheelType, et)`, then `UpdateWheels(front)` per axle |
| Alignment | postfix `WheelsAlignmentWindow.HideAction`, `LampAlignmentWindow.HideAction` (field `carLoader`) | assign `WheelsAlignment`, `HeadlampLeftAlignment`, `HeadlampRightAlignment` (struct copies) |
| Tuning | postfix `GearboxTab.ApplyAction`, `EcuTuning.ApplyAction`, `CarbTuning.ApplyAction` (field `carLoader`); `sync-car-parts` local commit event (a tuned part was mounted) | `GearboxHandle.gearRatio/finalDriveRatio`; `PartModule.CopyDataFrom(ref TuningData)`; `EcuModule.SetStage(byte)` |
| Paint | `MarkDirty` from the paint shop (`sync-workshop-car-tools`) | `SetFactoryColor`, `SetFactoryPaintType`, `color`, `paintData`, `SetCustomCarPaintType(PaintData)` when custom |
| BodyCosmetics | postfix `TintingWindow.TintAction` (selected window, `tintManager.GetSelectedWindow()`) and `TintingWindow.HideAction` (all `tintManager.windows`); `sync-car-parts` local commit event for a body part; `MarkDirty` from paint shop / car wash | per `CarPart`: `SetCarColorAndPaintType`, `SetCustomCarPaintType(part, data)` when Custom, `SetCarLivery`, `SetColorAndOpacity(tint, opacity, isTinted)`, `SetWashFactor`, `EnableDust`, then `UpdateCarBodyPart(part)` |
| Plates | postfix `SetNewLicensePlateNumber(string, bool)`, both `ChangeLicencePlateTexture` overloads | `SetNewLicensePlateNumber(n, isFront)`, `ChangeLicencePlateTexture(part, tex)`, `SetLicensePlateNumber()` |
| Info | 1 Hz poll (mileage changes after a test drive; headlights, also postfix `CarLoader.SwitchCarLights`) | assign `CarInfoData`; `SwitchCarLights` when `LightsOn` differs |
| BonusParts | postfix `SwapBonusPart`, `TakeOffBonusPart`; `MarkDirty` from the paint shop | `SwapBonusPart` / `TakeOffBonusPart(io, true)` for the difference, then paint fields; the inventory side of a fitted or removed part goes through the normal inventory flow (a rare double-take race is accepted) |

- *Why:* the poll catches every path for fluids, wheel sizes and mileage (refill can, `FluidExtractor`, oil
  bin, tire mount, test drive) without per-frame hooks and without depending on small setters that IL2CPP may
  inline into their callers. Those values have no preview state. Paint, tint and cosmetics change live while
  previewing, so they are read only at commit points. The `HideAction` postfix runs after the window has
  restored its backup, so whatever differs then was committed; the value diff drops the rest.
- *Cost:* per loaded car and second, about 10 fluid entries, 4 wheels and one struct. Body parts (~40 per car)
  are never polled.
- *Rejected: hooks only:* misses change paths, and the 0.4.x pour hook fired every frame.
  *Rejected: polling everything:* sends previews.
- The poll and the flusher skip a loader while its car is not loaded, while it is "awaiting snapshot", while a
  remote apply for it is queued or running, before the client's `SyncAck`, and outside the Garage scene.
- Tint opacity: `TintColor` is stored as is; the applier gets the opacity from
  `WindowTintManager.GetOpacityFromColor(TintColor)`. The probe checks the round trip.

### D4b: Corrections from the static spike (2026-10-06, `docs/spikes/car-details.md`)

These override the D4 table where they differ; probe 1.2 checks them at runtime.
- `TintingWindow`, `WheelsAlignmentWindow` and `LampAlignmentWindow.HideAction` share one native body with 36 other
  windows' close methods, so a patch on one fires for all of them: never patch `HideAction`. The paint shop and the
  tint window also restore their previews after `Hide` returns. Commit points: postfix `PaintshopManager.SubmitColor`
  (only for the garage car, `paintshopType == 0`) and postfix `TintingWindow.TintAction`.
- Alignment changes outside its windows too (`CheckMessageOnHide` randomises it, the headlight mount sets it) and has
  no preview state: it moves to the 1 Hz poll with fluids, wheels and info.
- Tuning: `PartModule.Tune(short[], float)` is the single hook for ECU and carburettor (both `ApplyAction`s tail-jump
  into it); `EcuModule.SetStage` shares its body with unrelated setters and must not be patched.
- Lights: `SwitchCarLights` only checks; nothing in the game writes `LightsOn`. Drop the hook and the field.
- Bonus parts: players fit and remove them through `CarLoader.TakeOffBonusPart(InteractiveObject, bool)`
  (`SwapBonusPart` has no callers). Apply remote changes with the `BonusPart` methods, never `TakeOffBonusPart`
  (it adds an inventory item or needs the selected item).
- The paint shop and tint window "wash first?" prompt takes money and cleans the whole car: postfix
  `CarLoader.EnableDust`/`SetWashFactor` when `part == null`.
- `ResizeWheel()` and `TunePart` act on the car under the mouse: never call them on a receiver. `SetET` is never called
  (the game writes ET inline), so wheels stay on the poll.
- `FluidsData` and `LicensePlatesData` are structs with references: writing them back from managed code is checked
  at runtime (GC write barrier) before relying on it; the codec in row 2 already initialises `FluidsData` fully.

### D5: No echo and no flood without suppression flags
- Applying a remote section first writes the incoming values to `lastKnown`, then applies, then re-reads the
  section from the game into `lastKnown`, so normalisation by the setters is absorbed. Hooks or polls that see
  the applied values find no difference and send nothing. No global or per-loader `listen` flag.
- Flooding is bounded by the 0.5 s debounce and the 1 Hz poll.

### D6: Tool handoff API (client)
`CarDetailsSync.MarkDirty(CarLoader loader, CarDetailSection sections, IEnumerable<int> bodyPartIndices = null)`
and `CarDetailsSync.FlushNow(CarLoader loader)`. A null index list means all body parts (car wash).
`sync-workshop-car-tools` calls them on the acting client only:
- paint shop: when `PaintshopManager.IsPainting` turns false after `MakeCarPaintEffects`, `Paint | BodyCosmetics |
  BonusParts`
- car wash: after `CarWashLogic.DoWorkAnim` / `TweenExteriorDustWash` ends, `BodyCosmetics`
- oil bin (`CarLoader.UseOilbin`): nothing needed, the Fluids poll sees it; `FlushNow` is optional
- interior detailing (`TweenInteriorConditionAndDust`): split by what the probe shows it changes. `CarPart`
  dust goes through `MarkDirty(BodyCosmetics)`, part condition and `PartScript` dust through
  `sync-car-parts`' `CarPartsSync.MarkDirty`.

### D7: Spawn snapshot and missing snapshots
- **Spawner:** the client that uploads `sync-car-parts`' part baseline for a loader sends
  `IsFull=true` with every section right after it, and again after every later baseline upload for the same
  `SpawnSeq` (`sync-orders-and-jobs` re-uploads after `PrepareJob`, row 2 after an unpark). That moment is
  after the game's random rolls (`SetRandomDust`, `SetRandomHeadlampAlignment`, `SetRandomWheelsAlignment`,
  `FluidsData.SetRandomLevel/SetRandomCondition`, `SetRandomMileage`, plates). Sending it clears
  "awaiting snapshot" on the spawner.
- **Other clients** stay "awaiting snapshot" until a full snapshot is applied, so their own rolls never leave
  the machine.
- **Server without a snapshot:** a record is valid only when `HasSnapshot` and its `SpawnSeq` equals
  `LoadedCars[loader].SpawnSeq`. Once per second in `ServerWindow.TickServer()` (under `StateLock`), for every
  loaded car with a part baseline and no valid record for more than 10 s, the server sends
  `CarDetailsRequestPacket` to one `InSession` client, the next one on each retry (10 s apart). The client
  answers with `IsFull` if that loader is `Ready` in `sync-car-parts`, otherwise it ignores the request.
  This covers a spawner that disconnects before its snapshot and saves written before this change.

### D8: Server: stores, validates, relays
- **Stores** `CarState.Details: Dictionary<int, ModCarDetails>` keyed by loader.
  - Present sections replace stored ones; keyed entries merge per entry (D1). `IsFull` replaces the record
    and sets `HasSnapshot`. An update whose `SpawnSeq` is newer than the stored record starts a new record.
  - Records whose loader is no longer in `LoadedCars` or whose `SpawnSeq` is stale are dropped by the
    1 s server tick (D7) and skipped by the snapshot (D10). No hook into spawn, delete, park or unpark is
    needed.
- **Persists** as part of the `cars` save section, as `session-persistence-and-rejoin` D2 lays out
  (`cars` "also holds row 4's `CarState.Details`"). Adding the field bumps the `cars` section from v2
  (`sync-car-parts`, which lands first) to **v3** with a no-op `Migrate(data, 2)` step (older data has no
  `Details` and loads as empty). There is no separate
  `car-details` save section; `car-details` is a snapshot provider only (D10).
- **Validates** before storing (the handler already runs under `StateLock`):
  - the loader is in `LoadedCars` and `SpawnSeq` matches; otherwise drop and log
  - fluid levels and conditions, dust and wash clamped to 0..1
  - caps: fluids ≤ 16, gear ratios ≤ 12, modules ≤ 16, tuning values ≤ 64, cosmetics ≤ 256, strings ≤ 64 chars
- **Relays only** the routing data: it stamps `SourceClientId`, passes `ClientSeq` through and routes
  `CarDetailsRequest`. It does not interpret tuning values, textures or livery names.
- **Broadcasts** the accepted (clamped) sections to all `Syncing`/`InSession` clients, including the sender (D9).

### D9: Converging under concurrent edits
- The server sends each accepted update to everyone in arrival order (one lock, ordered TCP/Steam reliable
  stream), so every client applies the same sequence and ends on the server's last write per section/entry.
- A client numbers its updates with `ClientSeq` and remembers its latest seq per `(loader, section)`. Its own
  echo with an older seq is skipped, so its quick successive changes never flip back. Its own echo with the
  latest seq is applied like any other update, which picks up server clamping and is a no-op otherwise.
- *Rejected: send to everyone except the sender:* two same-section writes in flight leave the senders
  crossed (A ends with B's value, B with A's).

### D10: Late-join path
1. `AskForSync` → the server runs the `ISnapshotProvider`s in `SyncOrder` under `StateLock`: world,
   garage, inventory, then `cars` (100, `sync-car-parts`), then `car-details` (150).
2. `[SessionSection] CarDetailsSnapshot : ISnapshotProvider` (key `car-details`, `SyncOrder` 150) sends one
   `CarDetailsUpdatePacket{IsFull=true, SourceClientId=-1}` per valid record and returns that number as the
   item count for `SyncEnd.Items`. Cars without a valid record are left to D7 and are not counted.
3. The client queues detail packets per loader, merging the latest of each section/entry. A coroutine waits
   until `sync-car-parts` reports the loader `Ready`, then applies in this order, each in its own try/catch:
   Wheels (resizing may reset geometry), Tuning, Fluids, Alignment, Paint, BodyCosmetics (after Paint, whose
   car-level setters touch every part), BonusParts, Plates, Info. It then re-reads `lastKnown`, clears "awaiting
   snapshot" and, for a `SourceClientId=-1` packet, calls `SyncTracker.Applied("car-details")`.
4. `SyncAck` goes out when every count is met (row 7), so the details are on screen before the joiner may
   send anything.

Returning to the garage from another scene uses the same path through `sync-players-and-scenes`' repeated
`AskForSync`; detail packets that arrive outside the Garage scene are dropped.

### D11: Failure and race handling
- **Update for a car not yet `Ready`:** queued; the queue is dropped on `CarSpawnDelete` or a `SpawnSeq`
  change.
- **Unknown part key or index:** that entry is skipped with a debug log. Tuning data of an unmounted module
  travels with its item through the inventory.
- **A section throws while applying:** logged; the other sections still apply; `lastKnown` for that section
  is not updated, so the next poll or commit repairs it.
- **Partial update while the server has no snapshot:** merged; a later `IsFull` replaces it.
- **Body part remounted:** the mounting client marks that `PartIndex` dirty on `sync-car-parts`' local commit
  event, so the stored cosmetics follow the new part. The mount packet and the cosmetics update travel on the
  same ordered stream from the same client.
- **Disconnect with unsent dirty sections:** those changes are lost (at most 0.5 s of edits).
- **Server restart:** records are saved; on load, records without a matching car are dropped (D8).

## Risks / Trade-offs

- [`SetWheelSize` ints may not map 1:1 to `Wheel.Width/Size/Profile`] → probe 1.2 reads before/after a call;
  if not, apply through `PartScript.ResizeWheel(carLoader, wheel)` on the mounted tire.
- [`FluidsData.SetLevelAndCondition`'s `id` may be a list index rather than `CarFluid.ID`] → probe on a car
  with two brake reservoirs; adjust `ModFluidLevel.Id` before the scenario runs.
- [Where the live gear ratios and ECU data sit (`GearboxHandle`, `PartModule`, car-level `NewCarData.gearRatio`
  / `ecuData`)] → probe; fallback is the in-memory `NewCarData` of the current profile's car entry, never a
  disk write.
- [The license plate window may preview textures through `ChangeLicencePlateTexture`] → checked by hand once
  in task 4.6 (log the calls while browsing); if it previews, move the Plates hook to the window's confirm or
  hide method.
- [Tool tweens on a receiver writing their own end values after our apply] → `sync-workshop-car-tools` plays only
  effects on receivers (its D7) and calls `MarkDirty` on the actor only.
- [Random rolls after the last baseline upload] → the scenario compares a freshly spawned car; if values
  still differ, the late roll's owner (`sync-orders-and-jobs`) calls `UploadBaseline` after it.
- [Overlap with `sync-car-parts`' body records, whose `ModItem` also carries colour/livery] → those fields are
  for mount time; BodyCosmetics applies after the parts on late join, and the remount rule in D11 keeps the
  stored cosmetics current.
- [Last-writer-wins inside a section or entry drops one of two simultaneous edits] → accepted (logged in
  `QUESTIONS.md`); window ≈ 0.5 s, and per-entry merging makes it rarer.
- [BinaryFormatter size: a full snapshot ≈ 5–15 KB] → one packet per car, within TCP and Steam reliable limits.

## Migration Plan

- New `PacketTypes` values go at the end of the enum; client and server update together (version check).
- Saves without `CarState.Details` load as empty (no-op `cars` migration step); D7's request path fills the
  records on the first connect.
- Rollback: remove the handlers; Newtonsoft ignores the extra field.

## Open questions / assumptions

- **A1 – `sync-car-parts` provides** (client): `CarPartsSync.IsReady(loaderId)`, the current `SpawnSeq` per
  loader, a `BaselineUploaded(loaderId)` event and a `LocalPartsCommitted(loaderId, keys)` event, plus
  `PartRegistry` lookups by key; (server): `LoadedCars[loader].SpawnSeq` and `HasBaseline`. Whatever is
  missing when this change is implemented is added there as a small, separate commit.
- **A2 –** Changing a car's place or lift does not change its loader (`sync-car-placement-and-lifts` §3), so
  details stay keyed by loader. Parking removes the car (its blob carries the details); unparking is a new
  spawn with a new `SpawnSeq` and baseline, which triggers D7.
- **A3 –** `sync-workshop-car-tools` calls `MarkDirty` at its commit points (D6) and sends no detail values of its own.
- **A4 –** Wheel balance and tire pressure are excluded (Non-Goals).
- **A5 – settled:** engine swap → `sync-car-parts` stores, `sync-workshop-car-tools` triggers; dyno results →
  ROADMAP row 13 stores them, `sync-workshop-car-tools` triggers; `LightsOn` → this change, as an `Info` field
  (user decision 2026-10-05).
- **A6 –** Simultaneous same-section edits: last writer wins (in `QUESTIONS.md`).
- **A7 –** The spawner's random rolls become shared (in `QUESTIONS.md`); the server cannot roll them without
  game data.
- **A8 – decided (user, 2026-10-05):** bonus (visual tuning) parts are the ninth section `BonusParts` here
  (D2, D4), applied with `SwapBonusPart`/`TakeOffBonusPart(io, true)`; the inventory side goes through the normal
  inventory flow.
- **References:** hook points confirmed by reading FixForTogether by TogetherFixer; no code adopted. If code
  is adapted later (for example the `IsPainting` watcher), its license requires credit to TogetherFixer, a
  link to its repository and marking the code as modified.
