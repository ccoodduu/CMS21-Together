# Tasks

Prerequisites (all merged): rows 2, 6, 19 part 3 (D17), 21.

## 1. Spike

- [ ] 1.1 With A seated in a car on lifter 1 (harness `sit`), read on B: the seat handles of B's copy, the `CarFrame`
      rotation and A's record; raise and lower the lift (`lift`) and log the handle path per frame. Answers: does the
      handle move with the lift in `Update` or `LateUpdate`, and can a player sit in a car on a raised lift at all.
      Done when the answers are in design.md D3.

## 2. Client

- [ ] 2.1 `Logic/Player/SeatPoses.cs` with D1; `RideAlong.SeatPose`/`TryGetAvatarSeat` use it. Verify: `ride-along`
      still passes (avatars in the seats, `toSeat` unchanged).
- [ ] 2.2 `PresenceManager.ReconcileAvatar`: D2 (no hiding while seated in the garage; pose when the car is Ready;
      hidden while it is not). Verify: B's dump shows A's avatar active and within 5 cm of the seat after `sit`.
- [ ] 2.3 `SeatedAvatars.LateUpdate` (D3) and the retry; standing up (D4). Verify with 3.1.

## 3. Harness and proof

- [ ] 3.1 Dump `players[]`: `seatPose { seated, side, toSeat }` (`toSeat` = distance from the avatar to the expected
      pose on this client, -1 when not seated). Scenario `seat-avatars` (lane 1 or 2, two clients), each step on B's
      dump unless noted:
      1. A spawns a car on lifter 1, both Ready; A `sit 0 left` → A's avatar active, `seated`, `side` left,
         `toSeat` ≤ 0.05 m;
      2. A raises the lift (`lift 0 up`) and B samples 20 dumps while it moves → every `toSeat` ≤ 0.10 m; at the top
         ≤ 0.05 m;
      3. A `stand`, then `sit 0 right` → right side, ≤ 0.05 m;
      4. A `stand` → avatar active, not seated, within 1.5 m of A's own position from A's dump;
      5. A `sit 0 left`, B disconnects and joins again → after `syncAcked` and the car Ready, A's avatar is seated
         (≤ 0.05 m) within 10 s;
      6. B also sits (right seat) → on A's dump B's avatar is seated right and on B's dump A's is seated left;
      7. A's car is moved to another place by B while A sits → A leaves the seat (row 6), and A's avatar on B is never
         farther than 2 m from the car or A between the move and A's next movement.
      Fails on the old code at step 1 (avatar hidden). Verify: `Run-Session.ps1 -Scenario seat-avatars` passes, and
      `Run-All -Changed` (areas `presence`, `driving`, smoke) passes.
- [ ] 3.2 Hand check (docs/playtest.md): a seated friend on three cars (one right-hand drive), on a raised lift, and
      the engine running.

## 4. Docs

- [ ] 4.1 QUESTIONS.md: replace the accepted default "A seated player's avatar is hidden instead of posed" with this
      change; ROADMAP row 29 status; STATUS entry with the run ids.
