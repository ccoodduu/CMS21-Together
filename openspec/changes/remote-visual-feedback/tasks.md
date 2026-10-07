# Tasks

**M7 (release 1.1).** Prerequisites (all on `main`): rows 1, 2, 5a, 5b, 6, 11, 13 and 14a. Part 1 = work visuals
(groups 1–7, M ≈ 4–5 sessions), mergeable on its own. Part 2 = driving (groups 8–12, L ≈ 6–8 sessions); it starts after
part 1 is merged, and becomes `remote-visual-feedback-2` by the ROADMAP rule if it outgrows its estimate. Runtime facts
the static decompile cannot give are spikes (groups 1 and 8); a spike that changes a decision updates design.md in the
same commit. Every scenario carries `# areas:` (new areas `visuals`, `driving`, task 3.4).

## 1. Spikes: work visuals

- [x] 1.1 **Done (2026-10-07):** `docs/spikes/remote-visuals.md` (side effects per method), design.md "Spike results"
      (D3 door route, dissolve, D4 bolt formula and speed). Static decompile (setup in `docs/spikes/native-decompile.md`) of `CarLoader.SwitchCarPart(CarPart, bool)`
      (coroutine) and `SwitchCarPart(CarPart, bool, bool)`, `PartScript.ShowMountAnimation` and its LeanTween lambda,
      `PartScript.GetUnmountDir`/`CalcUnmountDir`, `TweenHelper.TweenAlphaDissolve`, `MountObject.Update`/`Action`/
      `SetPosition` (bolt speed per second, what `mountState` runs between), `ToolsManager.Use(ToolType)` and
      `<UseAnim>` of `ObdScanner`/`FluidRefill` (which object becomes `CurrentUsedTool`, its layer and parent). Write
      `docs/spikes/remote-visuals.md` with a side-effect list per method (inventory, money, XP, mode, sound, state
      fields). Done when every method above has its side effects listed, and D3 (dissolve usable on a ghost, door swing
      route) and D4 (bolt speed) name the answer.
