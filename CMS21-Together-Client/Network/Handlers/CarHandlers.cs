using System.Collections;
using CMS21_Together_Core;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Hook;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.UI;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Network.Handlers
{
    public static class CarHandlers
    {
        [PacketHandler(PacketTypes.CarSpawnResponse)]
        public static void HandleCarSpawnResponse(long clientId, CarSpawnResponsePacket packet)
        {
            ClientScene.GarageBound(() =>
            {
                CarPartsSync.OnRemoteSpawn(packet);
                Logic.Car.Placement.ParkingSync.OnRemoteSpawn(packet.CarLoaderID);
                MelonCoroutines.Start(ProcessCarSpawnResponse(packet));
            });
        }

        private static IEnumerator ProcessCarSpawnResponse(CarSpawnResponsePacket packet)
        {
            while (!ClientData.IsInventorySynced || !ClientData.IsGarageStateSynced)
                yield return new WaitForSeconds(0.25f);

            yield return new WaitForEndOfFrame();

            // By the time IsGarageStateSynced/IsInventorySynced are true, CarLoaderPlaces
            // is already guaranteed populated (LoaderAddition.VanillaLoad calls
            // CarLoaderPlaces.Get().Load() before AskForSync is even sent, in the same
            // coroutine chain). A null result here is a real bug, not a timing race.
            CarLoader carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(packet.CarLoaderID);
            if (carLoader == null)
            {
                Log.Error($"[CarHandlers] CarLoader {packet.CarLoaderID} not found, dropping CarSpawnResponse.");
                yield break;
            }

            Log.Info($"[CarHandlers] Loading {packet.CarToLoad} from server into Loader {packet.CarLoaderID}{(packet.CarData == null ? "" : " from its saved data")}");
            yield return Logic.Car.Placement.CarLoading.Load(carLoader, packet.CarLoaderID, packet);
        }

        [PacketHandler(PacketTypes.CarSpawnDelete)]
        public static void HandleCarSpawnDelete(long clientId, CarSpawnDeletePacket packet)
        {
            ClientScene.GarageBound(() =>
            {
                CarPartsSync.OnCarDeleted(packet.CarLoaderID);
                MelonCoroutines.Start(ProcessCarSpawnDelete(packet));
            });
        }

        private static IEnumerator ProcessCarSpawnDelete(CarSpawnDeletePacket packet)
        {
            while (!ClientData.IsInventorySynced || !ClientData.IsGarageStateSynced)
                yield return new WaitForSeconds(0.25f);

            CarLoader carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(packet.CarLoaderID);
            if (carLoader == null)
            {
                Log.Error($"[CarHandlers] CarLoader {packet.CarLoaderID} not found, dropping CarSpawnDelete.");
                yield break;
            }

            if (string.IsNullOrEmpty(carLoader.carToLoad)) yield break;
            yield return Logic.Player.PresenceManager.EnsureNotSeatedIn(packet.CarLoaderID);
            Logic.Visuals.VisualScope.CancelLoader(packet.CarLoaderID, "car deleted");

            CarSpawnHooks.Suppress(packet.CarLoaderID);
            try
            {
                carLoader.DeleteCar(true);
                Log.Info($"[CarHandlers] Deleted car from Loader {packet.CarLoaderID} as ordered by server.");
            }
            finally
            {
                CarSpawnHooks.Release(packet.CarLoaderID);
            }
        }

        [PacketHandler(PacketTypes.CarPartsChange)]
        public static void HandleCarPartsChange(long clientId, CarPartsChangePacket packet)
        {
            ClientScene.GarageBound(() => PartChanges.OnRemoteChange(packet));
        }

        [PacketHandler(PacketTypes.CarPartsChangeResult)]
        public static void HandleCarPartsChangeResult(long clientId, CarPartsChangeResultPacket packet)
        {
            ClientScene.GarageBound(() => PartChanges.OnResult(packet));
        }

        [PacketHandler(PacketTypes.CarPartClaimUpdate)]
        public static void HandleCarPartClaimUpdate(long clientId, CarPartClaimUpdatePacket packet)
        {
            ClientScene.GarageBound(() => PartClaims.OnUpdate(packet));
        }

        [PacketHandler(PacketTypes.CarSpawnAck)]
        public static void HandleCarSpawnAck(long clientId, CarSpawnAckPacket packet)
        {
            CarPartsSync.OnSpawnAck(packet);
            Logic.Car.Placement.ParkingSync.OnSpawnAck(packet.CarLoaderID);
        }

        [PacketHandler(PacketTypes.CarPartsSnapshot)]
        public static void HandleCarPartsSnapshot(long clientId, CarPartsSnapshotPacket packet)
        {
            int snapshotId = SyncTracker.ReceivingSnapshotId;
            ClientScene.GarageBound(() => CarPartsSync.OnSnapshot(packet, snapshotId));
        }

        [PacketHandler(PacketTypes.CarSpawnRejected)]
        public static void HandleCarSpawnRejected(long clientId, CarSpawnRejectedPacket packet)
        {
            ClientScene.GarageBound(() => MelonCoroutines.Start(ProcessCarSpawnRejected(packet)));
        }

        private static IEnumerator ProcessCarSpawnRejected(CarSpawnRejectedPacket packet)
        {
            Log.Warn($"[CarHandlers] CarSpawnRequest for Loader {packet.CarLoaderID} was rejected by server: {packet.Reason}. Reverting local spawn.");
            ModNotify.ShowToast(packet.Reason);

            CarLoader carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(packet.CarLoaderID);
            if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) yield break;
            if (Logic.Car.Placement.ParkingSync.KeepAfterRejection(packet.CarLoaderID)) yield break;

            CarSpawnHooks.Suppress(packet.CarLoaderID);
            try
            {
                carLoader.DeleteCar(true);
            }
            finally
            {
                CarSpawnHooks.Release(packet.CarLoaderID);
            }
        }
    }
}
