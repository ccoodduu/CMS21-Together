# Spec Delta

## Purpose

Shows a car that another player drives to the players in the same scene (the test track, and the garage area if the
game drives cars there), with the driver's client as the only physics authority and without changing the car's shared
state while it moves.

## ADDED Requirements

### Requirement: Driver is the physics authority
Only the driving player's client SHALL simulate a driven car. Other players SHALL show it from the driver's stream as
a car that does not simulate physics and does not collide with their own player or car.

#### Scenario: Two cars on the test track
- **WHEN** players A and B drive their own cars on the test track at the same time
- **THEN** each drives their own car normally and sees the other's car move without being pushed by it

### Requirement: Smooth remote driving
Players in the same scene as a driver SHALL see the driven car move smoothly close to the driver's position, with
turning wheels and the engine sound at the driver's rpm, and SHALL see it come to rest where the driver stopped.

#### Scenario: Driving seen at the test track
- **WHEN** player A drives on the test track while player B is there too
- **THEN** B sees A's car follow A's path within about 1.5 m and stop where A stopped

#### Scenario: Packets lost for a moment
- **WHEN** A's stream stops for a quarter of a second and then continues
- **THEN** B's view of A's car keeps moving briefly and then catches up without jumping back

#### Scenario: Driver leaves
- **WHEN** player A leaves the test track or disconnects while driving
- **THEN** A's car disappears from B's test track

### Requirement: Driving the shared garage car
If a car in the garage is driven, it SHALL be claimed by the driver for the drive, other players SHALL NOT be able to
edit it meanwhile, and its place after the drive SHALL be the same for every player through the shared car placement.

#### Scenario: Car driven in the garage area
- **WHEN** player A drives a car of the garage and stops
- **THEN** player B saw it move, could not work on it during the drive, and both see it at the same place afterwards

### Requirement: Joining while someone drives
A player who arrives in a scene, joins or reconnects while another player drives there SHALL see the driven car at the
driver's current position within two seconds, without a replay of the path driven before.

#### Scenario: Arriving at the test track mid-drive
- **WHEN** player B travels to the test track while player A is driving there
- **THEN** within two seconds B sees A's car where A is now

### Requirement: Bandwidth of driving
A driver SHALL send at most 15 position updates per second, each a single compact state, and the driving stream SHALL
stay within the session budget for every client.

#### Scenario: Three drivers watched
- **WHEN** three players drive in the same scene while a fourth watches
- **THEN** the watching client's average download stays under 50 kB/s
