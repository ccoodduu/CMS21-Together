# Spec Delta

## Purpose

Defines how junk piles, items and cars in a shared junkyard or barn stay the same for every player in it: the first
player's piles are recorded and replayed, every take and put-back is decided by the server, and parts and cars bought
there disappear for the others.

## ADDED Requirements

### Requirement: Same piles for everyone
The contents of every junk pile in a shared instance SHALL be the contents the instance's first player generated. The
server SHALL store that record and every later player SHALL replace its own pile contents with it before it can open a
pile.

#### Scenario: Same items in a pile
- **WHEN** players A and B are in the same junkyard instance and open the same pile
- **THEN** both see the same items with the same conditions

#### Scenario: Pile opened before the record exists
- **WHEN** player B tries to open a pile before the first player's record has reached B
- **THEN** the pile does not open, B sees a short message, and the pile opens normally once the record has arrived

#### Scenario: First player leaves before recording
- **WHEN** the first player of an instance leaves before its record reached the server and player B is still there
- **THEN** B becomes the instance's first player, its piles are recorded, and later players get B's piles

### Requirement: Taking an item goes through the server
Taking an item from a pile SHALL be decided by the server: the first player to take it SHALL keep it, it SHALL
disappear from that pile for every other player in the instance, and a player whose take was refused SHALL lose the
item locally and see who took it.

#### Scenario: Item taken by one disappears for the other
- **WHEN** player A takes an item from a pile while player B is in the same junkyard
- **THEN** within 1 second the item is no longer in that pile for B, also in B's open pile window

#### Scenario: Both take the same item
- **WHEN** players A and B take the same item at the same moment
- **THEN** exactly one of them has it in the taken items, the other's take is undone with a message naming the first player, and the server holds it for one player only

### Requirement: Items go back to their pile
An item put back into its pile, removed from the taken-items window, or still unpaid when its taker leaves the scene or
disconnects SHALL become available in its pile again for every player in the instance.

#### Scenario: Put back
- **WHEN** player A puts a taken item back into its pile
- **THEN** player B sees it in that pile again and can take it

#### Scenario: Leave without paying
- **WHEN** player A leaves the junkyard without buying the items it took
- **THEN** those items are back in their piles for player B

#### Scenario: Disconnect with taken items
- **WHEN** player A disconnects while holding taken items in the junkyard
- **THEN** those items are back in their piles for player B

### Requirement: Buying parts in a shared scene
Buying taken parts SHALL charge the shared money once at the server's price and add the parts to the shared inventory
once; the server SHALL accept only items the buyer holds in that instance, and bought items SHALL never return to a
pile.

#### Scenario: Parts bought while another player stays
- **WHEN** player A buys 3 taken parts and returns to the garage while player B stays in the junkyard
- **THEN** shared money drops by the server's price once, both inventories contain the 3 parts once, and none of them is in B's piles

#### Scenario: Item not held by the buyer
- **WHEN** a purchase names an item that the buyer does not hold in that instance
- **THEN** that item is not added or charged, and the server logs it

### Requirement: A car bought there is gone for everyone
A car bought in a shared junkyard or barn SHALL be removed from the scene of every other player in the instance and
SHALL not be generated for players arriving later; a second purchase of the same car SHALL be refused without taking
money.

#### Scenario: Car bought while another player looks at it
- **WHEN** player A buys a car in the junkyard while player B has that car's info window open
- **THEN** B's window closes, the car disappears from B's junkyard, and the car is in the shared parking once

#### Scenario: Both buy the same car
- **WHEN** players A and B buy the same car at the same moment
- **THEN** one purchase is accepted, the other is refused as taken with a message, and shared money drops by one price only
