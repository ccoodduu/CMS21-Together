# Design

## Context

See proposal.md for the motivation. Facts that shape the approach:

- `sync-workshop-machines` (lands first) provides `ModToolId`, the `ToolSync` client base with the
  `ApplyingRemote(tool)` scope, `ToolPackets.cs`, `ToolHandlers.cs` on both sides and the harness `ToolsCommands.cs`.
  It also syncs the positions of the movable car tools (welder, oil bin, engine crane, interior detailing kit).
- `sync-car-parts` (row 1): `CarPartsSync.MarkDirty(loaderId, CarPart|PartScript)` (attribute-only `CarPartsChange`),
  `RebuildRegistry(loaderId)`, `UploadBaseline(loaderId)`; engine crane out/in (`NotificationCenter.ActionUnMountGroup`,
  `ActionInsertEngineToCar`) is one of its group transactions; it stores `EngineParams.EngineSwap` in every baseline
  and applies a stored or changed swap before it builds the registry.
- `sync-car-details` (row 4) D6: `CarDetailsSync.MarkDirty(CarLoader, CarDetailSection, IEnumerable<int> = null)`
  and `CarDetailsSync.FlushNow(CarLoader)`; sections `Paint`, `BodyCosmetics`, `BonusParts`; fluids are polled by row 4.
- ROADMAP row 13 `sync-test-drive-and-diagnostics` stores dyno results (`EngineData.measured`, `MeasuredDragIndex`).
- For tools that act on a car, remote clients should apply only the final state and not run `DoWorkAnim`, which
  locks interactive objects, closes the car and disables lifter buttons on the remote client (FixForTogether `desync_*`).

## Goals / Non-Goals

**Goals:**
- The car state a tool produces reaches everyone through rows 1/4 (and row 13 for the dyno); this change sends no
  car values of its own.
- Other players see the effect without locked controls, a second cost or a second reward.

**Non-Goals:**
- Machines that hold items, item processing, tool positions: `sync-workshop-machines`.
- Storing dyno results (row 13) and storing the engine swap (row 1).
- Costs of tool use beyond "the remote side never pays again": `economy-audit`.

## Decisions

### D1. Result through rows 1/4/13, effect through `ToolAction`

`ToolActionPacket { ModToolId Tool; int CarLoaderId; ToolActionKind Kind; ModGroupItem Group }` is relayed by the
server to the others and not stored. Remote clients play only particles and SFX (`GarageTool.particles`,
`GarageTool.sfx`, or `PaintshopManager.particleSystem`) at the car. They never call `DoWorkAnim`, `StartAnim`,
`UseOilbin` or `UseEngineCrane`, so they never lock controls, pay or gain XP a second time. The handler goes through
`ClientScene.GarageBound` (dropped while away; an effect is not worth queuing).

IEnumerator hooks (`DoWorkAnim`, `FinishAnim`, `MakeCarPaintEffects`) fire when the iterator is created, not when it
finishes. Under Unhollower the returned Il2Cpp iterator cannot simply be wrapped by a managed one, so the end of an
action is detected by a `MelonCoroutines` watcher started from the prefix: it waits for an observable end
(`PaintshopManager.IsPainting` false, `GarageTool.effectTime` elapsed plus 0.5 s, or the `FinishAnim` prefix) and
then calls the result sender.

| Tool | Actor hook | Result sender (owner) |
|---|---|---|
| Welder | `WelderLogic.DoWorkAnim(CarLoader)` prefix → action + watcher | `CarPartsSync.MarkDirty(loaderId, part)` for the welded body `CarPart`s (row 1) |
| Car wash | `CarWashLogic.DoWorkAnim(CarLoader)` prefix → action + watcher | `CarDetailsSync.MarkDirty(loader, CarDetailSection.BodyCosmetics)` + `FlushNow` (row 4 D6) |
| Interior detailing | `InteriorDetailingToolkitLogic.DoWorkAnim(CarLoader)`, `ToolsMoveManager.UseInteriorDetailingToolkitStationary()` | split per row 4 D6, fields decided by row 4's probe (its task 1.2): `CarPart` dust → `CarDetailsSync.MarkDirty(loader, BodyCosmetics)`; part condition and `PartScript` dust → `CarPartsSync.MarkDirty(loaderId, part)` (row 1's attribute-only `CarPartsChange`) |
| Oil bin | `CarLoader.UseOilbin()` prefix → action | nothing to send: row 4's 1 Hz Fluids poll sees the drain; optional `CarDetailsSync.FlushNow(loader)` after it |
| Paint shop (car) | `PaintshopManager.MakeCarPaintEffects()` prefix → action + watcher | `CarDetailsSync.MarkDirty(PaintshopManager.carLoader, Paint \| BodyCosmetics \| BonusParts)` + `FlushNow` (row 4 D6) |
| Engine crane | `CarLoader.UseEngineCrane()` (out), `NotificationCenter.InsertEngineToCar(GroupItem)` (in) → action | engine out/in is a group unmount/mount: a `CarPartsChange` transaction of row 1 (its D2 hooks `NotificationCenter.ActionUnMountGroup(InteractiveObject)` and `ActionInsertEngineToCar(GroupItem)`) |
| Dyno | `CarLoader.MeasurePower()` postfix, fallback `DynoManager.CloseDyno()` | row 13's dyno result sender (group 10 waits for row 13) |
| Engine swap | `InsertEngineToCar(group)` where `group.ID` differs from `GetEngineName()` and `CanSwapEngineTo` | `RebuildRegistry` + `UploadBaseline` (row 1 stores `EngineSwap`, D2) |

