# Review: server-owned order clock and limit

Reviewed: `docs/design/server-order-clock.md` at `ee1d0ca` (branch `feat/server-order-clock`).
Method: read-only. I checked the note against `JobsService.cs`, `JobsSection.cs`, `JobPackets.cs`, `JobHooks.cs`,
`JobsSync.cs`, `ClientScene`/`SceneHooks`/`LoaderAddition`, the harness `JobsCommands.cs`, the scenarios that use
`orders-autogen`/`orders-generate`, `ProtocolHash.cs`, the diff of `feat/seeded-job-cars`, the spikes
`orders-and-jobs.md`/`outdoor-scenes.md`, and the decompiles under `native\out\clean\` and `native\out\orderclock_clean\`
(`OrderGenerator.Update/Load/Save/CancelJob/.ctor`, `<TakeJob>d__19`, `<TakeMission>d__22`, `NewJobsData..ctor`,
`GlobalData.GetMaxOrdersAmount`, `<SelectSceneToLoad>d__34`, `GarageLoader.<Load>d__14`, the `SelectSceneToLoad` call
sites).

## Verdict: ready after fixes

The core design holds up. The server owns the clock and the limit, the elected garage client still makes the order with
the game's own `GenerateNewJob`, and missions are untouched. Handover, late join and restart become simpler than they are
today. Four things need fixing before you build:

- the proof scenario cannot pass as written (B1);
- two vanilla claims are wrong: taking a mission does not reset the clock (M1), and an empty car pool does not retry
  every frame (M2);
- zeroing the timer every frame does not stop local orders in every case (M4);
- the new request path skips the game's `CanGenerateOrders` check, which turns orders off in Sandbox and in a custom
  difficulty with missions disabled (M5).

None of these changes the architecture.

## Vanilla claims against the evidence

| Claim | Evidence | Status |
|---|---|---|
| `Update` ticks only below the limit; due → `GenerateNewJob`, then `0`/`30` | `OrderGenerator$$Update.c` | Supported. **But** the reset after `GenerateNewJob` is unconditional, which the note contradicts later (M2) |
| At the limit the timer stops, is not reset; decline/expiry don't touch it | `Update.c`; `CancelJob.c` (the `jobs` branch never writes `orderTimer`) | Supported |
| Accept resets `0`/`30` | `<TakeJob>d__19` lines 88–89 (state 0, **before** the full-garage check) | Supported for jobs only. `<TakeMission>d__22` has no timer write (M1) |
| Job end resets `orderTimer = 0` | `CancelJob.c`: the not-in-`jobs` branch sets `orderTimer = 0` (also for an id found nowhere); `EndJobCoroutine` → `CancelJob` | Supported, missions included. `nextOrderTime` is kept |
| Leaving saves the clock via `GarageLoader.Save(false)` | `<SelectSceneToLoad>d__34` | Partly. Saved only when `saveGame` is true and a `GarageLoader` exists. Every travel caller passes `saveGame = 1` (`MapWindow.SubmitPanelAction`, `SideCarsPanel.DriveAction`, `TakenItemsWindow.*`), so the result holds (m1) |
| Returning restores the clock and replaces `jobs` | `GarageLoader.<Load>d__14` (IsGameReady false → `OrderGenerator.Load` → true); `Load.c` lines 62–64 | Supported |
| First order 10 s after ready on a new profile | Only `OrderGenerator..ctor` (`nextOrderTime = 10`) | **Not shown.** `Load` overwrites the field from `ProfileData+0xE0` whenever that is non-null, and `NewJobsData..ctor` leaves `nextOrderTime` at 0. Whether a new profile has a null `+0xE0` is not decompiled (m2) |
| `CanGenerateOrders` is a difficulty setting (`Sandbox.Prepare` false) | `rg` over the decompiles: `BaseDifficulty.Activate`, `DifficultyManager.*`, `DifficultySettings.UpdateSettings/EnableSetting/DisableMissions`, `Sandbox.Prepare` | Supported. `DifficultySettings.DisableMissions` also clears it, which the design must respect (M5) |
| The limit table | `GlobalData$$GetMaxOrdersAmount.c` | Exact: 0–2→2, 3–4→3, 5–7→4, 8–11→5, 12–15→6, 16–19→7, ≥20→8; tutorial scene (15)→0; a negative level→0 |
| `WorldState.Level` = `RealPlayerLevel` | `WorldStatesPackets.cs:32` `PlayerLevel = Level - 1`; `RealPlayerLevel = PlayerLevel + 1` | Supported |
| No other reader of `orderTimer` | `rg orderTimer\|nextOrderTime` over every decompiled method: only `.ctor`, `Update`, `Load`, `Save`, `CancelJob`, `<TakeJob>d__19`, `NewJobsData.(De)Serialize` | Supported within the decompiled set |
| "Vanilla retries every frame" on an empty pool | — | **Wrong** (M2) |
| "`SendNew` already corrects" the `AddJob(1)` drift | `JobHooks.SendNew` returns early when no job was added | **Wrong** for the empty-pool case (m8) |

## Blockers

### B1. The proof scenario contradicts itself and depends on random expiry

- **Problem:**
  - With the limit at 3, step 1 leaves 2 open orders and step 2 adds a third. So the session is at the limit before
    step 3. Step 3 expects the next order 30 s after the previous one, but at the limit no request is sent.
  - The server-requested orders get the game's random `timeToEnd` (121–299 s). The scenario runs for several minutes,
    so orders expire at random points. That changes the open count, and with it whether the clock runs (steps 3–5).
  - So the scenario cannot pass reliably on the new code, which makes it a broken proof rather than only a flaky test.
- **Fix:**
  - Keep the open count low in steps 1–4: decline each regular order once its server log line appears. A decline does
    not touch the clock, in vanilla or in this design, so the timing stays exact.
  - Raise the limit first (server command `level set 20`, which gives a limit of 8).
  - Make expiry deterministic with a sticky harness TTL for orders generated on request, for example `orders-ttl 900`
    (reuse the `nextTtl` postfix in `JobsCommands`).
  - Test the limit only in step 5: fill it with `orders-generate 900` up to `max`, check that no request goes out, then
    decline one and check the timing.

## Major

### M1. Taking a mission must not reset the clock

- **Problem:** §2 says "an approved `Accept` sets `OrderTimer = 0`, `NextOrderTime = 30`". `OnOrderAction` handles
  missions and jobs alike. In vanilla, only `<TakeJob>d__19` resets the clock. `<TakeMission>d__22` has no write to
  `orderTimer` or `nextOrderTime` (checked: its `+0x20`/`+0x24` writes are on job tasks).
- **Fix:**
  - Reset only when `!order.Job.IsMission`.
  - Keep the reset on a mission's **end** (`OnJobEnd`), because the vanilla `CancelJob` on `selectedJobs` resets for
    missions too.
  - Add one assertion to the scenario: taking a mission leaves the clock alone.

### M2. Empty pool: vanilla waits 30 s, it does not retry; and the client should always answer

- **Problem:**
  - `Update` runs `GenerateNewJob(); orderTimer = 0; nextOrderTime = 30;` whether or not a job came out. So an empty pool
    (DLC or level gating) costs a full 30 s in vanilla.
  - The note instead lets the server re-ask every 10 s, and calls that vanilla.
  - The client also stays silent when it cannot generate. The server then cannot tell a lost request from an empty pool
    from "busy taking a job", and has to wait out the 10 s timeout every time.
- **Fix:** the generator answers every request.
  - Use `OrderGenerated { RequestId, Job = null, Reason }`, or a small `OrderRequestResult` packet. Reasons: `NoCar`,
    `NotReady`, `Busy` (take pending), `Disabled` (M5) and `HarnessOff`.
  - Server on `NoCar`: reset to `{0, 30}`, as vanilla does.
  - Server on `NotReady`/`Busy`/`HarnessOff`: retry after a short delay (2 s), with the log throttled.
  - Keep the 10 s timeout only for an answer that never arrives.

### M3. Late and duplicate answers can produce two orders back to back

- **Problem:** the note uses `RequestId` "only for the log and the pending request". Two paths give a burst:
  - **Timeout race:** request 5 times out (hitch, `net-hold`, a loaded lane) → the server asks with 6 → the answer to 5
    arrives (accepted, clock reset) → the answer to 6 arrives (accepted at once).
  - **Harness race:** `orders-generate` while request 7 is pending → the unrequested order resets the clock → the answer
    to 7 arrives and is accepted right after.
- **Fix:** in `OnOrderGenerated`, for non-mission orders:
  - Accept `RequestId == pending.Id`, or `RequestId == 0` (unrequested, harness only).
  - Refuse any other `RequestId` as stale and answer with the jobs snapshot, like the existing refusals. The client has
    already dropped the job locally (`SendNew` removes it), so nothing is lost.
  - Clear `pending` on **every** accepted non-mission order, so a later answer to it is stale.
  - Refuse a regular order when `open >= MaxOrders(level)` (the server limit), not only against the client's
    `MaxOpenOrders`.

### M4. Replace "zero `orderTimer` every frame" with a gate on `GenerateNewJob`

- **Problem:**
  - The prefix sets `orderTimer = 0`, then `Update` adds `Time.deltaTime` (up to `maximumDeltaTime`, 0.333 s by default)
    and checks `orderTimer > nextOrderTime`.
  - Any `nextOrderTime` below one frame's delta still fires a local, unrequested order. That happens with a profile whose
    saved `nextOrderTime` is 0 (the `NewJobsData` default; see m2) or with `orders-timer x 0`.
  - The server accepts unrequested orders by design and resets its clock on them. So a fresh client that becomes
    generator could make an order at once, which is exactly what step 3 tests against.
- **Side effects checked:**
  - The mission branch of `Update` reads only `GetMissionID()`, `MissionsAmount` and `CurrentMissionDone`, so it is
    unaffected.
  - No reader of `orderTimer` exists outside `OrderGenerator`.
  - `Save` writes 0 into the client's own profile, which is harmless.
  - The zeroing itself is safe, then; the gap is the comparison against a tiny `nextOrderTime`.
- **Fix:**
  - Let `JobHooks.BeforeGenerate` return `false` while connected, unless a server request is being served
    (`JobsSync.ServingRequest`) or the harness `orders-generate` bypass is set.
  - `Update` then calls a blocked `GenerateNewJob` and resets its own timer to 0/30, which is harmless.
  - The mission branch keeps working, there is no per-frame write, and no local timer value can leak an order.
  - The `orders-timer` verb stays useful for the main-branch proof (it makes `main` misbehave on cue).

### M5. The request path bypasses `GameSettings.CanGenerateOrders`

- **Problem:**
  - Today the elected client's `Update` is gated by `IsGameReady && CanGenerateOrders`. So a Sandbox world (still
    reachable through the server command `gamemode Sandbox` or an old save) and a custom difficulty with
    `DisableMissions` make no orders.
  - The new handler calls `generator.GenerateNewJob()` directly, so the server would make orders there.
- **Fix:**
  - The server does not run the clock while `WorldState.Gamemode == Sandbox`.
  - The client handler checks `GameSettings.CanGenerateOrders && NotificationCenter.IsGameReady` and otherwise answers
    `Disabled`/`NotReady` (M2).

### M6. The limit table: keep it, but get the user's explicit OK

- **Problem:** the user decided "no server-side port of game logic". `MaxOrders(level)` is a small port.
- **Why not reuse the reported `MaxOpenOrders`:**
  - The server needs the limit **continuously**: to stop and resume the clock, and to decide whether to ask at all. It
    is not enough at the moment an order arrives.
  - `MaxOpenOrders` only arrives together with an order. After a level-up while at the limit, the cached value stays
    low, no request goes out, so no order and no new value ever arrives. The clock stays stuck.
  - Fixing that needs a new client→server "my limit changed" report, which means a level hook or polling, another trust
    path, and handling for a generator that has not yet applied the level packet.
  - The table, by contrast, is a pure function of the server-owned `WorldState.Level`. I verified it against the
    decompile above, and every client computes the same value from the same synced level.
- **Fix:**
  - Keep the table and the `MaxOpenOrders` cross-check (log a mismatch once).
  - State in the note that this is the one deliberate exception to "no port", and why.
  - Confirm with the user before building.

### M7. Several proof steps pass on `main`

- **Problem:** the convention is that a proof fails on the old code. As written:
  - Step 1 only documents.
  - Step 4 can pass on `main`: A's `Load` restores A's own saved timer, which may land close to the server value.
  - Step 5 passes on `main`: the generator's `GlobalData.Jobs` mirrors the server count, so its timer also stands at the
    limit.
  - Step 6 can pass on `main`: A's game keeps its local timer across the server restart.
  - Step 7 passes on `main` if the generator accepts the order itself.
- **Fix:** make `main` diverge on purpose, and label the regression-only steps as such.
  - **Step 4:** let B be generator for about 15 s after an order before B leaves. That freezes the server clock at
    about 25 s. A left at 10 s, so on `main` A's restored local timer gives the next order about 20 s after A's return.
    The new code gives about 5 s. That difference is well outside ±3 s.
  - **Step 6:** `orders-timer 29 30` on A before the reconnect. On `main` the order comes right after A is generator;
    the new code waits for the saved remainder.
  - **Step 7:** the **non-generator** B accepts. On `main` that resets only B's timer, not the generator's.
  - **Steps 1 and 5:** mark them as regression checks, not proof.
  - **Resume timing:** measure from the server's `Order generator: client N` line.

## Minor

- **m1.** §1 "Leaving: … calls `GarageLoader.Save(false)`": add "when `saveGame` is set (every travel path sets it)".
- **m2.** Reword "first order 10 s after ready on a new profile" as unverified for vanilla. `{0, 10}` is a fine server
  default either way, so state it as a choice. Step 1 should not assert vanilla parity.
- **m3.** Drop "a lost car's reopen sets `OrderTimer = 0`" (`ReopenActive`, and `ReopenNow` on
  `feat/seeded-job-cars`). Vanilla has no such path. The reopened order counts toward the limit, which already stops the
  clock if needed.
- **m4.** Drop `OrderGeneratorRolePacket.ServerClock`. `ProtocolHash` hashes every packet's fields, and the client
  refuses a different hash, so the flag is always true.
- **m5.** `jobs-missions.ps1` waits for the log text `refused: \d+ open orders, the generator's limit is $max`. Keep that
  wording for the cross-check, or update the scenario when the server-limit refusal is added (M3).
