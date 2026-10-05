# Tasks

## 1. Harness read side and game probe (de-risk first)

- [ ] 1.1 Extend `tools/TestHarness/StateDump.cs` `Cars()` with a `details` object per car. It reads
  straight from the `CarLoader`, and all floats go through `Round`:
  - `fluids`: list of `{type, id, level, condition}` from `FluidsData.Oil` and the four lists
  - `wheels`: per `WheelType`, `{width, size, profile, et, tire, rim}` from `WheelsData.Wheels`
  - `alignment`: `WheelsAlignment.FL/FR/RL/RR` and both headlamps `Horizontal/Vertical`
  - `gearbox`: `{gearRatio[], finalDriveRatio}`
  - `tuning`: list of `{partKey, isTuned, values[], tuningValue, ecuStage}`
  - `paint`: `color`, `factoryColor`, `factoryPaintType`, `IsCustomPaintType`
  - `bodyCosmetics`: per `carParts[i]`, `{index, color, paintType, livery, liveryStrength, isTinted,
    tintColor, dust, washFactor}`
  - `plates`: the five `LicensePlatesData` strings
  - `info`: `{mileage, buyPrice, carFrom}`

  Verify: run `connect` with one car in the save and check that `dump_garage_A.json` has a filled
  `details` block.
- [ ] 1.2 Add `tools/TestHarness/Features/CarDetailsCommands.cs` with `[HarnessCommand("cardetails-probe")]`
  `<loader>`. It returns:
  - the transform path of every `GearboxHandle` and `PartModule` (with type and `EcuModule.Stage`)
    under the car root
  - every `FluidData` with list index and `CarFluid.ID`
  - `Wheel` fields before and after calling `SetWheelSize(w, r, t, FrontLeft)` with test values
  - whether the game `CarFrom`/`CarFluidType`/`PaintType` names match the Mod enums by ordinal

  Verify: run it in one instance, then record the answers to the three probe risks in design.md
  (Risks section) and adjust D2/D4 if the mapping differs.

## 2. Core packets and DTOs

- [ ] 2.1 Add `CMS21-Together-Core/Data/GameType/ModCarDetails.cs`:
  - `CarDetailSection` flags
  - `ModCarDetails`, `ModFluidLevel`, `ModCarWheel`, `ModAlignment`, `ModCarTuning`, `ModPartTuning`,
    `ModCarPaint`, `ModBodyCosmetics`, `ModCarInfo`
  - `ModCarFluidType` and `ModCarFrom`, in the game's ordinal order

  All are `[Serializable]` with public fields (design D2). Verify: `dotnet build CMS21-Together.sln`
  succeeds.
- [ ] 2.2 Fill `ModGearboxData` (`float[] GearRatio; float FinalDriveRatio`) and `ModLPData` (front/rear
  numbers, factory number, front/rear textures). Verify: the solution builds, and existing references to
  `ModItem.GearboxData`/`LPData` still compile.
- [ ] 2.3 Append `CarDetailsUpdate` and `CarDetailsRequest` to `PacketTypes`. Add `CarDetailsUpdatePacket`
  and `CarDetailsRequestPacket` to `Network/Packets/CarPackets.cs` (design D3). Verify: the solution
  builds, and `PacketRouter` discovers both types (startup log lists no unknown-packet warnings).
- [ ] 2.4 Add `Dictionary<int, ModCarDetails> Details` to `CarState` (snapshot flag as `ModCarDetails.HasSnapshot`)
  in `Data/ModGameState.cs`. Verify: the server starts with an old `server_save.json`, and `Details`
  loads as empty without errors.

## 3. Server: store, validate, relay

- [ ] 3.1 Add `CMS21-Together-Server/Network/Handlers/CarDetailsHandlers.cs`, handling
  `[PacketHandler(PacketTypes.CarDetailsUpdate)]`:
  - drop and log if the loader is missing from `LoadedCars` or `CarToLoad` differs
  - clamp values and cap sizes (D8)
  - stamp `SourceClientId`
  - merge: section replace, `BodyCosmetics` by `PartIndex`, `IsFull` replaces all and sets `HasSnapshot`
  - broadcast to all clients, including the sender

  Verify with server console command `cardetails <loader>` (task 3.4) after a harness change.
