# Review: shared-race-tracks (ROADMAP row 27)

Reviewed 2026-10-08 against the main code in the worktree (`change/singleplayer-features`), `il2cppdumper/dump.cs`,
`native/out/spfeat_clean`, and the spike run `tools/runs/20261008-231615_L2_sp-features-probe2`. The coordinator's
message relayed the user's decisions of 2026-10-08: a shared race start with results, and collisions between players'
cars. They are covered in the section "User decisions" below.

## Verdict: rework (scope)

The drafted part works against the code: the track set, the away claims on every track, scene identity by name,
visible driving, ride-along and lap records. With the fixes below it is ready. The user's decisions overturn open
questions 1 (ghost cars) and 2 (no shared start) and roughly triple the size, so the proposal must be revised before
implementation. Recommendation: split into **27a `shared-race-tracks`** (this draft plus fixes, M), **27b
`track-races`** (shared countdown and results, M, after 27a) and **27c `track-collisions`** (spike first, S + S–M, after
27a). 27b and 27c meet at the start grid (see U2).

## What checks out

- The track gates: `RaceTrack` and `SpeedTrack` have no DLC gate. The drag strip is the Drag Racing DLC (index 22) and
  `CustomTrack` needs Workshop items (spike section 5; `SceneHelper$$CanGoToScene.c` and `GetDLCForScene.c` are in
  `spfeat2_clean`).
- Spike run 2 confirms these claims:
  - `RaceTrack` reports `RaceTrackManager`, `CarDrive`, `laps 1`, `lastBestTime 0`;
  - the `SpeedTrack` scene reports `scene: TestTrack` with `FreeTrackManager`, and the client logged "SpeedTrack
    ready as TestTrack" and "Drive 7222 … (car -1) started on the TestTrack";
  - B's `remoteCars.statesReceived` is 0;
  - the mileage reached the car only through the 1 Hz poll ("2 km added locally (no result applied)").
- Every relevant check is test-track only today:
  - `DriveHandlers.driveScenes = { TestTrack }`;
  - `DriveCapture` (lines 70, 74, 112);
  - `CarAwayRegistry.OnSceneChanged`/`Tick`;
  - `Rides` (lines 44–86);
  - `RideAlong` (lines 165, 191–426);
  - `CarDetailsStore` line 191 (results only from a `TestTrack` owner).
- `GameScene` already has `RaceTrack = 11` and `SpeedTrack = 16`. `ClientScene.FromSceneType` maps `SceneType` by name, so
  `SceneType.SpeedTrack` → `GameScene.SpeedTrack` works once D2 uses the requested type or the scene name.
- `RaceTrackManager` has `LastTime()`, `NextCheckPoint()`, `AllCheckpointsDone()`, `timer` (`Stopwatch`),
  `lastBestTime` (long) and a light sequence in `Prepare`. `FreeTrackManager` has `topSpeed`/`lastTopSpeed`.

## Major

**M1. The `TestTrack` reference count and two places that D3–D5 do not name.** The draft cites "30 lines". There are 40
in client, core and server, plus 9 in the harness. Two of them are not covered by D3–D5:
- `RideAlong.Return` uses `FindObjectOfType<TestTrackManager>()`. On the race and speed tracks this falls back to
  loading `garage` without `TrackManager.ReturnToGarage`, so the passenger skips the game's return path.
- `DriveCapture.Start` picks the loader from the `CarAwayKind.TestTrack` claim. This is the "car -1" in spike run 2.

Fix: D5 uses `TrackManager.Instance` (the base class has `ReturnToGarage`). D4 names the loader lookup (`IsTrackKind`).
Task 1.1 records all 49 lines in design.md, as planned. Treat its classification as a gate for group 3.

**M2. The lap hook and its proof are unverified.** `RaceTrackManager.LastTime` is not in the decompiles (`spfeat_clean`
has only `Awake`, `Start` and `_Prepare_d__19`). D6 does not yet know whether `LastTime` runs per lap or per finish,
which value holds the lap time, or the type and unit of `ProfileData.BestRaceTime`. `track-lap <ms>` "calls the lap
path with a given time", so it either bypasses the hook (then the proof does not cover it) or must forge the game's
timer. Fix:
- Decompile `LastTime`, `NextCheckPoint` and `_Restart_d__20` in task 1.2, and check them with `work\at.py` (folded
  bodies).
- Make `track-lap` set the game state and call the real method: set `timer.elapsed` (the field is visible in the
  `Prepare` decompile), mark the checkpoints done (`numberOfCheckpoints` and the list), then call `LastTime()`.
