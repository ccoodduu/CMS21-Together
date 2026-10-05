# Design

## Context

See proposal.md for the motivation. Today's code (observed, `main`):

- `Movement.UpdateMovement` sends a `MovementPacket` at most 20 Hz and only on change; it reads
  `GameData.Instance.LocalPlayer`, which `LoaderAddition.CustomLoad` creates in the garage only. Outside the
  garage the reference points at a destroyed `CharacterMotor`, so nothing is sent. On DirectIP the server relays
  movement over UDP (`SendToClients(packet, id, reliable: false)`), so a single movement packet can be lost.
- `ClientData.Players` maps id → `PlayerInstance`; an avatar is created by `ClientData.SpawnPlayer` on the first
  movement packet. Avatars are scene objects (only `ModGameManager.PlayerPrefab` survives), so a scene change
  destroys them while the dictionary keeps the dead references. `PlayerInstance` already snaps beyond 3 m.
- Every garage load (first join and every return) runs `CustomLoad` → `ClientData.Reset()` → `AskForSync`.
  `GameData`'s constructor needs `GarageLevelManager`, so it cannot be built in other scenes.
- The server stores movement in `PlayerState` (parallel dictionaries, `[NonSerialized]`), never sends it to a
  joining client and never clears it. `ConnectPacket.username` exists but both send sites
  (`AuthHandler.HandleConnect` for DirectIP, `ClientSteam` for Steam) send `TestUser<id>` and the server ignores
  it. The server has no `Disconnect` packet handler: a leaving DirectIP client is noticed by the TCP close
  (`Tcp` → `Client.Disconnect`); `SteamTransport.OnDisconnected` does not call `Client.Disconnect`, so a Steam
  drop is only noticed by the 10 s heartbeat timeout. Server handlers run on socket threads; `Client.Disconnect`
  also runs from the timeout check. Client handlers run on the main thread (`ThreadManager`).
- Junkyard/barn parts already go through the server (`TakenItemsWindowHook` → `ItemsExchangePacket`, which then
  loads the garage with the 3-argument `SelectSceneToLoad`). Car purchases are not hooked; money is not hooked in
  general, so a local `AddPlayerMoney` is overwritten by the next `WorldState` (cars bought away are free and
  never shared).
- Game API (verified in the decompiled stubs): `SceneType` (None, Junkyard, Barn, Showroom, Garage, Auction,
  TestTrack, RaceTrack, Salon, Parking, Menu, FunTrack, OffroadTrack, DragStrip, CustomTrack, Tutorial,
  PhotoLocation, SpeedTrack); `NotificationCenter`: `IEnumerator SelectSceneToLoad(string, SceneType, bool, bool)`,
  `void SelectSceneToLoad(string, SceneType, bool)`, `void StartSelectSceneToLoad(string, SceneType, bool, bool)`,
  `static bool IsGameReady`; `GameScript`: `CurrentSceneType`, `carOnScene`, `IEnumerator SitInside(CarLoader,
  bool left, gameMode)`, `IEnumerator ExitFromInterior(bool openDoor = true)`, `void BuyCar(CarLoader, int)`,
  `void SellCar(CarLoader, int)`; `GameDataManager.SaveCar(NewCarData, int, bool toParking)`, `SaveCarInParking`,
  `SaveCarInGarage`; `GlobalData.AddPlayerMoney(int)` (static); `CarLoader.GetLeftSeatHandle()`,
  `GetRightSeatHandle()`; `CarLoaderPlaces.Get().GetCarLoaderId(CarLoader)`; `GameManager.EngineAudioController`
  (property: `carLoader`, `GetEngineStartingOrWorking()`, `CurrentRpm`, `IdleRpm`, `IEnumerator
  StartIgnition(bool, CarLoader, float)`, `EngineStop()`, `res` → `RealisticEngineSound.idleClip`);
  `GameMode.Get().GetCurrentMode()` (`gameMode.Interior`, `CarDrive`); `SceneLoader.blockProgress`;
  `TakenItemsWindow.Prepare()` / `BuyPartsAction()`; `SceneHelper.MapDestinationToSceneType(MapDestinationID)`;
  `MapWindow.SelectDestination(MapDestinationID)`.

## Goals / Non-Goals

**Goals:**
- One presence model (server record + client roster + avatar) that covers spawn, names, seat, engine, scene,
  late join and disconnect, instead of avatars that exist only as a side effect of movement packets.
