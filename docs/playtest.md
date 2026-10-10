# Playtest checklist

What to try with friends on the current `main`, and the checks the test harness cannot do. The harness drives the
game through its code paths; the items marked **hand check** need the real UI, a second PC or a real Steam friend.
Report problems with both players' `MelonLoader\Latest.log` and the server's `Log\Latest.txt`, and say what you did.

## Setup

1. Install the client zip into a game install without gameplay mods (QoLmod, TK, LvxBetterCarSpawns,
   LvxOwnedCarsOnly, QuickShop and AutosaveMod are refused; LoadOptimizer is fine). Set your name in the Multiplayer panel.
2. Host from the main menu: Multiplayer → Host (starts the bundled server; set a password for internet games). Or run
   `TogetherServer\CMS21_Together_Server.exe` yourself. Friends join by address, or over Steam when `use_steam = True`.
3. **Hand check:** a friend joins over the internet (Steam "Join Game", invite, and by IP with port forwarding and the
   password). The harness only joins on the same PC.
4. **Hand check:** the Friends tab and the F9 session panel with a real friend (ping, kick from the admin's client).

## The workshop together

1. Walk around together; names over heads; a late joiner sees everyone where they stand.
2. Take an order, both work on the customer car, hand it back: payout and XP arrive once, for both.
   **Hand check:** a real job with the game's own end-of-job checks (the harness ends jobs directly).
3. Take parts off and put them back at the same time, also on the same part (one of you gets a message).
4. Engine crane: drain the oil, take the engine out, put it back.
5. Move cars between places and onto lifts, raise and lower lifts, park and unpark, swap parking slots.
6. Tire changer, wheel balancer (only one player can balance at a time), spring clamp, brake
   lathe, battery charger.
   **Hand check: the engine stand** (hang an engine on it, rotate it, take a part off, take the engine off). The harness
   builds new engines only with the fade stepped away (`stand-nofade`, scenario `engine-build`), because the build waits
   for frame ends that never come headless. **Hand check: building an engine** with a visible game: "New engine" in
   the stand's pie menu on an empty stand (fade, engine on the stand for both), then on an occupied stand (refused,
   "Take the engine off the stand first.").
7. Fluids, wheels and alignment, plates, paint shop and window tint; also car wash, interior
   detailing and welder: the other player sees the result. Fees are charged once, by the server.
   **Hand check:** tint some windows: the money drops once by 50 per window for both players.
8. Sell a car, open crates, scrap parts, buy plates, reset skills: money, scrap and XP stay the same for everyone.

## Test drive and diagnostics

1. Take a car to the test track from the map (seated and on foot). The other player sees the car locked with a label
   and cannot work on it. After the return both see the driven kilometres.
   **Hand check:** a departure that is refused (the other player is working on the car) from both map paths: the map
   window, the input and the pie menu must be usable afterwards.
2. Test path: the other player sees the claim; after the drive the examine report opens and the car is at the "after
   the test" spot for both. **Hand check:** the real exit from the path and its report (the harness cannot finish it).
3. Dyno: measure a car; the other player sees the measured values. **Hand check:** a tuned car on the dyno (values
   before, during, after measure and after cancel) and a job car on the dyno.
   **Hand check: tuning at the dyno** with a visible game: open the tuning computer with racing parts, move the
   gearbox sliders and the ECU bars with the mouse and apply each tab; the other player sees the values (scenario
   `car-tuning` drives the tabs' apply actions headless). A second player clicking the computer is told "<name> is
   tuning this car."; leaving the window open five minutes closes it.
   **Hand check: bonus parts** with a visible game: fit and take off a bonus part with the mouse in the bonus modes
   (pie `mode_bonus_assemble`/`mode_bonus_disassemble`), paint the car with it; the other player sees the part and its
   paint (scenario `car-bonus` drives the same game calls headless).
4. Sit in a car and start the engine: the other player sees you seated and hears the engine.

## Travel and sessions

1. Junkyard alone, come back: the garage reloads from the server; bought parts are in the shared inventory.
   **Hand check:** try to buy a car in the junkyard. Buying cars is not shared yet, so it must be refused before any
   money leaves (the game takes the money before it asks where the car goes). Note your money before and after.
2. Leave and rejoin; stop the server with `/stop` and start it again: everything comes back.
3. If something looks out of sync, press F7: the garage reloads from the server.
4. Car salon (covered by the scenario `salon-buy`): both travel to the car salon and open the car list at the same
   time. Each player configures a car; each sees only their own configurator car (the cars stand at the same spot),
   which is expected. One buys a car in another version with other rims: the money drops once for both, and the car is
   in the shared parking for both with that version and rims.

## Not shared yet

Shared junkyard, barn and auction (row 15), seeing other players' actions as animations (row 17), the drag strip.
The game refuses blocked features with "... is not supported in multiplayer yet".
