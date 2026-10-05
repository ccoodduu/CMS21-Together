# Tasks

## 1. Core: records, packets, state

- [ ] 1.1 Turn `CarBodyPartUpdatePacket` and `CarSubPartUpdatePacket` into nested records: drop `[NetworkPacket]`, add `PartId`, `TunedID` (sub), `MountObjectData` (sub, `ModMountObjectData`), `Revision`; add `PartKeys.Body(int)` / `PartKeys.Sub(int[])` next to `CarSubPartIdentity`. Verify: solution builds.
- [ ] 1.2 Add `InventoryDelta` and the packets `CarPartsChange`, `CarPartsChangeResult`, `CarPartClaim`, `CarPartClaimUpdate`, `CarPartsSnapshot`, `CarPartsResyncRequest`, `CarSpawnAck`; replace the `CarBodyPartUpdate`/`CarSubPartUpdate` entries in `PacketTypes`; add `SpawnSeq` to `CarSpawnResponsePacket`. Verify: client and server start and `PacketRouter` registers every new type (startup log, no unknown-packet errors in the connect scenario).
- [ ] 1.3 Extend `CarState` with a per-loader entry (`Car` spawn info, `SpawnSeq`, `Revision`, `HasBaseline`, `SpawnedBy`) and a global `NextSpawnSeq`. Verify: a server save with a car round-trips through `SaveSession`/`TryLoadSession` with all fields intact.

## 2. Server: storage and arbitration

- [ ] 2.1 Add `GameDataManager.StateLock` and take it in `CarHandlers`, the list mutations of `InventoryHandlers` and the car part of `OnAskForSync`. Verify: connect scenario still passes.
- [ ] 2.2 Spawn/delete: assign `SpawnSeq`, record `SpawnedBy`, send `CarSpawnAck` to the spawner, put `SpawnSeq` in the relayed response; delete clears part records and claims of the loader. Verify with the new server console command from 2.7 after spawning and deleting a car.
- [ ] 2.3 Baseline: accept a client→server `CarPartsSnapshot` only from `SpawnedBy` for the current `SpawnSeq`, store records with `Revision = 1`, relay to the other clients; delete the car if the spawner disconnects before its baseline. Verify: server log and `cars` command show the part count after a spawn.
- [ ] 2.4 `CarPartsChange` handling per design D4 (SpawnSeq/baseline check, preconditions, consumed UIDs, apply delta to `InventoryState`, OR-merge examined, bump `Revision`, result to sender, relay to others). Verify: race scenario in 5.3 ends with one item and no error in the server log.
- [ ] 2.5 Claims: grant/deny, broadcast `CarPartClaimUpdate`, release on commit/cancel/disconnect, expire after 120 s. Verify: reservation steps of scenario 5.3.
- [ ] 2.6 `OnAskForSync`: send each car with a baseline as batched `CarPartsSnapshot`s (~100 records per batch) before `SyncEnd`; answer `CarPartsResyncRequest` the same way for one car; on save load drop cars without baseline and log it. Verify: late-join scenario 5.4.
- [ ] 2.7 Add a `cars` command to `CommandSystem` printing loader, car, `SpawnSeq`, `Revision`, part counts and active claims. Verify: run it on the test server.

## 3. Client: live part sync

