# Proposal

## Why

The user wants the car salon in multiplayer: travel there, look around, buy a car (2026-10-08, ROADMAP row 26). The
user means the car salon on the map, not the main menu's Showroom viewer (answer 2026-10-08).

- **The car salon** (`SceneType.Salon`, scene `Auto_salon`, `GameScene.Salon`) is the new-car dealership on the map.
  The player travels there from the garage map (free), opens the salon's car list (`SalonSelectCarWindow`, filled from
  the whole salon catalog `CarBundleLoader.GetCarsConfigDataForSalon()`), picks a model; `SubmitCar` loads it into the
  configurator (`CMS.Salon.Configurator.LoadCar(ID, DefaultConfig)` for one version, or `CarVersionWindow` with the
  configurator as `ICarLoaderExtension` for several), the player configures rims, tyres and paint
  (`SalonWizardWindow`) and buys that configured car (`CarSummaryTab` → `GameScript.BuyCar` → `CarLocationWindow`). The
  cars standing in the hall (`SalonManager.<LoadCars>d__6`, `GetRandomCars` plus one DLC car) are a random display;
  they are not what is bought.
- **This is already allowed in multiplayer** (guard `Scene Salon`, `Window SalonSelectCar`, `Window SalonWizard`, row 6
  part 2), and a purchase already goes through the server into the shared parking (`CarPurchaseSync`, `CarParkRequest`
  with `CarLoaderID = -1`; the server takes the money once in `ParkingHandlers` or refuses with `NoMoney`/
  `ParkingFull`). It was only ever checked by hand, and no test reaches the server's money refusal: `CarSummaryTab.
  BuyCar` checks `PlayerMoney < price` locally ("GUI_BrakKasy") before `GameScript.BuyCar`, so a plain "not enough
  money" test only exercises the game's own check.
- **Guard entries are misleading.** `SalonSelectCarWindow.SubmitCar` opens `CarVersionWindow` by calling the window's
  own `Show(object[])`, not through `WindowManager.Show`, so the guard's `Window CarVersion` rule ("row 4", Planned)
  never fires: the version choice already works in the salon. The Showroom (`Showroom_2`, the main menu's car viewer)
  is reached only from the main menu, and returning to the main menu disconnects a session (`SceneHooks`), so its
  guard entries `Scene Showroom` and `Window Showroom` ("row 6 part 2", Planned) can never fire either.

## What Changes

- **Proof of a salon purchase.** Scenario `salon-buy`: A buys, through the configurator, a model with several versions
  in a non-default version with a non-default rim; the money drops by the price once on both clients, and the car lands
  once in the shared parking for everyone with that version and rim.
- **Server money refusal proven.** In the same scenario, the server's money is lowered after A's local check has
  passed (A holds incoming packets, the server command `money set` lowers the money); the server refuses with
  `NoMoney`, A is answered ("There is not enough shared money.", D16), and money and parking are unchanged on both.
- **Guard clean-up.** `Window CarVersion` becomes allowed with owner row 26 and the label "Car version (car salon)";
  `Scene Showroom` and `Window Showroom` become `Never` with the label "The showroom (main menu only)".
- **Harness.** `salon-buy <carId> [version] [rimId]` (through `SalonSelectCarWindow.SubmitCar`, the version window and
  the wizard, then the `CarSummaryTab` path like `buy-car-here`); `travel Salon` loads `Auto_salon` (today the verb
  passes the enum name).
- **Not in scope.** The display cars in the hall stay per client (each player sees their own random lineup), and each
  client's configurator car stands at the same spot, so two players configuring at once each see only their own car.
  A shared lineup was drafted and dropped by the user (2026-10-08): purchases come from the full catalog, so it would
  be cosmetic.

Hooks: none new. Packets: none.

## Capabilities

### New Capabilities
- `shared-salon`: a car bought in the car salon, with its chosen version and configuration, reaches every player once
  and is paid once; a purchase the server refuses is answered; the guard describes the salon and the main menu's
  Showroom correctly.

### Modified Capabilities
- None in `openspec/specs/`.

## Impact

- Client: `Guard/GuardRules.cs` (three entries). No other client change unless spike 1.1 finds the purchase path
  broken.
- Harness: `salon-buy`, `travel Salon` fix; scenario `salon-buy`.
- Docs: docs/playtest.md (the salon hand check becomes the scenario plus "configure together"), docs/try-it.md.
- Depends on (merged): row 6 part 2 (`CarPurchaseSync`), row 2 (parking), row 10 (money), row 19 part 3 (D16 answers).

## Open questions

Each has the default the draft works with.

1. **Display cars buyable?** Spike 1.1 checks whether a display car in the hall can be bought at all (a `CarInfo` buy
   path). **Default:** it cannot (purchases come only from the configurator); if it can, the purchase path is the same
   and nothing is marked sold, because the salon is not a shared outdoor instance (`OutdoorCarSync.IndexOf` is -1
   outside a shared instance).
2. **Showroom.** **Default:** not part of multiplayer; the guard calls it "main menu only".
