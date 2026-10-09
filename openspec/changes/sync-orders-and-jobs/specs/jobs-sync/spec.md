# Spec Delta

## Purpose

Keeps customer orders and jobs identical for every player in a co-op session: one server-owned list of
open orders and active jobs, exclusive accept, shared payout and experience, and consistent state for
players who join late or reload the garage.

## ADDED Requirements

### Requirement: Server-owned order list
The server SHALL hold the authoritative list of open orders and active jobs and SHALL assign every order a job id that is unique within the session. Every connected player SHALL see the same open orders, in the same order, with the same car, tasks and parts.

#### Scenario: New order appears for everyone
- **WHEN** a new order is generated during a session with two connected players
- **THEN** both players' orders windows list that order with the same job id, car and tasks

#### Scenario: Job ids never collide
- **WHEN** orders are generated over a session, including after the generating player changes
- **THEN** no two orders or jobs in the session share a job id

### Requirement: Single order generator
At most one connected, fully synced player who is in the garage SHALL generate new orders at a time, using the game's own generation rules, and no other player SHALL add orders locally. The role SHALL stay with that player until they leave the session or the garage; then the server SHALL hand generation to another player in the garage, if any, and SHALL ignore orders still arriving from the previous generator.

#### Scenario: Only the generator produces orders
- **WHEN** two players are connected and the order timer elapses
- **THEN** exactly one new order is added to the shared list

#### Scenario: Generator leaves
- **WHEN** the player generating orders disconnects while another player stays connected
- **THEN** the remaining player becomes the generator and new orders keep appearing

#### Scenario: Generator leaves the garage
- **WHEN** the generating player travels away from the garage while another player stays in the garage
- **THEN** the player in the garage becomes the generator; if nobody is in the garage, no new orders appear until someone returns

#### Scenario: Open-order limit respected
- **WHEN** the shared list already holds the maximum number of open orders for the shared level and upgrades
- **THEN** no further order is generated until one is accepted, declined or expires

### Requirement: Exclusive accept
Accepting an order SHALL require the server's approval, and the server SHALL approve at most one accept per order. A player whose accept is refused SHALL see a message and SHALL NOT get a customer car for that order.

#### Scenario: Two players accept the same order
- **WHEN** two players accept the same open order at nearly the same time
- **THEN** exactly one of them takes the job, the other gets a refusal, and only one customer car spawns

#### Scenario: Accepted order leaves everyone's list
- **WHEN** a player's accept is approved
- **THEN** the order disappears from every player's orders window

### Requirement: Customer car spawn for an accepted job
Taking a job SHALL spawn the customer car for every player on the same car loader, marked as that job's customer car, and the job SHALL then be listed as active on every client with that car loader. While connected, a job's customer car SHALL NOT be movable to the parking lot, and the job SHALL return to the open list with its original remaining time if its car is lost.

#### Scenario: Other player sees the customer car
- **WHEN** player A takes a job
- **THEN** player B sees the same car on the same car loader, marked as a customer car for that job, and can open its order details

#### Scenario: Take cannot complete
- **WHEN** an approved take does not result in a customer car within the take timeout, the garage has no free place, or the taker disconnects first
- **THEN** the order returns to the open list for everyone and any car spawned for it is removed

#### Scenario: Customer car cannot be parked
- **WHEN** a player tries to move an active job's customer car to the parking lot while connected
- **THEN** the move is refused, and the job stays active for every player on the same car loader

#### Scenario: Job car lost
- **WHEN** an active job's customer car is removed by anything other than ending the job, for example a car dropped when the server loads its save
- **THEN** the job returns to the open list for everyone with its original remaining time, so it can be taken again

### Requirement: Decline
Declining an open order SHALL remove it for every player. Orders that the game marks as not deletable (story missions) SHALL NOT be declinable.

#### Scenario: Decline removes the order everywhere
- **WHEN** a player declines an open order
- **THEN** the order disappears from every player's orders window and cannot be accepted afterwards

### Requirement: Order expiry
The server SHALL count down each open order's remaining time and SHALL remove an expired order for every player. The countdown SHALL run only while at least one player is connected, and every client SHALL show the server's remaining time.

#### Scenario: Order expires for everyone
- **WHEN** an open order's remaining time reaches zero
- **THEN** the order disappears from every player's orders window at the same moment, within network latency

