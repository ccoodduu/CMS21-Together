# Questions for the user

Open questions that block a decision. Each has the default we work with until answered.
Answered ones move to the bottom with the answer.

## Open — gameplay (please answer)

1. **Level, XP and skills: shared or per player?** Dev today shares everything (one `WorldState`).
   `session-persistence-and-rejoin` makes level/XP/skills per player (money, scrap, upgrades, inventory,
   cars stay shared); `sync-orders-and-jobs` assumed shared. Default: per player, job XP goes to the
   player who finishes the job.
2. **Start from an existing single-player save?** Default: out of scope; a server starts a new career.
3. **Cars bought outside the garage** (junkyard, barn, auction, dealer) always go to the shared parking,
   never straight into the garage. Default: yes.
4. **Order expiry** pauses while nobody is connected. Default: yes.
5. **Story missions** synced like normal orders (tutorial excluded). Default: yes.
6. **Wheel balancer minigame** is no longer skipped (0.4.x and FixForTogether skipped it). Default: not skipped.

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
