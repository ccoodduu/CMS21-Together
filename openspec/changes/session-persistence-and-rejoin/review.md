# Review — session-persistence-and-rejoin (2026-10-05)

## Findings

### Blocker

1. **Per-player level/XP/skills contradicted the user decision** (D7, "Per-player records" requirement, tasks 5.4,
   1.4, migration template). *Changed:* removed. `PlayerRecord` = `Key, Name, LastSeenUtc, Position, Rotation, Scene`;
   `NewPlayerTemplate`, the `Extra` bag, per-receiver `WorldState`/`GarageState`, slot args for `level`/`exp` and the
   `shared`/`self` dump split are gone. New requirement "Shared progression"; v1 migration keeps level/exp/skills in
   `world`/`garage`. Seat is not persisted (row 6 clears it on leave).
2. **The contract could not land first on its own.** Old group 2 added interfaces but nothing used them (save still
   wrote `ModGameState`, `OnAskForSync` unchanged, no client ack/counting, no scenario), so rows 1–6 would have
   plugged into dead code. *Changed:* contract = groups 1–2: envelope save/load through sections + v1 migration,
   `StateLock`, provider-driven `OnAskForSync`, `SyncBegin/SyncEnd/SyncAck`, client `SyncTracker` + 30 s no-progress
   timeout, README, harness server control, scenario `server-restart.ps1` (plus `connect` still passing). Everything
   else is groups 3–7 ("rest, last").

### Major

3. **Harness could not drive a server save/restart.** `Run-Session.ps1` does not back up `Server\Saves`, Terminal.Gui
   owns console input so `/save`/`/stop` cannot be sent, and `CloseMainWindow()` does not reliably reach a save.
   *Changed:* server `--command-file` (harness only), `Send-ServerCommand`, `Wait-ServerLog`, `Start/Stop-TestServer`,
   `Saves` backup/restore (task 1.1).
