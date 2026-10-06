# Tasks

> **Read first (2026-10-06):** `docs/spikes/car-details.md` (static decompile). It corrects hooks in design.md that never fire (inlined builders, shared native bodies) and lists the runtime checks still needed.

Prerequisites: `session-persistence-and-rejoin` groups 1–2 (contract, `StateLock`, `SyncTracker`) and
`sync-car-parts` are merged. Check design A1 against the merged `sync-car-parts` code first; add any missing
hook there as a small separate commit.

## 1. Harness read side and game probe (de-risk first)

- [ ] 1.1 `tools/TestHarness/StateDump.cs` `Cars()`: add a `details` object per car, read straight from the
  `CarLoader`, floats through `Round`, lists in a fixed order: `fluids` (`{type, id, level, condition}` from
  `Oil`, `Brake`, `EngineCoolant`, `PowerSteering`, `WindscreenWash`), `wheels` (per `WheelType`:
  `{width, size, profile, et, tire, rim}`), `alignment` (wheels + both headlamps), `gearbox`, `tuning`
  (`{partKey, isTuned, values[], tuningValue, ecuStage}`), `paint`, `bodyCosmetics` (per `carParts[i]`),
  `plates`, `info` (incl. `lightsOn`), `bonusParts` (`ids`, `isPainted`, `color`). Verify: `car-spawn` (from `sync-car-parts`) in a one-client `-KeepRunning` session, then
  `dump`; the `details` block is filled.
- [ ] 1.2 `tools/TestHarness/Features/CarDetailsCommands.cs`: `cardetails-probe <loader>` reports
  - transform paths of every `GearboxHandle` and `PartModule` (type, `EcuModule.Stage`), and whether
    `NewCarData`-style car-level `gearRatio`/`ecuData` exist at runtime
  - every `FluidData` with its list index and `CarFluid.ID`
  - `Wheel` fields before/after `SetWheelSize(w, r, t, FrontLeft)` with test values
  - tint round trip: `SetColorAndOpacity(c, GetOpacityFromColor(c), true)` then read `TintColor`
  - which `CarPart`/`PartScript` fields `TweenInteriorConditionAndDust(1, 0, 0)` changes
  - whether `ModCarFrom`/`ModCarFluidType`/`ModPaintType` match the game enums by ordinal
  - which `CarLoader` field or getter holds the headlights state that `SwitchCarLights` changes
  - `bonusParts` before/after `SwapBonusPart` and `TakeOffBonusPart(io, true)` (models, inventory events)

  Verify: run it once; write the answers into design.md Risks and D6 (interior detailing), and adjust D2/D4
  if a mapping differs.

## 2. Core

- [ ] 2.1 `CMS21-Together-Core/Data/GameType/ModCarDetails.cs` with the D2 types (`[Serializable]`, public
  fields); fill `ModGearboxData` and `ModLPData`. Verify: `dotnet build CMS21-Together.sln` succeeds,
  existing `ModItem.GearboxData`/`LPData` uses still compile.
- [ ] 2.2 Append `CarDetailsUpdate`, `CarDetailsRequest` to `PacketTypes`; add both packets (D3) to
  `Network/Packets/CarPackets.cs`; add `Dictionary<int, ModCarDetails> Details` to `CarState` and bump the `cars` save section
  from v2 to v3 with a no-op `Migrate(data, 2)` step. Verify: server and client start, `PacketRouter` logs both types, and the
  test server's existing save loads with empty `Details`.

## 3. Server

- [ ] 3.1 `Network/Handlers/CarDetailsHandlers.cs`: `CarDetailsUpdate` handler per D8 (loader + `SpawnSeq`
  check, clamp, caps, stamp `SourceClientId`, merge sections and keyed entries, `IsFull` replaces) and
  broadcast to all synced clients including the sender. Add console command `cardetails <loader>` (one-line
  JSON of the record) to `CommandSystem.cs`. Verify: `Send-ServerCommand "cardetails 0"` on a server with no
  car logs `no car`, and with a hand-sent update from 4.3 logs the record.
- [ ] 3.2 `Data/Persistence/CarDetailsSnapshot.cs` (`[SessionSection]`, `ISnapshotProvider` key `car-details`,
  order 150): drop stale records (D8); send `IsFull`, `SourceClientId=-1` per valid record and return the
  count. Verify: the server log lists `car-details` after `cars` on a join, and `SyncEnd.Items` has its count.

## 4. Client

- [ ] 4.1 `Logic/Car/CarDetailsReader.cs` (`Read(CarLoader, CarDetailSection, partIndices)`) and
  `CarDetailsApplier.cs` (D4 setters, D10 order, try/catch per section, unknown keys skipped with a debug
  log). Verify: harness `cardetails-roundtrip <loader>` reads, changes every section, applies the original
  back, reads again and replies `equal`.
- [ ] 4.2 `Logic/Car/CarDetailsSync.cs`: `lastKnown`, per-section `ClientSeq`, "awaiting snapshot" set,
  `MarkDirty`/`FlushNow`, `OnUpdate` flusher (0.5 s debounce, 5e-4 epsilon, changed entries only), 1 Hz poll
  for Fluids/Wheels/Info, D4 skip rules (including no send before `SyncAck`). Verify with 4.3.
