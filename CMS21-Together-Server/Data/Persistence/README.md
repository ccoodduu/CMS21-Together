# Session persistence and sync contract

Every piece of shared server state plugs in here, once, as a *section*. A section is saved in the server save
file and/or sent to a joining client as part of the snapshot. Design: `openspec/changes/session-persistence-and-rejoin/design.md` (D1–D5).

## Interfaces

```csharp
public interface ISaveSection {
    string Key { get; }
    int Version { get; }
    JToken Save();                                // StateLock held
    void Load(JToken data);                       // already migrated to Version
    void Reset();                                 // new session, or section absent from the file
    JToken Migrate(JToken data, int fromVersion); // one step: fromVersion -> fromVersion + 1
}

public interface ISnapshotProvider {
    string Key { get; }
    int SyncOrder { get; }                        // constants in Core/Data/SyncOrder.cs
    int SendSnapshot(int clientId);               // StateLock held; Server.SendToClient only; returns item count
}
```

Mark the class `[SessionSection]`. `SessionRegistry.Initialize` finds it by reflection at server start; a duplicate
key stops the server. A class may implement either interface or both. Sections are thin adapters: handlers keep
using `GameDataManager.CurrentState.X`, the section only moves that object in and out of JSON.

## Sync order

| Order | Key | Owner | Snapshot sends |
|---|---|---|---|
| 0 | `world` | session-persistence-and-rejoin | `WorldState` (`updateGamemode = true`) |
| 10 | `garage` | session-persistence-and-rejoin | `GarageState` (`AvailablePoints` computed) |
| 20 | `inventory` | session-persistence-and-rejoin | `InventorySyncPacket` batches |
| 100 | `cars` | sync-car-parts | car replay + parts |
| 150 | `car-details` | sync-car-details | `CarDetailsUpdatePacket{IsFull}` per loaded car |
| 200 | `car-placement` | sync-car-placement-and-lifts | `ParkingState`, `ParkingSlotUpdate`, `LifterState` |
| 300 | `workshop-tools` | sync-workshop-machines | `ToolsState` |
| 400 | `jobs` | sync-orders-and-jobs | `JobsState` |
| 450 | `self` | session-persistence-and-rejoin (Part B) | `PlayerRestore` |
| 500 | `players` | sync-players-and-scenes | `PlayerRoster` |

`SyncOrder` keys and values are append-only. Do not renumber.

Save section `players` (`PlayerRecordsSection`, `Data/Presence/`) is save-only: one `PlayerRecord` per identity
(`steam:<id>` or `guid:<player key>`) with name, last seen and the last place. Live positions are copied into it on
leave and before every save. The `self` provider sends `PlayerRestore` from it on the first snapshot of a connection,
only when the last place was the garage.

## Items

An *item* is whatever unit the client reports with `SyncTracker.Applied(key, snapshotId)`. `SendSnapshot` returns
how many it sent; the server puts the counts in `SyncEnd.Items`. The client sends `SyncAck` once `SyncEnd` arrived
and every count is met, and only then sets `ClientData.IsInitialSyncFinished`.

- Capture `SyncTracker.ReceivingSnapshotId` when the packet arrives and pass it to `Applied` when the item is
  really applied (a coroutine may finish later). Packets that arrive outside a snapshot get `NoSnapshot` and are
  not counted, so the same handler can serve snapshot and live packets.
- Snapshot handlers never wait for `IsInitialSyncFinished`; that would deadlock the counts.
- Do not add "synced" flags or a provider-specific end packet. Count items instead.
- Do not send from `OnAskForSync`; add a provider.

## Versions

Bump `Version` and add a `Migrate` step when you rename, remove or reshape a field. Adding a field needs no bump
(Newtonsoft fills defaults). A save whose section version is newer than the server's refuses to load. A section
missing from the file gets `Reset()`; an unknown section is kept as raw JSON and written back.

## Locking

`GameDataManager.StateLock` guards all shared server state. It is held around packet dispatch (TCP, UDP, Steam),
`CommandSystem.Execute`, `Client.Disconnect`, the client update loop and building the save envelope. `Monitor` is
re-entrant, so locking again inside a handler is harmless. Do not add a second lock for shared state.

Never write files under the lock. Call `GameDataManager.RequestSave()`; the main loop (`ServerWindow.TickServer`)
saves on its next tick.
