# Proposal

## Why

Every row syncs only what it hooks. Anything the game lets a player do that no row syncs yet — opening a machine,
travelling, a camera mode, a window with its own economy — silently diverges the clients, and upstream's bug list is
mostly such cases. A list of "known unsynced actions" never catches the next one. Friends play from M1, when only
presence, stats, shop/warehouse and upgrades sync, so everything else has to be refused with a clear message instead
of splitting the session. (Split from `desync-detection-and-resync`, ROADMAP row 14 (a); that change keeps
reconciliation, the resync key and the bug report.)

## What Changes

**Default-deny guard + single-player audit — M1 (ROADMAP row 14a)**
- While connected, only allow-listed **windows** (`WindowManager.Show`), **pie-menu options**
  (`PieMenuController`), **game modes** (`GameMode.SetCurrentMode`) and **scenes**
  (`NotificationCenter.SelectSceneToLoad`) can open; everything else shows "Not supported in multiplayer yet" through
  row 8's `ModNotify.Message` and is logged. One rule table, grown by each row in the commit that syncs the feature;
  the player can override it in the mod's config (accepted by the user).
- Audit of single-player assumptions (pause menu and its save buttons, `Time.timeScale`, camera modes such as
  examine/inspection and photo, the game's and mods' autosave, modal windows over remote changes); each finding gets a
  fix task, a guard rule or a note to the owning row.

**Packets**: none. **`DisconnectReason`**: no new values.

**Hooks**: prefixes on `WindowManager.Show(WindowID, bool)` and `Show(WindowID, Il2CppReferenceArray<Object>)`,
`PieMenuController.CheckSelectedOption()` and a postfix on `PieMenuController.PreparePieMenu()`,
`GameMode.SetCurrentMode(gameMode)`, `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` and the
void entry points `StartSelectSceneToLoad`/`SelectSceneToLoad(string, SceneType, bool)`; audit fixes on
`PauseQuitWindow.CreateSaveButton`/`CreateSaveAndQuitButton` (exact list from the audit).

**Out of scope**: syncing anything the guard blocks (each row owns its feature); state reconciliation, the resync key
and bug reports (`desync-detection-and-resync`).

## Capabilities

### New Capabilities
- `multiplayer-feature-guard`: which game features a connected player can use, what happens when they try one that is
  not supported yet, how the allowed set grows and is overridden, and how single-player-only behaviour (pausing,
  saving, local camera modes) is handled while connected.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Client: new `Guard/` (`FeatureGuard`, `GuardRules`, hooks), `MainMod` (preferences category `CMS21Together_Guard`).
- Uses (not owns): row 7 `SessionGuard.Active`; row 8 `ModNotify` (private fallback until row 8 group 1 lands);
  row 6's scene prefix (`SceneHooks`, formerly `DisconnectHooks`) takes `__runOriginal`.
- Provides to other rows: `FeatureGuard.Decide`, `FeatureGuard.Bypass` (row 14's resync reload, row code that opens
  windows to apply remote changes), `GuardRules` (rows 1–6, 10, 13, 15 add their entries in their merge commit), the
  guard ring buffer (row 14's bug report `guard.log`).
- Test harness: `Features/GuardCommands.cs` (`guard-*` verbs); scenario `guard`.
