# Design: server-owned order clock and limit

The only part kept from ROADMAP row 16 (`server-game-logic`; the rest is dropped). Size M (about 2 sessions).
Status: reviewed (`server-order-clock-review.md`, "ready after fixes"); every fix is folded in below and marked with
its review id.

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
- Missions stay as they are on `main`.

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
- The reset after `GenerateNewJob` does not depend on a job coming out. With an empty car pool (DLC or level gating),
  vanilla waits a full 30 s before it tries again (M2).

**Resets:**

- Accepting a **job**: `<TakeJob>d__19` state 0 sets `orderTimer = 0`, `nextOrderTime = 30`, before the full-garage
  check. Taking a **mission** (`<TakeMission>d__22`) does not touch the timer (M1).
- Ending a taken job or mission: `CancelJob` of an id not in `jobs` sets `orderTimer = 0`. `nextOrderTime` is kept.
- A decline or an expiry (`CancelJob` of an id in `jobs`) does not touch the timer.
- Nothing else reads or writes `orderTimer`/`nextOrderTime` within the decompiled set.

**`GameSettings.CanGenerateOrders`** is a difficulty setting, not a scene flag. Its writers are
`DifficultyManager.ActivateDifficultyLevel`, `BaseDifficulty.Activate`, `DifficultySettings.*` (including
`DisableMissions`) and `Sandbox.Prepare` (false). `Load` returns early when it is false.

**Leaving the garage freezes the order state; returning restores it:**

- Leaving: when `saveGame` is set (every travel path sets it: `MapWindow.SubmitPanelAction`,
  `SideCarsPanel.DriveAction`, `TakenItemsWindow.*`), `NotificationCenter.<SelectSceneToLoad>d__34` calls
  `GarageLoader.Save(false)` (m1). `OrderGenerator.Save` (`0x180C65460`) writes `orderTimer`, `nextOrderTime`,
  `LastUId`, `CurrentMissionDone` and the open orders (with `timeToEnd`) into the profile (`ProfileData+0xE0`). Then
  `SceneManager.LoadScene("SceneLoader")`.
- Returning: `GarageLoader.<Load>d__14` sets `IsGameReady = false`, calls `OrderGenerator.Load` (`0x180C64250`; its
  only caller) and sets `IsGameReady = true` at the end. `Load` reads the clock back and **replaces `jobs`** with a new
  list built from the profile; it drops non-mission orders whose `timeToEnd` is about 0 and restarts their expiry
  timers.

**Result: in single player no order is made and no order expires while the player is away**, and on return the clock
continues from where it stopped: no catch-up, no burst.

- The constructor sets `nextOrderTime = 10`, but `Load` overwrites it from the profile, and `NewJobsData..ctor` leaves
  it at 0. What a brand-new profile starts with is not verified (m2). The server's `{0, 10}` for a new session is a
  choice, not vanilla parity.

## 2. Server state and rules

`JobsState` gains `[OptionalField] public OrderClock Clock = new OrderClock();` with `OrderTimer` and
`NextOrderTime = 10`:

- It is saved in the `jobs` section without a version bump. `JobsSection.Load` uses `ToObject`, which keeps the
  initializer, so a save without the member starts at `{0, 10}`.
- Runtime only (`JobsService`): the pending request (id, client, sent at) and the earliest retry time.

**The limit (M6, approved by the user):** `MaxOrders(level)` is a port of `GlobalData.GetMaxOrdersAmount`
(`0x180D7CD50`): `level < 0` → 0; 0–2 → 2; 3–4 → 3; 5–7 → 4; 8–11 → 5; 12–15 → 6; 16–19 → 7; ≥ 20 → 8. `level` is the
server's `WorldState.Level`, which is the game's `RealPlayerLevel` (clients apply `PlayerLevel = Level - 1`). This is
the one deliberate exception to "no server-side port of game logic": the server needs the limit continuously, to stop
and resume the clock and to decide whether to ask at all, and the generator only reports its `MaxOpenOrders` together
with an order. The generator's `MaxOpenOrders` stays as a cross-check; a mismatch is logged once per value pair.

