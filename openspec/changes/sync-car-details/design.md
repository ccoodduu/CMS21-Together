# Design

## Context

- The server can spawn and delete cars (`CarHandlers`, `CarState.LoadedCars`). `sync-car-parts` (being
  written in parallel) adds part mount state and the late-join replay of each car and its parts. Nothing
  syncs the other state a `CarLoader` holds yet.
- The game keeps that state on the live `CarLoader`. All names below were checked against the Unhollower
  stubs in `decomp/Assembly-CSharp-firstpass`:
  - `FluidsData FluidsData` (`Oil`, plus lists `Brake`, `EngineCoolant`, `PowerSteering` and
    `WindscreenWash` of `FluidData{Level, Condition, CarFluid{FluidType, ID}}`)
  - `CarWheelsData WheelsData.Wheels[]` (`Wheel{Tire, Rim, Width, Size, Profile, ET}`)
  - `WheelsAlignment WheelsAlignment` (`FL/FR/RL/RR`)
  - `HeadlampAlignment HeadlampLeftAlignment` and `HeadlampRightAlignment` (`Horizontal/Vertical`)
  - `color`, `factoryColor`, `factoryPaintType`, `paintData` and `IsCustomPaintType`
  - `List<CarPart> carParts`, where each `CarPart` has `Color`, `PaintType`, `PaintData`, `Livery`,
    `LiveryStrength`, `IsTinted`, `TintColor`, `Dust` and `WashFactor`
  - `LicensePlatesData LicensePlatesData` and `CarInfoData CarInfoData{Mileage, BuyPrice, CarFrom}`

  Tuning is stored on part components instead: `GearboxHandle{gearRatio[], finalDriveRatio}` and
  `CMS.PartModules.PartModule{TuningData data}`, with subclasses `EcuModule{Stage}` and `CarbModule`.
- The server runs without game types and saves `ModGameState` as JSON (`GameDataManager.SaveSession`).
  Packets are serialized with BinaryFormatter (`Packet.cs`).
- Lessons from the 0.4.x mod (`upstream-MainMod`):
  - It sent one `CarFluid` packet per `FluidsData.SetLevel`/`FluidData.SetLevel` call, which fires every
    frame while pouring.
  - One global `listen` flag that reset itself on the next call was meant to prevent echoes. It raced.
  - `SetLevelAndCondition` was applied without the reservoir id, so every brake and coolant reservoir got
    the same value.
  - The loader id was parsed from `gameObject.name[10]`.
  - `ModCarFrom` was declared in a different order from the game's `CarFrom`.
  - A late joiner got a `ModNewCarData` that was taken at spawn and never updated. This is the
    background of issue #85.
- FixForTogether (edge cases only, no code adopted) shows that:
  - The final values are only reliable at commit points: tuning `ApplyAction`, alignment windows
    `HideAction`, `TintingWindow.TintAction`, and the paint shop's `IsPainting` turning false.
  - The paint shop and tint window change the car live while the player previews, and restore it on
    cancel.
  - It read state by calling `CarLoader.SaveCarToFile()` and then reading the profile's `NewCarData`.
    That writes into the player's profile, and we avoid it.

## Goals / Non-Goals

**Goals:**
- One server-owned record per car loader holding the eight sections in `specs/car-details-sync/spec.md`,
  which is also saved with the session.
- Detecting changes in a way that never sends preview values and never echoes or floods.
- Spawn values become shared, and a late join is complete.
- A small client API and a stable packet that `sync-workshop-tools` can reuse.

**Non-Goals:**
- Mounting and unmounting parts, part condition, dent, quality, and dust/paint on mechanical
  `PartScript`s (`sync-car-parts`).
- Tool interaction, animations, costs and tool occupancy (`sync-workshop-tools`).
- Wheel balance. `WheelData.IsBalanced` exists only on items and the balancer machine, so it belongs to
  `sync-workshop-tools` and the inventory.
- Tire pressure, which the game does not model.
- Moving a car between loaders (`sync-car-placement-and-lifts`).
- The save format version (`session-persistence-and-rejoin`).

## Decisions

