# Design

## Context

See proposal.md for the motivation. Today's code (observed, `main`):

- `Movement.UpdateMovement` sends a `MovementPacket` at most 20 Hz and only on change; it reads
  `GameData.Instance.LocalPlayer`, which `LoaderAddition.CustomLoad` creates in the garage only. Outside the
  garage the reference points at a destroyed `CharacterMotor`, so nothing is sent.
- `ClientData.Players` maps id → `PlayerInstance`; an avatar is created by `ClientData.SpawnPlayer` on the first
  movement packet. Avatars are scene objects (only `ModGameManager.PlayerPrefab` is `DontDestroyOnLoad`), so a
  scene change destroys them while the dictionary keeps the dead references.
- Every garage load (first join and every return) runs `CustomLoad` → `ClientData.Reset()` → `AskForSync`.
  `GameData`'s constructor needs `GarageLevelManager`, so it cannot be built in other scenes.
- The server stores movement in `PlayerState` (parallel dictionaries, `[NonSerialized]`), never sends it to a
  joining client and never clears it. `ConnectPacket.username` exists but the client sends `TestUser<id>` and
  the server ignores it. `SteamTransport.OnDisconnected` does not call `Client.Disconnect`, so a Steam drop is
  only noticed by the 10 s heartbeat timeout.
- Junkyard/barn parts already go through the server (`TakenItemsWindowHook` → `ItemsExchangePacket`). Car
  purchases are not hooked; money is not hooked in general, so a local `AddPlayerMoney` is overwritten by the
  next `WorldState` (cars bought away are free and never shared).
- Game API (verified in the decompiled stubs): `SceneType` (None, Junkyard, Barn, Showroom, Garage, Auction,
  TestTrack, RaceTrack, Salon, Parking, Menu, FunTrack, OffroadTrack, DragStrip, CustomTrack, Tutorial,
  PhotoLocation, SpeedTrack); `GameScript.CurrentSceneType`, `SitInside(CarLoader, bool left, gameMode)`,
  `ExitFromInterior(bool)`, `BuyCar(CarLoader, int)`, `SellCar(CarLoader, int)`; `GameDataManager.SaveCar(NewCarData,
  int, bool toParking)`; `CarLoader.SaveCarToFile(int, bool)`, `GetLeftSeatHandle()`, `GetRightSeatHandle()`;
  `GameManager.EngineAudioController` (singleton: `carLoader`, `GetEngineStartingOrWorking()`, `CurrentRpm`,
  `IdleRpm`, `res` → `RealisticEngineSound` with `idleClip` etc.); `GameMode.GetCurrentMode()` (`gameMode.Interior`,
  `CarDrive`); `FPSInputController.ResetPosition()`; `PlayerData.IsDefault()`; `NotificationCenter.IsGameReady`;
  `SceneLoader.blockProgress`; `WindowID.TakenItems`, `TakenItemsWindow.BuyPartsAction()`;
  `SceneHelper.MapDestinationToSceneType(MapDestinationID)`.

## Goals / Non-Goals

**Goals:**
- One presence model (server record + client roster + avatar) that covers spawn, names, seat, engine, scene,
  late join and disconnect, instead of avatars that exist only as a side effect of movement packets.
- Travel that cannot corrupt the shared garage: away = not in the garage; return = late join into the garage.
- A server-validated path for cars obtained outside the garage.

**Non-Goals:**
- Sharing the world of non-garage scenes (same junkyard cars, joint auction, racing each other). Each client
  generates those scenes itself; making them shared would need seeded generation plus syncing every pick-up
  and is a different project.
