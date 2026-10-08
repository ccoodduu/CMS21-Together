# Spec Delta

## Purpose

Gives every player who worked on a job the game's Steam statistics and achievement progress for finishing it.

## ADDED Requirements

### Requirement: Job statistics for contributors
When a job ends, every connected player who worked on it (took it, changed its car's parts or details, held a lock on its
car, examined it, or finished it) SHALL receive the game's Steam statistics and achievement progress for that job
exactly once. The player who finished the job SHALL NOT receive them a second time.

#### Scenario: Two players repair one car
- **WHEN** player A takes a job, player B replaces a part on its car, and A finishes the completed job
- **THEN** both A and B have their finished-order statistic increased by one, and A's only once

#### Scenario: Player who did not touch the car
- **WHEN** player A finishes a job alone while player B is in the garage
- **THEN** B's statistics do not change

#### Scenario: Bonus orders
- **WHEN** a completed job with a money bonus ends and B worked on it
- **THEN** B also receives the money-bonus statistic

### Requirement: Rule chosen by the host
The server SHALL let the host choose who receives job statistics: the contributors (default), every player in the garage
at the end, or only the finisher.

#### Scenario: Everyone in the garage
- **WHEN** the server is set to award everyone in the garage and A finishes a job alone
- **THEN** B, who is in the garage, also receives the job's statistics

### Requirement: No award from a forged or repeated report
The server SHALL award a job's statistics only once, only after that job ended, only for the job statistics of the game,
and only on the report of the player who ended it.

#### Scenario: Report repeated
- **WHEN** the finishing client sends its report a second time
- **THEN** nobody receives the statistics again