### D1: Section snapshots instead of per-field deltas
Each update carries whole sections. A section that is present replaces the stored one, and a missing
(null) section means "unchanged". BodyCosmetics is a list of per-`PartIndex` entries, merged entry by
entry.
- *Why:* applying a snapshot twice gives the same result, the order of updates inside a section does not
  matter, and the same packet serves live updates, the initial spawn snapshot and the late-join replay.
  Merging by section keeps concurrent edits to different sections, for example alignment by A and
  fluids by B.
- *Rejected: per-field delta packets* (the 0.4.x style). They need many packet types, flood while values
  change continuously, and offer no full-state recovery.
- *Rejected: one blob for the whole car.* Two players editing different sections would overwrite each
  other, and every change would carry about 40 body parts.
- *Rejected: an opaque `NewCarData.Serialize(BinaryWriter, saveVersion)` blob applied with
  `CarLoader.LoadCarFromFile(NewCarData)`.* The server could not validate or merge it, it ties us to the
  game's save version, it reloads the whole car (stepping on `sync-car-parts`), and reading it needs
  `SaveCarToFile`, which writes the player's profile.

### D2: Data model (Core)
`Data/GameType/ModCarDetails.cs` holds:
- `[Flags] enum CarDetailSection {Fluids, Wheels, Alignment, Tuning, Paint, BodyCosmetics, Plates, Info}`
- `ModCarDetails` with `bool HasSnapshot` and one nullable field per section:
  - `List<ModFluidLevel>`, where `ModFluidLevel` is `{ModCarFluidType Type; int Id; float Level; float Condition}`
  - `ModCarWheel[4]`, where `ModCarWheel` is `{int Width, Size, Profile, ET; string Tire, Rim}` indexed
    by `WheelType`. Tire and Rim are for diagnostics only.
  - `ModAlignment{float FL, FR, RL, RR; float LampLH, LampLV, LampRH, LampRV}`
  - `ModCarTuning{string GearboxPartKey; ModGearboxData Gearbox; List<ModPartTuning> Modules}`, where
    `ModPartTuning` is `{string PartKey; ModTuningData Data; int EcuStage /* -1 if not ECU */}`
  - `ModCarPaint{ModColor Color, FactoryColor; ModPaintType FactoryPaintType; ModPaintData PaintData; bool IsCustom}`
  - `List<ModBodyCosmetics>`, where `ModBodyCosmetics` is `{int PartIndex; ModColor Color; ModPaintType
    PaintType; ModPaintData PaintData; string Livery; float LiveryStrength; bool IsTinted; ModColor
    TintColor; float Dust; float WashFactor}`
  - `ModLPData`, filled in with the five `LicensePlatesData` strings
  - `ModCarInfo{int Mileage, BuyPrice; ModCarFrom CarFrom}`

Rules:
- `ModGearboxData` gets `float[] GearRatio; float FinalDriveRatio`. That matches the game's
  `GearboxData`, so `ModItem.GearboxData` can carry it too.
