# Tasks

## 1. Quick win: spawn placement

- [ ] 1.1 Add `Logic/Player/SpawnPlacement.cs` (client): slot ring around the current spawn (slot 0 + 8 × 1.2 m), start slot `(playerId-1) % 8`, skip slots failing `Physics.CheckCapsule` or within 0.8 m of a remote avatar, move with `CharacterController` disabled; verify by the log line `spawn slot N at (x,y,z)` on both clients in a two-client run (slots differ)
- [ ] 1.2 Call it at the end of `LoaderAddition.CustomLoad` (after sync, only when `PlayerData.IsDefault()`), then force one `MovementPacket` (`Movement.ForceSend()`); verify with `connect.ps1`: local positions of A and B differ by ≥ 0.8 m (dump field from 6.2)
- [ ] 1.3 Add harness `teleport x,y,z[,yaw]` and `local-position` dump field early (see 6.1/6.2) so 1.2 can be verified; verify `teleport` moves the player and B's dump shows A's avatar within 0.3 m of the target after 1 s

## 2. Core: presence model and packets

- [ ] 2.1 Rewrite `Data/Enum/GameScene.cs`: `Unknown, Loading, Menu, Garage, Parking, Junkyard, Barn, Auction, Salon, Showroom, TestTrack, RaceTrack, FunTrack, OffroadTrack, DragStrip, CustomTrack, SpeedTrack, PhotoLocation, Tutorial` with explicit values, plus `GameSceneInfo.ShowsAvatars(GameScene)` (false for Unknown, Loading, Menu, Tutorial, Barn); verify the solution builds
- [ ] 2.2 Replace `Data/PlayerState.cs` dictionaries with `Dictionary<int, PlayerPresenceRecord>` (`PlayerId, Username, Scene, SeatCarLoaderId, SeatLeft, EngineCarLoaderId, EngineRunning, EngineRpm, LastMovement`); fix `Server/Handlers/PlayerHandlers.cs` to use it; verify build
- [ ] 2.3 Add packets in new `Network/Packets/ScenePackets.cs` / `PlayerPackets.cs`: `PlayerPresencePacket`, `PlayerRosterPacket` (records + last movements), `CarPurchasePacket` (`RequestId, Scene, Price, Car` — car DTO from `sync-car-placement-and-lifts`, `object`-free), `CarPurchaseResultPacket` (`RequestId, Accepted, Reason, Slot`); add `Scene` to `MovementPacket`; append the four `PacketTypes`; verify `PacketRouter.Initialize` registers them (startup log, no duplicate-id error)

## 3. Server: roster, relay, cleanup

- [ ] 3.1 On accepted `ConnectPacket` (`AuthHandlers.OnConnected`): sanitise and de-duplicate `username` (trim 24, empty → `Player<id>`, ` (2)` suffix), create the record with `Scene = Loading`, broadcast `PlayerPresence` to the other clients; verify server log `player 2 'Bob (2)' joined` with two clients named Bob
- [ ] 3.2 `[PacketHandler(PlayerPresence)]`: store scene/seat/engine from the sender, force seat/engine to none when scene ≠ Garage, raise `PresenceEvents.SceneChanged` on scene change, relay the full record to all other clients; verify with the harness scene scenario (8.2) roster fields
- [ ] 3.3 `OnMovementUpdate`: store `LastMovement`, relay only to clients whose record has the same scene and `ShowsAvatars(scene)`; verify a client in another scene receives no movement packets (debug counter in dump, 6.2)
- [ ] 3.4 `OnAskForSync`: send `PlayerRosterPacket` of all other connected players before `SyncEnd`; verify scenario 8.3 (idle late join)
- [ ] 3.5 `Client.Disconnect`: remove the record, raise `PresenceEvents.Left`; `SteamTransport.OnDisconnected` calls `Disconnect` for the matching client; verify by server log that a Steam drop is handled without waiting for `CONNECTION_TIMEOUT` (manual Steam test, noted in STATUS.md)

## 4. Client: roster, avatars, names, scenes

