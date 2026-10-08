# Spec Delta

## Purpose

Proves that a car bought in the car salon, with its chosen version and configuration, reaches every player once and is
paid once, and keeps the guard's description of the salon and the main menu's showroom honest.

## ADDED Requirements

### Requirement: Salon purchases with version and configuration
A car bought in the salon SHALL be paid once from the shared money and SHALL arrive once in the shared parking with the
version, rims, tyres and paint the buyer chose, for every player.

#### Scenario: Buying a configured car
- **WHEN** player A buys a model in a non-default version with other rims
- **THEN** the shared money drops by the price once and every player finds that car once, in that version with those
  rims, in the parking

### Requirement: The server refuses a purchase without enough money
When the shared money on the server is below the price of a salon car, the server SHALL refuse the purchase even if the
buyer's game allowed it, SHALL tell the buyer why, and SHALL change neither the money nor the parking.

#### Scenario: Money spent a moment earlier
- **WHEN** the shared money drops below the price while player A's game still shows the old amount and A buys the car
- **THEN** A is told there is not enough shared money, and the money and the parking are unchanged for everyone

### Requirement: The guard describes the salon and the showroom correctly
The multiplayer guard SHALL list the car version choice as part of the car salon and allowed, and SHALL list the
game's showroom, the car viewer reached from the main menu, as main menu only rather than as a feature still to come.

#### Scenario: Guard list
- **WHEN** a player reads the multiplayer guard's list of features
- **THEN** the car version choice is allowed as part of the car salon and the showroom is listed as main menu only
