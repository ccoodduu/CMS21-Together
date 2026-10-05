# Review: sync-players-and-scenes (2026-10-05)

Checked against the decompiled stubs, the code on `main`, ROADMAP (incl. the updated milestones, row 13 and the
integration notes), the user's decisions in QUESTIONS.md and the other six drafts.

## Findings

### Blocker
1. **The idle late-join fix relied on one UDP packet.** The newcomer's position reached existing players only via a
   single forced `MovementPacket`, which the server relays unreliably on DirectIP and whose scene filter could drop it
   before the `Garage` presence arrived. *Changed:* `PlayerPresence` carries the transform (`LastMovement`) on the
   reliable channel and is published on scene-ready (D3, D9). The spec now requires this transform to arrive reliably.
2. **The roster bypassed row 7's contract.** It was sent straight from `OnAskForSync`. *Changed, following the
   updated contract (coordinator input):* it is now `PlayersSnapshotProvider` in snapshot slot `players`
   (`SyncOrder` 500, after `self` 450 = own position). `SendSnapshot` returns 1 item, and the client calls
   `SyncTracker.Applied("players")`. There is no interim direct send, because the contract lands first. Row 7
   persists name, scene and position/rotation from the presence record; seat and engine are not saved and are
   cleared on leave (D9, task 1.7).
3. **Car purchases did not use the parking API from the integration notes.** The draft used its own `CarPurchase`
   packet. *Changed:* purchases now send row 2's `CarParkRequest` with `CarLoaderID = -1`, plus `Price` and
   `RequestId`, and the server answers with `CarPurchaseResult` (D8, tasks 6.2–6.3). The vanilla-chosen local
   parking slot is cleared so the client does not hold a duplicate.
4. **Returning to the garage would wipe test-drive results** (coordinator input, ROADMAP row 13). *Changed:* chose
   "send before leaving". The scene prefix raises `ClientScene.LeavingScene(from, to)` while the old scene is still
   loaded and before `Loading` is sent. Row 13 sends its results then; the return `AskForSync` follows on the same
   ordered stream, so the server's snapshot already contains them. A new requirement, scenario and harness stand-in
   (`leave-mark`, task 4.5) cover this. "Merge on return" was rejected (D6).

### Major
5. **The first slice could not be split off.** Group 2 needed row 2's car DTO, and the scenario mixed in travel.
   *Changed:* tasks are regrouped into slice 1 (groups 1–2: roster, spawn, minimal Loading/Garage, idle late-join
   scenario), then names, then scenes and away/return (part 1 = M1). Part 2 (M4) holds seat/engine and purchases.
6. **Presence state was not thread-safe.** `Client.Disconnect` runs from transport threads and the timeout tick.
   *Changed:* it relies on row 7's `StateLock`, which the updated contract holds in `Client.Disconnect` and
   `Client.Update`. Removal is idempotent, and the broadcast and `Left` event fire once (D3, task 1.8).
7. **The presence packet would be dropped before `SyncAck`.** Row 7 drops packets from clients that are not yet
   `InSession`, and the joining client publishes `Garage` before its `SyncAck`. *Changed:* the `PlayerPresence`
   handler gets `[AllowBeforeSync]` once row 7's task 5.1 adds the attribute.
8. **Spawn placement could block itself.** `CheckCapsule` would hit the player's own `CharacterController` and
   trigger volumes. The `IsDefault()` condition also skipped placement in the "return" case. *Changed:* the check
   runs with the controller disabled and ignores triggers; placement is skipped only after `PlayerRestore`. Slot
   counts are now consistent (9 slots, `% 9`; the draft had both 8 and 9).
9. **The guard rule clashed with mirrors in other rows.** "Garage-bound handlers must drop packets" conflicted with
   row 2's parking profile writes and row 5's `ClientToolsState`. *Changed:* away from the garage, handlers may not
   touch scene objects but may update data mirrors (D6).
10. **Discovering scene names needed manual travel.** *Changed:* the harness `travel` first tries the map path,
    and the name table is filled from the hook-trace logs and a `scene-list` command (task 4.3).

### Minor
11. **Wrong paths in the proposal.** Server files are under `Network/Handlers` and `Network/Transport`, and the name
    is sent from `AuthHandler.HandleConnect` *and* `ClientSteam`, not `Network/Client.cs`. Fixed.
12. **`GameScene` values described as never persisted.** Row 7 persists `PlayerRecord.Scene`, so the values are
    now explicit and stable. Fixed.
13. **Engine sound described as a singleton.** `GameManager.EngineAudioController` is a property. `ExitFromInterior`
    and `SitInside` are coroutines, so `EnsureNotSeatedIn` and the harness `stand` now start them on
    `GameScript.Get()`. Fixed.
14. **Unreachable fallback removed.** The "shared parking unavailable" scenario was dropped, because purchases are
    implemented after row 2.
15. **Dangling economy gap.** Travel fees, car sales and auction bids now point to ROADMAP row 10
    `economy-audit` instead of QUESTIONS.md.
16. **Spec gaps fixed.** Added a late-joiner-sees-seated-player scenario, photo location to the visible scenes,
    tutorial to the hidden ones, and dealer = `Salon`.
17. **`PresenceEvents` aligned with the integration notes.** Rows 1, 5 and 13 consume them, and
    `PresenceRegistry.InScene(Garage)` lets row 3 elect a garage client as order generator.
18. **Left as is: suppressing `AddPlayerMoney` during a purchase is optional.** The server's absolute `WorldState`
    corrects money anyway. It is kept to avoid a visible dip and double-click purchases.
19. **Left as is: tasks 1.8 (Steam drop) and 6.1 (auction purchase path) need the user.** They are marked to be
    parked in QUESTIONS.md when unattended.

## Conflicts to fix in other changes

- **`session-persistence-and-rejoin`, tasks.md:**
  - Task 4.4 adds a harness `teleport`; `Commands.Discover` throws on duplicate verbs. Reuse row 6's verb (task
    2.4), which lands earlier.
  - Task 5.1's `[AllowBeforeSync]` list (design D9) should include `PlayerPresence`.
  - The rest of the updated contract (slot 500, item counting via the `SendSnapshot` return value, `PlayerRecord`
    without seat or progression, `StateLock` in `Client.Disconnect`) now matches this change.
- **`sync-car-placement-and-lifts`, design.md (Decision 5, A5):**
  - `CarParkRequest` needs `Price` and `RequestId`.
  - The `CarLoaderID = -1` branch needs a reject instead of "car comes back via `CarSpawnResponse`" when the
    parking is full. Rows 3 and 6 share that branch.
- **`sync-car-details`, design.md A5:** offers `LightsOn` to row 6. Row 6 declines (it is car state, not presence);
  ROADMAP already gives dyno results to row 13.
- **`sync-car-parts`, design.md D9:** still sends straight from `OnAskForSync`. This is already noted in the
  ROADMAP; no change from row 6 is needed.

## Open questions for the user

1. **Travel in M1:** ROADMAP M1 blocks travel through row 14's guard until row 1 restores garage cars, but this
   change's `scenes` scenario needs travel. *Recommended:* the guard has a harness/config override, so the
   scenario runs while players stay blocked.
2. **Barn visibility:** remote avatars are hidden in the barn on the assumption that its layout varies per visit.
   *Recommended:* keep hidden; revisit if the barn turns out to be fixed.
