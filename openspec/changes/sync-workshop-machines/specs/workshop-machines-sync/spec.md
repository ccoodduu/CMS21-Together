# Spec Delta

## Purpose

Keeps the garage machines shared between all players in a session: what each machine holds, how it is set up,
who is using the wheel balancer, where movable tools stand, and what machine work does to items, including for
players who join later.

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

#### Scenario: Other player takes the engine off the stand
- **WHEN** player A put an engine on engine stand 1 and player B takes it off
- **THEN** exactly one engine group is in the shared inventory for both players and both stands are empty

### Requirement: One winner when players race for a machine
The server SHALL decide the outcome when two players change the same machine, or load the same item, at nearly the same time. The losing player's change SHALL be undone on that player's client, and no item SHALL be lost or duplicated.

#### Scenario: Two players load the same machine
- **WHEN** players A and B each put a different wheel on the empty tire changer at the same moment
- **THEN** exactly one wheel ends up on the tire changer for both players, and the other wheel is back in the shared inventory

#### Scenario: Two players take the same item
- **WHEN** players A and B both take the engine off engine stand 1 at the same moment
- **THEN** exactly one engine group is in the shared inventory and the stand is empty for both players

#### Scenario: Same item put on two machines
- **WHEN** player A puts a wheel on the tire changer and player B puts the same wheel on the wheel balancer at the same moment
- **THEN** the wheel is on exactly one of the two machines for both players and is not in the shared inventory

### Requirement: Machine settings are shared
Machine settings that are visible in the world SHALL be shared: the mounted/separated state of the tire changer and spring clamp, the rotation angle of each engine stand, and whether the battery charger is switched on.

#### Scenario: Engine stand rotated
- **WHEN** player A rotates the engine on engine stand 1
- **THEN** player B sees the engine at the same angle once A stops rotating

#### Scenario: Wheel separated on the tire changer
- **WHEN** player A separates the tire from the rim on the tire changer
- **THEN** player B's tire changer shows the separated tire and rim

### Requirement: Parts on an engine on the stand
Mounting or unmounting a part on an engine on an engine stand SHALL change that engine for every player, and the unmounted part SHALL appear in the shared inventory exactly once, also when two players act on the same part at once.

#### Scenario: Part removed from the engine on the stand
- **WHEN** player A unmounts a part from the engine on engine stand 1
- **THEN** player B sees the part missing from that engine and the part is in the shared inventory once

#### Scenario: Two players remove the same part on the stand
- **WHEN** players A and B unmount the same part of the engine on the stand at the same moment
- **THEN** the part is in the shared inventory exactly once and missing from the engine for both players

### Requirement: Wheel balancing keeps its minigame, one player at a time
The wheel balancer minigame SHALL be played only by the player who balances the wheel. Other players SHALL see the wheel on the balancer and the balanced result, but not the minigame. While one player has the minigame open, the balancer SHALL be locked for everyone else: no other player SHALL be able to take the wheel off or start balancing until that player finishes, cancels, leaves the garage or disconnects.

#### Scenario: Wheel balanced by one player, taken by another
- **WHEN** player A balances the wheel on the wheel balancer through the minigame and player B takes it off
- **THEN** the wheel in the shared inventory is balanced for both players

#### Scenario: Balancer locked while the minigame is open
- **WHEN** player A has the balance minigame open and player B tries to take the wheel off the balancer or to start balancing
- **THEN** B is refused with an on-screen message, A's minigame stays open, and the wheel stays on the balancer for both players

#### Scenario: Two players open the minigame at once
- **WHEN** players A and B open the balance minigame for the same wheel at nearly the same time
- **THEN** exactly one of them keeps the minigame open, the other's window closes with a message, and only the holder's result is applied

#### Scenario: Holder leaves
- **WHEN** player A disconnects while the balance minigame is open
- **THEN** the balancer is free again for player B, and the wheel is still on the balancer, not balanced

### Requirement: Results of machine work on items
Work done by a machine, the repair table or the paint shop on an item SHALL change the item for every player: a repaired or painted part has the same condition, dent and paint for every player, and the result is kept when the item is later taken off or used by another player.

#### Scenario: Part repaired on the repair table
- **WHEN** player A repairs a part from the shared inventory on the repair table
- **THEN** player B's inventory shows the part with the new condition

#### Scenario: Part painted in the paint shop
- **WHEN** player A paints a part from the shared inventory in the paint shop
- **THEN** player B's inventory shows the part with the same colour and paint type

### Requirement: Shared tool positions
The position of each movable tool (welder, interior detailing kit, oil bin, engine crane, headlamp aligner, window tinting kit) SHALL be the same for every player, both when moved to a car place and when sent back to its default position.

#### Scenario: Welder moved to a car place
- **WHEN** player A moves the welder to car lifter 2
- **THEN** player B's welder stands at car lifter 2

### Requirement: Machine state for players who join later or return
A player who connects, or returns to the garage from another scene, while machines hold items, have settings or have been moved SHALL see the same machine contents, settings and tool positions as the players already in the garage, and the player's own save SHALL NOT add or remove anything on the machines or in the shared inventory.

#### Scenario: Late join with a loaded machine
- **WHEN** player A has a wheel on the tire changer, an engine with one part removed rotated on engine stand 1 and the welder at lifter 1, and player B connects
- **THEN** B sees the same wheel, engine, missing part, angle and welder position, and B's dump of machines and inventory equals A's

#### Scenario: Joining player's own save had something on a machine
- **WHEN** player B's own save has a battery on the battery charger and the session has an empty charger
- **THEN** after B joins, B's battery charger is empty and that battery is not added to the shared inventory

#### Scenario: Return from another scene
- **WHEN** player B is away from the garage while player A puts a wheel on the tire changer, and B then returns
- **THEN** B sees the wheel on the tire changer and B's inventory equals A's

### Requirement: Machine state survives a disconnect and a server restart
Machine contents, settings and tool positions SHALL stay on the server when the player who changed them disconnects, and the server SHALL keep them as part of the session state that it saves and loads.

#### Scenario: Player leaves with an item on a machine
- **WHEN** player A puts a brake disc on the brake lathe and disconnects
- **THEN** player B still sees the brake disc on the lathe and can take it off once

#### Scenario: Restart with an engine on the stand
- **WHEN** an engine is on engine stand 1 and the server is restarted with the saved session
- **THEN** players who connect see the engine on engine stand 1 again
