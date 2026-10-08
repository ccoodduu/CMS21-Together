# Design

## Context

- Salon purchase (decompiles `outdoor_clean/CMS.UI.Windows.SalonSelectCarWindow$${SubmitCar,Show}.c`,
  `spfeat_clean/CMS.UI.Windows.CarVersionWindow$$Show.c`): `SalonSelectCarWindow.Show` fills its grid from
  `CarBundleLoader.GetCarsConfigDataForSalon()`, the whole salon catalog. `SubmitCar(ShowroomCarItem)` calls
  `Configurator.LoadCar(ID, DefaultConfig)` when `VersionsCount == 1`, else sets `CarVersionWindow.checkDLCAndScene =
  true`, `SetCar(id, versions)` and calls the window's `Show(object[])` with the configurator as
  `ICarLoaderExtension`; the chosen entry calls `carLoaderExtension.LoadCar(car, version)`. None of this passes
  `WindowManager.Show`, where the guard hooks windows. The wizard (`SalonWizardWindow`) changes the configurator's car;
  `CarSummaryTab.<BuyCar>g__BuyCarAction` checks `PlayerMoney < price` locally, then `GameScript.BuyCar` →
  `CarLocationWindow` → `NotificationCenter.<BuyCar>d__21`.
- Row 6 part 2's `CarPurchaseSync` captures `GameScript.BuyCar` outside the garage, forces the parking target and sends
  `CarParkRequest` with `CarLoaderID = -1`, the car's `NewCarData` and the price; `ParkingHandlers` refuses
  `request.Price > world.Money` with `ParkRefusal.NoMoney`, otherwise takes the money once; the client shows
  `"There is not enough shared money."` on a refusal. `SourceCarIndex` is `OutdoorCarSync.IndexOf(carLoader)`, which is
  -1 outside a shared outdoor instance; the salon is not one, so nothing is marked sold.
- Display cars: `SalonManager.<LoadCars>d__6` (`GetRandomCars` plus `GetRandomDLCCar`) and `<LoadCar>d__7` (colour roll,
  plates "SALON", `CarFrom = Salon`), per client. Not changed.
- Server money command: `money set <val>` sets `WorldState.Money` and broadcasts the world state. Harness `net-hold on`
  holds a client's incoming packets (outgoing still sent) until `net-hold off` replays them.

## Goals / Non-Goals

**Goals:** a salon purchase proven end to end (configurator car, non-default version and rim, money once, parking once,
for everyone); the server's money refusal proven and answered; honest guard entries.

**Non-Goals:** a shared display lineup (dropped by the user 2026-10-08); a configurator preview for others; a
multiplayer Showroom; server-side salon prices (row 16).

## Decisions

### D1. Purchase path unchanged

The configurator's `CustomCar` is bought through row 6's path. Spike 1.1 names the loader `GameScript.BuyCar`
receives (the configurator's car) and the `NewCarData` fields that carry the version and the configurator choices
(config version, wheels, colour). If a field is missing, the fix goes into `CarPurchaseSync` and this design names it.

### D2. Server refusal proof

The scenario makes the server's money lower than A's local money after A's local check: A `net-hold on`, the server
`money set` to below the price, A `salon-buy` (the local check passes on A's old money, the request goes out), A
`net-hold off`. The server refuses with `NoMoney`; A sees the message, A's money becomes the server's value, and the
parking is unchanged on both. A step with the money simply set low beforehand is labelled "local refusal" and kept as
a second, cheaper check.

### D3. Guard

`Window CarVersion` → allowed, owner "row 26", label "Car version (car salon)"; `Scene Showroom` and `Window Showroom` →
`Never`, label "The showroom (main menu only)". `guard-trace` (row 14a) confirms that `CarVersion` never passes
`WindowManager.Show` (task 1.2).

## Risks / Trade-offs

- [The configurator loader is not a normal `CarLoader` and `CarPurchaseSync` reads the wrong data] → spike 1.1; the
  scenario compares the unparked car's version and rim with the choice.
- [`net-hold on` also holds the server's answer] → the answer is replayed with `net-hold off`; the scenario checks the
  message after that.
- [Two players configuring at once each see their own car at the same spot] → accepted (proposal, not in scope).

## Migration Plan

No data or packet change. Rollback: revert the guard entries.
