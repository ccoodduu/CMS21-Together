# Spec Delta

## Purpose

The server generates customer orders and story missions itself, with the game's own rules and timing, instead of
an elected client. It gives each order the seed its car is prepared with, so the order decides the car's damage and
colour, not the player who takes it.

## ADDED Requirements

### Requirement: The server generates orders
While the session's order source is the server, new orders SHALL be generated only by the server, with the game's
rules: the first order 10 seconds after the session starts, then one every 30 seconds while the open orders are below
the game's limit for the shared player level. Accepting an order and ending a job SHALL restart the countdown as in the
game. No client SHALL generate an order.

#### Scenario: Orders arrive with nobody generating
- **WHEN** two players are in the garage of a new session
- **THEN** the first order appears for both about 10 seconds after the start, then one about every 30 seconds until the limit for the level is reached, and the server log shows no generator election

#### Scenario: Limit follows the shared level
- **WHEN** the displayed player level rises from 4 to 5 while 3 orders are open
- **THEN** a fourth order appears within about 30 seconds and no fifth one

#### Scenario: Nobody in the garage
- **WHEN** every connected player is in the junkyard
- **THEN** no new order is generated, open orders keep expiring, and generation resumes when a player is back in the garage

### Requirement: Orders equal the game's own
For the same random seed and the same inputs (shared level and experience, difficulty, open orders, car pool), the
server's order SHALL equal the order the game's own generator makes. The car pool SHALL contain only cars every
connected player can load, within the DLC set they all own.

#### Scenario: Server order against the native generator
- **WHEN** a client runs the game's own order generation under a seed and the server generates an order with the same seed and inputs
- **THEN** both orders have the same car, configuration, colour, tasks, easy flags, condition, mileage, bonus and time limit

#### Scenario: Car another player cannot load
- **WHEN** one connected player lacks a car configuration that the other has
- **THEN** no order is generated for that configuration

### Requirement: Story missions from the server
The server SHALL offer the next story mission when the previous one is done and the shared level allows it, from the
exported mission data, without a time limit. Tutorial missions SHALL never be offered.

#### Scenario: Next mission after a finished one
- **WHEN** the players finish the current story mission and the shared level allows the next one
- **THEN** the server adds the next mission to the orders for every player

### Requirement: The order decides its car
Every order SHALL carry a preparation seed chosen by the server, and taking it SHALL prepare the car with the game's
own preparation code on that seed. Taking the same order again SHALL produce the same car.

#### Scenario: Re-taking the same order
- **WHEN** player A takes an order and player B later takes an order with the same content and seed on the same car place
- **THEN** both cars have the same part conditions, faults, fluids, colour, panels, livery and dents

#### Scenario: Late joiner sees the prepared car
- **WHEN** a player joins while a job car prepared from a seed is in the garage
- **THEN** that player's copy of the car equals the server's

### Requirement: Fallback to the elected generator
When the server cannot generate orders (tables missing or from another game version, or the order source set to
`client`), the elected-client generator SHALL keep working as before, and its orders SHALL still get a server-chosen
preparation seed.

#### Scenario: Missing car table
- **WHEN** the server runs without the car table
- **THEN** one garage client is elected as before, its orders reach every player, and `gamelogic` shows the order fallback and why

### Requirement: Order clock survives a restart
The order countdown SHALL be saved with the orders, and a session restored from a save SHALL continue it.

#### Scenario: Restart mid-countdown
- **WHEN** the server is restarted 20 seconds into a 30-second countdown and the players rejoin
- **THEN** the next order follows the saved countdown, not a new 10-second start
