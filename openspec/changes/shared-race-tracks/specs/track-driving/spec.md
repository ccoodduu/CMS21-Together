# Spec Delta

## Purpose

Lets players take garage cars to the game's base tracks (test track, race track, speed track) in multiplayer with the
same guarantees as the test track: the car is reserved while away, the result comes back, others on the track see it
drive, a passenger can ride along, and lap records are kept.

## ADDED Requirements

### Requirement: Base-game tracks only
Players SHALL be able to travel with a garage car to the test track, the race track and the speed track. The drag strip
(Drag Racing DLC), Workshop tracks and track types without a scene SHALL stay refused with a message that says why.

#### Scenario: Race track allowed
- **WHEN** player A drives a garage car to the race track from the map
- **THEN** A arrives on the race track with that car

#### Scenario: Drag strip refused
- **WHEN** a player tries to travel to the drag strip
- **THEN** the travel is refused with a message naming the Drag Racing DLC

### Requirement: The car is reserved while away
While a player has a car on a track, other players SHALL NOT be able to change, move, sell or delete it, and SHALL be
told who has it and where. Mileage and dirt from the drive SHALL reach the car for every player when the driver returns.

#### Scenario: Working on a car that is on the race track
- **WHEN** player A is on the race track with a car and player B tries to take a part off it
- **THEN** B is told that A has this car on the race track and nothing changes

#### Scenario: Return from the speed track
- **WHEN** player A drives 2.5 km on the speed track and returns
- **THEN** every player sees the car's mileage increased by the driven distance

### Requirement: Drivers visible on the same track
Players on the same track SHALL see each other's cars driving.

#### Scenario: Two players on the race track
- **WHEN** players A and B are both on the race track with their own cars
- **THEN** each sees the other's car moving where the other drives

### Requirement: Ride along on every base track
A player seated in a car SHALL ride along when its driver takes it to any base track.

#### Scenario: Passenger on the speed track
- **WHEN** player B sits in the passenger seat and A drives the car to the speed track
- **THEN** B is on the speed track in A's car as passenger

### Requirement: Lap records
The server SHALL keep each player's best race-track lap and the group's best lap across sessions and server restarts,
show a player's own best in the game's race display after joining, and tell everyone when the group record falls. Only
the driver's complete laps SHALL count; a passenger riding along SHALL NOT record a lap.

#### Scenario: New group record
- **WHEN** player A finishes a lap faster than the group record
- **THEN** every player is told A's new record, and it is still the record in the next session and after a server
  restart

#### Scenario: Passenger on the race track
- **WHEN** player B rides along in A's car on the race track and A completes a lap
- **THEN** only A's lap is recorded
