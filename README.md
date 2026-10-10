# CMS21 Together

A multiplayer mod for Car Mechanic Simulator 2021: work in one shared garage with 2 to 4 friends.

This repository is a fork of [Fozkais/CMS21-Together](https://github.com/Fozkais/CMS21-Together), continuing its
dedicated-server (Dev) architecture. Its builds are not compatible with upstream releases, so everyone in a game
needs the same build of this fork.

## How it works

One player hosts a server, from the game or as a separate program. The server keeps the shared garage and saves it.
Everyone else joins it from the main menu or through Steam. Your own single-player saves are never used or changed.

## What you can do together (1.0)

- **Join and see each other.** Join by Steam (**Join Game**, invites) or by IP address, also while a session is
  running. Everyone sees the others walk around with name tags.
- **Shared progress.** Money, XP, level, skills, scrap, inventory and warehouse belong to the whole group. Shop,
  warehouse and garage upgrades work for everyone.
- **Work on cars together.** Take parts off and put them on, also on the same car at the same time. A part another
  player works on is locked for you, together with what is fixed to it and its fluids. You see it before you click:
  no highlight and the player's name on hover, no mount preview on a slot in use, a lock mark on items another player
  is mounting, and locked move options in the pie of a car someone works on. Lifts, moving cars, the garage
  parking, the engine crane.
- **Car details.** Fluids, wheels, tires and alignment, plates, paint, livery, window tint, mileage, dirt and lights.
- **Tuning.** Gearbox, ECU and carburettor tuning at the dyno, one tuner per car; bonus (visual tuning) parts such as
  spoilers and roof signs, with their paint.
- **Jobs.** Orders and story missions come in for the whole group. Accept a job, work on the customer car together,
  hand it back: the payout and XP arrive once.
- **The workshop.** Tire changer, wheel balancer, spring clamp, engine stand, brake lathe, battery charger, repair
  table, part painting, paint shop, car wash, interior detailing, welder, oil bin and dyno.
- **Diagnostics.** Test drive, test path and dyno. The car is locked for the others while it is away, and the results
  come back to everyone.
- **Trips and buying cars.** Trips to the junkyard, barns, the auction and the car salon. Players who travel to the
  junkyard, a barn or the auction at the same time are in the same place: the same cars and junk piles, and an item
  one player takes from a pile is gone for the others. Parts you buy at the junkyard go to the shared inventory; cars
  you buy go to the shared parking. Travel fees follow one server setting.
- **See each other work and drive.** Bolts turn and parts slide off and on when another player works, and their
  avatar holds the tool. On the test track you see each other's cars drive, and a passenger can ride along.
- **Ping and shopping list.** Point out a part to the others, and share one shopping list.
- **Sit in a car and start the engine**: the others see and hear it.
- **Hosting.** Host from the game with one click, or run the dedicated server on another PC. Password for IP joins,
  admin and kick, a session panel with ping (**F9**).
- **Safe sessions.** The server autosaves and keeps backups. Players who leave and rejoin come back where they left,
  and the session survives a server restart.
- **When something goes wrong.** The server compares the shared state with every player and repairs differences by
  itself. **F7** reloads the garage from the server, **F8** saves a bug report.

## Not shared yet

While you are connected, the game blocks these with "... is not supported in multiplayer yet", so that the garages
cannot drift apart:

- the race track, the other tracks and the photo location;
- building a new engine on the engine stand;
- the separate Parking scene (the garage parking works); the showroom, the main menu's car viewer, is single-player
  only;
- the tutorial, and saving or loading from the game's menus (the server saves the session).

Also not yet:

- Gameplay mods are refused by the server; visual mods are fine.
- A texture pack for the garage is shared by its id only: a player who does not have it installed sees the default
  garage textures.

Planned: the race track and building new engines. The DLC tracks, the photo
location, the tutorial and saving or loading stay blocked.

## Get started

- [Install the mod](docs/install.md): requirements, install, update, uninstall and troubleshooting.
- [Host a session](docs/hosting.md): from the game or as a dedicated server, Steam or IP, settings, backups.
- [Report a bug](docs/bug-reports.md): **F8** in the game, or `Collect-Logs.bat` after a crash.
- [Changelog](CHANGELOG.md) and [how versions work](docs/versioning.md).

Short version: install MelonLoader **0.5.7**, unzip `CMS21-Together-<version>-client.zip` into the game folder, start
the game, click **Multiplayer** in the main menu.

## Playing together

- **Names:** each player's name floats above their head. Set it in the Multiplayer panel in the main menu or as
  `PlayerName` in `UserData\MelonPreferences.cfg` (section `[CMS21Together]`); empty means your Steam name. Two players
  with the same name get a ` (2)` suffix.
- **Who you see:** players are visible when you are in the same scene. In the garage everyone sees everyone; a player
  who travels away shows as away and appears again when you are both in the same place, also in the junkyard, a
  barn or the auction.
- **Sitting in a car:** a player who sits in a car in the garage is shown in that seat (driver or passenger side) and
  stays there while the lift moves. Their avatar is crouched, as the game has no sitting pose for it.
- **Shared and not shared:** the garage, money, XP, level, skills, inventory and warehouse are shared and kept by the
  server. The junkyard, a barn and the auction are shared by everyone who is there at the same time; the next trip
  after everyone has left is a new one, as in the game. Coming back to the garage always loads the shared garage
  from the server.
- **Garage look:** the walls, floors, machines and gates you pick at the garage-look computer, and the texture pack,
  are the same for everyone and kept by the server. One player customises at a time ("<name> is customising the
  garage."); the others see the new look when that player closes the window.
- **Tuning:** the gear ratios, ECU maps and carburettor settings you apply at the dyno's tuning computer reach
  everyone when you press apply. One player tunes a car at a time ("<name> is tuning this car."); while the window is
  open the others cannot take off the tuned parts, run the dyno with that car, or lift, move, park or delete it. The
  window closes by itself after five minutes without an applied change. A tuned part that is taken off keeps its
  tuning in the shared inventory and on the car it is fitted to next.
- **Bonus parts:** spoilers, hood scoops, roof signs and the other bonus parts you fit or take off in the bonus modes
  look the same for everyone, painted as you painted them (the paint shop paints them with the car). The part leaves
  the shared inventory once when fitted and comes back once when taken off. One player works on a bonus slot at a
  time ("<name> is fitting a bonus part here."); if the slot changed a moment ago and your game has not shown it yet,
  your fit is refused ("This slot just changed."), you keep your part and then see the new one.
- **Job achievements:** when a job is finished, the Steam stats and achievements for it (finished orders, the XP and
  money bonus, the last story mission) count for every connected player who worked on it: who took the order, who
  changed, examined or locked a part of its car, and who finished it. The host can give them to everyone in the garage
  instead, or only to the finisher (`job_stats_to` in `server_config.ini`).
- **Shopping list:** the group shares one shopping list. What anyone adds, removes or clears shows up for everyone,
  and the server keeps it with the session.
- **Test drive together:** players who are on the test track at the same time see each other's car drive (it
  appears a few seconds after you arrive). Cars pass through each other and through players.
- **Ride along:** sit in the passenger seat of a car before its driver starts a test drive, and you travel to the
  test track with them ("Riding along with <name>."). A few seconds after you arrive you sit next to the driver: the
  mouse turns your head, you cannot drive or steer, and the driver sees you in the passenger seat. When the driver
  drives back to the garage or leaves the game, you come back to the garage too. Only the driver's test drive counts
  (mileage, examined parts). To leave early, use the pause menu's return button.
- **Drive:** the Drive option in a car's pie menu opens the map, as in the game.
- **Test path:** the test path is for one player; it does not start while another player sits in the car ("<name> is
  sitting in the car.").
- **See each other work:** when another player takes a part off or puts it on, you see the bolts turn and the part
  slide off or on, and their avatar holds the tool. This is only a picture of what they do: the change itself
  arrives as before. Turn it off with `RemoteVisuals = false` in `UserData\MelonPreferences.cfg`.
- **Parts in use:** before a part comes off or goes on, your game asks the server for it. A part another player works
  on has no highlight, its label says "<name> is working on this part.", and a click on it plays the error sound with
  that message. The same goes for what is fixed to it (a crankshaft while its bearing caps are worked on), for a fluid
  being filled or drained ("… working on the coolant system."), and for the item another player is mounting. While
  anyone works on a car, the others cannot lift, move, park, delete it or end its job ("<name> is working on this
  car."). Holding the mouse button to unmount reserves the part at the start of the hold, so there is no wait. If
  the server does not answer within 3 s you see "The server did not answer. Try again."
- **DLC:** players may own different DLC. DLC cars and parts that not every connected player owns are blocked while
  connected.
- **Ping:** look at a part (or any spot) and press the **middle mouse button**. Everyone in the same scene sees it
  boxed with your name for 5 seconds, also through walls and cars, and hears a short sound. Change the key with
  `PingHotkey` in `UserData\MelonPreferences.cfg` (`None` turns it off).
- **Keys:** **F7** reloads the garage from the server, **F8** saves a bug report, **F9** opens the session panel,
  the **middle mouse button** pings.

## For developers

- Build: open `CMS21-Together.sln` (client = MelonLoader mod, `CMS21-Together-Core`, dedicated server). The release
  zips come from `tools/release/Build-Release.ps1`.
- Tests: the two-instance test harness lives in `tools/test-env` and `tools/TestHarness`.
- Plans and design: `openspec/ROADMAP.md`, the changes in `openspec/changes/`, and `STATUS.md`.

## Authors

* **Fozkais** - *Main dev* - [Fozkais](https://github.com/Fozkais)

This fork is maintained in [ccoodduu/CMS21-Together](https://github.com/ccoodduu/CMS21-Together). It uses
[MelonLoader](https://github.com/LavaGang/MelonLoader), [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks),
[Newtonsoft.Json](https://www.newtonsoft.com/json) and [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui).

## License

Distributed under the MIT License. See [LICENSE](LICENSE) for more information.
