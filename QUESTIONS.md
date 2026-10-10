# Questions for the user

Open questions that block a decision. Each has the default we work with until answered.
Answered ones move to the bottom with the answer.

## Open — car angle on entrance places (2026-10-10)

Found by the `ping` batch flake: the game turns a car parked on an entrance place by a random extra angle
(`CarLoader.SetAdditionalCarRot`, `Random.Range` from the place's `CarPlaceRotation`), rolled by each client on its
own. The players' copies of the same car point up to ~15° apart, so parts sit up to ~0.5 m apart (pings, remote work
poses, ghosts).

1. **Fix.** (a) send the angle with placement and spawn and store it on the server (protocol and save change);
   (b) seed the roll from data every client shares (loader, spawn number, place), no protocol change;
   (c) leave it and run `ping` on a lift. **Default:** (b), built tonight unless you say otherwise.
   Built on `fix/entrance-car-angle` (not merged): the roll is seeded from car name, loader and place, and a saved
   angle is replaced by the same roll, so a car points the same way on every client and after every reload. A car's
   angle on a given place is therefore fixed (a respawn or move back gives the same angle).

## Open — row 19 state-merges-and-contention (2026-10-08)

Details in `openspec/changes/state-merges-and-contention/proposal.md` (reviewed, see `review.md`).

1. **Stale examine or condition change:** merged when the player's view of the part still matches, otherwise dropped
   and counted. **Default:** merge, drop only the stale parts.
2. **One body panel is one detail entry** (paint, livery, tint, dust, wash together). **Default:** yes.
3. **Parking** stores the server's own records after a drain of up to 1 s ("Try again in a moment."). **Default:** yes.
4. **Split** into part 1 (state merges), part 2 (detection and soak contention), part 3 (server answers every
   refusal, seats). **Default:** yes.
5. **Order with row 18:** groups that change the same methods wait for row 18's merge. **Default:** yes.
6. **Machine put of an item another player used** is refused with "<name> used this part."; items the server never
   saw are accepted and logged. **Default:** yes.
7. **New digest keys** only log at first; repair is turned on once the soak is quiet for that key. **Default:** yes.
8. **Stall warning** goes to the server log and bug report only. **Default:** log only.
9. **Contention** runs in the regular lane-3 soak (weight 15); open gaps are reported as "known gap". **Default:** yes.
10. **Player messages:** "<name> used this part.", "<name> is sitting there.", "This order is no longer available.",
    "Try again in a moment." **Default:** these texts.

## Open — row 18 part-locks (2026-10-07)

Details and reasons in `openspec/changes/part-locks/proposal.md` (reviewed, see `review.md`).

1. **Sibling parts** (two caps on one crankshaft) can be worked on at the same time. **Default:** yes.
2. **Idle holder:** the holder's own game cancels after 60 s in the item chooser without a choice, or 5 min in the
   bolt view without progress. **Default:** yes; the server expiry only covers crashes.
3. **Engine-stand parts** stay on today's path. **Default:** yes, follow-up if the next playtest shows races.
4. **Welder, paint, wash, detailing** take no lock. **Default:** no lock.
5. **Hover look** of a part in use: no highlight plus the label. **Default:** that.
6. **Message** for a connected part: "<name> is working on the <part name>". **Default:** that.
7. **Split:** this change ships the locks, the click-time refusal and the hover label; mount-mode previews, item
   chooser filtering and pie greying move to `part-locks-2` (clicks there are still refused). **Default:** split.

## Playtest findings (2026-10-07)

Not questions for the user; open bugs from the first playtest, fixed one by one.

1. **Ghost stuck when the lift moves (row 17):** a remote mount's ghost stayed at its world position after the lift
   moved, and the real caliper stayed hidden (`forceRenderingOff`) until the scene was reloaded. Not reproduced by `visual-lift` (fast mount and unscrew while the lift moves, held ghost: no ghost or hidden renderer left, 2026-10-07); the playtest mount ran with bolts. Row 18 `part-locks` D7 (no lift or move while another player holds a lock on the car) removes the situation. **Fixed by row 18** (`change/part-locks`): the lift is refused while a friend works on the car (`visual-lift`, `locks-car`).
