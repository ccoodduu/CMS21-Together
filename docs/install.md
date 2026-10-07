# Installing CMS21 Together

This guide installs the multiplayer mod for Car Mechanic Simulator 2021. It takes about ten minutes. To host a game
for your friends, read [hosting.md](hosting.md) afterwards.

## What you need

- **Car Mechanic Simulator 2021 on Steam.** Steam must be running when you play. You don't need any DLC.
- **MelonLoader 0.5.7.** Exactly this version: MelonLoader 0.6 does not work with the mod.
- **The client zip**, named `CMS21-Together-<version>-client.zip` (for example `CMS21-Together-1.0.0-client.zip`).
  Everyone in a game must use the same zip. Ask your host which version they run.
- **No gameplay mods.** Mods that change how the game plays are refused by the server, for example QoLmod,
  TK Aftermarket, TK Basics, LvxBetterCarSpawns, LvxOwnedCarsOnly and QuickShop. Autosave mods are refused too: the
  server saves multiplayer games itself. Visual mods and CMS21LoadOptimizer are fine. If you need a gameplay mod, ask
  the host to allow it. Keep a second copy of the game without them if you also play with them alone.

## 1. Back up your saves

The mod does not touch your own saves: a multiplayer game is saved on the server. A backup still costs nothing.

1. Press **Windows + R**, paste `%USERPROFILE%\AppData\LocalLow\Red Dot Games\Car Mechanic Simulator 2021` and press
   **Enter**.
2. Copy the whole folder somewhere safe, for example to your Desktop.

The mod also makes its own copy of your save profiles every time you join a multiplayer game. It keeps the last five
in `UserData\CMS21Together\ProfileBackups` in the game folder.

## 2. Install MelonLoader 0.5.7

1. Open the game folder: in Steam, right-click **Car Mechanic Simulator 2021** > **Manage** > **Browse local files**.
   Keep this window open, you need it again.
2. Download the MelonLoader installer from https://github.com/LavaGang/MelonLoader/releases/tag/v0.5.7
   (`MelonLoader.Installer.exe`) and run it.
3. Click **SELECT** and pick `Car Mechanic Simulator 2021.exe` in the game folder.
4. Untick **Latest** and choose **v0.5.7** in the list. Click **INSTALL**.
5. Start the game once and quit it. A black MelonLoader console window opens with the game. The first start takes
   longer than usual. Afterwards the game folder has the folders `MelonLoader`, `Mods`, `UserData` and `UserLibs`.

## 3. Install the mod

1. Close the game.
2. Unzip `CMS21-Together-<version>-client.zip` into the game folder. If Windows asks, choose **Replace the files in
   the destination**.
3. Check that these files are now in the game folder:
   - `Mods\CMS21-Together.dll`
   - `UserLibs\CMS21_Together_Core.dll`, `UserLibs\Facepunch.Steamworks.Win64.dll` and `UserLibs\steam_api64.dll`
   - the folder `TogetherServer` (the server program, for hosting)
   - `CMS21-Together-TRY-IT.txt`, `CMS21-Together-release.json`, `Collect-Logs.bat` and `Collect-Logs.ps1`

   The `.pdb` files next to the dlls are normal. They make error reports more useful.
4. Start the game. The MelonLoader console shows `Together Mod <version> initialized!` and
   `Steamworks initialized successfully.`
5. In the main menu, a **Multiplayer** button is now in the top right corner.

## 4. Set your name

Your Steam name is used unless you set another one. Click **Multiplayer** and type it in the name field. Or close the
game, open `UserData\MelonPreferences.cfg` in the game folder with Notepad, find the section `[CMS21Together]` and set
`PlayerName = "YourName"`.

## 5. Join a game

Click **Multiplayer** in the main menu. On the **Join** tab, type the address your host gives you and click **Join**:

- the host's IP address, for example `192.168.1.20` (or `IP:port` if the host uses a port other than 7777);
- or the host's Steam server ID (17 digits, shown in the server window).

With Steam it is easier: when your friend is in a session, right-click them in the Steam friends list and choose
**Join Game**. This also works when your game is closed. The **Friends** tab in the Multiplayer panel shows the same.

If the server has a password, the Join tab asks for it.

In a game, these keys help:

| Key | What it does |
|---|---|
| **F7** | Reloads the garage from the server, if something looks different from what your friends see |
| **F8** | Saves a bug report (see [bug-reports.md](bug-reports.md)) |
| **F9** | Session panel: players, ping, your Steam friends, and **Kick** for the host |

## Updating

Everyone has to update at the same time: the server refuses a different version.

