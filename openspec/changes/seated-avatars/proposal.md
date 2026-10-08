# Proposal

## Why

The user wants a player who sits in a car in the garage to be shown sitting there (2026-10-08, ROADMAP row 29). Today
`PresenceManager.ReconcileAvatar` hides the avatar while `record.SeatCarLoaderId != NoCar` (accepted default of
2026-10-06: "A seated player's avatar is hidden instead of posed"). The other players see the person vanish next to
the car and the car start by itself.

Row 21 (ride-along, merged) already seats avatars on the test track: `RideAlong.TryGetAvatarSeat` finds the seat
handle of the car (`CarLoader.GetLeftSeatHandle`/`GetRightSeatHandle`), the avatar is created there
(`PresenceManager.CreateAvatar(record, seat, rotation)`), crouched, 0.45 m below the handle, and `PlaceAvatars` moves it
with the car every frame. The garage has everything else already: the seat record (`SeatCarLoaderId`, `SeatLeft`,
row 6), seat arbitration (`SeatRefused`, row 19 D17), and the same car on every client at the same loader.

## What Changes

- **Seated pose in the garage.** A remote player whose presence record names a seat in a car that is loaded and Ready
  on this client is shown in that seat: driver seat or passenger seat as recorded, crouched like row 21's riders, with
  the car's frame as rotation. The avatar is no longer hidden.
- **Follows the car.** The pose is recomputed in `OnLateUpdate` from the seat handle, so a lift going up or down
  (row 2) and the car rocking keep the avatar in the seat. A car that is moved to another place or parked forces its
  sitters out today (`PresenceManager.EnsureNotSeatedIn`); their next record has no seat and the avatar stands up where
  the player's next movement puts it.
- **Leaving the seat.** When the record's seat clears, the avatar goes back to the movement stream at once. The leaving
  client already forces a movement packet (`SeatEngine.SetSeat`), but before the game has placed the character next to
  the car, so it sends one more when its poll sees the seated mode end.
- **Late join.** A joiner gets the players' records in the `players` snapshot (row 6) before the cars are Ready; the
  seat pose waits for the car and is applied when it becomes Ready (retry like row 21's `PlaceAvatars`).
- **One helper for both scenes.** `RideAlong.TryGetAvatarSeat`, the name tags (`NameTags`, today on
  `PresenceManager.SeatHandle`) and the new garage case share one helper
  (`SeatPoses`, built from `SeatHandle`), so the test track, the garage and the name tag use the same seat; the tag
  follows the posed avatar's head.
- **No stale walking position.** A seated player's presence records still carry the last walking position, and
  `ApplyRecord` applies it to the avatar (up to 4 records per second with the engine running). While a player is posed,
  neither `ApplyRecord` nor `ApplyMovement` moves the avatar.
- **No new packet and no server change.** All inputs exist in the presence record.

Hooks: none new (presence reconcile and the existing `OnLateUpdate`). Packets: none.

## Capabilities

### New Capabilities
- `seated-avatars`: other players see a seated player in the right seat of the right car in the garage, following the
  car, standing up when the player leaves the seat, and after a late join.

### Modified Capabilities
- None in `openspec/specs/` (the seat requirement of row 6 is not archived; this change replaces its "hidden while
  seated" behaviour, see design D1).

## Impact

- Client: `Logic/Player/PresenceManager.cs` (no hiding while seated, seat pose on create and reconcile, no movement
  apply while posed, `SeatHandle` moves to `SeatPoses`), `Logic/Player/SeatPoses.cs` (new; the handle lookup and the pose
  math from `RideAlong.SeatPose`), `Logic/Player/NameTags.cs` (tag above the posed head), `Logic/Player/SeatEngine.cs`
  (one more forced movement packet when the seated mode ends), `Logic/Driving/RideAlong.cs` (uses `SeatPoses`), the
  `OnLateUpdate` placement.
- Harness: the dump's `players[]` gains `seatPose` (`seated`, `side`, `toSeat` and `maxToSeat` in metres, from a
  per-frame ring buffer), verb `seat-pose-reset`; scenario `seat-avatars`.
- Depends on (merged): row 6 (seat records), row 2 (lifts), row 19 D17 (seat arbitration), row 21 (seat pose code).

## Open questions

Each has the default the draft works with.

1. **Pose.** The avatar model has no sitting animation; row 21 crouches it 0.45 m below the seat handle. **Default:**
   the same crouched pose; a visible game run judges it (hand check). A real sitting pose would need a new animation in
   the avatar bundle (not planned).
2. **Head direction.** The seated player's camera pitch is in the movement stream, but movement is not sent while
   seated. **Default:** the avatar looks straight ahead with the car.
3. **Engine sound and lights.** Unchanged (row 6 already plays a remote engine).
4. **Car interior hidden.** The game hides some interior parts when the local player is not inside. **Default:**
   accept that the avatar may clip with a seat or door in some cars; hand check on three cars.
