# Review — desync-detection-and-resync (2026-10-06, self-review of the first draft)

Checked against `openspec/ROADMAP.md` (row 14, M1/M2/M5, integration notes, working rules),
`openspec/INTEGRATION.md`, `config.yaml` rules and the drafted rows 1, 2, 5a, 6, 7. `openspec validate
desync-detection-and-resync --strict` passes.

## Verified while drafting

- Stubs: `WindowManager.Show(WindowID, bool)` / `Show(WindowID, args)` / `ShowAfterFrame` / `ShowCoroutine` /
  `ShowAfterWindowClose`, `Window.Show()`; `PieMenuController.PreparePieMenu/CheckSelectedOption/GetOnClick/
  SetEnableOption/optionsId/CurrOption/Close`; `PieMenuHelper.GetIniEntryForMachine(IOSpecialType, out string)` and the
  24 `IOSpecialType` machines; `GameMode.SetCurrentMode(gameMode)` and the 25 `gameMode` values; the three
  `NotificationCenter` scene entry points and `SceneType`; `UIManager.Get().ShowPopup(title, text, PopupType)`;
  `PauseQuitWindow.CreateSaveButton/CreateSaveAndQuitButton`; `GarageLoader.Save(bool)`, `GameDataManager.Save(int)`.
- Client has Newtonsoft.Json and `System.IO.Compression` (MelonLoader managed folder) for the bundle.
- Harness verbs `guard-trace` (spike), `guard-set`, `guard-allow`, `guard-try`, `guard-log`, `guard-rules`,
  `digest-show`, `inv-corrupt`, `digest-hold`, `resync`, `bug-report` and scenarios `guard`, `desync-autofix`,
  `resync-key`, `bug-report` are not used by other changes. Row 1's `part-corrupt` is reused, not redefined. The guard
  class is `FeatureGuard` because row 7 already uses `SessionGuard` (its activity flag is reused).

## Conflicts / coordination with other changes

1. **Row 5a's `tool-resync` sends an in-place `AskForSync`.** This design rejects in-place resync (D10): row 1's
   snapshot loads cars into loaders emptied by `VanillaLoad`, so a second snapshot over a live garage is undefined.
   *Proposed:* row 5a's late-join-with-own-save scenario uses this change's `resync` (garage reload), or keeps
   `tool-resync` only if its own check shows cars and tools survive an in-place snapshot.
2. **Prefix order on `NotificationCenter.SelectSceneToLoad`.** The guard's prefix runs first; `DisconnectHooks` (this
   change, task 2.5) and row 6's scene prefix must take `__runOriginal` and do nothing when the guard cancelled.
   Row 6's design needs that sentence.
