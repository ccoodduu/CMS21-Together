# Design

## Context

See proposal.md for the why. Facts that shape the approach, from `docs/spikes/outdoor-scenes.md` (static decompile,
2026-10-06), `docs/spikes/economy-paths.md`, a read of the decompiled LvxBetterCarSpawns 1.0.0 (behaviour only, no
code taken) and the code on `main` (`bd3415f`):

- **What makes an outdoor scene.** Junkyard (`JunkyardGenerator.<Generate>d__18`): car count
  `clamp(FloorToInt(CarSpawnPositions.Length * Random.Range(CarsPercentage) / 100), 3, Length)`, car models from
  `Helper.GetRandomCars(CarBundleLoader.GetCarsForScene(1), n)` plus a DLC roll, a random free position per car, then
  `<CreateCar>d__19` rolls colour, fluids, missing panels, conditions, rust, dents, dust, `negotationMod`, mileage and
  plates over several frames; then every type-8 `InteractiveObject` gets a `Junk` with `Junk.AddRandomItems`, a random
  `JunkyardRandomShow` subset is switched on, and one pile gets the barn map. Barn (`ShedManager.<Generate>d__29`): a
  procedural shed (random group, floor, walls, props, helper group, spawn point), piles via `ShedManager.AddItems`,
  three cars via `CreateCar`, one loot case. Auction: `AuctionManager.GenerateCars(AuctionType)` builds
  `AuctionCarData { Car, Version, Seed, Rating, Value, StartingPrice }` lazily per auction type; `AuctionManager.LoadCar`
  calls `Random.InitState(Seed)` before it builds the car, so a lot list reproduces its cars.
- **Randomness:** only the global, unseeded `UnityEngine.Random`, plus the player's DLC (`CanGenerateDLCCar`), the
  owned-car list (`GameInventory.GetRandomItemForOwnedCars`, 10 % of good pile items) and the installed car list.
- **Hooks that fire:** `JunkyardGenerator.Generate`/`CreateCar`, `ShedManager.Generate`/`CreateCar`/`AddItems`,
  `Junk.AddRandomItems`, `AuctionManager.GenerateCars`/`LoadCar`, `NotificationCenter.MoveItem` (both overloads),
  `ItemsExchangeWindow.Show` (virtual), `TakenItemsWindow.BuyPartsAction`, `AuctionBidding.ReceiveCarAction`.
  Inlined (never hook): `TempInventory.AddItem`, `AuctionManager.Generate`/`LoadNormalCar`/`LoadSalvageCar`, the salon
  builders. Iterator `MoveNext` methods can be patched (row 6 does it for `NotificationCenter._BuyCar_d__21`).
- **Items on the way home:** `MoveItem` takes an item from `junk.ItemsInTrash` into `TempInventory` and the scene-local
  `Inventory`; the garage load appends `TempInventory`. `TakenItemsWindowHook` already sends `ItemsExchange` and
  clears `TempInventory`; the server prices the items (0.47 junkyard, 0.50 barn, shop discount) and adds them.
  `FoundTab.RefreshItems` sorts `ItemsInTrash` in place, so items must be keyed by UID, not by list index.
- **Purchases on `main`** (`CarPurchaseSync`, row 6 group 6): `GameScript.BuyCar` and `AuctionBidding.ReceiveCarAction`
  open a capture, the location window can only pick the parking, the car is encoded after `CarLoader.SaveCarToFile`
  and sent as `CarParkRequest { CarLoaderID = -1, Price }`; the server checks money and space (`ParkRefusal`
  `NoMoney`, `ParkingFull`, `Taken`, ...). Travel fees and the shared barn count are row 10's
  (`TravelFee(Barn)` with `Arg2 = 1`, server rule `travel_fees`).
