# Spec Delta

## Purpose

Shortens the wait before another player's car appears on a track without making its load heavier. The bounds come
from the user's answer to row 31 (scope B: shorten the fixed wait; about 3 s was acceptable, no frame over 0.13 s as
the target).

## ADDED Requirements

### Requirement: Another player's car appears quickly
When a player arrives on a track where another player drives, the other player's car SHALL appear within 1.5 s of the
player's track scene being ready. When another player starts driving on the track where the player already drives,
that car SHALL appear within 1 s of the drive start reaching the player.

#### Scenario: Arriving where a friend drives
- **WHEN** player B drives on the test track and player A travels there
- **THEN** A sees B's car within 1.5 s after A's track scene is ready, where B is now

#### Scenario: A friend arrives while you drive
- **WHEN** player A drives on the test track and player B arrives and starts driving there
- **THEN** A sees B's car within 1 s after B's drive start reaches A

### Requirement: Loading another car does not freeze the game
Building another player's car copy SHALL NOT freeze the player's game: no frame of the build SHALL take over 0.2 s,
the build SHALL cost no more per frame than before this change (target: no frame over 0.13 s), and the player's own
car SHALL stay drivable while the copy loads.

#### Scenario: Copy loads right after arrival
- **WHEN** player A arrives on the test track where player B drives
- **THEN** no frame of A's game during the copy's build takes over 0.2 s, and A can drive with B's car shown
