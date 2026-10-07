# Design

## Context

See proposal.md for the motivation. Facts that shape the approach (`main` = `c01427f`, dump
`%USERPROFILE%\CMS21-TestInstalls\native\out\il2cppdumper\dump.cs`, read only):

- **Remote part apply is instant on purpose.** `PartChanges.OnRemoteChange` → `PartApplier.Apply` uses
  `CarLoader.TakeOffCarPartFromSave`/`TakeOnCarPartFromSave`, `SwitchCarPart(part, instant: true, switched)`,
  `PartScript.HideBySavegame`, `ShowBySaveGame` plus the managed `ShowMounted` replica (renderers back on after
  0.5 s, `MountObject.SetFullMountPosition`). The animated game paths are not usable on a receiver: `PartScript.Hide()`
  (`<Hide>d__159`: dissolve tween and move, `Inventory.Add`, `PartTakeOff` SFX, `AddPlayerExp(1)`,
  `GameMode.SetCurrentMode`, fluid spill money), `<ShowMounted>d__155` (`Inventory.Delete`, XP, `Examine`),
  `<TakeOffCarPart>` (SFX, `Inventory.Add`, LeanTween move, rust map). `ApplyingRemote.Scope(loader)` already
  suppresses our own hooks during an apply.
- **Visual-only building blocks in the game:** `TweenHelper.TweenAlphaDissolve(GameObject, alphaTo, time, delay)`
  (static, used by `UnMountByGroup`); `PartScript.unmountDirection`, `unmountVector`, `customPivotForUnmount`,
  `unmountSpinning`, private `GetUnmountDir()`, private `ShowMountAnimation()` (LeanTween); `MountObject`
  (`mountState`, `posUnMount`, `child`, `childPosUnMount`, `GetMountState()`, `SetFullMountPosition`,
  `SetFullUnmountPosition`, `CalcUnmountPos`); `CarLoader.SwitchCarPart(CarPart, bool instant, bool switched)` and
  the `SwitchCarPart(CarPart, bool)` coroutine (doors, hood, trunk).
- **Who works where is already on the wire for parts:** row 1's `PartClaims` claims the part keys in the prefixes of
  `PartScript.ActionUnMount`/`ActionMount` and `CarLoader.TakeOffCarPart`; every client gets
  `CarPartClaimUpdatePacket { CarLoaderID, Keys, OwnerPlayerId }` (`Released` = -1). The claim spans the bolt work;
  the commit arrives as `CarPartsChange`, then the release.
- **Tools in hand:** `ToolsManager` holds the examine and fluid tools (`ObdScanner`, `Compression`, `Multimeter`,
  `TireTreadDepthTester`, `CompoundMeter`, `FluidExtractor`, `OilBayonet`, five `FluidRefill`s, `Oil_drain_h`,
  `MountObjectSpray`), `ToolIsActive`, `CurrentUsedTool` (GameObject), `currentUsedTool` (`ToolType`), and renders them
  through `CarToolsRenderCamera` into a `RawImage` overlay (own layer). `GameScript.currentExamineType`. Car tools
  (welder, wash, interior kit, paint shop, oil bin, crane) report through row 5b's `CarToolActions.Started/Finished`;
  the receiver plays `ToolAction` particles at the car (`ToolActionPacket` has no actor id).
- **Remote avatar:** `ModGameManager.PlayerPrefab` from the mod's `Assets/playermodel.bundle` (`model_rigged`,
  animator `model_ac` with `Horizontal`, `Vertical`, `IsGrounded`, `IsCrouching`). `PlayerInstance` interpolates
  movement and, in `LateUpdate`, rotates the spine and head bones found by name for the camera pitch. A seated avatar is
  hidden (`PresenceManager.ReconcileAvatar`). No work animation clips exist.
- **Presence (row 6):** `PlayerPresenceRecord` (name, scene, seat, engine, `LastMovement`) is stored by the server,
  sent in the `players` snapshot (`SyncOrder` 500) and relayed on change; movement is relayed only to clients in the
  same scene with `GameSceneInfo.ShowsAvatars` (`Server.SendToClient(packet, id, reliable: false)`).
  `RemoteEngines` plays a remote engine with a copy of the car's `RealisticEngineSound` prefab.