**The clock runs** while a generator is elected (at least one `InSession` player is in the garage) and
`WorldState.Gamemode != Sandbox` (M5). Every server tick (`JobsService.Tick`), with `delta` clamped to 1 s (m7):

- `open = State.Orders.Count` (open and claimed orders, missions included, like `GlobalData.Jobs`).
- A pending request whose client is no longer the generator is dropped (m6); one older than 10 s is dropped as lost.
- `if (open < max) OrderTimer += delta`.
- **Due** when `open < max && OrderTimer > NextOrderTime`, there is no pending request and the retry time has passed:
  the server sends `OrderRequest { RequestId }` to the generator and logs `Order due: asking client N (request R)`.

**Answers** (`OnOrderGenerated` from the generator):

- A regular order with `RequestId == pending.Id`, or `RequestId == 0` (unrequested: harness `orders-generate`):
  - refused when `open >= MaxOrders(level)` (`refused: N open orders, the limit is M`; m5 updates `jobs-missions`) or,
    as today, when the generator's own `MaxOpenOrders` is lower;
  - otherwise accepted as today, then `Clock = {0, 30}` and the pending request is cleared.
- A regular order with any other `RequestId` is **stale** (an answer to a request that timed out or was replaced):
  refused with the jobs snapshot, as the other refusals are. The client already dropped it locally (M3).
- `Job == null` with `RequestId == pending.Id` and a `Reason` (M2):
  - `NoCar` (the pool made nothing) and `Disabled` (`CanGenerateOrders` is false): `Clock = {0, 30}`, as vanilla;
  - `NotReady`, `Busy` (a take or an apply is running) and `HarnessOff`: retry after 2 s.
  - The log line for the same reason is throttled to once per 30 s.
- Missions are unchanged: generated by the generator's `OfferMission`, refused while one is open or active, outside
  the limit check. The clock never asks for a mission.

**Resets:**

- An approved `Accept` of a regular order: `Clock = {0, 30}`. A mission accept leaves the clock alone (M1).
- A job or mission end (`OnJobEnd`): `OrderTimer = 0`.
- Declines, expiries and a lost car's reopen leave the clock alone (m3).

**Expiry is frozen with the clock (open question 1: yes).** The `RemainingSeconds` countdown runs only while the clock
runs (a generator is elected and not Sandbox). The claim timeout keeps running. Missions never expire.

The server command `jobs` adds the line `clock 12.3 / 30 s, 2 of 8 open (level 20), running|frozen, request R to
client N 2 s ago`.

## 3. Packets (optional fields only, no version bump; open question 2: a new packet type)

- `PacketTypes.OrderRequest` (appended) with `OrderRequestPacket { int RequestId; }`, server → generator. The role
  packet stays role only.
- `OrderGeneratedPacket` gains `[OptionalField] int RequestId` (0 = unrequested) and
  `[OptionalField] OrderRequestReason Reason` (`None`, `NoCar`, `NotReady`, `Busy`, `Disabled`, `HarnessOff`), used
  when `Job` is null.
- `JobsState.Clock` (above). Mixed builds cannot meet: the protocol hash covers the new type and fields, and the client
  refuses a different hash (m4: no `ServerClock` flag).

## 4. What the client does

- **Gate (M4):** `JobHooks.BeforeGenerate` returns false while connected unless `JobsSync.ServingRequest` is set. The
  generator's `Update` keeps running for its mission branch; when its local timer fires, the blocked `GenerateNewJob`
  makes nothing and `Update` resets its own timer, which is harmless. No local timer value can leak an order.
- **`JobsSync.OnOrderRequest(packet)`** answers every request:
  - not the generator, garage not ready or `NotificationCenter.IsGameReady` false → `NotReady`;
  - `GameSettings.CanGenerateOrders` false → `Disabled` (M5);
  - a take pending or an apply running → `Busy`;
  - otherwise `JobsSync.GenerateOrder(requestId)`: it sets `ServingRequest`, calls the game's `GenerateNewJob()`, and
    the existing `AfterGenerate` → `SendNew` path sends the order with the `RequestId`. If no job came out it answers
    `NoCar`. Either way it then sets `GlobalData.Jobs = jobs.Count` (m8).
- `GenerateOrder(0)` is also the harness's `orders-generate` path while connected (an unrequested order).

