# Design: server-owned order clock and limit

The only part kept from ROADMAP row 16 (`server-game-logic`; the rest is dropped). Size S–M (about 1–1.5 sessions).
Status: design for review, nothing built.

**Today** (row 3, `sync-orders-and-jobs`):

- The server elects one generator: the lowest client id that is `InSession` and in the garage.
- That client runs the game's own `OrderGenerator.Update`, so its local `orderTimer`/`nextOrderTime`, loaded from its
  own profile, decide when an order comes. Its `GlobalData.Jobs < GetMaxOrdersAmount()` decides the limit.
- When the role moves, the new generator continues from its own, unrelated timer. The server only checks the
  generator's reported `MaxOpenOrders`.

**Goal:**

- The server owns the order timer, the open-order limit and when the next order is due.
- The elected garage client still makes the order with the game's own `GenerateNewJob`, but only when the server
  asks. Its local timer no longer decides anything.
- Missions stay as they are on `main` (`fix/story-missions`).

## 1. What vanilla does (static; decompiles under `%USERPROFILE%\CMS21-TestInstalls\native\out\`)

**`OrderGenerator.Update` (`0x180C5B920`; spike `orders-and-jobs.md` §1)** runs only while
`NotificationCenter.IsGameReady && GameSettings.CanGenerateOrders`:

```c
max = GlobalData.GetMaxOrdersAmount();                 // by RealPlayerLevel: 0-2 → 2 … ≥20 → 8; 0 in the tutorial
if (GlobalData.Jobs < max) orderTimer += Time.deltaTime;   // scaled time: pausing stops it
if (GlobalData.Jobs < max && orderTimer > nextOrderTime) { GenerateNewJob(); orderTimer = 0; nextOrderTime = 30; }
```

- `GlobalData.Jobs` counts every open order, missions included.
- At the limit the timer stops but is not reset. After a decline or an expiry it continues from where it stopped.

**Resets:**

- Accepting an order: `<TakeJob>d__19` state 0 sets `orderTimer = 0`, `nextOrderTime = 30`.
- Ending or cancelling a taken job: `CancelJob` of an id in `selectedJobs` sets `orderTimer = 0`.
- A decline or an expiry (`CancelJob` of an id in `jobs`) does not touch the timer.

**`GameSettings.CanGenerateOrders`** is a difficulty setting, not a scene flag. Its writers are
`DifficultyManager.ActivateDifficultyLevel`, `BaseDifficulty.Activate`, `DifficultySettings.*` and `Sandbox.Prepare`,
which sets it to false. `Load` returns early when it is false.

**Leaving the garage freezes the order state; returning restores it:**

- Leaving: `NotificationCenter.<SelectSceneToLoad>d__34` calls `GarageLoader.Save(false)`.
  `OrderGenerator.Save` (`0x180C65460`) writes `orderTimer`, `nextOrderTime`, `LastUId`, `CurrentMissionDone` and the
  open orders (with `timeToEnd`) into the profile (`ProfileData+0xE0`). Then `SceneManager.LoadScene("SceneLoader")`.
- Returning: `GarageLoader.<Load>d__14` sets `IsGameReady = false`, calls `OrderGenerator.Load` (`0x180C64250`;
  its only caller) and sets `IsGameReady = true` at the end.
  - `Load` reads `orderTimer`/`nextOrderTime` back and **replaces `jobs` with a new list** built from the profile.
  - It drops non-mission orders whose `timeToEnd` is about 0, restarts their expiry timers and may regenerate the
    mission (`CanRegenerateMission`).
- So whatever the generator object did while the player was away is thrown away on return. Order expiry timers
  (`Job.<Timer>` on `UIManager`, in scaled seconds) restart from the saved `timeToEnd`.
- Whether `GameManager`/`OrderGenerator` survives the scene change does not matter for the result.
  `Singleton<T>.Instance` only creates a `DontDestroyOnLoad` object when none exists, and `GameManager.Awake` does not
  call it, so this is not settled statically, and nothing depends on it.

**Result: in single player, no order is made and no order expires while the player is away** (junkyard, barn,
auction, salon, parking, test track: every trip goes through `SelectSceneToLoad`). On return, the clock continues from
where it stopped. There is no catch-up and no burst.