- **Driving today:** `GuardRules` has `Planned(Mode, "CarDrive")` and `Planned(Pie, "car_drive")` (owner "row 6 part
  2"); `GuardHooks` treats `CarDrive` as log-only. The test track is its own scene (`docs/spikes/test-drive.md`): each
  client loads only `GlobalData.SelectedCarLoader` with `PrepareCarPhysics` (VPP: `VPVehicleController`,
  `rigidBody`, `res`), `TestTrackManager` sets `GameMode.CarDrive`. Row 13 claims the car (`CarAwayKind.TestTrack`)
  so nobody edits it while it is away. The drag strip already shows a non-player car: `OpponentCarPhysics`
  (`LoadCar(CarForDragstrip)`, `EnableKinematic(bool)`, `OpponentVPSelfDrive`). What the garage pie option
  `car_drive` does has not been decompiled (`ParkingSpace.DriveIn/DriveOut` exist in the Parking scene).
- **Budget (row 11):** the 15-minute four-player soak measured 1.3–2.7 kB/s download per client; the budgets are
  average download ≤ 50 kB/s and upload ≤ 20 kB/s per client, peak 10 s window ≤ 1 MB/s outside snapshots. Packets are
  `BinaryFormatter` objects, so a small packet costs a few hundred bytes of type header; the server's `perf` command
  counts bytes per packet type.
- **Test games run headless by default** (`-batchmode -nographics`): transforms, coroutines and tweens run, nothing is
  rendered, `Renderer.isVisible` is always false. A scenario with `# needs: graphics` runs visible.

## Goals / Non-Goals

**Goals:**
- Another player's part work, tool use and driving are visible in time with what they do, so a session feels shared.
- Never change shared or local game state through a visual; state sync stays the only authority, and a visual that
  is cut short, dropped or turned off leaves exactly the state the sync applied.
- Stay well inside row 11's budget with four busy players.
- A late joiner (and a player returning from travel) sees the current activity of everyone, never a replay.

**Non-Goals:**
- New animation clips in `playermodel.bundle` (open question 3); a carried part in the avatar's hands (parts live in
  the inventory in this game); facial or finger animation.
- Showing a test drive to players who are not at the test track (no spectator camera from the garage); racing and the
  other tracks (guard backlog); car-to-car or car-to-player collisions between players (open question 1).
- Replaying machine minigames (balancer, tire changer) beyond the avatar pose and row 5a's existing machine state;
  parts on the engine stand (row 5a) apply without a visual in v1.
- Anything that changes timing or order of state sync (rows 1–14).

## Decisions

### D1. Visual-only contract

