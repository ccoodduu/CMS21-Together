# Spec Delta

## Purpose

Shortens the wait before another player's car appears on a track. The time bound is open question 1 of the proposal;
this delta states the behaviour so the change validates and is refined with the design.

## ADDED Requirements

### Requirement: Another player's car appears quickly
When a player arrives on a track where another player drives, or another player starts driving on the track where the
player is, the other player's car SHALL appear within a few seconds and SHALL NOT freeze the player's game while it
loads.

#### Scenario: Arriving where a friend drives
- **WHEN** player B drives on the test track and player A arrives there
- **THEN** A sees B's car within a few seconds of arrival and A's own car stays drivable meanwhile

#### Scenario: Driving again with the same car
- **WHEN** player B stops driving and starts again with the same car while A stays on the track
- **THEN** A sees B's car again without waiting for a full load
