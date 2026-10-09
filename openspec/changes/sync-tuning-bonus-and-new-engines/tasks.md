# Tasks

Prerequisites (all merged): rows 4, 5a, 5b, 13, 18, 19 parts 1 and 3, 24. Each group (tuning 2, bonus 3, engine 4) can
merge on its own once its scenario passes; its guard entries open in its merge commit. Merge tuning first (cheapest,
unblocks `Window Tune`).

## 1. Spikes

- [x] 1.1 Engine build (spike run `20261008-231615_L2_sp-features-probe2`): `SetEngineOnEngineStand(new
      Item("engine_v8_stary"))` costs no money, but the build does not finish in a harness game within 25 s (no engine on
      A's or B's stand, no error logged), as noted for the engine stand in STATUS 2026-10-06.
- [ ] 1.1b Timebox ≤ 0.5 session. Try driving `_SetGroupOnEngineStand_d__8` step by step with `withFade = false`, as
      `EngineStandSync.Put` does through `Step(...)`; check that `carLoader.GetEngineName()` is an id that
      `GetEnginesToCreate` lists (the stall may be a bad id). If the build can be driven, 4.4's build steps run in the
      harness; otherwise they are hand checks in docs/playtest.md. Either way 4.2's `ToolsCheck` is written.
- [x] 1.2 Tune window: open it at the dyno with a car with a tuned gearbox and ECU (`give-item` racing parts, mount,
      `car-move` to the dyno, `tune-open`); trace `TuneWindow.Show/Hide`, `GearboxTab.ApplyAction`, `PartModule.Tune`;
      confirm the gate can re-invoke `Show` with the same args. Name the racing gearbox item id that makes
      `IsTuned()` true for the test car (`ApplyAction` silently does nothing otherwise). Unmount the tuned ECU and
      gearbox and check that the game fills `Item.TuningData`/`Item.GearboxData` (D6). Done when D2 and D6 are confirmed
      or changed. **Done** (`20261009-220832_L1_tune-probe`, `20261009-221010_L1_tune-probe`; spike doc section 1):
      the gate re-invokes `WindowManager.Show(Tune, args)` and the window opens on the same car; Bolt Atlanta racing
      parts `t_v8_gearbox_stary` and `t_v8_gaznik_1` (the car has a carburettor, no ECU); D6 holds (the taken-off
      items carry `GearboxData` and `tuningData`). Addition: `DoMount` copies an item's tuning into the part without
      `PartModule.Tune`, so a part change that mounts a mechanical part also marks `Tuning` dirty.
- [ ] 1.3 Bonus parts: buy a bonus part (`give-item`), fit it through `SelectPartToMount` and remove it through `ClickIO`
      with the harness; log `TakeOffBonusPart`, inventory add/remove and sounds. Confirm the remove path has no side
      effect before `TakeOffBonusPart` (D4) and that slot `UID`s match on two clients for three cars with bonus slots.
      Apply a fitted, painted slot on B through D3's setters and read it back (the `Paint` struct argument); use the
      fallback if it fails.

## 2. Tuning

- [x] 2.1 `GearboxTab.ApplyAction` postfix (D1); harness `cardetails-gearbox` and `cardetails-ui gearbox`.
- [x] 2.2 `CarLockKind.Tune` (appended), `LockKeys.Tune` (`IsWellFormed`, `CarLocks.Exists`), server rules, `TuneWindow`
      gate and release, `TuneIdleSeconds` cap, `CarLockResultPacket.HolderKind` and `LockMessages` (D2); harness
      `tune-open <loader>`, `tune-close`.