#### Scenario: Empty server pauses expiry
- **WHEN** all players disconnect and someone reconnects later
- **THEN** each open order has the same remaining time it had when the last player left

### Requirement: Shared task progress
Every player SHALL see the same progress for an active job: which tasks and parts are done, which faulty parts were found, and the money spent on parts for the job, regardless of which player did the work.

#### Scenario: Progress by another player
- **WHEN** player A examines and finds a faulty part of an active job's car
- **THEN** player B's order details for that job show the same part as found

### Requirement: Job completion
Ending a job SHALL be applied once by the server: the job's payout SHALL be added to the shared money, its experience to the shared experience and level (never also through the generic experience update), and the job and its customer car SHALL be removed for every player. Every connected player who worked on the job SHALL receive the game's Steam statistics and achievement progress for the finished job once (row 30, `shared-job-achievements`; user decision 2026-10-08, replacing "every player connected at that moment"). A second attempt to end the same job SHALL be refused without paying again.

#### Scenario: Job ends with shared payout
- **WHEN** a player ends an active job and the game's completion checks pass
- **THEN** every player's money increases by the same payout, every player's experience matches the server's, and the job and its car are gone for everyone

#### Scenario: Steam progress for everyone who worked on the job
- **WHEN** player A ends a job that player B worked on while B is connected
- **THEN** both A and B receive the game's Steam statistics and achievement progress for that job once

#### Scenario: Two players end the same job
- **WHEN** two players end the same job at nearly the same time
- **THEN** the payout and experience are applied once, and the second player's money and experience are corrected to the server's values

#### Scenario: Completion checks fail
- **WHEN** a player tries to end a job whose car fails the game's completion checks
- **THEN** the game shows its usual message and no payout, experience or removal happens for anyone

### Requirement: Story missions
Story missions SHALL be shared like orders: generated only by the order generator, shown to every player, not declinable, not expiring, and taken by at most one player. The story mission progress (missions finished, current mission done, mission in progress) SHALL be stored by the server and be the same for every player.

#### Scenario: Story mission finished
- **WHEN** a player ends an active story mission
- **THEN** every player's story mission progress matches the server's and the next story mission, when the game offers one, appears once for everyone

### Requirement: Tutorial disabled in multiplayer
While connected to a server, no player SHALL be able to start a tutorial, no tutorial mission SHALL be generated or accepted into the shared order list, and the tutorial SHALL NOT lock order slots of the multiplayer session.

#### Scenario: Tutorial cannot be started
- **WHEN** a connected player tries to run a tutorial from the pause menu's tutorials window
- **THEN** the tutorial does not start and the player sees a message that tutorials are not available in multiplayer

#### Scenario: No tutorial orders
- **WHEN** orders are generated during a session
- **THEN** none of them is a tutorial mission, and order slots are not locked for the tutorial

### Requirement: Jobs survive a player leaving
Active jobs SHALL belong to the session, not to the player who took them. Any player SHALL be able to continue and end a job after the player who took it has left.

#### Scenario: Taker leaves mid-job
- **WHEN** player A takes a job and disconnects, and player B stays
- **THEN** the job stays active with its car for player B, and player B can end it and receive the payout into the shared money

### Requirement: Late join and garage reload
A player who joins mid-session SHALL receive the current open orders with their remaining time, the active jobs with their progress, and the generator role if applicable, as part of the initial sync. A client SHALL show the server's orders and jobs after its garage reloads from the local profile, not the profile's saved jobs.

#### Scenario: Late joiner sees current orders and jobs
- **WHEN** player A has open orders and an active job, and player B connects afterwards
- **THEN** player B's orders window and active jobs match player A's, and the customer car on its car loader is marked for that job

#### Scenario: Return to the garage
- **WHEN** a player's garage reloads from the local profile, for example after returning from the test track
- **THEN** the player's orders and active jobs equal the server's, not the jobs saved in the local profile

### Requirement: Persistence of order and job state
Open orders with their remaining time, active jobs with their car loader and progress, the next job id and the story mission counters SHALL be part of the server's saved session state.

#### Scenario: Server restart
- **WHEN** the server saves, restarts and a player reconnects
- **THEN** the player sees the same open orders, remaining times and active jobs as before the restart

#### Scenario: Take in progress during a restart
- **WHEN** the server restarts while an order is claimed but its job has not started
- **THEN** after the restart the order is open again and can be accepted
