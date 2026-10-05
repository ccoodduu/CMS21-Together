# Spec Delta

## Purpose

Defines how connected players appear to each other in a co-op session: where they spawn, how they are
named, how a joining player learns about everyone already present, car seat and engine state, and cleanup
when a player leaves.

## ADDED Requirements

### Requirement: Distinct spawn positions
When a scene becomes playable for a connected player whose position is not being restored from a saved
player record, the client SHALL place that player at a spawn slot next to the scene's default spawn point. Slots
are chosen by player id; a slot blocked by level geometry or within 0.8 m of another player's avatar SHALL be
skipped. If no slot is free the default spawn point SHALL be used.

#### Scenario: Two players load the garage
- **WHEN** players 1 and 2 connect one after the other and both reach the garage
- **THEN** their local positions are at least 0.8 m apart and neither camera is inside the other's avatar

#### Scenario: Slot taken by another player
- **WHEN** a player's own slot is within 0.8 m of a remote avatar at the moment the scene becomes playable
- **THEN** the player is placed at the next free slot instead

#### Scenario: Return from another scene
- **WHEN** a player returns to the garage while another player stands at the garage's default spawn point
- **THEN** the returning player is placed at a free slot, not on top of the other player

#### Scenario: Restored position wins
- **WHEN** the session restores a saved position for a rejoining player
- **THEN** spawn placement does not move that player

### Requirement: Player display names
Each player SHALL have a display name taken from a mod preference, defaulting to the Steam persona name when
Steam is available and to `Player<id>` otherwise. The server SHALL trim it to 24 characters, replace an empty
name with `Player<id>` and make it unique among connected players by appending ` (2)`, ` (3)` and so on.

#### Scenario: Duplicate names
- **WHEN** two connected players both use the name `Bob`
- **THEN** the second one to connect is shown to everyone as `Bob (2)`

#### Scenario: No name configured without Steam
- **WHEN** a DirectIP player with no name preference connects as player 3
- **THEN** everyone sees that player as `Player3`

### Requirement: Name tags
Every visible remote avatar SHALL show its player's display name above the avatar, turned towards the local
camera. The local player SHALL NOT see a tag for themselves.

#### Scenario: Name tag shown
- **WHEN** player `Bob` stands in the garage in view of player `Ann`
- **THEN** Ann sees the text `Bob` above Bob's avatar, readable from her camera angle

### Requirement: Presence roster
The server SHALL keep a presence record for every connected player (display name, scene, seat, engine state,
last position and rotation) and every client SHALL know the records of all other connected players. A client
SHALL create an avatar from the roster as soon as the other player is visible, without waiting for movement.

#### Scenario: Idle player seen by a late joiner
- **WHEN** player A is in the garage and does not move at all, and player B connects afterwards
- **THEN** when B's garage is ready, B shows A's avatar at A's current position and A shows B's avatar, without either player moving

#### Scenario: New player announced
- **WHEN** player B connects while player A is in the garage
- **THEN** A's roster contains B (name, scene `Loading`) immediately, and A shows B's avatar once B reports the garage as ready

#### Scenario: Roster before sync end
- **WHEN** a client performs initial sync
- **THEN** it receives the presence records of all other connected players before the end of its initial sync

### Requirement: Remote avatar motion
A visible remote avatar SHALL follow the position, rotation, crouch, run and look pitch reported by its player,
and SHALL snap to the new position when the reported position jumps by more than 3 m.

#### Scenario: Teleport
- **WHEN** player A is moved 10 m in one step
- **THEN** other players in the same scene see A's avatar at the new position without a slide across the room

### Requirement: Seated players
In the garage, when a player sits inside a car, all other players in the garage SHALL see that player as seated
in that car and seat (left or right): the avatar body is hidden and the name tag is placed above that seat. When
the player leaves the seat, travels or disconnects, the seated state SHALL end for everyone.

#### Scenario: Enter and leave a car
- **WHEN** player A sits in the left seat of the car on car loader 0 and later gets out
- **THEN** player B sees A's name tag above the left seat of that car and no avatar body while A is seated, and A's avatar at A's position again after A gets out

#### Scenario: Car removed under a seated player
- **WHEN** the car a player is sitting in is removed from the garage by another player
- **THEN** the seated player is taken out of the car first and everyone sees the player standing next to where the car was

#### Scenario: Seat outside the garage
- **WHEN** a player sits in a car in any scene other than the garage
- **THEN** no seated state is shared and other players in that scene keep seeing the avatar where it stands

### Requirement: Shared engine state
In the garage, the running state of a car engine started by a player SHALL be known to all players in the
garage, together with its rpm (updated at least twice per second while it changes), and other players SHALL
hear an engine sound from that car while it runs. The state SHALL end when the engine stops, the player travels
or the player disconnects.

#### Scenario: Engine started from the driver's seat
- **WHEN** player A starts the engine of the car on car loader 2
- **THEN** player B's state shows the engine of car loader 2 as running and B hears engine sound from that car

#### Scenario: Engine owner disconnects
- **WHEN** player A disconnects while the engine of a car is running
- **THEN** the engine sound on that car stops for all other players

### Requirement: Cleanup on disconnect
When a player disconnects for any reason (menu, kick, timeout, closed socket, Steam disconnect), the server SHALL
drop that player's presence record and tell all clients, and every client SHALL remove that player's avatar,
name tag, seat and engine state, whatever scene either player is in.

#### Scenario: Player leaves to the menu
- **WHEN** player A returns to the main menu
- **THEN** within 2 seconds player B no longer shows A's avatar and A is no longer in B's roster

#### Scenario: Steam client drops
- **WHEN** a Steam client's connection closes without a disconnect packet
- **THEN** the server removes the player as soon as the transport reports the closed connection, without waiting for the heartbeat timeout

#### Scenario: Disconnect while away
- **WHEN** player A disconnects while in the junkyard and player B is in the garage
- **THEN** A disappears from B's roster and no avatar of A remains in any scene B loads later
