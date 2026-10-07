# Spec Delta

## Purpose

Defines the shared auction: every player in it sees the same lots, the players bid as one team with the shared money,
one player at a time runs the bidding on a lot, the others see it and can raise the team's bid, and a closed lot is
closed for everyone.

## ADDED Requirements

### Requirement: Same lots for everyone
Every player in a shared auction instance SHALL see the same lots for each auction type, with the same car, version,
rating, value and starting price, and a lot's car SHALL look the same for every player who opens it.

#### Scenario: Same lot list
- **WHEN** players A and B open the normal auctions in the same auction instance
- **THEN** both lists contain the same lots in the same order with the same starting prices

#### Scenario: Same car on the stage
- **WHEN** players A and B each open lot 2
- **THEN** both cars have the same colour and condition digest

### Requirement: One bidding per lot
At most one player SHALL run the bidding on a lot at a time. A player who tries to start bidding on a lot that another
player is bidding on SHALL be refused with a message naming that player.

#### Scenario: Second player starts on the same lot
- **WHEN** player A is bidding on lot 2 and player B tries to start bidding on lot 2
- **THEN** B's bidding does not start and B sees that A is bidding on it

### Requirement: Others see the bidding and can raise the team's bid
While a player bids on a lot, every other player in the auction SHALL see that lot's current bid, whether the team or
another bidder leads, and the time left, updated at least once a second. Another player SHALL be able to raise the
team's bid on that lot; the raise SHALL count as the team's bid in the bidding player's auction.

#### Scenario: Watching
- **WHEN** player A bids on lot 2 and the current bid changes
- **THEN** player B's auction shows the new current bid and leader for lot 2 within 1 second

#### Scenario: Raise from a watcher
- **WHEN** player B raises the team's bid on lot 2 while player A runs its bidding
- **THEN** the team's bid in A's auction rises once by the game's bid step, and both show the same current bid

### Requirement: A won or lost lot closes for everyone
When a lot is won by the team, it SHALL become one purchase through the server, with the shared money taken once and the
car in the shared parking. When it is won by another bidder, refused by the server, or its bidding player leaves or
disconnects, the lot SHALL be closed. A closed lot SHALL disappear from every player's list and SHALL not be offered to
players arriving later.

#### Scenario: Team wins
- **WHEN** the team wins lot 2 at 15,000 in player A's bidding while player B is in the auction
- **THEN** shared money drops by exactly 15,000 once, the car is in the shared parking for both, and lot 2 is gone from B's list

#### Scenario: Bidding player leaves
- **WHEN** player A leaves the auction while bidding on lot 2
- **THEN** lot 2 is closed for player B and no money moves

#### Scenario: Late arrival
- **WHEN** player C arrives in the auction after lot 2 was won and while lot 3 is being bid on
- **THEN** C's list has no lot 2 and shows lot 3's current bid
