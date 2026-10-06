# Review — multiplayer-guard (2026-10-06)

Split from `desync-detection-and-resync` (ROADMAP row 14 (a)) in the integration pass, as both reviews of that change
proposed: (a) is M1, client-only hooks plus a manual trace; (b)–(d) are M2/M5 with Core, server and packets. The
self-review and the second review of the guard part are in `desync-detection-and-resync/review.md`; their guard items
are carried over here. Content moved unchanged except for the references listed under "Integration pass".

## Carried over (still open for implementation)

1. **Prefix order on `NotificationCenter.SelectSceneToLoad`.** The guard's prefix runs first; row 6's scene prefix
   (`SceneHooks`, formerly `DisconnectHooks`) takes `__runOriginal` and does nothing when the guard cancelled. Recorded in
   INTEGRATION.md.
2. **Guard rules are owned per row.** A row adds its `GuardRules` entries in its merge commit and its scenario runs with
   the guard on `Enforce` (ROADMAP integration note). Row 6's travel question is answered by `guard-allow`.
3. **Row 7 save coverage.** Task 3.1 checks whether `GarageLoader.Save(bool)` (game autosave and AutosaveMod) reaches
   row 7's blocked `GameDataManager.Save(int)`; if not, row 7 adds it (also raised in `mod-compatibility`'s review).
4. Harness verbs that act as the player are not bypassed (second review fix 2); messages go through row 8's
   `ModNotify.Message` (second review fix 4); Junkyard and `Map` belong to row 1 (M2) (second review fix 1).

## Integration pass (2026-10-06)

- Owner in INTEGRATION.md and ROADMAP: row 14a. Verbs `guard-trace` (spike), `guard-set`, `guard-allow`, `guard-try`,
  `guard-log`, `guard-rules`; scenario `guard`; APIs `FeatureGuard` (`Decide`, `Bypass`), `GuardRules`.
- Preferences: category `CMS21Together_Guard` (`Mode`, `Allow`, `Deny`), the companion of the mod's main category
  `CMS21Together` (one scheme for all rows).
- Task 1.1 also checks that the game binds nothing to F7/F8/F9 (row 14 resync, row 14 bug report, row 8 session panel).

## Open questions (with proposed defaults)

1. **Pause menu does not pause while connected** and has no save buttons. *Default:* yes.
2. **Photo mode, map viewing and the main gate in M1**: decided by the audit; *default:* allowed if they change no
   shared state.

## Size

M (≈ 4–5 sessions, one with the user for the manual trace walk).
