using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class CarPartsHandlers
	{
		private static readonly Dictionary<string, List<CarPartsSnapshotPacket>> incoming = new Dictionary<string, List<CarPartsSnapshotPacket>>();

		[PacketHandler(PacketTypes.CarPartsSnapshot)]
		public static void OnBaseline(long clientId, CarPartsSnapshotPacket packet)
		{
			string key = $"{clientId}/{packet.CarLoaderID}/{packet.SpawnSeq}";
			if (!incoming.TryGetValue(key, out var batches))
			{
				batches = new List<CarPartsSnapshotPacket>();
				incoming[key] = batches;
			}
			batches.Add(packet);
			if (!packet.IsLastBatch) return;
			incoming.Remove(key);

			var entry = CarPartsStore.Get(packet.CarLoaderID);
			if (entry == null || entry.SpawnSeq != packet.SpawnSeq)
			{
				Logger.Debug($"[Cars] Baseline from client {clientId} for loader {packet.CarLoaderID} SpawnSeq {packet.SpawnSeq} dropped (car replaced or gone).");
				return;
			}
			if (!entry.HasBaseline && entry.SpawnedBy != (int)clientId)
			{
				Logger.Warn($"[Cars] First baseline for loader {packet.CarLoaderID} came from client {clientId}, not the spawner {entry.SpawnedBy}; dropped.");
				return;
			}

			CarPartsStore.StoreBaseline(entry, packet.EngineSwap,
				batches.SelectMany(b => b.BodyParts), batches.SelectMany(b => b.SubParts));
			CarPartsStore.SendSnapshot(packet.CarLoaderID, entry, CarPartsSnapshotPacket.LiveSnapshot, except: (int)clientId);
			GameDataManager.RequestSave();
		}
	}
}