- `ModCarFluidType` and `ModCarFrom` mirror the game enums in the game's order. `CarFrom` is `None,
  Junkyard, Barn, Order, Mission, Auction, Salon`. The harness probe checks the ordinals by
  comparing names (the repo has no unit test project).
- A part key is `sync-car-parts`' sub-part identity (`CarSubPartIdentity.BuildKey(PartIndexPath)`), and
  `PartIndex` is its body-part index. Loader ids always come from `CarLoaderPlaces.GetCarLoaderId`.

### D3: Packets
Both new `PacketTypes` values are appended at the end of the enum.
- `CarDetailsUpdatePacket{int CarLoaderID; string CarToLoad; bool IsFull; int SourceClientId; int
  ClientSeq; ModCarDetails Details}` is used in both directions. A tool reuses it unchanged through D6.
- `CarDetailsRequestPacket{int CarLoaderID; string CarToLoad}` goes from the server to one client and
  asks that client for a full snapshot.

### D4: Detecting changes: commit hooks plus value diff, and polling only where previews cannot happen
The client keeps `lastKnown[loader][section]`: the last values it sent or applied, re-read from the game
after applying. A hook or the poll only marks `(loader, section)` dirty. A flusher in `OnUpdate` sends the
dirty sections of a loader 0.5 s after the last mark, and only those whose current values differ from
`lastKnown`. Floats count as different when they differ by more than 5e-4.

| Section | Marked dirty by | Applied with |
|---|---|---|
| Fluids | 1 Hz poll of `FluidsData` | `FluidsData.SetLevelAndCondition(level, cond, type, id)` per reservoir |
| Wheels | postfix `CarLoader.SetWheelSize`, `SetET`, `SetRim`, `SetTire` | `SetWheelSize(width, rimSize, tireSize, WheelType)`, `SetET`, `UpdateWheels(front)` |
| Alignment | postfix `WheelsAlignmentWindow.HideAction`, `LampAlignmentWindow.HideAction` (window field `carLoader`) | assign `WheelsAlignment`, `HeadlampLeftAlignment`/`HeadlampRightAlignment` (struct ctors from `*AlignmentData`) |
| Tuning | postfix `GearboxTab.ApplyAction`, `EcuTuning.ApplyAction`, `CarbTuning.ApplyAction` (field `carLoader`); `sync-car-parts` "part mounted" event | `GearboxHandle.gearRatio/finalDriveRatio`; `PartModule.CopyDataFrom(ref TuningData)`; `EcuModule.SetStage` |
| Paint | `MarkDirty` from the paint shop (`sync-workshop-tools`) | `SetFactoryColor`, `SetFactoryPaintType`, `color`/`paintData`, `SetCustomCarPaintType(PaintData)` |
| BodyCosmetics | postfix `TintingWindow.TintAction` (car from `tintManager.carLoader`); `MarkDirty` from paint shop/car wash | per `CarPart`: `SetCarColorAndPaintType`, `SetCustomCarPaintType(part, data)` when Custom, `SetCarLivery`, `CarPart.SetColorAndOpacity`, `SetWashFactor`, `EnableDust`, then `UpdateCarBodyPart(part)` |
| Plates | postfix `SetNewLicensePlateNumber`, both `ChangeLicencePlateTexture` overloads | `SetNewLicensePlateNumber(n, isFront)`, `ChangeLicencePlateTexture(part, tex)`, `SetLicensePlateNumber()` |
| Info | 1 Hz poll of `CarInfoData` (mileage changes after a test drive) | assign `CarInfoData` |

- *Why:* polling catches every way a fluid level or the mileage can change (refill can, `FluidExtractor`,
  oil bin, engine crane, test drive) without per-frame hooks. Those values have no preview state.
  Paint, tint and cosmetics are changed live while the player previews, so they are read only at commit
  points.
- *Rejected: hooks only.* The 0.4.x approach misses change paths and fires every frame.
- *Rejected: polling everything.* It would send previews, and it costs too much for about 200 `CarPart`s.
- The poll and the flusher skip a loader while its car is not loaded, while it waits for its first
  snapshot (D7), while a remote apply for it is queued or running, and outside the Garage scene.

### D5: No echo or flood without suppression flags
- Applying a remote section first stores its values in `lastKnown`. After applying, the client re-reads
  the section from the game and stores that, so any normalization the setters do is absorbed. Hooks
  that fire during the apply then find no difference and send nothing.
- No global or per-loader `listen` flag is needed.
- Flooding is limited by the 0.5 s debounce plus the 1 Hz poll.

### D6: Tool handoff API (client)
`CarDetailsSync.MarkDirty(CarLoader loader, CarDetailSection sections, IEnumerable<int> bodyPartIndices = null)`
and `CarDetailsSync.FlushNow(CarLoader loader)`. A null index list means all body parts (car wash).

`sync-workshop-tools` calls these at its commit points:
- paint shop: after `PaintshopManager.IsPainting` turns false following `MakeCarPaintEffects`, with
  `Paint | BodyCosmetics`
- car wash: at the end of `CarWashLogic.DoWorkAnim` / `TweenExteriorDustWash`, with `BodyCosmetics`
- oil bin: `CarLoader.UseOilbin`, with `Fluids`

Interior detailing changes `PartScript` dust and condition, so it stays on `sync-car-parts`' sub-part
packet.

### D7: Spawn snapshot and missing snapshots
- The client that spawned a car natively (the `CarSpawnHooks.LoadCarHook` path, not suppressed) sends
  `IsFull=true` with every section once `sync-car-parts` reports the car as settled. Settled means after
  the game's random rolls (`SetRandomDust`, `SetRandomHeadlampAlignment`, `SetRandomWheelsAlignment`,
  `FluidsData.SetRandomLevel`/`SetRandomCondition`, `SetRandomMileage`, plates).
  - If that signal does not exist, the fallback is `IsCarLoaded()` plus 2 s.
- Every other client marks the loader "awaiting snapshot" when it handles `CarSpawnResponse`. Until the
  full snapshot is applied it neither polls nor sends for that loader, so its own random rolls never
  leave the machine.
- On `CarSpawnResponse` the server creates an empty record with `HasSnapshot=false`. When it needs a
  snapshot and has none, it sends `CarDetailsRequestPacket`:
  - when to ask: at late join, or after loading a save that has cars without details
  - whom to ask: a connected, synced client with that car loaded, not the joiner. If the joiner is the
    only client, it asks the joiner after its car has loaded.
  - retry: after 10 s with the next candidate
  - The answer is an `IsFull` update, handled like any other.

### D8: Server: what it stores and what it only relays
- **Stores** `CarState.Details: Dictionary<int, ModCarDetails>`, keyed by loader, with `HasSnapshot`.
  - Present sections replace stored sections. `BodyCosmetics` merges by `PartIndex`.
  - `IsFull` replaces the whole record.
  - It is saved through the existing JSON save. It is reset on `CarSpawnResponse` and removed on
    `CarSpawnDelete`.
  - When `sync-car-parts` handles a body-part mount or swap, it calls
    `CarDetailsStore.OnBodyPartReplaced(loader, partIndex)`. That drops the cosmetics stored for that slot
    (spec: "Mounting a body part replaces its cosmetics"), because the mount event carries the new part's
    cosmetics.
- **Validates** before storing:
  - The loader is in `LoadedCars` and `CarToLoad` matches. Otherwise the update is dropped and logged.
  - Fluid levels and conditions, dust and wash are clamped to 0..1.
  - List sizes are capped: fluids ≤ 16, gear ratios ≤ 12, modules ≤ 16, values ≤ 64, cosmetics ≤ 256,
    strings ≤ 64 chars.
- **Relays only** `SourceClientId` and `ClientSeq` (the server stamps the sender id), and routes
  `CarDetailsRequest`. It does not interpret tuning values, textures or livery names.
- **Broadcasts** the validated sections to all clients, including the sender (D9).

### D9: Converging under concurrent edits
- The server sends each accepted update to everyone in arrival order. TCP keeps that order per client,
  so every client applies the same sequence and ends on the server's last write for each section.
- A client numbers its updates with `ClientSeq` and remembers the latest seq it sent per
  `(loader, section)`. It skips its own echo of a section when the seq is older than its latest, so its
  own quick successive changes never flip back. An echo of the latest seq has no effect.
- *Rejected: send to everyone except the sender* (the current car-spawn handlers). Two same-section
  writes in flight leave the senders crossed: A ends with B's value and B with A's.

### D10: Late-join path
1. The client sends `AskForSync`. The server sends WorldState, GarageState and inventory as today.
2. Per loaded car, `sync-car-parts` sends `CarSpawnResponse` and the parts replay. Right after each
   car, the server sends `CarDetailsUpdatePacket{IsFull=true, SourceClientId=-1}` from
   `CarState.Details`. If `HasSnapshot` is false, it sends nothing and goes through D7.
3. `SyncEnd` follows as today.
4. The client queues detail packets per loader, merging the latest of each section. A coroutine waits
   for `carLoader.IsCarLoaded()` and for `sync-car-parts`' "parts replay applied" for that loader. It
   then applies the sections in this order, each one in its own try/catch:
   1. Wheels, because resizing may reset geometry
   2. Tuning
   3. Fluids
   4. Alignment
   5. Paint
   6. BodyCosmetics, after Paint because car-level paint setters touch every part
   7. Plates
   8. Info

   After applying, it re-reads the sections into `lastKnown` and clears "awaiting snapshot".
5. `ClientData.IsInitialSyncFinished` becomes true only after `SyncEnd` and once the detail queue is
   empty. `sync-car-parts` has to make its parts replay count toward this in the same way.

Returning to the garage from another scene reuses this path, through whatever replay
`sync-players-and-scenes` triggers.

### D11: Failure and race handling
- **Update for a car that is not loaded yet:** it is queued. The queue is dropped on `CarSpawnDelete`, or
  when the loader's `CarToLoad` changes.
- **Unknown part key or index:** that entry is skipped with a debug log. Tuning data on an unmounted
  module travels with the item through the inventory.
- **A section throws while applying:** it is logged, and the remaining sections still apply.
  `lastKnown` for that section is not updated, so the next poll or commit can repair it.
- **Server receives a partial update while `HasSnapshot=false`:** it merges it. A later `IsFull` from the
  spawner wins.

## Risks / Trade-offs

- [`SetWheelSize(wheelWidth, rimSize, tireSize, …)` may not map 1:1 to `Wheel.Width/Size/Profile`] →
  Task 1.2 probe reads the values before and after a call in-game. If they don't map, apply through
  `PartScript.ResizeWheel` on the mounted tire.
- [The `id` argument of `FluidsData.SetLevelAndCondition` may be a list index rather than `CarFluid.ID`]
  → Task 1.2 probe on a car with two brake reservoirs. Adjust the key in `ModFluidLevel` before the
  scenario runs.
- [Where `GearboxHandle` and `PartModule`s live, and whether `GearboxTab.ApplyAction` writes to the
  handle or only into save data, is unknown] → Task 1.2 probe. Fallback: also apply to
  `NewCarData.gearRatio` in the current profile's car entry. No disk write: profile safety is
  `session-persistence-and-rejoin`.
- [Tool animations or tweens on a receiver, such as the wash tween, could write their own end values after
  our apply] → `sync-workshop-tools` must not run state-changing tweens on receivers, or must call `MarkDirty`
  only on the acting client and leave the final state to this packet.
- [The game rolls random values after the "settled" moment, for example in job setup] → the scenario
  compares a freshly spawned customer car on both clients. If values still differ, move the settle
  signal later (coordinate with `sync-orders-and-jobs`).
- [Overlap with `sync-car-parts`' body-part packet, whose `ModItem State` also has colour and tint fields]
  → ownership rule: those fields apply only at mount time. Later in-place changes go through
  BodyCosmetics, and the server drops a slot's stored cosmetics on remount.
- [Last-writer-wins inside a section can drop one of two simultaneous edits of the same section] → this
  is accepted. The window is about 0.5 s and the case is rare.
- [BinaryFormatter size: a full snapshot is about 5–15 KB, a late join with 5 cars about 75 KB] → this is
  within the TCP and Steam reliable message limits.

## Migration Plan

- The new `PacketTypes` values go at the end of the enum, so client and server have to be updated
  together, as with any packet change.
- Old saves have no `Details`. They load as null, and D7's request path fills them in on the first
  connect.
- Rollback means removing the handlers. Newtonsoft ignores the extra `Details` field in a save.

## Open questions / assumptions

These are decisions taken so this change can be specified on its own. The user should confirm the ones
marked **(ask)**.

- **A1 – sync-car-parts provides:**
  - body `PartIndex` and sub-part `PartIndexPath` identity
  - a per-car slot in `OnAskForSync` to append our replay
  - client signals "car settled after local spawn" and "parts replay applied for loader X"
  - a client "part mounted" event
  - a server call when a body part is mounted or swapped

  If any of these is missing when this change is implemented, it gets added here as a small hook and
  the other change's tasks are updated.
- **A2 –** `sync-car-placement-and-lifts` moves `CarState.Details` together with the car when the car
  changes loader.
- **A3 –** `sync-workshop-tools` calls `MarkDirty` at its commit points (D6). It does not sync these
  values through its own packets.
- **A4 –** Wheel balance and tire pressure are excluded (see Non-Goals).
- **A5 – (ask)** No roadmap row owns dyno results (`EngineData.measured`, `MeasuredDragIndex`),
  `LightsOn` or engine swap (`NewCarData.engineSwap`). The proposal: engine swap goes to
  `sync-car-parts`/`sync-workshop-tools` (engine crane), dyno results to `sync-workshop-tools`, and
  `LightsOn` to `sync-players-and-scenes`.
- **A6 – (ask)** Simultaneous edits of the same section use last-writer-wins, with no locking or warning.
- **A7 – (ask)** Whoever spawns a car decides its random details, and a late joiner's local rolls are
  discarded. That is what the spec says. The alternative, letting the server roll them, is impossible
  without game data.
- **References:** the hook points were confirmed by reading FixForTogether by TogetherFixer. No code was
  adopted. If code is adapted from it later, for example the `IsPainting` completion watcher, its
  license requires credit to TogetherFixer, a link to its repository, and marking the code as modified.
