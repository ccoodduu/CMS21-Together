# Spike: outdoor scenes, travel and purchases (row 6 part 2, row 15 `shared-outdoor-scenes`)

Static decompile only (setup in `native-decompile.md`), game not launched. Date: 2026-10-06.
Outputs: `%USERPROFILE%\CMS21-TestInstalls\native\out\outdoor_clean` and `outdoor2_clean` (targets
`work\targets\outdoor*.txt`); older files in `out\clean`, `placement*_clean`. "Fires" means `xref.py` found a direct
call/`jmp` to the method entry, or the method is a delegate/virtual target, so a Harmony detour runs. "Inlined" means
no native caller reaches the entry, so a hook on it never fires. Caller lists come from `xref.py` (E8/E9 scan); Ghidra
bodies were not trusted for call/jmp targets.

## Design-impacting findings (read first)

1. **Auction wins do not go through `GameScript.BuyCar`.** `AuctionBidding.ReceiveCarAction` (`0x180CED590`, a
   UIDescription delegate registered in `PrepareDescriptions`, fires) does `AddPlayerMoney(-currentBid)`, sets
   `CarInfoData.BuyPrice`, `stat_win_car`, then opens `CarLocationWindow` itself. D8's `BuyCar` prefix misses it; add
   a second capture opener on `ReceiveCarAction` (price = `currentBid`, field `+0xA4`). This answers D8's open question.
2. **Money leaves before the player picks garage/parking.** `GameScript.BuyCar` (`0x180E95560`) calls
   `GlobalData.AddPlayerMoney(-buyPrice)` and `GlobalData.Save()` right after showing `CarLocationWindow`; the car is
   saved only when a button of that window is pressed. The capture must survive that window (no 5 s timeout that
   starts at `BuyCar`; see recommended hooks).
3. **"Garage" is a valid vanilla destination from outside the garage.** `CarLocationWindow` button 0 = `garageHash`
   (`PositionTo = "Garage"`), button 1 = `parkingHash`. Garage → `NotificationCenter.MoveCarToGarage` →
   `CarLoader.SaveCarToFile(freeGarageIndex, false)` → `GameDataManager.SaveCar(…, false)` → `SaveCarInGarage`.
   D8 must clear a *garage* slot in that case (or force the parking button while connected).
4. **Row 2's park hook will also fire for purchases.** Buying to parking runs `MoveCarToParking` →
   `CarLoader.SaveCarToFile(slot, true)` — the exact postfix row 2 uses to send `CarParkRequest` for garage cars. Row 2's
   hook must skip when a purchase capture is open (or when `CurrentSceneType != Garage`), else the car is sent twice.
