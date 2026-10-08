using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
    public static class CarHandlers
    {
        [PacketHandler(PacketTypes.CarSpawnRequest)]
        public static void HandleCarSpawnRequest(long clientId, CarSpawnRequestPacket packet)
        {
            Logger.Debug($"[CarHandlers] Received CarSpawnRequest from client {clientId} for Loader {packet.CarLoaderID} (Car: {packet.CarToLoad}, Job: {packet.IsJob})");

            if (string.IsNullOrEmpty(packet.CarToLoad))
            {
                Logger.Error($"[CarHandlers] CarSpawnRequest from client {clientId} missing CarToLoad!");
                Server.SendToClient(new CarSpawnRejectedPacket
                {
                    CarLoaderID = packet.CarLoaderID,
                    Reason = "CarToLoad was empty."
                }, (int)clientId);
                return;
            }

            if (packet.IsJob && (!Data.Jobs.JobsService.IsClaimedBy(packet.JobID, (int)clientId) || CarPartsStore.Get(packet.CarLoaderID) != null))
            {
                Logger.Info($"[Cars] Job car {packet.CarToLoad} for job {packet.JobID} on loader {packet.CarLoaderID} from client {clientId} refused: no claim or the loader is in use.");
                Server.SendToClient(new CarSpawnRejectedPacket { CarLoaderID = packet.CarLoaderID, Reason = "This order is not yours to take any more." }, (int)clientId);
                return;
            }

            if (packet.Dlc >= 0 && !SharedDlc.Shared.Contains(packet.Dlc.ToString()))
            {
                Logger.Info($"[Cars] Spawn of {packet.CarToLoad} on loader {packet.CarLoaderID} from client {clientId} refused: DLC {packet.Dlc} is not shared ({SharedDlc.Format(SharedDlc.Shared)}).");
                Server.SendToClient(new CarSpawnRejectedPacket
                {
                    CarLoaderID = packet.CarLoaderID,
                    Reason = "This car needs a DLC that not every player owns, so it cannot be used in this session."
                }, (int)clientId);
                return;
            }

            var occupant = CarPartsStore.Get(packet.CarLoaderID);
            if (occupant != null)
            {
                Logger.Info($"[Cars] Spawn of {packet.CarToLoad} on loader {packet.CarLoaderID} from client {clientId} refused: the loader holds {occupant.Spawn?.CarToLoad} (SpawnSeq {occupant.SpawnSeq}, spawned by client {occupant.SpawnedBy}).");
                Server.SendToClient(new CarSpawnRejectedPacket { CarLoaderID = packet.CarLoaderID, Reason = "Another car is already in that place." }, (int)clientId);
                if (occupant.HasBaseline)
                {
                    CarPartsStore.SendSnapshot(packet.CarLoaderID, occupant, CarPartsSnapshotPacket.LiveSnapshot, only: (int)clientId);
                    CarDetailsStore.SendTo(packet.CarLoaderID, (int)clientId);
                }
                return;
            }

            var entry = CarPartsStore.RegisterSpawn(new CarSpawnResponsePacket
            {
                CarLoaderID = packet.CarLoaderID,
                CarToLoad = packet.CarToLoad,
                ConfigVersion = packet.ConfigVersion,
                PlaceNo = packet.PlaceNo,
                IsJob = packet.IsJob,
                JobID = packet.JobID
            }, (int)clientId);

            Server.SendToClients(entry.Spawn, (int)clientId);
        }

        [PacketHandler(PacketTypes.CarSpawnDelete)]
        public static void HandleCarSpawnDelete(long clientId, CarSpawnDeletePacket packet)
        {
            Logger.Debug($"[CarHandlers] Received CarSpawnDelete from client {clientId} for Loader {packet.CarLoaderID}");
            var entry = CarPartsStore.Get(packet.CarLoaderID);
            bool busy = entry?.Spawn != null && CarLocks.RefuseBusy(packet.CarLoaderID, (int)clientId, CarLocks.BusyDelete);
            if (entry?.Spawn != null && (busy || CarAwayRegistry.Blocks(packet.CarLoaderID, (int)clientId, "delete")))
            {
                Server.SendToClient(entry.Spawn, (int)clientId);
                if (entry.HasBaseline) CarPartsStore.SendSnapshot(packet.CarLoaderID, entry, CarPartsSnapshotPacket.LiveSnapshot, only: (int)clientId);
                CarDetailsStore.SendTo(packet.CarLoaderID, (int)clientId);
                return;
            }
            if (!CarPartsStore.ClearLoader(packet.CarLoaderID, ClearReason.Deleted))
            {
                Logger.Info($"[Cars] Delete of empty loader {packet.CarLoaderID} from client {clientId}: nothing to delete, not relayed.");
                return;
            }
            Server.SendToClients(packet, (int)clientId);
        }
    }
}
