# Spec Delta

## Purpose

Defines how the junkyard, the barn and the auction become one shared place for every player in them: one instance
per scene, decided by the server, entered, left and rejoined without losing what happened there, with the same cars
and the same barn layout for everyone, and a car selection on the server that does not depend on third-party code.

## ADDED Requirements

### Requirement: One shared instance per outdoor scene
The server SHALL keep at most one open instance of each shared outdoor scene (junkyard, barn, auction). A player who
travels to that scene SHALL join the open instance, or open a new one when none is open. An instance SHALL close when
its last player has left it by travel; when its last player disconnected, it SHALL stay open for the configured grace
period first. Only the scenes listed in the server configuration SHALL be shared; every other scene SHALL behave as
before this change.

#### Scenario: Second player joins the open junkyard
- **WHEN** player A is in the junkyard and player B travels to the junkyard
- **THEN** B's junkyard is A's instance (same instance id in both dumps)

#### Scenario: New yard after everyone left
- **WHEN** players A and B leave the junkyard and A travels there again
- **THEN** A gets a new instance with a new seed

#### Scenario: Crash inside the grace period
- **WHEN** player A is alone in the junkyard, disconnects, reconnects and travels back to the junkyard within the grace period
- **THEN** A is in the same instance as before

#### Scenario: Scene not shared by configuration
- **WHEN** the server configuration does not list the barn and players A and B are in a barn at the same time
- **THEN** each generates its own barn, neither shows the other's avatar, and no barn instance exists on the server

### Requirement: The server decides an instance's content
The server SHALL decide the cars of an instance (model and configuration per position in the creation order) and a
visit seed before any client builds the scene, and every client SHALL build exactly those cars. The cars SHALL be
chosen only from cars every connected player can load for that scene, as reported by the clients.

#### Scenario: Same cars for both players
- **WHEN** players A and B are in the same junkyard instance
- **THEN** both list the same car models and configurations at the same positions

#### Scenario: Car only one player can load
- **WHEN** player A has a DLC car that player B does not have, and a new junkyard instance opens while both are connected
- **THEN** that car is not among the instance's cars

#### Scenario: Client waits for its instance
- **WHEN** player B's game starts generating the junkyard before the server's instance has arrived
- **THEN** B's generation starts only after the instance has arrived, and B's cars are the instance's cars

#### Scenario: Instance does not arrive
- **WHEN** a client gets no instance within 15 seconds of starting its travel
- **THEN** it generates the scene locally, shows that this visit is not shared, logs one warning, and shows no other player's avatar there

### Requirement: Same appearance and barn layout
Every client in an instance SHALL generate the instance's cars with the same appearance (colour, missing panels,
condition, price modifier) and, in a barn, the same layout, from the instance's seed. Each client SHALL report a
digest of what it generated and the server SHALL log every difference from the first player's digest.

#### Scenario: Equal digests
- **WHEN** players A and B are in the same junkyard instance with the same game data
- **THEN** their digests are equal and the server logs no difference

#### Scenario: Same barn
- **WHEN** players A and B are in the same barn instance
- **THEN** both have the same barn layout digest and the same three cars

#### Scenario: Difference is visible
- **WHEN** a client's generated car differs from the first player's
- **THEN** the server logs which car and which value differ and its `outdoor` command counts the difference

### Requirement: Replaceable car selection
Car selection on the server SHALL go through one selection interface chosen by server configuration. The default
selector SHALL be this project's own code: unique models per visit, drawn uniformly from the allowed cars, avoiding
the models of the previous visit of that scene while enough other models remain. Its history SHALL be saved with the
session. A selector adapted from LvxBetterCarSpawns SHALL NOT be included unless its author's permission has been
recorded.

#### Scenario: No model twice in one visit
- **WHEN** the default selector picks the cars of a junkyard instance from at least as many models as cars
- **THEN** no model appears twice

#### Scenario: Not the same cars as last time
- **WHEN** the junkyard is visited twice in a row and the allowed models are at least twice the car count
- **THEN** no model of the first visit is in the second

#### Scenario: Unknown selector name
- **WHEN** the server configuration names a selector that does not exist
- **THEN** the server logs a warning and uses the default selector

#### Scenario: History survives a restart
- **WHEN** the server is restarted between two junkyard visits
- **THEN** the second visit still avoids the first visit's models

### Requirement: Players see each other in shared outdoor scenes
In a shared instance, including the barn, a client SHALL show the avatars of the other players in the same instance,
and SHALL NOT show the avatar of a player whose visit is local.

#### Scenario: Barn avatars
- **WHEN** players A and B are in the same shared barn instance
- **THEN** each sees the other's avatar and name tag

#### Scenario: Local visit stays hidden
- **WHEN** player A's barn visit fell back to local generation and player B is in the shared barn
- **THEN** neither shows the other's avatar

### Requirement: Arriving, leaving and returning
A player arriving in an open instance SHALL get its current state: the same cars minus the cars sold, the piles minus
the items taken or bought, and the auction lots with their state. A player who leaves and comes back while others stay
SHALL find the same instance. Leaving or disconnecting SHALL release everything the player held there.

#### Scenario: Late arrival after a sale and a take
- **WHEN** player A bought car 2 and took an item from a pile in the junkyard, and then player B arrives
- **THEN** B's junkyard has no car 2 and B's pile does not contain the item

#### Scenario: Leave and come back
- **WHEN** player B stays in the junkyard while player A travels to the garage and back
- **THEN** A is in the same instance and sees B's changes made meanwhile

### Requirement: Barn trips use one barn per opened barn
Opening a new barn instance SHALL use one barn from the shared barn count; joining an open barn instance SHALL NOT use
one. The travel fee SHALL follow the server's travel fee rule for every trip.

#### Scenario: Two players, one barn
- **WHEN** the shared barn count is 2, player A travels to a barn and then player B travels to the barn while A is still there
- **THEN** the shared barn count is 1 and both are in the same barn instance
