# Spec Delta

## Purpose

Keeps the garage machines shared between all players in a session: what each machine holds, how it is set up,
where movable tools stand, and what machine work does to items and cars, including for players who join later.

## ADDED Requirements

### Requirement: Shared machine contents
When a player puts an item or item group on a slot machine (tire changer, wheel balancer, spring clamp, engine stand 1 or 2, brake lathe, battery charger), every other player in the garage SHALL see the same item, with the same UID and state, on the same machine.

#### Scenario: Wheel put on the tire changer
- **WHEN** player A puts a wheel from the shared inventory on the tire changer
- **THEN** player B sees that wheel on B's tire changer, and the wheel is in neither player's inventory

#### Scenario: Engine put on the second engine stand
- **WHEN** player A puts an engine on engine stand 2
- **THEN** player B sees the engine on engine stand 2, and engine stand 1 is unchanged

### Requirement: Taking an item off a machine
Any player SHALL be able to take the item off a machine that another player loaded. The item SHALL return to the shared inventory exactly once and the machine SHALL be empty for every player.

#### Scenario: Other player takes the wheel
- **WHEN** player A put a wheel on the tire changer and player B takes it off
- **THEN** the wheel is in the shared inventory once for both players and both tire changers are empty

### Requirement: One winner when players race for a machine
The server SHALL decide the outcome when two players change the same machine at nearly the same time. The losing player's change SHALL be undone on that player's client, and no item SHALL be lost or duplicated.

#### Scenario: Two players load the same machine
- **WHEN** players A and B each put a different wheel on the empty tire changer at the same moment
- **THEN** exactly one wheel ends up on the tire changer for both players, and the other wheel is back in the shared inventory

#### Scenario: Two players take the same item
- **WHEN** players A and B both take the wheel off the tire changer at the same moment
- **THEN** the wheel is in the shared inventory exactly once and the tire changer is empty for both players

### Requirement: Machine settings are shared
Machine settings that are visible in the world SHALL be shared: the mounted/separated state of the tire changer and spring clamp, the rotation angle of each engine stand, and whether the battery charger is switched on.

#### Scenario: Engine stand rotated
- **WHEN** player A rotates the engine on engine stand 1
- **THEN** player B sees the engine at the same angle once A stops rotating

#### Scenario: Wheel separated on the tire changer
- **WHEN** player A separates the tire from the rim on the tire changer
- **THEN** player B's tire changer shows the separated tire and rim

### Requirement: Results of machine work on items
Work done by a machine or the repair table SHALL change the item for every player: a balanced wheel stays balanced, a repaired or painted part has the same condition, dent and paint for every player, and the result is kept when the item is later taken off or used by another player.

#### Scenario: Wheel balanced by one player, taken by another
- **WHEN** player A balances the wheel on the wheel balancer and player B takes it off
- **THEN** the wheel in the shared inventory is balanced

#### Scenario: Part repaired on the repair table
- **WHEN** player A repairs a part from the shared inventory on the repair table
- **THEN** player B's inventory shows the part with the new condition

### Requirement: Results of tool work on cars
Using the welder, car wash, interior detailing kit, oil bin, engine crane or paint shop on a car SHALL leave the car in the same state for every player, through the same car state that part and detail sync use.

#### Scenario: Car washed
- **WHEN** player A washes a car in the car wash
- **THEN** player B sees the car clean after A's wash finishes

#### Scenario: Engine removed with the crane
- **WHEN** player A removes a car's engine with the engine crane
- **THEN** player B sees the car without its engine and the engine group in the shared inventory

### Requirement: Visible tool actions
Other players SHALL see a tool working when it acts on a car or item (sparks, wash or paint effect, sound) at the right place. Seeing the effect SHALL NOT lock the other player's controls or change state a second time.

#### Scenario: Welding seen by another player
- **WHEN** player A welds a car on lifter 1
- **THEN** player B sees the welding effect at that car and can keep moving and using other tools

### Requirement: Shared tool positions
The position of each movable tool (welder, interior detailing kit, oil bin, engine crane, headlamp aligner, window tinting kit) SHALL be the same for every player, both when moved to a car place and when sent back to its default position.

#### Scenario: Welder moved to a car place
- **WHEN** player A moves the welder to car lifter 2
- **THEN** player B's welder stands at car lifter 2

### Requirement: Machine state for players who join later
A player who connects while machines hold items, have settings or have been moved SHALL see the same machine contents, settings and tool positions as the players already in the session, and the joining player's own save SHALL NOT add or remove anything on the machines or in the shared inventory.

#### Scenario: Late join with a loaded machine
- **WHEN** player A has a wheel on the tire changer, an engine rotated on engine stand 1 and the welder at lifter 1, and player B connects
- **THEN** B sees the same wheel, engine, angle and welder position, and B's dump of machines and inventory equals A's

#### Scenario: Joining player's own save had something on a machine
- **WHEN** player B's own save has a battery on the battery charger and the session has an empty charger
- **THEN** after B joins, B's battery charger is empty and that battery is not added to the shared inventory

### Requirement: Machine state survives a server restart
The server SHALL keep machine contents, settings and tool positions as part of the session state that it saves and loads.

#### Scenario: Restart with an engine on the stand
- **WHEN** an engine is on engine stand 1 and the server is restarted with the saved session
- **THEN** players who connect see the engine on engine stand 1 again
