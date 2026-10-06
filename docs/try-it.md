# CMS21 Together: how to try this build

A development build of the multiplayer mod for Car Mechanic Simulator 2021. The version is in the zip name (for
example `0.6.0-dev.540`). Host and players must all use the same zip: the server refuses any other build.

## You need

- Car Mechanic Simulator 2021 on Steam, with Steam running. No DLC is needed.
- MelonLoader **0.5.7** exactly (not 0.6): https://github.com/LavaGang/MelonLoader/releases/tag/v0.5.7 - run the
  installer, pick `Car Mechanic Simulator 2021.exe`, untick "Latest" and choose v0.5.7.

## Install

1. Back up your saves: copy `%USERPROFILE%\AppData\LocalLow\Red Dot Games\Car Mechanic Simulator 2021` somewhere
   safe. The mod does not write your own save profiles during a multiplayer session, but a backup costs nothing.
2. Open the game folder (Steam: right-click the game > Manage > Browse local files) and unzip
   `CMS21-Together-<version>-client.zip` into it, overwriting older files. `Mods\CMS21-Together.dll` must end up
   next to `Car Mechanic Simulator 2021.exe`.
3. Start the game. The MelonLoader console shows `Together Mod <version> initialized!`.
4. Optional: your name above your head. After the first start, open `UserData\MelonPreferences.cfg`, find
   `[CMS21Together]` and set `PlayerName = "YourName"` (empty = your Steam name).

## Host

Run `TogetherServer\CMS21_Together_Server.exe` from the game folder (or unzip
`CMS21-Together-<version>-server.zip` on another PC and run `CMS21_Together_Server.exe` there). The first start
creates `server_config.ini` next to it. The shared garage is saved in `Saves\server_save.json` in the server
folder. Type `stop` in the server window to shut it down with a final save.

## Join

In this build you join a server on the same PC: wait for the main menu and press **F5** (connects to `127.0.0.1`,
port 7777). Joining from another PC (Steam "Join Game", typing an address) comes with the join menu in a later build.

## What works

- Joining a running server, also late; everyone loads the same garage.
- Shared money, XP, level, scrap and skills; shop, warehouse and inventory; garage upgrades.
- Seeing each other walk around, with name tags; spawning and deleting cars in the garage.

## Not yet (expect things to go out of sync)

Working on cars (parts, fluids, wheels, paint), lifts and parking, orders and jobs, workshop machines, and trips to
the junkyard, barn or auction. This build does not block them yet, so leave them alone while connected.

## Logs for a bug report

- Game: `MelonLoader\Latest.log` (earlier runs in `MelonLoader\Logs\`).
- Server: `Log\Latest.txt` in the server folder (earlier runs: `Log\Log_<date>.txt`).

Send both, with what you did, what you expected, what happened and roughly when.