- [x] 1.2 **Done (2026-10-07):** static: ghost swing (the animated call keeps `InProgress` for 1 s and can leave
      `Switched` inverted), D3 updated; the route needs no game run. Door, hood and trunk route: if 1.1 shows `SwitchCarPart(part, instant: false, switched)` has no inventory,
      money, XP or mode side effects, confirm it in a game run (`vfx-trace` on, A opens and closes the hood on loader 0,
      B applies the change with the animated call: B's `stats`, `inventory` and game mode unchanged, no `[Visuals]
      state leak`); else record the ghost swing. Done when D3 states one route and the reason.
- [x] 1.3 **Done (2026-10-07):** Results in design.md "Spike results" (runtime half): ~0.9 s per bolt, the release arrives before the commit in the same frame, `PartUnMount` while unscrewing; `vfx-unscrew` drives a click like a player (it sets `canBeUnmount` and starts `Hide()` for a disabled `PartScript` on test games). `Features/VisualCommands.cs`: `vfx-trace on|off|report` (claim/change/apply order on the receiver, per-frame bolt `GetMountState()` of the claimed or unscrewed part, game mode and `ToolsManager` state on the actor; the report adds the avatar bones and wrench-like meshes), `vfx-unscrew` drives `ActionUnMount`/`ActionMount` and then `SetCanAction(true)` + `MountObject.Action()` per frame on the next unfinished bolt and reports `finished`, `part not committed` or `timeout`. Run order: this run first; it decides whether `vfx-unscrew` behaves like a click (else the scenarios that use it need a different driver). Harness verb `vfx-trace on|off|report` (logging only): on the receiver the order and times of
      `CarPartClaimUpdate`, `CarPartsChange` and the release for one part; on the actor the claimed part's
      `MountObject.GetMountState()` per frame, game mode and `ToolsManager` (`ToolIsActive`, `currentUsedTool`,
      `CurrentUsedTool` name and layer). Add `vfx-unscrew <loader> <key> [mount]` (the real
      `ActionUnMount`/`ActionMount` path driven bolt by bolt to its end). Run it on three parts (a wheel, a brake
      caliper, an exhaust part) with A and B (needs a lane). Done when design.md D4 has the measured time per bolt, the
      packet order (and whether the release can arrive before the change), and the activity mapping in D5 is confirmed
      or corrected.
- [x] 1.4 **Done (2026-10-07):** `vfx-trace report`: Mixamo bones present, no wrench/ratchet/spanner mesh loaded, `CurrentUsedTool` stays null with the OBD scanner. Static half done: bones `mixamorig:RightArm`/`RightForeArm`/`RightHand` (UnityPy on the bundle), no wrench mesh by name in the game data (Spike results); `vfx-trace report` lists `bones` and `wrenchMeshes` at runtime instead of a log line in `LoadPlayerPrefab`. Avatar rig and props: log the bone names of `model_rigged` once at `ModGameManager.LoadPlayerPrefab`; list
      the names of loaded `Mesh` assets that look like a wrench or ratchet (`Resources.FindObjectsOfTypeAll<Mesh>()`) in
      the garage. Done when D6 names the right-arm bones (or records that only yaw is possible) and the prop source for
      part work (mesh name or "none").

## 2. Core and server: activity

- [x] 2.1 **Done (2026-10-07):** Activity reaches B in every visual scenario. `ToolType`/`ModTool` are `int` (-1 = none), `Progress` 0..16; `PlayerActivityState.Clone`/`SameAs`/`QuantizeProgress`. Core `Network/Packets/VisualPackets.cs`: `ActivityKind`, `PlayerActivityState { Kind, CarLoaderID, PartKey,
      ToolType (int, game value), ModTool, Progress (byte, 1/16 steps) }`, `PlayerActivityPacket { PlayerId, State }`;
      `PlayerPresenceRecord.Activity` (`[OptionalField]`, copied by `Copy()`); append `PlayerActivity` to
      `PacketTypes`. Done when the solution builds and `PacketRouter` logs the new packet count on server start.
- [x] 2.2 **Done (2026-10-07):** Relay, roster field and the clear on travel are checked by `visual-activity` and `visual-latejoin`. Scene change clears `Activity` before the presence relay, so receivers get the cleared record; leave removes the record; the dispatch already holds `StateLock`; packets with an unknown `Kind` are dropped; idle is stored as `null`. Server `Network/Handlers/VisualHandlers.cs` (`[AllowBeforeSync]`): under `StateLock` overwrite `PlayerId`,
      set `CarLoaderID = -1` when the loader is not in `CarPartsStore`, store the state in the presence record, relay
      to clients in the same scene with `ShowsAvatars` and `SyncState` past `Connected` (the movement rule); drop
      packets beyond 8 per second per client (one warning per minute). `PlayerPresence` scene change and leave clear
      `Activity`. Done when a two-client `connect` run with A sending a test activity shows the relay log line and B's
      roster carries it, and A's travel clears it on B.

## 3. Client visual framework and harness

- [x] 3.1 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Split into `VisualScope` (effects, caps, `forceRenderingOff` refcounts restored on end/throw/cancel/reset, leak detector, counters, `Hold`), `VisualEffect`, `Ghost` (own material copies, destroyed with the ghost) and `VisualLeakHooks` (logging-only prefixes on `Inventory.Add/Delete/AddGroup/DeleteGroup` and `GameMode.SetCurrentMode`, all already patched by this mod); preference in `PlayerSettings.RemoteVisuals`; effects also end when the loader leaves `Ready` or its `SpawnSeq` changes, and on `ClientScene.LeavingScene`. `Logic/Visuals/VisualScope.cs`: ghost creation (clone `sharedMesh`/`sharedMaterials` of `MeshRenderer`s,
      no colliders or scripts, source layer), the `forceRenderingOff` record with restore in `finally`,
      `CancelFor(loader, key)`, `CancelPlayer(id)`, `CancelLoader(loader)`, `Reset()` (wired into `ClientData.Reset`,
      car delete and car snapshot), the caps of D3, the preference `CMS21Together.RemoteVisuals` (default on), and the
      D1 leak detector (a scope flag checked in `PartChangeTracker.MarkDirty`, `CarDetailsSync.MarkDirty`, the
      inventory hooks and a `GameMode.SetCurrentMode` prefix). Done when the client builds and a test effect that calls
      `MarkDirty` inside the scope logs `[Visuals] state leak MarkDirty`.
- [x] 3.2 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. (`car-live` in it). Plus `RemoteChangeApplied` after the apply (On ghosts hide the real part in the same frame); released test holds raise neither; handlers run in try/catch so a visual never stops the apply. Row 1 events: `PartChanges.RemoteChangeApplying(loader, body, sub)` raised in `OnRemoteChange` just before
      `Apply` (not in `OnResult`, snapshots or resync), and `PartClaims.ClaimChanged(loader, keys, owner, fromSnapshot)`
      raised in `OnUpdate` (`fromSnapshot` = `SyncTracker.InSnapshot`). Done when `car-live` and `car-parts` still pass
      and a `vfx-trace` run logs both events once per change and claim.
- [x] 3.3 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Verbs registered in INTEGRATION.md; helper verbs `vfx-parts`, `vfx-switch`, `vfx-stand` added; dump also has `players.<id>` (activity, pose, facing, propActive, propTool, arms), `activitySent/Dropped/Received`, `localActivity`, `unscrew`. Harness `Features/VisualCommands.cs`: dump section `visuals` (D12, plus `renderersHidden` = count of
      renderers with `forceRenderingOff` set by this change), verbs `vfx-hold on|off`, `vfx-enable on|off`,
      `vfx-tool <ToolType|none> [loader]`; `vfx-unscrew` gains `pause <fraction>` (stops the real action at that share
      of bolts and keeps the claim until `vfx-unscrew <loader> <key> resume`, or `undo`, which calls the game's
      `PartScript.UndoUnMounting`/`UndoMounting`). Register the verbs in INTEGRATION.md
      (owner row 17). Done when `dump` shows `visuals` on both clients after `connect`.
- [x] 3.4 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Areas and path rows added (checked with `Resolve-ChangedFile`), guard owner changed; `visual-parts` holds the full group 4/5 steps rather than an empty list. `TestAreas.psm1`: areas `visuals` and `driving`; path rows for `Logic/Visuals/*`,
      `Network/Handlers/Visual*`, `Network/Packets/VisualPackets.cs` (`visuals, presence`), `Logic/Driving/*`,
      `Network/Handlers/Drive*`, `Network/Packets/DrivePackets.cs` (`driving, testdrive`),
      `tools/TestHarness/Features/Visual*` and `Drive*`. `GuardRules`: owner of `Mode:CarDrive` and `Pie:car_drive`
      becomes "row 17" (still `Planned`). Create `scenarios/visual-parts.ps1` (`# areas: visuals, parts`) with the
      connect preamble, a car on loader 0 `Ready` on both, and an empty step list. Done when `Run-All -List -Changed`
      maps the new paths and `Run-Session.ps1 -Scenario visual-parts` passes.

## 4. Parts moving off and on

- [x] 4.1 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Ghosts use the shrink fade (no `_AlphaDissolve` on mounted parts). Off/On ghosts exclude the part's bolts (BoltReplay draws them), child parts and `enableOnUnmount` stand-ins; `GetUnmountDir()` falls back to the direction away from the car. `Logic/Visuals/PartGhosts.cs` for `PartScript` per D3: off (clone before the apply, move along the unmount
      direction with `TweenAlphaDissolve`), on (clone after the apply, real renderers `forceRenderingOff`, fly in),
      `unmountWith` members of the same change in one ghost, caps and distance skip. Done when the client builds.
- [x] 4.2 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Swing is a ghost (spike 1.1), about the hinge implied by the poses before and after the instant switch; `PartApplier` unchanged. Body panels (`CarPart`: pull-away ghost) and doors/hood/trunk by the route chosen in 1.2 (`PartApplier`
      gets `animate` for live changes only if the animated call is used). Done when the client builds.
- [x] 4.3 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Written in `scenarios/visual-parts.ps1` (On uses `part-fast-mount`, the hood uses `vfx-switch 0 hood`). `visual-parts` steps: A `vfx-unscrew 0 <wheel key>` → B `ghostsStarted.Off` +1, then `ghostsActive` empty and
      `renderersHidden` 0 within 3 s; with B `vfx-hold on` during the next unmount, B's `cars` equals A's while the ghost
      is held (state applied, ghost only visual), then `vfx-hold off`; A mounts it back → `ghostsStarted.On` +1, same
      checks; A opens and closes the hood → `ghostsStarted.Swing` +2 (or the animated call counted); B `resync` → no new
      ghost; B `vfx-enable off`, A unmounts → `ghostsSkipped.disabled` +1 and `cars` equal; `visuals.leaks` 0 on both and
      no `[Visuals] state leak` in either log. Done when the steps pass.

## 5. Bolts turning

- [x] 5.1 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Pacing needs no bolt speed: each bolt shows `clamp(p * n - i, 0, 1)` of the actor's mean progress, extrapolated at the observed rate by at most one 1/16 step; a release waits 0.5 s for a late commit before running back. `Logic/Visuals/BoltReplay.cs` per D4: start on `ClaimChanged` to another player (not `fromSnapshot`), ghost
      bolts paced by the actor's `Progress` and the measured bolt speed (1.3), finish on the commit (hand over to 4.1),
      run back on a release without commit, restore the real bolts at the end. Done when the client builds.
- [x] 5.2 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. In `scenarios/visual-parts.ps1`. `visual-parts` steps: A `vfx-unscrew 0 <key> pause 0.5` → B `boltsActive` lists the key with owner A and about
      half the bolts done (±1), `renderersHidden` > 0; A `resume` → B's bolts finish, the Off ghost plays, then
      `boltsActive` empty and `renderersHidden` 0; A pauses again and `vfx-unscrew 0 <key> undo` → B's bolts run back and `cars` are unchanged on both. Done when the steps pass.

## 6. Activity and the remote avatar

- [x] 6.1 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Order: own claim > car tool (`CarToolActions.LocalWork`) > machine (`ToolSync.OwnClaims`) > hand tool > interior mode; the claim leader key comes from `ClaimChanged`; `vfx-trace report` lines prefixed `activity`. `Logic/Visuals/ActivityCapture.cs` per D5 (4 Hz poll, send on change, coalesce to ≤ 4/s, hard cap, `None` on
      work end, travel and seat). Done when `vfx-trace report` on A lists the activity changes of a `vfx-unscrew` and a
      `vfx-tool OBD` run with at most 4 sends per second.
- [x] 6.2 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. `PlayerInstance.Work` (plain class, yaw before the pitch code, arms after); `PlayerId` set in `CreateAvatar`; poses `Idle`/`Reach`/`Wrench`/`ArmsUp`; with visuals off the pose stays `Idle`. `Logic/Visuals/RemoteActivity.cs` + `WorkPose` on `PlayerInstance` per D6 (target resolution, yaw, arm aim
      with the bones from 1.4, wrench oscillation, arms up for a lifted car), applied from the roster and from live
      packets, cleared on `None`, scene change and `Remove`. Done when the client builds and B's dump shows `pose` and
      `facing` for A.
- [x] 6.3 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Part work has no prop (no wrench mesh, spike 1.4 static); scale divides by the hand bone's lossy scale. `Logic/Visuals/ToolProps.cs`: prop cloned from the receiver's own `ToolsManager` tool per `ToolType` (layer to
      the world layer, scaled, parented to the hand bone), the part-work prop from 1.4 if any, cache per type, destroy
      with the avatar. Done when the client builds.
- [x] 6.4 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Written; reads `visuals.players.<A>`; the welder step moves the car to `CarLifter1` first like `tools-car-effects`. `scenarios/visual-activity.ps1` (`# areas: visuals, presence, tools`): A stands 1.5 m from loader 0 and
      `vfx-tool OBD 0` → B's roster `activity.kind` `Examine`, `propActive` true, `propTool` `OBD`, `facing` < 20°; A
      `vfx-tool none` → `None`, no prop; A `vfx-unscrew 0 <key>` → B `pose` `Wrench` while it runs, `Idle` after; A
      `tool-use Welder 0` (row 5b) → `activity.kind` `CarTool`, `ModTool` `Welder`, B `toolActionsSeen.Weld` 1; A
      `travel Junkyard` → B's roster activity `None`; `stats` and `cars` equal throughout. Done when it passes.

## 7. Late join, screenshots, budget and part-1 verification

- [x] 7.1 **Done (2026-10-07):** `visual-parts`, `visual-activity`, `visual-latejoin` pass (batch 2026-10-07, lane 1, headless) and the smoke set passes. Written; after the rejoin a paused actor shows `Reach` (D6: the wrench motion needs moving progress), so the check accepts `Wrench` or `Reach` with kind `Unmount`. `scenarios/visual-latejoin.ps1` (`# areas: visuals, presence, persistence`): A `vfx-tool OBD 0`; B connects
      → B shows A's prop and pose at once, `ghostsStarted` and `boltsActive` empty; A `vfx-tool none`, A
      `vfx-unscrew 0 <key> pause 0.5`; B disconnects and reconnects → B shows pose `Wrench` from the roster,
      `boltsActive` empty (claim came in the snapshot), no ghost; A `resume` → B plays the Off ghost once (live);
      `cars` equal. Done when it passes.
- [ ] 7.2 **In code (2026-10-07):** needs a night run with graphics (it takes the mouse). Written (`# run-all: skip` too, it is for the user's look); adds a hood-swing shot and notes the ghost `fade`. `scenarios/visual-screens.ps1` (`# needs: graphics`, `# areas: visuals`): with `vfx-hold on`, screenshots
      of an Off ghost mid-way, bolts half out, A's avatar holding the OBD scanner and reaching under a lifted car. Done
      when the run folder holds the four screenshots and STATUS lists them for the user's look.
- [ ] 7.3 **Open:** not written yet (needs lane 3 and the measured bytes). `scenarios/visual-budget.ps1` (`# run-all: lane 3`, `# areas: visuals`, 2–4 instances): for 3 minutes every
      client loops `vfx-unscrew`/mount on its own loader and `vfx-tool` on and off; server `perf top` before and after;
      checks: `PlayerActivity` upload ≤ 2.4 kB/s average per client, every client's total download ≤ 50 kB/s average,
      no `activityDropped` above the cap, `visuals.leaks` 0, dumps equal at the end; frame time p95 with visuals vs.
      `vfx-enable off` reported (`WARN` above 1.2×, graphics runs only). Add `vfx-unscrew` and `vfx-tool` rows to row
      11's soak action table. Done when it passes on lane 3 and the measured bytes per type are in design.md
      "Measurements" (a value over twice D8's estimate goes to QUESTIONS.md).
- [ ] 7.4 **Partly (2026-10-07):** the three visual scenarios and the smoke set pass on lane 1; the `parts`, `presence` and `tools` areas are not run yet; `server-saves` fails on "window close saves" also without this change (test servers start hidden, so `CloseMainWindow` finds no window). Two-instance verification: `visual-parts`, `visual-activity`, `visual-latejoin` pass with A and B, plus the
      scenarios of the `visuals`, `parts`, `presence` and `tools` areas and the smoke set (`Run-All -Changed`); record
      run ids in STATUS. Done when all are green.
- [ ] 7.5 **In code (2026-10-07):** INTEGRATION.md rows added (packet, record field, row 1 events, read-only views, preference, verbs, dump section, scenarios, areas); README "Playing together" waits for the screenshots. Docs: INTEGRATION.md (packet, record field, row 1 events, verbs, dump sections, scenarios, areas), README
      "Playing together" (what other players see, the `RemoteVisuals` switch). Done when `openspec validate
      remote-visual-feedback --strict` passes and part 1 is merged.

## 8. Spikes: driving (part 2)

- [ ] 8.1 **Static done, run open (2026-10-07):** `car_drive` = `WindowManager.Show(Map)` + `CloseAnim()`; only the track managers set `CarDrive`: **garage driving no-go**, group 11 parked, QUESTIONS.md row 17 #7 (docs/spikes/remote-visuals.md "Part 2", design D10/D11). `drive-trace`/`drive-pie` written; the `drive-probe` scenario confirms it in a game run. Static decompile of the `car_drive` pie lambda (`PieMenuController.<GetOnClick>b__72_N`), what it starts
      (scene change, free drive in the garage scene, `ParkingSpace.DriveIn/DriveOut`), which game modes it sets, where the
      car ends and whether `CarLoaderPlaces` or the lifts change; add a `drive-trace on|off|report` verb (logging only)
      and confirm in a game run with the guard on `Off` (single client is enough). Done when docs/spikes/remote-visuals.md
      has the flow, and design.md D10 says "garage driving: go" (with the end-placement rule) or "no-go" (group 11
      parked, entry stays `Planned`, recorded in QUESTIONS.md).
- [ ] 8.2 **In code (2026-10-07):** `RemoteCars` clone route (inactive parent, renamed, scripts removed) with `new` and `base` fallbacks; `drive-ghost-test clone|new|base` and the `drive-probe` scenario measure it (not run yet). Second car on the test track: in the track scene, clone the track's `CarLoader`, load a parked car's
      `NewCarData` blob with `LoadCarFromFile`, make it inert (kinematic, colliders off, `PartScript` and interactive
      objects disabled, no `PrepareCarPhysics`); measure load time and memory; try the base-model fallback. Done when
      D10 records which route works and its cost.
- [ ] 8.3 **Static done (2026-10-07):** fields in docs/spikes/remote-visuals.md 8.3, D9 holds (36 bytes); `drive-probe` reads which transform carries the car (not run yet). Drive state sources on VPP: where steer, wheel angular speed, gear, rpm, brake and lights are read on the
      driver (`VPVehicleController` data channels, `BaseCarPhysics.res`, `rigidBody`), and how the observer turns wheels
      on an inert car (wheel transforms under the car root). Done when D9's field list is confirmed.
- [ ] 8.4 **In code (2026-10-07):** the first half of `drive-track` (not run yet). Two players at the test track with two different cars at once (row 13 claims both): both drive, return and
      their results apply. Done when a scratch run shows both away claims granted and released and `cars` equal after the
      return; a failure goes to row 13's code first.

## 9. Core and server: driving

- [ ] 9.1 **In code (2026-10-07):** builds; `CarAwayKind.Driving` not added (8.1 no-go); `drive-codec-check` written (not run yet). Core `Network/Packets/DrivePackets.cs`: `CarDriveStartPacket`, `CarDriveStatePacket { DriveId, Seq, Payload }`,
      `CarDriveStopPacket`, `DriveStateCodec` (40-byte layout of D9); append the three to `PacketTypes`; append
      `CarAwayKind.Driving` if 8.1 says go. Done when the solution builds and a harness `drive-codec-check` (encode and
      decode 1000 random states, errors within the quantization) passes.
- [ ] 9.2 **In code (2026-10-07):** builds; only the test track scene is accepted (no garage drives), a start for a car away with someone else is dropped; checked by `drive-track`/`drive-latejoin` (not run yet). Server `Network/Handlers/DriveHandlers.cs` + `Data/Presence/ActiveDrives.cs` per D9: store start and latest
      state per driver, relay to the driver's scene (state unreliable), require the `Driving` away claim for a garage
      drive, send active drives to a client when its presence scene becomes that scene, send `CarDriveStop` and clear on
      the driver's scene change and leave; cap state at 20 per second per client. Done when a two-client scratch run logs
      start, relay and the stop on the driver's disconnect.

## 10. Test track driving

- [ ] 10.1 **In code (2026-10-07):** builds. `Logic/Driving/DriveCapture.cs` on the test track: start when `GameMode.CarDrive` is set by
      `TestTrackManager` (send `CarDriveStart` with the blob from `NewCarDataCodec`), stream at 15 Hz, stop on
      `ClientScene.LeavingScene`. Done when the client builds.
- [ ] 10.2 **In code (2026-10-07):** builds; positions are set in `OnLateUpdate`. `Logic/Driving/RemoteCars.cs` + `DriveInterpolator.cs` per D9/D10: build the observer car (route from 8.2),
      100 ms buffer, Hermite, 250 ms extrapolation, 5 m snap, wheels, engine sound through `RemoteEngines` from the
      stream's rpm; destroy on stop, leave and scene change. Done when the client builds.
- [ ] 10.3 **In code (2026-10-07):** verbs, dump section and `drive-track` written; the follow check compares B's shown pose with A's own path at B's render time (`drive-history`). Not run yet. Harness `Features/DriveCommands.cs`: `drive-start`, `drive-input <throttle> <steer> <seconds>`, `drive-stop`,
      dump section `remoteCars`. `scenarios/drive-track.ps1` (`# areas: driving, testdrive`): cars on loaders 0 and 1, A
      and B each take theirs to the test track; A `drive-input 0.6 0 5` then `0.4 0.5 3` → B's `remoteCars` shows A's car
      (`mode` `ghost` or `ghost=base`, `collidersOff`, `kinematic`), its position within 1.5 m of A's sampled position
      100 ms earlier, `snaps` 0; A stops → B's car within 0.3 m of A's final pose; B's own driving is unaffected (B
      `drive-input` moves B's car); A returns → B's `remoteCars` empty; both return, row 13's results equal (`cars`).
      Done when it passes.

## 11. Garage-area driving (only if 8.1 says go)

**Parked (2026-10-07):** spike 8.1 says no-go (no driving inside the garage); recorded in QUESTIONS.md row 17 #7.

- [ ] 11.1 `CarAwayKind.Driving` claim on `car_drive` (row 13's `CarAwaySync.Request` before the drive starts, the
      guard's message on refusal), released at the end and on leave. Done when a second player is refused a part edit on
      the driven car with row 13's away message.
- [ ] 11.2 `Logic/Driving/GarageDriveHooks.cs`: capture as in 10.1 for the garage car; observers move their own copy of
      that loader's car root kinematically and restore it on stop; the end placement per D10 (row 2's
      `CarPlaceChangeRequest`, or back to its place). Done when the client builds.
- [ ] 11.3 Guard: `Allow` `Pie:car_drive` and `Mode:CarDrive` (owner row 17) in the merge commit; `guard` scenario
      updated. `scenarios/drive-garage.ps1` (`# areas: driving, placement, guard`, guard on `Enforce`): A drives the car
      of loader 0 → B sees it move (`remoteCars.mode` `garage`), B cannot edit it; A stops → `placement` and `cars` equal
      on both, B's copy at the authoritative pose. Done when it passes.

## 12. Driving: late join, budget and verification

- [ ] 12.1 **In code (2026-10-07):** written (B travels mid-drive; B disconnects to the menu, reconnects and travels again; A's disconnect removes the car). Not run yet. `scenarios/drive-latejoin.ps1` (`# areas: driving, testdrive, presence`): A drives on the test track; B
      travels there mid-drive → B's car for A appears within 2 s at A's current position (no replay of the path); B
      disconnects and reconnects while A still drives (garage drive if group 11 is in) → the same. Done when it passes.
- [ ] 12.2 **In code (2026-10-07):** `drive-track` saves the server's `perf top 30` (`perf_drive.txt`) and notes the `CarDrive*` lines. Not run yet. Budget: `drive-track` reads server `perf top` for `CarDriveState` (≤ 4 kB/s per driver upload) and
      `CarDriveStart` size; numbers into design.md "Measurements" (over twice D8 → QUESTIONS.md). Done when recorded.
- [ ] 12.3 Two-instance verification: `drive-track`, `drive-latejoin` (and `drive-garage`) pass with A and B, plus the
      `driving`, `testdrive`, `placement` and `guard` areas and the smoke set; INTEGRATION.md and README updated (who sees
      a driven car, no collisions); run ids in STATUS. Done when all are green and part 2 is merged.