2. **Server keeps running** after the host returned to the main menu (the user typed `/exit`). By design (session-hosting spec: leaving keeps hosting; Stop on the Host tab or quitting the game stops it); my advice to "go to the main menu" was wrong. Idea: a main-menu notice "your server is still running".
3. **Persistent car desync** on wheels: `s:3.22.4.tunedId` empty on the host vs `tire_sport` on the server.
   **Fixed** (`c369763`): the game writes a tire's tuned id as empty or equal to its id, and TunePart also changes a
   rim's or tire's id; digests and the apply now compare the effective id, and the car-details wheel apply keeps the
   rim and tire ids. `car-wheel-swap` (own wheel, then a new rim and tire type) proves it.
4. **Rollback after a rejected mount** (same item taken by both players) left the loser's inventory out of sync
   until F7. **Fixed** (`f9557c0`): the rejection gave the loser back the item the other player had mounted; it now
   restores only items the server still has. `car-mount-race` proves it. Row 18 removes the race itself: the item is
   locked when it is picked, so the second player is refused before mounting (`locks-race`).
5. **F7 on a friend's client** logs `Error in handler WorldState: Object reference not set`. **Fixed** (`c81fd05`):
   UIManager is missing while the garage reloads; the refresh is skipped then. `resync-key` sends money changes
   during the reload.
6. **Car state "not ready"** in the host's bug report while the car was being worked on (`be9b`). **Expected:** the
   car digest is left out while any claim, open transaction or unsent change exists on the car (`ClientDigests.Car`),
   which is almost always the case during shared work. Row 18 (locks) should keep car digests running while locks
   are held. **Expected:** the car digest is left out while any claim, open transaction or unsent change exists on the car (`ClientDigests.Car`), which is almost always during shared work. Row 18 (locks) should keep digests running with locks held.
7. **Version check too strict for a playtest:** **Fixed** (this commit: same version and build kind, e.g. `dev.892` and `dev.894`, join when the protocol hash matches; releases must match exactly; `compat-refusal` proves both).   every commit changes `dev.N`, so friends must reinstall for
   server-only fixes. Proposal: compare the base version and the protocol hash, not the build number.

## Open — row 17 remote visual feedback (2026-10-07)

1. **Collisions** between players' cars and walking players while driving. **Default:** none (others' cars pass
   through).
2. **Seeing a test drive** only when you are at the test track too. **Default:** yes, no spectator view from the
   garage.
3. **Real animation clips** for the remote avatar need the Unity project behind `playermodel.bundle`. Do you have it?
   **Default:** a simple procedural work pose.
4. **Remote visuals** (parts moving, bolts turning) on by default, with a local off switch. **Default:** yes.
5. **Driving** as part 2 of this change (merged separately) or a change of its own. **Default:** part 2.
6. **A garage drive** that ends somewhere other than a car place. **Default:** the car goes back to its place.
   *Moot since spike 8.1: the game has no driving inside the garage.*
7. **The pie option "Drive" (`car_drive`)** only opens the map (spike 8.1, 2026-10-07); there is no driving inside
   the garage, so garage driving (group 11) is dropped. Allow the option in multiplayer as a map shortcut? A test
   track trip from that map goes through row 13's claim as usual. **Default:** it stays blocked (`Planned`) until
   you say yes.
8. **Another player's car on the test track appears after about 10 s** (7 s when you are already there), not the
   2 s the spec asks: loading another car takes about 6.5 s, and it waits 3 s after your own car is ready (starting
   earlier froze the game). **Default:** accept it for 1.1; a faster way (keeping the copy between drives, loading it
   before you arrive) would be a follow-up. *Row 31 (2026-10-10): the 6.5 s predated the freeze fix; with the wait
   cut to 0.5 s the car shows ~0.7 s after arrival.*

## Accepted defaults (user, 2026-10-06)

- Remote players see only the finished part state, not the bolt animation, while a part is reserved.
- When two players edit the same car-detail section at once, the last write wins.
- Whoever spawns a car decides its random values (damage, colour…).
- A joining player's own local-save cars are dropped while connected; the server's cars are shown.
- Order generation runs on one elected client (lowest player id) because the server has no game code.
- Payout/XP reported by the finishing client is trusted (with bounds checks).
- Steam achievements/stats for a finished job go to every connected player who worked on it (row 30, user decision
  2026-10-08; the host can choose `job_stats_to = garage` or `finisher`).
- Barn: other players are hidden (its layout differs per visit).
- A seated player's avatar is shown in the seat (replaced the earlier "hidden instead of posed" by row 29,
  `seated-avatars`, user wish 2026-10-08).
- Player name comes from a mod setting (no in-game UI yet).
- Engine stand 2 is synced like stand 1; disabled while connected if that fails.
- While connected, taking a car out from the separate Parking scene is blocked (garage parking works).
- No automatic reconnect after a server restart; a server that cannot load its save refuses to start.

