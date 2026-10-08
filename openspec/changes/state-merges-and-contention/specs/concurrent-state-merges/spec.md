# Spec Delta

## Purpose

Makes sure that what one player does to a car, its details, an inventory item, a seat or a parked car is never undone
or duplicated by another player who acted at the same time on an older view of the same thing, and that a player whose
action the server refused, ignored or overrode always ends up with the server's state at once.

## ADDED Requirements

### Requirement: A change that does not mount or unmount a part never changes its mount state
A part change that only examines a part, runs a diagnostic tool on it, or changes its condition, quality or dust SHALL
NOT change that part's mount state, its part id or its bolts, on the server or on any client, even when the player who
made it had not yet seen another player's mount or unmount of that part. A change made on a view of a part that has
since been unmounted or replaced SHALL NOT be written onto the part that is there now. A change of another player
SHALL NOT overwrite a local change of a different property that the player has not sent yet.

#### Scenario: Examine on a part another player has just unmounted
- **WHEN** player B unmounts a brake disc and player A, who has not received that yet, examines the car with a diagnostic tool that covers the disc
- **THEN** the disc is unmounted on the server and on every client, and the disc's item exists exactly once, in the inventory

#### Scenario: Change on a part another player has just replaced
- **WHEN** player B unmounts a worn brake disc and mounts a new one, and player A, who has seen neither change, examines the car or changes the disc's condition
- **THEN** the new disc stays mounted with its own condition on every client, and the worn disc's item exists exactly once

#### Scenario: Examine while another player unscrews the part
- **WHEN** player B is unscrewing a part and player A examines the car
- **THEN** player B's unscrewing goes on undisturbed, and player B's unmount is accepted when it finishes

#### Scenario: Own change not sent yet
- **WHEN** player B changes a part's condition and, before player B's change is sent, player A's examine of that part arrives at player B
- **THEN** player B's condition reaches the server and every client, and the part is marked examined

#### Scenario: Examine during another player's lock
- **WHEN** player B holds a lock on a part, player A examines it, and player B then cancels the unmount
- **THEN** the part stays mounted and examined on every client

#### Scenario: Mounted and unmounted again within half a second
- **WHEN** a part is mounted and unmounted again on another player's screen within half a second
- **THEN** the part is not visible on that player's car afterwards

### Requirement: Changes to different details of one car are all kept
When several players change different entries of one car's details at the same time (different fluids, different
body panels, different wheels, different alignment values, different tuning modules), every change SHALL be kept on
the server and on every client. When two players change the same entry (the same fluid, the same panel, the same
wheel), every client and the server SHALL end with the change the server received last.

#### Scenario: Two fluids at once
- **WHEN** player A fills brake fluid and player B fills coolant on the same car at the same moment
- **THEN** every client and the server show player A's brake fluid level and player B's coolant level

#### Scenario: Two body panels at once
- **WHEN** player A washes the hood and player B washes a door of the same car at the same moment
- **THEN** every client shows both panels clean

#### Scenario: Two wheels and two alignment values
- **WHEN** player A changes the front left wheel and the front left alignment while player B changes the rear right wheel and the rear right alignment
- **THEN** every client shows all four changes

#### Scenario: Own change not yet sent when another player's change arrives
- **WHEN** player A changes the brake fluid and, before player A's change is sent, player B's coolant change arrives at player A
- **THEN** player A's brake fluid change still reaches the server and every client

#### Scenario: Same fluid by two players, in either order
- **WHEN** player A and player B set the same fluid of one car to different levels at the same moment, whichever reaches the server first
- **THEN** every client and the server end with the level the server received last

### Requirement: A player's own change is never rolled back by its echo
A client SHALL NOT set a value of a car's details back to a value it sent earlier when the server's copy of its own
change comes back, unless another player wrote that value in between or the server changed it (for example a level
limited to its range).

#### Scenario: Pouring a fluid with a slow connection
- **WHEN** player A pours brake fluid for three seconds with 150 ms of network delay
- **THEN** the level on player A's screen never drops during the pour, and player B sees the same final level as player A

### Requirement: An inventory item ends up in exactly one place
An inventory item or group SHALL end up in exactly one of the shared inventory, the warehouse, one workshop machine,
mounted on one car or one engine stand, or sold, whatever two players do with it at the same time. A player whose
machine put lost SHALL be told who used the item.

