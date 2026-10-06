# Questions for the user

Open questions that block a decision. Each has the default we work with until answered.
Answered ones move to the bottom with the answer.

## Open — from the M1 drafts (2026-10-06), defaults in use

Most important first:

1. **Your main install has five gameplay mods** (QoLmod, both TK mods, LvxBetterCarSpawns, QuickShop), so
   `mod-compatibility` would refuse it. Default: play multiplayer from an install without them (visual mods
   like LoadOptimizer are fine).
2. Mods the heuristic classes as "unknown" are refused. Default: yes.
3. The game version and DLC set are pinned from the first client that joins a new server. Default: yes.
4. Desync auto-repair (the server reloads a mismatching car/section) is on by default. Default: yes.
5. The pause menu does not pause the game while connected. Default: yes.
6. Photo mode, map and the main gate are allowed in M1 if the audit shows they change no shared state. Default: yes.
7. A server password applies to DirectIP joins only (Steam joins go through friends/invites). Default: yes.
8. The server ships inside the client zip (`TogetherServer\`), so one download can host. Default: yes.
9. A server hosted from the game keeps running when the host leaves the session. Default: yes.
10. Admin = whoever has the server's admin key; the only admin action is kick. Default: yes.
11. Steam rich presence shows the join string, so friends can join from the friends list. Default: yes.
12. Hosting UI: spike IMGUI first; clone the game's UI only if IMGUI fails. Default: yes.
13. Every client writes its own bug-report bundle; the server bundle includes a save copy without player keys. Default: yes.
14. Version `0.6.0` for the M1 dev build, `1.0.0` for M6; `.pdb` files are shipped; GitHub releases only when you
    ask (outward communication). Default: yes.
15. Docs in English; the short `TRY-IT` for friends could be Danish. Default: English.
16. Row 9's game-data exporter moves into row 16 (`server-game-logic`), its heuristic tuning into a follow-up. Default: yes.

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