## Decide later

Row 15 `shared-outdoor-scenes` (drafted 2026-10-07, M7; design.md "Open Questions"). Each has a default the
draft works with:

1. **Asking LvxMagick** for permission to adapt LvxBetterCarSpawns' car selection (and how to credit them) is your
   step. Default: not asked yet; the server uses its own simple selector (`BasicCarSelector`: unique models per
   visit, no repeat of the last visit) and the Lvx-based selector (task 11.2) waits for a yes.
2. **One junkyard (barn, auction) at a time:** everyone who travels there joins the open one; it closes when the
   last player leaves (60 s grace after a crash), and the next trip gets a new one, as in the game. Default: yes.
3. **Joining a friend in an open barn** uses no barn from the shared barn count; the barn travel fee is still charged
   per trip (server rule `travel_fees`). Default: yes.
4. **Auction:** money is shared, so the players bid as one team; one player runs the bidding on a lot, the others
   see it and can raise the team's bid (view-only if the game's bid cannot be driven from outside). Default: yes.
5. **Items taken but not paid for** go back to their pile when the player leaves or disconnects. Default: yes.
6. **Fill every junkyard spawn point** (what your single-player mod does): off by default (vanilla car count), one
   server setting (`outdoor_fill_all_spawn_points`) turns it on.

Row 30 `shared-job-achievements` (built 2026-10-09):

7. **Garage upgrade achievements** (`stat_full_garage`, `stat_unlock_allupgrade`, from
   `UpgradeSystem.CheckForAchievements`): upgrades are shared, but only the buyer's game runs that check. Default: out
   of scope, only the buyer gets them. **Answered 2026-10-10:** everyone gets every achievement that follows from shared save state. Built on `feat/shared-achievements` (`docs/spikes/shared-achievements.md`).

Shared achievements (2026-10-10):

8. **Event stats on shared things** (`stat_buy_carsalon`/`_carjunkyard`/`_barn`, `stat_sell_car`, `stat_sell_fix_car`,
   `stat_win_car`, `stat_scrap`, `stat_buy_parts`, `stat_sell_junk`): they count a purchase, sale or scrap event, not a
   state another client can check, so sharing them needs a server award (a new packet). **Default:** the player who
   acted keeps them; say if cars bought/sold should go to everyone and the packet gets built.

## Answered
- **Answered by the user on 2026-10-10 (row 31 scope):** B. The fixed wait after the own track car is ready goes from
  3 s to 0.5 s (another player's car shows ~0.7 s after arrival); no spreading of the game's `LoadCar` over frames, so
  the first copy's one-frame load (85–130 ms headless with lane 1 busy) stays as it was.

Answered by the user on 2026-10-08 (evening):

- **Row 16 scope:** no full port and no decompiled game logic on the server. Kept: seeded job cars (extra tasks,
  drained fluids and plate may differ on a retake) and the server-owned order clock and limit (design first,
  reviewed before it is built). The elected client keeps running the game's generator; payout and XP stay
  client-reported with bounds. The server-hosted generator client stays an idea for after the release.
- **Blocked features to open in multiplayer:** race track (base-game tracks only, DLC tracks stay blocked), tuning
  window, bonus parts, building new engines, the showroom, garage customization (everything visual). Save/load,
  benchmark, tutorial, photo location and changing garages stay blocked. Drafts on `change/singleplayer-features`.
- **Rows 25–31 (evening):** the user meant the car salon, not the Showroom viewer; the salon change is cut to a
  purchase proof and guard clean-up. Race track and speed track both come (row 27a); races with a shared start only on
  the race track (row 27b); collisions between players' cars on the tracks are wanted, cheap form after a spike (row
  27c); the long wait before another player's car appears is to be fixed (row 31); seated avatars in the garage (row
  29) and job achievements for everyone who worked on the job (row 30) are wanted. No in-game "save now" button, no
  automatic reconnect, no text chat.
