# Tasks

Prerequisites: 27a `shared-race-tracks` merged; rows 17 part 2 and 21 (merged). 27b `track-races` for the start rule
(D3), which lands with whichever of 27b and 27c merges second.

## 1. Spike (S ≈ 1, go/no-go)

- [ ] 1.1 Behind a harness switch, add D2's box collider to the remote copy on two lanes. Answer: (1) do VPP's wheel
      raycasts and suspension ignore the layer (name the local car's body layer and the free layer); (2) do the race
      track's checkpoint triggers ignore it; (3) overlap at spawn and after a snap or restart, with the overlap guard;
      (4) position error and contact at 30, 80 and 150 km/h, with and without collider extrapolation. Apply D1's go
      criteria and record the result in design.md. On no-go, stop and tell the user before any further task.

## 2. Client and server

- [ ] 2.1 `RemoteCollider` (D2), layer matrix, `RemoteCars` create/remove, `DriveInterpolator` snap event.
- [ ] 2.2 Enable rules (D3), including ride-along and the race start.
- [ ] 2.3 Settings (D4): client `track_collisions`, server config key, `ServerInfoPacket.TrackCollisions`.
- [ ] 2.4 Harness `remote-collider`; dump `remoteCars[].collider`.

## 3. Proof

- [ ] 3.1 Scenario `track-collide` (two clients on the test track, `guard-allow Mode:CarDrive`):
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

## 4. Docs

- [ ] 4.1 docs/playtest.md: bumping a friend's car at low and high speed (phantom contacts expected at speed);
      docs/try-it.md: the setting and the host switch; ROADMAP row 27c status; STATUS entry with run ids.
