# CMS21 Together: how to try this build

A development build of the multiplayer mod for Car Mechanic Simulator 2021. The version is in the zip name (for
example `0.6.0-dev.540`). Host and players must all use the same zip: the server refuses any other build.

## You need

- Car Mechanic Simulator 2021 on Steam, with Steam running. No DLC is needed.
- MelonLoader **0.5.7** exactly (not 0.6): https://github.com/LavaGang/MelonLoader/releases/tag/v0.5.7 - run the
  installer, pick `Car Mechanic Simulator 2021.exe`, untick "Latest" and choose v0.5.7.

## Install

1. Back up your saves: copy `%USERPROFILE%\AppData\LocalLow\Red Dot Games\Car Mechanic Simulator 2021` somewhere
   safe. The mod does not write your own save profiles during a multiplayer session, but a backup costs nothing.
2. Open the game folder (Steam: right-click the game > Manage > Browse local files) and unzip
   `CMS21-Together-<version>-client.zip` into it, overwriting older files. `Mods\CMS21-Together.dll` must end up
   next to `Car Mechanic Simulator 2021.exe`.
3. Start the game. The MelonLoader console shows `Together Mod <version> initialized!`.
4. Optional: your name above your head. After the first start, open `UserData\MelonPreferences.cfg`, find
   `[CMS21Together]` and set `PlayerName = "YourName"` (empty = your Steam name).

## Host

Run `TogetherServer\CMS21_Together_Server.exe` from the game folder (or unzip
`CMS21-Together-<version>-server.zip` on another PC and run `CMS21_Together_Server.exe` there). The first start
creates `server_config.ini` next to it. The shared garage is saved in `Saves\server_save.json` in the server
folder. Type `/stop` in the server window to shut it down with a final save.

Or host from the game: in the main menu, click **Multiplayer** > **Host**, choose the settings and press **Start**.
The game starts that server and joins it. **Stop** on the same tab (or **Stop server** in the F9 panel) saves the
session and shuts the server down; quitting the game does the same.

## Join

In the main menu, click **Multiplayer** (top right), type the address and press **Join**:

- Same PC as the server: `127.0.0.1`.
- Another PC: the host's IP address, for example `192.168.1.20` on the same network, or the host's public IP with
  port 7777 forwarded on the host's router (`IP:port` if the server uses another port).
- Steam: when the server runs with `use_steam = True`, its 17-digit Steam server ID (in the server window) works as
  the address. Players in a session show **Join Game** in the Steam friends list, which also works with the game
  closed.

If a join fails, the menu says why (server not reachable, other mod version, server full, wrong password, kicked,
server stopped). After "Wrong server password" the Join tab asks for the password. The address is remembered for next
time. In a session, **F9** shows the players with their ping, your Steam friends, and for the host a **Kick** button.

## What works

- Joining a running server, also late; leaving and rejoining; everyone loads the same garage.
- Shared money, XP, level, scrap and skills; shop, warehouse and inventory; garage upgrades.
- Seeing each other walk around, with name tags; sitting in cars and starting engines.
- Working on cars together: parts, fluids, wheels, paint, tint, lifts, moving cars, parking, engine crane.
- Orders and jobs, test drive, test path and dyno, and the workshop machines and car tools.
- Trips to the junkyard, barns, auction and car salon; bought cars go to the shared parking. In the car salon you pick
  the version, rims, tyres and paint as in the game, and the money is taken once from the shared money.
- The garage's look (walls, floors, machines, gates at the garage-look computer) is the same for everyone and kept by
  the server; one player customises at a time.
- Tuning at the dyno's tuning computer (gearbox, ECU, carburettor): one player tunes a car at a time, and a tuned part
  you take off keeps its tuning in the shared inventory and on the next car it goes on.
- Bonus parts (spoilers, scoops, roof signs) fitted, painted and taken off look the same for everyone.
- **F7** reloads the garage from the server if something looks wrong.

## Please try: the race track and the speed track

1. **Drive a car to the race track** from the map while a friend stays in the garage. Your friend should see "<name>
   has this car on the race track." when they try to work on it. Report: anything your friend could change on it.
2. **Drive together:** your friend takes another car to the race track. You should see each other's car drive. Report:
   a car that does not appear, jumps or hangs.
3. **Drive a full lap.** When it beats the group's best, everyone sees "New group record on the race track: <name>,
   m:ss.fff". Rejoin later: the race track should still show your best time. Report: a record that was lost.
