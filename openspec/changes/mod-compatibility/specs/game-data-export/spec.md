# Spec Delta

## Purpose

Produces the dedicated server's game database (item, upgrade and later price/payout tables) from an installed copy of
the game, so the server can validate and price actions without game code, and records which game version it came from.

## ADDED Requirements

### Requirement: Export from an installed game
A developer SHALL be able to export the server's game database from a running, unmodded game in the file layout and
schema the server already reads, without connecting to a server.

#### Scenario: Export matches the committed database
- **WHEN** the export runs on the same game version the committed database came from
- **THEN** the item, garage-upgrade and player-upgrade files contain the same entries and values as the committed files, apart from fields that depend on the exporting machine

#### Scenario: Export refuses a modded game
- **WHEN** the export runs while a gameplay mod is loaded
- **THEN** it writes nothing and names the mod, so modded items never enter the base database

### Requirement: Export metadata
Every export SHALL write a metadata file with the game version, the exporter's mod version, the export time and the
list of tables written, and the server SHALL log that metadata when it loads the database.

#### Scenario: Server reads the metadata
- **WHEN** the server starts with a database that has metadata
- **THEN** it logs the game version and export time, and uses that game version as the automatic reference for the game-version check

#### Scenario: Database without metadata
- **WHEN** the server starts with a database that has no metadata
- **THEN** it logs that the source version is unknown and the game-version check falls back to its other reference sources

### Requirement: Machine-dependent fields
Values that depend on the exporting machine (for example whether that machine owns a DLC) SHALL NOT be used by the
server to decide anything for a client.

#### Scenario: DLC ownership of the exporter
- **WHEN** the exporting machine owns no DLC
- **THEN** DLC items are still exported with their DLC ids, and the server's checks do not depend on the exporter's ownership flag
