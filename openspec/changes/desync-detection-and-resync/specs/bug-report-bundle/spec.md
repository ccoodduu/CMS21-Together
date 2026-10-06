# Spec Delta

## Purpose

Lets a player capture everything needed to investigate a multiplayer problem with one key press, on their own
machine, on the server and on the other connected clients, all tagged with the same report id.

## ADDED Requirements

### Requirement: One-key client bundle
A player SHALL be able to press one key to write a bug-report bundle on their machine containing the game and mod
logs of the current and previous run, the mod's configuration, the loaded mod list, the guard log and the client's
current shared-state projection, named with a new report id.

#### Scenario: Report while connected
- **WHEN** a connected player presses the bug-report key
- **THEN** a bundle with a new report id is written on their machine and its location is shown in the game

#### Scenario: Report while not connected
- **WHEN** a player who is not connected presses the key
- **THEN** a client-only bundle is written and the message says that no server part exists

### Requirement: Server and other clients join the report
When the reporting player is connected, the server SHALL write its own bundle with the same report id (its logs, a
copy of the current save, its state projection, the desync log and its configuration) and SHALL ask every other
in-session client to write theirs with that id.

#### Scenario: Matching ids
- **WHEN** a player in a two-player session reports a bug
- **THEN** the server and both clients each hold a bundle with the same report id, and the reporting player is told the server bundle's name

### Requirement: No secrets in bundles
Bundles SHALL NOT contain login tokens, player identity keys or other credentials.

#### Scenario: Server configuration in the bundle
- **WHEN** the server configuration holds a game server login token
- **THEN** the bundled configuration shows the token as redacted

### Requirement: Reporting never disturbs the session
Writing a bundle SHALL not pause the game, disconnect anyone or change any state, and repeated presses SHALL be
limited to one report per 30 seconds per player.

#### Scenario: Report during play
- **WHEN** a player reports a bug while another player is working on a car
- **THEN** both stay in the session and no state changes because of the report
