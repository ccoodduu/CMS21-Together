# Spec Delta

## Purpose

Lets players build a new engine on the engine stand together.

## ADDED Requirements

### Requirement: A new engine on the stand is shared
When a player builds a new engine on the empty engine stand, every player SHALL see the same engine on the stand, and
work on it SHALL be shared as for any engine on the stand.

#### Scenario: New engine built
- **WHEN** player A builds a new engine on the empty engine stand
- **THEN** player B sees the same engine block on the stand with no parts mounted

### Requirement: A build never destroys or duplicates an engine
Building a new engine SHALL be refused while an engine is on the stand, with a message. Two builds at the same moment
SHALL leave one engine on the stand and SHALL NOT add the other build's engine to the shared inventory.

#### Scenario: Stand occupied
- **WHEN** an engine is on the stand and player A tries to build a new one
- **THEN** A is told to take the engine off first and the engine stays on the stand for everyone

#### Scenario: Two builds at once
- **WHEN** players A and B build a new engine on the empty stand at the same moment
- **THEN** one engine is on the stand for both, the other player is told the stand is taken, and the shared inventory
  has no extra engine
