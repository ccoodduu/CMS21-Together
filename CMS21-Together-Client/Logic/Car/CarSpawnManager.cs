using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Network;

namespace CMS21Together.Logic.Car
{
    public static class CarSpawnManager
    {
        private const float PlaceSettleSeconds = 5f;

        private static readonly HashSet<int> pendingSpawn = new HashSet<int>();
        private static readonly HashSet<int> overriddenSpawn = new HashSet<int>();

        public static void Reset()
        {
            pendingSpawn.Clear();
            overriddenSpawn.Clear();
        }

        public static void OnSpawnAck(int loader) => pendingSpawn.Remove(loader);

        public static void OnRemoteSpawn(int loader)
        {
            if (pendingSpawn.Remove(loader)) overriddenSpawn.Add(loader);
        }

        // Another player's car took the loader first: its spawn reached this client before the refusal of ours.
        public static bool KeepAfterRejection(int loader)
        {
            pendingSpawn.Remove(loader);
            return overriddenSpawn.Remove(loader);
        }

        public static IEnumerator RequestCarSpawn(string carToLoad, CarLoader carLoader)
        {
            int carLoaderID = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
            float deadline = UnityEngine.Time.realtimeSinceStartup + 90f;
            while (!carLoader.IsCarLoaded() && carLoader.carToLoad == carToLoad && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            if (carLoader.carToLoad != carToLoad) yield break;

            var request = new CarSpawnRequestPacket
            {
                CarLoaderID = carLoaderID,
                CarToLoad = carToLoad,
                ConfigVersion = carLoader.ConfigVersion,
                PlaceNo = carLoader.placeNo,
                IsJob = carLoader.customerCar,
                JobID = carLoader.customerCar ? carLoader.orderConnection : -1,
                Dlc = CarDlc.For(carToLoad)
            };

            if (!request.IsJob)
            {
                overriddenSpawn.Remove(carLoaderID);
                pendingSpawn.Add(carLoaderID);
            }
            Client.Instance.Send(request);
            Log.Info($"[CarSpawnManager] Requested spawn for {carToLoad} on Loader {carLoaderID}");

            deadline = UnityEngine.Time.realtimeSinceStartup + PlaceSettleSeconds;
            while (carLoader.placeNo == request.PlaceNo && carLoader.carToLoad == carToLoad && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
            if (carLoader.carToLoad != carToLoad || carLoader.placeNo == request.PlaceNo) yield break;
            Log.Info($"[CarSpawnManager] Loader {carLoaderID}: the game placed {carToLoad} at {carLoader.placeNo} after the spawn request.");
            Client.Instance.Send(new CarPlaceChangeRequestPacket { CarLoaderID = carLoaderID, FromPlace = request.PlaceNo, ToPlace = carLoader.placeNo });
        }

        public static IEnumerator RequestCarDelete(CarLoader carLoader)
        {
            int carLoaderID = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);

            var request = new CarSpawnDeletePacket
            {
                CarLoaderID = carLoaderID
            };

            Client.Instance.Send(request);
            Log.Info($"[CarSpawnManager] Requested delete for Loader {carLoaderID}");
            yield break;
        }
    }
}
