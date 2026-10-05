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

- New car-detail state, owned by the server and stored per car loader as `CarState.Details`. It has
  eight sections: Fluids, Wheels (size/ET per wheel), Alignment (wheels + headlamps), Tuning (gearbox
  ratios + ECU/carb `PartModule` data), Paint (car colour/factory colour/paint data), BodyCosmetics
  (per `CarPart`: colour, paint type, livery, tint, dust, wash factor), Plates and Info (mileage, buy
  price, origin).
- New packets in `CarPackets.cs`. `CarDetailsUpdatePacket` sends whole sections, not single fields, and
  the server merges them into its stored record. `CarDetailsRequestPacket` lets the server ask a client
  for a full snapshot when it has none. New `PacketTypes`: `CarDetailsUpdate` and `CarDetailsRequest`.
  New DTOs go in `Core/Data/GameType/`. The empty `ModGearboxData` and `ModLPData` get filled in.
- The client marks a section dirty and sends it after a short debounce, only if its values differ from the
  last ones sent or applied. It hooks these points (Harmony postfixes):
  - `GearboxTab.ApplyAction`, `EcuTuning.ApplyAction`, `CarbTuning.ApplyAction`
  - `WheelsAlignmentWindow.HideAction`, `LampAlignmentWindow.HideAction`
  - `TintingWindow.TintAction`
  - `CarLoader.SetWheelSize` and `CarLoader.SetET`
  - `CarLoader.SetNewLicensePlateNumber` and `CarLoader.ChangeLicencePlateTexture`

  A 1 Hz value-diff poll covers Fluids and Info, because those change along many paths (refill can,
  extractor, oil bin, test drive).
- The client applies remote state with the game's own setters, for example
  `FluidsData.SetLevelAndCondition`, `CarLoader.WheelsAlignment`, `SetWheelSize`, `SetCarColor`,
  `SetCarLivery`, `CarPart.SetColorAndOpacity`, `SetWashFactor`, `EnableDust`, `PartModule.Tune`,
  `GearboxHandle` ratios and `SetNewLicensePlateNumber`.
- The client that spawns a car sends a full snapshot once the car has settled, so the random values that
  client rolled become the shared values.
- Late join: once `sync-car-parts` has replayed a car and its parts, the server sends that car's stored
  details to the joining client.
- Client API `CarDetailsSync.MarkDirty(carLoader, sections, partIndices)`. `sync-workshop-tools` calls it
  when the paint shop, car wash, interior detailing or oil bin finishes, so those tools reuse these
  packets.
- Test harness: per-feature `cardetails-*` commands, a `details` block per car in `StateDump`, and the
  scenarios `car-details` (both clients connected) and `car-details-latejoin`.

## Capabilities

### New Capabilities
- `car-details-sync`: shared, server-stored per-car detail state for every car in the garage. It covers
  fluids, wheels and alignment, tuning settings, paint/tint/dirt, license plates and car info: how changes
  spread, how the spawn values become shared, and how a late joiner gets them.

### Modified Capabilities
- None. No specs exist yet in `openspec/specs/`.

## Impact

- **Core**: `PacketTypes.cs`, `Network/Packets/CarPackets.cs`, new `Data/GameType/ModCarDetails.cs` (and
  section DTOs), `ModGearboxData.cs`, `ModLPData.cs`, `Data/ModGameState.cs` (`CarState.Details`).
- **Server**: new `Network/Handlers/CarDetailsHandlers.cs`. `CarHandlers.cs` resets details on
  spawn/delete. `AuthHandlers.OnAskForSync` sends the late-join replay. Saved through the existing
  `GameDataManager` JSON.
- **Client**: new `Logic/Car/CarDetailsSync.cs` (read/apply/diff/flush), `Logic/Car/CarDetailsReader.cs`,
  `Logic/Car/CarDetailsApplier.cs`, `Logic/Hook/CarDetailsHooks.cs`, `Network/Handlers/CarDetailsHandlers.cs`.
- **Harness**: `tools/TestHarness/Features/CarDetailsCommands.cs`, `StateDump.cs`,
  `tools/test-env/scenarios/car-details*.ps1`.
- **Depends on** `sync-car-parts` for part identity (body `PartIndex`, sub-part index path), the late-join
  car replay, and a "car settled after spawn" signal. **Used by** `sync-workshop-tools`.
