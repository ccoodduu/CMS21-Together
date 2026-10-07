# Proposal

## Why

The session is consistent but does not feel shared. When player A takes a wheel off, player B sees the wheel vanish
the moment A's change arrives: `PartApplier` uses the game's "from save" methods (`HideBySavegame`,
`TakeOffCarPartFromSave`, `SwitchCarPart(…, instant: true, …)`), which have no animation, sound or tween, on purpose
(the animated paths add inventory items, XP, money and game-mode switches, `docs/spikes/native-decompile.md` d). A's
avatar meanwhile stands still next to the car: it only walks, crouches and pitches its head (`PlayerInstance`), and
nothing tells B that A holds the OBD scanner, is unscrewing bolts or welds the body (row 5b's `ToolAction` plays
particles at the car, but the avatar does nothing). Driving is not shared at all: `Pie:car_drive` and `Mode:CarDrive`
are still `Planned` in `GuardRules`, and a test drive (row 13) is a separate scene in which every client loads only
its own car, so two players at the test track never see each other's car.

ROADMAP row 17 (milestone M7, release 1.1): visual-only replay of other players' actions, simple work animations on
the remote avatar, then driving sync.

## What Changes

Part 1 (work visuals, M):
- **Visual-only rule**: every effect is a disposable object this change creates (a "ghost" clone of renderers) or a
  pose on the remote avatar. The authoritative apply of rows 1/4/5b runs unchanged and at once; a visual never
  calls a game method that changes state, and a leak detector fails the scenario if one does.
- **Parts moving off/on**: when another player's live `CarPartsChange` unmounts or mounts a part, the receiver
  plays a ghost of that part moving along the game's unmount direction with the game's dissolve tween
  (`TweenHelper.TweenAlphaDissolve`); doors, hood and trunk swing with the game's animated `SwitchCarPart` variant if
  the spike finds it side-effect free, otherwise as a ghost. Snapshots, resyncs and the actor's own results never
  animate.
