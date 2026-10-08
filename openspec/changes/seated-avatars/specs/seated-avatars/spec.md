# Spec Delta

## Purpose

Shows a player who sits in a car in the garage sitting in that car for every other player.

## ADDED Requirements

### Requirement: Seated players are shown in their seat
While a player sits in a car in the garage, every other player in the garage SHALL see that player's avatar in the same
seat (driver or passenger side) of that car, moving with the car, instead of a hidden avatar.

#### Scenario: Sitting down
- **WHEN** player A sits in the driver seat of the car on lifter 1
- **THEN** player B sees A's avatar in the driver seat of that car within half a second

#### Scenario: Lift moves with a seated player
- **WHEN** player A sits in a car and the lift under that car goes up
- **THEN** player B sees A's avatar stay in the seat while the car rises

#### Scenario: Two players in one car
- **WHEN** player A sits in the driver seat and player B in the passenger seat of the same car
- **THEN** A sees B in the passenger seat and B sees A in the driver seat

### Requirement: Leaving the seat
When a seated player leaves the seat, the other players SHALL see the avatar standing at the player's position again;
when the car is moved, parked or deleted, the avatar SHALL not stay where the car was.

#### Scenario: Standing up
- **WHEN** player A leaves the car
- **THEN** player B sees A's avatar standing next to the car where A stands

### Requirement: Seated players after a late join
A player who joins while another player sits in a car SHALL see that player seated once the car has loaded.

#### Scenario: Join while someone sits
- **WHEN** player B joins while player A sits in the passenger seat of a car
- **THEN** B sees A in the passenger seat once the car has loaded
