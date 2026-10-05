# Spec Delta

## Purpose

Keeps a co-op session's state on the dedicated server across restarts and rejoins, brings every joining client to
the same state in a defined order, and guarantees that taking part in a session never changes the player's own
single-player saves.

## ADDED Requirements

### Requirement: Versioned save file
The server SHALL store the session in one JSON file that carries a format identifier, an integer save version,
the mod version that wrote it, the save time, and one entry per state section, where every section carries its
own integer version.

#### Scenario: Save written by this version
- **WHEN** the server saves the session
- **THEN** the file contains the current save version, the writing mod version and a versioned entry for every registered section

#### Scenario: Legacy file without a version
- **WHEN** the server loads a file that has no save version but has the legacy world, garage and inventory fields
- **THEN** it treats the file as save version 1 and migrates it

### Requirement: Pluggable state sections
Every feature that owns persistent server state SHALL contribute it as a named section with its own version,
defaults and migrations, and every feature that must reach a joining client SHALL contribute a snapshot provider in
a fixed order slot, without changing the save or sync code of other features.

#### Scenario: New section added to an existing save
- **WHEN** a server with a newly registered section loads a save that has no entry for it
- **THEN** that section starts from its defaults and all other sections load unchanged

#### Scenario: Unknown section in the file
- **WHEN** the file contains a section that no registered feature claims
- **THEN** the server logs a warning, keeps the section's data unchanged and writes it back on the next save

#### Scenario: Two features claim the same key
- **WHEN** two registered sections or providers use the same key
- **THEN** the server refuses to start and names both

### Requirement: Save migration
The server SHALL migrate an older save or section version step by step to the current version and SHALL keep a
copy of the original file that rotation never deletes.

#### Scenario: Migrating a version 1 save
- **WHEN** the server loads a version 1 save
- **THEN** it writes a pre-migration copy and migrates world, garage, inventory and car data into sections with money, scrap, level, exp, skills and garage upgrades unchanged

#### Scenario: Save from a newer version
- **WHEN** the save version or any section version is newer than this server supports
- **THEN** the server refuses to start, names the file and versions in the log, and does not modify any save file

### Requirement: Consistent shared state
Every change to shared server state, every broadcast and the building of a save or a join snapshot SHALL happen
under one server-wide lock, so that a save or a snapshot never contains a half-applied change.

#### Scenario: Save during a burst of changes
- **WHEN** clients send state changes while an autosave runs
- **THEN** the saved file reflects the state before or after each change, never part of one

### Requirement: Crash-safe saving
A save SHALL replace the previous file only after the new content is fully written, so that a crash or power
loss at any moment leaves either the old or the new complete file in place.

#### Scenario: Server killed during a save
- **WHEN** the server process is killed while a save is in progress
- **THEN** on the next start a complete save file (old or new) is loaded and no session data older than the previous save is lost

### Requirement: Backups
The server SHALL keep a configurable number of rolling backups of previous saves and a separate copy of the
save as it was when the server started, keeping the last three start copies.

#### Scenario: Rolling backup
- **WHEN** the server saves and a previous save exists
- **THEN** the previous save becomes the newest backup and the oldest backup beyond the configured count is deleted

#### Scenario: Start copy survives a bad session
- **WHEN** a session runs long enough that every rolling backup has been replaced
- **THEN** the copy taken at server start is still available

### Requirement: Load fallback for unreadable saves
The server SHALL load its save before accepting connections. When the main save cannot be read or migrated, it
SHALL load the newest backup that can be, move every unreadable file to a quarantine folder, and refuse to start if
a save existed but none could be loaded.

#### Scenario: Corrupt main file
- **WHEN** the main save is not valid JSON and the newest backup is valid
- **THEN** the server loads the backup, moves the corrupt file to quarantine, saves the backup as the new main file and logs an error naming both files

#### Scenario: Nothing loadable
- **WHEN** a main save exists but neither it nor any backup can be loaded
- **THEN** the server refuses to start, accepts no connection and does not create a new session over the existing files

#### Scenario: First start
- **WHEN** no save and no backup exist
- **THEN** the server creates a new session with default values and saves it

### Requirement: Autosave and save triggers
The server SHALL save at a configurable interval, when a player leaves, on an operator save command, and when it
is shut down by command or by closing its window; it SHALL skip writing when the content is unchanged.

#### Scenario: Player leaves
- **WHEN** a connected player disconnects or times out
- **THEN** the server stores that player's record and saves the session

#### Scenario: Operator stops the server
- **WHEN** the operator stops the server
- **THEN** the server saves, tells connected clients that it is shutting down, and exits

#### Scenario: Window closed
- **WHEN** the server window is closed
- **THEN** the server saves before the process ends

#### Scenario: Nothing changed
- **WHEN** an autosave is due and the session content is identical to the last save
- **THEN** no file is written and no backup is rotated

### Requirement: Player identity
Every client SHALL present a stable player key when it connects: the server-verified Steam ID on the Steam
transport, otherwise a random key created once per game install and kept in the mod's own data folder.

#### Scenario: Returning DirectIP player
- **WHEN** a player connects again over DirectIP from the same install, in any slot
- **THEN** the server matches them to their existing player record

