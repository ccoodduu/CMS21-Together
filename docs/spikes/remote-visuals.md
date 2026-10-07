# Spike: remote visual feedback (row 17 part 1, `remote-visual-feedback` group 1)

Static half only: decompile and dump, game not launched. Date: 2026-10-07. The runtime halves (tasks 1.2 confirmation,
1.3 timing and packet order, 1.4 loaded meshes) need a game run and are listed at the end.

Outputs: `%USERPROFILE%\CMS21-TestInstalls\native\work\out_rvf1` (targets `work\targets\rvf1.txt`), plus the older
`native\out\clean\CarLoader._SwitchCarPart_d__398$$MoveNext.c` and `crane2_clean\PartScript._UnMountByGroup_d__108`.
Bone names come from `CMS21-Together-Client\Assets\playermodel.bundle` (read with UnityPy); mesh names from a string
scan of the game's `level*` and `sharedassets*.assets` files.

## Design-impacting findings (read first)

1. **Doors, hood and trunk: ghost swing, not the animated `SwitchCarPart`.** `SwitchCarPart(part, instant, switched)`
   first writes `Switched = !switched` and starts `<SwitchCarPart>d__398`. The coroutine returns **before** it toggles
   `Switched` back when any `ConnectedParts` member has `InProgress` set (both variants), and the animated variant also
   when the part itself is `InProgress`. The animated variant keeps `InProgress` true for 1 s (`WaitForSeconds(1)`), so
   a second live change in that second (or the local player's click) would leave `Switched` inverted: a state change
   by a visual. It has no inventory, money, XP or mode calls (SFX `DoorOpen/Close`, `HoodOpen/Close`,
   `TrunkOpen/Close`, `InteractiveObject.on = false` and `SetMouseOver(false)` while it runs, `OnSwitchCarPart` (only
   tutorial tasks listen), `CarLoader.isExploding = false`), but the `InProgress` window rules it out. `PartApplier`
   keeps the instant call; the receiver shows a ghost swing around the hinge and hides the real panel with
   `forceRenderingOff` while it runs.
2. **The dissolve works on any GameObject, but only on materials with `_AlphaDissolve`.** `TweenHelper.TweenAlphaDissolve(go,
   alphaTo, time, delay)` walks `go.GetComponentsInChildren<Renderer>()` (active only), takes `renderer.materials`
   (instanced copies), and for each material with `CMS.ShaderProperties.AlphaDissolve` starts
   `LeanTween.value(go, current, alphaTo, time).setDelay(delay)` writing the float back. No other side effect. The
   game's own fade (`UnMountByGroup`) is `TweenAlphaDissolve(part, 0, 0.5, delay)`: 0 = gone. Whether a mounted part's
   normal material has `_AlphaDissolve` (the game swaps shaders with `ReplaceShader` when it enters unmount mode) is a
   runtime fact; the ghost falls back to shrinking when none of its materials has the property (`fade` in the dump).
   The ghost must destroy the instanced materials when it ends.
3. **Bolt motion is a pure function of `mountState`.** `MountObject.SetPosition`: `t = 1 - mountState` (clamped);
   `localPosition = Lerp(oldPos, posUnMount, t)`; `localRotation = oldRot * Euler(angle, 0, 0)` with
   `angle = t * 360 * length * 30` degrees (`mountState` instead of `t` when `localScale.x < 0`); `child.localPosition =
   Lerp(oldPosChild, childPosUnMount, t)`, `child.localRotation = oldRotChild`, and the child's renderer is shown only
   while `mountState >= 0.9`. `Awake` computes `length` and calls `CalcUnmountPos`, so `oldPos`/`posUnMount` are valid
   on every client without calling anything. Mounted = `mountState` 1 (`SetFullMountPosition`), unscrewed = 0.
   A receiver can draw any bolt at any progress from these fields alone.
4. **Bolt speed (per frame while the button is held, `MountObject.Action`):**
   `mountState -= |sin(10 * Time.time)| * k * deltaTime` (unmount; `+=` with `reverseMode` for a mount) where
   `k = max(0.7, fast_mount * 0.05 / clamp(boxColliderSize, 0.025, 0.5))` and `fast_mount` is the upgrade value
   (`UpgradeSystem.GetUpgradeValue("fast_mount")`; `DevSettings.FastMountMode` makes `k` 5000). Average `|sin|` is
   2/pi, so one bolt takes about `1.57 / k` seconds, at most 2.2 s. The exact `fast_mount` base value and typical
   collider sizes need task 1.3. **Decision:** the actor's `Progress` is the mean bolt progress of the claimed part
   (continuous, quantized to 1/16), not the share of finished bolts, and the receiver paces each bolt from that mean
   and the observed rate of progress between packets. No bolt speed constant is needed on the receiver.
5. **`GetUnmountDir()` is visual-safe.** It calls `CalcUnmountDir()`, which only rewrites the derived cache
   `unmountVector` from `unmountDirection` (front, up, right, left, back, down, none), `customPivotForUnmount` (or the
   part's transform), its parent and `bonusMoveDirection`, and returns it (world space). The game's mount animation
   (`ShowMountAnimation`) starts the part at `position + unmountVector * vol * 0.15` and `LeanTween.move`s it home
   (ease 12), spinning around `transform.up` when `unmountSpinning`. `ShowMountAnimation` itself is not usable: it
   calls `ReplaceShader`, `Alpha1`, `SetLayerRecursively(16)` and `UnblockBlockParts` on the real part.
6. **Avatar rig:** `model_rigged` is a Mixamo rig with `mixamorig:` bones; the right arm is `mixamorig:RightShoulder` →
   `RightArm` (upper arm) → `RightForeArm` → `RightHand` (left side the same), spine `Spine`, `Spine1`, `Spine2`,
   `Neck`, `Head`. Bones point along local +Y. The bundle has 26 locomotion clips (`HumanM@Idle01`, walk, run,
   crouch) and no work clip. Arm aiming is possible (D6).
7. **No wrench mesh by name.** The only "ratchet"/"wrench"/"spanner" names in the game data are sound clips
   (`Ratchet`, `RatchetStuck`, `Airratchet`, `CMS21 Ratchet Single n`). Part work has no hand prop unless task 1.4 finds a
   loaded mesh at runtime.

## Side effects per method

| Method | Inventory | Money | XP | Game mode | Sound | State fields written |
|---|---|---|---|---|---|---|
| `CarLoader.SwitchCarPart(CarPart, bool instant, bool switched)` | no | no | no | no | instant: no; animated: open/close SFX | `Switched` (set to `!switched`, toggled back in the coroutine, lost when a connected part is `InProgress`), `InProgress` (animated, 1 s), `InteractiveObject.on`, `isExploding`, invokes `OnSwitchCarPart` |
| `CarLoader.SwitchCarPart(CarPart, bool)` (coroutine `d__398`) | no | no | no | reads only | as above | as above; LeanTween `rotateAround` of the real panel (ease 32) |
| `PartScript.ShowMountAnimation()` | no | no | no | no | no | `MountAnimationCompleted`, shader (`ReplaceShader`, `Alpha1`), layer of `unmountWith` members (16), `UnblockBlockParts`, moves the real transform |
| `PartScript.<ShowMountAnimation>b__152_0` | no | no | no | no | no | `MountAnimationCompleted = true` |
| `PartScript.GetUnmountDir()` / `CalcUnmountDir()` | no | no | no | no | no | `unmountVector` (derived cache only) |
| `TweenHelper.TweenAlphaDissolve(go, to, time, delay)` | no | no | no | no | no | `_AlphaDissolve` on `go`'s instanced materials |
| `MountObject.Update()` | no | no | no | reads `GameMode` | `BoltTakeOff`, `BoltEnd`, loop SFX stop | `mountState` (clamp), `unmounted`, `reverseMode`, `canBeUnmount`, box collider, renderers, highlighter, `Condition` |
| `MountObject.Action()` | no | no | no | no | no | `mountState` |
| `MountObject.SetPosition()` | no | no | no | no | no | the bolt's and its child's local pose, child renderer `enabled` |
| `MountObject.SetFullMountPosition()` / `SetFullUnmountPosition()` | no | no | no | no | no | `mountState` 1/0, `unmounted`, `reverseMode`, pose |
| `MountObject.PrepareToMount()` | no | no | no | no | no | `reverseMode`, `canBeUnmount`, `IsStuck`, renderers, UI description |
| `PartScript.ActionUnMount()` | via `Hide()` at the end | via `Hide()` | via `Hide()` | `GameScript.SelectToUnMount` | `PlaySFX` | `mountMode`, `oneClickUnmount`, bolts `SetCanBeUnmount`, `CameraManager.SaveZoom` |
| `PartScript.ActionMount()` | reads (`GetItems`, `GetGroup`), deletes via `ShowMounted` | via `ShowMounted` | via `ShowMounted` | `SetPartMouseOver` | `PlaySFX` | `mountMode`, `IsUnmounted`, `mountWasCanceled`, `unmountWithSeparate`; may open `ChoosePartUpWindow` |
| `PartScript.UndoUnMounting()` | no | no | no | no | no | bolts `SetFullMountPosition`, `hideWhenUnmontingMounting` renderers on, `mouseOver` |
| `PartScript.UndoMounting()` | no | no | no | no | no | bolts `SetFullUnmountPosition`, `ReplaceShader`, `mountWasCanceled`, `wasAutomatic`, `MountAnimationCompleted` |
| `ToolsManager.Use(ToolType)` | no | no | through the tool's `UseAnim` (OBD: `AddPlayerExp`) | no | through the tool | `currentUsedTool`, `ToolIsActive = true`, `OilBayonet.OilLevel/OilCondition`; OBD `UseAnim` sets `IsExamined` on parts (`PartScript.Examine`) |
| `ObdScanner.Use()` | no | no | no | no | `PlaySFX` | LeanTween `rotateAround` of the scanner (local), enables `CarToolsRenderCamera` |
| `ToolsManager.HideTool()` | no | no | no | `GameMode.EnableRaycast`, `CameraManager.ChangeCamera`, input mode | popups | `ToolIsActive = false`, body/interior show-hide, unfinished mount clean-up |

Every method above that writes state runs only on the actor (the harness verbs `vfx-unscrew` and `vfx-tool` drive the
real paths there). The receiver calls only `GetUnmountDir()` and `TweenAlphaDissolve` on its own ghosts, and reads
`MountObject`/`PartScript` fields.

## Tools in hand

`ToolsManager` keeps one instance per examine tool (`ObdScanner`, `Multimeter`, `Compression`,
`TireTreadDepthTester`, `CompoundMeter`, `FluidExtractor` as `ExamineTool`; `OilBayonet`; five `FluidRefill`s) and
`Oil_drain_h`, `MountObjectSpray` as GameObjects. `Use(tool)` stores `currentUsedTool`, calls the tool's virtual
`Use()` and sets `ToolIsActive`; the tools render through `CarToolsRenderCamera` into `cameraRawImage`.
`ToolType`: OBD 0, TestDrive 1, PathTest 2, Compression 3, Multimeter 4, TireTreadDepthTester 5, CompoundMeter 6,
OilBayonet 7, OilRefill 8, OilDrain 9, BrakeRefill 10, CoolantRefill 11, WindscreenWashRefill 12,
PowerSteeringRefill 13, MountObjectSpray 14, FluidExtractor 15. The receiver clones the renderers of its own instance
of that tool for the prop (`ToolProps`).

## Needs a game run

- 1.2: none for the route (finding 1 decides it statically); the ghost swing's hinge (the panel's `handle` pivot)
  is checked in `visual-screens`.
- 1.3: `vfx-trace` on A and B for a wheel, a brake caliper and an exhaust part: time per bolt (the `fast_mount` base
  value), whether `CarPartClaimUpdate` (release) can arrive before `CarPartsChange`, which game modes the actor shows
  during `ActionUnMount`/`ActionMount` (`PartUnMount`/`PartMount` or group modes), whether `vfx-unscrew` (driving
  `MountObject.Action()` per frame) ends in `Hide()` like a player's click.
- 1.4: `vfx-trace report` lists the avatar's bones (expected `mixamorig:RightArm`, `RightForeArm`, `RightHand`) and the
  loaded meshes whose names contain wrench, ratchet or spanner; `CurrentUsedTool`'s name and layer for OBD.
- Material check: whether a part's material has `_AlphaDissolve` while mounted (dump `visuals.ghostsActive[].fade`).

## Part 2: driving (group 8)

Static, 2026-10-07, from the existing decompiles (`native\out\testdrive_clean\PieMenuController$$GetOnClick.c`,
`cartools2_clean\PieMenuController$$_GetOnClick_b__72_52.c`) and `dump.cs`; no new Ghidra run was needed.

### 8.1 What `Pie:car_drive` does: it opens the map

- `PieMenuController.GetOnClick("car_drive")` returns the lambda `<GetOnClick>b__72_52` (`0x1812DB790`), whose whole
  body is `WindowManager.Instance.Show(WindowID.Map (12)); CloseAnim()`. No game mode, no scene change, no car moves.
- The string `car_drive` is used only by `PieMenuController..ctor`, `GetOnClick` and `GetSetDesc` (`dxref.py`).
- With the map open while seated, `MapWindow.Show` → `VerifyCarStateIfInterior` sets `GlobalData.SelectedCarLoader`
  to the seated car, and a destination is a scene trip: the test track is row 13's flow (its departure hold and claim
  work from this path as well), the race, drag, off-road and other tracks are guard backlog scenes.