- **Presence on `main`:** `PresenceRegistry` stores each player's `GameScene`; movement is relayed only within one
  scene and only where `GameSceneInfo.ShowsAvatars` is true (false for Barn). `SceneHooks.Leave` raises
  `ClientScene.LeavingScene(from, to)` before the scene changes; `SceneReady` publishes the new scene once
  `NotificationCenter.IsGameReady`. A client never joins a session outside the garage: connecting loads the garage.
- **LvxBetterCarSpawns 1.0.0** (author LvxMagick; no licence file, author only in `MelonInfo`): prefixes on
  `JunkyardGenerator.Generate` (sets `CarsPercentage = (100,100)` and pre-selects one candidate per spawn point),
  `JunkyardGenerator.CreateCar` and `ShedManager.CreateCar` (replace the `ref CarsIdWithConfig` argument),
  `ShedManager.Generate` (writes `randomCars`, which the game's state 2 most likely overwrites), `AuctionManager
  .GenerateCars` (returns its own list with a random `Seed` and a synthetic `Value`), `ItemsExchangeWindow.Show`
  (optionally injects the barn map or loot case). Its selector builds candidates from `CarBundleLoader.CarNamesData`
  and `CarIsAvailableOnScene` per config, and weights each config by its `rarity` INI key, how often it spawned
  (per location and globally) and whether it or its model is in the recent lists, with no repeated model in one
  visit; the history is a JSON file per game install. Row 9 refuses the mod in sessions (`ModClassifierRules`), so
  it never runs next to this change.

## Goals / Non-Goals

**Goals:**
- Players in the same junkyard, barn or auction see the same cars, the same piles, the same barn layout, the same
  auction lots and bids, and each other.
- The server decides what each instance contains; a client never trusts its own roll for shared content.
- Taking, putting back and buying are arbitrated once by the server; nothing is duplicated or lost when two players
  act at the same moment, leave or crash.
- Car selection is replaceable (`ICarSelector`) and the default is our own code.

**Non-Goals:**
- The salon (dealer) and its configurator: new cars, model and colour only; it stays local and its purchases keep
  row 6's path. The parking scene stays blocked (row 2).
- Porting the generators to the server (row 16): cars and piles are still built by the game on each client.
- Driving outdoor cars (not possible in the game: junkyard and barn cars cannot be dismantled or driven), physics
  props, opened doors or the junkyard crane.
- Bidding players against each other: money is shared (QUESTIONS.md 2026-10-05), so the players are one bidder.
- Persisting an instance across a server restart: instances are runtime state.
- Adapting Lvx's code before the author has agreed (task 11.1 is the user's).

## Decisions

### D1. One instance per outdoor scene, open while someone is there
`OutdoorInstances` on the server holds at most one `OutdoorInstance` per shared scene (`Junkyard`, `Barn`,
`Auction`): `InstanceId` (increasing), `Scene`, `Seed` (int from the server's `System.Random`), `Picks` (ordered car
list, D3), `GeneratorId` (D5), `Members`, `Sold` (car indices), `Loot` (D6), `Lots` (D8), `Digest` (D4),
`OpenedUtc`.
- A player enters when its travel starts (D2); it leaves when `PresenceEvents.SceneChanged` moves it out of the scene
  or `PresenceEvents.Left` fires. The instance closes when it has no members, except that after a `Left` it stays for
  `outdoor_rejoin_grace_seconds` (default 60) so a crashed player who reconnects and travels back finds the same yard.
- The next visit after a close is a new instance with a new seed: vanilla regenerates every visit too.
- *Why one per scene and not one per group of players:* players who travel to "the junkyard" expect to meet; a second
  instance would need a choice in the map UI, which the game does not have.
- *Alternative:* keep the junkyard for an in-game day — rejected, it changes vanilla's reroll-per-visit economy and
  leaves state on the server nobody sees.

### D2. Entering: the instance arrives before the generator runs
1. `ClientScene.LeavingScene(from, to)` with `to` in the server's shared set (from `ServerInfo`): the client sends
   `OutdoorEnter { Scene }` on the reliable channel, before the scene load.
2. Server (under `StateLock`): open or join the instance, add the member, answer `OutdoorInstance` (all of D1's fields
   the client needs: id, seed, picks, sold, generator flag, loot record and item states, lots and lot states,
   `FillAllSpawnPoints`).
3. Client: the generator entry (`JunkyardGenerator.Generate`, `ShedManager.Generate`; auction lists are lazy, D8) is
   held: the prefix returns false while no instance for this scene has arrived, starts a coroutine that waits for it
   (15 s), then calls the builder again with a bypass flag. On timeout the visit is local (`OutdoorSession.Local`):
   vanilla generation, no loot or car sync, avatars hidden as today, one warning and a toast.
4. `SceneReady` publishes the scene only after the instance is applied (cars created, piles replayed), so another
   player never sees an avatar in a yard that is still being built.
- No snapshot slot: outdoor state is not part of the garage snapshot and is only sent on entry. A client joining the
  session lands in the garage and gets an instance when it travels.
- Avatars: `GameSceneInfo.ShowsAvatars(Barn)` becomes true when the barn is shared; the server's movement relay
  additionally requires the same `InstanceId` (a `Local` visit has none), so a local barn never shows an avatar.

### D3. Car catalog and selection on the server
- **Catalog:** after its initial sync, each client sends `OutdoorCatalog { Scenes: { Junkyard, Barn, AuctionNormal,
  AuctionSalvage → [CarId, ConfigVersion, Dlc, Rarity] } }`, read from `CarBundleLoader.GetCarsForScene(1/2/5)`
  (salvage uses the junkyard list, as vanilla) and the config's `rarity` INI key. `CarCatalog` keeps the
  intersection over the connected clients, recomputed on join and leave. A pick is only made from that intersection,
  so every player can load every car, and DLC cars stay inside row 9's `SharedDlc`.
- **Interface:** `ICarSelector.Select(SelectionRequest { Scene, Count, Candidates, History, Random })` →
  `IReadOnlyList<OutdoorCarPick { CarId, ConfigVersion }>`. `car_selector` (server config) picks the implementation;
  unknown names fall back to `basic` with a warning.
- **`BasicCarSelector` (default, our own):** unique models per visit; candidates drawn uniformly; a model picked in the
  last visit of that scene is skipped while at least `Count` other models remain. `SelectionHistory` keeps the last
  three visits per scene and is saved in section `outdoor` (D12). This is vanilla's `Helper.GetRandomCars` behaviour
  plus "not the same cars as last time"; it uses no weighting from Lvx.
- **Count and order:** the server sends an ordered list longer than any visit needs (junkyard: up to 40 or the
  catalog size; barn: 3; auction: D8). The client's seeded generator (D4) computes vanilla's count and takes the first
  `n` picks; `outdoor_fill_all_spawn_points` (default false) sets `CarsPercentage = (100,100)` in the
  `JunkyardGenerator.Generate` prefix, as the user's single-player mod does.
- **DLC car roll:** `Helper.CanGenerateDLCCar` is replaced by the picks (the server may include a shared DLC car).
- **`LvxCarSelector` (optional, D13):** only after the author's permission.
- *Alternative:* the catalog from an exported `Database/cars.json` (row 9's exporter, not built yet) — kept as a later
  source for the same `CarCatalog`; the client report works now and follows installed DLC and game data exactly.

### D4. Same appearance and layout: reseed per generator step, digest to check
- **Reseed:** a prefix on each generator iterator's `MoveNext` (`JunkyardGenerator.<Generate>d__18`, `<CreateCar>d__19`,
  `ShedManager.<Generate>d__29`, `<CreateCar>d__30`) saves `Random.state` and calls
  `Random.InitState(Hash(Seed, kind, carIndex, __1__state, stepCounter))`; the postfix restores the saved state. One
  `MoveNext` step is synchronous, so frames in between (other scripts using `Random`) cannot shift the sequence.
  Positions, counts, colours, conditions, `negotationMod`, the barn's group, floor, walls, props and spawn point all
  come out equal when the inputs are equal.
- **Car model:** the `CreateCar` prefixes replace `ref CarsIdWithConfig` with `Picks[index]` (junkyard: index of the
  creation loop; barn: `i` of state 9). The barn's `randomCars` field is not touched (state 2 overwrites it).
- **Digest:** after generation each client sends `OutdoorDigest { InstanceId, Rows }` with one row per car
  (`index|carId|version|position index|colour hash|missing panel count|condition sum rounded|negotationMod`) and, for
  the barn, the layout choices (group, floor, wall and prop prefab names hashed). The server stores the generator's
  rows and compares each later digest; a mismatch is logged (`[Outdoor] digest mismatch Junkyard car 3 colour ...`)
  and counted in the `outdoor` command and the harness dump. v1 does not correct cars (a car is a scene object; the
  buyer's own copy is what reaches the parking, so a difference costs consistency, not money or items).
- *Alternative (spike option 2):* record each car as `NewCarData` and load it on later visitors — rejected for v1:
  there is no known way to build `NewCarData` without writing a profile slot, and it does not cover the barn layout.
  It stays the fallback if task 1.2 shows reseeding is not deterministic.

### D5. The generator: the instance's first player
The player who opens an instance is its `GeneratorId`. Only the generator uploads the loot record (D6) and its digest
becomes the reference. If the generator leaves before uploading, the server promotes the next member (oldest join)
and sends it `OutdoorInstance` again with `Generator = true`; it uploads its own generation. Members who arrive before
the record exists keep pile windows closed (D6) until it arrives.

### D6. Junk piles: record once, replay to later players, take through the server
- **Record:** when the generator's generation has finished (postfix of the last generator step, or
  `NotificationCenter.IsGameReady` turning true), the client collects every `Junk` under `Junkyard` and
  `JunkyardRandomShow` (barn: the `_PGPlace` piles under `ShedRoot`) in `GetComponentsInChildren<InteractiveObject>`
  order and sends `OutdoorLootRecord { InstanceId, Piles: [LootPile { Index, Key, Items: [ModItem] }], RandomShowActive }`.
  `Key` is the pile's world position rounded to 0.1 m; later players match piles by key and fall back to the index.
  Item UIDs are the generator's UIDs.
- **Replay:** a later player, after its own (seeded) generation, sets every pile's `ItemsInTrash` from the record
  (items rebuilt from `ModItem` with the recorded UID), applies `RandomShowActive`, then removes every item whose state
  is not `Available`. Unmatched piles are emptied and logged. Pile contents therefore never depend on the owned-car
  list or the DLC roll of the later player.
- **State per item** (server, `LootService`): `Available`, `Held(clientId)`, `Bought`. Key: `(InstanceId, Uid)`.
- **Take:** prefix `NotificationCenter.MoveItem(Item, toWarehouse = true, windowType)` (and the `GroupItem` overload)
  while an `ItemsExchangeWindow` is open in a shared visit: vanilla runs (optimistic), the client sends
  `LootTake { InstanceId, PileKey, Uid }`. Server: `Available` → `Held(sender)`, `LootUpdate { Uid, Held, By }` to the
  other members; not available → `LootTakeRefused { Uid, By }` to the sender, which removes the item from
  `TempInventory` (`RemoveItem`) and the scene `Inventory` (`Delete`), refreshes the window and shows "Bob took it
  first". Others remove the UID from that pile's `ItemsInTrash` and refresh an open window on it.
- **Put back:** `MoveItem(toWarehouse = false)`, `TakenItemsWindow.RemoveCurrentItemAction` and the trash button →
  `LootPutBack { Uid }` → `Available` again, `LootUpdate` with the item to every other member, who add it to the pile.
- **Leave without paying, disconnect:** on `SceneChanged`/`Left` every item `Held` by that player becomes `Available`
  and is broadcast. Items bought in the same trip are already `Bought` because `ItemsExchange` precedes the presence
  change on the same ordered stream.
- **Buy:** `TakenItemsWindowHook` adds `InstanceId`; `HandleItemsExchange` accepts only items `Held` by the sender in
  that instance (others are dropped from the purchase and logged), prices as today, marks them `Bought`, adds them with
  new UIDs. A refused purchase (not enough money) leaves them `Held` until the player leaves.
- **Barn map and loot case:** part of the record like any item (`Junk.AddSpecialMap`/`AddSpecialCase` run in the
  generator's game only).
- *Alternative:* reseed pile generation like the cars — rejected as the source of truth: piles depend on the owned-car
  list, which can differ between clients while they are away from the garage. Reseeding still runs (it costs nothing)
  so a replay usually changes nothing.

### D7. Buying a car while others are there
`CarPurchaseSync.Begin` records the instance car: junkyard/barn `SourceCarIndex` = the pick index stored on the
`CarLoader` when it was created (a `Dictionary<CarLoader, int>` in `OutdoorCarSync`), auction `SourceLot` (D8).
`CarParkRequest` carries `SourceInstanceId` and the index (`[OptionalField]`, `-1` = none). Server, `-1` branch of row 2,
before the money check: the car is already sold → `ParkRefusal.Taken`; else on accept add the index to `Sold` and
send `OutdoorCarRemoved { InstanceId, Index, By }` to the other members, whose `OutdoorCarSync` closes the car info
window if it shows that car and deletes the `CarLoader` (`DeleteCar`). A client also refuses `BuyCar` locally for a
car it already knows as sold (the race window is the round trip). A later player skips sold indices in `CreateCar`
(the prefix sets the pick but the car is deleted right after creation, so the seeded sequence stays aligned).

### D8. Shared auction
- **Lots:** on entry the server builds `Lots[type]` for normal and salvage: `Count` picks from the selector (count drawn
  from the client-reported `normalCarsAmountRange`/`salvageCarsAmountRange`, sent with the catalog) and a `Seed` per lot
  (server random). `Rating`, `Value` and `StartingPrice` need game functions (`AuctionHelper.GetRatingForCar`,
  `GetCarValue`, `GetStartingPrice`), so the generator client computes them under `Random.InitState(lot seed)` and
  uploads them with its digest; the server stores them and sends them to every member. Every client's `GenerateCars`
  prefix returns the server's list (returns false); a non-generator whose lots are not complete yet shows "preparing
  the auction" and retries. `LoadCar` then builds the same car from `Seed` on every client.
- **Lot state** (server, `AuctionService`): `Open`, `Bidding(ownerId, currentBid, leader Team|Ai, secondsLeft)`,
  `Won`, `Lost`. One owner per lot: starting the bidding (the hook found by task 1.3) sends `AuctionLotClaim`; the
  server grants it if the lot is `Open`, else the client cancels the start and shows "Ann is bidding on this car".
- **Bidding runs in the owner's game:** vanilla AI bidders and timers. The owner's client sends `AuctionBidState` every
  change and once a second; the server stores and relays it to the members, whose auction window shows the lot as
  "Ann is bidding: 12,400 (team leads), 0:14". Watching a lot that someone else bids on does not load its car on the
  watcher's stage in v1.
- **Team bid from another player:** `AuctionBidRequest { Lot }` → server (lot `Bidding`, money check against shared
  money) → owner's client → `AuctionBidding.PlayerBid` (or the method task 1.3 finds). If the bid cannot be driven
  from outside the UI, the watcher is view-only (QUESTIONS.md default).
- **End:** team wins → the owner's `ReceiveCarAction` capture sends `CarParkRequest` with `SourceLot`; accept → `Won`;
  refused (`NoMoney`, `ParkingFull`) → `Lost`. AI wins → `AuctionLotClosed { Lot, Lost }`. Owner leaves or disconnects
  while bidding → `Lost` (vanilla also loses the lot when the player walks away). Closed lots are removed from every
  member's list.
- Money moves once, at the win, through row 6/row 2's path; a bid itself moves no money (vanilla).

### D9. Barn trips and the barn count
Row 10 charges the barn travel fee and `Barns − 1` on `TravelFee(Barn)` with `Arg2 = 1`. With a shared barn: if a barn
instance is open when the request arrives, the server applies the fee (by `travel_fees`) and does **not** decrement
the barn count; the trip joins the open barn. Vanilla only offers the barn on the map while `BarnsAmount > 0`; while
a barn instance is open the client's map shows the barn even with 0 barns (prefix on the map's barn availability check,
found by task 1.2; fallback: joining needs at least one barn on the counter, which is not used). Junkyard and auction
fees stay per trip, as in the game.

### D10. What stays local
Physics props and their positions, opened doors and lids, the junkyard's background objects, the camera, sounds, the
car info window and examine view (outdoor cars cannot be dismantled), the taken-items window UI, the vanilla AI bidders
(in the bidding player's game), the stage car a watcher sees in the auction, the salon, and every visit of a scene
that is not in `shared_outdoor_scenes` or that timed out (D2).

### D11. Guard
No new window, scene or mode needs opening: the scenes, `ItemsExchange`, `CarLocationWindow` (purchase capture),
`Auction`, `start_bidding` and `car_buy` are allowed by rows 6 and 10. The merge commit only moves the owner notes of
the `Junkyard`, `Barn` and `Auction` scene entries to row 15, and the scenarios run with the guard on `Enforce`.

### D12. What the server stores vs. relays
| State | Server | Persisted |
|---|---|---|
| Instances (seed, picks, members, generator, sold cars, loot record and item states, lots and lot states, digest) | **stores** and decides | no (runtime) |
| Car catalog per scene (intersection) | **stores** (recomputed on join/leave) | no |
| Selection history (last three visits per scene) | **stores** | section `outdoor` v1 (`ISaveSection`, no snapshot slot) |
| Auction bid state (current bid, leader, time) | **relays** from the owner, keeps the last one for late entrants | no |
| Digests | **compares** and logs | no |
| Pile item take/put back/buy, car sold, lot claim | **decides** (first wins) | no |

### D13. LvxBetterCarSpawns: optional selector after permission
`LvxCarSelector : ICarSelector` would adapt Lvx's weighting (rarity, spawn counts per location and global, recent
configs and models, one model per visit) and its "fill all spawn points" default, with its history in section
`outdoor` instead of a file. It is written only after the user has asked LvxMagick and the answer allows it (task
11.1, `[user]`), with credit in README and CHANGELOG as the author asks. Until then the code must not contain Lvx's
formulas or constants; `BasicCarSelector` is independent. If permission is refused or never asked, row 15 is complete
without it.

### D14. Late arrival, leaving and returning, disconnect
- **Arriving while others are there** (the outdoor "late join"): `OutdoorInstance` carries everything: picks and seed
  (same cars, same positions), sold indices (skipped), the loot record and every non-`Available` item (removed), the
  lots with their states and the last bid state of a lot being bid on. If the record is not uploaded yet, pile windows
  stay closed with a toast until it arrives.
- **Leaving and coming back while others stay:** the instance stays open; the returning player gets the current state
  as a late arrival (its own unpaid items went back to the piles when it left).
- **Everyone leaves:** the instance closes; the next trip opens a new one.
- **Disconnect in an outdoor scene:** `Left` frees its held items and lot; the instance survives the grace period for
  it (D1). The reconnecting client lands in the garage.
- **Server restart:** instances are gone; clients in an outdoor scene are disconnected by the restart anyway.

### D15. Packets
Appended to `PacketTypes`: `OutdoorCatalog` (C→S), `OutdoorEnter` (C→S), `OutdoorInstance` (S→C),
`OutdoorLootRecord` (C→S), `OutdoorDigest` (C→S, also carries auction values from the generator), `LootTake`,
`LootPutBack` (C→S), `LootUpdate`, `LootTakeRefused` (S→C), `OutdoorCarRemoved` (S→C), `AuctionLotClaim` (both:
request and answer), `AuctionBidState` (owner → S → members), `AuctionBidRequest` (C → S → owner), `AuctionLotClosed`
(S→C). Changed, additive (`[OptionalField]`): `CarParkRequestPacket` (`SourceInstanceId`, `SourceCarIndex`,
`SourceLot`), `ItemsExchangePacket` (`InstanceId`), `ServerInfo` (`SharedOutdoorScenes`). All outdoor handlers run
under `StateLock`; `OutdoorEnter` and the loot packets need `InSession` (a client travels only after its sync).

## Risks / Trade-offs

- [Reseeding is not deterministic (other code in the same step, frame-dependent branches, different item databases)]
  → task 1.2 runs two clients against the same seed and compares the digest; piles do not depend on it (D6); the
  fallback for cars is spike option 2 or "same models, local appearance" with the digest mismatch accepted and logged.
- [Pile order or keys differ between clients] → position keys with index fallback; task 1.4 checks stability.
- [The generator hold breaks the scene loader (generator not started in time, loading screen waits)] → 15 s timeout to
  a local visit; task 1.1 measures how long the loader tolerates a held `Generate`.
- [Optimistic take then refusal confuses the player] → only in a real race (same item within one round trip); the
  toast names the other player.
- [Auction bidding cannot be driven by the mod] → view-only watchers; the lot claim still prevents two players
  bidding on the same lot.
- [A player with fewer DLC joins while an instance is open] → the catalog shrinks for new instances; in the open one,
  picks the new player cannot load are skipped for that player (logged) and cannot be bought by it.
- [Many players in one junkyard taking items at once (lane 3)] → item state is per UID under one lock; `outdoor-scale`
  measures it.
- [Licensing] → the default selector shares no code with Lvx; D13 is gated.

## Migration Plan

New save section `outdoor` (v1, history only); a save without it starts with an empty history. New packets and
optional fields need the same build on server and clients (row 9's protocol hash). `shared_outdoor_scenes =` (empty)
restores today's behaviour without a rollback. Rollback = previous build; the `outdoor` section is ignored.

## Open Questions

Decided without the user (QUESTIONS.md lists them with these defaults):
- **Q1** One instance per scene; it closes when the last player leaves (60 s grace after a disconnect); the next visit
  is a new yard, as in the game.
- **Q2** Joining an open barn does not use a barn from the shared count; the barn fee is charged per trip by the server
  rule.
- **Q3** In the auction the players are one team; another player can raise the team's bid on a lot someone else is
  bidding on (view-only if task 1.3 shows it cannot be driven).
- **Q4** Items taken but not paid for go back to their pile when the player leaves or disconnects.
- **Q5** `BasicCarSelector` (vanilla-like unique picks, no repeat of the last visit) is the default; "fill all
  spawn points" is off by default (the user's single-player mod turns it on).
- **Q6** Asking LvxMagick for permission is the user's step (outward communication); the Lvx selector waits for it.

Deferrable unknowns (answered by group 1): the hold tolerance of the scene loader; reseed determinism across two
clients; whether `GetCarValue`/`GetStartingPrice` are deterministic under a seed; the auction's bid, start and AI
methods and whether `PlayerBid` can be called from outside the UI; `MoveItem`'s `windowType`, right-click move-all, and
whether piles hold `GroupItem`s; whether a recorded UID can be set on a rebuilt `Item`; the map's barn availability
check; the amount and rating ranges of `AuctionManager` per type.
