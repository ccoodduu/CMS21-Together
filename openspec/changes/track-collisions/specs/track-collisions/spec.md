# Spec Delta

## Purpose

Lets a player's car collide with other players' cars on the tracks, without stalling the car when another player's car
appears next to it.

## ADDED Requirements

### Requirement: Cars collide on the tracks
On the test, race and speed tracks, a player's car SHALL collide with the cars of the other players it can see, unless
the host has turned collisions off for everyone. The other player's car SHALL NOT be pushed by the contact.

#### Scenario: Driving into a parked car
- **WHEN** player B's car stands on the track and player A drives into it slowly with collisions on
- **THEN** A's car is stopped or deflected by B's car

#### Scenario: Racers on their own grid boxes
- **WHEN** A and B start a race on two boxes of the race track's start grid and the lights turn green
- **THEN** both cars collide with each other from the green on

#### Scenario: Collisions turned off
- **WHEN** the host has turned collisions off and player A drives into B's car
- **THEN** A's car passes through B's car, and B's car is not pushed on B's screen

### Requirement: Collisions never stall a car
A car SHALL never get stuck because another player's car appears on or overlaps it, at arrival on a track, after the
other car jumps, at a race start, or while a player rides along in the other car.

#### Scenario: Other car appears on the spawn
- **WHEN** player A is on the spawn and B's car appears at the same spot
- **THEN** A can drive away normally
