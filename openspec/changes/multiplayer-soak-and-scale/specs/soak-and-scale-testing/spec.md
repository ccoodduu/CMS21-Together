# Spec Delta

## Purpose

Proves on the test PC that a session holds up with four players for hours: four game instances in one session
without disturbing the other test lanes or exhausting memory, long seeded sessions whose clients are checked for
equal state, a late join into a full garage and parking within a time budget, and crashes, rejoins and server
restarts without loss.

## ADDED Requirements

### Requirement: Four-instance test lane
The test environment SHALL provide a lane that runs four game instances against one dedicated server of its own, and
SHALL never run it at the same time as a lane that uses any of those instances; lanes SHALL wait for each other
instead of sharing an instance, and deploying a build SHALL never overwrite a lane that is running.

#### Scenario: Lanes wait for each other
- **WHEN** a four-instance run is started while a two-instance lane runs
- **THEN** it waits until that lane has finished and then starts, and a two-instance run started meanwhile waits for the four-instance run

#### Scenario: Deploy refused while busy
- **WHEN** a build is deployed to a lane whose instances are in use by a running test
- **THEN** the deploy stops with a message that the lane is busy and copies nothing

#### Scenario: Four players in one session
- **WHEN** four instances connect to the lane's server
- **THEN** each sees the other three with their names and all four agree on every shared state section

### Requirement: Memory protection
A test run SHALL start its games only when the PC has enough commit headroom and free memory for them, and a long
run SHALL stop cleanly, with a recorded reason, before commit headroom or free memory fall below a safety margin.

#### Scenario: Memory runs low during a soak
- **WHEN** commit headroom falls below the margin during a long session
- **THEN** the session stops issuing actions, records that it was aborted for memory with the last samples, and the lane shuts down normally

### Requirement: Seeded soak session
The soak test SHALL run a random mix of real player actions from a seed for a given duration, log every action so
the same sequence can be replayed, and at regular checkpoints quiesce the session and require that every client's
shared state is equal and matches the server's.

#### Scenario: Clean soak
- **WHEN** a soak runs for its duration with no sync bug
- **THEN** every checkpoint finds all clients equal and matching the server, no desync is confirmed, no client leaves unexpectedly, and the run passes

#### Scenario: Drift found
- **WHEN** one client's shared state differs from the others at a checkpoint and stays different
- **THEN** the run fails, keeps the dumps and the diff, names the seed and the action log, and continues to the end to count further failures

#### Scenario: Replay
- **WHEN** a soak is started with a previous run's action log
- **THEN** it issues the same actions, by the same clients, with the same arguments, in the same order

### Requirement: Performance report
Every soak, late-join and storm run SHALL produce a summary with bandwidth per client and per message kind, server and
game CPU and memory over time with their growth per hour, log growth per hour, join times, checkpoint and storm
results, and a verdict per budget.

#### Scenario: Budget exceeded
- **WHEN** a client's average download exceeds its budget
- **THEN** the summary marks that budget as a warning with the measured value, and the run's pass or fail is decided by the failure rules only

#### Scenario: Leak over hours
- **WHEN** the server's private memory at the end of a run of an hour or more is more than one and a half times its value at minute ten
- **THEN** the run fails and the summary shows the memory curve

### Requirement: Late join into a full garage
The test environment SHALL measure a join into a session whose garage car places and unlocked parking slots are all
occupied by worked-on cars, with a full inventory and machines in use: the time until the joining client has
acknowledged its snapshot, the snapshot size per section, and the effect on the players already in the session.

#### Scenario: Join within the limit
- **WHEN** a client joins the full session while the other players keep working
- **THEN** it acknowledges its snapshot within 120 seconds without a sync timeout, all clients agree afterwards, and the time, size and lock wait are recorded

### Requirement: Disconnect storms without loss
The test environment SHALL crash, disconnect, stall and reconnect clients, alone and several at once, and restart the
server gracefully and by force in the middle of a session, and SHALL verify that no shared state is lost, that every
returning player keeps their identity and garage position, and that claims and locks held by a crashed player are
released.

#### Scenario: Client crash and rejoin
- **WHEN** a player's game is killed while others work and the player starts the game again and rejoins
- **THEN** the server frees the slot, the player returns with the same identity and position, and all clients agree

#### Scenario: Several clients at once
- **WHEN** three players drop within a second and all reconnect at the same moment while the fourth keeps working
- **THEN** each gets a complete snapshot, the fourth player's changes reach everyone, and all clients agree

#### Scenario: Crash while holding a claim
- **WHEN** a player is killed while holding a part claim or the wheel balancer
- **THEN** the claim or lock is released within 15 seconds and another player can use it

#### Scenario: Graceful server restart
- **WHEN** the server is stopped by its stop command during a session and started again
- **THEN** every client is told the server shut down, and after reconnecting the shared state equals the state before the stop

#### Scenario: Server killed
- **WHEN** the server process is killed during a session and started again
- **THEN** it loads its newest save without falling back to a backup, all clients agree after reconnecting, and nothing older than the last save is lost