- [ ] 4.3 Harness setters in `CarDetailsCommands.cs`, each through the game setters and then `MarkDirty`:
  `cardetails-fluid <loader> <type> <id> <level> <cond>`, `-wheel <loader> <wheelType> <w> <rim> <tire> <et>`,
  `-alignment <loader> <FL> <FR> <RL> <RR>`, `-headlamp <loader> <left|right> <h> <v>`,
  `-gearbox <loader> <final> <r1,r2,…>`, `-tune <loader> <partKey> <tuningValue> <v1,v2,…>`,
  `-paint <loader> <partIndex> <r,g,b,a> <paintType>`, `-tint <loader> <partIndex> <r,g,b,a>`,
  `-wash <loader> <dust> <wash>`, `-plate <loader> <front|rear> <text>`, `-mileage <loader> <km>`,
  `-lights <loader> <on|off>` (`SwitchCarLights`), `-bonus <loader> <bonusPartId|none> [r,g,b,a]`,
  `-randomize <loader>` (the game's random rolls without `MarkDirty`, then `CarPartsSync.UploadBaseline`, as
  `sync-orders-and-jobs` does after `PrepareJob`), `-hold <on|off>` (stop sending spawn snapshots, for 4.7).
  Verify: each changes the local `details` dump; `cardetails-fluid` on one connected client logs exactly one
  `CarDetailsUpdate`.
- [ ] 4.4 `Network/Handlers/CarDetailsHandlers.cs`: per-loader queue merging sections/entries, apply when
  `CarPartsSync.IsReady`, own-echo rule (D9), `lastKnown` before and after apply, drop on delete/`SpawnSeq`
  change or outside the garage, `SyncTracker.Applied("car-details")` for snapshot packets, answer
  `CarDetailsRequest`. Verify: two-client run; B's log shows the apply and B sends no `CarDetailsUpdate` back.
- [ ] 4.5 Spawn snapshot (D7): on `CarPartsSync.BaselineUploaded`, send `IsFull` and clear "awaiting
  snapshot". Verify: after `car-spawn` + `cardetails-randomize` on A, B's `details` equals A's.
- [ ] 4.6 `Logic/Hook/CarDetailsHooks.cs`: the D4 postfixes, including `SwitchCarLights`, `SwapBonusPart` and `TakeOffBonusPart` (they only call `MarkDirty`, resolving the car
  through the window's `carLoader` / `tintManager.carLoader` field), and the `LocalPartsCommitted`
  subscription (Tuning for mechanical keys, BodyCosmetics for body keys). Verify: harness
  `cardetails-ui <gearbox|ecu|carb|wheelalign|lampalign|tint|plate> <loader>` sets the window's car field and
  invokes the commit method; each logs one `MarkDirty` and, after a value change, one update;
  `cardetails-ui tint-cancel <loader>` (`MakeBackup`, preview with `tintManager.UpdateWindow`, `HideAction`)
  sends nothing. A window that cannot be driven while closed is listed in design.md Risks and checked once
  by hand.
- [ ] 4.7 Request timer in `ServerWindow.TickServer()` (1 s, under `StateLock`): D7 rule, 10 s grace, retry with
  the next `InSession` client. Verify: A `cardetails-hold on`, `car-spawn 0`; `Wait-ServerLog` sees a
  `CarDetailsRequest` within 15 s and `cardetails 0` then shows a snapshot.

## 5. Two-instance scenarios (feature done when both pass)

- [ ] 5.1 `tools/test-env/scenarios/car-details.ps1`: A and B connect; A `car-spawn 0` + `cardetails-randomize 0`;
  wait until both `car-ready 0`, then 5 s; compare (spawn values shared). A runs every 4.3 setter; wait 3 s;
  compare. A `cardetails-alignment` while B `cardetails-fluid` (brake); A `cardetails-fluid` (oil) while B
  `cardetails-fluid` (coolant); wait 3 s; compare and assert all four values are present. A
  `cardetails-ui tint-cancel 0`; B's dump is unchanged. Compare with `Compare-HarnessDumps -Sections cars`.
  Verify: `Run-Session.ps1 -Scenario car-details` prints `RESULT car-details: PASSED`.
- [ ] 5.2 `tools/test-env/scenarios/car-details-latejoin.ps1`: only A connects, `car-spawn 0`, runs every 4.3
  setter; B connects; wait for `syncAcked`; compare; A `cardetails-gearbox` once more, wait 3 s, compare
  (live after late join). Then `Send-ServerCommand save`, dump A, `Stop-TestServer`, both `to-menu`,
  `Start-TestServer` (helpers from `session-persistence-and-rejoin` 1.1/1.2); B connects alone; B's `details`
  equals A's last dump. Verify: `Run-Session.ps1 -Scenario car-details-latejoin` prints
  `RESULT car-details-latejoin: PASSED`.
