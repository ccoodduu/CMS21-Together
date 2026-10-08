# Spec Delta

## Purpose

Makes tuning at the dyno (gearbox, ECU, carburettor) shared, with one tuner per car.

## ADDED Requirements

### Requirement: Tuning values reach every player
When a player applies gearbox, ECU or carburettor tuning to a car, every player SHALL see the same tuning values on that
car within a few seconds, and so SHALL a player who joins later or after a server restart.

#### Scenario: Gearbox tuned
- **WHEN** player A applies new gear ratios to a car's racing gearbox at the dyno
- **THEN** player B's copy of the car has the same gear ratios and final drive

#### Scenario: Late join after tuning
- **WHEN** player B joins after A tuned a car's ECU
- **THEN** B's copy of the car has A's ECU map

### Requirement: One tuner per car
While a player has the tuning window open for a car, another player SHALL NOT be able to open it for the same car or
take off the parts being tuned, and SHALL be told who is tuning it.

#### Scenario: Second player tries to tune
- **WHEN** player A has the tuning window open for a car and player B tries to open it for the same car
- **THEN** B is told that A is tuning this car and nothing changes