## 5. Handover, late join, server restart

- **Handover:** the clock is on the server. Late answers from an old generator are refused by the existing generator
  check, with a snapshot back. A pending request to a client that is no longer generator is dropped in `Tick` (m6), and
  the new generator is asked on the next tick, because the timer is still past `NextOrderTime`.
- **Generator leaves mid-request:** the client publishes `Loading` presence before the scene load, so the server
  re-elects at once; an order sent before leaving arrives first on the same connection and is accepted. A request that
  arrives during the leave is answered `NotReady`.
- **Nobody in the garage:** the clock and the expiry stand still. On the first garage arrival they resume from the
  frozen values: no order on arrival unless one was already due.
- **Late join:** nothing new. The clock rides in `JobsState`, unused by clients.
- **Server restart:** the clock is saved with the jobs section and resumes; the pending request is runtime only.

## 6. Harness

- `orders-timer [<timer> <next>]` sets or reads the native `OrderGenerator.orderTimer`/`nextOrderTime`. It makes `main`
  misbehave on cue; on the new code it changes nothing.
- `orders-ttl <seconds>|off`: a sticky `timeToEnd` for every order this client generates, requested or not (B1).
- `orders-autogen off` also blocks `JobsSync.OnOrderRequest` and answers `HarnessOff` (m9).
- `orders-generate` while connected goes through `JobsSync.GenerateOrder(0)`.

## 7. Proof scenario `jobs-clock` (two instances, fresh session as in `jobs-missions`)

Setup: `level set 20` (limit 8), `orders-ttl 900` on both, autogen on (B1). Each new regular order is declined right
after its server log line, so the open count stays low and the clock never meets the limit before step 5 (a decline
does not touch the clock). Times come from server log timestamps (1 s resolution), with a tolerance of ±3 s. Travels
go to the parking scene (m11). Steps marked **regression** pass on `main` too (M7); the others must fail there.

1. **Regression:** the first regular orders come, then one every 30 s while both are in the garage.
2. **The local timer does not decide:** right after an order, `orders-timer 29 30` on the generator A. The next order
   comes 30 s after the previous one. On `main`, A makes one within about 1 s.
3. **Handover keeps the clock:** 10 s after an order, B gets `orders-timer 29 30` and A travels to parking. B becomes
   generator, and the next order comes 30 s after the previous one. On `main`, B makes one at the handover.
4. **Nobody in the garage:** B stays generator for about 25 s after an order, then travels too. During 45 s no order
   is made and the server's remaining time of an open order does not move (on `main` it drops by about 45 s). A
   returns; the next order comes `30 − 25` s after A's election (`Order generator: client N`). On `main`, A's restored
   local timer gives it about 20 s after.
5. **Regression, the limit:** `orders-generate 900` up to the limit, no request for 40 s, then one decline: the next
   order comes 30 s after the decline.
6. **Restart:** 10 s after an order, `save`, stop and start the server, both reconnect; A (generator) gets
   `orders-timer 29 30` as soon as it is in the garage. The next order comes at the saved remainder after A's
   election. On `main`, A makes one at once.
7. **The non-generator's accept resets the clock:** B accepts a regular order 15 s after the last order; the next
   order comes 30 s after the accept. On `main`, B's accept resets only B's local timer.
8. **Regression:** taking the story mission leaves the clock alone.

Regression runs: `jobs`, `jobs-latejoin`, `jobs-missions`, `jobs-seeded`, every scenario that uses
`orders-autogen off`, and the smoke set.

## 8. Risks

- **Harness interplay:** 8 scenarios use `orders-autogen off` and 10 use `orders-generate`. In a fresh session the
  first server-requested order can arrive before `orders-autogen off` is sent; the current assertions tolerate that.
  Mitigation: the harness block, and one run of every scenario that uses either verb.
- **Timing assertions under load:** 1 s server log resolution on a loaded lane. Mitigation: ±3 s tolerances, intervals
  between server log lines rather than client polls.
- **Scaled time:** vanilla's clock pauses with `timeScale 0`; the server uses real seconds, which matches what players
  see in multiplayer.
- **The limit port** must match `GetMaxOrdersAmount`; the generator's `MaxOpenOrders` cross-check logs any mismatch.
