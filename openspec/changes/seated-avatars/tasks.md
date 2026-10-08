# Tasks

Prerequisites (all merged): rows 2, 6, 19 part 3 (D17), 21.

## 1. Spike

- [ ] 1.1 With A seated in a car on lifter 1 (harness `sit`), read on B: the seat handles of B's copy, the `CarFrame`
      rotation and A's record; raise and lower the lift (`lift`) and log the handle path per frame. Answers: does the
      handle move with the lift in `Update` or `LateUpdate`, and can a player sit in a car on a raised lift at all. Also
      sit on the left in one right-hand-drive car and log which handle `SeatLeft = true` names (left handle or driver
      side). Done when the answers are in design.md D3 and D1.

## 2. Client

- [ ] 2.1 `Logic/Player/SeatPoses.cs` with D1, replacing `PresenceManager.SeatHandle`; `RideAlong.SeatPose`/
      `TryGetAvatarSeat` and `NameTags` use it. Verify: `ride-along` still passes (avatars in the seats, `toSeat`
      unchanged).
- [ ] 2.2 `PresenceManager.ReconcileAvatar`: D2 (no hiding while seated in the garage; pose when the car is Ready;
      hidden while it is not; pose ends on a scene change). `ApplyRecord` and `ApplyMovement(MovementPacket)` skip the
      avatar update while posed (D3). Verify: B's dump shows A's avatar active and within 5 cm of the seat after `sit`,
      with A's engine running for 10 s (no jitter: `toSeat` max ≤ 0.05 m in the ring buffer).
- [ ] 2.3 `SeatedAvatars.LateUpdate` (D3) and the retry; standing up with the extra forced movement packet (D4).
      Verify with 3.1.

## 3. Harness and proof

- [ ] 3.1 Dump `players[]`: `seatPose { seated, side, toSeat, maxToSeat }` (`toSeat` = distance from the avatar to the
      seat handle pose sampled in `LateUpdate` after the lift tween, -1 when not seated; `maxToSeat` = the largest
      per-frame offset in a ring buffer of the last 120 frames, reset by `seat-pose-reset`). Scenario `seat-avatars`
      (lane 1 or 2, two clients), each step on B's dump unless noted:
      1. A spawns a car on lifter 1, both Ready; A `sit 0 left` and starts the engine → A's avatar active, `seated`,
         `side` left, `toSeat` ≤ 0.05 m, and after 3 s `maxToSeat` ≤ 0.05 m;
      2. B `seat-pose-reset`; A raises the lift (`lift 0 up`) and waits for the top → `maxToSeat` ≤ 0.10 m over the
         whole travel; at the top `toSeat` ≤ 0.05 m;
      3. A `stand`, then `sit 0 right` → right side, ≤ 0.05 m;
      4. A `stand`; wait for A's first movement packet after `InSeatedMode` ends → avatar active, not seated, within
         1.5 m of A's own position from A's dump;
      5. A `sit 0 left`, B disconnects and joins again → after `syncAcked` and the car Ready, A's avatar is seated
         (≤ 0.05 m) within 10 s;
      6. B also sits (right seat) → on A's dump B's avatar is seated right and on B's dump A's is seated left;
      7. A's car is moved to another place by B while A sits → A leaves the seat (row 6), and between the move and A's
         next movement A's avatar on B is either inactive or never farther than 2 m from the car or A (inactive passes).
      Fails on the old code at step 1 (avatar hidden). Verify: `Run-Session.ps1 -Scenario seat-avatars` passes, and
      `Run-All -Changed` (areas `presence`, `driving`, smoke) passes, including `ride-along`, where B's garage pose
      ends cleanly when the ride starts (nothing left at the garage seat).
- [ ] 3.2 Hand check (docs/playtest.md): a seated friend on three cars (one right-hand drive), on a raised lift, and
      the engine running.

## 4. Docs

- [ ] 4.1 QUESTIONS.md: replace the accepted default "A seated player's avatar is hidden instead of posed" with this
      change; ROADMAP row 29 status; STATUS entry with the run ids.
