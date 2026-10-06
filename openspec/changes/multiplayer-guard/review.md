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

## Implementation notes (2026-10-06, static trace from the native decompile)

Found with `native/work/dec.sh`, `xref.py` and `calls.py` (no game run yet, see "Not run in game" below). Where the
design does not hold, the code does what is listed here; the design text is unchanged until the user agrees.

1. **`PieMenuController.CheckSelectedOption` is inlined into `HandleInput`** (no native caller; `HandleInput` reads
   `NameList[CurrOption]`, checks `LayoutElements[i].IsAvailable` and invokes `options[id].OnClick` itself on the
   Rewired `UISubmit` button). A prefix on `CheckSelectedOption` never runs in play, and D2's postfix on
   `PreparePieMenu` would be too late: `IsAvailable = option.Enabled && option.IsAvailable()` is computed while the
   icons are built. **Done instead:** prefix on `PrepareIcons(string[])` (the one builder called by `Show`,
   `PreparePieMenu`, `RunBackFaderAnimation` and the fader lambda) sets `Enabled = false` (via the game's
   `SetEnableOption`) for every id the guard would block and restores it when the guard no longer blocks; the game
   then draws the lock icon and ignores the click. A prefix on `HandleInput` reports a click on a locked option
   (mouse/Return) through `FeatureGuard.Decide`, which logs and shows the message. The `CheckSelectedOption` prefix
   stays for `guard-try` and any non-inlined caller. The QoL mod uses the same `SetEnableOption` mechanism.
2. **Pie option ids** come from `GetOnClick`'s string switch (85 ids, each mapped to its lambda and what it calls);
   all are in `GuardRules`. `settings_save` (`GarageLoader.Save`), `settings_load` and `settings_parking_garage` are
   refused for good.
3. **Pause menu:** `CreateSaveButton` and `CreateButtonsForGarage` are inlined into `PauseQuitWindow.CreateButton`
   (garage case), so prefixes on them never run. **Done instead:** postfix on `CreateButton` hides a button whose
   caption is the localized `pie_settings_save` or `GUI_Pause_QuitMenuSaveButton` while connected.
4. **Map:** the map's panel actions (`MapWindow.SubmitPanelAction`) call `GlobalData.AddPlayerMoney` (barn trip fee)
   before `SelectSceneToLoad`. Blocking only the scene would charge the shared money without travel, so `Window:Map`
   and `Pie:map` stay refused in M1 (owner row 1, M2). This answers QUESTIONS.md fifth round "map viewing allowed if
   no shared state": it has shared state. Photo mode (`Window:Photo`, `Mode:PhotoMode`, `Pie:mode_photo`) is allowed
   per the user's default (local camera; `ShowPhotoWindow` only changes input, fades and shows the window).
5. **Windows:** `ShowAfterFrame`, `ShowCoroutine` and `ShowAfterWindowClose` all end in a native call to
   `WindowManager.Show(id, false)`, which the Harmony detour covers. `Window.Show()` is virtual; direct calls cannot be
   found statically (the trace verb logs `SetWindowAsActive` to catch them in game).
6. **Scenes:** both void entry points only build the coroutine and start it; the guard blocks them and the
   coroutine factory (returns an empty `Il2CppSystem.Collections.ArrayList` enumerator, so `StartCoroutine` gets a
   valid object). Row 6's `SceneHooks` prefix takes `__runOriginal`.
7. **Modes:** `Interior`, `CarDrive`, `PathTest`, `Dyno` and `Benchmark` are entered after the game has moved the
   camera or loaded a scene, so they are log-only (logged as "would block") and refused at their pie options and
   windows. Not verified in game.
8. **D6 static outcomes:** no game code writes `Time.timeScale` (only LeanTween test examples and HDRP's
   `SubFrameManager`), so the pause menu does not stop time; `GarageLoader.Save` calls `GameDataManager.Save(int)`
   (xref), so the game's autosave and AutosaveMod's `GarageLoader.Save(false)` end in row 7's block — row 7 needs no
   change. F7–F9: the game reads the keyboard only through Rewired (the only direct `Input.GetKeyDown` callers are
   `VPReplayController` and `ExtendedFlyCam`); the Rewired keyboard map was not inspected.

### Not run in game

The first lane-2 run (`20261006-092309_L2_guard-trace`) failed before any guard code ran: inside the agent's command
sandbox the client's UDP socket bind is refused ("An attempt was made to access a socket in a way forbidden by its
access permissions", `client_C.log`), so the client never finished joining. Game runs need to be started outside that
sandbox (by the user or a session allowed to). Every task's game check (1.1 runtime walk, 2.1–2.6, 3.1–3.2, 4.1) is
therefore still open; the scenario `guard` and the spike scenario `guard-trace` are written and ready.

## Size

M (≈ 4–5 sessions, one with the user for the manual trace walk).
