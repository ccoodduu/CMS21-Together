# Spec Delta

## Purpose

Keeps the body parts and mechanical parts of every car loaded in the shared garage in the same state on all
connected clients, resolves conflicts when players act on the same part, and replays the cars and their parts
to a player who joins mid-session.

## ADDED Requirements

### Requirement: Parts are identified the same way on every client
Every part of a loaded car SHALL have a key that refers to the same physical part on every client that has
loaded the same car model and configuration. A client that cannot resolve a received key to a part of the
expected kind SHALL NOT apply the change to another part; it SHALL request a full resync of that car instead.

#### Scenario: Same key, same part
- **WHEN** two clients have loaded the same car and player A unmounts the third piston
- **THEN** player B's client removes the third piston, not another piston with the same name

#### Scenario: Key does not resolve
- **WHEN** a client receives a part change whose key or part id does not match its local car
- **THEN** the client leaves its parts unchanged, logs the mismatch and receives the server's full state for that car

### Requirement: Unmounting a part is shared together with the item it produces
When a player unmounts a body part or mechanical part from a car in the garage, every connected client SHALL
show that part as unmounted and the shared inventory SHALL contain exactly one item (or group item) for it,
with the condition, quality and other state the part had on the car.

#### Scenario: Unmount a mechanical part
- **WHEN** player A unmounts a mounted mechanical part
- **THEN** player B sees the part unmounted within one second and both players see one new item for it in the shared inventory

#### Scenario: Unmount a body part
- **WHEN** player A takes off a mounted door
- **THEN** player B sees the door removed and both inventories contain the door item with its condition and dent

### Requirement: Mounting a part is shared together with the item it consumes
When a player mounts a part on a car in the garage, every connected client SHALL show that part as mounted with
the state of the item that was used, and that item SHALL be removed from the shared inventory for everyone.

#### Scenario: Mount from the shared inventory
- **WHEN** player B mounts a part using an item from the shared inventory
- **THEN** player A sees the part mounted with that item's condition and quality, and the item is gone from both inventories

### Requirement: Conflicting mount and unmount actions are resolved by the server
The server SHALL accept a mount or unmount only if the part is still in the state the player saw and every item
the action consumes is still in the shared inventory. The first accepted action wins; a rejected player's client
SHALL roll back its local part state and inventory change to the server's state.

#### Scenario: Two players unmount the same part
- **WHEN** players A and B unmount the same mounted part at nearly the same time
- **THEN** the part ends unmounted on both clients and the shared inventory holds exactly one item for it

#### Scenario: Item already used by another player
- **WHEN** player A mounts an item that player B has just mounted on another car
- **THEN** player A's mount is rolled back, the part stays unmounted on all clients and no item is duplicated

### Requirement: A part being worked on is reserved
While a player is mounting or unmounting a part, the other players SHALL NOT be able to start mounting or
unmounting that same part. The reservation SHALL end when the action finishes or is cancelled, when that player
disconnects, or after a timeout.

#### Scenario: Other player tries the same part
- **WHEN** player A is unscrewing the bolts of a part and player B tries to unmount it
- **THEN** player B's action does not start and player B is told the part is in use

#### Scenario: Reservation holder disconnects
- **WHEN** player A disconnects while unmounting a part
- **THEN** the reservation is released and player B can unmount the part

#### Scenario: Reservation times out
- **WHEN** a reservation has not been released for 120 seconds
- **THEN** the server releases it and other players can act on the part again

#### Scenario: Joining player sees reservations
- **WHEN** player B joins while player A holds a reservation on a part
- **THEN** player B cannot start mounting or unmounting that part until the reservation ends

### Requirement: Part attributes on the car are shared
Changes to a part's condition, quality, dent, examined flag, opened/closed state and tuned variant while it is on
a car, and to the dust and paint of a mechanical part, SHALL be shown on every client. Once a part is examined it
SHALL stay examined for everyone.

#### Scenario: Examine a part
- **WHEN** player A examines a part
- **THEN** player B sees that part as examined with its condition

#### Scenario: Open a door
- **WHEN** player A opens the hood of a car
- **THEN** player B sees the hood open

### Requirement: The server holds a complete part state for every loaded car
After a car is spawned in the garage, the server SHALL hold the state of every part of that car, taken from the
client that spawned it, and every other client SHALL bring its copy of the car to that state once the car has
loaded. Deleting a car SHALL remove its stored part state.

#### Scenario: Spawned car has the same damage everywhere
- **WHEN** player A spawns a car whose parts have random conditions
- **THEN** after loading, player B's copy has the same condition and mounted state for every part

#### Scenario: Part state survives a server restart
- **WHEN** the server saves, is restarted and a player joins
- **THEN** that player receives every car with a stored baseline with the same part states as before the restart

#### Scenario: Car deleted
- **WHEN** a car is deleted from a car loader
- **THEN** the server no longer holds part state for that loader and a new car on that loader starts from its own baseline

### Requirement: Changes that arrive during car loading are not lost or misapplied
Part changes received for a car that is still loading on a client SHALL be applied, in order, once the car has
loaded and its baseline is applied. Changes that belong to a car that has since been deleted or replaced SHALL
be discarded.

#### Scenario: Change during load
- **WHEN** player A unmounts a part while player B is still loading that car
- **THEN** player B's car shows the part unmounted once loading finishes

#### Scenario: Change for a replaced car
- **WHEN** a part change for the previous car on a loader arrives after a new car was spawned there
- **THEN** the change is ignored

### Requirement: A joining player receives all loaded cars and their parts
A client joining a running session SHALL load exactly the cars the server holds, in the same car loaders, with
the server's part states, and SHALL NOT load cars from its own local save while connected. Initial sync SHALL
finish only after those cars are loaded and their parts applied.

#### Scenario: Late join after part changes
- **WHEN** player A unmounts and examines parts on a car and player B connects afterwards
- **THEN** player B's garage shows the same car with the same unmounted and examined parts as player A's

#### Scenario: Local save has other cars
- **WHEN** a player joins whose local save has cars in the garage that the server does not hold
- **THEN** those cars do not appear in that player's garage

#### Scenario: Car with a swapped engine
- **WHEN** a player joins after the engine of a loaded car was swapped
- **THEN** the joining player's copy has the swapped engine and the same engine part states

#### Scenario: Spawner has left
- **WHEN** the player who spawned and modified a car disconnects and another player joins later
- **THEN** the joining player still receives the car with all its part states from the server
