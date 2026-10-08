# Spec Delta

## Purpose

Makes bonus (visual tuning) parts on cars the same for every player.

## ADDED Requirements

### Requirement: Bonus parts on cars are shared
Fitting, removing or painting a bonus part on a car SHALL show the same result to every player, to a player who joins
later and after a server restart. The part SHALL move between the shared inventory and the car exactly once.

#### Scenario: Spoiler fitted
- **WHEN** player A fits a spoiler from the inventory to a car
- **THEN** player B sees the spoiler on the car and the spoiler is no longer in the shared inventory

#### Scenario: Spoiler removed
- **WHEN** player A removes the spoiler
- **THEN** B sees the slot empty and the spoiler is in the shared inventory once

#### Scenario: Painted with the car
- **WHEN** player A paints the car in the paint shop
- **THEN** B sees the bonus parts in the same paint

### Requirement: One player per bonus slot
Two players SHALL NOT change the same bonus slot at the same time; the player who is refused SHALL be told why and keep
their item.

#### Scenario: Both click the same slot
- **WHEN** players A and B try to fit a part to the same slot at the same moment
- **THEN** one part is fitted, the other player is told who is working there and still has their item