- On a new profile the first order comes 10 s after the garage is ready: the constructor sets `nextOrderTime = 10`.
- After loading a save, the clock continues from the saved values.

## 2. Server state and rules

`JobsState` gains `[OptionalField] OrderClock Clock` (`OrderTimer`, `NextOrderTime`):

- It is saved in the `jobs` section without a version bump. Newtonsoft fills missing members, and a missing clock
  means `{ 0, 10 }`, like a new profile.
- Runtime only (`JobsService`): `pendingRequest` (id, client, sent at).

`JobsService.Tick(now)` runs the vanilla rule with real seconds:

- **The clock runs only while a generator is elected,** that is while at least one `InSession` player is in the
  garage. With nobody in the garage it stands still, as in single player.
- **Limit:**
  - `max = MaxOrders(world.Level)`, a port of `GetMaxOrdersAmount`'s table, with the RVA in the commit message. The
    server's `WorldState.Level` is the game's `RealPlayerLevel`, because clients apply `PlayerLevel = Level - 1`.
  - `open = State.Orders.Count`: open and claimed orders, missions included, like `GlobalData.Jobs`.
  - The generator's `MaxOpenOrders` is still checked as today. A difference between the two is logged once.
- **Ticking:** `if (open < max) OrderTimer += delta`.
- **When the order is due** (`open < max && OrderTimer > NextOrderTime && pendingRequest == null`), the server asks
  the generator for one order.
- **When an order from the generator is accepted** (`OnOrderGenerated`, non-mission), set `OrderTimer = 0` and
  `NextOrderTime = 30`, whether or not it was requested. Harness `orders-generate` keeps working, and the timing is
  vanilla's.
- **Request timeout:** if no order comes back within 10 s (the generator's pool was empty, the take of a scene was
  in progress, or the client ignored it), the request is dropped and asked again on the next tick. Vanilla retries
  every frame in that case.
- **Resets:** an approved `Accept` sets `OrderTimer = 0`, `NextOrderTime = 30`. A job end (`OnJobEnd`) and a lost
  car's reopen set `OrderTimer = 0`. Declines and expiries leave the clock alone.
- **Expiry** (`RemainingSeconds`) is unchanged: it ticks while any client is connected.
  - Vanilla freezes it while away, so this is **open question 1**.
  - Recommendation: freeze expiry with the clock, that is while nobody is in the garage. It is one condition in
    `Tick` and makes "away" fully vanilla-like.
- **Missions are unchanged:** `OfferMission` on the generator, `HasMission` refusal, outside the limit check of
  `OnOrderGenerated`. The clock never asks for a mission.
- Server command `jobs` adds the line `clock 12.3 / 30 s, limit 3 (level 4), requested from client 1 2 s ago`.

## 3. Packets (optional fields only, no version bump)

- `OrderGeneratorRolePacket`:
  - `[OptionalField] public bool ServerClock;` tells the generator that the server owns the clock.
  - `[OptionalField] public int OrderRequest;` asks for one order when it is greater than 0 (a request id).
  - The server sends the role packet again with `OrderRequest = id` to ask.
  - This reuses the generator's existing channel instead of a new `PacketTypes` value; a reviewer may prefer a new
    `OrderRequest` packet type (append-only).
- `OrderGeneratedPacket`: `[OptionalField] public int RequestId;` echoes the request (0 means unrequested). It is used
  only for the log and the pending request.
- Mixed builds cannot meet: the protocol hash changes with the new fields and the client refuses a different hash. So
  there is no old-client fallback beyond "a missing field is false or 0".

## 4. What the client stops doing

- The generator's `OrderGenerator.Update` still runs, so the vanilla mission branch is untouched. While connected
  with `ServerClock`, the `JobHooks.BeforeUpdate` prefix sets `orderTimer = 0` every frame, so the local timer never
  reaches `nextOrderTime`. Non-generators keep `Update` blocked as today.
- `JobsSync.OnRole` with `OrderRequest > 0` (generator only, garage ready, not applying, no take pending) calls
  `generator.GenerateNewJob()`.
  - The existing `AfterGenerate` → `SendNew` path sends it as `OrderGenerated` with the `RequestId`.
  - If the client cannot generate (not in the garage, a take running), it does nothing and the server asks again
    after the timeout.
