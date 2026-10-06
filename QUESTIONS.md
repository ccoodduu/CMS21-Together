# Questions for the user

Open questions that block a decision. Each has the default we work with until answered.
Answered ones move to the bottom with the answer.

## Open — new (2026-10-06, row 2)

1. **Moving a car onto an occupied place.** In the game, moving a car onto a place where another car stands swaps the
   two cars. The design said the server refuses a move onto an occupied place, which would make that swap fail
   whenever you are connected. Options: (a) a swap request, so the server swaps both cars in one change; (b) refuse it
   and show "the place is taken" (simpler, but a vanilla feature stops working). **Default: (a).**
2. **Removing an unloadable car from parking.** When a parked car cannot load (its config or DLC is missing), the game
   asks "remove it from parking?" and deletes it. Default: blocked while connected, with a message, because it only
   happens when players have different DLC or mods.

## Open — smaller, defaults probably fine

- Remote players see only the finished part state, not the bolt animation, while a part is reserved.
- When two players edit the same car-detail section at once, the last write wins.
- Whoever spawns a car decides its random values (damage, colour…).
- A joining player's own local-save cars are dropped while connected; the server's cars are shown.
- Order generation runs on one elected client (lowest player id) because the server has no game code.
- Payout/XP reported by the finishing client is trusted (with bounds checks).
- Steam achievements/stats for a finished job go to the player who finishes it.
- Barn: other players are hidden (its layout differs per visit).
- A seated player's avatar is hidden instead of posed.
- Player name comes from a mod setting (no in-game UI yet).
- Engine stand 2 is synced like stand 1; disabled while connected if that fails.
- While connected, taking a car out from the separate Parking scene is blocked (garage parking works).
- No automatic reconnect after a server restart; a server that cannot load its save refuses to start.

## Decide later

- **Server-hosted generator client** (your idea, 2026-10-06): the server runs a hidden game instance that generates
  orders, car damage, junkyard layouts and prices with the game's own code. A spike measures it at the start of M3
  (headless or not, RAM, generation without a player, Steam). Then we decide together whether it replaces parts of
  row 16. Until then the plan stays: elected player generator (row 3), server logic later (row 16).

## Answered

Answered by the user on 2026-10-05:

1. **Level, XP and skills: shared or per player?** Shared, as in Dev today (one `WorldState`). Per-player
   data is only identity, name, position/scene.
2. **Start from an existing single-player save?** Maybe a later feature (backlog), not now.
3. **Cars bought outside the garage** always go to the shared parking. Yes.
4. **Order expiry** pauses while nobody is connected. Yes.
5. **Story missions** synced like normal orders. Yes. Tutorial: make it multiplayer only if cheap; otherwise
   drop the tutorial from multiplayer games (decision: drop it now, multiplayer tutorial is backlog).
6. **Wheel balancer minigame** is kept (not skipped). Yes.

Second round (2026-10-05):

- Tutorial: disabled in multiplayer (confirmed).
- Remote bolt/part animation: user asked how hard → planned as polish after M2 (visual-only replay).
- Two players on the same car: yes — already the design (only the same part at the same time is reserved).
- Random values of a spawned car: the server decides which client's roll counts (it cannot roll itself: the
  game's generation code is native). Same principle for order generation and job payout: the server owns and
  validates the result; computing it server-side would mean re-implementing hidden native game logic (revisit later).
- Joining player's local-save cars are not shown while connected: ok.
- Steam achievements/stats for a finished job: ALL connected players get them.
- Shared junkyard/barn ("scavenge together"): wanted → new ROADMAP row 15 `shared-outdoor-scenes` (after M4).
  Until then: barn hides other players (ok for now).
- Seated player hidden instead of posed: ok. Name from config until the UI exists: ok.
- Engine stand 2 synced like stand 1, disabled while connected if unstable: ok (explained to user).
- Parking scene blocked while connected: ok for now. No auto-reconnect; unloadable save refuses to start: ok.
- Playtest with a friend: later, date unknown.
- DLC: nobody owns any DLC → the handshake just compares DLC sets.
- Mods: for now no gameplay mods, only visual mods → row 9 checks only mods that change gameplay
  (server-configured required/ignored lists + Harmony patch-target heuristic), visual mods are ignored.
- Difficulty: chosen on the server; sandbox skipped for now.

Third round (review questions, 2026-10-05):

- Headlights on/off: a feature, owned by `sync-car-details` (not backlog).
- Wheel balancer: locked while one player has the minigame open (one person at a time).
- Lost job car: the order reopens with its original time (explained to the user).
- Accepted defaults: same identity twice → second refused; returning player spawns where they left; M1 guard
  overridable via config; visual tuning (bonus) parts in `sync-car-details`; workshop split into
  `sync-workshop-machines` + `sync-workshop-car-tools`; machine-usage spike needs the user later; customer cars
  cannot be parked while connected; parking-level price from the client until row 10; parts changed just before
  parking may be lost (v1); no new orders while nobody is in the garage; engine swap disabled while connected if
  it cannot be replayed cleanly.

Fourth round (2026-10-06):

- CMS21 gets no more updates (CMS 2026 is coming), so porting game logic to the server has no maintenance cost.
  Move to the server: prices/fees, job payout/XP, order generation, random damage/colour of spawned cars
  (ROADMAP row 16 `server-game-logic`, decompile spike with Cpp2IL/Il2CppDumper + Ghidra in M0).
- Junkyard: not ported from the game; instead adapt the user's LvxBetterCarSpawns mod (author LvxMagick)
  on the server (row 15). Lvx also covers barn and auction, so the same approach is planned there.
  Adapting Lvx code for a public fork needs the author's permission/credit — asking is outward communication,
  so the user decides when.

Fifth round (M1 drafts, 2026-10-06):

- Gameplay mods: play multiplayer without them for now (drop them in multiplayer). The mod check refuses them.
- DLC: the user asked whether a DLC one player owns can be "in". Answer and new default for `mod-compatibility`:
  DLC content must be installed and owned on every client to load, so players may join with different DLC sets;
  the server tracks the DLC set owned by all connected players, and DLC content not owned by everyone (DLC cars,
  parts, tools) is blocked from shared use. Game version is still pinned from the first client.
- All docs in English (including TRY-IT).
- Everything else: the proposed defaults (unknown mods refused, auto-repair on, pause menu does not pause,
  photo mode/map/main gate allowed if the audit shows no shared state, password for DirectIP only, server inside
  the client zip, hosted server keeps running when the host leaves, admin key with kick only, rich presence join
  string on, IMGUI spike first, per-client bug bundles + server save copy without keys, 0.6.0/1.0.0 with .pdb and
  GitHub releases only on request, row 9's exporter moves to row 16).