#### Scenario: Wheel put on the tire changer while another player mounts it
- **WHEN** player A puts a wheel on the tire changer and player B mounts the same wheel on a car at the same moment
- **THEN** the wheel is either on the tire changer or on the car on every client and the server, and taking it off the tire changer never creates a second wheel

#### Scenario: Item put on a machine while another player sells it
- **WHEN** player A puts a part on the brake lathe and player B sells the same part at the same moment
- **THEN** the part is either on the lathe or sold, the money changes only if it was sold, and the part exists at most once

#### Scenario: Item mounted while another player sells it
- **WHEN** player A mounts a part and player B sells the same part at the same moment
- **THEN** the part is either mounted or sold, never both, and the money changes only if it was sold

#### Scenario: Same engine-stand part by two players
- **WHEN** player A and player B unmount the same part of the engine on the engine stand at the same moment
- **THEN** one unmount is kept, the other is undone on its player's screen, and the part's item exists exactly once

#### Scenario: Same item moved to the warehouse twice
- **WHEN** player A and player B move the same item to the warehouse at the same moment
- **THEN** the item is in the warehouse once on every client and the server

#### Scenario: Put refused while another player is mounting the item
- **WHEN** player B is mounting a part and player A tries to put the same part on a machine
- **THEN** player A's put is refused with a message naming player B, the part stays available for player B's mount, and player B's mount succeeds

### Requirement: A parked car keeps every accepted change
Parking a car SHALL keep every change to its parts and details that the server accepted before the park, including
changes made by another player that had not yet reached the player who parks it, and the parker's own last change. A
change that reaches the server after the park SHALL be refused and undone for the player who made it, so that its
item is never duplicated.

#### Scenario: Unmount by another player just before the park
- **WHEN** player B unmounts a part and player A, who has not received that yet, parks the car
- **THEN** after the car is taken out of the parking again, the part is unmounted on every client, and its item exists exactly once

#### Scenario: Own unmount just before the park
- **WHEN** player A unmounts a part and parks the car within a fraction of a second
- **THEN** after the car is taken out of the parking again, the part is unmounted, and its item exists exactly once

#### Scenario: Fluid changed by another player just before the park
- **WHEN** player B changes the coolant level, the server receives it, and player A parks the car before player B's change reached player A
- **THEN** after the car is taken out of the parking again, it has player B's coolant level

#### Scenario: The player taking the car out leaves at once
- **WHEN** player B starts taking a parked car out and disconnects before the car has finished loading
- **THEN** the car is back in its parking slot, and when player A takes it out later it has every change from before the park

#### Scenario: Server restart with a parked car
- **WHEN** a car is parked with changes made by two players and the server restarts
- **THEN** after the restart and taking the car out of the parking, it has every change

### Requirement: A refused, ignored or overridden action is answered with the server's state
Whenever the server refuses, ignores or overrides a player's action, it SHALL send that player the authoritative
result, so that the player's game equals the server's state right after the answer, without a resync.

#### Scenario: Two players finish the same job
- **WHEN** player A and player B finish the same job at the same moment
- **THEN** the payout is added once, and both players' money and job lists equal the server's right after

#### Scenario: Two players unlock the same skill
- **WHEN** player A and player B unlock the same skill at the same moment
- **THEN** the skill points are spent once, and both players' skills and points equal the server's right after

#### Scenario: Order expires while it is accepted
- **WHEN** player A accepts an order that has just expired on the server
- **THEN** player A is told the order is no longer available, and player A's order list equals the server's

#### Scenario: Repair of an item another player has mounted
- **WHEN** player A repairs an item on the repair table that player B has just mounted on a car
- **THEN** the repair is refused, and player A's inventory equals the server's right after

#### Scenario: Two players park the same car
- **WHEN** player A and player B park the same car at the same moment
- **THEN** the car is parked once, and both players' garage and parking equal the server's right after

#### Scenario: Order generated by a player who is not the generator
- **WHEN** a player who is not the order generator sends a new order
- **THEN** the order is not added, and that player's order list equals the server's right after

### Requirement: Two players never sit in the same seat
When two players try to sit in the same seat of a car, the server SHALL keep the first and refuse the second, and the
refused player SHALL leave the seat at once and be told who sits there.

#### Scenario: Two players sit down in the same seat
- **WHEN** player A and player B sit down in the driver's seat of the same car at the same moment
- **THEN** one of them sits there, the other is out of the seat on their own screen with a message naming the other player, and every client shows one player in that seat
