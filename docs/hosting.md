# Hosting a CMS21 Together session

There are two ways to host. You can start the server from the game, which is the easy way. Or you can run the
dedicated server yourself, for example on another PC. Both run the same server program, which the client zip
installs in `TogetherServer\` in the game folder.

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
IP address, they need your port forwarded for TCP and UDP.

## Password (IP joins)

When a server has a password, players who join by IP address must enter it. A missing or wrong password ends with
"Wrong server password", and the Join tab then shows a password field. The game remembers the password for that
server until it closes. Players who join through Steam are not asked, unless the server sets `password_steam = True`.

The password travels as plain text. Don't reuse a password that matters to you.

In the server console, `/password set <pw>` changes the password until the server stops, and `/password clear`
removes it.

## Dedicated server settings

`server_config.ini`, next to `CMS21_Together_Server.exe`, has these keys. Older files get any missing key added with
its default.

| Key | Default | Meaning |
|---|---|---|
| `password` | `""` | Password for IP joins; empty = none |
| `password_steam` | `False` | Also ask Steam joins for the password |
| `admin_key` | `""` | Players whose game sends this key are admins; empty = no admin |
| `new_session_difficulty` | `Normal` | Difficulty of a new session when no save exists: `Easy`, `Normal` or `Expert` |

Command-line arguments override the file for one run: `--password <pw>`, `--admin-key <key>`,
`--new-difficulty <Easy|Normal|Expert>`, together with `--port`, `--max-players`, `--use-steam`, `--server-name` and
`--public-address`. The server log shows the effective settings, with the password and admin key only as "set" or
"none".

**Admin key for a dedicated server:** put a long random value in `admin_key`. In your own game, open
`UserData\MelonPreferences.cfg`, find `[CMS21Together]` and set `AdminKey = "<the same value>"`. You are then admin
on that server. Keep the key secret. Bug-report bundles and the log collector hide it. When you host from the game,
the game makes a new key for every start, so you don't need this.

## Session panel and kick

Press **F9** in any scene (`CMS21Together.SessionPanelHotkey`) to open the session panel:

- every player with their name, current scene and ping in milliseconds, including you;
- for the admin, a **Kick** button next to every other player. A kicked player goes back to the main menu with the
  message "You were kicked". They can rejoin, because there are no bans. Everyone else sees "<name> was kicked".
- your Steam friends. **Join** works for a friend in a Together session that can be joined, and **Invite** is shown
  while you are in a session your friends can join.

Players also get a short notice when someone joins or leaves. When you join a session that is already running, you
get one notice saying how many players are online.

The server console has `/kick <id>` (the id is the slot number shown by `/serverinfo`) and `/serverinfo`, which
shows the settings and each player's ping and admin flag.
