# Proposal

## Why

The user wants to scavenge together (QUESTIONS.md, 2026-10-05; ROADMAP row 15, M7). Today two players who travel
to the junkyard at the same time are in two different junkyards: each game rolls its own cars, its own junk piles
and its own barn layout (`sync-players-and-scenes`' requirement "Scenes outside the garage are not shared"). A
player sees the other's avatar walk through cars that are not there, a part one player takes is still in the
other's pile, a car one player bought stays for sale in the other's yard, and the barn hides avatars because its
layout differs per visit. Money, inventory and the parking are already shared (rows 6 and 10), so the only thing
that is not shared is the place itself.

The static spike (`docs/spikes/outdoor-scenes.md`) shows what decides that content: the car models come from
`Helper.GetRandomCars(CarBundleLoader.GetCarsForScene(..))`, everything else (spawn positions, car appearance, pile
items, the barn's procedural geometry, auction lots) from the global, unseeded `UnityEngine.Random`, the player's
DLC and the owned-car list. Hooks for each step exist and fire. LvxBetterCarSpawns (LvxMagick), which the user
plays with in single player, replaces the car choice with a weighted, history-aware pick; the user wants that
behaviour on the server, but adapting its code needs the author's permission, which the user has not asked for yet.

## What Changes

- **One instance per outdoor scene.** The server keeps at most one live instance each of the junkyard, the barn and
  the auction. Every player who travels there joins the open instance; the instance closes when its last player
  leaves (a short grace period covers a crash and rejoin). The next visit after that is a new junkyard, barn or
  auction, as in the game.
- **The server decides what is there.** On entry the server sends the instance: a visit seed, the ordered car list
  chosen by the server, sold cars, the pile record and taken items, the auction lots and their state. Car selection
  runs on the server behind `ICarSelector`; the default `BasicCarSelector` is this mod's own code (unique picks from
  the scene's car catalog, with a short no-repeat history), so nothing depends on LvxBetterCarSpawns. An
  `LvxCarSelector` that adapts Lvx's weighting is an optional later task, gated on the author's permission.
- **The car catalog comes from the clients.** The server has no game data for cars. Every client reports
  `CarBundleLoader.GetCarsForScene` for junkyard, barn and auction once per session; the server selects only from
  cars every connected player can load (this also keeps DLC cars inside row 9's shared DLC set).
- **Clients load exactly the server's cars.** Hooks replace the car argument of `JunkyardGenerator.CreateCar` and
  `ShedManager.CreateCar` and the list of `AuctionManager.GenerateCars`. Appearance (colour, conditions, missing
  panels, `negotationMod`), spawn positions and the barn geometry are made equal by reseeding `UnityEngine.Random`
  from the visit seed in each generator step. A digest of what each client generated is compared on the server.
- **Junk piles are recorded and replayed.** The first player in an instance (the instance's generator) uploads every
  pile's items and the junkyard's `RandomShow` set; later players overwrite their piles with that record. Taking an
  item from a pile goes through the server: the first taker gets it, it disappears from everyone's pile, and a
  refused take is undone locally. Items put back, removed in the taken-items window, or still unpaid when their taker
  leaves or disconnects return to their pile for everyone. Buying parts keeps the `ItemsExchange` path, now checked
  against the items the buyer holds.
- **Cars bought there disappear for everyone.** A purchase keeps row 6's path (`CarParkRequest` with
  `CarLoaderID = -1`) and names the instance car it buys; the server refuses a car already sold (`Taken`) and tells the
  other players in the scene to remove it.
- **Shared barn.** All players in a barn see the same layout and each other (`ShowsAvatars(Barn)` becomes true).
  Joining an open barn uses no barn from the shared barn count (row 10); only opening a new barn does.
- **Shared auction.** Everyone in the auction sees the same lots (car, version, seed, rating, value, starting price).
  Money is shared, so the players are one bidder: one player at a time runs the bidding on a lot (lot claim on the
  server, vanilla AI bidders in that player's game), the others see its current bid, leader and time left, and can
  raise the team's bid through the server. A won lot becomes a row 6 purchase; a lot won or lost is closed for
  everyone.
- **Fallback.** Server config `shared_outdoor_scenes` (default `junkyard,barn,auction`) lists the shared scenes; a
  scene not listed behaves as today (local world, barn avatars hidden). A client that does not get its instance within
  15 s generates locally and is shown as "not shared" in that visit.

Hooks (all fire per the static spike; group 1 confirms at runtime): `JunkyardGenerator.Generate` (builder, hold and
`CarsPercentage`), `JunkyardGenerator.CreateCar` (prefix, `ref CarsIdWithConfig`), `JunkyardGenerator.<Generate>d__18`,
`<CreateCar>d__19` `MoveNext` (reseed prefix/postfix), `ShedManager.Generate`, `ShedManager.CreateCar`,
`ShedManager.<Generate>d__29`, `<CreateCar>d__30` `MoveNext`, `AuctionManager.GenerateCars(AuctionType)` (prefix
returning false), `AuctionManager.LoadCar` (lot identity), `AuctionBidding` bid and finish methods (per spike 1.3),
`NotificationCenter.MoveItem(Item, bool, string)` and its `GroupItem` overload, `ItemsExchangeWindow.Show`,
`TakenItemsWindow.RemoveCurrentItemAction`/`QuitWithoutPartsAction`, the existing `TakenItemsWindow.BuyPartsAction`,
`GameScript.BuyCar` and `AuctionBidding.ReceiveCarAction` captures of `CarPurchaseSync`, and `ClientScene.LeavingScene`.

Packets: **new** `OutdoorCatalog`, `OutdoorEnter`, `OutdoorInstance`, `OutdoorLootRecord`, `OutdoorDigest`,
`LootTake`, `LootPutBack`, `LootUpdate`, `LootTakeRefused`, `OutdoorCarRemoved`, `AuctionLotClaim`,
`AuctionBidState`, `AuctionBidRequest`, `AuctionLotClosed`; **changed (additive, `[OptionalField]`)**
`CarParkRequest` (+ `SourceInstanceId`, `SourceCarIndex`, `SourceLot`), `ItemsExchangePacket` (+ `InstanceId`);
`ParkRefusal.Taken` is reused.

## Capabilities

### New Capabilities
- `outdoor-scene-instances`: one shared instance per outdoor scene, who decides its content, entering, leaving,
  returning and disconnecting, the car catalog and server-side car selection (including the optional Lvx selector),
  identical cars and barn layout for everyone, avatars in shared scenes, the fallback when sharing is off.
- `outdoor-loot-and-purchases`: junk piles recorded and replayed, taking and putting back items through the server,
  buying parts and cars while other players are in the scene.
- `shared-auction`: shared lots, one team bid per lot, watching and raising another player's bidding, winning and
  losing a lot.

### Modified Capabilities
<!-- none: openspec/specs/ is empty. This change supersedes two requirements of `sync-players-and-scenes`' (not yet
archived) `scene-travel`: "Scenes outside the garage are not shared" and the barn rule of "Visibility per scene"
(task 9.4 turns them into MODIFIED deltas if row 6 is archived first). -->

## Impact

- Core: `Network/Packets/OutdoorPackets.cs` (new), `PacketTypes` (appended), `Data/Outdoor/` (DTOs: `OutdoorCarPick`,
  `LootPile`, `LootItemState`, `AuctionLot`, `AuctionLotState`, `OutdoorDigestRows`), `GameSceneInfo.ShowsAvatars`
  (barn shown when the scene is shared), `CarParkRequestPacket` and `ItemsExchangePacket` (optional fields).
- Server: new `Data/Outdoor/` (`OutdoorInstances`, `OutdoorInstance`, `CarCatalog`, `ICarSelector`,
  `BasicCarSelector`, `SelectionHistory`, `LootService`, `AuctionService`, `OutdoorSection` save section for the
  history), `Network/Handlers/OutdoorHandlers.cs`, `ParkingHandlers` (`-1` branch checks and marks the source car),
  `ShopHandlers.HandleItemsExchange` (held items only), row 10's `EconomyRules` (barn count when joining an open barn),
  `PresenceRegistry` (relay filter by instance), `ServerConfig` (`shared_outdoor_scenes`, `car_selector`,
  `outdoor_rejoin_grace_seconds`, `outdoor_fill_all_spawn_points`), server command `outdoor`.
- Client: new `Logic/Outdoor/` (`OutdoorSession`, `CatalogReporter`, `GeneratorHooks`, `Reseed`, `LootSync`,
  `OutdoorCarSync`, `AuctionSync`, `OutdoorDigest`), `Logic/Economy/CarPurchaseSync.cs` (source car/lot),
  `Logic/Hook/TakenItemsWindowHook.cs` (instance id, held items), `Logic/Player/PresenceManager.cs` and
  `SceneReady.cs` (barn avatars, scene-ready after the instance is applied), `Guard/GuardRules.cs`.
- Test harness: `Features/OutdoorCommands.cs`, `StateDump` section `outdoor`; scenarios `outdoor-trace` (spike),
  `outdoor-junkyard`, `outdoor-barn`, `outdoor-auction`, `outdoor-return`, `outdoor-latejoin`, `outdoor-scale` (lane 3).
- Docs: `docs/spikes/outdoor-runtime.md`, README "Playing together", `docs/playtest.md` hand checks; credit for
  LvxMagick only if the Lvx selector lands.
- Depends on (merged): row 6 (presence, `ClientScene`, scene hooks, `CarPurchaseSync`), row 2 (`CarParkRequest`,
  `ParkRefusal.Taken`), row 7 (`StateLock`, `ISaveSection`), row 9 (`SharedDlc`, Lvx refused as a gameplay mod), row 10
  (travel fees under `travel_fees`, barn count, `EconomyScope`), row 11 (lane 3), row 14a (guard).
