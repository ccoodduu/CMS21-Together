# Tasks

Prerequisites (all merged): rows 4, 5a, 5b, 13, 18, 19 parts 1 and 3, 24. Each group (tuning 2, bonus 3, engine 4) can
merge on its own once its scenario passes; its guard entries open in its merge commit.

## 1. Spikes

- [x] 1.1 Engine build (spike run `20261008-231615_L2_sp-features-probe2`): `SetEngineOnEngineStand(new
      Item("engine_v8_stary"))` costs no money, but the build does not finish in a harness game within 25 s (no engine on
      A's or B's stand, no error logged), as noted for the engine stand in STATUS 2026-10-06.
- [ ] 1.1b Find why the stand's build coroutine stalls in a harness game (fade with `withFade = true` in a hidden window,
      or the native exception from STATUS 2026-10-06): trace `_SetGroupOnEngineStand_d__8.MoveNext` states. If it can
      be driven (for example `SetGroupOnEngineStand(g, false)` from the harness), group 4's scenario builds; otherwise
      the build steps are a hand check.
- [ ] 1.2 Tune window: open it at the dyno with a car with a tuned gearbox and ECU (`give-item` racing parts, mount,
      `car-move` to the dyno, `tune-open`); trace `TuneWindow.Show/Hide`, `GearboxTab.ApplyAction`, `PartModule.Tune`;
      confirm the gate can re-invoke `Show` with the same args. Done when D2 is confirmed or changed.
- [ ] 1.3 Bonus parts: buy a bonus part (`give-item`), fit and remove it through the game's `BodyMount` path with the
      harness; log `TakeOffBonusPart`, inventory add/remove and sounds. Confirm the prefix can refuse before any side
      effect (D4) and that slot `UID`s match on two clients for three cars with bonus slots.

## 2. Tuning

- [ ] 2.1 `GearboxTab.ApplyAction` postfix (D1); harness `cardetails-gearbox` and `cardetails-ui gearbox`.
- [ ] 2.2 `CarLockKind.Tune` (appended), server rules, `TuneWindow` gate and release, message (D2); harness
      `tune-open <loader>`, `tune-close`.
- [ ] 2.3 Guard: `Window Tune` allowed (owner row 25).
- [ ] 2.4 Scenario `car-tuning` (two clients): A puts a car with tuned gearbox and ECU on the dyno, `tune-open`; B
      `tune-open` on the same car → refused with "A is tuning this car." and B's dump unchanged; A applies gearbox
      ratios and an ECU map → B's `carDetails` has the same `t:gearbox` and `t:<ecuKey>` within 2 s; A `tune-close`, B
      `tune-open` succeeds; B joins again → same entries after `syncAcked`; server restart → same entries. Fails on the
      old code at the gearbox step (no update sent). `Run-All -Changed` (areas `cars`, `locks`, smoke).

## 3. Bonus parts

- [ ] 3.1 Core: `ModBonusSlot`, `BonusSlots`, `x:` entries, merge, caps; cars section version bump.
- [ ] 3.2 Client read/apply (D3), hooks and inventory (D4); `CarLockKind.BonusPart` and `LockKeys.Bonus`.
- [ ] 3.3 Guard: modes `BonusAssemble`, `BonusDisassemble` and pies `mode_bonus_assemble`, `mode_bonus_disassemble`
      allowed (owner row 25). The `guard` scenario's blocked example (`BonusDisassemble`, STATUS 2026-10-06) moves to a
      feature that stays blocked (`Benchmark`).
- [ ] 3.4 Harness `bonus-probe`, `bonus-fit`, `bonus-remove`. Scenario `car-bonus` (two clients): A fits a bonus part
      from the inventory → B sees it in the slot, the item is gone from both inventories once; A paints the car in the
      paint shop → B's slot paint equal; A removes it → empty slot on B, the item back once in the shared inventory; A
      and B click the same slot at once (`net-hold`) → one fit, the other refused with the message and its item still in
      its inventory; late join and server restart keep the fitted part. Fails on the old code at the first step.

## 4. New engine on the stand

- [ ] 4.1 `tool-stand-create` fix (engine id from the car or an explicit id; no `GetEnginesToCreate` out parameter).
- [ ] 4.2 `ToolSlotUpdatePacket.Created`, `CreateEngineAction` prefix (D5), server count and log.
- [ ] 4.3 Guard: `Window CreateEngine`, pie `engine_new` allowed (owner row 25).
- [ ] 4.4 Scenario `engine-build` (two clients): A builds a new engine on the empty stand → B's stand shows the same
      engine id with every part unmounted; A tries to build on the occupied stand → refused with the message, the
      engine still on the stand on both; A and B build at once on an empty stand → one engine, the other answered;
      money unchanged (or changed once, per 1.1). Fails on the old code (guard on Enforce refuses `CreateEngine`; with
      `guard-allow` the occupied-stand step loses the engine). If 1.1 shows the build cannot run in the harness, the
      build steps become a hand check in docs/playtest.md and the scenario keeps the refusal and server steps.

## 5. Docs

- [ ] 5.1 ROADMAP row 25 status; STATUS entry with run ids; docs/try-it.md (tuning, bonus parts, new engines);
      docs/playtest.md hand checks (tuning at the dyno with a visible game).
