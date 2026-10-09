# Design

## Context

- Row 6 publishes the seat in the presence record: `SeatCarLoaderId` (`NoCar` when standing) and `SeatLeft`, polled
  every 0.25 s from the game mode (`SeatEngine.PollSeat`). Row 19 D17 arbitrates seats, so at most one player sits in
  each seat; the server keeps a seat only while the player is in the garage.
- `PresenceManager.ReconcileAvatar` creates or destroys the avatar per scene and today ends with
  `player.Avatar.gameObject.SetActive(record.SeatCarLoaderId == NoCar)`: seated players are hidden in the garage.
- `PresenceManager.ApplyRecord` calls `ApplyMovement(player.Avatar, record.LastMovement)` for every presence record, and
  `CaptureLocalRecord` fills `LastMovement` from the local motor even while seated. Seated players publish records
  often: `SeatEngine.PollEngine` calls `PublishLocal()` whenever the RPM moves by more than 150 (up to 4 per second with
  the engine running). `Movement` itself sends nothing while `SeatEngine.IsSeated`.
- `PresenceManager.SeatHandle(record)` (left/right handle from `record.SeatLeft`, requires `carToLoad`) exists and
  `NameTags` uses it to place a seated player's tag at `seat + SeatedHeadHeight`.
- Row 21 seats avatars on the test track: `RideAlong.SeatPose(carLoader, passenger, …)` turns "passenger" into the
  left or right handle with `OtherData.RightHandDrive`, `CarFrame` gives the car's rotation from the wheel positions,
  and `TryGetAvatarSeat` drops the avatar `SeatedAvatarDrop = 0.45 m` below the handle; `PlaceAvatars` writes the
  pose every `OnLateUpdate` with `UpdateNetworkState(position, rotation, 0, 0, grounded, crouched, false)`.
- Standing up: `BeforeExitFromInterior` runs `SetSeat(NoCar)` → `Movement.ForceSend()` before the `ExitFromInterior`
  coroutine places the character next to the car, so the forced packet can carry the pre-exit spot.
- In the garage the record says the side directly (`SeatLeft`, the game's `SitInside(…, left)` flag), not driver or
  passenger.

## Goals / Non-Goals

**Goals:** the right seat of the right car, following lifts and the car's own motion, standing up on leave, late join.

**Non-Goals:** a sitting animation; seats outside the garage other than the test track (row 21 keeps those); driving in
the garage (it does not exist in the game).

## Decisions

### D1. One seat-pose helper on top of `SeatHandle`

`SeatPoses` replaces `PresenceManager.SeatHandle`: `SeatPoses.Handle(record)` (the former `SeatHandle`),
`SeatPoses.TryGet(CarLoader car, bool leftHandle, out Vector3 position, out Quaternion rotation)` (the handle's
position, the `CarFrame` rotation, the 0.45 m drop) and `SeatPoses.IsPosed(record)` (seated in the garage, car Ready,
avatar placed). `RideAlong` keeps its driver/passenger mapping and calls `TryGet` with the handle it chose; the garage
passes `record.SeatLeft`. The constants move with it, so both scenes stay the same. `NameTags` places a posed player's
tag above the posed avatar's head; while the car is not Ready (avatar hidden) the tag is hidden too, so tag and avatar
never disagree.

### D2. Who is posed

In `ReconcileAvatar`: when `record.Scene == Garage`, `record.SeatCarLoaderId != NoCar` and the car at that loader is
Ready on this client (`CarPartsSync.IsReady`), the avatar is created at the seat pose (or moved there) and stays
active. If the car is not Ready yet (late join, the car still loading), the avatar stays hidden and a retry runs every
0.5 s from `OnLateUpdate` until the car is Ready or the seat clears (row 21's `nextAvatarRetry` pattern). When the
record's scene changes (a ride-along start takes the seated player to a track, row 21), the garage pose ends at once and
row 21 takes over; nothing stays at the garage seat.

### D3. Every frame, no stale movement

- `SeatedAvatars.LateUpdate()` (called next to `RideAlong.LateUpdate`) walks the roster's seated garage players and
  writes the pose with `UpdateNetworkState(..., crouched: true)` and `transform.SetPositionAndRotation`.
- Both `ApplyRecord` and `ApplyMovement(MovementPacket)` skip `ApplyMovement(player.Avatar, …)` while
  `SeatPoses.IsPosed(record)`, so the stale walking position in each seated record never resets the avatar's network
  target and velocity (no jitter, no walk animation). The record's `LastMovement` is still stored.
- Task 1.1 answers whether the lift's tween moves the handle in `Update` (then `LateUpdate` is late enough) or later.
  **Answer (2026-10-09):** the lift (LeanTween) moves the seat handle before `OnLateUpdate`: over a full Up travel
  the handle moved 1.678 m between the harness's `OnUpdate` and `OnLateUpdate` and 0 m after it, and the posed avatar
  stayed within 0.000 m of the seat in every frame (`20261009-002305_L1_seat-avatars`). A player can sit down in a car
  on a raised lift (step 3).

### D4. Standing up

When a record clears the seat, `ReconcileAvatar` hands the avatar back to the movement stream. Because the forced packet
of `SetSeat(NoCar)` can carry the pre-exit spot, `SeatEngine`'s poll sends one more forced movement packet when it sees
`InSeatedMode` end (after `ExitFromInterior` placed the character). A car that disappears (deleted, parked, moved to
another loader) also ends the pose: its sitters leave the seat themselves (`EnsureNotSeatedIn`), and the avatar is
hidden in between so it never floats where the car was.

### D5. Server

Nothing changes: no new state, no new packet. Late join is the `players` snapshot plus D2's retry.

## Risks / Trade-offs

- [The crouched model clips with seats, the steering wheel or doors] → hand check on a sedan, a pickup and a
  right-hand-drive car; tune the drop per `InteriorParams.SeatScale` only if it looks wrong.
- [`SeatLeft` means "driver side" rather than the left handle on a right-hand-drive car] → task 1.1 logs one RHD car.
  **Answer (2026-10-09):** on the Bolt Atlanta `sit left` puts the camera 0.88 m from the left handle (1.26 m from the
  right), `sit right` the other way round. No RHD car could be spawned: the RHD models (Sakura Tiara, CAB Roamer,
  Nissan 240Z, Jaguars, Land Rover) all need a DLC the test installs do not own. The game's own driver seat call,
  `PathTestManager.Prepare` → `SitInside(car, !RightHandDrive, …)`, shows that `left` is the physical left seat; the
  RHD hand check (3.2) confirms it in a real game.
- [Lift motion makes the avatar lag a frame] → the pose is written in `OnLateUpdate` after the lift's tween; the
  scenario reads a per-frame offset ring buffer, not single dumps.
- [A seated player who disconnects] → the roster removes the avatar (unchanged).

## Migration Plan

Client only; old and new clients differ only in what they show. Rollback: revert the change.
