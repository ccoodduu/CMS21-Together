# Design note: ride-along (ROADMAP row 21 `ride-along`)

Size S–M, no OpenSpec proposal. User idea 2026-10-08: two players in one car on the test track. Spike:
`docs/spikes/ride-along.md`. Builds on row 6 (seats), row 13 (test drive travel and away claim) and row 17 part 2
(driving stream, observer car).

## Behaviour

- **Who rides:** when the server grants a test-track away claim (row 13) to a player, every other player whose
  presence record says they sit in that car in the garage (row 6 `SeatCarLoaderId`, either seat) becomes a passenger.
  Sitting in the car before the drive starts is the consent; there is no menu. Seats are arbitrated (row 19 D17), so
  a car has at most two people: the driver and one passenger. The one who starts the drive drives, wherever they sat.
- **Passenger's trip:** the passenger's client gets `RideUpdate { Active }`, shows "Riding along with Ann.", sets
  `SelectedCarLoader` to the same car (`TestToShow = ""`, `NewMileage = 0`) and loads `Test_track_1` by row 13's path.
  It asks for no away claim (`TestDriveSync.HoldDeparture` lets passengers pass).
- **On the track:** the passenger's own track car (the game always loads one) is frozen at the start: kinematic, gas
  pedal and `VPStandardInput` off, audio off, no drive stream. When the observer copy of the driver's car is shown
  (row 17 part 2, 6–10 s), the passenger sits in it: the own car's renderers are hidden, the game camera is switched
  off (`CameraController.customCameraPosEnabled`) and the mod puts `Camera.main` at the passenger's head every frame.
  The head is the driver's head offset of the own track car (the same car), mirrored to the passenger seat. The mouse
  turns the head (±150° yaw, ±70° pitch) relative to the car. The passenger cannot drive or steer.
- **No jitter:** the copy is moved in `OnLateUpdate` (`RemoteCars.Update`, 100 ms behind the stream, Hermite). The
  camera is placed right after it in the same `OnLateUpdate`, from the seat of that frame. Nothing else moves the
  camera while the game camera is off; the harness measures it at the next frame (`maxDrift`).
- **Avatars:** players on the track send no movement (no character there). Every client on the track seats the
  riders' avatars itself: the passenger in the passenger seat and the driver in the driver seat of the car they ride
  (the driver's own track car on the driver's client, the observer copy elsewhere), crouched, 0.45 m below the seat
  handle. The driver sees the passenger in their passenger seat; the passenger sees the driver next to them.
- **End:** the server ends the ride and tells everyone (`RideUpdate { Active = false, Reason }`):
  driver leaves the track (`DriverReturned`, "Ann drove back to the garage."), driver disconnects (`DriverLeft`,
  "Ann left the game."), the driver's claim is released or the driver lands somewhere else (`DriveCancelled`),
  the passenger leaves the track or the game (`PassengerLeft`; the driver sees "Bob left the ride."), or the passenger
  has not reached the track 120 s after the start (`PassengerDidNotArrive`). A passenger whose ride ended returns to
  the garage by the track's own `ReturnToGarage` (once its own track car is loaded, at most 20 s). On the way out
  `NewMileage` is set to 0 and `TestToShow` to "", so only the driver's result counts and no examine report opens.
  The passenger arrives in the garage standing; the seat was cleared when the scene changed (row 6).
- **Passenger-side fallbacks:** no copy of the driver's car within 90 s of arriving, or the copy gone for 10 s while
  seated: the passenger returns with a message; the server ends the ride when the passenger leaves the track.
- **Leaving early:** the passenger can use the pause menu's return button; the ride ends for them only.

## Network

One packet, appended to `PacketTypes`: `RideUpdate { DriverId, PassengerId, CarLoaderID, Active, Reason }`, server to
clients only. The passenger needs no request: the server's decision comes from state it already has (seat records,
away claims, scenes). The server sends each start and end to every in-session client, and the running rides to a
client whose scene becomes the test track (for the avatars).

## Server (`Data/Presence/Rides.cs`)

Rides by passenger, never saved, under `StateLock`. Driven by `CarAwayRegistry.Granted` (new event, after the grant is
sent to the driver, so the driver gets its own answer first), `CarAwayRegistry.Released`,
`PresenceEvents.SceneChanged` and `PresenceEvents.Left`; `Rides.Tick` handles the arrival timeout. The server command
`away` lists the rides too.

## Fix in row 17 part 2

The spike found that observer cars were shown turned 180° (the stream carries the pose of the car model, which sits
turned inside the loader root that the copy moves). `RemoteCars.FindBody` now records the model's pose in the copy's
root once, and the root is placed so the model takes the streamed pose. Positions are unchanged, so `drive-track`'s
checks hold; the harness reports the model's position.

## Not done

- Riding the test path (garage scene, motion not shared; spike section "Test path: options").
- A seated pose: the avatar crouches; a visible game must judge how it looks.
- The passenger's speedometer and dashboard show the frozen own car, not the driver's.

## Tests

Scenario `ride-along` (lane 1, two clients): seats, the server's ride line, both on the track, the passenger seated with
the camera at the passenger head (≤ 5 cm) of the copy while the driver drives (camera travels > 5 m, copy within 1.5 m
of the driver's path, `maxDrift` ≤ 5 cm), the same car orientation and passenger seat on both games at rest (≤ 5°,
≤ 5 cm in the car, ≤ 0.3 m in the world), the passenger's input moving nothing and starting no drive, avatars in the
seats on both games, the return with the driver (not seated, claim released, mileage only from the driver's result,
same cars), and a second ride where the driver disconnects. Harness: `ride-state [driverId]` and the dump section
`ride`; `ride-probe` (spike data).
