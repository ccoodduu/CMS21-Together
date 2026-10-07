# Reporting a bug

Something went wrong? A report with logs helps a lot more than a description alone. There are two ways to collect the
logs. Both make one zip file and hide passwords and keys.

| When | How |
|---|---|
| The game still runs | Press **F8** |
| The game crashed, froze or does not start | Double-click `Collect-Logs.bat` in the game folder |

Then send the zip files with a few lines:

- what you did,
- what you expected,
- what happened instead,
- roughly when (the time), and whether you were the host.

Send them to whoever develops the mod for your group, for example in an issue on this repository's GitHub page.

## In the game: F8

Press **F8** right after the problem, while you are still in the game.

1. A message shows `Bug report <id> saved: ...\UserData\CMS21Together\BugReports\<id>.zip`. The `<id>` is the date,
   the time and four letters, for example `20261007-143005-3fa2`.
2. If you are connected, the server and every other player's game save their part too, with the same id. The other
   players see a short message about it.
3. Everyone sends their own zip:
   - each player: `UserData\CMS21Together\BugReports\<id>.zip` in their game folder;
   - the host: also the server's `BugReports\<id>.zip`. When you host from the game, that is
     `TogetherServer\BugReports\<id>.zip` in your game folder. In the server window, `/bugreport` lists them.

You can make one report every 30 seconds. To use another key, change `BugReportHotkey` in the section
`[CMS21Together]` of `UserData\MelonPreferences.cfg`.

## After a crash: Collect-Logs

1. Open the game folder (in Steam: right-click the game > **Manage** > **Browse local files**).
2. Double-click `Collect-Logs.bat`. A black window opens for a few seconds.
3. The zip `CMS21Together-logs-<id>.zip` is now on your Desktop, and a folder window shows it.
4. Press a key to close the black window.

Run it after the problem, before you start the game again: the game replaces its newest log at the next start (the
older ones are kept in `MelonLoader\Logs`).

From the game folder, `Collect-Logs.bat` also packs the logs of the server in `TogetherServer\`, so a host gets both in
one zip. A dedicated server has its own `Collect-Logs.bat` next to `CMS21_Together_Server.exe`.

The server's session save is left out unless you are asked for it. Then open the folder with `Collect-Logs.bat`, type
`cmd` in the address bar of the folder window, press **Enter**, and type `Collect-Logs.bat -IncludeSave`.

If Windows warns about the file, choose **More info** > **Run anyway**. If your antivirus removed it, unzip the client
zip again.

## What is in the zip

The zip has a `client` part (your game) and a `server` part (the server, if you host):

- `client`: the MelonLoader logs (`MelonLoader\Latest.log` and the last five runs), the mod's settings from
  `UserData\MelonPreferences.cfg`, the mod's files from `UserData\CMS21Together` except `player.json`, and a list of
  the files in `Mods` and `UserLibs`. F8 adds the list of your mods, the blocked-feature log and the shared state your
  game sees. `Collect-Logs` adds your last three F8 reports.
- `server`: the server logs (`Log\Latest.txt` and the last five runs) and `server_config.ini`. F8 adds the session
  save, the server's view of the shared state, recent desync records and the list of players. `Collect-Logs` adds the
  server's last three F8 reports, and the session save only with `-IncludeSave`.

## Privacy

- Passwords, admin keys and Steam tokens are replaced by `<redacted>`. This covers every setting whose name contains
  `password`, `key`, `token` or `secret` (key bindings like `BugReportHotkey` are kept).
- Your player key (`UserData\CMS21Together\player.json`) is never included. Session saves in a report have the
  players' keys removed.
- The logs do contain player names, Steam names and Steam IDs, the server address you joined, and folder paths (which
  can show your Windows user name). Only send the zip to people you trust with that.
- F8 on a connected game makes the server save a copy of the session (without keys) in its report. `Collect-Logs`
  includes it only with `-IncludeSave`.

## Without the tools

If neither works, send these files by hand:

- game: `MelonLoader\Latest.log` in the game folder (earlier runs in `MelonLoader\Logs\`);
- server: `Log\Latest.txt` in the server folder (earlier runs: `Log\Log_<date>.txt`).
