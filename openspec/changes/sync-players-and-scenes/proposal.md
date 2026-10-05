# Proposal

## Why

Players can see each other move in the garage, but that is all: both players spawn on exactly the same spot
(test run: both at x=17.168, y=-0.1, z=17.317, so each camera sits inside the other's model), avatars have no
name, a player who sits in a car or starts its engine looks like they are standing still, and a player who
joins late does not see anyone who is standing still: `Movement.UpdateMovement` only sends on change and
avatars are only created on the first movement packet (harness run: A idle, B joins, B sees nobody).
Travelling is worse: nothing tracks which scene a player is in, so a player in the junkyard is still drawn in
the garage at their last position, movement packets hit avatars that the scene change destroyed, and cars
bought outside the garage are paid for locally only (the next `WorldState` gives the money back) and never
reach the other players.

## What Changes

Part 1 (M1: spawn, roster, names, scene tracking + away/return):
- **Spawn placement**: on every scene load the local player is moved to a free slot next to where the game
  spawned it (slot chosen by player id, skipping slots blocked by geometry or another avatar). A position restored
  by `session-persistence-and-rejoin` (`PlayerRestore`) is left alone.
- **Presence roster**: the server keeps one presence record per connected player (name, scene, seat, engine,
  last transform), sends the whole roster as snapshot section `players` (`SyncOrder` 500 of
  `session-persistence-and-rejoin`) and relays changes. Avatars are spawned from the roster, not from the first
  movement packet, so idle players are visible to late joiners; a client publishes its presence with its
  transform on the reliable channel when its scene becomes ready.
- **Names and name tags**: the name comes from a mod preference (default: Steam name, else `Player<id>`) and is
  sent in the existing `ConnectPacket.username`; the server makes it unique; remote avatars get a name tag.
- **Cleanup**: on disconnect (also Steam, also while away) the server drops the record and every client
  removes the avatar; seat and engine state end when a player travels or leaves.
- **Scene tracking and visibility**: each client reports `Loading` when travel starts and the scene type when
  it is ready; avatars are shown only to players in the same scene (Barn excluded); the server relays movement
  only within a scene.
- **Travel**: leaving the garage stops applying garage-bound updates; returning to the garage is a late join
  into the garage (full snapshot). Before travel starts, a `LeavingScene` event lets
  `sync-test-drive-and-diagnostics` send results produced away, so the return snapshot already contains them.
  Worlds outside the garage are generated locally and stay unshared.

Part 2 (M4):
- **Car seat and engine** (garage only): entering/leaving a seat and engine on/off/rpm are synced; a seated
  avatar is hidden and its name tag moves to the seat; a remote running engine is audible on that car.
- **Purchases outside the garage**: junkyard/barn parts keep the existing `ItemsExchange` path; a car bought in
  junkyard, barn, auction or dealer is sent through the shared parking API of `sync-car-placement-and-lifts`
  (`CarParkRequest` with `CarLoaderID = -1`, plus a price) so the server debits shared money once and adds the
  car to the shared parking. Travel fees and car sales stay with ROADMAP row 10 `economy-audit`.

Hooks and packets:
- Hooks: `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` (prefix, extends
  `DisconnectHooks`), `GameScript.SitInside` / `GameScript.ExitFromInterior` (prefix), `GameScript.BuyCar`
  (prefix/postfix), `GameDataManager.SaveCar(NewCarData, int, bool)` (postfix), `GlobalData.AddPlayerMoney`
  (prefix, only while a car purchase is captured); polled: `GameMode.GetCurrentMode()`,
  `GameManager.EngineAudioController` (`GetEngineStartingOrWorking`, `carLoader`, `CurrentRpm`),
  `GameScript.CurrentSceneType`; scene-ready via `MelonMod.OnSceneWasInitialized` + `NotificationCenter.IsGameReady`.
- Packets: **new** `PlayerPresence`, `PlayerRoster`; **used** `CarParkRequest`/`CarParkResult` of
  `sync-car-placement-and-lifts` (`RequestId`, `Price`); **changed** `MovementPacket` (+`Scene`), `GameScene` enum (all
  game scene types, explicit stable values), `PlayerState` (one record per player instead of parallel
  dictionaries).

## Capabilities

### New Capabilities
- `player-presence`: how connected players appear to each other: spawn placement, names and name tags, the
  presence roster (including late join), car seat and engine state, and cleanup when a player leaves.
- `scene-travel`: which scene each player is in, who is visible where, travelling while connected, and how
  parts and cars obtained outside the garage reach the shared inventory and parking.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `Data/Enum/GameScene.cs` (+ `GameSceneInfo`), `Data/PlayerState.cs`, `Network/Packets/PlayerPackets.cs`,
  `PacketTypes.cs` (entries appended).
- Server: `Network/Handlers/PlayerHandlers.cs`, `Network/Handlers/AuthHandlers.cs` (name), new `Data/Presence/`
  (`PlayersSnapshotProvider`, `PresenceEvents`, `PresenceRegistry`), `Network/Client.cs` / `Server.cs` (presence
  cleanup, scene-filtered relay; purchases use row 2's existing `CarLoaderID = -1` branch),
  `Network/Transport/SteamTransport.OnDisconnected`.
- Client: `Data/ClientData.cs`, `Data/GameData.cs` (garage-scoped), `Logic/PlayerInstance.cs`,
  `Logic/Player/Movement.cs`, new `Logic/Player/` presence manager, spawn placement, name tag, seat/engine
  tracker, `Logic/Hook/DisconnectHooks.cs` → scene hooks, new car purchase hooks, `Network/Handlers/PlayerHandlers.cs`,
  `AuthHandlers.cs` and `Network/Transport/ClientSteam.cs` (name in both `ConnectPacket` sends),
  `MainMod.OnSceneWasInitialized` (new override).
- Depends on the `session-persistence-and-rejoin` contract (groups 1–2: state lock, `ISnapshotProvider`, `SyncOrder`,
  pipeline, `SyncTracker`) for part 1;
  on `sync-car-parts` for cars in the return snapshot; on `sync-car-placement-and-lifts` (`CarParkRequest`/
  `CarParkResult`, `NewCarDataCodec`, `BeforeRemoteCarMove`) for car purchases and seated players. Row 7 persists names and last positions; row 13 uses `LeavingScene`.
  Test harness: `Features/PresenceCommands.cs`, `StateDump`, three scenarios.