### D2. Engine swap

`CarLoader.SwapEngine(GroupItem)` replaces the engine model, so the part keys under the engine change. The actor runs
the native insert (row 1's crane transaction consumes the engine group), waits until the swap has finished, then
calls `CarPartsSync.RebuildRegistry(loaderId)` and `UploadBaseline(loaderId)`. The baseline carries `EngineSwap`;
row 1 stores it with the car, bumps the car's `Revision` (not its `SpawnSeq`) and relays the baseline as a full
snapshot. Other clients apply the swap, rebuild their registry and apply the records (row 1 D9); late joiners get
the swap before the registry is built. The `ToolAction(EngineSwap)` the actor sends is effect only. This change does
not write `EngineSwap` itself.

Fallback (row 1 review question 1, accepted by the user 2026-10-05): if row 1's spike finds no way to apply a swap to
a freshly loaded car, a prefix on `InsertEngineToCar` refuses a different engine while connected with
`UIManager.ShowInfoWindow`, and task 5.2 is parked.

### D3. Server

`ToolAction` is relayed to every other client under `GameDataManager.StateLock`; nothing is stored, so this change
adds no save section and no snapshot provider. Car-effect results reach a late joiner through rows 1/4/13.

## Risks / Trade-offs

- [IL2CPP inlining: a hook on a small method (`UseOilbin`, `MeasurePower`) may never run when called from native UI
  code] → `sync-workshop-machines`' `tool-trace` spike is extended with these hooks (task 1.2); each missing hook
  gets the caller-side fallback named in D1.
- [Tool tweens on a receiver writing their own end values after our apply] → receivers play only effects.
- [Paint shop: two players paint the same car] → last-write-wins on the car paint (row 4).
- [Rows 1/4 rename their APIs] → this change only calls the senders named in the Context; task 1.1 re-checks them.

## Migration Plan

`ToolAction` and the new `ModToolId` values are appended, so existing values do not change. Client and server must be
updated together (the version check enforces this). No saved state. Rollback means reverting the change.

## Open questions / assumptions

1. **Engine crane out/in and engine swap storage** depend on `sync-car-parts` (D1, D2). If row 1 is merged without
   them, task 1.2 parks group 5 and records the gap in ROADMAP/QUESTIONS instead of building a parallel mechanism.
2. **Interior detailing** results are split between row 4 (`CarPart` dust) and row 1 (part condition, `PartScript`
   dust, sent as an attribute-only `CarPartsChange`); row 4's probe (its task 1.2) decides the exact fields.
3. **Dyno results:** this change owns the trigger; the values are stored by ROADMAP row 13 (decision 2026-10-05).
   No dyno run is mirrored.
4. No FixForTogether code is adapted. Its findings (final-state-only for car-effect tools, `MeasurePower` as the dyno
   commit point) inform this design. If code is adapted later, credit TogetherFixer and link the repository.

## Code (2026-10-06)

The code follows `docs/spikes/workshop-car-tools.md` where it corrects D1. Names as written (not yet run in the game):

- `ToolActionPacket { ModToolId Tool; int CarLoaderID; ToolActionKind Kind }`, without the `Group` field (the effect
  needs no engine contents). `ToolActionKind` = `Weld, Wash, InteriorDetailing, DrainOil, EngineOut, EngineIn,
  PaintCar`; no `EngineSwap` kind while swaps are refused (D2 fallback). The server relays it if the loader is in
  `CarPartsStore`; machine handlers accept only `ModTools.IsMachine` ids.
- Commit points are the postfixes of `WelderLogic`/`CarWashLogic`/`InteriorDetailingToolkitLogic._DoWorkAnim_d__1.MoveNext`
  and `ToolsManager._UseOilDrain_d__40.MoveNext` returning false; no `MelonCoroutines` watcher.
- Row 4 API in use: `CarDetailsSync.MarkDirty(CarLoader, CarDetailSection)` (no `FlushNow`; BodyCosmetics marks are
  repeated after 1 s in case a tween is still writing). Row 1 API: `CarPartsSync.MarkDirty(int, CarPart)`.
- Paint shop: `PaintshopManager.SubmitColor` postfix (`PaintshopType.Garage`) sends the action; row 4's own postfix
  sends `Paint | BodyCosmetics`. `BonusParts` is not synced by row 4 yet.
- The paint/tint "wash first" answer (`CarLoader.EnableDust`/`SetWashFactor` with `part == null`) marks `BodyCosmetics`.
- Engine crane: postfixes with `__runOriginal` on `ActionUnMountGroup` and `InsertEngineToCar`; row 1's
  `EngineCraneHooks` owns the transaction and refuses swaps.
- Dyno: `CarLoader.MeasurePower` postfix → row 13's `DynoSync.Commit`, behind `#if SYNC_TEST_DRIVE` until rebased.
- Interior detailing: stationary when the car stands at `CarPlace.CarWash`.
- Remote effect: `GarageTool.particles.Play()` + `SoundManager.PlaySFX(sfx, car root)`, stopped after `effectTime` +
  0.5 s; paint: `PaintshopManager.particleSystem` + `CarPaint`. Skipped while the same tool runs locally. Oil drain and
  the crane only count the action.
