# Design

## Context

- Row 6 publishes the seat in the presence record: `SeatCarLoaderId` (`NoCar` when standing) and `SeatLeft`, polled
  every 0.25 s from the game mode (`SeatEngine.PollSeat`). Row 19 D17 arbitrates seats, so at most one player sits in
  each seat.
- `PresenceManager.ReconcileAvatar` creates or destroys the avatar per scene and today ends with
  `player.Avatar.gameObject.SetActive(record.SeatCarLoaderId == NoCar)`: seated players are hidden in the garage.
- Row 21 seats avatars on the test track: `RideAlong.SeatPose(carLoader, passenger, …)` turns "passenger" into the
  left or right handle with `OtherData.RightHandDrive`, `CarFrame` gives the car's rotation from the wheel positions,
  and `TryGetAvatarSeat` drops the avatar `SeatedAvatarDrop = 0.45 m` below the handle; `PlaceAvatars` writes the
  pose every `OnLateUpdate` with `UpdateNetworkState(position, rotation, 0, 0, grounded, crouched, false)`.
- In the garage the record says the side directly (`SeatLeft`), not driver or passenger.

## Goals / Non-Goals

**Goals:** the right seat of the right car, following lifts and the car's own motion, standing up on leave, late join.

**Non-Goals:** a sitting animation; seats outside the garage other than the test track (row 21 keeps those); driving in
the garage (it does not exist in the game).

## Decisions

### D1. One seat-pose helper

`SeatPoses.TryGet(CarLoader car, bool leftHandle, out Vector3 position, out Quaternion rotation)`: the handle's
position, the `CarFrame` rotation, the 0.45 m drop. `RideAlong` keeps its driver/passenger mapping and calls it with the
handle it chose; the garage passes `record.SeatLeft`. The constants move with it, so both scenes stay the same.

### D2. Who is posed

In `ReconcileAvatar`: when `record.Scene == Garage`, `record.SeatCarLoaderId != NoCar` and the car at that loader is
Ready on this client (`CarPartsSync.IsReady`), the avatar is created at the seat pose (or moved there) and stays
active. If the car is not Ready yet (late join, the car still loading), the avatar stays hidden and a retry runs every
0.5 s from `OnLateUpdate` until the car is Ready or the seat clears (row 21's `nextAvatarRetry` pattern).

### D3. Every frame

`SeatedAvatars.LateUpdate()` (called next to `RideAlong.LateUpdate`) walks the roster's seated garage players and
writes the pose with `UpdateNetworkState(..., crouched: true)` and `transform.SetPositionAndRotation`, so the
interpolation of `PlayerInstance` never pulls the avatar to the last walking position. Incoming movement packets for a
seated player are ignored by the avatar while seated (they only arrive at the moment of standing up).

### D4. Standing up

When a record clears the seat, `ReconcileAvatar` hands the avatar back to the movement stream: the first movement packet
after the seat (forced by `SeatEngine.SetSeat`) places it. A car that disappears (deleted, parked, moved to another
loader) also ends the pose: its sitters leave the seat themselves (`EnsureNotSeatedIn`), and the avatar is hidden in
between so it never floats where the car was.

### D5. Server

Nothing changes: no new state, no new packet. Late join is the `players` snapshot plus D2's retry.

## Risks / Trade-offs

- [The crouched model clips with seats, the steering wheel or doors] → hand check on a sedan, a pickup and a
  right-hand-drive car; tune the drop per `InteriorParams.SeatScale` only if it looks wrong.
- [Lift motion makes the avatar lag a frame] → the pose is written in `OnLateUpdate` after the lift's tween (`Update`);
  the scenario measures `toSeat` while the lift moves.
- [A seated player who disconnects] → the roster removes the avatar (unchanged).

## Migration Plan

Client only; old and new clients differ only in what they show. Rollback: revert the change.