A visual is either a ghost (a GameObject this change creates: cloned `MeshFilter`/`MeshRenderer` pairs with the
source's materials, no colliders, no scripts, layer of the source part) or a pose/prop on a remote avatar. The only
writes to existing game objects are `Renderer.forceRenderingOff` on the real part or bolts that a ghost stands in for
(no game code uses it; `PartApplier` and the game toggle `enabled`, so the two never fight), always recorded and
restored by the effect's `finally`, by `VisualScope.CancelFor(loader, key)` before any later apply of that part, by a
car delete or snapshot of that loader, and by `ClientData.Reset()`.

- The authoritative apply is never delayed: the ghost is cloned before `PartApplier.Apply` and animated after it.
- Effects run inside `VisualScope`; while it is active, row 1's `PartChangeTracker.MarkDirty`, row 4's
  `CarDetailsSync.MarkDirty`, the inventory hooks and `GameMode.SetCurrentMode` log `[Visuals] state leak <what>` as
  an error. Scenarios fail on that line (row 11's error scan picks it up too).
- Forbidden on a receiver (listed in `VisualScope` for the review): `PartScript.Hide/ShowMounted/DoMount/FastUnmount/
  FastMount/ActionUnMount/ActionMount`, `CarLoader.TakeOffCarPart`, `GarageTool.DoWorkAnim/StartAnim`,
  `MountObject.Action`, `ToolsManager.Use`.
- *Why ghosts, not the real part:* the real part already has the server's state; animating it would need the
  animated game paths (side effects above) or would leave a half-moved real part if the effect is cut short.
- *Alternative rejected:* delay the authoritative apply until the animation ends — breaks row 14's digests and
  every scenario's "equal within N s" check, and a lost animation would delay state.

### D2. What drives each visual (existing traffic first)

| Visual | Trigger on the receiver | New bytes |
|---|---|---|
| Part off (`PartScript`, `CarPart`) | live `CarPartsChange` from another player, record `Unmounted` false → true | none |
| Part on | live `CarPartsChange`, `Unmounted` true → false | none |
| Door/hood/trunk swing | live `CarPartsChange`, `CarPart.Switched` changed | none |
| Bolts turning | `CarPartClaimUpdate` to another player (start), `PlayerActivity.Progress` (pace), the commit or release (end) | activity only |
| Avatar pose, tool prop | `PlayerActivity` (and `Activity` in the roster) | `PlayerActivity` |
| Car tool at the car | row 5b `ToolAction` as today; the actor's avatar pose comes from its `PlayerActivity` | activity only |
| Driving | `CarDriveStart/State/Stop` | part 2 |

Row 1 gets one event, `PartChanges.RemoteChangeApplying(loader, body, sub)`, raised in `OnRemoteChange` just before
`Apply` and only there: snapshot batches (`CarPartsSync.ApplySnapshot`), `OnResult` restores (rejected own change),
resync and test holds never raise it, so they never animate. The actor is `PartClaims.OwnerOf(loader, key)` at that
moment, or the last owner of that key within 2 s (the release may arrive first; the spike checks the order).

### D3. Parts moving off and on

- **Off:** in `RemoteChangeApplying`, for each record that unmounts a mounted part, clone the part (its renderers and
  the renderers of `unmountWith` members unmounting in the same change) into a ghost at the part's world pose. After
  the apply (the real part is now hidden by `HideBySavegame`), move the ghost along the part's unmount direction
  (`GetUnmountDir()`, else `unmountDirection` in the part's frame, around `customPivotForUnmount` and spinning when
  `unmountSpinning`) by 0.35 m over 0.6 s and dissolve it (the ghost's own material copies, `_AlphaDissolve` 1 → 0,
  the property `TweenAlphaDissolve` tweens, driven per frame so `vfx-hold` can freeze it; a ghost whose materials lack
  the property shrinks instead); then destroy it. Body panels (`CarPart`) use the panel's handle mesh and a straight
  0.4 m pull away from the car centre.
- **On:** after the apply, clone the now-mounted part, set `forceRenderingOff` on the real renderers, fly the ghost
  in from the same offset over 0.5 s with the dissolve going 0 → 1, then restore the real renderers and destroy the
  ghost. `PartApplier`'s own 0.5 s `ShowMounted` replica still runs; it only touches `enabled`.
- **Door, hood, trunk:** a ghost swing over 0.6 s from the panel's pose before the instant switch to its pose after
  it, about the hinge axis those two poses imply (no hinge data needed), and `forceRenderingOff` on the real panel.
  The spike (1.1) ruled out the animated `SwitchCarPart(part, false, switched)`: it keeps `InProgress` set for a
  second, and a second change inside it leaves `Switched` inverted (Spike results). `PartApplier` keeps the instant
  call.
- Limits: at most 16 ghosts at a time per client and 4 per car; over the limit, or farther than 40 m from the camera,
  or with `RemoteVisuals` off, the change applies without a visual (counted as `skipped` with the reason). No
  `isVisible` culling (always false headless).
- *Why the dissolve:* it is the game's own look for parts leaving a car (`UnMountByGroup`), and it hides that the ghost
  only approximates the hand animation.

### D4. Bolts turning

When a `CarPartClaimUpdate` gives a `PartScript` key to another player, the receiver resolves the part
(`PartRegistry.Sub(key)`), takes its `MountObjects`, sets `forceRenderingOff` on each bolt and shows a ghost bolt that
spins about its axis and moves from its mounted to its unmounted position (`posUnMount`, `childPosUnMount`;
`CalcUnmountPos` is not called on the real bolt) for an unmount, reverse for a mount (direction from the actor's
`PlayerActivity.Kind`, default unmount if the part is mounted). The actor's progress `p` is the mean bolt progress, so
bolt i of n shows progress `clamp(p * n - i, 0, 1)`, drawn with the game's own `SetPosition` formula (Spike results);
between progress updates the shown `p` follows the rate of the last two updates and never passes the reported value
by more than one 1/16 step. The commit (`CarPartsChange` for that key) finishes the remaining bolts in 0.2 s and
hands over to D3; a release without a commit (undo) runs the bolts back in 0.3 s. All bolt ghosts are destroyed and
real bolts restored at the end, so the real bolts show exactly what `PartApplier` set.

Body panels (`CarPart`) have no `MountObjects`; their claim only drives the avatar pose.

Late join: a claim seen in a snapshot (`SyncTracker.InSnapshot`) starts no bolt animation.

### D5. Activity: what the actor publishes

`PlayerActivityState { Kind, CarLoaderID, PartKey, ToolType, ModTool, Progress }` (Core):

- `ActivityKind`: `None, Unmount, Mount, BodyPanel, Examine, Fluid, CarTool, Machine, Interior`.
- `ActivityCapture` polls at 4 Hz while the garage is ready: own claims (`PartClaims.Held` where the owner is the local
  id → `Unmount`/`Mount` by game mode `PartUnMount`/`PartMount`/`GroupUnMount`/`GroupMount`, `PartKey`, `Progress` =
  mean progress of the claimed part's `MountObjects` (`1 - GetMountState()` for an unmount, `GetMountState()` for a
  mount), quantized to 1/16);
  `ToolsManager.ToolIsActive` + `currentUsedTool` (`Examine`, `Fluid`, with the car from
  `GameScript.GetIOMouseOverCarLoader2()`); `CarToolActions.Started/Finished` (`CarTool`, `ModTool`, loader); row 5a's
  `ToolSync` machine in use (`Machine`, `ModTool`); game mode `Interior`/`InteriorAssemble`/`InteriorDisassemble`.
- It sends `PlayerActivityPacket` (reliable) only when the state changes, at most 4 per second (a change inside the
  250 ms window is coalesced into the next send), and `None` when the work ends, the player travels or seats.
- **Server stores:** `PlayerPresenceRecord.Activity` (latest value, `[OptionalField]`), cleared on scene change and
  leave, never saved. **Server relays:** the packet with `PlayerId` overwritten to clients in the same scene that
  show avatars (the movement rule). **Server decides:** nothing about game state; it drops a packet whose loader is
  not in `CarPartsStore` (sets `CarLoaderID = -1`).
- *Why in the presence record:* it is "what is happening now", like seat and engine, so the `players` snapshot gives
  a late joiner the current pose and prop for free; one-shot events are not stored anywhere.
- *Alternative rejected:* per-bolt events — 4–8 packets per part for something progress at 4 Hz already paces.

### D6. Remote avatar: work pose and tool prop

- `WorkPose` (on `PlayerInstance`, runs after the existing pitch code in `LateUpdate`): the target point is the
  part's world position (`PartRegistry`), the car tool's car centre, the machine's position, or the car under the
  examine tool; the avatar yaws toward it over 0.3 s when its speed is below 0.2 m/s; the right `UpperArm`/`ForeArm`/
  `Hand` bones (found by name like the spine) aim at the target with an elbow bend; during `Unmount`/`Mount` the hand
  oscillates ±15° at 2 Hz (wrench motion) while progress changed within the last second; targets higher than the head
  raise both arms (car on a lift). Bones that are not found leave the pose to yaw only, logged once.
- `ToolProps`: for `Examine`/`Fluid` the receiver clones its own `ToolsManager` tool GameObject for that `ToolType`
  (renderers only, layer set to the default world layer so the main camera draws it), scales it to hand size from its
  bounds and parents it to the right hand bone; for `Unmount`/`Mount` a wrench mesh if the spike finds one loaded
  (else no prop); car tools and machines have no hand prop (the effect is at the car or machine). Props are cached per
  `ToolType` and destroyed with the avatar.
- *Why procedural:* the bundle has no work clips and changing it needs the Unity project behind it (open question 3);
  bone aiming reuses what `PlayerInstance` already does for the pitch.

### D7. Scene, late join and return

- Activity, bolts and ghosts exist only while the receiver's `ClientScene.IsGarageReady` and the actor's roster scene
  equals the local scene (`GarageBound`'s drop rule, never its queue: a visual that would be queued is dropped).
- Snapshot packets never start visuals; live packets before `IsInitialSyncFinished` are dropped for visuals (the
  state apply still runs).
- A late joiner or returning player gets `Activity` in the roster (D5) and shows the pose and prop at once; it plays
  no ghost and no bolt animation for anything that happened before. Because activity is relayed per scene, the server
  sends a player who enters a scene one `PlayerActivity` per player already there (idle included), so a returning
  player never keeps an activity it missed the end of.
- On `PresenceManager.Remove`, a car delete (`CarSpawnDelete`, row 1's `ClearLoader` on the client) or a car snapshot
  for a loader, all visuals of that player or loader are cancelled with their renderers restored.

### D8. Bandwidth budget

| Stream | Rate | Estimate (BinaryFormatter) | Worst case per client with 4 busy players |
|---|---|---|---|
| `PlayerActivity` | on change, ≤ 4/s | ≈ 0.4–0.6 kB per packet | up 2.4 kB/s, down 7.2 kB/s |
| `CarDriveState` | 15 Hz while driving | ≈ 0.25 kB (one 36-byte packed `byte[]`) | up 3.8 kB/s, down 11.3 kB/s |
| `CarDriveStart` | once per drive | ≈ 30 kB (`NewCarData` blob, test track only) | burst, within the 1 MB/s peak |

Worst case ≈ 19 kB/s down and 6 kB/s up on top of today's ≈ 2.7 kB/s (≈ 21 kB/s down in all): inside the 50/20
kB/s budget with margin. The client also caps `PlayerActivity` at 4/s and `CarDriveState` at 15/s hard (excess dropped and counted),
so a bug cannot flood the server. Task 7.3 measures real bytes per type with the server's `perf top`; a value over
twice the estimate goes to QUESTIONS.md before merge.

### D9. Driving: authority, stream, interpolation (part 2)

- The driver's client simulates (vanilla VPP physics); nobody else simulates that car. `DriveCapture` reads the
  driven car's root and rigidbody (`BaseCarPhysics.rigidBody` on the track, the garage car root for `car_drive`),
  `VehicleController` steer and wheel speed, `res.engineCurrentRPM`, gear, brake and light flags, and sends
  `CarDriveStatePacket { DriveId, Seq, Payload }` at 15 Hz unreliable. `DriveStateCodec` packs time (float),
  position (3 floats), rotation (smallest-three, 3 × int16 + index), velocity (3 × int16, cm/s), steer (int8), wheel
  angular speed (int16, 0.01 rad/s), rpm (uint16), gear (int8), flags (byte: brake, lights, engine, reverse): 36
  bytes. Steer is the front wheels' angle in 0.5° steps; the sender's `Time.time` is the state time.
- `CarDriveStartPacket { DriveId, Scene, CarLoaderID, CarToLoad, CarBlob }` (reliable) when driving starts;
  `CarLoaderID` is the garage loader for `car_drive` (observers use their own copy, `CarBlob` empty) and the source
  loader for the test track (`CarBlob` = `NewCarDataCodec` blob of the track car). `CarDriveStopPacket { DriveId,
  FinalPose }` (reliable) when it ends.
- **Server stores** (`ActiveDrives`, under `StateLock`, never saved): per driver the start packet and the latest
  state; cleared on stop, on the driver's scene change and on leave (then it sends `CarDriveStop` itself). **Server
  relays** start/state/stop to clients in the driver's scene (`ShowsAvatars` rule; state unreliable). **Server
  decides:** a start is accepted only in the test track scene and only when the driver's presence scene is that scene
  (garage driving does not exist, spike 8.1); a start for a car that is away with another player is dropped. A player
  whose presence scene becomes a drive scene gets each running drive there (start and latest state, no history).
- Observers (`RemoteCars` + `DriveInterpolator`): a buffer of states rendered 100 ms behind the newest, Hermite between
  two states using their velocities; extrapolation up to 250 ms when packets stop, then hold; snap when the error is
  over 5 m; wheels spin from the wheel speed and the front wheels steer; `RemoteEngines` plays the engine from the
  stream's rpm (presence `Engine*` fields stay row 6's garage-only signal).
- *Why unreliable at 15 Hz:* the stream is overwritten 15 times a second; a resent old pose is worse than a missing
  one. Movement already uses the same channel.
- *Alternative rejected:* deterministic replay of the driver's inputs — VPP is not deterministic across clients and
  frame rates.

### D10. The observer's car (part 2)

- **Test track:** the observer builds the car once from `CarBlob` on an extra `CarLoader` cloned from the track's own
  loader (spike 8.2), loaded with `CarLoader.LoadCarFromFile(NewCarData)`, then made inert like `OpponentCarPhysics`
  does for a drag opponent: rigidbody kinematic, colliders disabled, `PartScript`/interactive objects disabled, no
  `PrepareCarPhysics`. Fallback if a second car cannot be loaded in the track scene: the base model of `CarToLoad` with
  default config and the blob's paint, logged as `ghost=base`.
- **Garage area: no-go (spike 8.1).** `car_drive` only opens the map (`WindowManager.Show(Map)`); the game has no
  free driving in the garage scene, so there is no garage car to move, no `CarAwayKind.Driving` and no
  `GarageDriveHooks`. Group 11 is parked; QUESTIONS.md row 17 asks whether to allow `car_drive` as a map shortcut.
- The copy is renamed `RemoteCar[<player>]`, because the game finds a track's car by its loader's object name, and it
  is cloned under an inactive parent so that no copied script runs `Awake`; the scripts other than `CarLoader` are
  removed before it is activated.
- Collisions: observer cars have colliders off, so the local player and the local car pass through them (open
  question 1).

### D11. Guard (part 2)

`Pie:car_drive` and `Mode:CarDrive` change owner from "row 6 part 2" to "row 17" (labels only). Spike 8.1 found no
garage driving, so both stay `Planned`: `Mode:CarDrive` stays log-only in `GuardHooks` for the test track (only the
track managers set it), and `car_drive` (the map shortcut) waits for the user's answer in QUESTIONS.md.

### D12. Harness

- Dump section `visuals`: `enabled`, `ghostsActive` (kind, loader, key, phase), counters `ghostsStarted`,
  `ghostsFinished`, `ghostsSkipped` (per reason), `boltsActive` (loader/key → owner, bolts shown done, total),
  `leaks` (count of D1 leak lines), `activitySent`, `activityDropped`, and per roster player `activity` (kind, loader,
  key, tool, progress), `pose` (`Idle`, `Reach`, `Wrench`, `ArmsUp`), `facing` (degrees to the target), `propActive`,
  `propTool`. Section `remoteCars` (part 2): per drive `playerId`, `mode` (`garage`, `ghost`, `ghost=base`), position,
  speed, `bufferMs`, `extrapolatedMs`, `snaps`, `kinematic`, `collidersOff`, `statesReceived`.
- Verbs (new, unique): `vfx-trace on|off|report` (spike: logs claim/commit/release order and timings, bolt
  `mountState` per frame for the claimed part, `ToolsManager` state), `vfx-unscrew <loader> <key> [mount]` (runs the
  real `ActionUnMount`/`ActionMount` path to its end, bolt by bolt, like a player), `vfx-tool <ToolType|none>
  [loader]` (the real `ToolsManager.Use` path; `none` hides it), `vfx-hold on|off` (freezes every running visual at
  its midpoint for screenshots; restores on `off`), `vfx-enable on|off` (the preference); `vfx-unscrew` also takes
  `pause <fraction>`, `resume` and `undo` (the game's `UndoUnMounting`/`UndoMounting`). Part 2: `drive-trace
  on|off|report`, `drive-start <loader>` (garage `car_drive`, or the track car when on the track), `drive-input
  <throttle> <steer> <seconds>` (feeds VPP input), `drive-stop`, `drive-codec-check` (encode/decode round trip).
- Visual effects are checked through state the harness can read (counters and `*Active` flags rather than pixels),
  plus screenshots in `# needs: graphics` scenarios with `vfx-hold on`.

## Spike results

Static half of group 1 (2026-10-07, `docs/spikes/remote-visuals.md`; decompile `native\work\out_rvf1`). The runtime
halves are still open and marked there.

- **Door, hood, trunk (1.1/1.2): ghost swing.** The animated `SwitchCarPart(part, false, switched)` has no inventory,
  money, XP or mode calls, but it keeps `InProgress` set for 1 s, and `SwitchCarPart(part, bool, bool)` writes
  `Switched = !switched` before its coroutine checks `InProgress` (its own and its `ConnectedParts`'). A second change
  inside that second would leave `Switched` inverted, so a visual could change state. `PartApplier` keeps the instant
  call; D3's ghost swing applies. No game run is needed to choose the route.
- **Dissolve (1.1):** `TweenAlphaDissolve` is safe on a ghost (it only tweens `_AlphaDissolve` on the ghost's instanced
  materials; 0 = gone). Materials without the property do not fade, so a ghost whose materials lack it shrinks out
  instead (dump `fade`: `dissolve` or `shrink`); which one parts use when mounted needs a game run.
- **Bolts (1.1/1.3):** a bolt's pose is a pure function of `mountState` (D4 replays `SetPosition` on the ghost:
  `Lerp(oldPos, posUnMount, 1 - m)`, spin `(1 - m) * 360 * length * 30` degrees about local X, child shown at
  `m >= 0.9`). Bolt speed is `|sin(10 t)| * max(0.7, fast_mount * 0.05 / colliderSize)` per second, about 0.7–2.2 s per
  bolt. D4/D5 change accordingly: `Progress` is the mean bolt progress of the claimed part, and the receiver paces
  bolts from that mean and the rate between packets, so the bolt speed is not needed on the receiver. The measured time
  per bolt and the claim/commit/release order still need task 1.3's run.
- **Unmount direction (1.1):** `GetUnmountDir()` only refreshes the derived `unmountVector` (world space) and is called
  on the receiver; the game's mount animation starts at `position + unmountVector * vol * 0.15`.
- **Avatar (1.4, static):** Mixamo bones `mixamorig:RightArm`, `RightForeArm`, `RightHand` (left the same), bones point
  along local +Y; no work clips. No wrench or ratchet mesh exists by name in the game data (only sounds), so part work
  has no prop unless the runtime check finds one. Both are confirmed by `vfx-trace report` in a game run.

Runtime half (task 1.3/1.4, 2026-10-07, lane 1, headless; runs `20261007-181727_L1_visual-probe`,
`20261007-182206_L1_visual-parts`):

- **Time per bolt:** about 0.9 s (exhaust manifold `v8_kolektor_wydechowy_stary_1`, 8 bolts in 7.1 s; valve cover
  `v8_pokrywa_glowicy_stara_1`, 5 of 10 bolts in 4.6 s; fan `wentylator_2`, 4 bolts). The actor sends 16 progress
  steps per part, two per second, well under the 4/s cap.
- **Packet order:** on the receiver the release (`CarPartClaimUpdate`, owner -1) arrives **before** the
  `CarPartsChange` that commits the part, in the same frame. `BoltEffect`'s 0.5 s release grace covers it (the commit
  finishes the bolts instead of running them back).
- **Game modes:** `ActionUnMount` → `GameScript.SelectToUnMount` → `SetCurrentMode(PartUnMount)`, which sets
  `GameMode.mountUnMountMode`; `MountObject.Update` resets a bolt's `canBeUnmount` while that flag is off. After the
  part comes off the mode is `PartSelect`. The examine tools leave the mode at `Garage`. D5's mapping holds.
- **Commit point:** `PartScript.Update` hides the part (`Hide()`, stat `stat_unscrew`) when the sum of its bolts'
  `mountState` reaches 0, and shows it mounted (`ShowMounted()`) when it reaches the bolt count in mount mode.
  `PartScript.Update` also refreshes `canBeUnmount = blockedNo == 0`.
- **Disabled parts on test games:** in the headless test games the probed `PartScript`s and their renderers are
  disabled (`enabled = false`; most likely `PartScriptCuller`, not proven), so their `Update` never runs: `canBeUnmount` stays false and nothing commits.
  `vfx-unscrew` therefore sets `canBeUnmount` for an unblocked culled part and starts `Hide()`/`ShowMounted()` itself
  when the bolts are done, as `PartScript.Update` would. Ghosts ignore `Renderer.enabled` for the same reason.
- **Dissolve:** a mounted part's materials have no `_AlphaDissolve`; Off/On ghosts use the shrink fallback
  (`fade = shrink`).
- **Rig and props (1.4):** the bones are as in the static half (`mixamorig:RightArm` …); no loaded mesh is named
  wrench, ratchet or spanner (no part-work prop). `ToolsManager.CurrentUsedTool` stays null while the OBD scanner is
  in use; the prop comes from `ToolsManager.ObdScanner` as designed.
- A fast mount (`part-fast-mount`) leaves the actor's bolts at `mountState` 0 on a mounted part; a following
  `vfx-unscrew` then reports full progress at once. Scenarios use parts nobody has fast-mounted for the bolt steps.

Part 2 static half (2026-10-07, `docs/spikes/remote-visuals.md` "Part 2"):

- **8.1 garage driving: no-go.** `<GetOnClick>b__72_52` = `WindowManager.Show(Map)` + `CloseAnim()`; no mode, no
  scene, no car move. Only the track managers set `GameMode.CarDrive`. D10/D11 updated; group 11 parked.
- **8.3 state sources:** `BaseCarPhysics.rigidBody`/`carModel`/`VehicleController`/`res`; `wheelState[]`
  (`steerAngle`, `angularVelocity`); `data` bus (`GearboxGear`, `EngineWorking`, input `Brake`); `LightsOn`. D9's field
  list holds; the packed state is 36 bytes. Which transform carries the visible car is read in the runtime probe.
- **8.2 route:** clone of the track's `CarLoader` (inactive parent, renamed), `LoadCarFromFile(NewCarData)`; cost
  measured by `drive-probe`.

## Measurements

Filled by tasks 1.3 (bolt timing, packet order), 7.3 (bytes per type, frame time) and 12.2 (driving bytes).

## Risks / Trade-offs

- [Cloned renderers cost frame time on four-player sessions] → caps in D3, ghosts are short-lived; row 11's `perf`
  frame time is compared with and without visuals in task 7.3.
- [A ghost of a part with skinned or shared meshes looks wrong] → clone `sharedMesh` and `sharedMaterials` only; parts
  whose renderers are not `MeshRenderer` are skipped (counted).
- [`forceRenderingOff` left on by a crash in an effect] → every effect restores in `finally`, and `VisualScope`
  restores every recorded renderer on `Reset`, car delete and snapshot.
- [The actor's game mode does not tell mount from unmount in time] → the commit decides direction for D3 anyway; bolts
  fall back to "unmount if mounted".
- [IL2CPP: `ToolsManager` private fields not exposed, or `GetUnmountDir` inlined] → fields are read through the
  Unhollower properties; D3 falls back to `unmountDirection`.
- [Headless test games run no rendering] → checks use counters; screenshots only in `# needs: graphics`.
- [Driving: loading a second car on the test track may not be possible] → `ghost=base` fallback (D10).
- [Garage driving changes placement the server does not model] → refused instead (D10, open question 6).
- [Driving makes the change too big] → part 2 can be split into `remote-visual-feedback-2` by the ROADMAP rule; part 1
  merges on its own with the driving guard entries still `Planned`.

## Migration Plan

`PlayerActivity`, `CarDriveStart`, `CarDriveState`, `CarDriveStop` are appended to `PacketTypes`; `Activity` is an
`[OptionalField]` and `CarAwayKind.Driving` is appended, so existing values do not change. Client and server must run
the same build (version and protocol-hash check). Nothing is persisted. Rollback means reverting the change; with
`RemoteVisuals` off a client behaves as before for visuals.

## Open questions

1. **Collisions between players' cars and with walking players** — default: none (observer cars have colliders off).
2. **"Test drive visible to others"** — default: visible to players who are at the test track too; nobody in the
   garage watches a test drive (row 13's away label stays).
3. **Work animations** — default: procedural pose (D6). Real clips need the Unity project of `playermodel.bundle`; is
   it available?
4. **Remote visuals on by default** with a local switch — default yes.
5. **Driving in this change or a follow-up** — default: this change, part 2 (groups 8–12), merged separately; split
   into `remote-visual-feedback-2` if part 1 is merged and part 2 has not started.
6. **Garage driving that ends off a place** — default: the car returns to its place at the end (only row 2's places
   are shared); free parking positions would be a row 2 extension.