- [ ] 3.2 In `CarHandlers.HandleCarSpawnRequest`, reset `Details[loader]` to an empty record without a
  snapshot. In `HandleCarSpawnDelete`, remove it. Add `CarDetailsStore.OnBodyPartReplaced(loader,
  partIndex)`, which drops that slot's cosmetics, for `sync-car-parts` to call on a body-part mount or
  swap. Verify: after spawn → change → delete → spawn on the same loader, `cardetails <loader>` shows an
  empty record.
- [ ] 3.3 Late join in `AuthHandlers.OnAskForSync`:
  - after each car's `sync-car-parts` replay and before `SyncEnd`, send `CarDetailsUpdatePacket
    {IsFull=true, SourceClientId=-1}` for loaders with a snapshot
  - for loaders without one, start the D7 request (pick a synced client with the car, not the joiner if
    another exists, retry after 10 s)

  Verify: the server log shows one details packet per loaded car for a joining client, and a
  `CarDetailsRequest` when the snapshot is missing.
- [ ] 3.4 Add server console command `cardetails <loader>` to `CommandSystem.cs`. It prints the stored
  record as indented JSON. Verify: typing it in the server window prints the record.
- [ ] 3.5 Verify persistence: change a fluid with the harness, `stop` the server, check that
  `Saves/server_save.json` contains `CarState.Details` with the changed level, restart, and check that
  `cardetails 0` shows the same.

## 4. Client: read and apply

- [ ] 4.1 Add `CMS21-Together-Client/Logic/Car/CarDetailsReader.cs`, with
  `ModCarDetails Read(CarLoader, CarDetailSection, IEnumerable<int> partIndices)`, using the fields in
  design D4 (Context). The loader id comes only from `CarLoaderPlaces.Get().GetCarLoaderId`. Verify:
  harness `cardetails-read <loader>` (in `CarDetailsCommands.cs`) returns JSON equal to the dump's
  `details` block.
- [ ] 4.2 Add `Logic/Car/CarDetailsApplier.cs`, which applies each section with the setters in the D4
  table, in the order of D10 step 4, with one try/catch per section. Unknown part keys or indices are
  skipped with a debug log. Verify: harness `cardetails-roundtrip <loader>` reads, changes every
  section, applies the original back and reads again, and the result equals the original.
- [ ] 4.3 Add `Logic/Car/CarDetailsSync.cs`:
  - `lastKnown[loader][section]`, the per-section `ClientSeq` record, and the "awaiting snapshot" set
  - `MarkDirty(loader, sections, partIndices)` and `FlushNow(loader)`
  - an `OnUpdate` flusher with a 0.5 s debounce and an epsilon diff of 5e-4
  - a 1 Hz poll for Fluids and Info, with the skip rules from D4

  Verify: harness `cardetails-set` (task 7.1) on a single connected client produces exactly one
  `CarDetailsUpdate` in the client log.
- [ ] 4.4 Add `Network/Handlers/CarDetailsHandlers.cs` (client):
  - a per-loader queue that merges the latest of each section
  - a coroutine that waits for `IsCarLoaded()` and the `sync-car-parts` "parts replay applied" signal
  - skip own stale echoes by `ClientSeq` (D9)
  - write `lastKnown` before applying, then re-read it after
  - drop the queue on `CarSpawnDelete` or a `CarToLoad` change
  - answer `CarDetailsRequest` with `IsFull`

  Verify: in a two-client run, B's log shows the apply, and no `CarDetailsUpdate` is sent back from B.

## 5. Client: change hooks

- [ ] 5.1 Add `Logic/Hook/CarDetailsHooks.cs` with postfixes that only call `MarkDirty`:
  - `CarLoader.SetWheelSize`, `SetET`, `SetRim`, `SetTire` → Wheels
  - `WheelsAlignmentWindow.HideAction`, `LampAlignmentWindow.HideAction` → Alignment
  - `GearboxTab.ApplyAction`, `EcuTuning.ApplyAction`, `CarbTuning.ApplyAction` → Tuning
  - `TintingWindow.TintAction` → BodyCosmetics for `tintManager.windows` indices
  - `CarLoader.SetNewLicensePlateNumber` and both `ChangeLicencePlateTexture` overloads → Plates

  Each hook resolves the car through the window's public `carLoader` field (no memory offsets). Verify
  manually in one instance with the in-game UI: each action logs one `MarkDirty`, and a cancelled
  paint/tint preview logs none.
- [ ] 5.2 Subscribe to the `sync-car-parts` "part mounted" event: mark Tuning dirty, and clear
  `lastKnown` BodyCosmetics for a remounted body slot. Verify: mounting a tuned ECU from inventory sends a
  Tuning update.

## 6. Client: spawn snapshot and initial sync gate

- [ ] 6.1 On a native local spawn (`CarSpawnHooks.LoadCarHook` path, not suppressed), wait for the
  `sync-car-parts` "car settled" signal (fallback `IsCarLoaded()` + 2 s) and send `IsFull` with all
  sections. On a remote `CarSpawnResponse`, mark the loader "awaiting snapshot". Verify: after a spawn in
  a two-client run, B's dump `details` equals A's.
- [ ] 6.2 Make `ClientData.IsInitialSyncFinished` wait for the detail apply queue to drain after
  `SyncEnd`, and expose `carDetailsPending` in `StateDump.Status()`. Verify: during a late join the status
  shows `carDetailsPending > 0`, then `0` before `initialSyncFinished` becomes true.

## 7. Harness commands

- [ ] 7.1 In `tools/TestHarness/Features/CarDetailsCommands.cs`, add `[HarnessCommand]` methods that
  change the car through the same game setters as the UI and then call `CarDetailsSync.MarkDirty` (they
  bypass the UI hooks):
  - `cardetails-fluid <loader> <type> <id> <level> <condition>`
  - `cardetails-wheel <loader> <wheelType> <width> <rim> <tire> <et>`
  - `cardetails-alignment <loader> <FL> <FR> <RL> <RR>`
  - `cardetails-headlamp <loader> <left|right> <h> <v>`
  - `cardetails-gearbox <loader> <final> <r1,r2,...>`
  - `cardetails-tune <loader> <partKey> <tuningValue> <v1,v2,...>`
  - `cardetails-paint <loader> <partIndex> <r,g,b,a> <paintType>`
  - `cardetails-tint <loader> <partIndex> <r,g,b,a>`
  - `cardetails-wash <loader> <dust> <wash>`
  - `cardetails-plate <loader> <front|rear> <text>`
  - `cardetails-mileage <loader> <km>`

  Verify: each command changes the dump's `details` block in the same instance.
- [ ] 7.2 Add `cardetails-preview-paint <loader> <partIndex> <r,g,b,a>`. It calls the paint shop preview
  path (`PaintshopManager.UpdateColor` without `SubmitColor`, then `RestoreColor`). Verify: the other
  client's dump is unchanged (spec: previews are not shared).

## 8. Two-instance scenarios (feature done when both pass)

- [ ] 8.1 Add `tools/test-env/scenarios/car-details.ps1`:
  - A and B connect
  - A spawns a customer car on loader 0 (spawn harness command from `sync-car-parts`)
  - wait 5 s, then compare dumps (spawn values shared)
  - A runs every 7.1 command; wait 3 s; compare
  - A runs `cardetails-alignment` while B runs `cardetails-fluid` on the same car at the same moment;
    wait 3 s; compare, and check that both edits are present
  - A runs `cardetails-preview-paint`; check B is unchanged

  Compare with `Compare-HarnessDumps` on `cars`, including `details`. Verify:
  `Run-Session.ps1 -Scenario car-details` prints `RESULT car-details: PASSED`.
- [ ] 8.2 Add `tools/test-env/scenarios/car-details-latejoin.ps1`:
  - only A connects, spawns a car and runs every 7.1 command
  - B connects afterwards
  - wait for `initialSyncFinished` and `carDetailsPending == 0`
  - compare dumps
  - then A runs `cardetails-gearbox` once more, and the dumps are compared again (live updates after a
    late join)

  Verify: `Run-Session.ps1 -Scenario car-details-latejoin` prints `RESULT car-details-latejoin: PASSED`.
