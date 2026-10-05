# Spec Delta

## Purpose

Keeps where every car is the same for all players in a session: the state of each car lift, the place each
car occupies in the garage, the shared parking lot with its unlocked levels, and moving cars between the
parking lot and the garage, including when players act at the same time or join late.

## ADDED Requirements

### Requirement: Lift positions are shared
When a player raises or lowers a car lift, every other player in the garage SHALL see that lift move to the
same position (floor, middle or top). The server SHALL hold the current position of every lift as an absolute
value, not as a sequence of up/down steps.

#### Scenario: Raising a lift
- **WHEN** player A raises the lift holding a car from the floor to the middle position
- **THEN** player B sees the same lift move to the middle position, and the server records the lift as middle

#### Scenario: Two steps in a row
- **WHEN** player A raises a lift from the floor to the middle position and then to the top
- **THEN** player B's lift ends at the top position

### Requirement: Simultaneous lift operations end in one agreed position
When two players operate the same lift at nearly the same time, the server SHALL accept only the operation
that starts from the lift's current position, and every client, including the player whose operation was
refused, SHALL end with the lift at the position the server holds.

#### Scenario: Both players press up at the same time
- **WHEN** players A and B both press "up" on a lift at the floor position before either has seen the other's action
- **THEN** the lift ends at the middle position for both players and on the server, not at the top

#### Scenario: One presses up while the other presses down
- **WHEN** a lift is at the middle position and player A presses "up" while player B presses "down" at nearly the same time
- **THEN** both clients end with the lift at the position of whichever operation the server accepted first

### Requirement: A lift without a car rests on the floor
The server SHALL treat a lift whose car was parked, deleted or moved away as being on the floor, and a client
SHALL never raise a lift on another player's behalf when no car is on it.

#### Scenario: Car on a raised lift is deleted
- **WHEN** a car on a lift at the middle position is removed from the garage
- **THEN** the server records that lift as on the floor, and a player joining afterwards sees the lift on the floor

### Requirement: Moving a car between places in the garage is shared
When a player moves a car to another place in the garage (an entrance place, a lift, the paint shop, the dyno,
the diagnostic path or the car wash), every other player SHALL see the same car at the same place, and the
server SHALL record that place for the car.

#### Scenario: Car moved onto a lift
- **WHEN** player A moves the car standing at the first entrance place onto the first lift
- **THEN** player B sees that car on the first lift, and the first entrance place is free for both players

### Requirement: Conflicting car moves are refused and reverted
The server SHALL refuse a car move when the target place is already taken by another car or the car's lift is
not on the floor. The player whose move was refused SHALL see the car return to the place the server holds.

#### Scenario: Two players move different cars to the same place
- **WHEN** players A and B move two different cars to the same free lift at nearly the same time
- **THEN** only the move the server received first is applied, and the other player's car returns to its previous place on every client

#### Scenario: Moving a car whose lift was just raised
- **WHEN** player A raises the lift under a car and player B, who has not yet seen that, moves the same car to the paint shop
- **THEN** the move is refused, and the car stays on the raised lift for both players

### Requirement: A car can be moved while another player works on it
Moving a car SHALL NOT be blocked because another player is working on it. Parts changed on the car before or
after the move SHALL stay in the same state for every player.

#### Scenario: Car moved during part work
- **WHEN** player B has removed a part from a car and player A then moves that car to a lift
- **THEN** both players see the car on the lift with that part still removed

### Requirement: Parking a car is shared and keeps the whole car
When a player moves a car from the garage to the parking lot, the car SHALL disappear from the garage for every
player and appear in the same parking slot for every player. The server SHALL store the complete car (parts,
condition, paint, fluids and other car data) so that the car comes back unchanged when it is taken out.

#### Scenario: Park a car
- **WHEN** player A moves a car from the garage to the parking lot
- **THEN** player B no longer sees the car in the garage, and B's parking lot shows the same car in the same slot

