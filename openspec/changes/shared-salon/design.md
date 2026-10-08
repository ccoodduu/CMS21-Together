# Design

## Context

- Row 15 (`shared-outdoor-scenes`, merged) gives one instance per outdoor scene: the client reports its catalog
  (`OutdoorCatalog`), the server's `ICarSelector` (`BasicCarSelector`) picks the cars, the clients hold their generator
  until `OutdoorInstance` arrives (`GeneratorHooks.Hold`), replace the car arguments and reseed `UnityEngine.Random`
  from the visit seed around each random step (`Reseed`), and send a digest of what they generated (`OutdoorDigest`).
  `OutdoorScenes.All` is `{Junkyard, Barn, Auction}` today.
- Salon generator (`docs/spikes/outdoor-scenes.md` "Salon / dealer"): `SalonManager.Awake` starts `<Generate>d__5`
  inline; the `Generate`, `LoadCars` and `LoadCar` builders are inlined, so the coroutines' `MoveNext` are the hooks.
  `<LoadCars>d__6` calls `Helper.GetRandomCars(GetCarsForScene(8), carLoaders.Length or -1 with DLC)` plus a DLC car
  and starts one `<LoadCar>d__7` per `carLoaders[i]`; `<LoadCar>d__7` loads the model, examines all parts, places it,
  rolls `GetRandomCarColor` and sets plates "SALON" and `CarFrom = Salon`.
- Purchase: `CarSummaryTab.<BuyCar>g__BuyCarAction` → `GameScript.BuyCar` → `CarLocationWindow` → `NotificationCenter.
  <BuyCar>d__21`; row 6's `CarPurchaseSync` captures `BuyCar`, forces the parking target and sends `CarParkRequest`
  with `CarLoaderID = -1` and the car's `NewCarData`; the server takes the money once (`ParkingService.TryAdd`) or
  refuses with `NoMoney`/`ParkingFull`. The salon commission (20 %) is inside the client's price; row 16 may move prices
  to the server later.
- Version: `SalonSelectCarWindow.SubmitCar(ShowroomCarItem)` loads the default config directly when `VersionsCount == 1`,
  else sets `CarVersionWindow.checkDLCAndScene = true`, `SetCar(id, versions)` and calls the window's `Show(object[])`
  with the configurator as `ICarLoaderExtension`; the chosen entry calls `carLoaderExtension.LoadCar(car, version)`.
  None of this passes `WindowManager.Show`, which is where the guard hooks windows.

## Goals / Non-Goals

**Goals:** same salon for everyone; a salon purchase proven end to end, including version and configuration; honest
guard entries.

**Non-Goals:** a multiplayer Showroom (main-menu viewer); server-side salon prices (row 16); syncing a configurator
preview (open question 2).

## Decisions

### D1. Salon as an outdoor instance

`OutdoorScenes.All` gains `Salon`; `HasPiles(Salon)` is false; `ShowsAvatars(Salon)` stays true. The client reports
`GetCarsForScene(Salon)` with its catalog. The server's instance for the salon is `{ Seed, Cars[] }` with
`Cars.Length = 0` meaning "let the clients' count decide" (the number of display stands is a scene constant the first
visitor reports in `OutdoorEnter`).

### D2. Hooks

- `<Generate>d__5.MoveNext` prefix: hold (row 15 `Hold`) until the instance is applied or the 15 s fallback fires.
- `<LoadCars>d__6.MoveNext`: postfix on the step that filled the random list replaces it with the instance's models
  (task 1.1 finds the field: the display car array of `SalonManager` or the iterator's local).
- `<LoadCar>d__7.MoveNext`: reseed prefix/postfix around the colour roll with `Seed + index` (row 15 `Reseed`).
- Digest: the models and colours per stand, compared on the server like the junkyard's.

### D3. Purchases

Unchanged path; the source is `Salon` and no source car is marked sold (D1 of the proposal: unlimited stock). The
configurator's choice is in `NewCarData` (`carToLoad` with the config version, wheels, colour); the receiver of the
parking update loads that data when the car is unparked. The scenario checks version, rims and colour on both clients.

### D4. Guard

`Scene Showroom` and `Window Showroom` → `Never`, label "The showroom (main menu only)"; `Window CarVersion` →
allowed, owner "row 26". `guard-trace` (row 14a) shows whether any path opens `CarVersion` through `WindowManager`
(task 1.3); if one does, it is listed in design.md.

### D5. Late join and leaving

As row 15: a player entering an open salon gets the instance; the instance closes after the last player leaves (grace
period). Nothing is saved (the salon has no state worth keeping).

## Risks / Trade-offs

- [The display car array is filled inside an inlined builder that no hook reaches] → fall back to replacing
  `Helper.GetRandomCars`' result for scene 8 with a prefix (it is a static helper, real call per the outdoor spike);
  task 1.1 decides.
- [A model in the instance that a client cannot load (DLC)] → the selector picks only from cars every connected
  player reported (row 15 rule).
- [Configurator changes leak into the bought car on the buyer's client only] → that is the intended data; the
  scenario checks the receiver.

## Migration Plan

`shared_outdoor_scenes` default changes; an existing server config keeps its explicit value (salon not shared until
the host adds it). Rollback: remove `salon` from the setting.
