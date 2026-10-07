# Spec Delta

## Purpose

Makes other players' work visible as it happens (parts moving off and on, bolts turning, the avatar working with a
tool in hand) without ever changing shared or local game state, within the session's bandwidth budget, and without
replaying old actions to a player who joins or returns later.

## ADDED Requirements

### Requirement: Visuals never change state
A visual of another player's action SHALL NOT change shared or local game state: no part, car, inventory, money,
experience or game-mode change, and no delay of the state update it belongs to. When a visual is cut short, skipped or
turned off, the player SHALL see exactly the state that state sync applied.

#### Scenario: State is applied before the animation ends
- **WHEN** player A unmounts a part and player B's animation of it is still running
- **THEN** B's car state already shows the part unmounted and equals A's

#### Scenario: Visuals turned off
- **WHEN** player B has remote visuals turned off and player A unmounts a part
- **THEN** B sees the part disappear without an animation and B's car state equals A's

#### Scenario: Car removed during an animation
- **WHEN** player A deletes a car while player B is watching a part of it move off
- **THEN** the animation ends at once and nothing of it remains in B's garage

### Requirement: Parts moving off and on
When another player's live change unmounts or mounts a part of a car in the garage, the other players in the garage
SHALL see the part move off the car and fade out, or fade in and move onto the car, and a door, hood or trunk SHALL
swing open or closed. A change that arrives in a snapshot, a resync or as the answer to the player's own change SHALL
NOT animate.

#### Scenario: Wheel taken off
- **WHEN** player A takes a wheel off a car on lifter 1
- **THEN** player B sees the wheel move away from the hub and fade out, and the wheel is gone afterwards

#### Scenario: Part mounted
- **WHEN** player A mounts a brake caliper
- **THEN** player B sees the caliper fade in and move into place, and it stays mounted

#### Scenario: Resync does not animate
- **WHEN** player B resyncs the garage
- **THEN** no part animation plays on B's client

### Requirement: Bolts turning
While another player unscrews or screws a part, the other players in the garage SHALL see that part's bolts turn and
move out or in, keeping pace with the other player's progress, and an undone action SHALL show the bolts going back.

#### Scenario: Unscrewing seen by another player
- **WHEN** player A has unscrewed half of a part's bolts
- **THEN** player B sees about half of that part's bolts out and the next one turning

#### Scenario: Action undone
- **WHEN** player A stops unscrewing and undoes the action
- **THEN** player B sees the bolts go back in and the part stays mounted for both

### Requirement: Avatar activity and tool in hand
Each player SHALL publish what they are working on (kind of work, car, part, tool, progress) only when it changes and
at most four times per second. Other players in the same scene SHALL see that player's avatar turn towards the work,
reach for it, and hold the diagnostic or fluid tool in use.

#### Scenario: Diagnostic tool in hand
- **WHEN** player A uses the OBD scanner on a car
- **THEN** player B sees A's avatar facing the car and holding the scanner

#### Scenario: Working on a part
- **WHEN** player A unscrews a part under a car on a lifted lift
- **THEN** player B sees A's avatar reaching up towards that part and moving its hand while the work progresses

#### Scenario: Work ends
- **WHEN** player A puts the tool away or leaves the garage
- **THEN** player B sees A's avatar without a tool and in its normal stance

### Requirement: Late join without replay
A player who joins or returns to the garage SHALL see the current activity of every other player there (pose and tool
in hand) at once, and SHALL NOT see any animation of an action that happened before.

#### Scenario: Joining while another player works
- **WHEN** player B joins while player A holds the multimeter and is halfway through unscrewing a part
- **THEN** B sees A holding the multimeter and working, no part or bolt animation plays for earlier changes, and B sees the part move off when A finishes

### Requirement: Bandwidth of work visuals
The work visuals SHALL keep every client's average download within the session budget of 50 kB/s and average upload
within 20 kB/s with four busy players, and a client SHALL drop activity updates above its rate limit instead of sending
them.

#### Scenario: Four players working
- **WHEN** four players unscrew and mount parts and use tools for several minutes
- **THEN** each client's average download stays under 50 kB/s and the activity traffic per player stays under 2.4 kB/s upload