- Travel that cannot corrupt the shared garage: away = not in the garage; return = late join into the garage.
- A server-validated path for cars obtained outside the garage, through the shared parking API.

**Non-Goals:**
- Sharing the world of non-garage scenes (same junkyard cars, joint auction, racing each other). Each client
  generates those scenes itself (backlog: shared non-garage worlds).
- Driving: a car driven on a track or into/out of parking is not synced; only the walking avatar is.
- Selling cars, travel fees, auction bids and dealer trade-ins: owned by ROADMAP row 10 `economy-audit`.
- Parking contents and slot allocation (`sync-car-placement-and-lifts`), garage car snapshot (`sync-car-parts`),
  persistence of names/positions (`session-persistence-and-rejoin`), car lights (`LightsOn`, see Open Questions).
- A seated animation for avatars; v1 hides the body.

## Decisions

### D1. Spawn placement on the client, by player id
After a scene becomes playable (D5), unless row 7's `PlayerRestore` was applied in this load, the client takes the
position vanilla placed it at as slot 0 and builds slots 1..8 as a ring of radius 1.2 m at 45° steps in that
spawn's local frame (9 slots). It starts at slot `(playerId - 1) % 9` and takes the first slot where
`Physics.CheckCapsule` (player capsule, `Movement`'s layer mask, `QueryTriggerInteraction.Ignore`, run with the
local `CharacterController` disabled so it does not block its own slot) is free and no roster avatar is within
0.8 m. The move keeps the controller disabled, sets the transform, re-enables it and then publishes presence (D3).
- *Why client-side:* the server has no scene geometry. Player id is unique among connected players, so two
  players that load at the same moment start at different slots even before they see each other.
- *Why not `PlayerData.IsDefault()`:* the multiplayer profile is a fresh slot; the only restore that matters in a
  session is row 7's `PlayerRestore`.
- *Alternative:* server-assigned spawn coordinates per scene from a JSON table — rejected, needs per-scene data
  the server does not have and breaks with seasonal garage variants.

### D2. Display name
`ConnectPacket.username` (field exists, no schema change) is filled at both send sites from a MelonPreferences
entry `Together.PlayerName`; empty → Steam persona name when `MainMod.IsSteamAvailable`, else empty (the server
then uses `Player<id>`). The server sanitises and de-duplicates (spec) and stores it in the presence record.
Row 7 persists it per identity.

### D3. Presence record on the server, roster on the client
- Core: `PlayerState` becomes `Dictionary<int, PlayerPresenceRecord>` with `PlayerId, Username, Scene,
  SeatCarLoaderId (-1 = none), SeatLeft, EngineCarLoaderId (-1), EngineRunning, EngineRpm, LastMovement
  (MovementPacket)`. Still `[NonSerialized]` in `ModGameState`; row 7 copies name, scene and last position into
  its `PlayerRecord`.
- Packets (appended to `PacketTypes`):
  - `PlayerPresence` (reliable): the full record. Client → server carries the sender's own scene/seat/engine and
    its current transform as `LastMovement`; the server overwrites `PlayerId` and `Username`, stores, and relays
    the full record to all other synced clients. Carrying the transform on the reliable channel is what makes an
    idle player visible: the avatar never depends on a (possibly lost) UDP movement packet.
  - `PlayerRoster` (reliable, server → joining client): all other records.
  - `MovementPacket` gains `Scene` so a client can drop a movement that was sent from the previous scene.
- **Server stores:** presence record per connected client (name, scene, seat, engine, last movement).
  **Server relays:** movement (only to clients whose stored scene equals `packet.Scene` and
  `GameSceneInfo.ShowsAvatars(scene)`) and presence (to all synced clients).
  **Server decides:** name uniqueness, clearing seat/engine when scene changes or the player leaves, the car
  purchase check (D8).
- All record access happens under row 7's `GameDataManager.StateLock`: row 7 holds it around dispatch,
  `Client.Disconnect` and `Client.Update` (which also run from transport threads and the timeout check). Removal is
  idempotent: the `DisconnectPacket` broadcast and `PresenceEvents.Left` happen only when a record was removed.
- On `ConnectPacket` accepted: create the record (`Scene = Loading`), broadcast `PlayerPresence` to others.
  On `Client.Disconnect`: remove the record, broadcast the existing `DisconnectPacket`.
  `SteamTransport.OnDisconnected` calls `Client.Disconnect` for the client owning that connection.