- Keep a direct `TrackRecord` send only as a server-side check.

**M3. The proof's old-code failure and guard setup.** As written, step 1 fails on the old code because the guard
refuses the trip. That is the guard's reason, not the missing claim. `Mode CarDrive` is Planned (log-only), and every
driving scenario opens it with `guard-allow Mode:CarDrive` (`test-drive.ps1`, `drive-track.ps1`). Fix:
- The scenario runs with `guard-allow Mode:CarDrive`, or the row opens `Mode CarDrive` (owner row 17).
- On the old code it runs with `guard-allow Scene:RaceTrack`, so that it fails at "B's `away` shows A's claim of kind
  `RaceTrack`".
- State this in task 4.1.

**M4. The records requirement says "across sessions", but the proof has no server restart.** Step 4 only rejoins. Fix:
add a server restart after step 4 (`server-restart` helpers from row 7) and check `records`, B's `world` group record
and A's `BestRaceTime` again.

## Minor

- **m1.** D4's "> 20 m jump snaps" and the `_Restart_d__20` "restart marker" are not needed:
  `DriveInterpolator.SnapDistance = 5f` already snaps any state more than 5 m off the velocity prediction (and counts
  it in `Snaps`). Drop the hook, or keep it only if task 1.2 shows a restart that stays under 5 m.
- **m2.** Open question 5 is partly answered already: `FreeTrackManager` has `topSpeed` (float) and `lastTopSpeed` (int).
  Task 1.2 only needs to check whether these reach `ProfileData.TopSpeed`.
- **m3.** The dump also has `SpeedTrackManager : RaceTrackBase` (with `SpeedTest` and `StartLights`), but the speed
  track scene runs `FreeTrackManager` at runtime. Name `FreeTrackManager` as the manager in D1, and note that
  `SpeedTrackManager` is not used by `SpeedTrack`, so nobody hooks it by mistake.
- **m4.** A ride-along passenger on the race track runs the game's own `RaceTrackManager.Prepare` (lights, timer, mode
  15) on a frozen car. Check in step 5 or the hand check that the passenger never sends a `TrackRecord`: the hook sends
  only when `!RideAlong.IsPassenger`.
- **m5.** Step 6, `travel DragStrip`: the harness `travel` bypasses the map (which hides the button without the DLC), so
  it tests the guard label, not the game's gate. That is fine, but say so.
- **m6.** D6 ignores laps under 10 s, but a race-track lap with the pause-menu restart can produce a partial lap. Ignore
  a `LastTime` that follows a `Restart` without all checkpoints, if task 1.2 shows that can happen.
- **m7.** The lap time is sent by the driver's client, and the server trusts it within bounds. That is fine for co-op,
  but say so in D6.

## User decisions (2026-10-08): what the draft must add

### U1. Shared race start with a countdown, results recorded by the server

What to add (proposal "What Changes", a new design decision D8, a spec requirement, tasks and a scenario; or the
separate change 27b):