- [ ] 4.1 Name: MelonPreferences entry `Together.PlayerName` (default Steam persona name if `MainMod.IsSteamAvailable`), sent in `ConnectPacket.username` instead of `TestUser<id>`; verify server log shows the configured name
- [ ] 4.2 `ClientData.Roster` + `Logic/Player/PresenceManager.cs` with `Reconcile(id)` (create/destroy/hide avatar per D3, Unity-null-safe), handlers for `PlayerPresence`, `PlayerRoster`, `Movement` (drop when `packet.Scene` ≠ roster scene; store transform otherwise), `Disconnect` (destroy + remove); `ClientData.Reset()` destroys avatars; remove `ClientData.SpawnPlayer`; verify `connect.ps1` still passes
- [ ] 4.3 Add `Logic/Player/NameTag.cs` (`[RegisterTypeInIl2Cpp]`, `TextMesh` + outline above the head bone, faces `Camera.main`), attached by `Reconcile`; verify by screenshot in scenario 8.1 that `Bob` is readable above the avatar
- [ ] 4.4 Scene hooks: extend `DisconnectHooks` prefix on `NotificationCenter.SelectSceneToLoad(string, SceneType, bool, bool)` into `SceneHooks` (Menu → disconnect as today; else send `Loading`, end seat/engine, clear `GameData` instance when leaving the garage); first log every call with `newSceneName`/`sceneType` and travel once to each map destination by hand to record scene names and confirm the 3-argument overload ends here; write the name table into `PresenceCommands` (6.1)
- [ ] 4.5 Scene-ready: `MainMod.OnSceneWasInitialized` coroutine (wait `NotificationCenter.IsGameReady`, `!SceneLoader.blockProgress`, `GameScript.Get()`) for non-garage scenes, end of `CustomLoad` for the garage: map `CurrentSceneType` → `GameScene`, find local `CharacterMotor`, spawn placement, send presence + forced movement, reconcile all avatars; `Movement` uses the presence manager's motor; verify scenario 8.2 junkyard step (both see each other there)
- [ ] 4.6 `ClientScene.IsGarageReady` helper; guard `CarHandlers` (`CarSpawnResponse`, `CarSpawnDelete`, `CarSpawnRejected`) and `HandleGarageState`; verify scenario 8.2: B spawns/deletes a car while A is in the junkyard, A's log has no exception, and after A returns both car dumps are equal (requires `sync-car-parts` replay; until then note the expected difference in the scenario result)

## 5. Client: seat, engine, purchases

- [ ] 5.1 Seat: prefix `GameScript.SitInside` (pending seat via `CarLoaderPlaces.Get().GetCarLoaderId`), prefix `GameScript.ExitFromInterior` (clear), 4 Hz poll of `GameMode.Get().GetCurrentMode()` to confirm/clear; garage only; remote: hide body, name tag above `GetLeftSeatHandle()`/`GetRightSeatHandle()`; verify scenario 8.1 seat step
- [ ] 5.2 Engine: 4 Hz poll of `GameManager.EngineAudioController` (`GetEngineStartingOrWorking`, `carLoader`, `CurrentRpm`), send on change / rpm Δ > 150; remote `AudioSource` with `res.idleClip` on the remote car, pitch from rpm; verify scenario 8.1 engine step (`remoteEngines` dump shows running + `audioPlaying`)
- [ ] 5.3 `PresenceManager.EnsureNotSeatedIn(carLoaderId)` and call it from the `CarSpawnDelete` handler before deleting; verify scenario 8.1: B deletes the car A sits in, A's dump shows `seat = -1` and no exception
- [ ] 5.4 Verify purchase paths before coding the capture: log `GameScript.BuyCar`, `GameDataManager.SaveCar/SaveCarInParking/SaveCarInGarage` while buying one car each in junkyard, barn, dealer and auction (manual run); record in design.md whether each path goes through `BuyCar` + `SaveCar`
- [ ] 5.5 Car purchase capture: prefix/postfix `GameScript.BuyCar`, postfix `GameDataManager.SaveCar`, prefix `GlobalData.AddPlayerMoney` (blocked while capture open), 5 s timeout, `CarPurchase` send, `CarPurchaseResult` handler with on-screen message on reject; add the auction capture if 5.4 shows it needs one; verify scenario 8.2 purchase step
- [ ] 5.6 Server `Handlers/CarPurchaseHandlers.cs`: reject `Unavailable` without row 2's parking API, `NoMoney`, `ParkingFull`; on accept debit, add via parking API, broadcast `WorldState` + parking update, answer result; verify scenario 8.2 (money drops once on both, reject path with insufficient money)

