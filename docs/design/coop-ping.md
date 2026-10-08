# Design note: co-op ping (ROADMAP row 22 `coop-ping`)

Size S, no OpenSpec proposal. User wish 2026-10-08: look at a part (or a spot), press a key, and every other player
in the same scene sees it marked with your name for a few seconds, also through walls, with a short sound.

## Behaviour

- **Key:** `PingHotkey` in `[CMS21Together]` of `UserData\MelonPreferences.cfg`, default `Mouse2` (the middle mouse
  button); `None` turns it off. The game binds nothing to the middle mouse button in its Rewired maps (checked by the
  `ping` scenario with the `input-bindings` verb, every map and category, keyboard and mouse: the mouse maps use
  only the left and right buttons, buttons 4 and 5 and the wheel). `G` was the other candidate, but the UI map binds it
  to `UIEditLivery`. A mouse button also keeps the hand on the mouse while looking at a part.
- **What is pinged**, first match wins:
  1. the game's own mouse-over part (`GameScript.partMouseOver`, set by the game's raycast in the part views), resolved
     with row 18's `LockHooks.TryResolve` to loader + `s:` key;
  2. the body part under the cursor (`IOMouseOverType`/`IOMouseOverCarLoader`, as row 18's `LockSelection` reads it),
     resolved with `PartRegistry.TryGetBodyIndex` to loader + `b:` key;
  3. a raycast of 60 m from the camera through the cursor (or the screen centre when the cursor is hidden): a hit on a
     mounted `PartScript` of a synced car is a part ping, anything else a spot ping at the hit point;
  4. nothing hit: no ping.
- **What the others see:** a box around the part on screen (from its meshes' bounds; `Renderer.bounds` is empty while
  the game culls the renderer behind a wall), or a square at the spot, pulsing in orange, with
  the pinger's name and the distance above it, for 5 s with a 1 s fade. It is drawn with IMGUI after the 3D scene, so it
  shows through walls and cars; off screen or behind the camera it sticks to the screen edge. The pinger sees the same
  marker in blue, labelled "You". A new ping of the same player replaces the old marker. Others hear the game's `Popup`
  sound once (`SoundManager.PlaySFXOneShot`).
- **Part on the receiver:** the receiver resolves loader + key on its own car through `PartRegistry` (row 1's part keys,
  the same keys row 18 locks), so the box follows the part on the receiver's car. A key it cannot resolve (car not
  loaded yet, part off) falls back to a spot marker at the position the pinger sent.

## Network

One packet, `CoopPing { PlayerId, Scene, CarLoaderID, PartKey, Position }`, appended to `PacketTypes`. The client sends
it; the server relays it and keeps no state: nothing is saved, nothing goes into a snapshot, a late joiner sees no old
pings.

Server (`PingHandlers`): the sender must be in session (no `[AllowBeforeSync]`); the packet's scene must be the
sender's scene in the presence registry; the scene must be one where players see each other
(`GameSceneInfo.ShowsAvatars`, a shared outdoor scene needs an instance); the position must be finite. A loader the
server does not know, or a key that is not `s:`/`b:` (at most 128 characters), turns the ping into a spot ping. The
relay goes to every other in-session player in the same scene (and the same outdoor instance), like `Movement`.
Rate limit: one ping per player per 0.4 s; the rest is dropped and logged at most every 5 s. The client throttles
presses to one per 0.5 s, so normal use never meets the server limit even with network jitter.

## Guard and scenes

A ping changes no game state, so it needs no guard rule. The hotkey works only while connected and synced, in a scene
where players see each other, and not while a game window, the session panel or a text field has focus. Markers are
cleared on a scene change and on disconnect, and a player's marker goes when that player leaves.

## Not hooked

No Harmony patch: the ping reads `GameScript` fields and the part registry, and draws in `OnGUI`. The game's own
`Highlighter` on a part was not used: `PartScript.Update` sets it every frame from its own mouse-over state and would
fight a remote highlight, and it is not visible through walls.

## Tests

Scenario `ping` (lane 1, two clients, guard on Enforce), harness verbs `ping <loader> <key>` (puts the part under the
game's mouse-over and runs the hotkey's path; `ping` without arguments stays the status probe), `ping-spot x,y,z`,
`ping-burst <n> <loader> <key>` (raw packets past the client throttle), `ping-markers [clear]`,
`input-bindings [binding]`, and the dump section `pings`.
