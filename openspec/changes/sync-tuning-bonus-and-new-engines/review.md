# Review: sync-tuning-bonus-and-new-engines (ROADMAP row 25)

Reviewed 2026-10-08 against the main code in the worktree (`change/singleplayer-features`; main's newer commits do not
touch these areas), the decompiles in `native/out/spfeat_clean`, `cardetails_clean`, `locks_clean`, `wsm_clean`,
`crane3_clean`, `il2cppdumper/dump.cs`, and the spike run `tools/runs/20261008-231615_L2_sp-features-probe2`.

## Verdict: ready after fixes

Tuning (group 2) is nearly ready. Bonus parts (group 3) has one blocker: D4 gates and counts the fit at the wrong method,
and the decompile already shows it. The new-engine work (group 4) mostly checks out against the code, but its proof
cannot run in a harness game and two of its edge paths leave the wrong item state. All of these have clear fixes, so
no rework is needed.

## What checks out

- `GearboxTab.ApplyAction` (`0x180D73180`) returns unless the gearbox `PartScript.IsTuned()` and the part is mounted (the
  byte at `+0x38`). It then writes `finalDriveRatio` and a new `gearRatio`. `GearboxTab.carLoader` exists at `+0xA0` and
  `TuneWindow.PrepareTabs` sets it, so D1 can get the car from the tab.
- `TuneWindow.Show(object[])` accepts only a `CarLoader` in `args[0]`, so the D2 re-invoke can keep the args in its
  closure. `Show` sets mode 7 (`UI`) and `Hide` restores the previous mode.
- The row 4 read and apply already handle `t:gearbox`. The flush sends only entries whose signature changed
  (`CarDetailsSync.Flush`), so D1 needs only the `MarkDirty`.
- Bonus parts: `TakeOffBonusPart` does what the spike says. Remove = `new Item(ID)` with the paint → `Inventory.Add` →
  `TryDeleteBonusPart` → `TakeOff`. Fit = `Change(SelectedToMount.ID)` → `Paint(item paint)` → `TakeOn`. Paint is kept
  per slot at runtime, so the per-slot paint in D3 is right.
- Engine stand: `CreateEngineAction` → `SetEngineOnEngineStand(currentEngine)` on `ToolsManager+0x30` (stand 1), which
  builds `new GroupItem(id){ItemList=[engine]}` and starts `SetGroupOnEngineStand(g, true)`. No money call is made.
  The coroutine calls `ClearEngineStand()` between states 1 and 2 (`crane3_clean`). Row 5a's `BeforeClear` only calls
  `Forget()` when the group is not in the inventory, and the next put sends `ExpectedUid` = the old engine
  (`ToolSync.SendLocal`). So without D5 the server accepts the replacement and the old engine is gone for everyone.
  The occupied-stand refusal is needed.
- Two builds at once: `ToolsStore.Check` refuses the second put on `current.Uid != expectedUid` and answers with
  `ToolSlotRejected` (D16 holds).

## Blockers

**B1. D4 gates and counts the bonus fit at the wrong method.** `GameScript.BodyMount` (`cardetails_clean`) and its inlined
copy in `SelectPartToMount` (`locks_clean`, line 84) do three things for a bonus slot (`specialType == 0x14`), in this
order:
1. `PlaySFX("PartTakeOff")`;
2. `TakeOffBonusPart(io, false)`;
3. on return, `Inventory.FindItemIndex(SelectedToMount.UID)` and an inlined `List<Item>.RemoveAt`.

Two consequences:
- A `TakeOffBonusPart` prefix that refuses the fit, or holds it until a grant, still lets the caller remove the
  selected item from the local inventory. That removal is unsynced, so the player loses the item locally while the
  server keeps it. The design's own fallback ("gate at BodyMount/ClickIO … if so") is therefore certain, not a risk.
- A `TakeOffBonusPart` postfix runs before the `RemoveAt`. The D4 check "the remembered item has left the inventory" is
  therefore always false there, so the removal is never sent.

Fix:
- Gate the fit in the existing `SelectPartToMount` prefix. `LockHooks.ItemAction` already routes `BonusAssemble` to
  `BodyMountAction` because the mode is in `BodyModes`. Today `TryResolveBody("#…")` fails for a bonus slot, so the
  action is ungated. Add a bonus branch there (`IOMouseOverIO.specialType == 0x14` → slot index → key `x:<slot>`, item
  lock on the selected UID) and keep `Run = () => game.SelectPartToMount(item)`.
- Send the item removal from a `SelectPartToMount` postfix (and a `BodyMount` postfix) for a bonus IO, after the
  `RemoveAt`.
