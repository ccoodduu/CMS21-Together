# Spec Delta

## Purpose

Defines what players see about the session while playing (who is connected, where they are, their ping, who joins
and leaves) and how the session's admin removes a player.

## ADDED Requirements

### Requirement: In-game player list
A player in a session SHALL be able to open a player list in any scene showing every connected player's display
name, current scene and ping in milliseconds, including themselves.

#### Scenario: Three players
- **WHEN** Ann, Bob and Cid are connected and Cid is in the junkyard
- **THEN** Ann's list shows all three names, Cid's scene as junkyard, and a ping value for each that updates at least
  every 5 seconds

### Requirement: Join and leave notifications
Players in a session SHALL get a short notification when another player joins or leaves, naming the player; a leave
caused by a kick SHALL say so. A player who joins SHALL NOT get a notification for each player already present, only
one summary.

#### Scenario: Friend joins
- **WHEN** Bob joins while Ann is in the garage
- **THEN** Ann sees a notification that Bob joined

#### Scenario: Joining an active session
- **WHEN** Cid joins a session where Ann and Bob are present
- **THEN** Cid sees one notification that two other players are online, not one per player

### Requirement: Admin
The server SHALL treat a client as admin when it presents the server's admin key; a player who hosts from the game
SHALL be admin of that server. Other players SHALL NOT see admin controls.

#### Scenario: Host is admin
- **WHEN** Ann hosts from the game and Bob joins
- **THEN** Ann's player list shows a kick control next to Bob and Bob's list shows none

### Requirement: Kick
The admin SHALL be able to kick another connected player; the kicked player SHALL be returned to the main menu with
the message that they were kicked and MAY rejoin. Kick requests from non-admins SHALL be ignored.

#### Scenario: Admin kicks a player
- **WHEN** Ann (admin) kicks Bob
- **THEN** Bob is in the main menu with a "kicked" message, and Ann sees a notification that Bob was kicked

#### Scenario: Non-admin request
- **WHEN** a client that is not admin sends a kick request
- **THEN** no player is disconnected and the server logs the refused request