- **m6.** Drop a pending request in `Tick` when `pending.Client != generator`, instead of only "on `Elect()` changing
  the generator". `OnClientLeft` clears `generator` before `Elect()`, so the "changed" branch is missed when nobody is
  left. The timeout would cover it, but only after 10 s.
- **m7.** Clamp the per-tick `delta` for the clock and the expiry (the server loop already detects stalls with
  `StallSeconds`). Vanilla's clock is clamped by `maximumDeltaTime`; without a clamp, a stall jumps both.
- **m8.** The request handler should set `GlobalData.Jobs = jobs.Count` after its `GenerateNewJob` call. On an empty
  pool `SendNew` returns before its correction, so the generator's count drifts by +1 per request.
- **m9.** Harness: `orders-autogen off` should block the new request handler (`JobsSync.OnOrderRequest`, see Q2) and
  answer `HarnessOff` (M2).
  - In fresh-session scenarios (`jobs-missions`), the server clock's first order now arrives 10 s after A's garage
    arrival, often before `orders-autogen off` is sent (after B joins).
  - The current assertions tolerate that, because `jobs.ps1` measures `$base` afterwards and `jobs-missions` counts
    missions and "regular at max".
  - Sending `orders-autogen off` before `Connect-HarnessInstance` (it is a plain static) makes it deterministic.
