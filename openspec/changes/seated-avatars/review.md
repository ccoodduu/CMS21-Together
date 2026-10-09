# Review: seated-avatars (row 29)

**Verdict: ready after fixes** (no blockers; two gaps in the avatar plumbing).

Checked against main `3645acc`: `PresenceManager.cs` (`ApplyRecord`, `ApplyMovement`, `ReconcileAvatar`, `SeatHandle`,
`EnsureNotSeatedIn`, `CreateAvatar`), `SeatEngine.cs`, `Movement.cs`, `NameTags.cs`, `RideAlong.cs` (`CarFrame`,
`SeatPose`, `TryGetAvatarSeat`, `PlaceAvatars`), `MainMod.OnLateUpdate`, and server `PlayerHandlers` (seat arbitration)
and `Rides.cs`.

What holds:
- `ReconcileAvatar` ends with `SetActive(record.SeatCarLoaderId == NoCar)`.
- Row 21's pose is the left/right handle with `RightHandDrive`, `CarFrame` from the wheel handles, a 0.45 m drop and
  `UpdateNetworkState(..., grounded true, crouching true, false)` every `OnLateUpdate`.
- The server keeps a seat only while the player is in the garage and refuses a taken seat (D17).
- `Movement` sends nothing while `SeatEngine.IsSeated`.
- `CarPartsSync.IsReady(loader)` exists.

## Blockers

None.

## Major

**M1. The stale walking position still reaches the seated avatar through the record path.** D3 ignores "incoming
movement packets", but `PresenceManager.ApplyRecord` also calls `ApplyMovement(player.Avatar, record.LastMovement)` for
every presence record. `CaptureLocalRecord` fills `LastMovement` from the local motor even while seated. Seated players
publish records often: `SeatEngine.PollEngine` calls `PublishLocal()` whenever the RPM moves by more than 150, so up
to 4 per second with the engine running.

Each record resets the avatar's network target and velocity to the old standing spot. `LateUpdate` puts the transform
back, but `PlayerInstance` interpolation and the walk animation (velocity) jitter every frame. Fix: in both `ApplyRecord`
and `ApplyMovement(MovementPacket)`, skip `ApplyMovement` while the record is seated in the garage and posed (one
predicate, e.g. `SeatPoses.IsPosed(record)`).

**M2. There is already a seat helper, and name tags use it.** `PresenceManager.SeatHandle(record)` (left/right from
`record.SeatLeft`, requires `carToLoad`) is used by `NameTags` to place a seated player's tag at
`seat + SeatedHeadHeight`. D1 adds `SeatPoses.TryGet` next to it. Fix: build `SeatPoses` on top of `SeatHandle` or
replace it, and have `NameTags` follow the posed avatar's head. Otherwise the tag and the avatar can disagree, for
example while the car is not Ready, where the tag shows but the avatar is hidden. Add both to Impact.

## Minor

- m1. Standing up: `BeforeExitFromInterior` runs `SetSeat(NoCar)` → `Movement.ForceSend()` *before* the
  `ExitFromInterior` coroutine places the character next to the car. The forced packet can carry the pre-exit spot, and
  if A then stands still no further packet may follow. Make step 4 wait for A's first movement after `InSeatedMode`
  ends (or send one more forced packet when the poll sees the mode end). Then measure the 1.5 m.
- m2. Step 2's sampling cannot see a lag. `toSeat` compares the avatar with the pose computed on the same client at
  dump time, so it is ~0 whether or not the avatar trails the lift. 20 harness dumps also outlast the lift's travel. To
  test "follows the lift", compare against the car's seat handle sampled in `LateUpdate` after the lift tween, or log
  the per-frame offset in a ring buffer the dump returns. Keep the 0.10 m threshold.
- m3. Step 7 has no condition for the hidden case. D4 hides the avatar between the move and the next movement, so
  "never farther than 2 m" must treat "inactive" as passing. Say so, or the dump's position of an inactive avatar decides
  the result.
- m4. Ride-along interplay: `Rides.cs` starts a ride when the driver of a car with a seated garage player leaves for the
  test track. Run `ride-along` (task 2.1 already does) and check that the garage pose ends cleanly when the record's
  scene changes, so nothing is left at the garage seat.
- m5. RHD: the garage record stores the game's `SitInside(…, left)` flag and `SeatHandle` maps it straight to the left
  handle. Spike 1.1 should log one RHD car to confirm that `left` means the left handle and not "driver side".

## Size

S (≈ 1) is fair.

## Review resolution

Applied 2026-10-08. Checked on the branch: `PresenceManager.ApplyRecord` line 75 applies `record.LastMovement` to the
avatar; `SeatHandle` (line 176) is used by `NameTags` line 49; `SeatEngine.SetSeat` forces a movement packet when the
seat clears.

- **M1** Fixed. D3: `ApplyRecord` and `ApplyMovement(MovementPacket)` skip the avatar update while
  `SeatPoses.IsPosed(record)`; task 2.2 checks no jitter with the engine running.
- **M2** Fixed. D1: `SeatPoses` replaces `SeatHandle`; `NameTags` places the tag above the posed head and hides it while
  the avatar is hidden. Impact lists both.
- **m1** D4: one more forced movement packet when the poll sees the seated mode end; step 4 waits for it.
- **m2** `toSeat` against the handle sampled in `LateUpdate`, plus `maxToSeat` from a per-frame ring buffer; step 2 reads
  it over the whole lift travel.
- **m3** Step 7 counts an inactive avatar as passing.
- **m4** D2 ends the garage pose on a scene change; `ride-along` in 3.1's run checks it.
- **m5** Task 1.1 logs one RHD car for `SeatLeft`.
- Size: S (≈ 1).
