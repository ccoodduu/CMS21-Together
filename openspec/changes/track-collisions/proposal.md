# Proposal

## Why

The user wants players' cars to collide on the tracks (decision 2026-10-08, ROADMAP row 27c). They had dropped
collisions "for now" earlier; they want them now. Today every observer copy of another player's car is inert: row 17
part 2's `RemoteCars.MakeInert` → `StopPhysics` makes every `Rigidbody` of the copy kinematic with
`detectCollisions = false` and disables every `Collider`, because "the last load step puts the copy on the track's car
spot; its colliders there stalled the driver's VPP wheels". So cars on the test, race and speed tracks drive through
each other. The review of row 27 (`shared-race-tracks/review.md`, "U2") split this into its own change, spike first.

## What Changes

- **Spike first (go/no-go).** One box collider (or the body's convex mesh) per remote copy, on a new GameObject on a
  dedicated layer, with a kinematic `Rigidbody` moved by `MovePosition`/`MoveRotation` in `FixedUpdate` (so PhysX
  computes contact velocities); every part, wheel and trigger collider of the copy stays off. The spike answers: do VPP's
  wheel raycasts and suspension ignore the layer; do the race track's checkpoint triggers ignore it; what happens on
  overlap at spawn and after a snap or restart; how large the position error is at 30, 80 and 150 km/h. If VPP cannot
  coexist with the box, the change stops and the user hears it: cars stay ghosts.
- **Collider on the remote copy** (after a go): built on the copy's frame, following the interpolated pose; enabled
  only once it does not overlap the local car (`Physics.ComputePenetration`), and off for 1 s after a snap. The layer
  collision matrix lets it hit only the local car's body layer.
- **Contacts are one-sided.** The copy is kinematic and never pushed: when A hits B's copy, A's car reacts; B feels
  nothing on its own client (B's copy of A hits B only where B's game sees A). This is the cheap form the user accepted;
  real physics authority between clients is not planned.
- **Host switch only** (user decision 2026-10-10, which replaced a per-player client setting): collisions are on for
  everyone. The server setting `track_collisions` (`on`/`off`, default `on`) turns them off for everyone, sent in
  `ServerInfo` (`[OptionalField]`). A per-player setting would still let the player who turned it off push the others.
- **Ride-along.** No collider on the copy that carries the local ride-along passenger, nor on the passenger's own frozen
  track car (row 21).
- **Races (27b).** No collisions during a race countdown and until the racers are 10 m apart (everyone starts on the
  one car spot), unless spike 1.1 of 27b shows room for a grid offset per participant.

Hooks: none in game code beyond row 17's `RemoteCars` build path. Packets: `ServerInfo.TrackCollisionsOff`
(`[OptionalField]`).

## Capabilities

### New Capabilities
- `track-collisions`: on the test, race and speed tracks a player's car collides with the other players' cars, can be
  turned off by the host for everyone, and never stalls the local car's wheels.

### Modified Capabilities
- None in `openspec/specs/` (row 17 part 2 is not archived; this change replaces its "observer copies have no
  colliders" rule on the tracks).

## Impact

- Core: `ServerInfoPacket.TrackCollisionsOff` (`[OptionalField]`), server config key `track_collisions`.
- Client: `Logic/Driving/RemoteCollider.cs` (new: box, layer, kinematic body, overlap guard, snap pause),
  `RemoteCars` (create and remove it; still `MakeInert` for the copy's own physics), `DriveInterpolator` (snap event),
  `RideAlong` (off for the carried copy), 27b's `TrackRaceSync` (off during the start).
- Harness: `remote-collider` (state per copy: enabled, overlap, last contact, contact count), `drive-input` (from row
  17); dump section `remoteCars` gains `collider`; scenario `track-collide`.
- Depends on: 27a `shared-race-tracks` (copies on every track); 27b `track-races` for the start rule (that part lands
  with whichever of the two merges second); rows 17 part 2, 21.

## Size and risks

- Spike S ≈ 1 (go/no-go); implementation S–M ≈ 1.5–2 (host switch, layer matrix, overlap guard, scenario). Total ≈ 2.5–3.
- The copy lags ≈ 100 ms (`DriveInterpolator.Delay`) + ½ ping + up to 67 ms (15 Hz stream), that is ≈ 4–7 m at
  100 km/h, so contacts at speed are "phantom": A bumps where B was. Extrapolating the collider (not the visual) by the
  measured lag reduces the error but overshoots under braking; the spike decides.
- Re-enabling the copy's own colliders would bring back row 17's wheel freeze; only the dedicated box is enabled.

## Open questions

Each has the default the draft works with.

1. **Collider shape.** **Default:** one box from the body's bounds. Alternative: the body's convex mesh if the spike
   shows the box feels wrong at low speed.
2. **Collider extrapolation.** **Default:** none (collider at the shown pose); the spike measures whether extrapolating
   by the lag is better.