4. **Race a friend:** both drive to the race track, open the session panel (F9), choose the laps and press "Start
   race". Both cars jump to the start and the lights turn green for both at the same moment; after the laps everyone
   sees "Race result: 1. <name> m:ss.fff, 2. ...". Report: lights that were clearly apart, a lap that did not count,
   or a wrong result.
5. **Ride along** to the speed track: sit in the passenger seat before your friend drives there. Report: where you
   ended up, and whether the pause menu's return brought you both back.
6. **Bump into each other:** on the tracks your car now hits your friends' cars (their car is never pushed on their
   screen; at high speed a hit can come a few metres behind their car). Don't want it? Set `TrackCollisions = false`
   in `UserData\MelonPreferences.cfg` (category `CMS21Together`); the host turns it off for everyone with
   `track_collisions = off` in the server config. Report: a car you could not drive away from, or a hit that felt wrong.

## Please try: the garage look

1. **Customise the garage** at the garage-look computer and close the window. Your friends should see the new walls
   and floors a moment later. Report: anything that looked different for them.
2. **Click the computer while a friend customises.** You should see "<name> is customising the garage." and nothing
   opens. Report: a black screen or a window that opened anyway.
3. **Reset a wall to its default** and close; then let a friend open and close the window without changing anything.
   The wall should stay default for everyone.

## Please try: building an engine

1. **Build a new engine** on the empty engine stand ("New engine" in the stand's menu). Your friends should see it on
   their stand. Report: a stand that stays empty for someone.
2. **Build on a stand that holds an engine.** You should see "Take the engine off the stand first." and the engine
   stays. Report: an engine that disappeared.

## Please try: bonus parts

1. **Fit a spoiler or a roof sign** in the bonus assembly mode and paint the car in the paint shop. Your friends
   should see the part with your paint. Report: a missing part or another colour.
2. **Both pick the same empty slot at once.** One of you gets the part on the car; the other is told why and keeps
   the part in the inventory. Report: a part that disappeared from the inventory without being on the car.

## Please try: tuning

1. **Tune a car on the dyno** with racing parts (gearbox, ECU or carburettor) and press apply in each tab. Your
   friends should get the new values a moment later; run the dyno to compare. Report: values that differ.
2. **Click the tuning computer while a friend tunes the same car.** You should see "<name> is tuning this car." and
   nothing opens. Report: a window that opened anyway or a stuck screen.
3. **Take off a tuned ECU or carburettor** and fit it to another car of the same engine. The tuning should come along.

## Please try: parts in use (locks)

Two or more players on one car. For each item, report what you saw, and if it went wrong, press **F8** right away.

1. **Hover a part a friend is working on.** It should have no highlight, and the label should say "<name> is working
   on this part." Report: highlight shown or not, the label text, and whether the label stayed on your own parts.
2. **Hold to unmount at a normal ping** (100 ms or so). The ring should fill and the unmount start with no extra wait.
   Report: any pause after the ring was full, or a "Waiting for the server…" hint.
3. **Click a part a friend is working on.** You should hear the error sound and see the message; nothing starts.
   Report: whether anything started (bolt view, part moving).
4. **Open the item chooser on an empty slot and close it with ESC.** Your friend should be able to use that slot right
   after. Report: how long your friend had to wait.
5. **Fill a fluid while a friend is near the car** (coolant, brake fluid, oil). Your friend cannot take off the
   reservoir or fill the same fluid until you stop. Report: the levels both of you see afterwards.
6. **Raise the lift while a friend works on that car.** It should be refused with "<name> is working on this car."
   Report: whether the lift moved on anyone's screen.
7. **Park the car or end its job while a friend works on it.** Both should be refused with the same message, and the
   car should stay. Report: whether the car or the job disappeared for anyone.

## Not yet

Driving around in the garage, the drag strip (Drag Racing DLC), Workshop tracks and the separate Parking scene are
not shared yet. The showroom (the car viewer in the main menu) is single-player only. While connected, the game refuses
them with "... is not supported in multiplayer yet", so the garages cannot drift apart. The junkyard, barns and
auction are each player's own (not shared).

## Logs for a bug report

- In the game: press **F8**. The zip is in `UserData\CMS21Together\BugReports\`; the host also sends the server's
  `BugReports\<id>.zip`.
- After a crash: double-click `Collect-Logs.bat` in the game folder (or in the server folder). The zip lands on your
  Desktop.
- By hand: `MelonLoader\Latest.log` (earlier runs in `MelonLoader\Logs\`) and the server's `Log\Latest.txt`.

Send them with what you did, what you expected, what happened and roughly when. More in `docs/bug-reports.md` on
the project's GitHub page.