- **m10.** Land after `feat/seeded-job-cars`, or rebase onto it. Both touch `JobsState`, `JobsService.Reset/OnOrderGenerated/ReopenActive`.
- **m11.** Use a cheap, fast travel target in steps 3 and 4. The junkyard costs 500 and has the longest load. Parking is
  free.

## Topics the task asked about

- **Handover:** correct. The clock lives on the server, and late answers from the old generator are already refused by
  `OnOrderGenerated`'s generator check, with a snapshot back. Add m6.
- **Late join:** correct. Nothing new. The clock rides in `JobsState`, and the protocol hash covers it.
- **Server restart:** correct. Add `Clock = new OrderClock()` with `NextOrderTime = 10` as an initializer, so Newtonsoft
  keeps it for a save without the member. `JobsSection.Load` uses `ToObject` and keeps initializers. A pending request is
  rightly runtime only.
- **Generator leaves mid-request:**
  - The client publishes `Loading` presence in `SceneHooks.Leave` before the scene load. The server re-elects at once, and
    an order sent before leaving arrives before the presence change (same connection), so it is accepted.
  - Fine with m6.
- **Duplicate and late responses:** M3.
- **Missions:**
  - Generation is unchanged, and missions count toward `open`, like `GlobalData.Jobs`.
  - Two fixes: taking a mission must not reset the clock (M1); a mission's end must reset it (it does).
