# Tasks

Prerequisites (all merged): rows 2, 6 part 2, 9, 10, 15.

## 1. Spikes

- [ ] 1.1 Harness `travel Salon` loads `Auto_salon` (today the verb passes the enum name as scene name). Trace
      `SalonManager._Generate_d__5`, `_LoadCars_d__6`, `_LoadCar_d__7` `MoveNext` states (row 15's `outdoor-trace`
      pattern): which state fills the model list, where the list lives, how many stands the scene has, whether
      `GetRandomCars` is a real call. Done when design.md D2 names the replacement point.
- [ ] 1.2 In the salon, buy a car by the harness path (`salon-buy`, task 3.1): does the display car stay (vanilla
      `DeleteCar(sceneType != Salon)`), and which `NewCarData` fields carry the version and configurator choices? Done
      when D3 is confirmed or changed.
- [ ] 1.3 `guard-trace` while choosing a version in the salon: confirm that `CarVersion` never passes
      `WindowManager.Show` (D4).

## 2. Server and Core

- [ ] 2.1 `OutdoorScenes`: salon added, default setting; `CarCatalog` and `OutdoorInstances` accept it; server command
      `outdoor` lists salon instances. Verify: `OutdoorSelfTest` (server `--check-outdoor`) covers a salon instance.

## 3. Client and harness

- [ ] 3.1 Harness: `salon-cars` (per stand: model, version count, colour), `salon-buy <stand> [version] [rimId]`, travel
      fix. Dump section `outdoor` shows salon stands.
- [ ] 3.2 `GeneratorHooks` salon steps (D2), `OutdoorSession` salon, digest rows. Verify: two clients in the salon have
      equal `salon-cars` within 1 s of arrival.
- [ ] 3.3 Guard entries (D4) in the merge commit.

## 4. Proof

- [ ] 4.1 Scenario `salon-shared` (two clients): both `travel Salon` → `salon-cars` equal (models and colours); B
      arrives 20 s later → still equal; A `salon-buy` of a model with ≥ 2 versions in version 2 with a non-default rim
      → money down by the price once on both, the car in the shared parking on both with that version and rim
      (`parking` dump, then `unpark` and compare the car); A sets money below the price and buys → refused with the
      message on A, money and parking unchanged on both; both leave and enter again → a new lineup (new instance).
      Fails on the old code at the first comparison (different lineups). Verify: `Run-Session.ps1 -Scenario
      salon-shared` passes; `Run-All -Changed` (areas `outdoor`, `economy`, smoke) passes.
- [ ] 4.2 docs/playtest.md: the salon hand check becomes "configure and buy together"; docs/try-it.md mentions the
      shared salon and that the main menu Showroom is single-player only.

## 5. Docs

- [ ] 5.1 ROADMAP row 26 status; STATUS entry with the run ids; QUESTIONS.md answer to open question 1 if the user
      wants a shared Showroom (new row).