## 6. Harness commands and dump

- [ ] 6.1 `tools/TestHarness/Features/PresenceCommands.cs`: `set-name <name>` (preference, before connect), `teleport x,y,z[,yaw]`, `travel <MapDestinationID>` (`SceneHelper.MapDestinationToSceneType` + scene-name table → `NotificationCenter.SelectSceneToLoad(name, type, true, false)`), `sit <carLoaderId> left|right` (`GameScript.SitInside` coroutine), `stand` (`ExitFromInterior(true)`), `engine on|off` (`EngineAudioController.StartIgnition`/`EngineStop` on the seated car), `buy-car-here <index> <price>` (`GameScript.BuyCar(carOnScene[index], price)`), `junk-buy <itemId> <count>` (add `Item`s to `TempInventory`, `TakenItemsWindow.Prepare()` + `BuyPartsAction()`); reuse `sync-car-parts`' car-loading command or add `load-car <loaderId> <carId>` in `Features/CarCommands.cs` if it does not exist; verify each verb answers without error in a manual harness session
- [ ] 6.2 `StateDump`: add `local` (`position`, `scene` as `GameScene`, `seat`, `engine`, `name`), `roster` (per id: `name`, `scene`, `seat`, `engineRunning`, `avatarActive`, `avatarPosition`, `nameTag`), `remoteEngines` (`carLoaderId`, `audioPlaying`), `movementReceivedFromOtherScenes`; keep `players`/`remotePlayers` meaning "visible avatars" so `connect.ps1` stays valid; verify `dump` output contains the fields
- [ ] 6.3 `HarnessClient.psm1`: helper `Wait-HarnessDump -Condition` (poll `dump` until a condition holds, with timeout) for roster/scene checks; verify it is used by 8.x

## 7. Documentation

- [ ] 7.1 README: player name preference, who is visible where, that non-garage scenes are not shared, and car purchases going to the shared parking; verify the text matches the specs

## 8. Harness scenarios (two instances)

- [ ] 8.1 `scenarios/presence.ps1`: both connect (set names `Ann`/`Bob`); assert local positions ≥ 0.8 m apart and each sees the other's avatar with the right name; A teleports, B sees A within 0.3 m; load a car on loader 0, A `sit 0 left`, B sees seat 0/left and hidden body; A `engine on`, B sees running + audio; B deletes the car, A is unseated; A disconnects, B's roster is empty and no avatar remains; passes when all checks hold
- [ ] 8.2 `scenarios/scenes.ps1`: both in the garage; A `travel Junkyard`: B sees A `Loading` then `Junkyard`, no avatar; B travels too: both see each other in the junkyard; A `junk-buy` 3 parts → A back in garage, money equal on both, inventories equal once B is back; A `buy-car-here 0 <price>` in the junkyard: money dropped by exactly the price on both and the car is in the shared parking (or rejected `Unavailable` without row 2, money unchanged); both return, garage dumps (`stats`, `inventory`, `cars`) equal; passes when all checks hold
- [ ] 8.3 `scenarios/presence-latejoin.ps1` (idle late join, the reported bug; no manual input, `StartupSkipper` handles the start screen): A connects and does not move; B connects; without either moving, both dumps show the other's avatar (`remotePlayers` ≥ 1 on both) at the other's local position (±0.3 m) and the local positions are ≥ 0.8 m apart; then B disconnects, A travels to the junkyard, B reconnects: B's roster shows A in `Junkyard` and no avatar; A returns to the garage: both see each other; A travels to the junkyard again and disconnects there: B's roster is empty; passes when all checks hold
- [ ] 8.4 Run `Run-Session.ps1 -Scenario presence`, `scenes` and `presence-latejoin` (and the existing `connect`) and verify all pass in two instances; record results in STATUS.md