#### Scenario: Steam key mismatch
- **WHEN** a client on the Steam transport presents a key that differs from the Steam ID that Steam reports for the connection
- **THEN** the server uses the Steam ID reported by Steam

#### Scenario: Missing key
- **WHEN** a client on the DirectIP transport connects without a player key
- **THEN** the server rejects the connection with a reason the client can show

### Requirement: Duplicate identity
The server SHALL reject a connection whose player key belongs to a player who is already connected.

#### Scenario: Same key twice
- **WHEN** a second client connects with the key of a connected player
- **THEN** the second client is disconnected with a "player already connected" reason and the first stays connected

#### Scenario: Crashed client comes back
- **WHEN** a client crashed and connects again with its key before the server noticed the old connection was dead
- **THEN** it is rejected as a duplicate until the old connection times out, and is accepted afterwards

### Requirement: Shared progression
Money, scrap, level, exp, skills, gamemode, garage upgrades, inventory, warehouse and cars SHALL be one shared state
for all players, saved once per session.

#### Scenario: Exp earned by one player
- **WHEN** one player earns exp and scrap
- **THEN** every connected player sees the same level, exp and scrap, and a player who joins later gets the same values

### Requirement: Player records
The server SHALL keep one record per player key holding only the display name, last-seen time and the last position,
rotation and scene, and SHALL restore the garage position when that player joins again.

#### Scenario: Rejoin restores position
- **WHEN** a player left while in the garage and joins again
- **THEN** they spawn at their last garage position and rotation

#### Scenario: Left outside the garage
- **WHEN** a player's last scene was not the garage
- **THEN** no position is restored and the normal spawn placement applies

#### Scenario: New player
- **WHEN** a player key with no record joins
- **THEN** a record is created and the player gets the shared session state like everyone else

### Requirement: Join snapshot order
Every join (first join, rejoin, late join, return to the garage) SHALL receive one snapshot: a begin message, then
world, garage, inventory and the registered feature sections in their order slots, then an end message carrying
the number of items per section. The client SHALL acknowledge once every announced item is applied.

#### Scenario: Late join
- **WHEN** a client joins while other players are in the garage
- **THEN** after acknowledging the snapshot its shared state equals theirs and it sees the other players

#### Scenario: Incomplete snapshot
- **WHEN** the end message arrives but fewer items get applied than announced before the timeout
- **THEN** the client logs which section is incomplete, disconnects and returns to the menu

### Requirement: Live updates after the snapshot
A joining client SHALL NOT receive reliable state updates before its snapshot begins, SHALL receive every update
made after its snapshot was taken, and SHALL NOT send state changes until it has applied the snapshot; the server
SHALL drop state changes from a client that has not acknowledged its snapshot.

#### Scenario: Change during a join
- **WHEN** another player changes shared state while a client is loading its snapshot
- **THEN** the joining client ends up with the changed state

#### Scenario: Game load events on the joining client
- **WHEN** the game raises inventory or stat events while applying the snapshot
- **THEN** none of them are sent to the server, and any that are sent are dropped by the server with a warning

### Requirement: Progress-based sync timeout
The client SHALL give up on a join only when no snapshot data has arrived and no item has been applied for a set
time, not after a fixed total time.

#### Scenario: Large snapshot
- **WHEN** a snapshot takes longer than 15 seconds to arrive but data keeps arriving
- **THEN** the join completes

### Requirement: Server loss on the client
A client SHALL detect that the server is gone (closed connection or no message within the heartbeat timeout),
return to the main menu with a message, and not save anything locally.

#### Scenario: Server restarted while players are connected
- **WHEN** the server process ends while clients are in the garage
- **THEN** each client returns to the menu, its local profile files are unchanged, and after the server is back each player can join again and gets the last saved state

### Requirement: Local profiles untouched by a session
While connected to a server, and until the main menu is reached afterwards, the client SHALL NOT create, write,
back up or delete any of the game's profile files.

#### Scenario: Scene change and return to menu
- **WHEN** a player changes scene and then returns to the menu with saving requested during a session
- **THEN** every file in the game's save folder is byte-identical to before the session and no new profile file exists

#### Scenario: Game quits during a session
- **WHEN** the game is closed while connected
- **THEN** no profile file is written

### Requirement: Selected profile restored
The client SHALL restore the player's selected profile and the game's profile list when a session ends, and
SHALL repair them on the next start, before the game reads them, if the game ended during a session.

#### Scenario: Normal end of session
- **WHEN** a player leaves a session and is back in the main menu
- **THEN** the selected profile is the one selected before the session and only the game's own four profiles are listed

#### Scenario: Crash during a session
- **WHEN** the game crashed during a session and is started again
- **THEN** the main menu shows the profile that was selected before the session

### Requirement: Local profile backup before a session
Before the first session of a game run, the client SHALL copy the game's profile files into the mod's data
folder and keep the five most recent copies; if the copy fails, the client SHALL NOT join.

#### Scenario: Backup made
- **WHEN** the player connects to a server for the first time since starting the game
- **THEN** a timestamped copy of every profile file exists in the mod's backup folder before the garage loads

#### Scenario: Backup fails
- **WHEN** the profile files cannot be copied
- **THEN** the join is aborted with a message and no profile setting is changed
