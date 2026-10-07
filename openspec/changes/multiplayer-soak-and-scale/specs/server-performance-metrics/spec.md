# Spec Delta

## Purpose

Lets the host of a session and the test harness see what the dedicated server costs: how many bytes it sends and
receives per player and per kind of message, how long its handlers and its shared-state lock take, how much CPU and
memory it uses, and how long a joining player's snapshot takes.

## ADDED Requirements

### Requirement: Traffic accounting
The server SHALL count, for every kind of network message, the number of messages and bytes it sends and receives,
per transport and per connected player, for the whole time it runs, without measurably slowing message handling.

#### Scenario: Messages counted
- **WHEN** a player triggers ten stat changes
- **THEN** the server's traffic summary shows ten more received messages of that kind from that player and the bytes of the matching messages sent to the other players

#### Scenario: Refused connection counted
- **WHEN** the server refuses a connection before it gets a slot
- **THEN** the refusal message is counted as sent traffic

### Requirement: Handler and lock timing
The server SHALL record, per kind of message and for its main-loop tick, its save and its join snapshot, how long the
work waited for the shared-state lock and how long it ran inside it, as count, total, maximum and 99th percentile.

#### Scenario: Slow snapshot visible
- **WHEN** a player joins a session with a full garage
- **THEN** the snapshot's build time appears in the timings and the lock wait of other players' messages during the join is recorded

### Requirement: Periodic performance log
When a performance log interval greater than zero is configured, the server SHALL write one machine-readable line per
interval with its CPU use, memory, thread and handle counts, players by join state, last save size and duration, log
size, desync record count, and the traffic and timing changes since the previous line. With the interval at zero (the
default) it SHALL write no performance file.

#### Scenario: Logging enabled
- **WHEN** the interval is set to 10 seconds and the server runs for one minute
- **THEN** about six lines are written, each with a CPU value and the traffic of the message kinds that moved

#### Scenario: Default configuration
- **WHEN** the configuration has no performance log entry
- **THEN** the entry is added with value 0 and no performance file is written

### Requirement: Performance command
The server SHALL offer a console command that shows a summary since the last reset (uptime, CPU, memory, bytes per
second per player in each direction, the message kinds with the most bytes, the slowest handler and the longest lock
wait), a top-N list, and a reset.

#### Scenario: Host checks the load
- **WHEN** the host runs the command during a session with three players
- **THEN** the console shows each player's current download and upload rate and the message kinds that use the most bytes

### Requirement: Join snapshot measurement
For every join snapshot the server SHALL log, when the client acknowledges it, the time from the request to the
acknowledgement in milliseconds, the snapshot's bytes, its build time and its item count per section.

#### Scenario: Late join logged
- **WHEN** a client joins and acknowledges its snapshot
- **THEN** one log line names the client, the snapshot, the milliseconds until the acknowledgement, the bytes, the build time and the counts per section
