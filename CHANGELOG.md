# Changelog

All notable changes to this fork of CMS21 Together. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version numbers follow
[docs/versioning.md](docs/versioning.md).

The milestones M1 to M4 below were played as development builds of 0.6.0 (`0.6.0-dev.<n>`) and were not tagged as
releases. 1.0.0 (milestone M6) is the first release of this fork. Upstream's own releases (0.4.x, 0.5.0) are listed on
[Fozkais/CMS21-Together](https://github.com/Fozkais/CMS21-Together/releases).

## [Unreleased]

Work towards 1.0.0 (milestones M5 "Robust sessions" and M6 "Release 1.0").

### Added

- Cars bought outside the garage (junkyard, barn, auction, car salon) go to the shared parking. The server checks the
  money and a free parking slot and takes the price once.
- Returning players are recognised: by Steam ID, or by the player key in `UserData\CMS21Together\player.json` for IP
  joins. A player who rejoins comes back where they left. The same player cannot join twice at the same time.
- In-game bug report: **F8** saves your logs, settings, mod list and the shared state to
  `UserData\CMS21Together\BugReports\<id>.zip`. When you are connected, the server and the other players save theirs
  with the same id.
- `Collect-Logs.bat` in the game folder and in every server folder: packs the logs into one zip on the Desktop, also
  when the game crashed.
- Guides: [docs/install.md](docs/install.md), [docs/hosting.md](docs/hosting.md),
  [docs/bug-reports.md](docs/bug-reports.md); this changelog and [docs/versioning.md](docs/versioning.md).
- `tools/release/Build-Release.ps1 -Release` builds a release only from a clean, tagged commit with a changelog entry.

### Fixed

- Item ids handed out by the server always increase.

## M4: The full workshop - 2026-10-06/07

### Added

- Workshop machines are shared: tire changer, wheel balancer (one player at a time), spring clamp, engine stand, brake
  lathe, battery charger, repair table, part painting and the tool positions.
- Car tools are shared: engine crane, car paint shop, car wash, interior detailing, oil bin, welder and the dyno.
- Sitting in a car and starting its engine: the others see you seated and hear the engine.
- Every change of money, scrap and XP goes through the server. Fees (paint, wash, welder, tint, travel) are charged
  once. Travel fees follow one server setting, `travel_fees`.
- Host from the game: **Multiplayer > Host** starts the bundled server and joins it. New sessions pick a difficulty.
- Password for IP joins, an admin key, kicking players, the session panel (**F9**) with ping and Steam friends, and
  short notices when players join or leave.

### Fixed

- Two players (or one player after a rejoin) could create items with the same id.
- A late join failed while a car was on the test track.

## M3: Run jobs together - 2026-10-06

### Added

- Orders and jobs: the server keeps the order list, one player's game generates new orders, accepting, declining and
  expiry go through the server, the customer car appears for everyone, and the payout and XP arrive once.
- Car details are shared: fluids, wheels, tires and alignment, license plates, paint, livery and window tint,
  mileage, dirt and headlights.
- Test drive, test path and dyno: the car is locked for the others while it is away, and the results (driven
  kilometres, found parts, dyno values) come back to everyone.

## M2: Work on one car together - 2026-10-06

### Added

- Taking parts off and putting them on (mechanical and body parts) is shared; two players can work on the same car,
  and a part one player is working on is reserved for them.
- Lifts, moving cars between places, and the garage parking (park, take out, swap slots).
- Cars and their parts survive a server restart and are sent to players who join late.
- The server compares the shared state with every player every few seconds and repairs differences by itself.
  **F7** reloads the garage from the server by hand.
- Trips to the junkyard (parts only); parts you buy there land in the shared inventory.

### Fixed

- Selling items paid the full price, Expert difficulty did not double XP, and scrap did not reach the game profile.

## M1: Friends connect and see each other - 2026-10-06 (dev build 0.6.0-dev.561)

### Added

- A client zip (with the dedicated server in `TogetherServer\`) and a server zip, both with a how-to-try text.
  One version number for the mod, the server and the zips.
- Players see each other with name tags, also after a late join. Your name comes from the Multiplayer panel or the
  `PlayerName` setting, otherwise from Steam.
- Join from the main menu by IP address, host name or Steam server ID; Steam **Join Game**, invites and joining while
  the game is closed. Failed joins say why.
- The server refuses another mod version, another game version and gameplay mods. Different DLC is allowed; DLC
  content that not every player owns is blocked while connected.
- Features that are not shared yet are blocked while connected ("... is not supported in multiplayer yet").

### Fixed

- A player who stood still was invisible to players who joined later.
- Joins failed at random with a socket error.
- A session could run on, and write to, the player's own save profile.

## M0: Foundations - 2026-10-06

### Added

- The server save has versioned sections, autosaves, keeps rotating backups and a copy from each start, and is written
  so that a crash cannot leave a half-written file. A broken save is moved to `Saves\corrupt` and the newest working
  backup is loaded; a save from a newer server version is refused.
- The mod never writes your own save profiles during a multiplayer session, and backs them up before you join
  (`UserData\CMS21Together\ProfileBackups`).

### Changed

- Forked from the `Dev` branch of [Fozkais/CMS21-Together](https://github.com/Fozkais/CMS21-Together) (0.5.0, with
  the dedicated server). Builds of this fork do not work with upstream releases.

[Unreleased]: https://github.com/ccoodduu/CMS21-Together/commits/main
