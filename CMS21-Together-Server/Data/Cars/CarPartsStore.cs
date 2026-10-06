using System;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Cars
{
	public enum ClearReason
	{
		Deleted,
		Parked,
		JobEnded,
		SpawnerLeft
	}

	public static class CarPartsStore
	{
		public static event Action<int, CarLoaderEntry> SpawnRegistered;
		public static event Action<int, CarLoaderEntry, ClearReason> LoaderCleared;

		private static CarState State => GameDataManager.CurrentState.CarState;

		public static CarLoaderEntry Get(int loader) => State.LoadedCars.TryGetValue(loader, out var entry) ? entry : null;

		public static CarLoaderEntry RegisterSpawn(CarSpawnResponsePacket spawn, int clientId)
		{
			if (State.LoadedCars.ContainsKey(spawn.CarLoaderID)) ClearLoader(spawn.CarLoaderID, ClearReason.Deleted);

			int spawnSeq = State.NextSpawnSeq++;
			spawn.SpawnSeq = spawnSeq;
			var entry = new CarLoaderEntry { Spawn = spawn, SpawnSeq = spawnSeq, SpawnedBy = clientId };
			State.LoadedCars[spawn.CarLoaderID] = entry;

			if (clientId != CarLoaderEntry.NoClient)
				Server.SendToClient(new CarSpawnAckPacket { CarLoaderID = spawn.CarLoaderID, SpawnSeq = spawnSeq }, clientId);
			Logger.Info($"[Cars] Loader {spawn.CarLoaderID}: {spawn.CarToLoad} spawned by client {clientId}, SpawnSeq {spawnSeq}.");
			SpawnRegistered?.Invoke(spawn.CarLoaderID, entry);
			return entry;
		}

		public static bool ClearLoader(int loader, ClearReason reason)
		{
			if (!State.LoadedCars.TryGetValue(loader, out var entry)) return false;
			State.LoadedCars.Remove(loader);
			Logger.Info($"[Cars] Loader {loader}: {entry.Spawn?.CarToLoad} cleared ({reason}).");
			LoaderCleared?.Invoke(loader, entry, reason);
			return true;
		}
	}
}