- Driving: a car driven on a track or a car driving into/out of parking is not synced; only the walking avatar is.
- Selling cars, travel fees and dealer trade-ins (money paid or received through vanilla `AddPlayerMoney`
  outside this change's purchase window stays local and is overwritten by the next `WorldState`; see Risks).
- Parking contents and slot assignment (owned by `sync-car-placement-and-lifts`), garage car snapshot
  (`sync-car-parts`), persistence of names/positions (`session-persistence-and-rejoin`).
- A seated animation for avatars; v1 hides the body.

## Decisions

### D1. Spawn placement on the client, by player id
After a scene becomes playable (D5) and only if no restored position applies (vanilla `PlayerData.IsDefault()`
in the garage, and no `PlayerRestore` from row 7), the client computes slots around the spawn point it was given:
slot 0 = the spawn point, slots 1..7 = a ring of radius 1.2 m at 45° steps in the spawn's local frame. It starts
at slot `(playerId - 1) % 8` and takes the first slot where `Physics.CheckCapsule` (player capsule, same layer
mask as `Movement`'s ground raycast) is free and no avatar from the roster is within 0.8 m. The move disables
the `CharacterController`, sets the transform, re-enables it and then sends one movement packet (D4).
- *Why client-side:* the server has no scene geometry. Player id is unique among connected players, so two
  players that load at the same moment start at different slots even before they see each other.
- *Alternative:* server-assigned spawn coordinates per scene from a JSON table — rejected, needs per-scene data
  the server does not have and breaks with seasonal garage variants.
- This runs in every scene, so returning from the junkyard onto someone standing at the spawn also works.

### D2. Display name
`ConnectPacket.username` (field exists, no schema change) is filled from a MelonPreferences entry
`Together.PlayerName`; empty → Steam persona name when `MainMod.IsSteamAvailable`, else empty (the server then
uses `Player<id>`). The server sanitises and de-duplicates (spec) and stores it in the presence record.
Row 7 later persists it per identity.

### D3. Presence record on the server, roster on the client
- Core: `PlayerState` becomes `Dictionary<int, PlayerPresenceRecord>` with `PlayerId, Username, Scene,
  SeatCarLoaderId (-1 = none), SeatLeft, EngineCarLoaderId (-1), EngineRunning, EngineRpm, LastMovement
  (MovementPacket)`. Still `[NonSerialized]` in `ModGameState`; row 7 copies what it persists.
- Packets (appended to `PacketTypes`):
  - `PlayerPresence` (reliable): the record without `LastMovement`. Client → server carries only the sender's
    own scene/seat/engine; the server overwrites `PlayerId` and `Username`, stores, and relays the full record to
    all other synced clients.
  - `PlayerRoster` (reliable, server → joining client, during initial sync before `SyncEnd`): all other records
    plus each record's `LastMovement`.
  - `MovementPacket` gains `Scene` so a client can drop a movement that was sent from the previous scene.
- **Server stores:** presence record per connected client (name, scene, seat, engine, last movement).
  **Server relays:** movement (only to clients whose stored scene equals the sender's scene and is a visible
  scene — saves bandwidth and avoids drawing ghosts) and presence (to all synced clients).
  **Server decides:** name uniqueness, clearing seat/engine when scene changes or the player leaves, car purchases (D8).
- On `ConnectPacket` accepted: create the record (`Scene = Loading`), broadcast `PlayerPresence` to others.
  On `Client.Disconnect`: remove the record, cancel nothing else (no pending state exists, D8), broadcast the
  existing `DisconnectPacket`. `SteamTransport.OnDisconnected` calls `Client.Disconnect` for that connection.
- Server-internal events `PresenceEvents.SceneChanged(clientId, from, to)` and `PresenceEvents.Left(clientId)`
  for other modules (part claims of `sync-car-parts`, tool claims of `sync-workshop-tools`) to release claims.
- Client: `ClientData.Roster` (`Dictionary<int, RemotePlayer>`: record + last movement + avatar reference) is the
  single source; the avatar is derived: `PresenceManager.Reconcile(id)` creates the avatar when the player is
  visible (same scene, visible-scene table, transform known), destroys it otherwise (Unity-null-safe, because a
  scene unload may already have destroyed it), and hides the body while seated. Reconcile runs on every roster
  change and on local scene-ready. `ClientData.Reset()` destroys avatars and clears the roster; the
  `PlayerRoster` snapshot rebuilds it.
- *Alternative:* keep spawning on first movement and make idle clients send a keep-alive movement — rejected,
  it hides the bug instead of giving the joining client the state, and it does not carry name/scene/seat.

### D4. Movement source per scene
The local `CharacterMotor` is looked up again on every scene-ready (`Object.FindObjectOfType<CharacterMotor>()`)
and held by the presence manager; `Movement` uses it instead of `GameData.Instance.LocalPlayer`. On scene-ready
(after spawn placement) one movement packet is sent unconditionally. While seated, movement is not sent.

### D5. Scene tracking hooks
- Prefix on `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` (the coroutine overload the
  current `DisconnectHooks` patches; the 3-argument overload and `StartSelectSceneToLoad` end there — verified by
  a log task): if target is `Menu` → existing disconnect; otherwise send `PlayerPresence{Scene = Loading}`, mark
  the local scene `Loading`, end local seat/engine, and set `GameData.Instance = null` when leaving the garage
  (GameData becomes garage-scoped so stale garage objects cannot be touched).
- Scene-ready: `MainMod.OnSceneWasInitialized` starts a coroutine that waits until `NotificationCenter.IsGameReady`,
  `!SceneLoader.blockProgress` and `GameScript.Get() != null`; then maps `GameScript.Get().CurrentSceneType` to
  `GameScene`, runs spawn placement, sends presence + movement and reconciles avatars. In the garage the same
  steps run at the end of `CustomLoad`, after `SyncEnd` (the roster is known then).
- `GameScene` in Core mirrors the game's scene types 1:1 with explicit values plus `Unknown` and `Loading`
  (server stays free of game types). The visible-scene table lives in Core (`GameSceneInfo.ShowsAvatars`) so the
  server's relay filter and the client use the same rule.
- *Why not scene names:* names differ for seasonal garages (`garage`, `Christmas`, `Easter`, `Halloween`) and are
  only partly known; `CurrentSceneType` is the game's own classification.

### D6. Away from the garage = not in the garage; return = late join
Garage-bound client handlers (cars, parts, lifts, tools, `GarageState`) must drop packets while
`ClientData.LocalScene != Garage` or initial sync of the current garage load is unfinished. This change adds the
check helper `ClientScene.IsGarageReady` and applies it to the handlers that exist today
(`CarHandlers`, `WorldStatesPackets.HandleGarageState`); rows 1–5 use it for theirs. Returning runs the existing
`GarageLoader.Start` override → `AskForSync`, so the returning client gets the full snapshot — the same path as a
late join (row 1 makes it load cars from the snapshot instead of the local save).
- *Alternative:* FixForTogether's "Away/Return" (snapshot before leaving, diff the vanilla save on return, merge
  own purchases) — rejected: it exists because the old architecture had no authoritative state; here the server
  has it, so a full resync is simpler and cannot merge stale data.

### D7. Seat and engine (garage only)
- Prefix on `GameScript.SitInside(carLoader, left, mode)` records the pending seat (`CarLoaderPlaces.Get().GetCarLoaderId`);
  a 4 Hz poll publishes it once `GameMode.Get().GetCurrentMode()` is `Interior` (or `CarDrive`) and clears it when
  the mode leaves those; prefix on `GameScript.ExitFromInterior` clears it at once. Hooking a coroutine method only
  tells us it was started, hence the poll as confirmation.
- Engine: the same 4 Hz poll reads `Singleton<GameManager>.Instance.EngineAudioController`
  (`GetEngineStartingOrWorking()`, `carLoader`, `CurrentRpm`) and sends presence when running/car changes or rpm
  moves by more than 150. Polling instead of hooking `StartIgnition`/`EngineStop` because the controller is
  driven from several paths (interior, ignition key, dyno) and is a single shared singleton.
- Remote sound: the game's controller is a singleton playing the local engine, so it cannot also play a remote
  one. v1 adds one 3D looping `AudioSource` on the remote car (seat handle as anchor) using the local
  controller's `res.idleClip`, pitch = `rpm / IdleRpm` clamped to [0.5, 3]. If the clip is unavailable, the
  state is still shown in the dump and a single warning is logged.
- Car removed under a seated player: the client handler that removes a car calls
  `PresenceManager.EnsureNotSeatedIn(carLoaderId)` first (calls `ExitFromInterior(false)` if the local player sits
  there); this change wires it into today's `CarSpawnDelete` handler; row 2 calls it when moving cars.

### D8. Purchases outside the garage
- Parts: unchanged (`ItemsExchangePacket`, existing). This change only adds the harness check.
- Cars: optimistic local purchase + one server transaction.
  1. Prefix `GameScript.BuyCar(carLoader, buyPrice)` when connected and scene ≠ Garage: if local money < price,
     show `GUI_BrakKasy` and skip; else open a capture `{RequestId, Scene, Price}` and suppress
     `GlobalData.AddPlayerMoney` (prefix returns false while the capture is open).
  2. Postfix `GameDataManager.SaveCar(NewCarData, index, toParking)` while the capture is open converts the car to
     the parking DTO of `sync-car-placement-and-lifts` and sends `CarPurchase{RequestId, Scene, Price, Car}`.
     The capture closes there or after 5 s (logged as error; nothing is sent, no money moves).
  3. Server: if no parking state is registered → reject `Unavailable`; money < price → `NoMoney`; no free
     parking slot → `ParkingFull`; else `Money -= Price`, add car through row 2's parking API (server picks the
     slot), broadcast `WorldState` and row 2's parking update, answer `CarPurchaseResult{Accepted, Slot}`.
  4. On reject the client shows a message. The car vanished from the local scene and its local parking copy is
     irrelevant, because the garage/parking on return comes from the server (D6).