5. **Row 2's unpark hook fires in the parking scene.** `ParkingManager.<LoadCarAtPlace>d__18` *displays* a parked car
   with `CarLoader.LoadCarFromFile(saveIndex, true)`. A prefix on `LoadCarFromFile(int, true)` must check
   `CurrentSceneType == Garage`. The parking scene's real "move to garage" (`ParkingManager.<MoveCarToGarage>d__21`)
   calls `GameDataManager.LoadCarInParking` → `SaveCarInGarage(data, freeIdx)` → `SaveCarInParking(default, saveIndex)`
   directly, without `SaveCarToFile` (row 2's `ParkingWindow.MoveCarToGarageAction` hook is the right one).
6. **Picked-up parts never pass `Inventory.Add` on the way home.** In a pile window, `NotificationCenter.MoveItem`
   adds the item to `TempInventory.items` (inlined `List.Add`) *and* calls `Inventory.Add(item, false)`. On return,
   `GarageLoader.<Load>d__14` first reloads `Inventory` from the profile (dropping the scene-local adds), then appends
   every `TempInventory` item to `Inventory.items` with an inlined `List<Item>.Add` and clears `TempInventory`. Parts
   bought in the junkyard/barn are therefore only visible to a hook on `TakenItemsWindow.BuyPartsAction`
   (what `TakenItemsWindowHook` already does) or on `MoveItem`.
7. **Junkyard and barn cars cannot be dismantled.** Both generators set `NotificationCenter.CanMount = CanUnmount =
   false`, so the only item source there is the junk-pile window.
8. **The auction car look is already seeded.** `AuctionManager.LoadCar` calls `Random.InitState(auctionCarData.Seed)`
   before building the car. Sharing the `AuctionCarData` list (Car, Version, Rating, Seed, Value, StartingPrice) is
   enough to reproduce the auction cars, modulo frame interleaving (runtime check 3).
9. **`GenerateCars`' sort is random.** `<GenerateCars>b__47_0` compares `ConfigVersion` against two
   `Random.Range(0,100)` rolls, so the vanilla order is effectively random; vanilla `Seed` is just the list index.

## Scene types and travel

`SceneType`: None 0, Junkyard 1, Barn 2, Showroom 3 (menu car viewer, not a shop), Garage 4, Auction 5, …, Salon 8
(the dealer), Parking 9, Menu 10.

Fees: `MapWindow.SubmitPanelAction` (`0x180D0D650`), only with `GameSettings.TravelHaveCost` and not Sandbox:
`AddPlayerMoney(-200)` auction, `-500` junkyard, `-100` barn plus `GlobalData.BarnsAmount--` (barn maps). Salon,
parking and tracks are free. Every fee is a direct `AddPlayerMoney` call (fires), owned by row 10.

What leaving a scene saves (`NotificationCenter.<SelectSceneToLoad>d__34`, `0x180A6E5C0`):

- from Junkyard/Barn: `ShopListWindow.Save()` and `GameDataManager.Save(profile)` **always** (even with
  `saveGame = false`);
- if `saveGame`: `GlobalData.Save()` unless the source is None/Showroom/Menu; from Parking also
  `GameDataManager.Save(profile)`; `GarageLoader.Save()` only if a `GarageLoader` exists (garage only).
- `Inventory.Save` is called only by `GarageLoader.Save`, so inventory changes made away are never persisted away.
- Returning: `TakenItemsWindow.BuyPartsAction`/`QuitWithoutPartsAction` call `SelectSceneToLoad("garage", 4, true,
  true)`; the garage load merges `TempInventory` (finding 6).
- Every scene generator first runs `GameDataManager.Load` and `GlobalData.Load` (money comes from the local profile,
  i.e. from the last mirrored `WorldState`).

## Junkyard (`JunkyardGenerator`)

Entry: `Start` → `Generate()` builder `0x181873A40` (direct call, **fires**) → `<Generate>d__18.MoveNext`
`0x180EB07A0`. `Update` restarts it inline when the debug flag `GenerateNewJunkyard` is set.

- State 3: `TempInventory.ClearListOfItems()`, `CanMount/CanUnmount = false`,
  `amount = clamp(FloorToInt(CarSpawnPositions.Length * Random.Range(CarsPercentage)/100), 3, Length)`.
- State 5: `dlc = Helper.CanGenerateDLCCar(out id)` (only for an owned DLC on its release day, then `Random.Range(0,101) > 70`, about 30 %);
  `randomCars = Helper.GetRandomCars(CarBundleLoader.GetCarsForScene(1), amount - (dlc ? 1 : 0))` (Shuffle + unique
  `Random.Range` picks); `Helper.AddRandomDLCCar`.
- State 5/6 loop per car `i`: `pos = Random.Range(0, freePositions.Count)`, then
  `CreateCar(i, carSupport, positions[pos], randomCars[i])` (`0x181873AD0`, direct call, **fires**), and
  `positions.RemoveAt(pos)`.
- `<CreateCar>d__19` (`0x180EAF7D0`): `LoadCar(CarID)`, `CarFrom = Junkyard (1)`, `PlaceAtPosition`, then over several
  frames `SetRandomCarColor`, `SetRandomFactoryColor`, fluids, `SetRandomMissingPanels`, part/body conditions,
  unmounted IOs (`percRangeUnmountIO`), rust, dents, dust, `negotationMod = Random.Range(rangeNegotationMod)`,
  mileage, licence plates.
- State 7: every `InteractiveObject` under `Junkyard` whose type byte (`+0x30`) is 8 gets a `Junk` component and
  `Junk.AddRandomItems(…, rangeJunkCondition)` (direct call, fires); the pile goes into `listOfSelectedJunks`.
- States 8–10: under `JunkyardRandomShow`, a random subset (`Count * Random.Range(RandomShowPercentage)/100`) is
  toggled with `SetActive`, then all type-8 IOs there also get `Junk` + items.
- State 11: unless difficulty mode `== 2`, one random pile gets `Junk.AddSpecialMap()` (barn map item).
- Then `ShopListWindow.Load`, `IsGameReady = true`, `stat_visit_junkyard`.

`Junk.AddRandomItems` (`0x181872CE0`): items only when `Random.Range(0,101) > 19` (about 80 %); counts from `Range(1,6)` (other branches 5–7, 10–20, 20–25; *branch conditions not fully read*
branches), split into "good" (`AddGoodItems`: 10 % `GameInventory.GetRandomItemForOwnedCars`, else a random item;
random colour, quality rolls, rims/tyres via `GetRandomRimData`) and "bad" (`AddBadItem`) items, each inserted at a
random index of `ItemsInTrash`. Inputs: global `UnityEngine.Random`, the item database, **the owned car list**.

Randomness sources: global `UnityEngine.Random` only (no seed), the player's DLC ownership (`CanGenerateDLCCar`), the
owned-car list (`GetRandomItemForOwnedCars`), `GetCarsForScene(1)` (installed cars/mods).

LvxBetterCarSpawns: prefix `Generate` sets `CarsPercentage = (100,100)` (all spawn points) and pre-selects a weighted
candidate list; prefix `CreateCar` replaces the `ref CarsIdWithConfig randomCar` argument by index. Both hooks fire
(direct calls). Its `ItemsExchangeWindow.Show` prefix injects `AddSpecialMap`/`AddSpecialCase` into a pile on first
open. The selector (`SpawningEngine`, `WeightedRandomSelector`, `SpawnHistory`, `RuntimeVehicleDatabase`) is pure
managed code over `CarBundleLoader.CarNamesData`, `CarIsAvailableOnScene` and the config keys `allowedPlaces`,
`rarity`, `brand`; weights penalise rarity, spawn counts and recent history (per location and global), with
per-visit model diversity.

## Barn (`ShedManager`)

Entry: `Start` → `Generate()` `0x180A40560` (direct, **fires**) → `<Generate>d__29.MoveNext` `0x180973570`
(1,150 lines, states 0–16).

- States 1–2 (one frame): `TempInventory.ClearListOfItems()`, `CanMount/CanUnmount = false`, `ShedRoot` with a random
  Y rotation; `randomCars = Helper.GetRandomCars(GetCarsForScene(2), dlc ? 2 : 3)` + DLC car written to the field
  `randomCars` (`+0xD8`); shed group `Random.Range(ShedElements.Count)` (or `GenerateShedFromGroup-1`), random floor,
  random walls/props from `AG1x1…PG1x2` prefab arrays.
- State 3: random helper group, player spawn position, random prop placement.
- States 4–7: pile places (`_PGPlace`) get colliders and `ShedManager.AddItems` (direct, fires) →
  `Junk.AddRandomItems`.
- State 9 loop (`i < 3`): `CreateCar(i, carSupport, carHelpers[i], randomCars[i])` (`0x180A405F0`, direct, **fires**).
  `<CreateCar>d__30` mirrors the junkyard one with `CarFrom = Barn (2)`.
- State 10: unless difficulty `== 2`, a random pile gets `Junk.AddSpecialCase()` (loot case).
- `stat_visit_barn`, `ActivateDifficultyLevel`.

The barn layout is procedural geometry, not just cars and items, so "replay" means replaying many choices.

LvxBetterCarSpawns: its `Generate` prefix writes `randomCars`, but state 2 of `d__29` overwrites that field with the
vanilla list, so its `CreateCar` prefix (which reads `randomCars[index]`) most likely reapplies the vanilla choice.
*Uncertain; runtime check 5.*

## Auction (`AuctionManager`, `AuctionBidding`)

Entry: `Awake` starts `<Generate>d__41` (the `Generate` builder `0x180CEFB60` has **no native callers: inlined**). It
only loads profile/global data and UI; cars are generated lazily.

- `GenerateCars(AuctionType)` `0x180CF0090` (**fires**; from `OpenNormalAuctions`, `OpenSalvageAuctions`,
  `ChooseAuctionType.OnAuctionsClick`), once per type per visit (`normalCarsGenerated`/`salvageCarsGenerated`):
  `GetCarsForScene(type == Normal ? 5 : 1)` (salvage uses the junkyard list), random-comparator sort,
  `n = (int)Random.Range(amountRange)`, per car `AuctionCarData { Car, Version, Seed = index, Rating =
  GetRatingForCar(type), Value = GetCarValue(car), StartingPrice = GetStartingPrice(Value, priceModRange, Rating) }`,
  plus one DLC car. LvxBetterCarSpawns replaces the whole list in a prefix returning `false` (random `Seed`).
- `LoadCar(AuctionCarData, AuctionType)` `0x180CF0830` (**fires**; from `AuctionSelectCar`): `Random.InitState(Seed)`,
  then builds `<LoadNormalCar>d__54`/`<LoadSalvageCar>d__59` **inline** (their builders have no callers). `d__54`:
  `LoadCar`, `CarFrom = Auction (5)`, `PlaceAtPosition`, `AuctionHelper.GenerateAuctionCar` (seeded rolls),
  random tent, `FillOtherTents` → `<LoadAdditionalCars>d__55` (background cars; `GenerateAdditionalAuctionCar` builder
  inlined).
- Bidding is local UI (`PlayerBid`, AI bids, timers). No money moves until `ReceiveCarAction` (finding 1).
  `PlayerHasMoney = bidAmount + currentBid <= PlayerMoney`; `PlayerHavePlaceForCar` = free garage place or parking not
  full.
- After the car is stored, `MoveCarToGarage` (garage choice) does **not** delete the auction car; it reopens
  `AuctionWindow.OpenScreen(1)`. `MoveCarToParking` deletes it.

## Salon / dealer (`SalonManager`, `CMS.Salon.Configurator`)

Entry: `Awake` starts `<Generate>d__5` inline. **`Generate`, `LoadCars` and `LoadCar` builders have no native
callers: inlined.** Hook the coroutines' `MoveNext` instead.

- `<LoadCars>d__6` (`0x18096C240`): `Helper.GetRandomCars(GetCarsForScene(8), carLoaders.Length or -1 with DLC)` +
  DLC car; one `<LoadCar>d__7` per `carLoaders[i]`.
- `<LoadCar>d__7` (`0x18096BE00`): `LoadCar(CarID)`, `ExamineAllParts`, `PlaceAtPosition`, `GetRandomCarColor` +
  `SetCarColorAndPaintType`, default livery, plates "SALON", `CarFrom = Salon (6)`. New cars, so only the model and
  colour are random.
- The configurator (`Configurator`, `CustomCar`, `SalonWizardWindow`) edits `CarLoader.SalonCarData` (wheels, paint);
  price = `BaseCarPrice + tyre/rim costs` (`Configurator.GetPrice`, `CarSummaryTab.SetupForBuy`).
- Purchase goes through `CarSummaryTab` (`stat_buy_carsalon` when `CarFrom == 6`). *That the configurator's buy
  button opens `CarInfoWindow`/`CarSummaryTab` is inferred from the stat, not traced; runtime check 6.*

## Parking scene (`ParkingManager`, `ParkingWindow`)

Not a shop. `<PrepareScene>d__12` loads profile/global data and shows `ParkingWindow`. Selecting a slot runs
`<LoadCarAtPlace>d__18`, which displays the parked car via `LoadCarFromFile(saveIndex, true)`; leaving a slot
deletes the display car. "Move to garage" is `<MoveCarToGarage>d__21` with direct `GameDataManager` calls (finding 5).
Nothing is random; the visit is fully determined by `ProfileData.carsOnParking`, i.e. by row 2's shared parking.

## Buying a car (junkyard, barn, salon)

```
CarSummaryTab.SubmitAction (delegate) -> BuyCar() 0x180AE9A80           [fires]
   PlayerMoney < price -> "GUI_BrakKasy"; no free garage place && ParkingIsFull -> ShowFullGarageInfo
   else ShowAskWindow(…, <BuyCar>g__BuyCarAction|31_0)
<BuyCar>g__BuyCarAction|31_0(true) 0x180AEA290                           [delegate, fires]
   stat_buy_carsalon / _carjunkyard / _barn by CarFrom; GameScript.BuyCar(loader, price)
GameScript.BuyCar(CarLoader, int) 0x180E95560                           [fires; only caller is the lambda]
   salon: random licence plate; hide windows; CarLocationWindow.SetCarLoader; Show(CarLocationWindow)
   GlobalData.AddPlayerMoney(-buyPrice); GlobalData.Save(); playtime bookkeeping
CarLocationWindow <CreateButton>g__Action|0 -> NotificationCenter.ButtonAccept(hash{Type="BuyCar",
   PositionTo="Garage"|"Parking", CarLoader}) -> StartCoroutine(BuyCar(hash)) -> <BuyCar>d__21 0x180A6BA70
   "Garage":  MoveCarToGarage(hash, true) -> d__18: idx = CarPlaceManager.GetFreeCarLoaderIndexWithReservation();
              SaveCarToFile(idx, false) -> SaveCar(…, idx, false) -> SaveCarInGarage(data, idx); wait SaveComplete;
              auction: AuctionWindow.OpenScreen(1) | else DeleteCar(scene != Salon) + SetCarLoaderOverNull;
              CarPlaceManager.SetFreeGarageIndex(idx)
   "Parking": MoveCarToParking(loader) -> d__19 (car-placement.md 1.4): SaveCarToFile(slot, true) ->
              SaveCar -> SaveCarInParking(data, slot); DeleteCar(scene != Salon)
```

Price (`CarSummaryTab.SetupData` + `SetupForBuy` `0x180AE7770`): `base = (CalcCarValue + CalcBodyValue) * uniqueMod`
(salon: `SalonCarData` sum); `price = round(base * loader.negotationMod) + restorationBonus + tuning`; then
`price += price * commission / 100` with `GetCommissionForScene` **inlined here**: 5 for Junkyard/Barn, 20 for Salon,
1 otherwise. `negotationMod` is rolled per car in `CreateCar`.

Auction: `ReceiveCarAction` replaces the first three steps (finding 1); the rest is identical from `CarLocationWindow`.

## Picking up parts and items (junkyard, barn)

```
pile InteractiveObject -> ItemsExchangeWindow.Show() (virtual, fires) -> GetFoundItems(): junk =
   GameScript.<IO under cursor>.GetComponent<Junk>()
FoundTab/CollectedTab click -> BaseInventory.SubmitCurrentItem -> NotificationCenter.NewButtonAccept(hash)
   -> NotificationCenter.MoveItem(Item, bool toWarehouse, string windowType) 0x1809DF2D0   [direct, fires]
      (GroupItem overload 0x1809DFC60 likewise)
   take  (toWarehouse=true):  junk.ItemsInTrash.Remove(item); TempInventory.items.Add(item) [inlined];
                              Inventory.Add(item, false)
   put back (false):          junk.ItemsInTrash.Add(item); TempInventory.items.Remove(item) [inlined];
                              Inventory.Delete(item)
leave (exit trigger, *caller not traced*) -> TakenItemsWindow.Show -> Prepare / CheckIfThereAreItems (empty -> ask, then leave)
   price = TempInventory items at mod 0.47 (junkyard) / 0.50 (barn), times the shop_discount upgrade (GetDiscount)
   BuyPartsAction (delegate, fires): PlayerMoney < priceAfterDiscount -> GUI_BrakKasy; else
       AddPlayerMoney(-priceAfterDiscount); SelectSceneToLoad("garage", Garage, true, true)
   QuitWithoutPartsAction: TempInventory.ClearListOfItems(); SelectSceneToLoad(…)
   RemoveCurrentItemAction / OnTrashButtonClick: TempInventory.RemoveItem
garage load: GarageLoader.<Load>d__14: Inventory.Load(); foreach TempInventory item: Inventory.items.Add [inlined];
   TempInventory.ClearListOfItems()
```

`TempInventory.AddItem` has **no native callers** (always inlined); `ClearListOfItems`/`RemoveItem` are called.
`FoundTab.RefreshItems` sorts `ItemsInTrash` in place, so piles must be keyed by item UID, not list index.

## Recommended hook points

(a) Server-chosen car lists (row 15):

| Scene | Hook | Notes |
|---|---|---|
| Junkyard | prefix `JunkyardGenerator.Generate` (set `CarsPercentage` from the server count) + prefix `JunkyardGenerator.CreateCar(int, GameObject, Transform, ref CarsIdWithConfig)` | as LvxBetterCarSpawns. The spawn **position** is `Random.Range` in `d__18`, so also substitute the `Transform t` argument (`ref`) from a server position index into `CarSpawnPositions` |
| Barn | prefix `ShedManager.CreateCar(…, ref CarsIdWithConfig)` | not the `Generate` prefix (overwritten). Layout needs a seed (below) |
| Auction | prefix `AuctionManager.GenerateCars(AuctionType)` returning the server list (`Seed` included) | `LoadCar` then seeds itself |
| Salon | prefix `SalonManager.<LoadCar>d__7.MoveNext` at state 0, replace `randomCar` (+0x28) per `carLoader` | builders inlined |

Car appearance (conditions, missing parts, colour, `negotationMod`) is rolled over several frames with global
`UnityEngine.Random`. Two options: (1) **per-step reseed**: prefix on each generator/`CreateCar` `MoveNext`
saves `Random.state`, calls `Random.InitState(hash(visitSeed, carIndex, __1__state))`, postfix restores it; each
`MoveNext` step is synchronous, so other scripts between frames cannot desync it (also works for the barn layout,
states 1–7, and pile items). (2) **record and replay**: the first visitor serialises each generated car (row 2's
`NewCarDataCodec`) and later visitors load it. (1) is cheaper but depends on identical item/car databases and DLC
ownership on every client; (2) needs a way to build `NewCarData` without writing a profile slot (not found
statically) and does not cover the barn layout. Recommended: (1), with server-chosen car IDs overriding the DLC roll.

Loose items: record after the generator finishes (postfix on the generator's final `MoveNext` returning false, or on
`NotificationCenter.IsGameReady` turning true): for each `Junk` in `GetComponentsInChildren<InteractiveObject>()`
order of `Junkyard`/`JunkyardRandomShow` (barn: `ShedRoot` `_PGPlace`s), send `ItemsInTrash` and the
RandomShow active set; later visitors overwrite `ItemsInTrash` per index. With per-step reseeding the layout is
identical anyway and the server only needs the taken-item deltas.

(b) Purchase capture with vanilla money blocked:

1. Open the capture `{RequestId, Price}` in a prefix on `GameScript.BuyCar` (price = `buyPrice`) **and** on
   `AuctionBidding.ReceiveCarAction` (price = `currentBid`), only when connected and scene ≠ Garage.
2. While open, prefix `GlobalData.AddPlayerMoney` returns `false` for the matching negative amount (direct calls in
   both methods, fires). `GlobalData.Save` then writes the unchanged mirror money.
3. Capture the car in a postfix on `GameDataManager.SaveCar(NewCarData, int, bool)` (fires for both "Garage" and
   "Parking"), or in a prefix on `NotificationCenter.<BuyCar>d__21.MoveNext` state 0 to read `hash` (`PositionTo`,
   `CarLoader`) early. Clear the vanilla slot: `SaveCarInParking(default, slot)` or `SaveCarInGarage(default, idx)`
   (*empty-garage-slot semantics unverified*), then send `CarParkRequest { CarLoaderID = -1, Price }`.
4. Start the timeout at the `ButtonAccept("BuyCar")`/`d__21` step, not at `BuyCar`: the location window waits for
   the player.
5. Simpler alternative: while connected, rewrite `PositionTo` to `"Parking"` in the `d__21` state-0 prefix (or hide
   button 0), so purchases always take the parking path that D8 assumes.
6. Row 2's `SaveCarToFile(…, true)` park hook must ignore saves inside an open capture (finding 4).

(c) Item pick-up:

- Live, per item (row 15, scavenging together): prefix `NotificationCenter.MoveItem(Item, bool, string)` and the
  `GroupItem` overload with `windowType` ≠ `"Warehouse"` and an `ItemsExchangeWindow` open; key = pile index + item UID.
  The server arbitrates "who took it"; a refusal returns `false`.
- Purchase at exit: keep `TakenItemsWindowHook` on `BuyPartsAction` (delegate, fires). Its
  `TempInventory.ClearListOfItems()` is required, otherwise the garage load appends the items a second time.
  `QuitWithoutPartsAction` needs no hook.
- Do not hook `TempInventory.AddItem` (inlined) or `Inventory.Add` for this (scene-local and discarded).

## Inlined or never-called (do not hook)

`SalonManager.Generate/LoadCars/LoadCar`, `AuctionManager.Generate/LoadNormalCar/LoadSalvageCar/LoadAdditionalCars/
LoadAdditionalCar`, `AuctionHelper.GenerateAdditionalAuctionCar`, `TempInventory.AddItem`, the `TempInventory` merge
in `GarageLoader.<Load>d__14` (bypasses `Inventory.Add`), `GlobalData.GetCommissionForScene` (inlined in
`SetupForBuy`), `GameScript.BuyCar` for auctions (not on that path).

## Still needs a runtime check

1. Log `BuyCar`, `ReceiveCarAction`, `ButtonAccept("BuyCar")`, `SaveCarToFile`, `SaveCar`, `SaveCarInGarage/Parking`
   and `AddPlayerMoney` for one purchase per scene and both location buttons (task 6.1).
2. `SaveCarInGarage(default, idx)` clears a garage slot cleanly, and `SetFreeGarageIndex` reservations do not leak.
3. Auction determinism: two clients with the same `AuctionCarData` show the same car (seeded across frames?).
4. Per-step reseeding via `Random.InitState` in iterator `MoveNext` prefixes gives identical junkyard cars, piles and
   barn layout on two clients (same DLC/mod set).
5. LvxBetterCarSpawns' barn `CreateCar` prefix: effective or overwritten (finding in the barn section).
6. Salon configurator purchase path reaches `CarSummaryTab.BuyCar` / `GameScript.BuyCar`.
7. `GetComponentsInChildren<InteractiveObject>` pile order is stable across clients and visits.
8. `MoveItem`'s `windowType` string for the pile window, and right-click (move-all?) behaviour in `NewButtonAccept`.
9. Parking scene: confirm the display path `LoadCarFromFile(idx, true)` and the direct-save "move to garage" path.
10. Whether `difficulty == 2` (no barn map/loot case) is Sandbox.
