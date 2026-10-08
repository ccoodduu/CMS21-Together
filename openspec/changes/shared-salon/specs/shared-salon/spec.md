# Spec Delta

## Purpose

Makes the car salon one shared place and proves that a car bought there, with its chosen version and configuration,
reaches every player.

## ADDED Requirements

### Requirement: One salon for everyone in it
Players who are in the car salon at the same time SHALL see the same car models in the same places with the same
colours. A player who arrives later SHALL see the same cars as the players already there. The next visit after
everyone has left MAY show other cars.

#### Scenario: Two players in the salon
- **WHEN** players A and B travel to the car salon one after the other
- **THEN** both see the same models and colours on the same stands

### Requirement: Salon purchases with version and configuration
A car bought in the salon SHALL be paid once from the shared money and SHALL arrive in the shared parking with the
version, rims, tyres and paint the buyer chose, for every player. A purchase the server refuses SHALL be answered with
the reason and change nothing.

#### Scenario: Buying a configured car
- **WHEN** player A buys a model in its second version with other rims
- **THEN** the shared money drops by the price once and every player finds that car, in that version with those rims,
  in the parking

#### Scenario: Not enough money
- **WHEN** player A tries to buy a car that costs more than the shared money
- **THEN** A is told why, and the money and the parking are unchanged for everyone

### Requirement: The main menu showroom is not part of a session
The game's showroom, the car viewer reached from the main menu, SHALL be described as single-player only; a connected
player has no way to reach it, and the multiplayer guard SHALL name it as main-menu only rather than as a feature still
to come.

#### Scenario: Guard list
- **WHEN** a player reads the multiplayer guard's list of blocked features
- **THEN** the showroom is listed as main menu only
