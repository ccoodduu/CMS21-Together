# Spec Delta

## Purpose

Defines how a player hosts a session from the game by running the bundled dedicated server, which settings a new
session takes, and how the optional DirectIP password protects a server.

## ADDED Requirements

### Requirement: Start a local server from the game
The multiplayer panel SHALL let the player start the dedicated server that ships with the mod as a separate process
with chosen settings (player limit, password, Steam on or off) and SHALL join it automatically once it accepts
connections. Starting SHALL be refused with a message when the server program is missing, a server is already
running, or the port is in use.

#### Scenario: Host and join
- **WHEN** the player chooses Host with default settings
- **THEN** a server process starts and the player reaches the garage of that server without typing an address

#### Scenario: Server fails to start
- **WHEN** the started server exits before accepting connections
- **THEN** the panel shows that hosting failed together with the end of the server's log

### Requirement: Stop the local server
The host SHALL be able to stop the server from the game; stopping SHALL save the session and disconnect every
player with a shutdown reason. Quitting the game while hosting SHALL stop the server the same way. Leaving the
session without stopping SHALL keep the server running for the others.

#### Scenario: Host quits the game
- **WHEN** the host quits the game while a friend is connected
- **THEN** the session is saved, the friend sees that the server shut down, and no server process remains

#### Scenario: Host leaves but keeps hosting
- **WHEN** the host returns to the main menu without choosing Stop
- **THEN** the server keeps running and connected friends keep playing

### Requirement: New-session settings
When the hosted server has no saved session, the host SHALL choose the difficulty (Easy, Normal or Expert) of the new
session; an existing session SHALL continue with its saved difficulty. Starting over SHALL require confirmation and
SHALL keep the previous session's files.

#### Scenario: New session on Expert
- **WHEN** the host starts a server without a save and chooses Expert
- **THEN** every player in that session plays on Expert difficulty

#### Scenario: Start over
- **WHEN** the host confirms starting a new session on a server that has a save
- **THEN** the old save is moved aside, not deleted, and the new session starts empty

### Requirement: Server password for DirectIP
A server MAY have a password; when set, a DirectIP connection with a missing or wrong password SHALL be refused with
the reason "wrong password" and the player SHALL be able to enter the password and retry. Steam connections SHALL
not be asked for the password unless the server is configured to require it for Steam too.

#### Scenario: Wrong password
- **WHEN** a player joins a password-protected server by IP with the wrong password
- **THEN** the player sees "wrong password" and a password field, and joining with the right password succeeds

#### Scenario: No password set
- **WHEN** a server has no password
- **THEN** players join without being asked for one