- **Bolts turning**: while another player holds a part claim (row 1's `CarPartClaimUpdate`), the receiver spins and
  backs out ghost copies of that part's bolts, paced by the actor's progress.
- **Activity and tool in hand**: each client publishes what it is doing (`PlayerActivity`: kind, car, part key,
  game `ToolType` or `ModToolId`, progress) at most 4 Hz and only on change; the server keeps the latest value in the
  presence record, so a late joiner sees the current pose and tool but no old animation. The remote avatar turns
  towards the work target, reaches with its right arm (procedural pose on the mod's own rig), and holds a prop cloned
  from the receiver's own copy of the game's tool model (`ToolsManager` tools).
- A MelonPreferences switch `CMS21Together.RemoteVisuals` (default on) turns the receiver's visuals off; state sync is
  unaffected.

Part 2 (driving, L):
- **Physics authority = the driver.** The driver streams a compact state (position, rotation, velocity, steer, wheel
  spin, rpm, gear, brake/lights) at 15 Hz on the unreliable channel to players in the same scene; observers show a
  kinematic car with colliders off, interpolated 100 ms behind with bounded extrapolation.
- **Test track**: a player who is at the test track too sees the other driver's car, built once from a car blob sent
  when the drive starts (row 2's `NewCarDataCodec`), with the remote engine sound of row 6's `RemoteEngines`.
- **Garage area**: what `Pie:car_drive` does is unknown today (spike first). If it drives a shared garage car, the
  driver claims it with row 13's away claim (new `CarAwayKind.Driving`), observers move their own copy of that car,
  and any placement change at the end goes through row 2. The guard entries `Pie:car_drive` and `Mode:CarDrive`
  move to this row and are allowed in the driving merge commit.

Game hooks and reads:
- Actor (polled at 4 Hz, no new hooks): `ToolsManager.ToolIsActive`, `currentUsedTool`, `CurrentUsedTool`;
  `GameMode.GetCurrentMode()`; own claims (`PartClaims`); `MountObject.GetMountState()` of the claimed part; the
  existing `CarToolActions.Started/Finished` (row 5b) and `ToolSync` machine use (row 5a).
- Receiver (visual only): `TweenHelper.TweenAlphaDissolve`, `LeanTween` on ghosts, `PartScript.GetUnmountDir()` (or
  `unmountDirection`/`customPivotForUnmount`), `MountObject` transforms, `CarLoader.SwitchCarPart(CarPart, bool,
  bool)` with `instant: false` (if the spike clears it).
- Driving: `PrepareCarPhysics`/`BaseCarPhysics` (`rigidBody`, `VehicleController`, `res`), `OpponentCarPhysics`
  (`EnableKinematic`, `LoadCar`) as the model for an observer car, `CarLoader.LoadCarFromFile(NewCarData)`, the
  `car_drive` pie lambda in `PieMenuController` (found by the spike), `GameMode.CarDrive`.
- Packets: **new** `PlayerActivity`, `CarDriveStart`, `CarDriveState`, `CarDriveStop` (appended to `PacketTypes`);
  **changed** `PlayerPresenceRecord` + `Activity` (`[OptionalField]`), `CarAwayKind` + `Driving` (appended, only if
  garage driving needs it). `CarPartsChange`, `CarPartClaimUpdate` and `ToolAction` are used as they are.

## Capabilities

### New Capabilities
- `remote-work-visuals`: other players' work is visible as it happens (parts moving off and on, bolts turning, the
  avatar working with a tool in hand) without changing shared state, within the bandwidth budget, and without
  replaying old actions to a late joiner.
- `remote-driving`: a car driven by another player in the same scene (test track, garage area) is shown moving
  smoothly, with the driver as the only physics authority.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `Network/Packets/VisualPackets.cs` (`PlayerActivityPacket`, `PlayerActivityState`, `ActivityKind`),
  `Network/Packets/DrivePackets.cs` (`CarDriveStartPacket`, `CarDriveStatePacket` with a packed payload,
  `CarDriveStopPacket`, `DriveStateCodec`), `Data/PlayerState.cs` (`Activity`), `PacketTypes.cs` (appended),
  `CarAwayKind` (appended, part 2).
- Server: `Network/Handlers/VisualHandlers.cs` (activity fold + same-scene relay), `Network/Handlers/DriveHandlers.cs`
  and `Data/Presence/ActiveDrives.cs` (latest start/state per driver, sent on scene entry, cleared on stop, scene
  change and leave). Nothing is saved; no snapshot provider, no `SyncOrder` slot.
- Client: new `Logic/Visuals/` (`VisualScope`, `PartGhosts`, `BoltReplay`, `ActivityCapture`, `RemoteActivity`,
  `WorkPose`, `ToolProps`), new `Logic/Driving/` (`DriveCapture`, `RemoteCars`, `DriveInterpolator`,
  `GarageDriveHooks`); one event each added to row 1's `PartChanges` (`RemoteChangeApplying`) and `PartClaims`
  (`ClaimChanged`); `PlayerInstance` hosts `WorkPose`; `GuardRules` driving entries (part 2).
- Harness: `Features/VisualCommands.cs`, `Features/DriveCommands.cs`, dump sections `visuals` and `remoteCars`;
  scenarios `visual-parts`, `visual-activity`, `visual-latejoin`, `visual-screens` (`# needs: graphics`),
  `visual-budget` (`# run-all: lane 3`), `drive-track`, `drive-garage`, `drive-latejoin`; area `visuals` in
  `TestAreas.psm1`.
- Depends on (all merged): row 1 (`PartChanges`, `PartClaims`, `PartRegistry`, `PartApplier`), row 2
  (`NewCarDataCodec`, place changes), row 5a/5b (`ToolSync`, `CarToolActions`), row 6 (presence roster,
  `PlayerInstance`, `RemoteEngines`, `GameSceneInfo.ShowsAvatars`, scene-filtered relay), row 11 (`perf` traffic
  counters, lane 3, soak action table), row 13 (`CarAwayRegistry`, `CarAwaySync`, test track flow), row 14a (guard).