- Keep the `TakeOffBonusPart` prefix only for the remove path (`ClickIO`). Task 1.3 confirms that path has no side
  effect before the call.
- Rewrite D4 and the hook list in the proposal to match.

## Major

**M1. A bonus fit can overwrite a slot that was just filled (lost item).** The `BonusPart` lock only serialises
overlapping clicks. A fits, the details flush, and A releases the lock. B's client may not have applied A's `x:<slot>`
yet: `ApplyWhenReady` is a coroutine, and the lock release and the details update are separate packets. B then sees
an empty slot and fits its own item. The server merges per entry, so the last writer wins, and A's item is gone from
the inventory and from the car. Fix: the `BonusPart` lock request (or the `x:` detail entry) carries the slot state the
client expects (`Id`/`Unmounted`). The server refuses the lock when its stored `x:<slot>` differs, with a D16 answer and
the stored entry so B applies it. Add this step to `car-bonus`: A fits, B fits the same slot with `net-hold` on the
details only → B refused, B's item still in the inventory, one part on the car.

**M2. Group 4's proof cannot run in a harness game, so the scenario as written cannot pass or fail for the right
reason.** STATUS (row 5a, 2026-10-06) says the stand's build coroutine throws or stalls in a harness game. This also
holds for the remote apply `EngineStandSync.Put`, which runs the same coroutine. Every step that is not a refusal is
therefore out of reach in the harness:
- B's stand showing the engine;
- setting up "an engine on the stand" for the occupied-stand step;
- the concurrent build.

The old-code failure claim ("with `guard-allow` the occupied-stand step loses the engine") cannot be shown either:
`ClearEngineStand` runs only after the fade in state 1, which the harness never gets past. And `tool-stand-create`
calls `SetEngineOnEngineStand` directly, which bypasses the guard (the guard hooks `WindowManager.Show` and the pie).
Fix:
- (a) Timebox 1.1b (≤ 0.5 session). Try driving `_SetGroupOnEngineStand_d__8` step by step with `withFade = false`,
  as `EngineStandSync.Put` does through `Step(...)`. Also check that `carLoader.GetEngineName()` is an id that
  `GetEnginesToCreate` lists, because the spike's stall may be a bad id.
- (b) Whatever 1.1b finds, add a server self-check (in the style of `CarLocksCheck`/`MergeCheck`). It covers: a created
  put on an empty slot is accepted and counted; a created put with an old `ExpectedUid` is refused; two puts with
  `ExpectedUid = 0` give one stand and one `ToolSlotRejected`.
- (c) Make `tool-stand-create` call `CreateEngineWindow.CreateEngineAction` on a prepared window (`currentEngine` set),
  so that the D5 prefix is exercised. For the occupied case, set the stand's `GroupOnEngineStand` through the harness
  without a build, or skip the case.
- (d) The scenario's old-code failure is then "no refusal toast, `CreateEngineAction` ran". The build steps go to
  `docs/playtest.md`.

**M3. A refused created put puts a free engine into the shared inventory.** `ToolSync.OnRejected` → `Compensate`: for
a refused put whose item is neither on a machine nor in the inventory, with outcome `Unchanged` (always the case for a
UID the server never saw), the code calls `inventory.AddGroup(...)` with hooks on. B's lost race in "two builds at once"
therefore adds B's new engine to the shared inventory. That contradicts the spec ("one engine is on the stand for both"),
and the scenario step does not check the inventory. Fix: keep the `Created` flag on the `PendingUpdate`. In
`Compensate`, drop a refused created group instead of returning it (log "built engine discarded, the stand was taken").
State the rule in D5 and add an inventory check to task 4.4. This is the one real use of `Created`: the gap-9 rule never
applies to a UID the server never saw, so D5's "the gap-9 item race rule does not apply" only means a different
counter.

**M4. `PendingCreated` can stick to the wrong put.** D5 sets a static flag in the `CreateEngineAction` prefix, and the
next stand send consumes it. If the build never finishes (the harness, a scene change, an exception), the flag stays set
and marks the next ordinary put from the inventory as created. With M3's fix, a refusal of that put would then destroy
a real engine. Fix: key the flag to the group. In a `SetEngineOnEngineStand` postfix (or the `SetGroupOnEngineStand`
postfix when called from it), remember the `GroupItem` pointer or UID, and consume the flag only when
`GroupOnEngineStand` is that group. Clear the flag on `ClearEngineStand`, a scene change and `Reset`.

