# Spec Delta

## Purpose

Gives the dedicated server the game data its rules need (items, upgrades, cars, orders, missions, faults, tuning),
exported from a copy of the game the user owns, on the user's own machine. The data is never shipped with the mod or
the server and never committed to the repo. Takes over the export requirements of `mod-compatibility`'s
`game-data-export` delta.

## ADDED Requirements

### Requirement: Export from an installed game
A player or developer SHALL be able to export the server's game tables from their own installed, unmodded game, in
the layout the server reads, without connecting to a server. The export SHALL contain only the ids and numbers the
server's rules use, and no meshes, textures, audio, copied game text files or localized text.

#### Scenario: Export on a test install
- **WHEN** the export runs on a test install of the pinned game version
- **THEN** it writes the item, upgrade, car, order and mission tables and a metadata file, and every value the server reads equals what the game reports for it

#### Scenario: Export refuses a modded game
- **WHEN** the export runs while a gameplay mod is loaded
- **THEN** it writes nothing and names the mod

#### Scenario: Export refuses while connected
- **WHEN** the export is started while the player is in a multiplayer session
- **THEN** it writes nothing and says why

### Requirement: Game data is never redistributed
The repository and every release archive SHALL contain no exported game tables. The server's self-tests SHALL use
hand-written fixtures.

#### Scenario: Release archive check
- **WHEN** the release build produces the client and server archives
- **THEN** the release check passes only if neither archive contains an exported game table

### Requirement: The data is made where the game is
A server started from the game ("Host") SHALL get its tables from the hosting player's game before the session needs
them. A developer's build SHALL get them from the developer's own install at build or deploy time. A dedicated server
SHALL read tables copied from an export made on a PC with the game.

#### Scenario: Hosting from the game without tables
- **WHEN** a player hosts from the game and the bundled server has no tables, or tables from another game version
- **THEN** the player's game exports the tables for that server, and the server loads them before it generates the first order

#### Scenario: Developer deploy
- **WHEN** a developer deploys the server to the test environment and no export exists for the installed game version
- **THEN** the deploy exports once from the developer's test install and reuses that export on later deploys

### Requirement: Versioned tables with a fallback
Every table SHALL carry a schema version, and the metadata SHALL name the game version and exporter version. A piece
of server game logic whose tables are missing, too old or from another game version SHALL run its interim
client-side path and say so in the server log and the `gamelogic` command.

#### Scenario: Server without an export
- **WHEN** the server starts without any exported tables
- **THEN** it starts, logs one warning per affected piece, orders are made by the elected client, and payouts and prices keep their client values within bounds

#### Scenario: Tables from another game version
- **WHEN** the tables were exported from a game version other than the one the session pins
- **THEN** the server does not use them for game logic and reports the version difference

### Requirement: Machine-dependent fields
Values that depend on the exporting machine, for example whether that machine owns a DLC, SHALL NOT be used by the
server to decide anything for a client.

#### Scenario: DLC ownership of the exporter
- **WHEN** the exporting machine owns no DLC
- **THEN** the server still limits DLC content by the DLC set every connected player owns, not by the exporter's ownership
