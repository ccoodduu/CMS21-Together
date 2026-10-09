# Tasks

Prerequisites (all merged): rows 2, 6 part 2, 10, 19 part 3.

## 1. Spikes

- [x] 1.1 Harness `travel Salon` loads `Auto_salon`. Buy a model with ≥ 2 versions through `SalonSelectCarWindow.
      SubmitCar`, the version window and the wizard (`salon-buy`, task 2.1): name the loader `GameScript.BuyCar`
      receives and the `NewCarData` fields that carry the version, rims and paint; check whether a display car in the
      hall can be bought at all (a `CarInfo` buy path). Done when D1 is confirmed or changed.
- [x] 1.2 `guard-trace` while choosing a version in the salon: confirm that `CarVersion` never passes
      `WindowManager.Show` (D3).

## 2. Harness and guard

- [x] 2.1 Harness `salon-buy <carId> [version] [rimId]` (prints the chosen car, version, rim and the money before);
      `travel Salon` fix.
- [x] 2.2 Guard entries (D3) in the merge commit.

## 3. Proof

- [x] 3.1 Scenario `salon-buy` (two clients): A and B `travel Salon`; A `salon-buy` of a model with ≥ 2 versions in a
      non-default version with a non-default rim → money down by the price once on both, the car once in the shared
      parking on both (`parking` dump), and after `unpark` the car has that version and rim on both; A `net-hold on`,
      server `money set` below the next car's price, A `salon-buy` → after `net-hold off` A shows "There is not enough
      shared money.", money equals the server's on both, parking unchanged on both (server refusal, D2); A buys with the
      money already low → the game's local refusal, nothing sent (labelled "local refusal"). Old-code failure: the
      guard on Enforce shows `Window CarVersion` as Planned in `guard-rules` (the clean-up step); the purchase steps
      pass on the old code and serve as the missing proof. Verify: `Run-Session.ps1 -Scenario salon-buy` passes;
      `Run-All -Changed` (areas `economy`, `guard`, smoke) passes.
- [x] 3.2 docs/playtest.md: the salon hand check becomes the scenario plus "configure at the same time" (each player sees
      only their own configurator car); docs/try-it.md: buying in the salon while connected, the Showroom is
      single-player only.

## 4. Docs

- [x] 4.1 ROADMAP row 26 status; STATUS entry with the run ids.
