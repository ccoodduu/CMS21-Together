# Spec Delta

## Purpose

Makes sure that what one player does to a car, its details, an inventory item or a parked car is never undone or
duplicated by another player who acted at the same time on an older view of the same thing.

## ADDED Requirements

### Requirement: A change that does not mount or unmount a part never changes its mount state
A part change that only examines a part, runs a diagnostic tool on it, or changes its condition, quality, paint or
dust SHALL NOT change that part's mount state, its part id or its bolts, on the server or on any client, even when
the player who made it had not yet seen another player's mount or unmount of that part. The examined state,
condition, quality, paint or dust it carries SHALL be kept.

#### Scenario: Examine on a part another player has just unmounted
- **WHEN** player B unmounts a brake disc and player A, who has not received that yet, examines the car with a diagnostic tool that covers the disc
- **THEN** the disc is unmounted on the server and on every client, it is marked examined, and the disc's item exists exactly once, in the inventory

#### Scenario: Examine on a part another player has just replaced
- **WHEN** player B unmounts a worn brake disc and mounts a new one, and player A, who has seen neither change, examines the car
- **THEN** the new disc stays mounted with its own condition on every client, and the worn disc's item exists exactly once

#### Scenario: Examine while another player unscrews the part
- **WHEN** player B is unscrewing a part and player A examines the car
- **THEN** player B's unscrewing goes on undisturbed, and player B's unmount is accepted when it finishes

#### Scenario: Condition change made during another player's lock
- **WHEN** player A changes the condition of a part that player B holds a lock on, and player B then unmounts that part
- **THEN** the part is unmounted and keeps player A's condition on every client

### Requirement: Changes to different details of one car are all kept
When several players change different entries of one car's details at the same time (different fluids, different
body panels' paint, dust, wash or tint, different wheels, different alignment values, different tuning modules),
every change SHALL be kept on the server and on every client. When two players change the same entry, the change the
server receives last SHALL be the one every client keeps.

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

#### Scenario: Same fluid by two players
- **WHEN** player A and player B set the same fluid of one car to different levels at the same moment
- **THEN** every client and the server end with the same one of the two levels

### Requirement: A player's own change is never rolled back by its echo
A client SHALL NOT set a value of a car's details back to a value it sent earlier when the server's copy of its own
change comes back. Only a value the server changed (for example a level limited to its range) SHALL be applied from
that copy.

#### Scenario: Pouring a fluid with a slow connection
- **WHEN** player A pours brake fluid for three seconds with 150 ms of network delay
- **THEN** the level on player A's screen never drops during the pour, and player B sees the same final level as player A

### Requirement: An inventory item ends up in exactly one place
An inventory item or group SHALL end up in exactly one of the shared inventory, the warehouse, one workshop machine,
or mounted on one car, whatever two players do with it at the same time. A player whose action lost SHALL be told
who used the item.

#### Scenario: Wheel put on the tire changer while another player mounts it
- **WHEN** player A puts a wheel on the tire changer and player B mounts the same wheel on a car at the same moment
- **THEN** the wheel is either on the tire changer or on the car on every client and the server, and taking it off the tire changer never creates a second wheel

#### Scenario: Item put on a machine while another player sells it
- **WHEN** player A puts a part on a machine and player B sells the same part at the same moment
- **THEN** the part is either on the machine or sold, the money changes only if it was sold, and the part exists at most once

#### Scenario: Put refused while another player is mounting the item
- **WHEN** player B is mounting a part and player A tries to put the same part on a machine
- **THEN** player A's put is refused with a message naming player B, the part stays available for player B's mount, and player B's mount succeeds

### Requirement: A parked car keeps every accepted change
Parking a car SHALL keep every change to its parts and details that the server accepted before the park, including
changes made by another player that had not yet reached the player who parks it. A change that reaches the server
after the park SHALL be refused and undone for the player who made it, so that its item is never duplicated.

#### Scenario: Unmount by another player just before the park
- **WHEN** player B unmounts a part and player A, who has not received that yet, parks the car
- **THEN** after the car is taken out of the parking again, the part is unmounted on every client, and its item exists exactly once

#### Scenario: Fluid changed by another player just before the park
- **WHEN** player B changes the coolant level, the server receives it, and player A parks the car before player B's change reached player A
- **THEN** after the car is taken out of the parking again, it has player B's coolant level

#### Scenario: Server restart with a parked car
- **WHEN** a car is parked with changes made by two players and the server restarts
- **THEN** after the restart and taking the car out of the parking, it has every change