- The `PlayerPresence` handler is `[AllowBeforeSync]` (row 7): a joining client publishes `Garage` right after
  `SyncEnd`, possibly before its `SyncAck`; presence is not shared game state.
- Server-internal events `PresenceEvents.SceneChanged(clientId, from, to)` and `PresenceEvents.Left(clientId)` let
  other modules release claims when the holder leaves the garage or disconnects (rows 1, 5, 13 consume them) and
  let `sync-orders-and-jobs` elect its order generator among clients whose scene is `Garage`
  (`PresenceRegistry.InScene(Garage)`). Raised under the state lock.
- Client: `ClientData.Roster` (`Dictionary<int, RemotePlayer>`: record + avatar reference) is the single source;
  the avatar is derived: `PresenceManager.Reconcile(id)` creates the avatar when the player is visible (same
  scene, `ShowsAvatars`, transform known), destroys it otherwise (Unity-null-safe, because a scene unload may
  already have destroyed it), and hides the body while seated. Reconcile runs on every roster change and on local
  scene-ready. `ClientData.Reset()` destroys avatars and clears the roster; the `PlayerRoster` snapshot rebuilds it.
- *Alternative:* keep spawning on first movement and make idle clients send a keep-alive movement — rejected,
  it hides the bug instead of giving the joining client the state, and it does not carry name/scene/seat.

### D4. Movement source per scene
The local `CharacterMotor` is looked up again on every scene-ready (`Object.FindObjectOfType<CharacterMotor>()`)
and held by the presence manager; `Movement` uses it instead of `GameData.Instance.LocalPlayer`. On scene-ready
(after spawn placement) `Movement.ForceSend()` resets the change tracking and sends once. While seated or while the
local scene is `Loading`, movement is not sent.

### D5. Scene tracking hooks
- Prefix on `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` (the coroutine overload the
  current `DisconnectHooks` patches; whether the 3-argument overload and `StartSelectSceneToLoad` reach it is
  checked by a log task): if target is `Menu` → existing disconnect; otherwise raise
  `ClientScene.LeavingScene(from, to)` (D6), send `PlayerPresence{Scene = Loading}`, mark the local scene `Loading`, end local seat/engine, and clear `GameData` (new
  `GameData.Clear()`) when leaving the garage, so stale garage objects cannot be touched. No-op when not connected.
- Scene-ready: new `MainMod.OnSceneWasInitialized` override starts a coroutine for non-garage scenes that waits until
  `NotificationCenter.IsGameReady`, `!SceneLoader.blockProgress` and `GameScript.Get() != null`; then maps
  `GameScript.Get().CurrentSceneType` to `GameScene`, runs spawn placement, publishes presence (with transform)
  and reconciles avatars. The garage runs the same steps at the end of `CustomLoad`, after `SyncEnd`.
- `GameScene` in Core mirrors the game's scene types with explicit, stable values plus `Unknown` and `Loading`
  (server stays free of game types; row 7 persists the value). `GameSceneInfo.ShowsAvatars` lives in Core so the
  server's relay filter and the client use the same rule.
- *Why not scene names:* names are only partly known (and seasonal garages exist in single player, although
  multiplayer always loads `garage`); `CurrentSceneType` is the game's own classification. Scene names are only needed by the
  harness `travel` command, which tries the map's own path first (`MapWindow.SelectDestination`) and falls back to
  a name table recorded by a task.

### D6. Away from the garage = not in the garage; return = late join
Garage-bound client handlers (cars, parts, lifts, tools, `GarageState`) must not touch scene objects while
`ClientData.LocalScene != Garage` or initial sync of the current garage load is unfinished. While the initial
sync of a garage load is running, live packets are QUEUED and applied in order after the snapshot (row 7 sends
live changes right after `SyncEnd`, so dropping them would lose updates). Only while the player is away from the
garage do handlers drop the packet or only update a data mirror (row 2's parking slots in `ProfileData`, row 5's `ClientToolsState`); the
return snapshot overwrites both. This change adds the check helper `ClientScene.IsGarageReady` and applies it to
the handlers that exist today (`CarHandlers`, `WorldStatesPackets.HandleGarageState`); rows 1–5 use it for theirs.
Returning runs the existing `GarageLoader.Start` override → `AskForSync`, so the returning client gets the full
snapshot — the same path as a late join (row 1 makes it load cars from the snapshot instead of the local save;
row 7 gives it a new `snapshotId`).
- *Alternative:* FixForTogether's "Away/Return" (snapshot before leaving, diff the vanilla save on return, merge
  own purchases) — rejected: it exists because the old architecture had no authoritative state; here the server
  has it, so a full resync is simpler and cannot merge stale data.
