# Spec Delta

## Purpose

Keeps a car consistent for every player while one player takes it on the test track, the test path or the dyno, and
makes the results of those activities and of the diagnostic tools part of the shared car state, including for the
driver's own return to the garage, late joiners and a server restart.

## ADDED Requirements

### Requirement: Car claimed while away
Taking a car to the test track, starting the test path with it or starting a dyno run on it SHALL require a claim on that car granted by the server. The server SHALL grant at most one such claim per car at a time and SHALL refuse it while another player holds part claims on the car. The claim SHALL last until the activity is over, including the driver's return to the garage, and SHALL be released when the holder leaves the session.

#### Scenario: Car taken to the test track
- **WHEN** player A selects a car in the garage and travels to the test track while player B is in the garage
- **THEN** A reaches the test track with that car, and the server records the car as claimed by A

#### Scenario: Two players take the same car
- **WHEN** players A and B try to take the same car to the test track or the dyno at nearly the same time
- **THEN** exactly one of them gets it, and the other gets a message and stays in the garage with nothing started

#### Scenario: Car in use by another player
- **WHEN** player B is unmounting a part of a car and player A tries to take that car to the test track
- **THEN** A's departure is refused with a message and A stays in the garage

#### Scenario: Driver leaves the session while away
- **WHEN** player A disconnects or quits to the menu while on the test track
- **THEN** the claim is released, the car is unchanged for everyone, and other players can work on it again

### Requirement: Claimed car is shown as away and locked
While a car is claimed, every other player SHALL see it marked with the holder's name and the activity, and SHALL NOT be able to mount or unmount its parts, change its fluids, wheels or other details, move or park it, raise or lower its lift, delete it or end its job. The server SHALL refuse such changes from anyone but the holder and SHALL send the refused player the car's current state. Examining a claimed car SHALL stay allowed.

#### Scenario: Other player tries to work on the car
- **WHEN** player A's car is on the test track and player B tries to unmount one of its parts
- **THEN** B gets a message naming A, the part stays mounted for both players, and the server's car state is unchanged

#### Scenario: Change that slips past the client
- **WHEN** a change to a claimed car from a player other than the holder reaches the server
- **THEN** the server refuses it and that player's car returns to the server's state

#### Scenario: Away marker
- **WHEN** player A runs the dyno on a car
- **THEN** player B sees the car labelled with A's name and "dyno" until A closes the dyno

### Requirement: Test drive results reach the server before the return
When the driver leaves the test track, the mileage driven and the dirt the drive added SHALL be sent to the server before the driver's garage reload requests its snapshot. The server SHALL add them to the stored car and SHALL send them to every player. The driver's returning snapshot SHALL already contain them.

#### Scenario: Mileage after a test drive
- **WHEN** player A drives 5 km on the test track and returns to the garage
- **THEN** the car's mileage is 5 km higher than before the drive for A and for B, and equal on both

#### Scenario: Drive left early
- **WHEN** player A leaves the test track through the pause menu before finishing the tests
- **THEN** the mileage driven so far is added for both players and no examine report opens

#### Scenario: Repairs survive the test drive
- **WHEN** players A and B repair a car, A takes it on a test drive and returns
- **THEN** every repair is still on the car for both players after A's return

### Requirement: Examined parts after a test drive or test path
After a finished test drive or test path, the examine report SHALL run on the car as the server knows it, and the parts it marks as examined SHALL be marked for every player and the server. The claim SHALL be released only after that.

#### Scenario: Examine report after the return
- **WHEN** player A finishes every test on the test track and returns to the garage
- **THEN** the examine report opens for A, and the parts it examined are shown as examined for both A and B

#### Scenario: Examine report after the test path
- **WHEN** player A runs the test path to the end and leaves the car
- **THEN** the parts the test path examined are shown as examined for both players, and the car is free again

### Requirement: Mileage is counted once
The mileage of a test drive SHALL be added to the car exactly once, whether the server or the returning client applies it, and SHALL NOT be carried into a later game outside the session.

#### Scenario: Result not applied by the server
- **WHEN** the server cannot apply a test drive's result when it arrives
- **THEN** the returning client adds the mileage to its car after the snapshot, and both players end up with the same mileage, increased once

#### Scenario: No leak into single-player
- **WHEN** a player returns from a test drive, disconnects and loads their own save
- **THEN** no car in their own save gets the test drive's mileage

### Requirement: Dyno results are shared
A completed dyno measurement SHALL store the car's measured engine data and drag index on the server and SHALL show the same values to every player. A dyno run closed without measuring SHALL change nothing for anyone.

#### Scenario: Dyno measurement
- **WHEN** player A measures a car on the dyno and closes the dyno
- **THEN** player B's car has the same measured engine data and drag index, and B's dyno window shows the measured values

#### Scenario: Dyno closed without measuring
- **WHEN** player A opens the dyno on a car and closes it without starting a measurement
- **THEN** the car's engine data is unchanged for both players

### Requirement: Test path result is shared
A finished test path SHALL mark the car as tested for every player, as the game does for the driver, until the car is moved to another place.

#### Scenario: Car after the test path
- **WHEN** player A finishes the test path and player B joins afterwards
- **THEN** the car is marked as tested for A and B, and moving it to another place clears the mark for both

### Requirement: Diagnostic tools are shared
Parts examined with the OBD scanner, compression tester, multimeter, tire tread gauge or compound meter SHALL be marked as examined for every player.

#### Scenario: OBD scan
- **WHEN** player A scans a car with the OBD scanner
- **THEN** player B sees the same parts marked as examined

#### Scenario: Two players examine at once
- **WHEN** players A and B use diagnostic tools on the same car at the same time
- **THEN** every part examined by either player is marked as examined for both

### Requirement: Late join and persistence
A player who joins while a car is claimed SHALL see it as claimed. Mileage, track dirt, dyno results and the test path mark SHALL be part of the server's saved state and of every snapshot.

#### Scenario: Late join during a test drive
- **WHEN** player A is on the test track and player B connects
- **THEN** B sees A's car marked as away and locked, and after A's return B has the same mileage and examined parts as A

#### Scenario: Server restart
- **WHEN** a car has a test drive's mileage and dyno results, the server saves, restarts and the players reconnect
- **THEN** the car has the same mileage and dyno values as before the restart

### Requirement: Features available while connected
While connected, the test track, the test path, the dyno and the diagnostic tools SHALL be usable with the multiplayer guard enforcing. The other tracks SHALL stay blocked.

#### Scenario: Test track allowed
- **WHEN** a connected player with the guard enforcing travels to the test track with a car
- **THEN** the travel is not blocked by the guard

#### Scenario: Other tracks stay blocked
- **WHEN** a connected player tries to travel to the drag strip
- **THEN** the guard blocks it with its "not supported in multiplayer yet" message
