# Proposal

## Why

Once `sync-car-parts` makes both players see the same parts on a car, everything else about that car still
differs between clients: fluid levels, wheel sizes and alignment, gearbox/ECU/carb tuning, paint, window
tint, dirt, license plates and mileage. Each client also rolls its own random values for these when a car
spawns, so the same car starts out different on every machine. The old 0.4.x mod hooked
`FluidsData.SetLevel` per call, used one global "don't echo" flag and replayed only part of this state.
That caused the fluid and body-panel desyncs in upstream issue #85. The server never stored the state,
so a player who joined late got whatever their own game had randomized.

## What Changes

- New car-detail state, owned by the server and stored per loaded car (`CarState.Details`, saved in the
  `cars` save section and tied to `sync-car-parts`' `SpawnSeq`). It has nine sections: Fluids, Wheels (sizes/ET per wheel), Alignment
  (wheels + headlamps), Tuning (gearbox ratios + ECU/carb `PartModule` data), Paint (car colour/factory
  colour/paint data), BodyCosmetics (per `CarPart`: colour, paint type, livery, tint, dust, wash factor),
  Plates, Info (mileage, buy price, origin, headlights on/off) and BonusParts (visual tuning parts
  `CarLoader.bonusParts`: which are fitted and their paint; user decisions 2026-10-05).
- New packets in `CarPackets.cs`. `CarDetailsUpdatePacket` sends whole sections (list sections only the
  changed entries), and the server merges them into its record. `CarDetailsRequestPacket` lets the server
  ask a client for a full snapshot when it has none. New `PacketTypes`: `CarDetailsUpdate`,
  `CarDetailsRequest`. New DTOs in `Core/Data/GameType/`; the empty `ModGearboxData` and `ModLPData` get filled in.
- The client marks a section dirty and sends it after a 0.5 s debounce, only if its values differ from the
  last ones sent or applied. Harmony postfixes at commit points:
  - `GearboxTab.ApplyAction`, `EcuTuning.ApplyAction`, `CarbTuning.ApplyAction`
  - `WheelsAlignmentWindow.HideAction`, `LampAlignmentWindow.HideAction`
  - `TintingWindow.TintAction`, `TintingWindow.HideAction`
  - `CarLoader.SetNewLicensePlateNumber` and both `CarLoader.ChangeLicencePlateTexture` overloads

  Bonus parts are marked dirty by postfixes on `SwapBonusPart`/`TakeOffBonusPart` and by the paint shop.
  A 1 Hz value-diff poll covers Fluids, Wheels and Info (incl. headlights), because those change along many paths (refill can,
  extractor, oil bin, tire mount, test drive) and have no preview state.
- The client applies remote state with the game's own setters, for example
  `FluidsData.SetLevelAndCondition`, `SetWheelSize`/`SetET`, `SetCarColorAndPaintType`, `SetCarLivery`,
  `CarPart.SetColorAndOpacity`, `SetWashFactor`, `EnableDust`, `PartModule.CopyDataFrom`,
  `GearboxHandle` ratios and `SetNewLicensePlateNumber`.
- The client that uploads a car's part baseline (`sync-car-parts`) also sends a full detail snapshot, so the
  random values that client rolled become the shared values.
- Late join: a `car-details` snapshot provider (`SyncOrder` 150, after all cars) from
  `session-persistence-and-rejoin`'s contract; items are counted by the contract's `SyncTracker`.
- Client API `CarDetailsSync.MarkDirty(carLoader, sections, partIndices)` / `FlushNow`, called by
  `sync-workshop-car-tools` when the paint shop or car wash finishes.
- Test harness: `cardetails-*` commands, a `details` block per car in `StateDump`, and the scenarios
  `car-details` and `car-details-latejoin`.

## Capabilities

### New Capabilities
- `car-details-sync`: shared, server-stored per-car detail state for every car in the garage: fluids, wheels
  and alignment, tuning settings, paint/tint/dirt, license plates and car info; how changes spread, how spawn
  values become shared, and how a late joiner gets them.

### Modified Capabilities
- None. No specs exist yet in `openspec/specs/`.

## Impact

- **Core**: `PacketTypes.cs`, `Network/Packets/CarPackets.cs`, new `Data/GameType/ModCarDetails.cs`,
  `ModGearboxData.cs`, `ModLPData.cs`, `Data/ModGameState.cs` (`CarState.Details`).
- **Server**: new `Network/Handlers/CarDetailsHandlers.cs`, new `Data/Persistence/CarDetailsSnapshot.cs`
  (`ISnapshotProvider`), a version step in the `cars` save section, the request timer in `ServerWindow.TickServer()`, a `cardetails`
  console command in `CommandSystem.cs`.
- **Client**: new `Logic/Car/CarDetailsSync.cs`, `CarDetailsReader.cs`, `CarDetailsApplier.cs`,
  `Logic/Hook/CarDetailsHooks.cs`, `Network/Handlers/CarDetailsHandlers.cs`.
- **Harness**: `tools/TestHarness/Features/CarDetailsCommands.cs`, `StateDump.cs`,
  `tools/test-env/scenarios/car-details*.ps1`.
- **Depends on** `session-persistence-and-rejoin` groups 1–2 (contract, `StateLock`, `SyncTracker`) and
  `sync-car-parts` (part keys, `SpawnSeq`, loader `Ready`, baseline-uploaded and local-commit events).
  **Used by** `sync-workshop-car-tools`.
