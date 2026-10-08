# Proposal

## Why

The user wants "the showroom / salon" in multiplayer: travel there, look around, buy a car (2026-10-08, ROADMAP
row 26). The game has two different places with these names, and only one of them is a shop:

- **The car salon** (`SceneType.Salon`, scene `Auto_salon`, `GameScene.Salon`) is the new-car dealership on the map.
  The player travels there from the garage map (free), walks between the displayed cars, opens a car, picks a version
  (`CarVersionWindow`, when the model has several), configures rims, tyres and paint (`SalonWizardWindow`,
  `CMS.Salon.Configurator`) and buys it; the car goes to the garage or the parking (`CarSummaryTab` → `GameScript.BuyCar`
  → `CarLocationWindow`). **This is already allowed in multiplayer** (guard `Scene Salon`, `Window SalonSelectCar`,
  `Window SalonWizard`, row 6 part 2), and a purchase already goes through the server into the shared parking
  (`CarPurchaseSync`, `CarParkRequest` with `CarLoaderID = -1`). It was only ever checked by hand.
- **The Showroom** (`SceneType.Showroom`, scene `Showroom_2`, `ShowroomManager`, `ShowroomWindow`) is the main menu's
  car viewer, reached only from the main menu's "Showroom" button (`MainSection` → `SelectSceneToLoad("Showroom_2",
  Showroom)`), not from the garage map. There the player picks any car model of the game (and its version), orbits
  the camera around it and uses the top menu: X-ray, start the engine, explode the car into its parts, change the
  paint, toggle rust, sit inside, and "Exit" back to the menu (`ShowroomOptions`). No money, no buying, no save: it
  is a museum. Its only way out is back to the main menu, and returning to the main menu disconnects a multiplayer
  session (`SceneHooks`), so a connected player can never reach it. The guard entries `Scene Showroom` and
  `Window Showroom` ("row 6 part 2", Planned) can never fire.

What is still missing for the salon, from the static spike (`docs/spikes/singleplayer-features.md` section 4):

1. **Two players in the salon see different cars.** `SalonManager.<LoadCars>d__6` picks the displayed models with
   `Helper.GetRandomCars(GetCarsForScene(Salon), …)` and `<LoadCar>d__7` rolls each car's colour, per client. Avatars
   are shown in the salon (`GameSceneInfo.ShowsAvatars(Salon)` is true), so players stand next to cars the other does
   not have. Row 15 solved exactly this for the junkyard, barn and auction (server-chosen instance, `ICarSelector`,
   reseeding).
2. **Car version.** `SalonSelectCarWindow.SubmitCar` opens `CarVersionWindow` for a model with several versions by
   calling the window's own `Show(object[])` directly, not through `WindowManager.Show`, so the guard's
   `Window CarVersion` rule ("row 4", Planned) never sees it: the version choice already works in the salon. The
   rule is dead and misleading. `CarVersionWindow` has three callers: the salon, the Showroom and one obfuscated
   method; none is in the garage, so "car version" is not a garage-car feature.
3. **No proof.** STATUS lists salon purchases as a hand check; no scenario buys a salon car.

## What Changes

- **Shared salon instance.** The salon joins row 15's outdoor instances: `OutdoorScenes.All` gains `Salon`, the
  default of `shared_outdoor_scenes` becomes `junkyard,barn,auction,salon`. The server picks the displayed models
  from the client-reported salon catalog (`CarBundleLoader.GetCarsForScene(Salon)`, kept inside the shared DLC set)
  with the existing `BasicCarSelector`, plus the visit seed for the colours; every client in the salon loads exactly
  those cars. The instance closes when the last player leaves, as for the junkyard.
- **Hooks for the salon generator** (`SalonManager.<Generate>d__5`, `<LoadCars>d__6`, `<LoadCar>d__7` `MoveNext`):
  hold until the instance arrives, replace the model list, reseed around the colour roll (row 15's `Reseed`).
- **Buying stays as it is.** A salon car is a new car: buying one does not remove it from the salon (vanilla keeps the
  display car; check in task 1.2), so there is no "sold" state to share. The purchase keeps row 6's path; the
  configurator choices (version, rims, tyres, paint) travel inside the bought car's `NewCarData`.
- **Configurator is local.** A player configuring a displayed car changes only their own copy of it until they buy;
  others keep seeing the factory look. (Open question 2.)
- **Guard clean-up.** `Scene Showroom` and `Window Showroom` become "menu only" entries (`Never`, label "The
  showroom (main menu only)"); `Window CarVersion` becomes allowed with owner row 26 and the label "Car version
  (salon)".
- **Proof.** Scenario `salon-shared`: both players in the salon see the same models and colours; A buys a model with
  several versions in a non-default version and configuration; the car lands in the shared parking with that version
  and those rims on both clients; a purchase without money is refused and answered (D16).

Hooks: `SalonManager._Generate_d__5.MoveNext`, `_LoadCars_d__6.MoveNext`, `_LoadCar_d__7.MoveNext` (prefix/postfix,
row 15 pattern). Packets: none new (row 15's `OutdoorCatalog`, `OutdoorEnter`, `OutdoorInstance`, `OutdoorDigest`
gain the salon as a scene value; `OutdoorInstance` reuses its car list).

## Capabilities

### New Capabilities
- `shared-salon`: one car salon for everyone in it (same models and colours), purchases into the shared parking with
  the chosen version and configuration, and a clear rule that the main menu's Showroom is not part of a session.

### Modified Capabilities
- None in `openspec/specs/` (row 15's `outdoor-scene-instances` is not archived yet; task 4.1 adds the salon there if
  row 15 is archived first).

## Impact

- Core: `OutdoorScenes.All` and `DefaultSetting` (salon added), `OutdoorScenes.HasPiles` unchanged (no piles).
- Server: `CarCatalog` accepts the salon catalog; `OutdoorInstances` opens salon instances; `ServerConfig` default.
- Client: `Logic/Outdoor/GeneratorHooks.cs` (salon steps), `OutdoorSession` (salon), `Guard/GuardRules.cs`.
- Harness: `salon-cars` (displayed models, versions, colours), `salon-buy <index> [version] [rim]` (opens the car,
  picks the version through `CarVersionWindow.LoadCar`, sets the configurator, then the `CarSummaryTab` path like
  `buy-car-here`), `travel Salon` fix (scene name `Auto_salon`); scenario `salon-shared`.
- Depends on (merged): row 15 (outdoor instances, selector, reseed), row 6 part 2 (`CarPurchaseSync`), row 2 (parking),
  row 9 (shared DLC set), row 10 (money).

## Open questions

Each has the default the draft works with.

1. **Showroom.** The main menu's Showroom is a single-player car viewer that a connected player cannot reach. **Default:**
   not part of multiplayer; the guard calls it "main menu only". If the user wants a shared viewer (two players looking
   at the same car in `Showroom_2`), that is a separate, larger change: a new way to travel there from the garage while
   connected, the viewer's options synced, and a way back to the garage (the scene only knows "back to menu").
2. **Configurator preview.** **Default:** local until bought. Alternative: others see the car change while a player
   configures it (one configurator user per car, claim like the balancer).
3. **Stock.** Vanilla keeps a bought salon car on display. **Default:** the same (unlimited stock). Alternative: a
   bought car disappears for everyone until the instance closes.
4. **Salon in the shared list by default.** **Default:** yes; a host can remove `salon` from `shared_outdoor_scenes`.