- **`orders-autogen off`:** m9 and M2.
- **Generator in a non-garage scene:**
  - The election already requires `Garage` presence.
  - `LoaderAddition.CustomLoad` sets `Garage` only after the vanilla load, so "elected" roughly matches vanilla's
    `IsGameReady`.
  - The only gap is the leave race, which M2's `NotReady` answer covers.
- **Order expiry:** after a decline or an expiry, the clock resumes from where it stood, as in vanilla. Supported.

## Answers to the open questions

1. **Freeze order expiry while nobody is in the garage? Yes.**
   - Gate the `RemainingSeconds` countdown with the clock's own condition (`generator != NoClient`, after M5's Sandbox
     check), not with "any client connected".
   - Keep the claim timeout running. Claims are released when the claimer leaves anyway, and a stuck claim must still
     time out.
   - Missions never expire, so nothing changes there.
   - Show "frozen" in the `jobs` command line.
   - No existing scenario relies on expiry while everyone is away (`jobs.ps1`'s 20 s expiry runs with both players in
     the garage).
2. **Add a new `PacketTypes.OrderRequest { int RequestId }`** (appended), and leave the role packet as role only.
   - `JobsSync.OnRole` sets `IsGenerator` from the packet, logs "Order generator: this client" and calls
     `OfferMission()`. A request riding on it repeats all three every time.
   - A request built without `IsGenerator = true` would demote the generator: an easy bug.
   - A role is state; a request is an event. A dedicated `OnOrderRequest` is also the clean point for the harness block
     (m9) and the answer (M2).
   - The protocol hash changes either way, so the new type costs nothing extra.

## Size

The note estimates S–M (1–1.5 sessions). **I estimate M (about 2 sessions):**

- **Code (about 0.5–0.75):** clock, persistence, tick, request/answer/timeout, stale handling, the limit table, the
  new packet, the client handler and gate, harness `orders-ttl`/`orders-timer`.
- **Proof scenario (about 1):** two instances, 30 s intervals, two travels, a server restart and timing assertions. Once
  B1 and M7 are fixed, one run takes roughly 8–10 minutes, and getting it stable on a loaded lane is the bulk of the
  work.
- **Regressions (about 0.25–0.5):** `jobs`, `jobs-latejoin`, `jobs-missions`, `jobs-seeded`, smoke, plus one pass over
  every `orders-generate` scenario, as the note itself plans.