- [x] 2.3 `ItemConverter` tuning fields (D6), unless 1.2 drops them.
- [x] 2.4 Guard: `Window Tune` allowed (owner row 25).
- [ ] 2.5 Scenario `car-tuning` (two clients): A puts a car with the tuned gearbox from 1.2 and a tuned ECU on the dyno,
      `tune-open`; B `tune-open` on the same car → refused with "A is tuning this car." and B's dump unchanged; A applies
      gearbox ratios alone (`cardetails-ui gearbox`, the real `ApplyAction`) and waits 2 s → B's `t:gearbox` equal,
      checked before any ECU or carb apply; A applies an ECU map → B's `t:<ecuKey>` equal within 2 s; A `tune-close`, B
      `tune-open` succeeds; A takes off the tuned ECU and B fits it to another car of the same engine → the tuning is
      kept (D6); B joins again → same entries after `syncAcked`; server restart → same entries. Old-code failure: with
      `guard-allow Window:Tune`, B's `t:gearbox` is unchanged after 2 s. `Run-All -Changed` (areas `cars`, `locks`,
      smoke). **Done** with the carburettor in place of the ECU, plus the idle cap (`tune-idle 5`): fails on the old
      code `20261009-222538_L1_car-tuning` (B's `t:gearbox` unchanged after 2.2 s), passes `20261009-222319_L1`. Smoke plus `car-details`, `car-mount-race`, `locks-basic`, `locks-car`, `locks-race`,
      `economy-trades`, `diagnostics` pass (`20261009-222727_regression.json`; `guard` failed there because its blocked
      window example was `Window:Tune`, moved to `Window:RevertBackup`, passes `20261009-224552_L1`); `--check-locks`
      (new Tune cases) and `--check-merges` pass.

## 3. Bonus parts

- [ ] 3.1 Core: `ModBonusSlot`, `BonusSlots`, `x:` entries, merge, caps; cars section version bump.
- [ ] 3.2 Client read/apply (D3, `TryDeleteBonusPart` on remove, `bonusSlotMismatch` counter); `LockHooks` bonus branch,
      `SelectPartToMount`/`BodyMount` postfixes, `TakeOffBonusPart` remove gate and postfix (D4); `CarLockKind.BonusPart`,
      `LockKeys.Bonus`, `IsWellFormed` and `CarLocks.Exists` for `x:`; `CarLockRequestPacket.Expect` and the server's
      `Stale` refusal with the stored entry.
- [ ] 3.3 Guard: modes `BonusAssemble`, `BonusDisassemble` and pies `mode_bonus_assemble`, `mode_bonus_disassemble`
      allowed (owner row 25). The `guard` scenario's blocked example (`BonusDisassemble`, STATUS 2026-10-06) moves to a
      feature that stays blocked (`Benchmark`).
- [ ] 3.4 Harness `bonus-probe`, `bonus-fit`, `bonus-remove`. Scenario `car-bonus` (two clients): A fits a bonus part
      from the inventory → B sees it in the slot, the item is gone from both inventories once; A paints the car in the
      paint shop → B's slot paint equal; A removes it → empty slot on B, the item back once in the shared inventory; A
      and B click the same slot at once (`net-hold`) → one fit, the other refused with the message and its item still in
      its inventory; A fits, then B fits the same slot with `net-hold` on the car details only → B refused (`Stale`), B's
      item still in its inventory, one part on the car, B's slot shows A's part; late join and server restart keep the
      fitted part. Fails on the old code at the first step.

## 4. New engine on the stand

- [ ] 4.1 `tool-stand-create` runs `CreateEngineWindow.CreateEngineAction` on a prepared window (`currentEngine` set,
      engine id from the car or an explicit id; no `GetEnginesToCreate` out parameter). For the occupied case the harness
      sets the stand's `GroupOnEngineStand` without a build, or the case is skipped.
- [ ] 4.2 `ToolSlotUpdatePacket.Created`, `CreateEngineAction` prefix on stand 1, `SetEngineOnEngineStand` postfix and
      the group-keyed `PendingCreated` with its clears, `Compensate` discard, deferred remote apply (D5); server count,
      log and the `ToolsCheck` self-check.
- [ ] 4.3 Guard: `Window CreateEngine`, pie `engine_new` allowed (owner row 25).
- [ ] 4.4 Scenario `engine-build` (two clients): A's `tool-stand-create` on an occupied stand → refused with the
      message, `CreateEngineAction` did not run, the engine still on the stand on both; `ToolsCheck` passes on the
      server. Old-code failure: with `guard-allow`, no refusal toast and `CreateEngineAction` ran. If 1.1b can drive the
      build: A builds on the empty stand → B's stand shows the same engine id with every part unmounted; A and B build at
      once → one engine on the stand, the other answered, and the shared inventory has no new engine (D5 discard); money
      unchanged. Otherwise these build steps are hand checks in docs/playtest.md.

## 5. Docs

- [ ] 5.1 ROADMAP row 25 status; STATUS entry with run ids; docs/try-it.md (tuning, tuning kept on a moved part or the
      non-goal line from 1.2, bonus parts, new engines); docs/playtest.md hand checks (tuning at the dyno with a visible
      game, the engine build). INTEGRATION: add `cardetails-gearbox` as new (the row 4 entry listed it and
      `cardetails-bonus` though neither exists) when the verbs land.
