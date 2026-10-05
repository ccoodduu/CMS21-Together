# Review: sync-workshop-car-tools

This change was split out of `sync-workshop-tools` by the integration pass (user decision 2026-10-05). The full
review of the combined change is kept in `sync-workshop-machines/review.md`; its findings 1, 3, 6, 7, 11 (watchers,
`MeasurePower`) and the "Updates after the other reviews" section are the ones that apply here.

## Integration pass (2026-10-06)

- Split: groups 16–21 of `sync-workshop-tools` (engine crane, car paint, car wash and interior detailing, oil bin,
  welder, dyno) and the `tools-car-effects` scenario became this change (groups 5–10), with a small own core/server/
  client/harness part (groups 2–4: `ToolAction`, appended `ModToolId` values, effect handler, `toolActionsSeen`).
  Capability `workshop-car-tools-sync`. It lands after `sync-workshop-machines`, `sync-car-parts` and
  `sync-car-details`.
- Engine swap (D2) now matches `sync-car-parts` D6/D9: the actor calls `RebuildRegistry` + `UploadBaseline`; row 1
  stores `EngineSwap` from the baseline and bumps `Revision`, not `SpawnSeq`; this change never writes
  `EngineSwap`. Fallback (accepted by the user): block a different engine while connected if row 1 finds no replay.
- Engine crane out/in: row 1's D2 now hooks `NotificationCenter.ActionUnMountGroup`/`ActionInsertEngineToCar` as a
  group transaction, so the old blocker is resolved there.
- Paint shop (car) also marks `BonusParts` dirty (row 4 gained that section, user decision).
- Dyno: trigger here, values stored by ROADMAP row 13 (unchanged).
- `ToolAction` handler goes through row 6's `ClientScene.GarageBound`.
