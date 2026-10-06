# Spec Delta

## Purpose

Makes every change to the shared money, scrap, experience, level, skills and barn count server-authoritative, so all
players always see the same values and no game path can change them on one machine only.

TODO (draft cut short by a reboot): add requirements for scrap per condition, quality upgrade, license plates and
persistence of the barn count; review scenario wording.

## ADDED Requirements

### Requirement: Every economy change goes through the server
Every change to the shared money, scrap, experience or barn count made by a game action SHALL be applied once by the server and SHALL reach every player. A money or scrap change that no synced feature claims SHALL NOT change any player's value.

#### Scenario: Travel fee
- **WHEN** player A travels to the junkyard from the map and the trip costs 500
- **THEN** the shared money drops by 500 once, for A and for B

#### Scenario: Unclaimed money change
- **WHEN** a game path changes money without being claimed by a synced feature
- **THEN** no player's money changes and the client logs and counts the unclaimed change

### Requirement: Fees are checked by the server
For fees the server can compute (travel, fluid spill, car paint, wash, tint), the server SHALL apply its own amount. For fees it cannot compute (welder, interior detailing, fluid refill, part repair) it SHALL apply the client's amount only within bounds. An amount outside the rule SHALL be refused and the requesting player's value corrected.

#### Scenario: Paint costs the same for everyone
- **WHEN** player A paints a car in the paint shop for 1000
- **THEN** the shared money drops by 1000 once for A and B

#### Scenario: Wrong amount
- **WHEN** a fee request arrives with an amount the rule does not allow
- **THEN** the server applies nothing and the requesting player's money returns to the server's value

### Requirement: Selling a car
Selling a car SHALL remove it for every player and add its price to the shared money once. The server SHALL refuse the sale of a job car, a car another player is working on or a car that is away, and the car SHALL then stay for everyone.

#### Scenario: Car sold
- **WHEN** player A sells a car from the garage
- **THEN** the car is gone for A and B and the shared money rises by the sale price once

#### Scenario: Car in use
- **WHEN** player B is unmounting a part of a car and player A tries to sell it
- **THEN** the sale is refused with a message and the car stays for both

#### Scenario: Two players sell the same car
- **WHEN** players A and B sell the same car at nearly the same time
- **THEN** the car is sold once and the money rises once

### Requirement: Skill reset
Resetting the skills SHALL reset the shared skills for every player and take the reset cost from the shared money once.

#### Scenario: Reset
- **WHEN** player A resets the skills
- **THEN** both players see no point skills unlocked, the same available points, and the money dropped by the cost once

### Requirement: Scrapping
Scrapping an item SHALL remove it from the shared inventory and add the scrap the server computes, once.

#### Scenario: Scrap a part
- **WHEN** player A scraps a part from the inventory
- **THEN** the part is gone for both players and the shared scrap rises by the same amount for both

### Requirement: Crates keep money and level equal (upstream #94)
Opening a crate SHALL pay the picked card once into the shared money, scrap or experience, within the card's range, and only once per crate.

#### Scenario: Several crates
- **WHEN** players A and B each open three crates and take a card from each
- **THEN** money, scrap, experience and level are equal for both players and the server

### Requirement: Shared barn count
The number of available barns SHALL be shared: using a barn map SHALL add one barn for every player, and a barn trip SHALL use one up for every player.

#### Scenario: Barn map
- **WHEN** player A uses a barn map from the shared inventory
- **THEN** the map is gone and the barn count is one higher for both players

### Requirement: Late join
A player who joins SHALL get the server's money, scrap, experience, level, skills and barn count.

#### Scenario: Late join after trades
- **WHEN** player A sells a car, resets the skills and uses a barn map, and player B joins afterwards
- **THEN** B's money, skills and barn count equal A's