- **Row 27b:** a race is started with a "Start race" button in the F9 session panel, shown only on the race track
  (not the game's own restart, which stays a personal restart). **Row 31:** about 3 s until another player's car
  appears is fine, as long as loading it gives no big frame spike (no frame over 0.13 s, as in the freeze fix).
- **Row 31 (2026-10-10):** option B — shorten the fixed wait to 0.5 s after your own car is ready (≈1 s); no
  spreading of the game's load steps. **Row 27c:** no per-player collision setting; only the host's
  `track_collisions` switch turns collisions off, for everyone. **Row 27b:** look for marked start spots on the race
  track so racers can start on different spots.
- **Row 27c (2026-10-10, later):** the lower push-out speed while touching another car's copy is fine (kept).
  **Achievements:** every achievement and Steam stat that follows from shared save state (upgrades, skills, level,
  money, cars, garage, and so on) is earned by every player, not only the one who triggered it.


- **Answered by the user on 2026-10-08 (row 19 part 1):** the defaults of "Open — row 19" stand; the part record base check also compares the part's quality (a part replaced by one of another quality never takes a stale edit), as recommended.
- **Answered by the user on 2026-10-08:** no collisions between driven cars for now (row 17 part 2 keeps cars passing through each other; *superseded the same evening: collisions on the tracks are wanted, row 27c, spike first*); ride-along (row 21) is wanted. Ping (row 22) is wanted; a shared shopping list (row 23) is wanted; row 17 #8 (other car appears after ~10 s) is fine for now (*later the same evening: to be fixed at some point, row 31*); no Unity project for playermodel.bundle (#3 stays the procedural pose); asking LvxMagick maybe later; the 1.0 release waits; the Drive pie option may open the map (merged 2026-10-08); the LvxBetterCarSpawns-based car selector (row 15 group 11) is dropped: the mod was hidden by its author on 2026-09-11 for permission issues (it spawned DLC cars players did not own); the server keeps its own selector, which limits DLC cars to the DLC every connected player owns; task split in the job view, more orders for more players, gestures and an end-of-session scoreboard are not.
- **Answered by the user on 2026-10-07 (late evening), row 19 ledger:** S1 (two players in one seat) is fixed in
  row 19 (server arbitrates, the second is refused and leaves the seat). For I3, I7, E4, M8, C4, J2, J4, J5 the
  server's choice stays, but every refused, ignored or overridden action must reach the acting client with the
  authoritative result, so the client whose action was cancelled rolls back and never stays out of sync (no silent
  drops). The race and drift audit's rows are all either covered by rows 18/19, a later hardening change (I6, E5,
  C1, C5) or this rule.
- **Answered by the user on 2026-10-07 (evening):** an agent reviews every complicated OpenSpec change before it is
  implemented; races between players get a soak contention mode after row 18.
- **Answered by the user on 2026-10-07 (afternoon):** release questions 1–4 take the defaults (no tags for past milestones; 0.6.0 → 1.0.0 in the release commit; bug reports as GitHub issues on the fork; the new README). The Steam playtest will probably be after the autumn holiday. Mod lists: only the user's own install for now (the friends just bought the game). No need to open row 11's proposal/design. Row 11: budgets and headless C/D take the defaults; the 4-hour soak runs on 2026-10-08 while the user is at school (start after 08:00 once the PC is idle; the PC and this Claude Code session must be running).
- **Two lanes and the memory guard (2026-10-06, 23:55):** (b). The user wants parallel lanes again; `CLAUDE_CODE_DISABLE_BG_SHELL_PRESSURE_REAP=1` is in `~/.claude/settings.json` (works after a restart of Claude Code). The second lane only starts with at least 10 GB free RAM. Also: test less (full regression only for client/server changes) and reuse the running games across scenarios (batch mode).
Answered by the user on 2026-10-06 ("4. server rule, andre questions default"):

1. **Moving a car onto an occupied place:** a swap request; the server swaps both cars in one change.
2. **Removing an unloadable car from parking:** blocked while connected, with a message.
3. **Money or scrap changes no game feature claims:** dropped and logged; the scenarios' counter must stay 0.
4. **Travel fees:** one server rule (server config), not each player's own game setting.
5. **Fees charged after the fact** can take money down to 0 and are never refused, as in the game.
6. **Selling a car** is refused while another player works on it, while it is away, or if it is a job car.
7. **Prices the game computes on the client** are trusted within bounds until row 16.
8. **The barn count** is shared.
9. **Skill reset:** any player can reset the shared skills; the others get a message.
10. **The drag strip** and the map's "measure power" stay blocked.
11. **Server-hosted generator client:** (a), an opt-in `generator_client = true` server feature that becomes the order
    generator, built after row 3 works with players.
12. **How row 10 syncs money:** C, the mix (server-computed where it can, range-checked client values otherwise).
13. The "smaller, defaults probably fine" list stands as written.

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
- Steam achievements/stats for a finished job: ALL connected players get them (2026-10-05; replaced on 2026-10-08 by
  "everyone who worked on the job", row 30).
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