**M5. While a local build runs, an incoming remote put starts a second build on the same stand.** B's
`SetGroupOnEngineStand(g, true)` coroutine runs for several frames (fade). A's relayed put arrives meanwhile, and
`ToolSync` starts `EngineStandSync.Put`, a second build coroutine. Two coroutines then run `ClearEngineStand` and build
on one stand. Fix: while a local created build is pending (M4's group), `ToolSync` defers the remote apply for that tool
until the local build ends. B's send is then refused and `Compensate` applies the server state. Add this to D5.

**M6. The `car-tuning` proof does not fail at the gearbox step on the old code.** Step 1 (`tune-open`) is refused by
the guard (`Window Tune` is Planned), so the old code fails there, for the guard's reason. Also, "A applies gearbox
ratios and an ECU map → B has both" passes on the old code once the ECU is applied: the `PartModule.Tune` postfix marks
`Tuning`, and the flush reads the whole tuning section and sends every changed entry, including `t:gearbox`. Fix:
- Order the steps so that A applies the gearbox alone (through `cardetails-ui gearbox`, the real `ApplyAction`), and
  wait 2 s.
- Check B's `t:gearbox` before any ECU or carb apply.
- In tasks.md, state the old-code failure as "with `guard-allow Window:Tune`, B's `t:gearbox` unchanged after 2 s".

**M7. A tuned part loses its tuning when it is unmounted and moved.** The game keeps a part's tuning on its item
(`Item.TuningData`, `Item.GearboxData`; `ModItem` has both fields). `ItemConverter` and `PartRecords` carry neither, so
a tuned ECU or racing gearbox that A takes off reaches the shared inventory untuned and stays untuned when B fits it.
`t:<partKey>` is keyed by the car slot, so it does not follow the item. This conflicts with "tuning values reach every
player". Fix: in task 1.2, confirm that the game fills `TuningData`/`GearboxData` on unmount, then either carry both
fields in `ItemConverter` (an additive change) or list "tuning follows the part through the inventory" as a non-goal
in the proposal. In both cases, add a line about it to `docs/try-it.md`.

## Minor

- **m1.** In D2, decide what an empty tunable set means. If a car has no tuned gearbox and no `PartModule`, the
  `Tune` lock has no exclusive key, so two players can open the window, and the spec ("SHALL NOT be able to open it")
  fails. Add a synthetic exclusive key, for example `tune` (like `engine`), to every `Tune` lock. `LockKeys.IsWellFormed`
  and `CarLocks.Exists` must accept it.
- **m2.** `LockKeys.IsWellFormed` accepts only `car`, `engine` and `b:`/`s:`/`f:` keys, so `x:<slot>` requests are refused
  as `Invalid` until it and `CarLocks.Exists` learn `x:`. Add both to task 3.2 (the proposal says only
  "`LockKeys.Bonus(slot)`").
- **m3.** The "<name> is tuning this car." message: `LockMessages.ForKey` builds the text from the key, and
  `CarLockResultPacket` has no `Kind`. For a server refusal, look up the holder's lock kind in `CarLockMirror` by the
  conflict key, or append a `HolderKind` field (`[OptionalField]`). Otherwise the player sees "is working on the ECU".
  The same applies to "<name> is fitting a bonus part here."
- **m4.** A `Tune` lock has no cap. It is renewed while the window is open (`CarLockMirror` renews every own lock), and
  `LockLifecycle.Update` has no case for `Tune`, so a player who leaves the window open blocks tuning, park, delete, lift
  and a dyno run (`RefuseBusy`, `CarAwayRegistry` `InUse`) indefinitely. Add an idle cap, for example 5 min without
  `ApplyAction`/`Tune`, which closes the window and releases the lock, like `ChooserIdleSeconds`.
- **m5.** `BonusPart.Paint(bool, CustomColor, PaintData, PaintType)` takes a non-blittable struct by value. The row 4 spike
  flags it as "fine to call (verify)". Task 1.3 tests only the local game path. Add a receiver check: apply a fitted,
  painted slot on B through D3's setters and read it back. Fallback: set `IsPainted`/`Color`/`PaintType`/`PaintData`
  and call the renderer update the load path uses.
- **m6.** The receiver's remove (`BonusPart.TakeOff(true)`) skips `BonusPartsManager.TryDeleteBonusPart`, so the part
  asset stays loaded on receivers. This is harmless but worth one line in D3, or call `TryDeleteBonusPart` (it has no
  inventory side effect).
- **m7.** Correct "the DTO … is never read, applied, hooked or stored". `DetailsMerge` line 80 stores `BonusParts`, and
  `CarDetailsStore` line 130 caps it. Nothing on the client writes it, so the migration claim (no-op) still holds.
- **m8.** INTEGRATION lists `cardetails-gearbox`/`-bonus` as existing row 4 verbs, but neither is in `tools/TestHarness`.
  The proposal adds `cardetails-gearbox` as new. Align INTEGRATION when the verbs land.
- **m9.** The `car-tuning` setup needs a car with an `IsTuned()` gearbox (a racing gearbox of the car's own engine,
  fitted). Name the item id in task 1.2's result, because `ApplyAction` silently does nothing otherwise and the step
  would fail for a setup reason.
- **m10.** `CreateEngineAction` always uses stand 1 (`ToolsManager+0x30`), while row 5a also models `Engine_stand_2`.
  The game has only one stand today, but D5's prefix should check stand 1 explicitly, not "the stand the pie was
  opened on".
- **m11.** The user has settled the salon question: they meant the car salon. The proposal's note that `CarVersion`
  belongs to row 26 is consistent with that. Change "salon and the main menu's Showroom" to "the car salon (row 26)".

## Sizes

The ROADMAP says M ≈ 4–5: tuning S ≈ 1, bonus M ≈ 2–3, engine S ≈ 1. With B1, M1 and m1–m3, bonus becomes ≈ 3. With
M2–M5 and the self-check, the engine group becomes S–M ≈ 1.5–2. The total is ≈ 5–6, still M. Each group merging on its
own remains a good choice. Merge tuning first: it is the cheapest and unblocks `Window Tune`.

## Risks

- The bonus slot order between clients (`UID bonusPart<i>`) is the right check. Keep the "skip on mismatch" debug log
  as a counter in the dump, so a soak shows it.
- The `Tune` lock blocks a dyno run by another player (`HeldByOther`) and park, delete and lift (`RefuseBusy`). This is
  intended, but name it in the spec ("other work on the car goes on" is not quite true).

## Review resolution

Applied 2026-10-08. Claims checked against main (`LockHooks.BodyModes`/`BodyMountAction`, `ToolSync.Compensate`,
`LockKeys.IsWellFormed`, `ItemConverter`, `CarLockRefusal.Stale`).

- **B1** Fixed. The fit is gated in the existing `SelectPartToMount` prefix through a bonus branch in `BodyMountAction`
  (`x:<slot>` + item lock); the removal is sent from `SelectPartToMount`/`BodyMount` postfixes after the `RemoveAt`; the
  `TakeOffBonusPart` prefix gates only the remove path (D4, proposal hook list, task 3.2).
- **M1** Fixed. `CarLockRequestPacket.Expect` carries the slot state the client sees; the server refuses as `Stale` and
  sends the stored entry (D4); `car-bonus` has the `net-hold` step and the spec a "slot filled a moment ago" scenario.
- **M2** Fixed. 1.1b timeboxed with the `withFade = false` and engine-id checks; server self-check `ToolsCheck` (D5,
  4.2); `tool-stand-create` runs `CreateEngineAction`; the old-code failure is "no refusal toast, `CreateEngineAction`
  ran"; build steps go to `docs/playtest.md` unless 1.1b drives the build (4.4).
- **M3** Fixed. `Compensate` drops a refused created group (D5); spec and 4.4 check the inventory. The "gap-9 rule"
  wording is gone.
- **M4** Fixed. `PendingCreated` is the built group's UID from a `SetEngineOnEngineStand` postfix, cleared on the send,
  `ClearEngineStand`, scene change and `Reset` (D5).
- **M5** Fixed. `ToolSync` defers a remote apply for stand 1 while a local created build is pending (D5).
- **M6** Fixed. `car-tuning` applies the gearbox alone first and checks `t:gearbox` before any ECU apply; old-code
  failure stated with `guard-allow Window:Tune` (2.5).
- **M7** Fixed (default: carry). `ItemConverter` carries `TuningData`/`GearboxData` (D6, task 2.3); task 1.2 confirms
  the game fills them, otherwise it becomes a non-goal; try-it line in 5.1.
- **m1** `tune` synthetic key on every `Tune` lock (D2). **m2** `IsWellFormed`/`Exists` for `x:` and `tune` (D4, 3.2).
  **m3** `CarLockResultPacket.HolderKind` (D2). **m4** `TuneIdleSeconds = 300` (D2, spec). **m5** receiver paint check
  and fallback (D3, 1.3). **m6** `TryDeleteBonusPart` on the receiver's remove (D3). **m7** wording corrected (proposal,
  D3). **m8** noted in 5.1; the INTEGRATION row 4 line is corrected in this commit. **m9** item id named in 1.2. **m10**
  stand 1 explicit (D5). **m11** "the car salon (row 26)".
- Risks: slot mismatch counter `bonusSlotMismatch` (D3); the `Tune` lock's effect on dyno/park/delete/lift is in the
  spec.
- Size: tuning ≈ 1, bonus ≈ 3, engine ≈ 1.5–2; total M ≈ 5–6.
