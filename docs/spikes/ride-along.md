# Spike: ride-along (ROADMAP row 21)

Two players in one car on the test track. Method: static decompile (`native/out/ride_clean`, targets
`CameraController$$(LateUpdate|Update|Start)`, `VPCameraController$$(LateUpdateMe|UpdateMe|MonitorTargetChanges|InitializeTarget|OnEnable)`,
`VehiclePhysics.CameraAttachTo$$(Update|SetTargetConfig)`, `PrepareCarPhysics$$(CheckFall|EnableKinematic|EnableGasPedal)`,
`BaseCarPhysics$$SetupInterior`; older folders `testdrive2_clean` (`PathTestManager`), `testdrive3_clean`
(`BaseCarPhysics$$SetupHeadPosition`)) and two headless runtime probes on lane 1 with the harness verb `ride-probe`
(scenario `ride-probe`: A and B drive two cars on the track, both probe while A drives; runs
`20261008-151452_L1_ride-probe` and `20261008-151836_L1_ride-probe`, JSON files `ride_*.json`). Date: 2026-10-08.

## Short answers

1. **Seat handles work on the observer copy.** `CarLoader.GetLeftSeatHandle()`/`GetRightSeatHandle()` return the
   fields `i_seat1_h`/`i_seat2_h`. On the copy they are `RemoteCar[n]/car_boltatlanta/seatLeft|seatRight`, children
   of the root that `RemoteCars` moves, so they follow the copy without extra work. On the player's own track car they
   are `Physics/VPP BluePrint/Model/seatLeft|seatRight`, siblings of the car model, at the right world positions.
2. **The driver seat is the left one unless `OtherData.RightHandDrive`** (`PathTestManager.Prepare` calls
   `SitInside(car, !RightHandDrive, PathTest)`), and `BaseCarPhysics.SetupHeadPosition` puts the driver's head above
   it: seat + up × `SeatScale` × 0.9 × `SeatLeftHeightMod` (or `SeatRightHeightMod`), × 0.75 when the seat is on the
   `InteriorUnactive` layer. The passenger's head is the same offset above the other seat.
3. **The track camera can be taken over with one flag.** One camera: `Physics/!!Logic/Camera Controller - Standard Keys`
   (`VPCameraController` + `CameraController`) with the child `Main Camera` (`Camera`, `AudioListener`). Default mode
   `AttachTo` with the target `VPP BluePrint/DriverHeadPivot/DriverHead` (copies its position and rotation).
   `CameraController.Update` and `LateUpdate` return at once when `customCameraPosEnabled` is set, so the game stops
   moving the camera and the mod can place it.
4. **The second client reaches the track by row 13's travel path.** `GlobalData.SelectedCarLoader` = the same loader
   name, `SelectSceneToLoad("Test_track_1", TestTrack, true, true)`. `TestDriveSync.HoldDeparture` must let it pass:
   the away claim belongs to the driver and a request would be refused (`Busy`). The passenger's track loads its own
   drivable copy of the same car from its in-memory profile; it cannot be skipped and is frozen instead (below).
5. **No jitter by ordering, not by smoothing.** `RemoteCars.Update` moves the copy in MelonLoader's `OnLateUpdate`. The
   game's `CameraController.LateUpdate` has no fixed order against it, so pointing the game's `AttachTo` at the copy
   could show the copy one frame late (0.46 m at 100 km/h and 60 fps). With the game camera off (3.), the mod places
   the camera in the same `OnLateUpdate`, right after the copy's interpolation, from the copy's seat in that frame.
6. **Bug found: observer cars were shown turned 180°.** The driver streams the pose of the car model under the VPP
   blueprint (`DriveCapture.BodyOf`, `model(Clone)`, car front = −z). The copy's root `car_boltatlanta` has its front
   at +z and holds its `model(Clone)` turned 180°. Setting the root to the streamed pose turned the car around: hood at
   z −0.857 in the driver's frame and +0.857 in the copy root's frame; A's left→right seat vector (−0.478, −0.763) on
   A, (+0.478, +0.763) on B's copy at the same moment. Positions matched (row 17's checks are positional), so the
   headless tests did not see it. Fixed: the copy's `model(Clone)` takes the streamed pose (`RemoteCars.FindBody`).
   `DriveCapture`'s `Reverse` flag is inverted for the same reason; nothing reads it.