#### Scenario: Parked car keeps its state
- **WHEN** a car with a removed door is parked and later taken out again by another player
- **THEN** the car comes back into the garage with the door still removed, for every player

### Requirement: Simultaneous parking into the same slot keeps both cars
When two players park cars into the same free slot at nearly the same time, the server SHALL keep both cars by
moving the later one to another free slot, and every client SHALL show the slots as the server holds them. When
the parking lot is full on the server, the car SHALL stay in the garage.

#### Scenario: Same slot picked twice
- **WHEN** players A and B each park a different car and both games pick slot 3
- **THEN** both cars are in the parking lot for both players, one in slot 3 and the other in another free slot

#### Scenario: Parking lot full
- **WHEN** a player parks a car while the server's parking lot has no free slot
- **THEN** the car is put back in the garage for that player, and no other player sees it change

### Requirement: Taking a car out of parking is shared
When a player moves a car from the parking lot into the garage, the car SHALL leave the parking slot and appear
in the same garage place for every player, with all the data it had when it was parked.

#### Scenario: Take a car out
- **WHEN** player A moves the car in parking slot 3 into the garage
- **THEN** player B sees that car appear in the garage at the same place, and slot 3 is empty for both players

### Requirement: A parked car can only be taken out once
When two players take the same parked car out at nearly the same time, the server SHALL let only the first one
succeed. The other player's game SHALL remove the car it loaded and show the parking lot as the server holds it.

#### Scenario: Both players take out the same car
- **WHEN** players A and B both move the car in parking slot 3 into the garage before either sees the other's action
- **THEN** exactly one copy of the car is in the garage on every client, and slot 3 is empty for both players

### Requirement: Rearranging the parking lot is shared
When a player moves or swaps cars between parking slots, every player SHALL see the same cars in the same slots.
A rearrangement based on an outdated view of the parking lot SHALL be refused, and that player SHALL be shown
the server's parking lot.

#### Scenario: Swap two slots
- **WHEN** player A swaps the cars in parking slots 1 and 4
- **THEN** player B sees the car from slot 1 in slot 4 and the car from slot 4 in slot 1

### Requirement: Parking levels are shared and paid once
Unlocking a parking level SHALL cost money from the shared balance once and SHALL unlock that level for every
player. If two players unlock the same level at nearly the same time, the level SHALL be unlocked and paid only once.

#### Scenario: Unlock a parking level
- **WHEN** player A unlocks the next parking level
- **THEN** the shared money drops by the price once, and player B can use the new level

#### Scenario: Both players unlock the same level
- **WHEN** players A and B both unlock the second parking level at nearly the same time
- **THEN** the second level is unlocked and the price is charged only once

### Requirement: A joining player receives lifts, places and the parking lot
A player who joins while the session is running SHALL see every car at the place the server holds, every lift at
the position the server holds, and the same parking lot (cars per slot and unlocked levels) as the other players,
regardless of what the joining player's own save contains.

#### Scenario: Late join after changes
- **WHEN** player A raises a lift with a car to the top, moves a second car to the paint shop and parks a third car, and player B joins afterwards
- **THEN** player B sees the lift at the top with its car, the second car in the paint shop and the third car in the same parking slot as player A

### Requirement: Placement state is part of the server's game state
Lift positions, car places and the parking lot with its levels SHALL be part of the game state the server saves,
so they are the same after the server restarts.

#### Scenario: Server restart
- **WHEN** a car is parked and a lift is raised, the server saves and is restarted, and a player connects
- **THEN** the player sees the parked car in its slot and the lift at the saved position

### Requirement: Taking cars out from the parking scene is unavailable while connected
While connected to a session, a player in the separate parking scene SHALL be able to view parked cars but SHALL
NOT be able to move a car from there into the garage; the game SHALL tell the player to use the parking
management in the garage instead.

#### Scenario: Attempt from the parking scene
- **WHEN** a connected player in the parking scene chooses to move a car to the garage
- **THEN** nothing is moved, the player sees a message, and the parking lot is unchanged for every player
