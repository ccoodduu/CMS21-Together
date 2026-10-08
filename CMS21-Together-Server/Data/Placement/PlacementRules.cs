using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Placement
{
	public static class PlacementRules
	{
		public const int OnFloor = 0;
		public const int Middle = 1;
		public const int Up = 2;
		public const int CarLifter1Place = 3;
		public const int CarLifter2Place = 4;

		private static PlacementState State => GameDataManager.CurrentState.PlacementState;

		public static int PlaceOfLifter(int lifter) => lifter == 0 ? CarLifter1Place : lifter == 1 ? CarLifter2Place : -1;

		public static int LifterAtPlace(int place) => place == CarLifter1Place ? 0 : place == CarLifter2Place ? 1 : -1;

		public static int LifterState(int lifter) => State.Lifters.TryGetValue(lifter, out int state) ? state : OnFloor;

		public static int? LoaderAtPlace(int place)
		{
			foreach (var pair in GameDataManager.CurrentState.CarState.LoadedCars)
				if (pair.Value.Spawn != null && pair.Value.Spawn.PlaceNo == place) return pair.Key;
			return null;
		}

		public static void SetLifter(int lifter, int state)
		{
			if (state == OnFloor) State.Lifters.Remove(lifter);
			else State.Lifters[lifter] = state;
		}

		public static void ResetLifterAt(int place)
		{
			int lifter = LifterAtPlace(place);
			if (lifter < 0 || LifterState(lifter) == OnFloor) return;
			SetLifter(lifter, OnFloor);
			Logger.Info($"[Placement] Lift {lifter} reset to the floor (no car on it any more).");
		}

		// The game raises a lift to Middle by itself when the car put on it cannot stand on its wheels; the client
		// that moves the car tells the server which state its game ends in.
		public static void OnCarArrived(int place, int loader, int liftState)
		{
			int lifter = LifterAtPlace(place);
			if (lifter < 0 || liftState != Middle || LifterState(lifter) != OnFloor) return;
			SetLifter(lifter, Middle);
			Logger.Info($"[Placement] Lift {lifter} raised to the middle: loader {loader} arrived without a full set of wheels.");
			SendLifter(lifter, instant: true);
		}

		public static void OnLoaderCleared(int loader, CarLoaderEntry removed, ClearReason reason)
		{
			if (removed.Spawn != null) ResetLifterAt(removed.Spawn.PlaceNo);
			if (reason != ClearReason.SpawnerLeft || removed.FromParking == null) return;
			if (ParkingService.TryAdd(removed.FromParking, -1, out int slot))
			{
				if (removed.ParkedRecord != null)
				{
					removed.ParkedRecord.CarId = removed.FromParking.Id;
					ParkingService.KeepRecord(removed.ParkedRecord);
				}
				Logger.Info($"[Parking] {removed.FromParking.CarToLoad} went back to slot {slot}: its unparker left before the baseline.");
				ParkingService.BroadcastSlot(slot);
			}
			else
			{
				Logger.Error($"[Parking] {removed.FromParking.CarToLoad} is lost: its unparker left before the baseline and the parking is full.");
			}
		}

		public static void ResetLiftsWithoutCars()
		{
			foreach (int lifter in State.Lifters.Keys.ToList())
				if (LoaderAtPlace(PlaceOfLifter(lifter)) == null) SetLifter(lifter, OnFloor);
		}

		public static IEnumerable<string> Describe()
		{
			for (int lifter = 0; lifter < 2; lifter++)
			{
				int? loader = LoaderAtPlace(PlaceOfLifter(lifter));
				yield return $"lift {lifter}: state {LifterState(lifter)}, car {(loader.HasValue ? $"loader {loader}" : "none")}";
			}
			foreach (var pair in GameDataManager.CurrentState.CarState.LoadedCars.OrderBy(p => p.Key))
				yield return $"loader {pair.Key}: {pair.Value.Spawn?.CarToLoad} at place {pair.Value.Spawn?.PlaceNo}";
			foreach (string line in ParkingService.Describe()) yield return line;
		}

		public static void SendLifter(int lifter, bool instant, int only = CarLoaderEntry.NoClient)
		{
			var packet = new LifterStatePacket { LifterIndex = lifter, State = LifterState(lifter), Instant = instant };
			if (only == CarLoaderEntry.NoClient) Server.SendToClients(packet);
			else Server.SendToClient(packet, only);
		}

		public static void SendCarPlaces(int clientId)
		{
			foreach (var car in GameDataManager.CurrentState.CarState.LoadedCars.OrderBy(c => LifterAtPlace(c.Value.Spawn?.PlaceNo ?? -1) >= 0))
				Server.SendToClient(new CarPlaceChangedPacket { CarLoaderID = car.Key, Place = car.Value.Spawn?.PlaceNo ?? -1 }, clientId);
			for (int lifter = 0; lifter < 2; lifter++) SendLifter(lifter, instant: true, only: clientId);
		}
	}
}