- **Start.** A player on the race track (with an active drive, not a passenger) chooses "Start race" (pause menu entry,
  or a hotkey from row 14a's set) with a lap count. The client sends `RaceStartRequest { Scene, Laps }`. The server's new
  `TrackRaces` (under `StateLock`) creates a race:
  - participants are the players whose presence scene is that track and who have an `ActiveDrives` entry;
  - it refuses with a D16 answer when a race already runs on that track or when the requester has no drive.
- **Countdown.** The server broadcasts `RaceCountdown { RaceId, Scene, Laps, Participants, StartInMs }` to the
  players on that track. Each client converts `StartInMs` to local time, minus half its ping. The ping is already
  measured, as `PingMs` in `MultiplayerMenuModel`. Note that `DriveInterpolator`'s clock offset is per stream and does
  not give a server clock.
  - On the race track the client starts the game's own sequence, so the green light falls on the shared start:
    `RaceTrackManager.Restart`, which teleports to the start, resets `timer`/`laps` and runs the lights in `Prepare`
    with `WaitForSeconds`. If the sequence turns out not to be deterministic, our own overlay counts down and starts
    the game's timer at zero.
- **Results.** The `LastTime` postfix (M2) sends `RaceLap { RaceId, Lap, LapMs }`. The server marks a racer finished
  after `Laps` laps and records DNF on leaving the scene, disconnecting, a pause-menu restart or a timeout (for example
  10 min). It broadcasts `RaceResult { RaceId, Order[{PlayerId, TotalMs, BestLapMs, Dnf}] }`. Every lap still feeds D6's
  personal best and group record. The server keeps the last N results in `world` (optional field).
- **Speed track.** `FreeTrackManager` has no race, laps or lights, so a "race" there needs a product rule: first to a
  distance, or the best top speed within N seconds (from `topSpeed`). Default: race only on the race track in this
  change. The speed track gets a top-speed challenge later, or the user picks a rule now. Ask.
- **Late join and restart.** A player arriving during a race watches but does not race. A server restart ends running
  races (DNF for all, noted in the log); stored results survive.
- **Proof.** Scenario `race-start` (two clients, both on the race track):
  - A starts a 1-lap race → both get `RaceCountdown` with both ids;
  - both start times on the clients are within 150 ms;
  - `track-lap` (M2's real path) on both with 90 s and 95 s → `RaceResult` with A first;
  - B leaves mid-race in a second race → DNF;
  - the old code has no `RaceCountdown` packet type, so the scenario fails at the first check.

**Risks.**
- The start timing over the network: half the ping is an estimate, so expect ±50 ms. That is fine, because lap times
  are measured locally.
- `Restart` may re-run the fade and reload the car physics, so check the timing in the spike.
- Every racer starts on the same spot (the track has one car spot), so with U2 the grid overlaps. U2 must handle this.
- Folded or inlined bodies for `LastTime`/`Restart` (M2).

**Size.** Spike S ≈ 0.5–1 (`Restart`/`Prepare` timing, `LastTime` semantics, the start area). Implementation M ≈ 3–4:
server race state, four packets, client countdown and results UI, hooks, and the `race-start` scenario. A speed-track
rule adds S ≈ 1.

### U2. Collisions between players' cars (cheapest form: kinematic colliders on the remote copies)

**What exists.** Row 17 part 2's `RemoteCars.MakeInert` → `StopPhysics` makes every `Rigidbody` of the copy kinematic
with `detectCollisions = false` and disables every `Collider`. The reason is recorded in the code: "The last load step
puts the copy on the track's car spot; its colliders there stalled the driver's VPP wheels." The copy therefore
overlapped the local car at spawn, and the VPP wheel physics hung on its colliders. The copy is shown 100 ms behind the
stream (`DriveInterpolator.Delay`), which runs at 15 Hz (`DriveCapture.SendInterval`), with up to 250 ms of extrapolation
and a snap above 5 m.

**Is it viable?** Only with a spike first. Re-enabling the copy's own colliders would bring the freeze back. A
workable cheap form looks like this:
- one box collider (or the body's convex mesh) per copy, on a new GameObject on a dedicated layer;
- a kinematic `Rigidbody` moved with `MovePosition`/`MoveRotation` in `FixedUpdate` (not by setting the transform), so
  PhysX computes contact velocities;
- all part, wheel and trigger colliders of the copy stay off.

The spike must answer:
1. With the dedicated layer, do VPP's wheel raycasts and suspension ignore the box? Set the layer collision matrix so
   the box hits only the local car's body layer, never wheel colliders or ground probes.
2. Do the race track's checkpoint triggers ignore the layer? Otherwise a copy passing a checkpoint calls the local
   `NextCheckPoint` and corrupts lap times.
3. What happens on overlap at spawn or after a snap or restart? Enable the box only once it does not overlap the local
   car (`Physics.ComputePenetration`), and disable it for 1 s after a snap.
4. How large is the position error at speed? The copy lags ≈ 100 ms + ½ ping + up to 67 ms, that is ≈ 4–7 m at
   100 km/h. Contacts are therefore one-sided and "phantom": A bumps where B was, and B feels nothing because the copy
   is not pushed. Extrapolating the collider (not the visual) by the measured lag reduces the error, but overshoots
   under braking. The spike should measure contact at 30, 80 and 150 km/h on two lanes.
5. Ride-along: the passenger rides in the driver's copy, and its own frozen, hidden track car sits at the spawn. Keep
   colliders off on the copy that carries the local passenger, and on the passenger's own frozen car.

**What to add after the spike.**
- A client setting (`MelonPreferences`, default on, per user decision), plus an optional server setting that forces
  collisions off, sent in `ServerInfo`. One client turning it off is fine, because the copy is never pushed.
- U1 interplay: no collisions during the countdown and until the racers are 10 m apart (everyone starts on one spot),
  or a lateral grid offset per participant index if the spike shows room on the start straight.
- Proof scenario `track-collide`:
  - B parks its car on the track with `drive-input` at zero throttle;
  - A drives into B's position at a fixed low throttle;
  - with the setting on, A's car stops or deflects (speed drop and a contact counter in the `remoteCars` dump);
  - with it off, A passes through;
  - a third run checks that A's wheels never stall when B's copy appears at the spawn (the row 17 freeze regression).
  - Use low speed and a stationary target, so the check does not depend on stream timing.

**Size.** Spike S ≈ 1 (a go/no-go). Implementation S–M ≈ 1.5–2 including the setting, the layer matrix, the overlap guard
and the scenario. If the spike shows VPP cannot coexist with the copy's box, the fallback is "no collisions, ghost cars"
as drafted, and the user should hear that before 27c starts.

### Size with the additions

| Part | Size |
|---|---|
| 27a (this draft + M1–M4) | M ≈ 4–5 (the ROADMAP's 3–4 does not count the 49-line classification or the restart proof) |
| 27b races | M ≈ 4–5 incl. spike |
| 27c collisions | S + S–M ≈ 2.5–3 |
| Together | L ≈ 10–13, beyond the M of one change, hence the split |

## Risks of the track generalisation

- Missing one of the 49 `TestTrack` checks leaves a track without a claim release or a relay. Mitigation: task 1.1's
  list goes into design.md, and the review of group 3 checks each line. Run `test-drive`, `drive-track`, `ride-along`
  and `test-drive-latejoin` unchanged (already in task 4.1).
- `CarAwayRegistry`'s non-test-track branches release a claim as soon as the owner leaves the garage
  (`from == Garage`) and run a 15-minute watchdog. A new kind that is not routed through `IsTrackKind` everywhere
  loses its claim the moment the driver leaves. Make the server check (`CarLocksCheck` style) cover each of the three
  kinds.
- The speed track's game-side `CurrentSceneType == TestTrack` may also steer game code (pause menu, `TestToShow`). D2
  keeps our identity separate; the hand check 5.1 should include the speed track's pause menu and return.

## Review resolution

Applied 2026-10-08. The split is done as recommended (user decision of the same day): **27a** `shared-race-tracks`
(this change), **27b** `track-races` (shared start, results, DNF, race track only) and **27c** `track-collisions`
(spike first, kinematic box on the remote copy, client setting default on, off on a copy carrying a ride-along
passenger), each with its own proposal, design, tasks and spec. Reference counts checked on the branch: 40 `TestTrack`
lines in client, core and server plus 9 in the harness; `RideAlong.cs:278` `FindObjectOfType<TestTrackManager>`,
`DriveCapture.cs:112` loader from the `TestTrack` claim, `DriveInterpolator.SnapDistance = 5f`.

- **M1** Fixed. D1 lists the 49 references per file and makes task 1.1's table a gate for group 3; D4 names the
  `DriveCapture` loader lookup (`IsTrackKind`); D5 returns through `TrackManager.Instance`.
- **M2** Fixed. Task 1.2 decompiles `LastTime`, `NextCheckPoint`, `_Restart_d__20` and checks them with `work\at.py`;
  `track-lap` sets `timer`/checkpoints and calls the real `LastTime`; `track-record-send` is a server-side check only
  (D6).
- **M3** Fixed. The scenario runs with `guard-allow Mode:CarDrive`; the old-code run uses `guard-allow
  Scene:RaceTrack` and fails at B's `away` claim check (4.1).
- **M4** Fixed. Step 4 adds a server restart and re-checks `records`, the group record and `BestRaceTime`; the spec
  says "across sessions and server restarts".
- **m1** Restart hook and 20 m snap dropped (D4). **m2** open question 3 names `topSpeed`/`lastTopSpeed`. **m3**
  `FreeTrackManager` named, `SpeedTrackManager` noted as unused (proposal, D1). **m4** passenger never sends (D5, D6,
  step 5, spec scenario). **m5** step 6 says it checks the guard label. **m6** partial lap after a restart ignored (D6).
  **m7** trusted lap time stated (D6).
- Track risks: `AwayCheck` self-check per kind (D3); the speed track's pause menu and return in hand check 5.1.
- **U1** → 27b `track-races` (race track only; speed-track rule left as its open question 2). **U2** → 27c
  `track-collisions` (spike go/no-go; on no-go the user is told before implementation).
- Sizes: 27a M ≈ 4–5, 27b M ≈ 4–5 incl. spike, 27c S + S–M ≈ 2.5–3.
