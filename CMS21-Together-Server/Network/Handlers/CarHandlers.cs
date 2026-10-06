using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
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
            CarPartsStore.ClearLoader(packet.CarLoaderID, ClearReason.Deleted);
            Server.SendToClients(packet, (int)clientId);
        }
    }
}
