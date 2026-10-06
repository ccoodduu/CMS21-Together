using System;
using System.Collections.Generic;
using System.Linq;
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

		private const int SnapshotBatchSize = 100;

		private static CarState State => GameDataManager.CurrentState.CarState;

		public static CarLoaderEntry Get(int loader) => State.LoadedCars.TryGetValue(loader, out var entry) ? entry : null;

		public static IEnumerable<string> Describe()
		{
			if (State.LoadedCars.Count == 0) yield return "no cars";
			foreach (var pair in State.LoadedCars.OrderBy(p => p.Key))
			{
				var entry = pair.Value;
				var held = CarClaims.Held(pair.Key).ToList();
				string claims = held.Count == 0 ? "none" : string.Join(", ", held.Select(c => $"{c.Key} by {c.Owner}"));
				yield return $"loader {pair.Key}: {entry.Spawn?.CarToLoad} SpawnSeq {entry.SpawnSeq}, revision {entry.Revision}, baseline {entry.HasBaseline}, {entry.BodyParts.Count} body, {entry.SubParts.Count} mechanical ({entry.SubParts.Values.Count(s => s.Unmounted)} unmounted), claims: {claims}";
			}
		}

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

		public static void StoreBaseline(CarLoaderEntry entry, string engineSwap, IEnumerable<CarBodyPartUpdatePacket> body, IEnumerable<CarSubPartUpdatePacket> sub)
		{
			entry.Revision = entry.HasBaseline ? entry.Revision + 1 : 1;
			entry.HasBaseline = true;
			entry.EngineSwap = engineSwap;
			entry.BodyParts.Clear();
			entry.SubParts.Clear();
			foreach (var record in body)
			{
				record.Revision = entry.Revision;
				entry.BodyParts[record.PartIndex] = record;
			}
			foreach (var record in sub)
			{
				record.Revision = entry.Revision;
				entry.SubParts[CarSubPartIdentity.BuildKey(record.PartIndexPath)] = record;
			}
			Logger.Info($"[Cars] Loader {entry.Spawn.CarLoaderID}: baseline revision {entry.Revision} ({entry.BodyParts.Count} body, {entry.SubParts.Count} mechanical).");
		}

		public static void SendSnapshot(int loader, CarLoaderEntry entry, int snapshotId, int only = CarLoaderEntry.NoClient, int except = CarLoaderEntry.NoClient)
		{
			var records = entry.BodyParts.Values.Cast<object>().Concat(entry.SubParts.Values).ToList();
			int batchCount = Math.Max(1, (records.Count + SnapshotBatchSize - 1) / SnapshotBatchSize);
			for (int i = 0; i < batchCount; i++)
			{
				var slice = records.Skip(i * SnapshotBatchSize).Take(SnapshotBatchSize).ToList();
				var packet = new CarPartsSnapshotPacket
				{
					SnapshotId = snapshotId,
					CarLoaderID = loader,
					SpawnSeq = entry.SpawnSeq,
					Revision = entry.Revision,
					BatchIndex = i,
					IsLastBatch = i == batchCount - 1,
					Spawn = entry.Spawn,
					EngineSwap = entry.EngineSwap,
					BodyParts = slice.OfType<CarBodyPartUpdatePacket>().ToList(),
					SubParts = slice.OfType<CarSubPartUpdatePacket>().ToList()
				};
				if (only != CarLoaderEntry.NoClient) Server.SendToClient(packet, only);
				else Server.SendToClients(packet, except);
			}
		}

		public static void OnPlayerLeft(int clientId)
		{
			foreach (var pair in State.LoadedCars.Where(c => c.Value.SpawnedBy == clientId).ToList())
			{
				if (!pair.Value.HasBaseline)
				{
					ClearLoader(pair.Key, ClearReason.SpawnerLeft);
					Server.SendToClients(new CarSpawnDeletePacket { CarLoaderID = pair.Key });
				}
				else
				{
					pair.Value.SpawnedBy = CarLoaderEntry.NoClient;
				}
			}
		}

		public static bool ClearLoader(int loader, ClearReason reason)
		{
			if (!State.LoadedCars.TryGetValue(loader, out var entry)) return false;
			State.LoadedCars.Remove(loader);
			CarClaims.DropLoader(loader);
			Logger.Info($"[Cars] Loader {loader}: {entry.Spawn?.CarToLoad} cleared ({reason}).");
			LoaderCleared?.Invoke(loader, entry, reason);
			return true;
		}
	}
}
