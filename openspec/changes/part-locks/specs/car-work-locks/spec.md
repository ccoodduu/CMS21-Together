# Spec Delta

## Purpose

Makes sure two players never run conflicting work on the same car. Before an action that changes a part, a fluid,
an inventory item used for a mount, or the car's position starts, the server grants a lock that covers everything
that belongs together. A player sees a part another player is using as unavailable before trying to use it.

## ADDED Requirements

### Requirement: Work starts only after the server grants a lock
An action that mounts or unmounts a part, fills or drains a fluid, takes the engine out or puts it in, moves a car or
moves a lift SHALL start only after the server has granted a lock for it. A denied action SHALL NOT start, and no
rollback of part, fluid or inventory state SHALL be needed for it.

#### Scenario: Two players start the same part at the same moment
- **WHEN** player A and player B start unmounting the same bearing cap within the same second
- **THEN** exactly one of them unmounts it, the other's action never starts, and both inventories and car states are equal afterwards

#### Scenario: Same inventory item mounted by two players
- **WHEN** player A and player B try to mount the same inventory item into two slots at once
- **THEN** exactly one mount starts, the other player is told the item is being mounted, and the item exists exactly once on the car or in the inventory

#### Scenario: Grant arrives after a delay
- **WHEN** player A's connection adds 150 ms of delay and player A clicks a free part to unmount it
- **THEN** the unmount starts once the grant arrives and nothing else about the action changes

### Requirement: A lock covers what belongs together
A lock on a part SHALL also cover the part's unmount group, the parts it is fixed to (the part it is mounted on and
the parts the game blocks it with), the fluids it holds, gates or drains, and the car's position. Two players SHALL be
able to work at the same time on parts that share only the part they are mounted on.

#### Scenario: Part and the part it is fixed to
- **WHEN** player A is unscrewing a bearing cap and player B tries to unmount the crankshaft it holds
- **THEN** player B's action does not start and player B is told that player A is working on the bearing cap

#### Scenario: Sibling parts
- **WHEN** player A is unscrewing one bearing cap and player B starts unscrewing another bearing cap of the same crankshaft
- **THEN** both actions run

#### Scenario: Engine taken out while a part of it is in work
- **WHEN** player B is mounting a part on the engine and player A tries to take the engine out with the crane
- **THEN** player A's action does not start

### Requirement: Fluid work and the fluid's parts exclude each other
Filling, extracting or draining a fluid SHALL lock that fluid of the car exclusively, and unmounting a part that
holds, gates or drains that fluid SHALL be refused while the fluid is locked, and the other way round. When a fluid
lock ends, the fluid level SHALL already be on the server before another player can lock it.

#### Scenario: Reservoir removed while filling
- **WHEN** player A is filling coolant and player B tries to unmount the coolant reservoir
- **THEN** player B's action does not start and player B is told player A is working on the coolant system

#### Scenario: Filling while the reservoir is being removed
- **WHEN** player B is unscrewing the coolant reservoir and player A tries to fill coolant
- **THEN** player A's fill does not start

#### Scenario: Next player sees the final level
- **WHEN** player A finishes filling brake fluid and player B starts filling brake fluid right after
- **THEN** player B starts from the level player A left

### Requirement: The car does not move while someone works on it
A car SHALL NOT be moved to another place, swapped, or lifted or lowered while another player holds a lock on any
part or fluid of it. Nobody SHALL be able to start work on a car while its lift or the car itself is moving, or while
it is away on a test drive, test path or dyno run of another player.

#### Scenario: Lift while another player mounts a part
- **WHEN** player B is mounting a brake caliper and player A tries to raise the lift the car stands on
- **THEN** the lift does not move on any client and player A is told player B is working on this car

#### Scenario: Part work while the lift moves
- **WHEN** player A's lift is moving and player B tries to unmount a part of the car on it
- **THEN** player B's action does not start until the lift has stopped

#### Scenario: Test drive while a part is in work
- **WHEN** player B is unscrewing a part and player A tries to take the car on a test drive
- **THEN** the test drive is refused

### Requirement: A part in use cannot be selected
While another player holds a lock that a player's action would conflict with, that player's game SHALL NOT
highlight the part as selectable on hover. It SHALL show "<name> is working on this part" (or names the connected
part, fluid system or car), SHALL hide the part's slot in mount mode, SHALL show the matching pie-menu options as
unavailable, and SHALL grey out inventory items another player is mounting. A click on such a part SHALL be refused
without asking the server. When the lock ends, everything SHALL return to normal without any action by the player.

#### Scenario: Hovering a part in use
- **WHEN** player A is unscrewing a wheel and player B points at that wheel in disassembly mode
- **THEN** player B sees no selection highlight and reads that player A is working on this part

#### Scenario: Mount mode
- **WHEN** player A is mounting a brake disc and player B enters mount mode with another brake disc
- **THEN** player B sees no mount preview on the slot player A is using

#### Scenario: Lock ends
- **WHEN** player A finishes the wheel
- **THEN** player B can select it immediately

### Requirement: The player is told what is happening
A player whose action waits for the server longer than 150 ms SHALL see that the game is waiting for the server. A
denied action SHALL play the game's error sound and name the player who holds the conflicting lock. An action whose
answer does not arrive within 3 seconds SHALL be cancelled with a message, and a grant that arrives later SHALL be
released without starting the action.

#### Scenario: Denied action
- **WHEN** player B clicks a part player A is working on and player B's mirror had not yet shown the lock
- **THEN** player B hears the error sound and reads that player A is working on this part

#### Scenario: No answer
- **WHEN** player A's connection stalls for 4 seconds right after player A clicks a part
- **THEN** player A's action is cancelled with a message, and when the connection recovers the server holds no lock for player A

### Requirement: Locks always end
A lock SHALL end when its work is finished or cancelled, when its owner disconnects or leaves the garage, when the
car is removed or replaced, and when its owner's game has not renewed it for 90 seconds. A player who holds a part
without progress for 5 minutes SHALL have that action cancelled by their own game.

#### Scenario: Holder disconnects
- **WHEN** player A disconnects while unscrewing a part
- **THEN** the lock ends and player B can unmount the part immediately

#### Scenario: Holder stops renewing
- **WHEN** player A's game stops renewing a held lock
- **THEN** the server ends the lock within 90 seconds

### Requirement: Locks reach a joining player
A player who joins or returns to the garage SHALL receive every active lock before the initial sync ends, SHALL be
blocked by them like any other player, and SHALL NOT see animations of work that started before they joined.

#### Scenario: Joining while another player holds locks
- **WHEN** player B joins while player A is filling oil and unscrewing a part
- **THEN** player B cannot select that part or start an oil fill, and no part or bolt animation plays for work that started before player B joined

### Requirement: Four players contend correctly
With four players working on the same car, the server SHALL never hold two conflicting locks of different players
at the same time, and no part change SHALL be rejected for a conflict that a lock should have prevented.

#### Scenario: Four players on one car
- **WHEN** four players repeatedly try the same part, connected parts, a fluid and the lift for several minutes
- **THEN** the server reports no overlapping locks, no part change is rejected, and every client's view of the locks equals the server's at the end
