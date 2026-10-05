# Proposal

## Why

Cars can be spawned and deleted together, but nothing that happens to a car afterwards is shared: when one
player takes off a door or an oil filter, the other players still see it mounted while the removed item already
shows up in the shared inventory (so the same part can be taken off twice), and a player who joins later sees the cars from their own
local save (or none at all, since initial sync sends no car data). Every later feature (lifts, jobs, wheels,
tools) builds on parts being in the same state on every client, so this is roadmap row 1.

## What Changes

- Every client keeps a stable key for each part of a loaded car: body parts (`CarPart`) by their index in
  `CarLoader.carParts`, mechanical parts (`PartScript`) by a child-index path from the car root captured right
  after the car loads (the `PartIndex` / `PartIndexPath` already in the DTOs).
- Mount and unmount of body parts and mechanical parts are sent to the server as one transaction together with
  the inventory change they cause (item added on unmount, item consumed on mount). The server accepts the
  transaction only if the part is still in the expected state and the consumed item still exists, then stores
  it and relays it; otherwise the sender is told to roll back.
- Part attributes that change on the car are synced too: condition, quality, dent, examined, door/hood open
  (`Switched`), tuned id. Bolt condition is carried with the part; bolt-by-bolt animation is not.
- "In progress" is shared: when a player starts mounting or unmounting a part, the server records a claim and
  the other clients refuse to start an action on that part until the claim ends.
- After a car spawns, the spawning client uploads a full baseline of the car's part states, so the server has a
  complete picture of every loaded car.
- Initial sync sends every loaded car with all its part states; a joining client loads those cars instead of
  the cars from its local save.
- Hooks (all verified in the decompiled game): `PartScript.ActionUnMount`, `ActionMount(bool)`, `FastUnmount`,
  `FastMount`, `Hide`, `DoMount`, `Examine(bool)`, `UndoMounting`, `UndoUnMounting`, `CancelUnmountAnim`;
  `CarLoader.TakeOffCarPart(string, bool)`, `TakeOffCarPart(string)`, `CanTakeOffCarPart`, `SwitchCarPart(string)`,
  `SwitchCarPart(string, bool)`, `ExamineAllParts`; `ChoosePartUpWindow.BackAllItems`; plus the existing
  `Inventory.Add/Delete/AddGroup/DeleteGroup` hooks, which learn to hand their event to an open part transaction.
- Packets: **new** `CarPartsChange`, `CarPartsChangeResult`, `CarPartClaim`, `CarPartClaimUpdate`,
  `CarPartsSnapshot`, `CarPartsResyncRequest`; **changed** `CarBodyPartUpdatePacket` and `CarSubPartUpdatePacket`
  become the per-part state records carried inside those packets (fields added: `PartId`, `TunedID`,
  `MountObjectData`, `Revision`; no longer sent on their own), `CarSpawnResponsePacket` gains `SpawnSeq`, and a new `CarSpawnAck`
  tells the spawning client its `SpawnSeq`. All clients and the server must run the same build (enforced by the
  existing version check).

## Capabilities

### New Capabilities
- `car-parts-sync`: shared mount/unmount and state of body and mechanical parts on cars in the garage,
  conflict handling between players, and replay of loaded cars and their parts to a joining client.

### Modified Capabilities
<!-- none: openspec/specs/ is empty -->

## Impact

- Core: `Network/Packets/CarPackets.cs`, `PacketTypes.cs`, `Data/ModGameState.cs` (`CarState`).
- Server: `Network/Handlers/CarHandlers.cs`, new `CarPartHandlers.cs`, `AuthHandlers.OnAskForSync`, inventory
  state mutation shared with `InventoryHandlers`, claim cleanup on disconnect.
- Client: new `Logic/Car/` part registry, change tracker and applier; new hook classes; `InventoryHook`;
  `Network/Handlers/CarHandlers.cs`; `LoaderAddition.VanillaLoad` stops loading cars from the local save when
  connected; `WorldStatesPackets.WaitForSyncCompletion` waits for cars.
- Test harness: `tools/TestHarness/Features/CarPartsCommands.cs`, car part fields in `StateDump`, scenarios
  `car-parts.ps1` and `car-parts-latejoin.ps1`.
- Server save grows by the part state of loaded cars (save format/versioning stays with
  `session-persistence-and-rejoin`).
