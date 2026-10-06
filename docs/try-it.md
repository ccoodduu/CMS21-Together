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
folder. Type `/stop` in the server window to shut it down with a final save.

Or host from the game: in the main menu, click **Multiplayer** > **Host**, choose the settings and press **Start**.
The game starts that server and joins it. **Stop** on the same tab (or **Stop server** in the F9 panel) saves the
session and shuts the server down; quitting the game does the same.

## Join

In the main menu, click **Multiplayer** (top right), type the address and press **Join**:

- Same PC as the server: `127.0.0.1`.
- Another PC: the host's IP address, for example `192.168.1.20` on the same network, or the host's public IP with
  port 7777 forwarded on the host's router (`IP:port` if the server uses another port).
- Steam: when the server runs with `use_steam = True`, its 17-digit Steam server ID (in the server window) works as
  the address. Players in a session show **Join Game** in the Steam friends list, which also works with the game
  closed.

If a join fails, the menu says why (server not reachable, other mod version, server full, wrong password, kicked,
server stopped). After "Wrong server password" the Join tab asks for the password. The address is remembered for next
time. In a session, **F9** shows the players with their ping, your Steam friends, and for the host a **Kick** button.

## What works

- Joining a running server, also late; everyone loads the same garage.
- Shared money, XP, level, scrap and skills; shop, warehouse and inventory; garage upgrades.
- Seeing each other walk around, with name tags; spawning and deleting cars in the garage.

## Not yet

Working on cars (parts, fluids, wheels, paint), lifts and parking, orders and jobs, workshop machines, and trips to
the junkyard, barn or auction are not shared yet. While connected, the game refuses them with "... is not supported
in multiplayer yet", so the garages cannot drift apart.

## Logs for a bug report

- Game: `MelonLoader\Latest.log` (earlier runs in `MelonLoader\Logs\`).
- Server: `Log\Latest.txt` in the server folder (earlier runs: `Log\Log_<date>.txt`).

Send both, with what you did, what you expected, what happened and roughly when.
