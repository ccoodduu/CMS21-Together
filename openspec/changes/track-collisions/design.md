# Design

## Context

- Row 17 part 2: `RemoteCars` builds an observer copy of another player's car in a track scene; `MakeInert` →
  `StopPhysics` makes every `Rigidbody` kinematic with `detectCollisions = false` and disables every `Collider`
  (`RemoteCars.cs`, comment at `StopPhysics`: the copy's colliders on the track's car spot stalled the driver's VPP
  wheels). `DriveCapture` sends at 15 Hz (`SendInterval`); `DriveInterpolator` shows the copy 100 ms behind (`Delay`),
  extrapolates up to 250 ms and snaps a state more than 5 m off the prediction (`SnapDistance`, counted in `Snaps`).
- Row 21: a ride-along passenger rides in the driver's copy on the passenger's client; the passenger's own track car is
  frozen and hidden at the spawn.
- 27a extends copies to the race and speed tracks; 27b starts every racer on the race track's one car spot.

## Goals / Non-Goals

**Goals:** the local car collides with other players' cars on the tracks; no wheel stall at spawn, after a snap or at a
race start; per-client and host switches.

**Non-Goals:** physics authority between clients (pushing the other car); collisions in the garage or outdoor scenes;
collisions between two remote copies.

## Decisions

### D1. Spike gate

Task 1.1 builds D2 behind a harness switch and answers the four questions of the proposal on two lanes. "Go" needs: no
wheel stall in 10 spawns with the copy on the car spot, checkpoint triggers unaffected, contact at 30 km/h against a
stationary copy in 10 of 10 runs. "No-go" ends the change with a note to the user (ghost cars stay).

### D2. Remote collider

`RemoteCollider` adds a child GameObject to the copy on a dedicated layer (an unused layer index chosen by the spike) with
a `BoxCollider` sized from the body renderers' bounds and a kinematic `Rigidbody` (`interpolation = None`,
`collisionDetectionMode = ContinuousSpeculative`). Each `FixedUpdate`, it moves with `MovePosition`/`MoveRotation` to the
copy's current interpolated pose. `Physics.IgnoreLayerCollision` lets the layer collide only with the local car's body
layer (the spike names it), never with wheel colliders, ground probes or triggers. The copy's own colliders stay off
(`MakeInert` unchanged).

### D3. Enable rules

The collider is enabled when all hold: the setting is on (client and server); the copy is not carrying the local
ride-along passenger; it does not overlap the local car (`Physics.ComputePenetration` against the local body collider);
no snap in the last 1 s (`DriveInterpolator` raises an event on a snap); no 27b race countdown is running and, after a
race start, the copy is more than 10 m from the local car once. The passenger's own frozen track car gets no change
(its colliders stay as row 21 leaves them, or are turned off if the spike shows contact).

### D4. Settings

Client: `track_collisions` in `PlayerSettings` (default on). Server: `track_collisions` in the server config (default
on), sent as `ServerInfoPacket.TrackCollisions` (`[OptionalField]`, missing = on); off forces every client's collider
off. Nothing is stored beyond the config; nothing is relayed.

### D5. Late join

A client that joins or arrives on a track builds copies as row 17 does; the collider follows D3 from its first frame
(overlap check first), so a copy appearing on the spawn never stalls the local car.

## Risks / Trade-offs

- [VPP's wheel raycasts hit the layer] → layer matrix; the spike's go criteria.
- [Phantom contacts at speed (4–7 m lag at 100 km/h)] → accepted for co-op; optional collider extrapolation (open
  question 2).
- [Checkpoint triggers react to the copy and corrupt local lap times] → triggers ignore the layer; the spike checks.
- [One-sided contacts feel unfair in a race] → the setting; 27b's start rule.

## Migration Plan

Optional `ServerInfo` field and a new config key. Rollback: revert, or set `track_collisions = off`.
