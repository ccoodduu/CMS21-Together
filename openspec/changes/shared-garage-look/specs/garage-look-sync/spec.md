# Spec Delta

## Purpose

Keeps the garage's look (the material chosen for each customisable section and the texture pack) the same for every
player and stores it with the session.

## ADDED Requirements

### Requirement: One garage look for everyone
When a player finishes customising the garage, every player in the session SHALL see the same material on every
section, and a player who joins later or after a server restart SHALL see that look.

#### Scenario: New wall colour
- **WHEN** player A chooses another material for the garage wall and closes the customisation window
- **THEN** player B sees the wall in that material within a few seconds

#### Scenario: Look after a server restart
- **WHEN** the server restarts after A changed the floor and B joins again
- **THEN** B sees the floor A chose

### Requirement: One player customises at a time
While one player has the customisation window open, another player SHALL NOT be able to open it and SHALL be told who
is customising the garage.

#### Scenario: Second player opens the window
- **WHEN** player A has the customisation window open and player B tries to open it
- **THEN** B is told that A is customising the garage and the window does not open for B

### Requirement: Texture pack not installed
When the shared look uses a texture pack that a player does not have, that player SHALL see the default garage textures
and SHALL be told once why.

#### Scenario: Missing pack
- **WHEN** A chooses a texture pack that B has not installed
- **THEN** B keeps the default textures and sees a notice that the pack is not installed
