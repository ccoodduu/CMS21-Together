# Spec Delta

## Purpose

Makes sure that a difference between a player's game and the server is found and repaired for every kind of shared
state, also while players are busy, and that the long-running test session makes players act on the same things at
the same time and fails when an item is duplicated or lost.

## ADDED Requirements

### Requirement: Every kind of shared state is compared
The server SHALL compare each player's car details (per car), workshop machines, warehouse, skills and garage
upgrades, and orders and active jobs with its own state, in addition to money, inventory, car parts and car
placement, and SHALL repair a confirmed difference by sending its state of that kind to that player. A newly compared
kind MAY only log a confirmed difference until the desync test session has shown no false alarm for it.

#### Scenario: Car details differ
- **WHEN** player B's coolant level of a car differs from the server's without any change being sent
- **THEN** within a few comparison rounds the server logs the difference, sends its car details to player B, and player B's coolant level equals the server's

#### Scenario: A machine differs
- **WHEN** player B's tire changer holds a wheel the server does not have on it
- **THEN** the server logs the difference and player B's tire changer is set to the server's state

#### Scenario: Normal play raises no alarm
- **WHEN** two players play normally for the length of the desync test session
- **THEN** no difference is reported for any kind of state

### Requirement: A busy player does not hide a difference
A comparison the player cannot answer at that moment (the state is being changed) SHALL neither confirm nor clear a
difference found before it. A difference that is not confirmed within four comparisons of that kind of state SHALL be
dropped. When a player cannot answer for the same kind of state for longer than the server's stall time
(`desync_stall_seconds`, 120 s by default) in a row, the server SHALL log it once and list it in the bug report.

#### Scenario: Difference confirmed across a busy round
- **WHEN** player B's inventory differs from the server's and one comparison round finds player B's inventory busy
- **THEN** the difference is still confirmed and repaired within three rounds

#### Scenario: Stuck "busy" answer
- **WHEN** one kind of a player's state (a car's parts or details, the inventory, or any other kind) cannot be compared for longer than the stall time in a row
- **THEN** the server logs one warning naming the player and that kind of state, the bug report lists it, and no new warning follows once the state can be compared again

### Requirement: A forced check covers everything
A forced comparison round started from the server console SHALL compare every loaded car and every kind of state for
every player in the garage.

#### Scenario: Forced check with four cars
- **WHEN** four cars are loaded and the admin runs the forced check
- **THEN** the server reports a result for each of the four cars' parts and details and for every other kind of state, for every player in the garage

### Requirement: The test session makes players contend
The long-running test session SHALL be able to make two to four players act on the same part, item, car detail,
machine, lift or car at the same moment, in a seeded order in which the server receives their actions and which a
replay repeats exactly, and SHALL fail when an item
is duplicated or lost, when a player's state is not comparable at two checkpoints in a row, or when the outcome of a
contention differs from the expected one. Contentions whose fix has not landed yet SHALL be reported as known gaps
instead of failures.

#### Scenario: Machine against mount in the test session
- **WHEN** the test session makes one player put a wheel on the tire changer while another mounts it
- **THEN** the session checks that the wheel exists in exactly one place and fails if it exists twice

#### Scenario: Replay of a contention
- **WHEN** a failed test session is replayed from its action log
- **THEN** the same players act on the same targets, and the server receives their actions in the same order as in the failed session

#### Scenario: Known gap
- **WHEN** the test session runs a contention whose fix is listed as not yet done
- **THEN** a wrong outcome is reported as a known gap and the session does not fail on it
