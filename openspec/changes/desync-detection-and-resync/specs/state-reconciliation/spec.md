# Spec Delta

## Purpose

Detects when a client's view of shared session state (stats, upgrades, inventory, cars, parking and lifts) has drifted
from the server's, repairs it from the server automatically or on the player's request, and records what differed so
the missed sync can be fixed.

## ADDED Requirements

### Requirement: Periodic comparison
While a client is in session and in the garage, the server SHALL compare digests of that client's shared state with
digests of its own state, covering every reconciled section and every loaded car at least every 30 seconds, using the
same canonical form on both sides so that equal state always gives equal digests.

#### Scenario: Everything in sync
- **WHEN** two clients play normally and nothing has drifted
- **THEN** every comparison matches and nothing is reloaded or logged as a desync

#### Scenario: Client away from the garage
- **WHEN** a client is in another scene or still loading its join snapshot
- **THEN** the server does not compare that client's garage state

### Requirement: No false alarms from changes in flight
A difference SHALL count as a desync only when it is seen in two consecutive comparisons while neither the server's
nor the client's digest of that section changed in between.

#### Scenario: Change in flight
- **WHEN** a player unmounts a part and a comparison runs before the server has applied it
- **THEN** no desync is reported, because the next comparison sees changed digests or a match

### Requirement: Automatic repair
On a confirmed desync, the server SHALL resend the affected section or car to that client from its own state, without
reloading the scene, and SHALL not touch other clients or other sections.

#### Scenario: Corrupted car part
- **WHEN** one client's copy of a car part differs from the server's and stays different
- **THEN** within about a minute the server resends that car to that client and its part matches the server again, while the other client is untouched

#### Scenario: Repair disabled
- **WHEN** the server is configured to detect only
- **THEN** it logs the desync and its diff but sends nothing

### Requirement: Desync log
For every confirmed desync the server SHALL write a record with time, player, section, item and the field-level
differences between the client's and the server's state, and SHALL log one summary line.

#### Scenario: Diff recorded
- **WHEN** a car's part condition differs
- **THEN** the record names the car, the part and both values

### Requirement: Repair that does not hold
When the same section or car desyncs again shortly after an automatic repair, the server SHALL stop repairing it
automatically for a while, log it as persistent, and tell the player that a manual resync is recommended.

#### Scenario: Persistent desync
- **WHEN** a car desyncs again within a minute of being repaired
- **THEN** no further automatic repair is sent for that car for five minutes, and the player sees a message suggesting the resync key

### Requirement: Manual resync
A connected player in the garage SHALL be able to press one key to reload the garage and receive the complete
current state from the server, as a late joiner would, with a cooldown against repeated presses.

#### Scenario: Player resyncs
- **WHEN** a player whose garage looks wrong presses the resync key
- **THEN** the garage reloads, the player's shared state equals the server's afterwards, other players stay in the session, and the server logs the manual resync

#### Scenario: Resync not possible
- **WHEN** the player presses the key outside the garage, during a join snapshot or within the cooldown
- **THEN** nothing reloads and the player is told why