- [ ] 3.1 Spike (logging only, removed afterwards): log the call order of the D2 hook methods and every `Inventory.Add/Delete/AddGroup/DeleteGroup` (ID, UID) while unmounting and mounting one door, one single mechanical part and one `unmountWith` group, and find which game call can drive a mount/unmount from a script (`FastUnmount`/`FastMount`, `TakeOffCarPart(string)`). Verify: findings written into design.md D3 (item IDs, event order) before 3.5 starts.
- [ ] 3.2 `PartRegistry` built after `IsCarLoaded()` (body by `carParts` index, mechanical by sibling-index path from the `CarLoaderOnCar` transform, including inactive objects), loader id via `CarLoaderPlaces.GetCarLoaderId`. Verify: harness `car-ready` reports the same registry count and hash on both clients for the same car.
- [ ] 3.3 Per-loader state machine (`Empty/Loading/AwaitingBaseline/Ready`) with an ordered queue that drops `SpawnSeq` mismatches and `Revision ≤` applied revision; handle `CarSpawnAck`; drop everything on `DeleteCar`. Verify: scenario 5.3 step "unmount while B loads".
- [ ] 3.4 Record applier per design D7 inside an `ApplyingRemote` scope that suppresses car, part and inventory hooks. Verify: after a baseline apply, both dumps match on body and sub parts.
- [ ] 3.5 Baseline upload after load + 1 s without part changes, and public `CarPartsSync.UploadBaseline(loaderId)` / `MarkDirty(loaderId, part)`. Verify: spawn in scenario 5.3 produces identical part dumps.
- [ ] 3.6 Hooks from the D2 table and the `PartChangeTracker` (stable for 3 polls, not in progress, build `CarPartsChange`). Verify: unmount/examine/switch steps of scenario 5.3.
- [ ] 3.7 `PartTransaction` and the `InventoryHook` hand-off (ID/UID matching, Delete+Add cancel-out, flush on cancel or 10 s idle). Verify: scenario 5.3 inventory sections match and contain exactly one item per unmounted part; mount-window cancel leaves the inventory unchanged.
- [ ] 3.8 `CarPartsChangeResult`: store revision on accept; on reject apply the returned records and revert the local delta with hooks suppressed. Verify: race step of scenario 5.3.
- [ ] 3.9 Client claims: send on action start/cancel, block actions and `CanTakeOffCarPart` for parts held by others with an info window, cancel own action when the server names another owner. Verify: reservation step of scenario 5.3.

## 4. Client: late join

- [ ] 4.1 In the connected `VanillaLoad` path call only `DeleteCar()` per loader, not `LoadCarFromFile()`. Verify: a client whose local save has a garage car joins an empty server and its dump shows no car.
- [ ] 4.2 Assemble snapshot batches per car, load the car through the existing suppressed `LoadCar` path, apply all records, go `Ready`, drain the queue. Verify: scenario 5.4.
- [ ] 4.3 `ClientData.IsCarsSynced`; `WaitForSyncCompletion` waits for it; `CustomLoad` timeout becomes "10 s without progress" while cars load. Verify: join with two loaded cars does not time out.
- [ ] 4.4 Send `CarPartsResyncRequest` when a key or id/name does not resolve and apply the answer without reloading the car when `carToLoad` matches. Verify: harness command `part-corrupt` (5.1) on B followed by a resync makes the dumps match again.

## 5. Harness and scenarios

- [ ] 5.1 `tools/TestHarness/Features/CarPartsCommands.cs`: `car-spawn <loader> <car> [config]`, `car-delete <loader>`, `car-ready <loader>` (state, registry count/hash, revision), `part-list <loader> [filter]` (keys of unblocked, mountable/unmountable parts), `part-unmount <loader> <key>`, `part-mount <loader> <key>` (uses the inventory item for that part; if 3.1 finds no scripted game path that consumes it, does `Inventory.Delete(item)` inside a `PartTransaction` then `FastMount()` and says so in the reply), `part-examine <loader> <key>`, `part-switch <loader> <bodyKey>`, `part-claim <loader> <key>` / `part-release`, `part-corrupt <loader> <key>` (local-only change for 4.4). Verify: each command returns a result on a single client.
- [ ] 5.2 `StateDump.Cars()`: add `spawnSeq`, `syncState`, `revision`, `registryHash`, body parts (`key`, `name`, `unmounted`, `switched`, `condition`, `dent`, `quality`, `tunedId`) and sub parts (`key`, `id`, `unmounted`, `condition`, `quality`, `examined`) sorted by key, and `claims`. Verify: connect scenario still passes with the larger dump.
- [ ] 5.3 Scenario `tools/test-env/scenarios/car-parts.ps1`: both connect; A deletes leftover cars; A `car-spawn`s on loader 0 and B is checked while still loading; both `car-ready`; compare `cars`; A unmounts a door and a mechanical part, B examines another part and opens the hood; compare `cars` + `inventory` and assert one item per unmounted part; B mounts the mechanical part back; compare; A `part-claim`s a part and B's `part-unmount` on it is refused, then A releases; A and B `part-unmount` the same part back-to-back and the result is one item and an unmounted part on both. Verify: `Run-Session.ps1 -Scenario car-parts` passes in two instances.
- [ ] 5.4 Late-join scenario `tools/test-env/scenarios/car-parts-latejoin.ps1`: only A connects, spawns a car, unmounts/examines parts; B connects afterwards, then A disconnects and B reconnects; after each join compare `cars` + `inventory` between A's last dump and B. Verify: `Run-Session.ps1 -Scenario car-parts-latejoin` passes in two instances, and `connect` still passes.
