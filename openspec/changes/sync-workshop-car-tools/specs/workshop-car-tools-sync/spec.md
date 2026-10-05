# Spec Delta

## Purpose

Keeps the effect of workshop tools that act on a car the same for every player: the car state they change
(through part and detail sync) and the visible effect, without a second cost or a locked player.

## ADDED Requirements

### Requirement: Results of tool work on cars
Using the welder, car wash, interior detailing kit, oil bin, engine crane, paint shop or dyno on a car SHALL leave the car in the same state for every player, through the same car state that part and detail sync use.

#### Scenario: Car washed
- **WHEN** player A washes a car in the car wash
- **THEN** player B sees the car clean after A's wash finishes

#### Scenario: Engine removed with the crane
- **WHEN** player A removes a car's engine with the engine crane
- **THEN** player B sees the car without its engine and the engine group is in the shared inventory once

#### Scenario: Engine swapped with the crane
- **WHEN** player A inserts a different engine into a car with the engine crane
- **THEN** player B, and a player who joins afterwards, see the new engine in that car

#### Scenario: Dyno run
- **WHEN** player A measures a car on the dyno
- **THEN** player B's car shows the same measured result, without a dyno run on B's client

### Requirement: Visible tool actions
Other players SHALL see a tool working when it acts on a car or item (sparks, wash or paint effect, sound) at the right place. Seeing the effect SHALL NOT lock the other player's controls, cost money or give experience a second time, or change state a second time.

#### Scenario: Welding seen by another player
- **WHEN** player A welds a car on lifter 1
- **THEN** player B sees the welding effect at that car, can keep moving and using other tools, and the shared money changes only once