- *Why optimistic:* the vanilla buy flow is UI-driven and partly coroutine-based; blocking it and re-invoking
  after approval risks a second local money check failing against the already-debited money. With money
  suppression and server-side placement the optimistic path needs no rollback beyond a message.
- *Alternative:* block car purchases while connected — kept as the fallback when row 2 has not landed (the
  server answers `Unavailable`).
- Auction: whether winning an auction ends in `GameScript.BuyCar` is not visible in the stubs; a task verifies it
  with a log and, if not, adds the same capture around the auction's finishing method.

### D9. Late join (and return)
`OnAskForSync` sends, before `SyncEnd`, a `PlayerRoster` with every other connected player's record and last
movement (with row 7 this becomes one section of its ordered snapshot). The joining client stores it; when its
garage is ready (end of `CustomLoad`) it runs spawn placement against the roster, reconciles avatars (so idle
players appear immediately), and sends its own presence (`Garage`) and one movement packet, which the server
relays to the players already in the garage, who then create its avatar. Records of players in other scenes are
known but produce no avatar until scenes match.

## Risks / Trade-offs

- [Coroutine hooks fire on start, not completion] → seat is confirmed by game-mode polling; scene-ready by a
  separate readiness wait.
- [Remote engine sound clip unavailable or car-specific] → fall back to state-only and log; real per-car sound is
  a later improvement.
