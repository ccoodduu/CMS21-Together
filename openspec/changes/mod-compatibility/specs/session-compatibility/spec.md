# Spec Delta

## Purpose

Decides whether a joining client is compatible with the server before it enters the session — same mod build,
same game version, same DLC set and matching gameplay mods — and tells a refused player exactly what differs.

## ADDED Requirements

### Requirement: Same mod build
The server SHALL accept a client only when its mod version and its protocol fingerprint (derived from the shared
packet definitions) equal the server's, and the client SHALL check the same two values in the server's welcome
before it sends its own details.

#### Scenario: Different mod version
- **WHEN** a client with mod version 0.5.1 connects to a server running 0.5.0
- **THEN** the client is refused with the version-mismatch reason and a message naming both versions

#### Scenario: Same version string, different packets
- **WHEN** a client built from another branch has the same version string but a different protocol fingerprint
- **THEN** the client is refused with the version-mismatch reason and a message saying the builds differ

#### Scenario: Server's welcome cannot be matched
- **WHEN** the client receives a welcome whose version or fingerprint differs from its own
- **THEN** the client disconnects by itself, returns to the menu and shows the mismatch without waiting for the server

### Requirement: Same game version
The server SHALL refuse a client whose game build version differs from the session's reference game version. The
reference is the configured version, or the version recorded with the server's game database, or else the version of
the first client accepted since the server started.

#### Scenario: Configured version
- **WHEN** the server is configured for game version 1.0.40 and a client reports 1.0.39
- **THEN** the client is refused with the game-version reason and a message naming both versions

#### Scenario: Pinned by the first client
- **WHEN** the game version is set to automatic, no database version is recorded, and the first client reports 1.0.40
- **THEN** that client is accepted, the server logs 1.0.40 as the reference, and later clients with another version are refused

### Requirement: Same DLC set
The server SHALL refuse a client whose set of owned DLC differs from the session's reference set. The reference is
the configured set (which may be empty), or else the set of the first client accepted since the server started.

#### Scenario: Client owns a DLC the others do not
- **WHEN** the reference set is empty and a client owns one DLC
- **THEN** the client is refused with the DLC reason and a message naming the extra DLC

#### Scenario: Client lacks a DLC
- **WHEN** the reference set contains a DLC the client does not own
- **THEN** the client is refused with the DLC reason and a message naming the missing DLC

### Requirement: Gameplay-mod classification
For every other loaded mod or plugin, the client SHALL report its name, version and the game methods its patches
target. Each mod SHALL be classified as gameplay, visual or unknown from those targets; a mod with no target in the
game's own code is visual. This multiplayer mod and the test harness SHALL never be classified.

#### Scenario: Mod patching gameplay code
- **WHEN** a mod patches inventory, car loading or part methods
- **THEN** it is classified as gameplay and the report names those targets

#### Scenario: Mod patching only rendering and other mods
- **WHEN** a mod patches only engine-level texture methods and methods of another mod
- **THEN** it is classified as visual

#### Scenario: Mod patching a window's logic
- **WHEN** a mod patches a game window method that is not a drawing method
- **THEN** it is classified as unknown and listed as such in the report

### Requirement: Gameplay-mod policy
The server SHALL refuse a client whose gameplay or unknown mods are not on the server's required list, or that lacks
a required mod. Mods on the server's ignore list SHALL count as visual and mods on its gameplay list SHALL count as
gameplay whatever their targets. With empty lists, any gameplay or unknown mod is refused.

#### Scenario: Visual mod only
- **WHEN** a client's only other mod is classified as visual
- **THEN** the mod check accepts the client

#### Scenario: Unlisted gameplay mod
- **WHEN** a client has a gameplay mod and the server's lists are empty
- **THEN** the client is refused with the mod reason and a message naming the mod and its main targets

#### Scenario: Operator ignores a mod
- **WHEN** the server's ignore list contains a mod that the classifier calls gameplay
- **THEN** a client with that mod is accepted and the server logs that the mod was ignored by configuration

#### Scenario: Required mod missing
- **WHEN** the server's required list names a mod the client does not have
- **THEN** the client is refused with the mod reason and a message naming the missing mod

### Requirement: Refusal reaches the player
A refused client SHALL receive the refusal reason and message before its connection is closed, SHALL return to the
menu showing them, and the server SHALL free the slot and log the reason. A client that is refused SHALL never enter
the join sync.

#### Scenario: Refusal shown and slot freed
- **WHEN** the server refuses a client for any compatibility reason
- **THEN** the client shows the reason and message in the menu, the server logs the reason, and a new client can take the slot at once

#### Scenario: Several differences
- **WHEN** a client differs in game version and in mods
- **THEN** the refusal names the first failing check in the order mod build, game version, DLC, mods, and the message lists every difference found

### Requirement: Compatibility visible to the operator
The server SHALL log every client's reported game version, DLC set and classified mod list on connect, and SHALL
show the session's reference values and the last refusals on request.

#### Scenario: Operator inspects the session
- **WHEN** the operator asks the server for its compatibility state
- **THEN** it prints the reference game version and DLC set (and whether each was configured, read from the database or pinned), the configured mod lists, and the last refusals with their reasons