4. **Item counts in `SyncBegin` needed `CountItems` + `SendSnapshot` to agree.** *Changed:* counts move to `SyncEnd`;
   `SendSnapshot` returns its count; `CountItems` removed (row 1 already assumed "at `SyncEnd` the client knows the
   full set").
5. **Save threading.** "Save after a player leaves" ran on socket threads inside the re-entrant lock (so not "outside
   the lock"), and concurrent saves raced on the `.tmp`. *Changed:* saves only on the main loop via `RequestSave()`;
   `Client.Disconnect`/`Client.Update` under `StateLock`; a `saveGate` for the console-close handler.
6. **Shutdown save would not fire.** Terminal.Gui consumes Ctrl+C (`CancelKeyPress` never fires) and .NET Framework
   does not reliably raise `ProcessExit` on console close. *Changed:* `SetConsoleCtrlHandler(CTRL_CLOSE_EVENT)`.
7. **Crash recovery of `selectedProfile` in `OnInitializeMelon` is too early** (no `GameManager` yet) and the game
   may read the pref before the menu. *Changed:* prefix on `ProfileManager.Awake()`, task 6.3 first logs the read
   order, and drops the pref write entirely if `StartGame` works without it.
8. **Hash skip could never skip**: the hashed string contained `SavedAtUtc`. *Changed:* hash `Sections` only.
9. **Cross-change conflicts** — see the list below; left for those changes (outside this folder).

### Minor

10. Added `PlatformManager.DeleteSave(string)` (non-generic, patchable) to the blocked entry points.
11. `SaveUtils.RestoreProfileDataSize` was cited as existing; marked as new.
12. `File.Replace` can fail on a file held by antivirus/editors: retry 3×, keep the old main file.
13. `SendToClients` skips loading clients, so the shutdown notice now goes to every connected slot explicitly.
14. Spec gaps filled: duplicate section key, consistent lock, window close, crashed client rejoining (half-open
    slot), backup failure; harness scenario `duplicate-identity.ps1` added.
15. Over-engineering trimmed: design 320 → ~265 lines in Part A / Part B; `/players` kept (cheap, operator need).
16. Verified in the stubs: `GameDataManager.Save(int)`, `.ProfileData`, `.CurrentProfileData`;
    `ProfileManager.Awake/Load/Save/BackupSave/DeleteProfile/DeleteSelectedProfile`, `selectedProfile`;
    `RDGPlayerPrefs.GetInt/SetInt/Save/CheckFile`; `GlobalStrings.SaveDirectory`; `PlatformManager.SendSave<T>`
    (generic) and `DeleteSave(string)`; `PlayerData.SetPosition/SetRotation/IsDefault`;
    `FPSInputController.SetCharacterControllerPosition(bool)`; `CarLoader.IsCarLoaded()`;
    `NotificationCenter.SelectSceneToLoad(string, SceneType, bool useFader, bool saveGame)`. Repo references
    (`GameDataManager`, `RotateBackups`, `AuthHandler.OnAskForSync/OnConnected/HandleConnect`, `Tcp/Udp.HandleData`,
    `SteamTransport.OnMessage/OnConnectionChanged`, `Client.SteamID`, `CommandSystem.Execute`,
    `ServerWindow.TickServer`, `ModGameManager.StartGame`, `SaveUtils.ExtendProfileDataSize`,
    `WaitForSyncCompletion`, `ClientData.IsInitialSyncFinished/IsServerUpdating`) all exist.

## Conflicts to fix in other changes

The state lock is `GameDataManager.StateLock` everywhere (row 1's name wins; row 7 creates it). Every row registers
an `ISnapshotProvider` in its `SyncOrder` slot and counts items with `SyncTracker.Applied(key)` instead of sending
from `OnAskForSync` "before `SyncEnd`" or adding its own "synced" flag.

- `sync-car-parts`: task 2.1 and the D-section on concurrency ("this change introduces `StateLock`") → use the
  contract's lock. D9 / task 2.6 → provider `cars` (100). Task 4.3 `IsCarsSynced` + "10 s without progress" →
  `Applied("cars")` + the contract's 30 s timeout. The `CarState` shape change bumps section `cars` to v2 (dropping
  cars without baseline is its migration).
- `sync-car-placement-and-lifts`: design §10 step 5 / task 2.5 → provider `car-placement` (200); task 5.1
  `IsParkingSynced` → counting; task 1.4 `PlacementState` → also an `ISaveSection` `car-placement`.
- `sync-orders-and-jobs`: D10 / task 3.10 send `JobsState` "after `GarageState`, before the inventory" → slot `jobs`
  (400, after cars; its "whichever happens last" customer-car marking already allows that). Task 2.3 `JobsState` →
  `ISaveSection` `jobs`; the expiry timer runs under `StateLock`.
- `sync-car-details`: D10 step 2 "right after each car" and assumption A1 "per-car slot in `OnAskForSync`" → provider
  `car-details` (150) after all cars (its per-loader queue already handles order). D10 step 5 → `Applied("car-details")`
  per car. `CarState.Details` stays inside section `cars` (additive, no bump).
- `sync-workshop-tools`: D10 / task 3.3 → provider `workshop-tools` (300); task 2.4 `ToolsState` → `ISaveSection`
  `workshop-tools`. Its Migration Plan question is answered: missing sections reset, unknown ones are kept.
- `sync-players-and-scenes`: D9 / task 3.4 → provider `players` (500). Its presence record (D3) is where row 7 copies
  name, scene and last movement from; keep those fields.
- `openspec/ROADMAP.md`: "task group 2 (the contract)" → "task groups 1–2"; the integration note about removing
  row 7's per-player progression is done.

## Open questions for the user

1. **Duplicate identity**: when the same player key connects twice, the first connection stays and the second is
   refused (a crashed client waits up to 10 s). *Recommended default: first wins.*
2. **Rejoin position**: a returning player spawns where they left the garage (not at the default spawn). *Recommended
   default: yes.*

## Integration pass (2026-10-06)

- D2: added the `cars` section version history (v1 contract, v2 `sync-car-parts`, v3 `sync-car-details`; row 2's
  fields additive) — answers `sync-car-details`' request for one bump each. Slot 300 owner renamed to row 5a
  (`sync-workshop-machines`, split from `sync-workshop-tools`).
- D4: snapshot vs live rule — snapshot packets arrive between `SyncBegin` and `SyncEnd`, snapshot handlers never wait
  for `IsInitialSyncFinished`; live garage-bound packets before `SyncAck` are queued by row 6's `ClientScene` gate.
  Task 2.7: `SyncTracker.InSnapshot`.
- D11: row 3 may set `ProfileData.FinishedTutorial = true` on the in-memory session profile in `StartGame`
  (coordination asked by `sync-orders-and-jobs`).
- Already applied before this pass (by the user): `PlayerPresence` in the `[AllowBeforeSync]` list, task 4.4 reuses
  row 6's `teleport`.
