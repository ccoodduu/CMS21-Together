# Spec Delta

## Purpose

Part 2 of row 16. The server computes the money and XP that it can derive from its own state: job payout and XP, the
special case after a job, the sale price of a car, and the welder and interior detailing costs. It checks car
purchases against what it already knows. Every difference from the client's own computation is logged, and the
player is told what was actually paid.

## ADDED Requirements

### Requirement: Job payout and XP from the server
When a job ends and the payout mode is `server`, the server SHALL pay the money and XP its own check of the job gives
from its stored car state. That check uses the game's rules: repaired parts' prices with the fault difficulty, task
and job bonuses, Expert rules, bonus flags, and payment even when the job is not complete. The finisher's own numbers
SHALL only be compared and logged.

#### Scenario: Payout equals the game's check
- **WHEN** a job is checked on the finisher's client with the game's own check and on the server, at any point of the repair
- **THEN** both give the same money spent, bonuses, total payout, XP, completion and per-task results

#### Scenario: Another player repaired a part after the order tab opened
- **WHEN** player B repairs a job part after player A opened the order tab, and player A ends the job
- **THEN** the payout includes player B's repair

#### Scenario: Paid amount differs from the finisher's prediction
- **WHEN** the server pays a different amount than the finisher's game predicted
- **THEN** the finisher sees the paid amount, and money and XP equal the server's on every client

#### Scenario: Server state incomplete
- **WHEN** a job part has no record on the server
- **THEN** the finisher's value is paid within today's bounds and the server logs the part as a fallback

### Requirement: Special case after a job from the server
The server SHALL decide whether a finished job gives a special case: always for a story mission, and a one-in-four
chance with the shared luck skill. It SHALL add that case to the shared inventory, and no client SHALL add one itself.

#### Scenario: Mission end
- **WHEN** the players finish a story mission
- **THEN** exactly one special case appears in the shared inventory on every client

### Requirement: Car sale price from the server
When the price mode is `server`, selling a car (from the garage or the parking) SHALL pay the price the server
computes with the game's sale rules from its stored car state. The seller SHALL see the paid amount when it differs
from the price the sell window showed.

#### Scenario: Sale price equals the game's
- **WHEN** a car's sale price is computed by the game's own sell window rules on a client and by the server
- **THEN** both prices are equal, on Normal and on Expert

### Requirement: Car-dependent fees from the server
When the price mode is `server`, the welder and interior detailing costs SHALL be the server's values for the car's
model, and interior detailing SHALL stay free with the car wash upgrade.

#### Scenario: Welder on two models
- **WHEN** a player uses the welder on two cars of models with different value modifiers
- **THEN** each charge equals the game's own welder cost for that model

### Requirement: Purchases checked against the server's knowledge
A car bought in a shared junkyard or barn SHALL cost the price the instance's reference generation recorded, and an
auction win SHALL cost the server's last bid for that lot. A purchase with another price SHALL be refused, and the
buyer SHALL get the authoritative money back.

#### Scenario: Tampered purchase price
- **WHEN** a client reports a junkyard car purchase at a lower price than the instance recorded for that car
- **THEN** the purchase is refused, the car does not reach the parking, and the buyer's money equals the server's

#### Scenario: No reference recorded
- **WHEN** a purchase comes from a visit the server has no reference for
- **THEN** today's purchase bound applies and the server logs a fallback

### Requirement: Shadow mode and the ledger
For payout and prices, a `shadow` mode SHALL compute the server's value, pay the client's value and log every
difference. The `gamelogic` command SHALL show, per piece, how many values were computed, equal, different or fell
back, and the last differences with both values.

#### Scenario: Shadow mode during a playtest
- **WHEN** the payout mode is `shadow` and a job ends with a different client value
- **THEN** the client's value is paid, and `gamelogic` lists the job with both values
