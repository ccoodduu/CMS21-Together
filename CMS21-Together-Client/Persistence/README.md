# Client save safety

A multiplayer session must never write, delete or switch the player's own game profiles. Design:
`openspec/changes/session-persistence-and-rejoin/design.md` (D11).

## Where the game keeps its profiles

- Profile files: `Application.persistentDataPath/Save/profileN.cms21b`, with the game's own copies in
  `.../Backup/`. `persistentDataPath` is `%USERPROFILE%\AppData\LocalLow\<company>\Car Mechanic Simulator 2021`.
- The selected profile: `RDGPlayerPrefs.GetInt/SetInt("selectedProfile")`, stored as a Unity PlayerPrefs value in
  the registry (`HKCU\Software\<company>\Car Mechanic Simulator 2021`, value `selectedProfile_h2169348711`).
  The in-memory copy is `ProfileManager.selectedProfile`.
- `<company>` is `Red Dot Games` in the real game. Test installs patch it to `RDGTogether-<X>`, so every test
  install has its own profile folder and registry key (`tools/test-env/TestLanes.psm1`).

The harness verb `profile-pref` returns the pref, the field, the number of profile slots and whether the guard is on.

## What the mod does

- `ModGameManager.StartGame` backs up `Save/` and `Backup/` to `UserData/CMS21Together/ProfileBackups/<ts>/` once per
  game run (keeps 5) and aborts the join when that fails (`ProfileBackup`).
- The session runs in an extra in-memory profile slot 4 (`ProfileData` grown to 5, `selectedProfile = 4`). The
  pref is not written.
- `SessionGuard` is active from `StartGame` until the Menu scene has loaded (or the game quits). While active it
  blocks `GameDataManager.Save(int)`, `ProfileManager.Save/BackupSave/DeleteProfile/DeleteSelectedProfile` and
  `PlatformManager.DeleteSave(string)`, logging each once with the call stack. On end it shrinks `ProfileData`
  back to 4 slots and restores `selectedProfile`.
- `profile4.cms21b` written by older mod versions is never deleted, only logged.

## Player identity

`PlayerIdentity` creates `UserData/CMS21Together/player.json` (`{ "PlayerKey": "<Guid N>" }`) once per install
and sends the key as `ConnectPacket.playerKey` (design D8). On DirectIP the server knows the player by it, so never
copy the file between installs (`Setup-TestInstalls.ps1` skips `UserData/CMS21Together`) and never put it in a bug
report. On Steam the server uses the Steam ID and ignores the key. The harness verb `player-key [<key>|reset]`
overrides it in memory.
