# Spec Delta

## Purpose

Defines how players move between game scenes while connected: which scene each player is in, who is visible
where, what is and is not shared outside the garage, and how parts and cars obtained outside the garage reach
the shared inventory and parking.

## ADDED Requirements

### Requirement: Scene tracking
Every client SHALL report its scene to the server: `Loading` as soon as travel to another scene starts, and the
scene type (garage, parking, junkyard, barn, auction, dealer (`Salon`), showroom, each track type, photo location) once
that scene is playable. The server SHALL store the latest scene per player and every client SHALL know the
scene of every connected player.

#### Scenario: Travel to the junkyard
- **WHEN** player A travels from the garage to the junkyard
- **THEN** player B's roster shows A as `Loading` within 1 second of the travel starting and as `Junkyard` once A's junkyard is playable

#### Scenario: Late joiner learns scenes
- **WHEN** player B connects while player A is in the auction
- **THEN** B's roster shows A in `Auction` after B's initial sync

### Requirement: Visibility per scene
A client SHALL show a remote player's avatar only while both players are in the same scene and that scene is
one where avatars are shown: garage, parking, junkyard, auction, dealer, showroom, photo location and every track
type. In barn, tutorial, `Loading`, menu and unknown scenes no remote avatars SHALL be shown.

#### Scenario: Different scenes
- **WHEN** player A is in the junkyard and player B is in the garage
- **THEN** B shows no avatar for A and A shows no avatar for B

#### Scenario: Same non-garage scene
- **WHEN** players A and B are both in the junkyard
- **THEN** each sees the other's avatar and name tag moving in the junkyard

#### Scenario: Barn
- **WHEN** players A and B are both in the barn
- **THEN** neither shows an avatar for the other

### Requirement: Scenes outside the garage are not shared
Content of scenes other than the garage (cars and parts for sale, junk piles, barn finds, auction lots, dealer
stock, track runs) SHALL stay generated and owned by each client. Actions there SHALL NOT change another
player's copy of that scene; only money, exp, inventory and parking changes that result from them are shared.

#### Scenario: Same junkyard, different cars
- **WHEN** players A and B are in the junkyard at the same time
- **THEN** each may see a different set of cars and parts, and a car A buys there is still offered in B's junkyard

### Requirement: Garage updates while away
While a client is not in the garage it SHALL NOT apply garage-bound updates (cars, parts, lifts, tools, garage
upgrades) to the game, and when it returns to the garage it SHALL receive and apply the full current garage
state, exactly as a client joining mid-session, instead of the garage from its local save.

#### Scenario: Car changed while away
- **WHEN** player A is in the junkyard and player B removes a door from a car in the garage, then A returns
- **THEN** A's garage shows the door removed and A's car data matches B's

#### Scenario: Update arrives during travel
- **WHEN** a garage-bound update for player A arrives while A's scene is `Loading`
- **THEN** A does not apply it and A's game raises no error, and the state after A's return includes the change

### Requirement: Results sent when leaving a scene precede the return snapshot
When travel away from a scene starts, the client SHALL give other modules a chance to send state produced in that
scene (for example test-drive results) while the scene is still loaded, before it reports `Loading`. Everything
sent then SHALL be applied by the server before it builds that client's next garage snapshot.

#### Scenario: Update sent on leaving survives the return
- **WHEN** a module sends a garage-bound update from the leaving-scene event while player A travels from the test track back to the garage
- **THEN** the garage snapshot A receives on arrival already contains that update, and A's garage shows it

### Requirement: Garage-bound activity ends when leaving
When a player starts travelling away from the garage, the server SHALL end that player's seat and engine state
and notify the other server modules, so claims a player holds in the garage are released before the next player
needs them.

#### Scenario: Travel while seated with the engine running
- **WHEN** player A sits in a car with the engine running and starts travelling to the test track
- **THEN** player B sees the seat and engine state of A end within 1 second

### Requirement: Parts bought outside the garage
Parts taken from the junkyard or barn and paid for in the taken-items window SHALL be priced and paid by the
server from the shared money and added to the shared inventory once, visible to every player.

#### Scenario: Junkyard parts
- **WHEN** player A buys 3 parts in the junkyard while player B is in the garage
- **THEN** shared money drops by the server-computed price once, B's inventory contains the 3 parts, and A's inventory contains them once after returning to the garage

#### Scenario: Not enough shared money
- **WHEN** the server-computed price is higher than the shared money
- **THEN** no money is taken and no part is added for anyone

### Requirement: Cars bought outside the garage
A car bought in the junkyard, barn, auction or dealer SHALL be one server-validated purchase: the server checks
shared money and free shared parking space, takes the money once, stores the car in the shared parking and
tells all players; otherwise it rejects the purchase and the buyer gets an on-screen message.

#### Scenario: Barn car bought
- **WHEN** player A buys a car in the barn for 5000 while player B is in the garage
- **THEN** shared money drops by exactly 5000 on both clients and the car is in the shared parking for both players

#### Scenario: Money spent by another player first
- **WHEN** player A buys a car for 5000 while player B's purchase has just left the shared money below 5000
- **THEN** A's purchase is rejected, A sees a message, shared money is unchanged by A, and the car is not in the parking when A returns

#### Scenario: Parking full
- **WHEN** a player buys a car while the shared parking has no free space
- **THEN** the purchase is rejected and no money is taken

### Requirement: Travel to the menu leaves the session
Travelling to the main menu SHALL disconnect the player from the session; it SHALL NOT be reported as a scene.

#### Scenario: Back to menu from the auction
- **WHEN** player A opens the pause menu in the auction and quits to the main menu
- **THEN** A disconnects and is removed from every other player's roster
