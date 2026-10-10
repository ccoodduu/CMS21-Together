# Tasks

Prerequisites: 27a `shared-race-tracks` merged; rows 17 part 2 and 21 (merged). 27b `track-races` for the start rule
(D3), which lands with whichever of 27b and 27c merges second.

## 1. Spike (S ≈ 1, go/no-go)

- [x] 1.1 Behind a harness switch, add D2's box collider to the remote copy on two lanes. Answer: (1) do VPP's wheel
      raycasts and suspension ignore the layer (name the local car's body layer and the free layer); (2) do the race
      track's checkpoint triggers ignore it; (3) overlap at spawn and after a snap or restart, with the overlap guard;
      (4) position error and contact at 30, 80 and 150 km/h, with and without collider extrapolation. Apply D1's go
      criteria and record the result in design.md. On no-go, stop and tell the user before any further task.
      **Done 2026-10-10: GO** (design.md "Spike results"; scenario `collide-spike`; runs `20261010-094840`, `-100150`,
      `-101506`, `-111733` on lane 1, one lane because both instances of a lane are the two players):
      1. The body collider is on layer 0, the wheels on layer 8, and the box is on free layer 3. A box under a wheel
         leaves the wheel's ground hit unchanged.
      2. Checkpoints count only `Player`-tagged colliders, and a box moved through one leaves the count unchanged.
      3. Overlap: off in 10 of 10 spawns on the car spot, with no frame over 118 ms. Snap and restart:
         `snap` → `overlap`, with no lift.
      4. Contact at 30 km/h 10 of 10 times, and also at 80 and 150 km/h. Lag is 0.19–0.22 s, so the error is
         4.5 m at 77 km/h. No extrapolation.

## 2. Client and server

- [x] 2.1 `RemoteCollider` (D2), layer matrix, `RemoteCars` create/remove, `DriveInterpolator` snap event. The snap is
      read from the existing `DriveInterpolator.Snaps` counter every physics step, not an event. After the spike, the
      local car's depenetration speed is limited to 1.5 m/s while it touches a box (D2).
- [x] 2.2 Enable rules (D3), including ride-along and the race start.
- [x] 2.3 Settings (D4): client `TrackCollisions`, server config key `track_collisions`,
      `ServerInfoPacket.TrackCollisionsOff` (inverted, so a missing field means on).
- [x] 2.4 Harness `remote-collider`; dump `remoteCars[].collider`. Also `collide-set`, `collide-ghost*`,
      `collide-cruise`, `collide-hold`, `collide-trace`, `collide-probe`, `collide-wheels`, `collide-box`,
      `collide-race`, `collide-clock`.

## 3. Proof

- [x] 3.1 Scenario `track-collide` (two clients on the test track, `guard-allow Mode:CarDrive`):
      1. B parks its car on the track (`drive-input` zero throttle); A drives into B's position at a fixed low
         throttle → with the setting on, A's speed drops or A deflects, and A's `remote-collider` contact count > 0;
      2. A sets `track_collisions` off and repeats → A passes through, contact count unchanged;
      3. A returns and drives again while B's copy appears on the spawn → A's wheels never stall (the row 17 freeze
         regression; speed rises under throttle within 2 s);
      4. B sits in A's car and A drives to the test track → no collider on the copy carrying B (`remote-collider` shows
         disabled with reason `passenger`).
      Low speed and a stationary target keep the check independent of stream timing. Old-code failure: step 1, A passes
      through (no collider). Verify: `Run-Session.ps1 -Scenario track-collide` passes; `drive-track`, `ride-along`,
      `race-track`, `race-start` (if merged) and the smoke set pass (`Run-All -Changed`, area `driving`).
      **Done 2026-10-10.** Roles as built: Ann arrives first and Bob arrives on her car spot (step 3, done first, so
      each sees the other appear on their own car). Ann parks ahead, held still; Bob drives at her car and stops one car
      length behind, then with his setting off drives through. Then the ride (step 4).
      - Fails on the old code (main `216d857`, `20261010-113338_L1_track-collide`): Bob drives through Ann's car,
        37.5 m with her car at 24.0 m.
      - Passes: `20261010-112757_L1` alone, and in the batch `20261010-113802_regression.json`, 14/14: the smoke set,
        `drive-track`, `drive-latejoin`, `race-track`, `race-start`, `ride-along`, `test-drive`, `connect`,
        `track-collide` and server-saves. `race-start` now also checks that both colliders are off (`race-start`)
        during the countdown and on the start spot after the green.

## 4. Docs

- [x] 4.1 docs/playtest.md: bumping a friend's car at low and high speed (phantom contacts expected at speed);
      docs/try-it.md: the setting and the host switch; ROADMAP row 27c status; STATUS entry with run ids.
