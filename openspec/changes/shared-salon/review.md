# Review: shared-salon (row 26)

**Verdict: ready after fixes.** The shared lineup on top of row 15 is the right shape. The draft confuses the salon's
display cars with the car that is actually bought, and that changes the purchase, stock and proof sections.

Checked against main `3645acc`: `OutdoorScenes.cs`, `OutdoorData.cs` (`OutdoorCatalogScene`), `OutdoorPackets.cs`,
`OutdoorInstances.cs`, `CarSelection.cs`, `OutdoorCarSync.cs`, `CatalogReporter.cs`, `CarPurchaseSync.cs`,
`GuardRules.cs`, harness `SceneCommands.travel`, `docs/spikes/outdoor-scenes.md` ("Salon / dealer", "Buying a car",
hook table), and the decompiles `outdoor_clean/CMS.UI.Windows.SalonSelectCarWindow$${SubmitCar,Show}.c` and
`spfeat_clean/CMS.UI.Windows.CarVersionWindow$$Show.c`.

What holds:
- The guard entries are quoted correctly.
- `travel` passes `type.ToString()` as the scene name, so `travel Salon` needs the `Auto_salon` fix.
- `SalonSelectCarWindow.SubmitCar` calls `CarVersionWindow`'s vtable `Show(object[])` directly, not through
  `WindowManager.Show`, so the `Window CarVersion` rule is dead.
- The Showroom is reached only from the main menu.

## Blockers

None.

## Major

**M1. A salon purchase buys the configurator car, not a displayed car.** `SalonSelectCarWindow.Show` fills its grid
from `CarBundleLoader.GetCarsConfigDataForSalon()`, which is the whole salon catalog. `SubmitCar` loads the choice into
the configurator: `Configurator.LoadCar(ID, DefaultConfig)` for one version, or `CarVersionWindow` with the configurator
as `ICarLoaderExtension`. The wizard and `CarSummaryTab` then buy that `CustomCar`. The cars from
`SalonManager.<LoadCars>d__6` are a random display, picked with `GetRandomCars`. The proposal's flow ("walks between
the displayed cars, opens a car, picks a version … buys it") and the harness verb `salon-buy <stand>` mix the two.

Consequences to write down:
- (a) The shared lineup is cosmetic. Players see the same display cars, while what they buy comes from the full
  catalog.
- (b) Each client has one configurator car at a fixed spot. Two players configuring at once each see their own car in
  the same place, and the other player's avatar stands by a car they cannot see. This belongs in open question 2.
- (c) Spike 1.2 must name the bought loader and check whether display cars can be bought at all (a `CarInfo` buy
  path).

Fix: rewrite "What Changes"/D3 around the configurator. Make the verb `salon-buy <carId> [version] [rimId]` (through
`SalonSelectCarWindow.SubmitCar` and the wizard), and keep `salon-cars` for the display.

**M2. If display cars can be bought, row 15 marks them sold.** `CarPurchaseSync.Begin` sends
`SourceCarIndex = OutdoorCarSync.IndexOf(carLoader)` for every loader `Track`ed in a shared instance.
`OutdoorInstances` adds it to `Sold` and broadcasts `OutdoorCarRemoved`. Every other client then calls
`DeleteCar(true)` (`OutdoorCarSync.Delete`), and `CannotBuy` refuses "Another player bought this car first." That is
the opposite of D3's "no source car is marked sold (unlimited stock)". Fix: if 1.2 finds a display-car buy path, do not
`Track` salon display cars, or send `SourceCarIndex = -1` for `Salon` and skip `Sold` for salon instances on the server.
State which, and add a scenario check that the display car stays on both clients after a purchase.

**M3. The pick count and the DLC slot are unspecified.**
- D1's "`Cars.Length = 0` meaning let the clients' count decide … the first visitor reports in `OutdoorEnter`"
  contradicts "every client loads exactly those cars". It also needs a new field: `OutdoorEnterPacket` has only
  `Scene`, so "Packets: none new" would not hold.
- The junkyard pattern is a fixed server count (`JunkyardPickCount = 40`), and clients take the first n.

Fix: add `SalonPickCount` ≥ the number of stands (1.1 measures `carLoaders.Length`), with no packet change.

`<LoadCars>d__6` also adds **one DLC car** (`GetRandomDLCCar`, rolled from the local DLC ownership) next to
`GetRandomCars`. D2 must replace that slot too, from a pick inside the shared DLC set, or the stand differs whenever
DLC ownership differs.

**M4. The hook point is already known.** `docs/spikes/outdoor-scenes.md` (hook table) recommends a prefix on
`SalonManager.<LoadCar>d__7.MoveNext` at state 0 that replaces `randomCar` (+0x28) per `carLoader`. D2 instead plans a
postfix on `<LoadCars>d__6` with "task 1.1 finds the field". Use the documented point; 1.1 then only confirms it (and
the DLC slot).

## Minor

- m1. Client code hardcodes two scenes: `OutdoorCarSync.ApplyPick` and `PicksAsCars` use
  `scene == Barn ? Barn : Junkyard`. Add `OutdoorCatalogScene.Salon` (appended), map it in
  `OutdoorScenes.CatalogScenesFor`, in `CatalogReporter.SceneTypeFor` (`SceneType.Salon`) and in both mappings. List
  them in Impact.
- m2. `BasicCarSelector` picks a `ConfigVersion` per car. The salon display loads `LoadCar(CarID)` with the default
  config. Either apply the picked version or pick default configs for the salon, and keep the digest rows consistent.
- m3. The "Not enough money" step does not reach the server. `CarSummaryTab.BuyCar` checks
  `PlayerMoney < price` locally ("GUI_BrakKasy") before `GameScript.BuyCar`, so lowering the money tests the vanilla
  check, not the server's `NoMoney` refusal (D16). To prove D16, lower the server money only after A's local check
  passed (server command while A's world update is held back, or B spends concurrently). Otherwise label the step
  "local refusal".
- m4. Proof flakiness: "salon-cars equal within 1 s of arrival" is measured from scene arrival, but the generator holds
  until `OutdoorInstance` arrives (15 s fallback). Measure from the generator's done signal (row 15's dump flag), not
  from arrival.
- m5. Fails-on-old-code: old clients can show the same model on a stand by chance. Compare the full model plus colour
  list; a match of all n stands by chance is negligible. Fine as written if the comparison covers colours.
- m6. Guard label for `CarVersion`: per M1 the version window belongs to the configurator ("Car version (salon
  configurator)").

## Size

S–M (≈ 2–3) holds if the configurator rethink stays within spike 1.2. Plan 3.