- `GameMode.CarDrive` (15) is set only by the track managers (`TestTrackManager.<Prepare>d__6`,
  `TrackManager.<Restart>d__16`; `xref.py` on `GameMode$$SetCurrentMode` lists no garage caller that sets it).
  `ParkingSpace.DriveIn/DriveOut` belong to the Parking scene.
- **Garage driving: no-go.** The game has no free driving in the garage scene, so group 11 is parked: no
  `CarAwayKind.Driving`, no `GarageDriveHooks`, `Pie:car_drive` and `Mode:CarDrive` stay `Planned` (QUESTIONS.md row 17
  asks whether to allow `car_drive` as the map shortcut it is). The server accepts drives only in the test track scene.
- Runtime check (`drive-probe` scenario, `drive-pie`): A calls the lambda with the guard off; `drive-trace` and the
  before/after snapshot show `WindowManager.Show(Map)`, the game mode and every car's place unchanged.

### 8.2 A second car on the test track

- Each client's track scene has one `CarLoader` (the one `PrepareCarPhysics` drives). The drag strip shows that a
  track scene can hold a second car (`OpponentCarPhysics.LoadCar`, `EnableKinematic`).
- Route in code (`RemoteCars`): clone the track's `CarLoader` object under an inactive parent (so no `Awake` runs on
  copied scripts), drop its children and every other `MonoBehaviour`, rename it `RemoteCar[<player>]` (the game
  finds the track car by `GetSaveName()` = object name, so the copy never matches `SelectedCarLoader`), then
  `LoadCarFromFile(NewCarData)` from the driver's blob (`GameDataManager.LoadCarInGarage` of the selected loader, the
  data the track itself loads). Inert: every `Rigidbody` kinematic without collisions, every `Collider`, `PartScript`,
  `MountObject` and `InteractiveObject` disabled, `addInteractiveObject` off, no `PrepareCarPhysics`. Fallbacks:
  `new` (a fresh `GameObject` with a `CarLoader`) and `base` (`LoadCar(carToLoad)`, mode `ghost=base`).
