# Proposal

Proposal 2026-10-08; spike, design and tasks 2026-10-10 (design.md). The spike found the numbers below out of date:
since the freeze fix of 2026-10-08 the load takes 0.2–0.5 s, and arrival → shown is 3.3–3.6 s, almost all of it the
3 s settle. The first copy in a scene visit still has one 92–120 ms frame. The user chose scope B (2026-10-10): only
option 1, a 0.5 s settle (measured ~0.7 s after arrival, frames as today); option 5 is designed and measured but not
built.

## Why

The user wants the long load fixed "at some point" when another player's car appears on a track (2026-10-08, ROADMAP
row 31: "den lange load når andre biler joiner skal fikses på et tidspunkt"). Today, on the test track, another
player's car appears about 10 s after you arrive, or about 7 s after the other player arrives when you are already
there; row 17 part 2's spec asks for 2 s. QUESTIONS.md (row 17, #8) records why, and the user accepted it "for now":

- Loading another car takes about 6.5 s (`RemoteCars.Build`: `CarLoader.LoadCarFromFile` with the driver's car blob,
  stepped one `MoveNext` per frame; steps over 100 ms are logged).
- The build waits until your own track car is loaded and then `LocalCarSettleSeconds = 3 s` more, because loading a
  second car while the game still prepared your own track car froze the game for over 10 s.
- The copy is thrown away when the drive stops or when you leave the scene (`ClientScene.LeavingScene` → `Clear`), so
  every new drive and every trip pays the full load again.

With rows 27a–27c (race and speed tracks, races, collisions) more players meet on tracks, so the wait shows up more
often, and a race (27b) cannot start fairly while one racer's copy is still loading.

## What Changes

Not decided yet. A first spike measures where the 6.5 s go (per load step, from the existing step log, on two lanes)
and whether the 3 s settle is still needed (the freeze it avoids may be a specific load step, not the whole load). Then
one or more of these options:

1. **Shorter settle.** Replace the fixed 3 s after your own car is ready with the signal the freeze actually depends on
   (for example the game's own `PrepareCarPhysics` finishing its last step), so the copy starts loading as early as is
   safe. Cheap; saves up to 3 s.
2. **Keep the copy between drives.** When a driver stops and starts again with the same car (same car blob hash) in the
   same scene visit, park the copy (`ParkingSpot`) instead of destroying it, and reuse it on the next `CarDriveStart`.
   Saves the full 6.5 s for repeated drives (a race restart, laps after a pit back to the garage do not count: a trip
   clears the scene). Costs memory per kept copy; capped at one per player.
3. **Load before you arrive.** The server knows who drives on which track (`ActiveDrives`). When you travel to a track
   where someone already drives, send their `CarDriveStart` with your arrival, so the build starts the moment option
   1's signal allows rather than after the scene's own ready step. Saves the time between arrival and the first
   relayed state.
4. **Lighter copy.** The copy is inert (no physics, no part scripts). Load only what is visible (body, wheels, the parts
   that show through glass) or show the base model at once and swap in the full blob when it is loaded. Saves most of
   the 6.5 s if the load is dominated by hidden parts; risks a copy that looks different (missing or wrong parts) for a
   few seconds.
5. **Spread the load over frames** (added by the spike). The game's `LoadCar` builds chassis, engine, exterior and
   parts in one frame (its own staged `asyncLoading` path is switched off); for the copy, run those stages one per
   frame (design D2).

Hooks and packets depend on the option: 1 none new; 2 none; 3 `CarDriveStart` sent to an arriving player (server only,
existing packet; the spike found the server already does this); 4 possibly a reduced car blob (`CarBlobVersion` bump);
5 prefixes on `CarLoader._LoadCar_d__215.MoveNext` and twelve `CarLoader` stage methods, no packet.

## Capabilities

New capability `remote-car-loading` (spec delta): the car appears within 1.5 s of arrival (1 s when already there) and
its load does not freeze the game (no frame over 0.2 s, frames as before; target 0.13 s).

## Impact

- Client: `Logic/Driving/RemoteCars.cs` (settle 0.5 s, readiness tracked every frame, timing fields).
- Server: none (option 3 is already in place).
- Harness: `waitSeconds`, `shownAt` and `readyAt` in the `remoteCars` dump, `remote-build state`; new proof
  `remote-car-timing`; `drive-latejoin` gains a time bound.
- Depends on: row 17 part 2, row 21 (the ride-along copy), 27a (copies on every track). 27c's collider must stay off
  until a preloaded or kept copy is shown and placed.

## Risks

- The freeze that the 3 s settle avoids comes back (row 17's regression). Every option keeps a regression run with the
  copy appearing while the local car loads.
- Kept copies cost memory (each car is tens of MB); cap one per player and drop them on a scene change.
- A lighter copy can show the wrong parts for a moment; only acceptable if the swap is quick.
- Two lanes on one PC load slower than one player's PC; the spike should also time a single lane.

## Size

Spike S ≈ 0.5–1 (measure the steps, test option 1). Options 1 and 3: S ≈ 1 together. Option 2: S ≈ 1. Option 4:
M ≈ 2–3. Expected total with 1–3: S–M ≈ 2–3. Built: option 1 only (scope B), S.

## Open questions

1. **Target.** Answered 2026-10-08: about 3 s is fine with no frame over 0.13 s. Scope B (2026-10-10): the delta states
   1.5 s.
2. **Which options.** Scope B: 1 only (design D1); 5 designed, not built (D2); 2, 3 and 4 not now (D4).