- Harness `orders-autogen off` keeps meaning "no automatic orders": it also blocks the request handler (a harness
  prefix). Today's scenarios that count orders exactly (`jobs`, `jobs-latejoin`, `jobs-missions` …) therefore stay
  as they are. The server re-asks every 10 s meanwhile, and that log line is throttled.

## 5. Handover, late join, server restart

- **Handover:** the clock is on the server, so a new generator changes nothing. On `Elect()` changing the generator,
  a pending request is dropped. The new generator gets the request on the next tick, because the timer is still past
  `NextOrderTime`.
- **Nobody in the garage:** no generator, so the clock stands. On the first garage arrival it resumes from the frozen
  value: no order on arrival unless one was already due.
- **Late join:** nothing new. The joiner gets the jobs snapshot as today (the clock rides along in `JobsState`, unused
  by clients) and may become generator later.
- **Server restart:** the clock is saved with the jobs section and resumes. A save without it starts at `{ 0, 10 }`.
  A pending request is runtime only and is re-sent after the restart if the order is still due.

## 6. Proof scenario `jobs-clock` (two instances, fresh session as in `jobs-missions`)

Each step must fail on `main`. Times come from server log timestamps (1 s resolution), with a tolerance of ±3 s.
New harness verb: `orders-timer <timer> <next>` sets the native `OrderGenerator.orderTimer`/`nextOrderTime` and
returns them, so a client's local timer can be set close to due.

1. **Session start:** A and B in the garage, autogen on, the shared level at a limit of 3. The first regular order
   comes 10 s after A's garage arrival, the second 30 s later. On `main` the timing depends on A's profile; this step
   documents rather than proves.
2. **The local timer does not decide:** right after an order, `orders-timer 29 30` on the generator A. No order comes
   in the next 5 s, and the next comes 30 s after the previous one. On `main`, A makes one within about 1 s.
3. **Handover keeps the clock:** set B's local timer to `29 30`, then A travels to the junkyard 10 s after an order.
   B becomes generator, and the next order comes 30 s after the previous one, not at once. On `main`, B makes one
   within about 1 s of the handover.
4. **Nobody in the garage:** B travels too. During 45 s no order is made, and the server `jobs` clock does not move.
   A returns, and the next order comes after the remaining time, not at arrival and not as a burst.
5. **Limit:** at the limit no request is sent, and `jobs` shows the clock standing. B declines one order, and the next
   comes 30 s after the last order, because the timer stood at the limit.
6. **Restart:** `save`, stop and start the server. `jobs` shows the same clock (±1 s), and the next order comes at the
   saved remainder.
7. **Accept and job end reset the clock**, as vanilla does: after an accept the next order is 30 s later.

Regression: `jobs`, `jobs-latejoin`, `jobs-missions`, `jobs-seeded` and the smoke set.

## 7. Risks

- **Harness interplay:** 8 scenarios use `orders-autogen off` (10 use `orders-generate`). If the harness block is missed, server-requested
  orders break their exact counts. Mitigation: the harness prefix, plus one run of every scenario that uses
  `orders-generate`.
- **Timing assertions under load:** the server's 1 s log resolution and loaded lanes. Mitigation: ±3 s tolerances and
  intervals measured between server log lines, not client polls.
- **Scaled time:** vanilla's clock pauses with `timeScale 0`; the server uses real seconds. In multiplayer the game is
  not paused for others, so this matches what players see.
- **`GenerateNewJob` with an empty pool** (DLC, level) makes nothing. The server then re-asks every 10 s: a small log
  cost, no state drift. Vanilla also increments `GlobalData.Jobs` before success, which `SendNew` already corrects.
- **The limit port** must match `GetMaxOrdersAmount` exactly (bands at 3, 5, 8, 12, 16 and 20; 0 in the tutorial,
  which multiplayer blocks). The generator's `MaxOpenOrders` check stays as a cross-check, and mismatches are logged.
- **The level mapping** (`WorldState.Level` = `RealPlayerLevel`) is taken from `WorldStatesPackets`
  (`GlobalData.PlayerLevel = packet.Level - 1`). Step 1 checks the limit at a set level.

## Open questions

1. Freeze order expiry while nobody is in the garage, as vanilla does? Recommended yes. Today the expiry ticks while
   anyone is connected.
2. Reuse `OrderGeneratorRolePacket` for the request (as above) or add a `PacketTypes.OrderRequest`?