- **Results produced away (dependency on ROADMAP row 13 `sync-test-drive-and-diagnostics`):** a full snapshot
  on return would discard what a test drive / test path found (upstream #18/#83/#85/#95). Chosen: *send before
  leaving*, not *merge on return*. The scene prefix (D5) raises the client event `ClientScene.LeavingScene(from,
  to)` while the scene being left is still loaded, before it publishes `Loading`. Row 13 subscribes and sends its
  results (examined parts, mileage, …) for its claimed car on the reliable channel. The return `AskForSync` is
  sent later on the same ordered stream, and the server handles packets in order under `StateLock`, so the
  snapshot already contains the results. *Rejected:* merging local results into the snapshot on the client —
  it needs per-field merge rules in every row and reintroduces the stale-merge problem above. This change only
  provides the event and the ordering; what is sent, and the car claim while away, are row 13's.

### D7. Seat and engine (garage only)
- Prefix on `GameScript.SitInside(carLoader, left, mode)` records the pending seat (`CarLoaderPlaces.Get().GetCarLoaderId`);
  a 4 Hz poll publishes it once `GameMode.Get().GetCurrentMode()` is `Interior` (or `CarDrive`) and clears it when
  the mode leaves those; prefix on `GameScript.ExitFromInterior` clears it at once. Both are coroutine methods:
  the prefix only tells us the coroutine was created, hence the poll as confirmation.
- Engine: the same 4 Hz poll reads `Singleton<GameManager>.Instance.EngineAudioController`
  (`GetEngineStartingOrWorking()`, `carLoader`, `CurrentRpm`) and sends presence when running/car changes or rpm
  moves by more than 150. Polling instead of hooking `StartIgnition`/`EngineStop` because the controller is
  driven from several paths (interior, ignition key, dyno).
- Remote sound: the game's controller plays only the local engine. v1 adds one 3D looping `AudioSource` on the
  remote car (seat handle as anchor) using the local controller's `res.idleClip`, pitch = `rpm / IdleRpm` clamped
  to [0.5, 3]. If the clip is unavailable, the state is still shown in the dump and one warning is logged.
- Car removed under a seated player: the client handler that removes a car calls
  `PresenceManager.EnsureNotSeatedIn(carLoaderId)` first (starts `GameScript.Get().ExitFromInterior(false)` as a
  coroutine and waits for the game mode to leave `Interior` if the local player sits there); this change wires it
  into today's `CarSpawnDelete` handler; row 2 calls it when moving cars.

### D8. Cars bought outside the garage (through the shared parking API)
User decision: a car bought outside the garage always goes to the shared parking. Row 2 owns the parking lot and
its `CarParkRequest { CarLoaderID, PreferredSlot, Car }` with `CarLoaderID = -1` for cars arriving from outside
(ROADMAP integration note); `Car` is the opaque `NewCarData` blob made by row 2's `NewCarDataCodec`.
1. Prefix `GameScript.BuyCar(carLoader, buyPrice)` when connected and scene ≠ Garage: open a capture
   `{RequestId, Price}` and suppress `GlobalData.AddPlayerMoney` while it is open (the server's `WorldState` sets
   the money).
2. Postfix `GameDataManager.SaveCar(NewCarData, index, toParking)` while the capture is open: encode the car with
   `NewCarDataCodec`, put the vanilla-chosen local slot back to empty (the server picks the slot), and send
   `CarParkRequest { CarLoaderID = -1, PreferredSlot = -1, Car, Price, RequestId }`. The capture closes there or
   after 5 s (logged as error; nothing is sent, no money moves). `toParking = false` is treated the same.
3. Server (the `CarLoaderID = -1` branch of row 2's park handler, under `StateLock`): money < `Price` → `NoMoney`;
   no free slot → `ParkingFull`; else `Money -= Price`, store the car in the slot row 2's allocation picks,
   broadcast `WorldState` and `ParkingSlotUpdate`. The requester always gets `CarPurchaseResult{RequestId,
   Accepted, Reason, Slot}`.
4. On reject the client shows a message. The car is gone from the local scene and no local copy remains.
- This change adds `Price` and `RequestId` (0 = not a purchase) to `CarParkRequest` and the new
  `CarPurchaseResult`; whichever of rows 2/3 lands first implements the `-1` branch itself (row 3 uses it with
  `Price = 0`).
- *Why optimistic:* the vanilla buy flow is UI-driven and partly coroutine-based; blocking it and re-invoking
  after approval risks a second local money check failing against the already-debited money.
- Auction: whether winning an auction ends in `GameScript.BuyCar` is not visible in the stubs; a task verifies it
  with a log and, if not, adds the same capture around the auction's finishing method. Per-bid money stays with
  row 10.

### D9. Late join (and return)
The roster is row 7's snapshot slot `players` (`SyncOrder` 500, after `self` 450 = own position):
`PlayersSnapshotProvider : ISnapshotProvider` (snapshot only, no save section) sends one `PlayerRoster` with every
other connected player's record under `StateLock` and returns `1` from `SendSnapshot`; the client's roster handler
calls `SyncTracker.Applied("players")` once the roster is stored, so `SyncEnd.Items["players"] = 1` is met. Row 7's
pipeline (its contract groups 1–2) lands before this change, so there is no direct send from `OnAskForSync`.
What row 7 persists per player comes from the presence record: name, scene and `LastMovement` position/rotation,
copied on leave and before each save; seat and engine are not saved (they are cleared on leave). The joining client
stores the roster; when its
garage is ready (end of `CustomLoad`) it runs spawn placement against it (skipped if `PlayerRestore` was applied),
reconciles avatars (so idle players appear immediately), and publishes its own presence (`Garage` + transform),
which the server relays to the players already there, who then create its avatar. Records of players in other
scenes are known but produce no avatar until scenes match.

## Risks / Trade-offs

- [Coroutine hooks fire on start, not completion] → seat is confirmed by game-mode polling; scene-ready by a
  separate readiness wait.
- [Remote engine sound clip unavailable or car-specific] → fall back to state-only and log.
- [`SaveCar` not called by some purchase path (auction, dealer delivered straight to garage), or inlined by
  IL2CPP] → a verification task logs every `SaveCar`/`SaveCarInParking`/`SaveCarInGarage` and
  `AddPlayerMoney` call during each purchase type before implementing the capture; missing paths get their own
  capture or are refused while connected.
- [Money moved by vanilla outside the capture (travel fees, selling cars)] → stays local until the next
  `WorldState`; owned by ROADMAP row 10 `economy-audit`.
- [Garage-bound packets arriving during `Loading` or before `SyncEnd` being dropped] → row 7's per-load snapshot
  (`snapshotId`) and row 1's revision queue cover the window; until row 7 lands it is a known gap.
- [Two players sit in the same seat] → allowed, purely visual; both name tags show above the seat.
- [Visibility in scenes with local worlds] → a remote avatar in the junkyard can stand on a pile the local player
  does not have; accepted for co-presence. Barn is hidden because its layout varies per visit (assumption).
- [Spawn ring blocked in a tight spawn area] → falls back to the vanilla spawn; the scenario checks the garage.

## Migration Plan

No persisted data changes in this change (`PlayerState` is `[NonSerialized]`). `GameScene` gets new explicit
values; all clients and the server must run the same build (existing version check). The values are stable from
here on because row 7 persists them. Rollback = revert the change.

## Open Questions

Decisions taken without the user (recorded here; none changes the task list):
- Barn hides remote avatars (layout assumed to vary per visit); every other non-menu scene type shows them.
- Display name comes from a MelonPreferences entry, default Steam name; in-game UI is ROADMAP row 8.
- Seated avatars are hidden (name tag above the seat) rather than posed.
- `LightsOn` (offered by `sync-car-details` A5) is car state, not presence; this change does not take it.
- Results produced away from the garage reach the server before the return snapshot by row 13 sending them on
  `ClientScene.LeavingScene` (D6); dyno results are row 13's.
- Assumptions on other changes: `sync-car-placement-and-lifts` provides the `CarParkRequest` slot allocation and
  `NewCarDataCodec`; `sync-car-parts` makes the returning/late-joining garage load cars from the snapshot;
  `session-persistence-and-rejoin` provides `StateLock`, `ISnapshotProvider`, `SyncOrder`, `[AllowBeforeSync]`,
  `SyncTracker` and `PlayerRestore`, and persists name and last position from the presence record.