3. **Digests for rows 3, 4, 5a.** This change implements digests for the sections that exist at M2 (`world`, `garage`,
   `inventory`, `cars`, `car-placement`). Rows 3 (`jobs`), 4 (`car-details:<loader>`) and 5a (`workshop-tools`) are
   drafted without one. *Proposed:* the integration pass adds one task to each ("register an `IClientDigest`/
   `IServerDigest` with a Core mapper for your DTO; `desync-autofix`-style check"). Until then drift there is only
   fixed by the resync key.
4. **Server-initiated resends need small APIs from rows 1 and 2**: row 1's single-car snapshot builder and row 2's
   `ParkingResyncRequest` answer must be callable for a given client without a request packet (e.g.
   `CarsSnapshotProvider.SendCar(clientId, loader)`, `ParkingService.SendFullState(clientId)`). Add to INTEGRATION.md
   "APIs" as owned by 1 and 2, used by 14.
5. **Guard rules are owned per row.** ROADMAP integration notes need the rule: a row adds its `GuardRules` entries in
   its merge commit, and its scenario runs with the guard enforcing. Row 6's open question 1 (travel in M1) is answered
   by `guard-allow`.
6. **Row 7 save coverage**: the audit (task 3.1) checks whether `GarageLoader.Save(bool)` (game autosave path, and
   AutosaveMod's call) reaches row 7's blocked `GameDataManager.Save(int)`; if not, row 7 adds it (also raised in
   `mod-compatibility`'s review).
7. **Size.** ROADMAP says M. Estimate: (a) M ≈ 4 sessions (trace needs the user for one manual walk), (b) M ≈ 4,
   (c) S ≈ 1, (d) S ≈ 1–2 → ≈ 10 sessions = L. Also the change spans M1–M5, so it cannot be archived until M5.

## Open questions (with proposed defaults)

1. **One change across M1–M5, or split?** *Default:* split part (a) into its own change `multiplayer-feature-guard`
   before implementation (its spec is already a separate capability), keep (b)–(d) here, and set ROADMAP sizes to
   M + M. Otherwise relabel row 14 as L and archive after M5.
2. **Automatic repair on by default** (`desync_autofix = true`), or detect-and-log only for the first playtests?
   *Default:* on; the log records every repair.
3. **Keys**: F7 = resync, F8 = bug report (configurable). *Default:* yes, if the trace shows the game does not use them.
4. **Pause menu does not pause while connected** and has no save buttons. *Default:* yes.
5. **Bug report collects from every connected client** (each writes its own zip and sees a popup), not only the
   reporter. *Default:* yes.
6. **Photo mode, map viewing and the main gate in M1**: decided by the audit; *default:* allowed if they change no
   shared state.

## Risks

- The trace (task 1.1) needs a manual walk through every window and machine — needs the user once, like row 5a's
  machine trace; without it the pie-option ids stay unknown and those options default to blocked.
- Cancelling the scene coroutine may be impossible in IL2CPP → fallback blocks travel at its windows/pie options.
- Blocking a mode change may leave the game stuck → per-mode trace, fallback log-only + pie block.
- Projections may differ for equal state (float noise, client-only fields) → shared Core mappers, quantization, a
  10-minute false-alarm check (task 6.6) before the scenario counts as passing.
- Resends while a player works on the car → `NotReady` during transactions/claims, two stable rounds, backoff.
- A forgotten guard entry blocks a feature that already syncs → owner column, `guard-log` in every scenario's notes.

## Second review (2026-10-06)

Independent review against the code on `main` (row 7 contract groups 1–2 implemented), the stubs (`members.sh`) and
the other drafts (rows 1, 2, 5a, 6, 8, 12). `openspec validate desync-detection-and-resync --strict` passes after the
fixes. All guard hook signatures in Context exist in the stubs as written.

### Judgements on the open points

- **Split (a) — agree.** (a) is M1, client-only hooks plus a manual trace; (b)–(d) are M2/M5 with Core, server and
  packets. Separate milestone, code area and capability, and as one change it cannot be archived before M5. Proposed
  split (not created here): new change `multiplayer-feature-guard` = proposal part (a) and its hooks, design Context
  (windows, pie, modes, scenes, messages, saves/pause), D1–D6 and "What the guard stores", spec
  `multiplayer-feature-guard` unchanged, tasks groups 1–4, review items 2, 5, 6; size M (4–5 sessions, one with the
  user). This change keeps D7–D11, specs `state-reconciliation` and `bug-report-bundle`, tasks 5–8 (renumbered 1–4),
  and depends on the guard change for `FeatureGuard.Bypass` (resync reload) and the guard log (bundle). I would also
  split (d) out (M5, small, shares layout with row 12) — see question 1.
- **(c) reload vs row 6 and row 5a — consistent with row 6, not with 5a.** A garage→garage load goes through row 6's
  scene prefix (`LeavingScene`, `Loading`, claims released, `GameData.Clear()`) and `CustomLoad` → `AskForSync`,
  exactly row 6 D6's "return = late join". Row 5a's `tool-resync` (in-place `AskForSync`) bypasses that and hits row 1's
  "snapshot into empty loaders" assumption. Row 5a is M4, after (c) in M2, so it should use `resync force`.
- **(b) feasibility — feasible at 5 s.** Hashing at receipt is tight because requests and live updates share one
  ordered stream (added to D8). A hash of `ISaveSection.Save()` does not work: the save holds server-only fields the
  client cannot rebuild; the per-section Core mapper is the right choice, with `IServerDigest` on the same
  `[SessionSection]` class. Server cost under `StateLock` is small (a few thousand rows); the real risk is the client
  side (car projection through IL2CPP interop on the main thread), which task 6.6 measures.
- **Refusal bug** (raised in row 9) verified in code; see `mod-compatibility` review.

### Fixed in this pass

1. D4: Junkyard and `Map` moved from row 6 part 2 (M4) to row 1 (M2) — ROADMAP M2 promises "travel to the junkyard
   (parts only)"; junkyard car buying stays with row 6 part 2.
2. D1: harness verbs that act as the player are **not** bypassed (the draft bypassed "harness commands", which would
   have hidden forgotten guard entries from every row's scenario, contradicting the Risks mitigation).
3. Task 4.1: full regression with the guard on `Enforce`; row 6 part 1's `scenes` scenario (lands earlier in M1, uses
   `travel`) gets `guard-allow` in the same commit. D2/task 2.5: row 6 renames `DisconnectHooks` to `SceneHooks`.
4. D3: message through row 8's `ModNotify.Message` (row 8 D1 already says the guard uses it).
5. D10/task 7.1: resync refused while an own car waits for its baseline (row 1 deletes it with `SpawnerLeft` when the
   spawner leaves the garage); coroutine started with `StartCoroutine`; harness `resync [force]` for later rows.
6. D11/proposal/tasks 8.2–8.3: bug-report key F8 → **F9** (row 8's session panel is F8); layout follows
   `release-and-docs` D6 (`client\`, `server\`, `info.txt` — row 12 was drafted first); redaction adds `admin_key`,
   `Together.AdminKey`, `player.json`; the server's save copy drops `players[].Key` (bearer identity keys — the spec
   already forbids them, the draft copied the save as is).
7. Server log path `Logs/` → `Log/` (actual folder, `Program.Main`), incl. `Log/desync/`; wrong task references in
   design (2.1 → 3.1, 4.3 → 6.3, 4.6 → 6.6); D8 one outstanding request per client and server-time measurement.

### Remaining (questions with proposed defaults)

1. **Split into how many changes?** *Default:* three — `multiplayer-feature-guard` (M1, M), this change for (b)+(c)
   (M2, M), `bug-report-bundle` (M5, S). ROADMAP row 14 sizes become M + M + S.
2. **A car under continuous work is never confirmed** (its hashes change every round). *Default:* accept; the resync
   key covers it. Later option: confirm per differing row via `StateDetail`.
3. **Server save copy in the bundle** (row 12's `Collect-Logs.ps1` makes it opt-in). *Default:* include it with
   identity keys removed; the host decides whether to send it. Note for row 12: `-IncludeSave` must strip
   `players[].Key` too.
4. **F7/F9 free in the game?** Checked by the trace (1.1); defaults stand otherwise.
5. INTEGRATION.md additions: rows 1/2 server-initiated resend APIs (item 4 above), `resync [force]` (user: 5a),
   `ModNotify` (owner 8, user 14), guard rule ownership note.

## Integration pass (2026-10-06)

- **Split done for (a) only**: the guard is now `multiplayer-guard` (ROADMAP row 14a, M1) with the guard spec, design
  D1–D6 and task groups 1–4. (d) stays here (user decision: one less change). This change keeps (b)–(d): design
  renumbered D1–D5 (old D7–D11), tasks renumbered groups 1–4 (old 5–8). Size ≈ L (b M, c S, d S).
- Bug-report key is **F8** (F7 resync, F9 is row 8's session panel); preferences `CMS21Together.ResyncHotkey`,
  `CMS21Together.BugReportHotkey`.
- Bundle layout and redaction are the shared ones in INTEGRATION.md (owner: this change): `info.json` (not
  `info.txt`), redaction by `token`/`password`/`secret`/`key` except `*Hotkey*`, server logs in `Log\`.
- Server resends use row 1's `CarsSnapshotProvider.SendCar` and row 2's `ParkingService.SendFullState`
  (INTEGRATION.md APIs, owners 1 and 2). Row 5a now uses `resync force` instead of `tool-resync`.
- Digests for rows 3, 4, 5a: recorded in INTEGRATION.md "Not resolved" (one task each when they land).
- Still open for the user: first-review questions 2 (autofix on by default) and 5 (every client writes a bundle),
  second-review questions 2 (a car under continuous work is never confirmed) and 3 (save copy in the server bundle).
  Settled: the split (guard only); keys (F7/F8, checked by row 14a's trace task 1.1). Moved to row 14a: pause menu
  and photo mode/map/main gate questions.
