# Spec Delta

## Purpose

Shortens the wait before another player's car appears on a track and keeps its loading free of visible hitches. The
bounds come from the user's answer to row 31 (about 3 s is fine, no frame over 0.13 s) and row 17's original 2 s.

## ADDED Requirements

### Requirement: Another player's car appears quickly
When a player arrives on a track where another player drives, the other player's car SHALL appear within 2 s of the
player's scene being ready. When another player starts driving on the track where the player already drives, that
car SHALL appear within 1 s of the drive start reaching the player.

#### Scenario: Arriving where a friend drives
- **WHEN** player B drives on the test track and player A travels there
- **THEN** A sees B's car within 2 s after A's track scene is ready, where B is now

#### Scenario: A friend arrives while you drive
- **WHEN** player A drives on the test track and player B arrives and starts driving there
- **THEN** A sees B's car within 1 s after B's drive start reaches A

### Requirement: Loading another car does not hitch the game
Building another player's car copy SHALL NOT make any frame of the player's game longer than 0.13 s, and the player's
own car SHALL stay drivable while the copy loads. The copy SHALL look the same as a copy loaded in one go (same model,
parts and wheels).

#### Scenario: First copy in a scene visit
- **WHEN** player A arrives on the test track where player B drives a car model A has not loaded in this visit
- **THEN** no frame of A's game during the copy's build takes over 0.13 s, and A can drive meanwhile

#### Scenario: Copy matches the car
- **WHEN** A's game builds a copy of B's car
- **THEN** the copy has the same parts, wheels and model as a copy built without spreading the load over frames