7. **The test path is not covered by this approach.** It is in the garage scene: the car moves only on the tester's
   client (row 13 runtime item 7), and a passenger is put out of the seat when the car is moved to the path
   (`CarPlacementSync.BeforeRemoteCarMove` → `EnsureNotSeatedIn`). Options below.

## Runtime probe data (lane 1, headless)

| What | Own track car (driver or passenger) | Observer copy |
|---|---|---|
| Frame the stream sends / the copy moves | `VPP BluePrint/Model/model(Clone)` | `RemoteCar[n]/car_boltatlanta` |
| Front wheels, local z | −1.666 | +1.666 |
| `seatLeft` local | (0.45, 0.075, −0.053), yaw 90 | (−0.45, 0.075, 0.053), yaw 270 |
| `seatRight` local | (−0.45, 0.075, −0.053), yaw 270 | (0.45, 0.075, 0.053), yaw 90 |
| Seat layer | 13 | 13 |
| Driver head (`DriverHeadPivot`) local | (0.446, 0.96, 0.087): 0.885 above the left seat, 0.14 m behind it | none (no `PrepareCarPhysics`) |
| `SeatScale`, height mods (Bolt Atlanta) | 0.95, 1.0, 1.0 | same car data |

- The car's front direction is taken from the wheel handles (front pair minus rear pair), on both cars, so seat and
  head poses do not depend on which frame a transform uses.
- No `CharacterMotor` exists on the track. Players there send no `Movement`, so row 6 never creates an avatar for them;
  ride-along creates the riders' avatars from the seat.
- `Camera.main` exists in the headless games; the transforms can be checked, the picture cannot.

## The passenger's own track car

`PrepareCarPhysics` loads `SelectedCarLoader` and the track managers set `GameMode.CarDrive` as for a driver. The
passenger freezes it once it is loaded and `VehicleController.initialized`:

- `EnableKinematic(true)` (`Rigidbody.isKinematic`), `EnableGasPedal(false)` (`EdysToRes.ReceiveInput`),
  `EnableAudio(false, false)`, every `VPStandardInput` disabled; repeated every 0.5 s (a track restart rebuilds it);
- left at the start: `PrepareCarPhysics.CheckFall` restarts the car below y −200, so it is not parked far away;
- renderers under the VPP blueprint hidden once the passenger sits in the copy;
- `DriveCapture` does not start (the passenger would otherwise stream a second drive of the same car);
- leaving the track: `TrackManager.ReturnToGarage` → `SaveMileage` writes `NewMileage = max(1, km)`; the passenger sets
  it to 0 and `TestToShow = ""` in `LeavingScene`, so no kilometre is added and no examine report opens.

## Test path: options (not built)

1. Stream the test path like a drive: the tester sends the car root's pose while `GameMode.PathTest` (garage scene,
   loader id, no car blob), every garage client moves its own copy of that loader's root, and a passenger seated in it
   follows by the game's own parenting (`SitInside` parents the player to the car root). Also fixes row 13 runtime
   item 7 for everyone in the garage. Needs: the drive server to accept the garage for path tests, a "path" kind in
   `CarDriveStart`, putting the car back at `PathTestManager.places[1]` with `specialState` 1 at the end (row 13 already
   syncs that), and not ejecting a seated player for a path-test move.
2. Run the path animation locally on the passenger's client (`PathTestManager.Prepare` with the passenger in the right
   seat, results discarded). Deterministic only if the path is a fixed animation; `PathTestManager.Update` was not
   read for that.
3. Leave the test path solo (today's behaviour) and say so in the README.
