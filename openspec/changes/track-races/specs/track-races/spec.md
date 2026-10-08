# Spec Delta

## Purpose

Lets players on the race track start a race together, with one countdown for everyone and a result kept by the server.

## ADDED Requirements

### Requirement: Shared race start
A player driving on the race track SHALL be able to start a race with a lap count. Every player driving on the race
track at that moment SHALL take part, and every participant's start SHALL fall on the same moment within a fraction of a
second. A start SHALL be refused, with the reason, while a race already runs on the track or when the player is not
driving there. Races SHALL be offered on the race track only.

#### Scenario: Two players start a race
- **WHEN** players A and B are driving on the race track and A starts a one-lap race
- **THEN** both see the same countdown and both get the green light at the same moment

#### Scenario: Race already running
- **WHEN** a race runs on the race track and player B tries to start another
- **THEN** B is told a race is already running and nothing changes

### Requirement: Race results
The server SHALL decide the finishing order from the participants' lap times, SHALL mark a participant who leaves the
track, disconnects, restarts from the pause menu or does not finish in time as not finished, SHALL show the result to
every player, and SHALL keep the last results across server restarts. A player who arrives during a race SHALL watch it
without taking part.

#### Scenario: Result of a one-lap race
- **WHEN** A finishes the lap in 1:30 and B in 1:35
- **THEN** every player sees A first and B second

#### Scenario: Racer leaves
- **WHEN** B leaves the race track during a race
- **THEN** the result shows B as not finished
