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
stationary copy in 10 of 10 runs. "No-go" ends the change with a note to the user (ghost cars stay). Result: **go**
(see "Spike results").

### D2. Remote collider

`RemoteCollider` gives each copy a root GameObject `RemoteCollider[<player>]` (not a child of the copy) on layer 3, an
unnamed layer that no scene uses (spike: layer 30 is the game's "StaticObjects", so it is not used). The object has a
`BoxCollider` and a kinematic `Rigidbody` (`interpolation = None`, `collisionDetectionMode = ContinuousSpeculative`).
The box is sized once, on the first physics step after the copy's first pose sample, from the copy's enabled mesh and
skinned renderers in the frame of the streamed body, lifted 0.15 m off the ground (2.02 × 1.45 × 5.19 m for the Bolt
Atlanta). Each `FixedUpdate` it moves with `MovePosition`/`MoveRotation` to the copy's interpolated pose. A move of 5 m
or more is a teleport and counts as a jump (D3). `Physics.IgnoreLayerCollision` lets layer 3 collide only with the
layers of the local car's non-trigger, non-wheel colliders on its rigidbody (spike: layer 0 Default). The wheel colliders
are on layer 8 and ignore the box. The copy's own colliders stay off (`MakeInert` unchanged). There is no collider
extrapolation (open question 2): the box sits on the copy as it is shown. While the local car touches a box, its
`maxDepenetrationVelocity` is 1.5 m/s instead of the game's 8 m/s, and it is restored once the car no longer touches.
This was added after the spike: a copy shown late, or extrapolated past a stop, sinks into the local car, and the
push-out at 8 m/s threw cars.

### D3. Enable rules

The collider is on unless one of these holds, checked in this order. The first one that holds is the reason that
`remote-collider` shows:

1. `host`: the host turned collisions off for everyone.
2. `passenger`: the local player rides along (row 21). This covers the copy that carries them.
3. `no-local-car`: no local track car in `CarDrive`.
4. `race-start`: a 27b countdown is running, or after one, the copy has not yet been more than 10 m from the local car.
5. `snap`: the copy snapped (`DriveInterpolator.Snaps`) or teleported, or the local car jumped 5 m or more, in the last
   1 s. The race restart and the pause-menu restart are such jumps.
6. `overlap`: the box would overlap the local car (`Physics.OverlapBox` of the box plus 0.1 m against the local
   rigidbody's colliders). This is checked only while the box is off, so a running contact never turns it off.

The snap is a counter on `DriveInterpolator`, not an event: the collider reads it every physics step. A contact is
counted when the box plus 0.05 m starts to touch the local car's colliders. The passenger's own frozen track car gets
no change.

### D4. Host switch

User decision 2026-10-10: there is no per-player setting. Collisions are on for everyone, and only the host turns
them off. A per-player setting made the contacts one-sided in a bad way: a player who turned it off still pushed
the others, because on their screens his copy drove wherever his car went (spike: 5.7 m for a parked car). The host
sets `track_collisions = on|off` in the server config (default on, `true`/`false` also accepted), sent as
`ServerInfoPacket.TrackCollisionsOff` (`[OptionalField]`, so a missing field reads as false, which means on). Off turns
every client's collider off. Nothing is stored beyond the config, and nothing is relayed.

### D5. Late join

A client that joins or arrives on a track builds copies as row 17 does. The collider follows D3 from its first frame:
the first move is a teleport (`snap`, 1 s), then the overlap check runs. So a copy that appears on the spawn never
stalls the local car.

## Spike results (task 1.1, 2026-10-10): GO

Scenario `collide-spike` (lane 1, headless; output `spike_*.json` in the run folders). Runs: `20261010-094840` (all
phases, layer 30), `20261010-100150` (spawns, hits and lag from the car spot, layer 3), `20261010-101506` (rebound
comparison, with the deep-overlap guard removed), and `20261010-111733` (rebound and two-player contact with the brake
released, with and without the depenetration limit).

**Layers.** The local car's rigidbody is `VPP BluePrint` (layer 8 "Table", tag `Player`, 1699 kg, `Discrete`). Its only
enabled solid collider is `CarPhysicCollider(Clone)`, a convex `MeshCollider` on layer 0 (material `VP_Vehicle Body`,
bounciness 0.1, combine Maximum). The four `WheelCollider`s are on layer 8. Layers 3, 6 and 7 are unnamed and have no
colliders. 27 is "EMPTY3" and 30 is "StaticObjects", so both belong to the game.

**(1) Wheel raycasts ignore the layer.** A 0.8 × 0.1 × 0.8 m box on the collider layer, with its top 6 cm above the
ground, was put under the front-left wheel. The wheel still reported the track mesh `tor_arizona` and the same
suspension compression (0.5). The layer matrix (layer 3 collides with layer 0 only) keeps it away from the wheels'
layer 8.

**(2) Checkpoints ignore the copy.** The race track's 11 checkpoints (`CheckPoints`, triggers on layer 0, untagged)
count a collider only when it is tagged `Player`. This is the native `CheckPoints.OnTriggerEnter` (`CompareTag("Player")`).
A forced-on box moved into the active checkpoint left `numberOfCheckpoints` at 0, the active index at 0 and the laps at 1.
The box's layer does meet the checkpoint triggers (both use layer 0), so the tag check is what protects lap times.

**(3) Overlap, snaps and restarts.**
- Ten test copies spawned on the car spot (the car reset there each time with the pause-menu restart, 0.00 m from the
  first spot): the collider stayed off with reason `overlap` 10 of 10 times. No frame was over 118 ms (the headless
  frame is 80–110 ms anyway). The car drove away 15–21 m every time, and the collider came on once the cars were apart.
- A copy jumped onto the car: `snap` for 1 s, then `overlap`, and on once the car drove off.
- A race-track restart put the car on a copy at the start: `snap`, then `overlap`. The car was not lifted (y 0.31 m
  before and after), and it drove away 18 m.
- With the guard forced off, a copy put on the car lifted it 1.2 m onto the box's roof. There was no stall (frame
  110 ms), but this shows why the guard is needed.
- Found and fixed: a copy's box could come on at the world origin before the copy's first pose sample, then teleport
  into the car and lift it (run 1, spawn 2). The box now waits for the first sample, and any box teleport counts as a
  snap.

**(4) Contact and position error.**
- Driving at a parked test copy from 12 m (the harness holds the speed until the first contact): contact at 28 km/h
  10 of 10 times, and at 78 and 148 km/h. With the collider off, the car drove through (46 m, no contact).
- Run 2 turned the collider off inside a contact when the car was more than 0.3 m deep (a "deep overlap" guard), so
  at 150 km/h the car drove through. That guard is gone (run 3: contact and stop at 148 km/h, twice). Teleports are
  covered by the jump rules.
- Rebound. In runs 1–3 every hit seemed to throw the car back at 36–38 km/h, and a static `BoxCollider` did the same.
  A per-frame trace showed the cause: the car stops at the contact and then accelerates backwards with no contact.
  The harness's `drive-stop` leaves the brake pressed, and the game shifts into reverse when the brake is held at a
  standstill. With the inputs released (run `20261010-111733`), a hit at 30 km/h simply stops the car (−1.8 km/h,
  then standstill). Hits at 80 and 150 km/h rebound at 5 and 10–12 km/h. The static and the kinematic box still
  behave the same. A larger `contactOffset` (0.3 m) changed nothing and is not used.
- Two players (`mutual`, 8 km/h into a parked car, run `20261010-111733`). The driver stops a car length behind on his
  own client. On the parked player's client, the driver's copy arrives late and is extrapolated past its stop, so it
  sinks into the parked car. At the game's depenetration limit (8 m/s) a parked car standing free was pushed out at
  4.8 m/s, lifted 0.64 m and moved 15.9 m. With the limit at 1.5 m/s while touching a copy (now D2), the same case gave
  1.6 m/s, 0.01 m and 3.5 m. A parked car that is held moved 0.26–0.35 m either way.
- Lag of the shown copy on one PC (loopback, headless): 0.19–0.22 s (the 100 ms delay, up to 67 ms of 15 Hz stream,
  and 80–110 ms headless frames). Position error at steady speed: 1.6–1.9 m at 27 km/h and 4.5 m at 77 km/h. That makes
  about 5.5 m at 100 km/h and 8.5 m at 150 km/h, plus speed × ½ ping online. At 60 fps the frame share drops by about
  0.07 s.
- Extrapolation (open question 2) was decided by arithmetic rather than measured. Moving the box ahead by the measured
  lag would cancel the error at steady speed. Under braking or in a turn, it would put the box up to v × lag (5–8 m at
  100–150 km/h) where the car never goes. Phantom contacts at speed are accepted (proposal), so the box stays on the
  shown car.

**Real copy.** The proof scenario `track-collide` covers contact with another player's real stream (run
`20261010-112757`). Bob drives at Ann's parked car and stops one car length behind it, and Ann moves 0.3 m on her client.
The one-sided rule shows when one client lets its car through: the spike's deep-overlap guard in run 2, or Bob's
per-player setting off in an earlier `track-collide`. That car's copy then pushes the other player's car on the other
client: 94 m in run 2, and 5.7 m for Ann held still. Each client's copy of the other is a wall that moves wherever
the other car really goes. This is why the per-player setting was removed (D4).

**Verdict: GO.** The D1 criteria hold: no stall in 10 spawns on the car spot, checkpoints unaffected, and contact at
30 km/h in 10 of 10 runs.

## Risks / Trade-offs

- [VPP's wheel raycasts hit the layer] → wheels are on layer 8, which layer 3 ignores (spike 1).
- [Phantom contacts at speed (≈ 5.5 m at 100 km/h on one PC, more online)] → accepted for co-op, no extrapolation.
- [Checkpoint triggers react to the copy] → they count only `Player`-tagged colliders (spike 2).
- [A copy driving into a parked car pushes it like a wall (kinematic, infinite mass)] → the cheap form the user
  accepted. Where the other driver is stopped by your copy on their client, their copy stops at your car too.
- [One-sided contacts feel unfair in a race] → the host switch and 27b's start rule.
- [A player who could turn collisions off alone would still push the others] → no per-player setting (D4).
- [A late or extrapolated copy sinks into a parked car] → the 1.5 m/s depenetration limit while touching (D2). Kept
  for now; the user decides later (QUESTIONS.md).

## Migration Plan

Optional `ServerInfo` field and a new config key. Rollback: revert, or set `track_collisions = off`.
