# Spec Delta

## Purpose

Keeps connected players inside the part of the game that multiplayer actually syncs: features that are not synced yet
are refused with a clear message instead of silently splitting the session, and single-player-only behaviour is
neutralised while connected.

## ADDED Requirements

### Requirement: Default deny while connected
While the player is in a multiplayer session, the client SHALL let a game window, a pie-menu action, a game mode or a
scene change proceed only if it is on the allow-list; anything else SHALL be refused. Outside a session the game SHALL
behave exactly as without the mod.

#### Scenario: Unsupported window
- **WHEN** a connected player opens a window that is not on the allow-list
- **THEN** the window does not open, a "Not supported in multiplayer yet" message appears, and the attempt is logged with the window's name

#### Scenario: Supported window
- **WHEN** a connected player opens the shop, which is on the allow-list
- **THEN** it opens as in single player and nothing is logged as blocked

#### Scenario: Same action in single player
- **WHEN** a player who is not connected opens a window that is not on the allow-list
- **THEN** it opens normally

### Requirement: Every entry kind is guarded
The guard SHALL cover windows, pie-menu actions (including the actions offered at workshop machines), game modes and
scene changes, so that a feature reachable through more than one of them is refused on every path.

#### Scenario: Travel from the map
- **WHEN** a connected player chooses a destination that is not allowed
- **THEN** the scene does not change and the player stays in the garage with the message shown

#### Scenario: Machine action
- **WHEN** a connected player picks an action at a workshop machine whose sync has not landed
- **THEN** the action is refused with the message, and a refused action is shown as unavailable when the menu opens

#### Scenario: Car work mode
- **WHEN** a connected player tries to enter a car disassembly mode before car parts are synced
- **THEN** the mode does not change and the message is shown

### Requirement: Always-allowed basics
The guard SHALL never refuse the menu and garage scenes, generic message and confirmation windows, the pie menu
itself, the pause menu, settings, or anything the mod itself opens.

#### Scenario: Returning to the menu
- **WHEN** a connected player quits to the main menu
- **THEN** the scene change is allowed and the session ends as before

#### Scenario: Mod's own reload
- **WHEN** the mod reloads the garage for a resync
- **THEN** the guard allows it

### Requirement: Allow-list grows with synced features
Each feature SHALL be added to the allow-list in the same release that syncs it, and the allow-list SHALL be
readable as one table that names, per entry, the feature that made it allowed.

#### Scenario: Feature lands
- **WHEN** a release syncs a workshop machine
- **THEN** that machine's window and actions are allowed in that release, and the table names the feature that added them

### Requirement: Player override
The player SHALL be able to switch the guard to enforce, log-only or off, and to add or remove single entries, in the
mod's configuration; the active mode and overrides SHALL be logged when a session starts.

#### Scenario: Log-only mode
- **WHEN** the guard is set to log-only and a connected player opens an unsupported window
- **THEN** the window opens, no message is shown, and the attempt is logged as "would block"

#### Scenario: Extra allowed entry
- **WHEN** the configuration adds a scene to the allowed entries
- **THEN** a connected player can travel there, and the session start log lists the override

### Requirement: No single-player pause or save while connected
While connected, the game SHALL keep running for everyone when a player opens the pause menu or any window, and the
pause menu SHALL not offer save actions.

#### Scenario: Pause menu open
- **WHEN** one player opens the pause menu
- **THEN** game time keeps running on that client and others still see that player and their changes

#### Scenario: Save buttons
- **WHEN** a connected player opens the pause menu
- **THEN** it offers no save or save-and-quit action, only continue, settings and quit to menu

### Requirement: Single-player assumptions audited
Before the guard is considered done, every known single-player assumption (pausing, time scale, local camera modes,
autosave by the game or other mods, modal windows shown over remotely changed data) SHALL be checked in a running
session and each SHALL be either handled, blocked by the guard or recorded as a known gap with its owning feature.

#### Scenario: Audit result
- **WHEN** the audit is finished
- **THEN** each assumption has one of the three outcomes recorded, and a blocked one has a guard rule
