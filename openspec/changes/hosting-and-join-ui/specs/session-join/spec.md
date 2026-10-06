# Spec Delta

## Purpose

Defines how a player joins a multiplayer server from the game: the ways to name a server, joining through Steam
friends, what the player sees while connecting, and how every failure or disconnect is reported and recovered from.

## ADDED Requirements

### Requirement: Join by address or server ID
The main menu SHALL offer a multiplayer panel where the player can join a server by typing an address
(`host` or `host:port` with an IPv4 address or host name, default port 7777) or a Steam server ID. Input that is
neither SHALL be rejected with a message and SHALL NOT start a connection. No server ID SHALL be built into the mod.

#### Scenario: Join by address with port
- **WHEN** the player enters `192.168.1.20:7800` and chooses Join
- **THEN** the client connects over DirectIP to 192.168.1.20 on port 7800

#### Scenario: Invalid input
- **WHEN** the player enters `abc:xyz` and chooses Join
- **THEN** the panel shows that the address is invalid and no connection is attempted

### Requirement: Remember the last server
After a join reaches the session, the client SHALL remember that server and offer it as the default the next time
the panel opens, also after a game restart.

#### Scenario: Rejoin after restart
- **WHEN** a player joined `10.0.0.5` yesterday and opens the multiplayer panel today
- **THEN** the panel offers `10.0.0.5` as the server to join

### Requirement: Display name in the join panel
The join panel SHALL let the player view and change their display name before joining; the change SHALL be stored in
the same setting the session uses for the player's name. The field SHALL NOT be editable while connected.

#### Scenario: Name set in the panel
- **WHEN** the player types `Ann` in the name field and joins
- **THEN** other players see the player as `Ann`

### Requirement: Connection status
While joining, the client SHALL show the current stage (connecting, waiting for the server, loading the garage,
synchronising) until the player is in the session, and SHALL end every attempt in exactly one of: in session,
failed with a reason, or idle.

#### Scenario: Normal join
- **WHEN** a player joins a running server
- **THEN** the status passes through connecting, loading and synchronising, and disappears once the player can move
  in the garage

### Requirement: Failure messages
Every failed join SHALL end in the main menu with one readable message naming the cause: server unreachable, no
answer in time, Steam not available, Steam connection failed, server full, wrong password, version mismatch,
game version, DLC or mod difference (with the details the server sends), duplicate identity or synchronisation
failed. A version mismatch message SHALL name both the server's and the player's mod version.

#### Scenario: Server not running
- **WHEN** a player joins an address where no server listens
- **THEN** within 12 seconds the menu shows that the server could not be reached

#### Scenario: Version mismatch
- **WHEN** a player with mod version 0.6.0 joins a server running 0.6.1
- **THEN** the player is back in the main menu with a message naming 0.6.1 as the server's version and 0.6.0 as theirs

### Requirement: Disconnect messages
When a player in a session is disconnected by the server, the client SHALL return to the main menu and show why
(kicked, server shut down, or the reason the server sent). The player's own single-player saves SHALL NOT be written.

#### Scenario: Server shuts down
- **WHEN** the host stops the server while a friend is in the garage
- **THEN** the friend is returned to the main menu with a message that the server was shut down

### Requirement: Join again after a failure
After any failed or ended session the player SHALL be able to start a new join without restarting the game.

#### Scenario: Retry after unreachable server
- **WHEN** a join fails because the server was not running, the host then starts it, and the player joins again
- **THEN** the second join reaches the session

### Requirement: Server information on connect
On connect the server SHALL tell the client its name, Steam server ID (if Steam is enabled), configured public
address and port, player limit, difficulty, whether a password is required and whether this client is an admin.

#### Scenario: Steam ID known after a DirectIP join
- **WHEN** a player joins a Steam-enabled server over DirectIP
- **THEN** the client knows the server's current Steam ID

### Requirement: Steam friends can join through rich presence
While in a session and with Steam available, the client SHALL advertise a join string for its server as Steam rich
presence unless the player turned this off, preferring the server's Steam ID, then a configured public address, then
a non-local address the player joined. Leaving the session SHALL clear it.

#### Scenario: Friend joins from the Steam friends list
- **WHEN** Ann is in a session on a Steam-enabled server and her friend Bob, running the game in the main menu,
  chooses "Join Game" on Ann in Steam
- **THEN** Bob's game joins Ann's server without Bob typing anything

#### Scenario: No joinable address
- **WHEN** Ann joined her own server over 127.0.0.1 and the server has neither Steam nor a public address
- **THEN** no join string is advertised and the panel tells Ann that friends cannot join her through Steam

### Requirement: Join requested at game start
When the game is started by Steam with a join string, the client SHALL join that server once the main menu is ready.

#### Scenario: Cold start from an invite
- **WHEN** Bob accepts Ann's Steam invite while the game is closed
- **THEN** the game starts and, once the main menu has loaded, joins Ann's server

### Requirement: Join request while in a session
A join request received while the player is already connected SHALL NOT disconnect the player without
confirmation.

#### Scenario: Request during a session
- **WHEN** Bob is in a session and accepts an invite to another server
- **THEN** Bob is asked whether to leave the current session, and stays if he declines
