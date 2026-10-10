# Hosting a CMS21 Together session

There are two ways to host. You can start the server from the game, which is the easy way. Or you can run the
dedicated server yourself, for example on another PC. Both run the same server program, which the client zip
installs in `TogetherServer\` in the game folder.

The server keeps the shared garage: money, XP, level, skills, inventory, warehouse, cars, parking, jobs and tools.
Every player's game shows the server's state, and nobody's own single-player save is used or changed.

## Host from the game

1. In the main menu, click **Multiplayer** (top right) and open the **Host** tab.
2. Choose the settings:
   - **Continue the saved session**, if the server has a save (`TogetherServer\Saves\server_save.json`). Otherwise
     it says **New session**, and you pick the difficulty: Easy, Normal or Expert.
   - **Start over** starts a new session even though a save exists. You have to confirm it first. The old session
     is moved to `TogetherServer\Saves_old_<date>`, never deleted.
   - **Port** (default 7777), **Max players** (default 4), an optional **password** for IP joins (see below), and
     **Steam joins** on or off.
3. Click **Start**. The server opens minimized in its own console window. Once it accepts connections, the game
   joins it by itself and you are the session's admin.

Starting is refused with a message if:

- the server program is missing. The panel shows the path it looked for. Set `CMS21Together.ServerPath` in
  `UserData\MelonPreferences.cfg` to use another copy.
- a server from the same folder is already running. **Stop it** stops that server, which can be left over after a
  crash.
- another program uses the port.

If the server exits or does not start within 45 seconds, the panel says hosting failed and shows the end of the
server's log (`TogetherServer\Log\Latest.txt`).

**Stopping:** click **Stop** on the Host tab, or **Stop server** in the session panel (F9). The server saves the
session, and every other player sees "The server was shut down". Quitting the game while hosting stops the server
the same way. If you only return to the main menu, the server keeps running, so your friends can keep playing.
**Join** on the Host tab takes you back in.

Friends join through Steam (**Join Game** in the friends list, an invite, or the Friends list in the game). With an
IP address, they need your port forwarded for TCP and UDP (see [Steam or IP address](#steam-or-ip-address)).

## Run a dedicated server

Use the dedicated server when the session should keep running without your game, for example on a second PC. It
needs Windows, but not the game.

1. Unzip `CMS21-Together-<version>-server.zip` into an empty folder, for example `C:\CMS21Server`. (On a PC with the
   client zip installed you can also use `TogetherServer\` in the game folder.)
2. For Steam joins, start the Steam client on that PC.
3. Double-click `CMS21_Together_Server.exe`. The first start creates `server_config.ini`, the folders `Log\` and
   `Saves\`, and a new session.
4. To change settings, stop the server, edit `server_config.ini` with Notepad and start it again. The settings are
   listed [below](#all-server-settings).
5. Stop the server by typing `/stop` in its window and pressing **Enter**. It saves the session first. Closing the
   window also saves.

The server window shows its version, the settings it uses, its Steam server ID (with Steam on) and who joins. Type
`/help` for all commands.

Players who should be admins on a dedicated server need its admin key, see
[Admin key for a dedicated server](#admin-key-for-a-dedicated-server).

## Steam or IP address

Friends can join in two ways:

- **Steam** (`use_steam = True`, the default): friends click **Join Game** in the Steam friends list (or the Friends
  tab in the game), or type the server's 17-digit Steam server ID. The connection goes through Valve's relay, so
  you do not need to forward a port. The PC that runs the server needs the Steam client running.
  The Steam server ID changes at every start unless you set a `GSLT_Token` (see the table); **Join Game** does not
  care.
- **IP address**: friends type your address. On the same network that is your local IP (for example
  `192.168.1.20`). Over the internet it is your public IP, and your router must forward the server's `port`
  (default 7777) to the server PC for **both TCP and UDP**. Add `:port` to the address if you use another port. Set a
  password for internet games (see below). With Steam joins off, put your public IP in `public_address`: then **Join
  Game** in the Steam friends list connects your friends to that address.

With Steam on, the server also opens the query port `port + 1` (7778). It does not need to be forwarded.

## Password (IP joins)

When a server has a password, players who join by IP address must enter it. A missing or wrong password ends with
"Wrong server password", and the Join tab then shows a password field. The game remembers the password for that
server until it closes. Players who join through Steam are not asked, unless the server sets `password_steam = True`.

The password travels as plain text. Don't reuse a password that matters to you.

In the server console, `/password set <pw>` changes the password until the server stops, and `/password clear`
removes it.

## All server settings

`server_config.ini`, next to `CMS21_Together_Server.exe`, has these keys. Older files get any missing setting added
with its default when the server starts. Changes take effect at the next start.

| Key | Default | Meaning |
|---|---|---|
| `max_players` | `4` | Most players at the same time. The mod is made for 2 to 4 |
| `server_name` | `"CMS21 Together Server"` | Name shown to joining players |
| `public_address` | `""` | Your public IP or host name. With Steam off, **Join Game** in the Steam friends list connects to it |
| `port` | `7777` | Port for IP joins, TCP and UDP. Steam also uses `port + 1` |
| `use_steam` | `True` | Steam joins on or off |
| `GSLT_Token` | `""` | Optional Steam game server login token, for a Steam server ID that stays the same. Make one at https://steamcommunity.com/dev/managegameservers (app id 1190000). Empty = anonymous |
| `autosave_interval_seconds` | `300` | Seconds between autosaves; only written when something changed. `0` = off |
| `backup_count` | `5` | Number of rotating backups in `Saves\backups` |
| `desync_check_interval_seconds` | `5` | Seconds between state comparisons with each player |
| `desync_autofix` | `True` | Resend the server's state when a player's game is confirmed out of sync |
| `password` | `""` | Password for IP joins; empty = none |
| `password_steam` | `False` | Also ask Steam joins for the password |
| `admin_key` | `""` | Players whose game sends this key are admins; empty = no admin |
| `new_session_difficulty` | `Normal` | Difficulty of a new session when no save exists: `Easy`, `Normal` or `Expert` |
| `new_session_money` | `4000` | Money of a new session when no save exists (a new profile in the game starts with 4000) |
| `new_session_level` | `1` | Level of a new session when no save exists (a new profile in the game starts at level 1) |
| `game_version` | `auto` | Game version every player must have. `auto` = the version of the first player who joins |
| `mods_required` | empty | Gameplay mods every player must have, comma separated: `Name` or `Name@Version` |
| `mods_ignored` | empty | Mods to allow even though the mod check thinks they change gameplay |
| `mods_gameplay` | empty | Mods to refuse even though the mod check thinks they are visual |
| `travel_fees` | `True` | Charge the travel fee for trips to the junkyard, barns and the auction, for every player |
| `max_car_sale_price` | `5000000` | Highest price a player may sell a car for |
| `max_car_purchase_price` | `5000000` | Highest price a player may pay for a car |
| `job_stats_to` | `contributors` | Who gets the Steam stats and achievements of a finished job besides its finisher: `contributors` (players who worked on it), `garage` (everyone in the garage) or `finisher` (nobody else). The server command `jobs stats-to <rule>` changes it until the next start |
| `track_collisions` | `on` | Players' cars collide on the test, race and speed tracks; `off` turns it off for everyone |
| `log_level` | `1` | `1` = detailed log (helps with bug reports), `0` = less |

Command-line arguments override the file for one run: `--password <pw>`, `--admin-key <key>`,
`--new-difficulty <Easy|Normal|Expert>`, together with `--port`, `--max-players`, `--use-steam`, `--server-name` and
`--public-address`. The server log shows the effective settings, with the password and admin key only as "set" or
"none".

Money, XP, level and skills are shared by everyone in the session; there are no per-player wallets.

### Admin key for a dedicated server

Put a long random value in `admin_key`. In your own game, open `UserData\MelonPreferences.cfg`, find
`[CMS21Together]` and set `AdminKey = "<the same value>"`. You are then admin on that server. Keep the key secret.
Bug-report bundles and the log collector hide it. When you host from the game, the game makes a new key for every
start, so you don't need this.

## Saves, autosave and backups

The session lives in `Saves\server_save.json` in the server folder (`TogetherServer\Saves\` when you host from the
game). The server writes it:

- every `autosave_interval_seconds` (5 minutes) when something changed;
- when you type `/save`;
- when the server stops (`/stop`, **Stop** on the Host tab, closing the window).

A save never leaves a half-written file behind, even if the PC crashes. Before each write, the previous save moves to
`Saves\backups\server_save_bak1.json` (older ones to `bak2` ... up to `backup_count`). At every start the server also
keeps a copy of the save it started from (`Saves\backups\start_<date>.json`, the last three).

If the save cannot be read, the server moves it to `Saves\corrupt\` and loads the newest backup that works. If none
works, it does not start, so nothing gets overwritten. It also refuses a save written by a newer server version.

**To go back to a backup:** stop the server, copy the backup you want over `Saves\server_save.json`, and start the
server.

**To start a new session:** stop the server and move the `Saves` folder somewhere else. The next start creates a new
session with `new_session_difficulty`. (Host from the game: **Start over** does this for you.)

## Move a session to another PC

1. Stop the server with `/stop` (or **Stop** on the Host tab), so the save is complete.
2. Copy `Saves\server_save.json` and `Saves\backups\` to the new server folder's `Saves\`. That is either a folder
   with the server zip, or `TogetherServer\` in the new host's game folder.
3. Copy `server_config.ini` too if you want the same settings.
4. Start the server there. It must be the same version or newer.

Everything in the session comes along. Players keep who they are: Steam players by their Steam ID, players who join
by IP by the key in their own game (`UserData\CMS21Together\player.json`). The new server has a different Steam server
ID and maybe a different address, so tell your friends, or let them use **Join Game**.

## Updating the server

1. Stop the server with `/stop`.
2. Unzip the new server zip into the server folder and replace all files. The zip has no `server_config.ini`, `Saves\`
   or `Log\`, so your settings and the session stay.
3. Start the server. It adds new settings to `server_config.ini` with their defaults.

Everyone has to update their game to the same version. A newer server loads the older save; do not go back to an older
server with it afterwards.

## Session panel and kick

Press **F9** in any scene (`CMS21Together.SessionPanelHotkey`) to open the session panel:

- every player with their name, current scene and ping in milliseconds, including you;
- for the admin, a **Kick** button next to every other player. A kicked player goes back to the main menu with the
  message "You were kicked". They can rejoin, because there are no bans. Everyone else sees "<name> was kicked".
- your Steam friends. **Join** works for a friend in a Together session that can be joined, and **Invite** is shown
  while you are in a session your friends can join.

Players also get a short notice when someone joins or leaves. When you join a session that is already running, you
get one notice saying how many players are online.

## Server commands

Type them in the server window; every command starts with `/`.

| Command | What it does |
|---|---|
| `/help` | Lists all commands |
| `/stop` | Saves and stops the server |
| `/save` | Saves the session now |
| `/serverinfo` | Settings, and each player's ping and admin flag |
| `/kick <id>` | Kicks a player; the id is the slot number from `/serverinfo` |
| `/password set <pw>`, `/password clear` | Changes or removes the password until the server stops |
| `/players` | Stored players: name, last seen, last place |
| `/compat` | Game version, shared DLC, mod lists and the last refused joins |
| `/bugreport` | Lists the bug reports in `BugReports\` (see [bug-reports.md](bug-reports.md)) |