1. Close the game.
2. Unzip the new client zip into the game folder and replace all files.
3. If you host from the game, the new server comes with it in `TogetherServer\`. Your session save in
   `TogetherServer\Saves\` and your settings in `TogetherServer\server_config.ini` are not in the zip, so they stay.
   A dedicated server is updated the same way with the new server zip, see [hosting.md](hosting.md#updating-the-server).

A newer server loads saves of older versions. An older server refuses a save written by a newer one, so don't go back
to an older version with the same save.

## Uninstalling

1. Close the game.
2. Delete these from the game folder:
   - `Mods\CMS21-Together.dll` and `Mods\CMS21-Together.pdb`
   - `UserLibs\CMS21_Together_Core.dll`, `UserLibs\CMS21_Together_Core.pdb`, `UserLibs\Facepunch.Steamworks.Win64.dll`
     and `UserLibs\steam_api64.dll`
   - the folder `TogetherServer` (move `TogetherServer\Saves` somewhere else first if you want to keep the session)
   - `CMS21-Together-TRY-IT.txt`, `CMS21-Together-release.json`, `Collect-Logs.bat` and `Collect-Logs.ps1`
   - the folder `UserData\CMS21Together` (your player key `player.json`, the profile backups and bug reports)
3. Optional: in `UserData\MelonPreferences.cfg`, delete the sections `[CMS21Together]` and `[CMS21Together_Guard]`.
4. To remove MelonLoader too, run its installer again and click **UN-INSTALL**.

Your own single-player saves are not affected.

## Troubleshooting

### The game starts without the MelonLoader console, or the Multiplayer button is missing

- Check the MelonLoader version: the first lines of the console (or of `MelonLoader\Latest.log`) show it. It must be
  0.5.7. Install it again as in step 2.
- Check that `Mods\CMS21-Together.dll` is in the game folder, not in a subfolder like
  `CMS21-Together-1.0.0-client\Mods`. If it is, the zip was unzipped into its own folder: move its contents up.

### "Steam DLL not found in UserLibs. Switching to Non-Steam mode."

`UserLibs\steam_api64.dll` is missing. Without it, Steam joins, **Join Game** and the Friends tab do not work; joining
by IP address still does. Unzip the client zip again. If the file disappears again, your antivirus removed it (see
below).

### "Steamworks could not be initialized"

Steam is not running, or you started the game without Steam. Start Steam first, then the game.

### Your antivirus blocks a file

The mod's files are not signed, so some antivirus programs flag `CMS21_Together_Server.exe`, `steam_api64.dll` or
`Collect-Logs.bat`. Restore the file from the antivirus quarantine and allow it, or unzip the zip again.

### A join fails

The Multiplayer panel says why. What to do:

| Message | What to do |
|---|---|
| Could not reach the server. Check the address and that the server is running. | Check the address and that the server window is open. Over the internet the host needs port forwarding or Steam (see [hosting.md](hosting.md#steam-or-ip-address)). |
| The server did not answer in time. | Try again. If it keeps happening, the host's port forwarding or firewall is the likely cause. |
| Steam is not available, so Steam joins do not work. Join by IP instead. | Start Steam, or see "Steam DLL not found" above. |
| The Steam connection failed. | Try again, or join by IP address. |
| The server runs another version of Together. | You and the host have different zips. The message names both versions. Everyone installs the same zip. |
| Your game version differs from the server's. | Update the game in Steam; the host does the same. The message names both versions. |
| Can't join: gameplay mods differ. | The message names each mod and what it changes. Remove them from your `Mods` folder, or ask the host to allow it (`mods_ignored` or `mods_required` in [hosting.md](hosting.md#all-server-settings)). |
| Wrong server password. | Type the password the host gave you in the password field that now shows on the Join tab. |
| The server is full. | Wait until someone leaves, or ask the host to raise `max_players`. |
| You are already connected to this server from another game. | Close the other game. If nobody else is playing as you: did you copy your game folder to a friend, including `UserData\CMS21Together\player.json`? Then delete that file on the copy. |
| The server could not identify you. | Restart the game and join again. If it keeps happening, send a bug report. |
| Loading the shared garage failed. | Join again. If it keeps happening, send a bug report. |
| You were kicked. | The host removed you. You can join again. |
| The server was shut down. | The host stopped the server. |

Different DLC does not stop you from joining. Cars and parts from a DLC that not every player owns are blocked while
you are connected.

### "... is not supported in multiplayer yet"

That part of the game is not shared yet. While you are connected, the game blocks it so that your garage cannot drift
apart from your friends'. The README lists what is shared.

### Something looks different from what your friends see

Press **F7**: the garage reloads from the server. If it happens again, press **F8** right away and send a bug report
([bug-reports.md](bug-reports.md)).