- [`SaveCar` not called by some purchase path (auction, dealer delivered straight to garage)] → verification task
  logs every `SaveCar`/`SaveCarInParking`/`SaveCarInGarage` call during each purchase type before implementing
  the capture; missing paths get their own capture or are refused while connected.
- [Money moved by vanilla outside the capture (travel fees, selling cars) stays local and is overwritten] → listed
  as a non-goal; noted in QUESTIONS for a later change.
- [Garage-bound packets arriving during `Loading` or before `SyncEnd` being dropped while the snapshot was taken
  earlier] → TCP order means the snapshot precedes later deltas; row 7's `SyncBegin…SyncAck` gating removes the
  remaining window. Until then a dropped delta after the snapshot is a known gap.
- [Two players sit in the same seat] → allowed, purely visual (each sits in their own copy of the car); both
  name tags show above the seat.
- [Visibility in scenes with local worlds] → a remote avatar in the junkyard can stand on a pile the local player
  does not have; accepted for co-presence. Barn is hidden because its layout varies per visit (assumption).
- [Spawn ring blocked in a tight spawn area] → falls back to the default spawn; the scenario checks the garage.

## Migration Plan

No persisted data changes (`PlayerState` is `[NonSerialized]`). `GameScene` values are renumbered; all clients and
the server must run the same build (enforced by the existing version check). Rollback = revert the change.

## Open Questions

Decisions taken without the user (recorded here; none changes the task list):
- Barn hides remote avatars (layout assumed to vary per visit); every other scene type shows them.
- Cars bought outside the garage always go to the shared parking, never straight into a garage car loader.
- Display name comes from a MelonPreferences entry, default Steam name; no in-game UI in this change.
- Seated avatars are hidden (name tag above the seat) rather than posed.
- Exact scene names for every destination are recorded by a task (only `garage`, `Junkyard`, `Barn`,
  `Auto_salon`, `Menu` are known from the old mod); the harness `travel` command needs them.
- Assumptions on other changes: `sync-car-placement-and-lifts` provides a server-side parking API
  (free-slot check, add car, broadcast) and a car DTO convertible from `NewCarData`; `sync-car-parts` makes the
  returning/late-joining garage load cars from the snapshot; `session-persistence-and-rejoin` persists name and
  last position and tells the client when a position is restored.
