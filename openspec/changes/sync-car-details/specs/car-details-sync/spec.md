# Spec Delta

## Purpose

Keeps a car's details the same for every player in the session, and makes them survive a late join and
a server restart. The details are fluids, wheels and alignment, tuning settings, paint/tint/dirt, license
plates and car info.

## ADDED Requirements

### Requirement: Detail sections
The system SHALL treat each car's details as eight sections and sync each section as a whole. The sections
are Fluids, Wheels, Alignment, Tuning, Paint, BodyCosmetics, Plates and Info. Fluids, BodyCosmetics and Tuning are the
exceptions: their entries are synced one at a time (Fluids per reservoir, BodyCosmetics per body part,
Tuning per module). Tire pressure and wheel balance are not part of car state.

#### Scenario: Section content
- **WHEN** a car's details are captured
- **THEN** Fluids holds the level and condition of every reservoir (oil, brake, coolant, power steering, washer fluid), identified by fluid type and reservoir id
- **AND** Wheels holds width, rim size, tire size/profile and ET for each of the four wheel positions
- **AND** Alignment holds the four wheel alignment values and the horizontal/vertical alignment of both headlamps
- **AND** Tuning holds the gearbox gear ratios and final drive and the tuning values of every ECU/carburettor module, each module identified by its part
- **AND** Paint holds the car colour, factory colour, factory paint type and custom paint data
- **AND** BodyCosmetics holds, for each body part, colour, paint type, paint data, livery and strength, tint flag and tint colour, dust and wash factor
- **AND** Plates holds front/rear plate numbers, the factory number and the front/rear textures
- **AND** Info holds mileage, buy price and where the car came from

### Requirement: Changes reach every player
When a player changes a section of a car in the garage, the system SHALL show the same values to every
other connected player within 3 seconds.

#### Scenario: Gearbox tuning
- **WHEN** player A applies new gear ratios to the car on loader 0
- **THEN** player B reads the same gear ratios and final drive on loader 0 within 3 seconds

#### Scenario: Oil refill
- **WHEN** player A tops up the engine oil of a car
- **THEN** player B sees the same oil level and condition on that car within 3 seconds after A stops pouring

#### Scenario: Window tint
- **WHEN** player A tints a window and confirms
- **THEN** that window has the same tint flag and tint colour on player B's car

### Requirement: Previews are not shared
The system SHALL share only committed values. It SHALL NOT share temporary values that a tool or window
shows while the player is still choosing.

#### Scenario: Paint shop preview
- **WHEN** player A picks colours in the paint shop and leaves without painting
- **THEN** player B never sees those colours on the car

#### Scenario: Tint window cancelled
- **WHEN** player A changes window tint in the tinting window and restores the backup before confirming
- **THEN** player B's car keeps its original tint

### Requirement: No echo or flooding
The system SHALL NOT send a section back to the server when the client has only applied it from the
network. While a value keeps changing, the system SHALL send at most one update per section per car per
debounce window.

#### Scenario: Remote apply does not echo
- **WHEN** player B applies a Fluids update received from the server
- **THEN** B sends no Fluids update for that car because of it

#### Scenario: Continuous pouring
- **WHEN** player A pours coolant for several seconds
- **THEN** the server receives a few Fluids updates for that car, not one per frame, and the last one holds the final level

### Requirement: Concurrent edits converge
When two players change the same car at the same time, the system SHALL make every client end up with the
server's merged state. Changes to different sections, different reservoirs, different body parts or
different tuning modules SHALL all be kept. For the same section or entry, the change that reaches the
server last SHALL win.

#### Scenario: Different sections at once
- **WHEN** player A changes the wheel alignment and player B refills the brake fluid on the same car at the same moment
- **THEN** both clients and the server end up with A's alignment and B's brake fluid level

#### Scenario: Two fluids at once
- **WHEN** player A pours engine oil while player B pours coolant into the same car
- **THEN** both clients and the server end up with A's oil level and B's coolant level

#### Scenario: Same section at once
- **WHEN** players A and B both change the Plates section of the same car at the same moment
- **THEN** both clients end up with the plate values that reached the server last

### Requirement: Spawn values are shared
When a car appears in the garage, every client SHALL use the detail values from the client that spawned
it, including any values that were rolled at random. The other clients' own random rolls SHALL NOT be used.

#### Scenario: Customer car arrives
- **WHEN** player A's game spawns a car and rolls random dirt, fluid levels, alignment and license plates for it
- **THEN** player B's copy of the car shows A's values for all sections
- **AND** B sends no detail values of its own for that car before A's values are applied

#### Scenario: Server has no details for a loaded car
- **WHEN** the server has a car on a loader but no stored details for it
- **THEN** the server asks one connected client that has the car loaded for a full snapshot, stores it and sends it to every client
- **AND** if that client does not answer within 10 seconds, the server asks the next one

### Requirement: Late join receives details
A client that connects while cars are in the garage SHALL get each car's stored details. The details SHALL
be applied after that car's parts have been replayed, and before the client acknowledges that its initial
sync is complete.

#### Scenario: Join after changes
- **WHEN** player A has changed the gearbox, a fluid, the plates and a body panel colour on a car, and player B then connects
- **THEN** when B acknowledges its initial sync, B's copy of that car already matches A's in every section

### Requirement: Server owns and persists details
The server SHALL keep the latest merged details for each car loader. It SHALL write them in its save
file and restore them on restart. It SHALL drop them when the car is deleted, and start empty when a
new car is spawned on that loader.

#### Scenario: Server restart
- **WHEN** the server is saved and restarted with a car whose oil level was changed
- **THEN** a client that connects afterwards receives the changed oil level

#### Scenario: Car replaced on a loader
- **WHEN** the car on loader 1 is deleted and a different car is spawned on loader 1
- **THEN** none of the old car's details are sent for the new car

### Requirement: Invalid and stale updates are rejected
The server SHALL drop a detail update when no car is on that loader, or when the update belongs to an
earlier car on that loader (a car that has since been deleted, parked or replaced). The server SHALL clamp fluid levels and conditions, dust and wash
factor to the range 0 to 1 before storing them.

#### Scenario: Update for a deleted car
- **WHEN** a detail update arrives for a loader whose car was deleted a moment ago
- **THEN** the server drops it, logs it and does not send it to anyone

#### Scenario: Update for a replaced car
- **WHEN** a detail update for the previous car on loader 0 arrives after a new car of the same model was spawned there
- **THEN** the server drops it and the new car's details are unchanged

#### Scenario: Out-of-range value
- **WHEN** a Fluids update has an oil level of 1.7
- **THEN** the server stores and sends an oil level of 1.0

### Requirement: Mounting a body part replaces its cosmetics
When a different body part is mounted in a slot, the system SHALL use the cosmetics that come with the
mounted part. Cosmetic values stored earlier for that slot SHALL NOT be applied to the new part, whether
live or on late join.

#### Scenario: Swap a door
- **WHEN** the red-painted left door is removed and a blue door from inventory is mounted
- **THEN** every client, including one that joins later, shows the blue door