- Cost (load time, memory) and which route works: `drive-probe` (`drive-ghost-test clone|new|base`), still to run.

### 8.3 Drive state sources (VPP)

- `BaseCarPhysics` (namespace `CMS.Tracks.CarPhysics`): `rigidBody`, `carModel`, `VehicleController`
  (`VPVehicleController`), `res` (`RealisticEngineSound.engineCurrentRPM`).
- `VehicleBase.wheelState[]` (`steerable`, `steerAngle` in degrees, `angularVelocity` in rad/s, `wheelCol.wheelTransform`).
- `VehicleBase.data` (`DataBus.Get(channel, id)`): `Channel.Vehicle` `EngineRpm` (×1000), `EngineWorking`,
  `GearboxGear`; `Channel.Input` `Steer`, `Throttle`, `Brake`, `Key`. Lights: `CarLoader.LightsOn`.
- Input for the harness: `VPStandardInput.externalThrottle/externalBrake/externalSteer/reverse` (added to the axes).
- Observer wheels: `CarLoader.GetWheelFL/FR/RL/RRHandle()`; each wheel turns about the car's axes (steer about up for
  the front pair, spin about right) from its rest pose relative to the root.
- Which transform carries the visible car on the driver (`carModel` or the loader root) is read by `drive-probe`
  (paths and positions of root, `carModel`, rigidbody and the first part); still to run.

### 8.4 Two cars away at once

Covered by `drive-track` (both claims granted at once, both results applied); still to run.
