using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Cars
{
	public static class CarClaims
	{
		private const float ExpirySeconds = 120f;

		private class Claim
		{
			public int Owner;
			public float Since;
		}

		private static readonly Dictionary<int, Dictionary<string, Claim>> claims = new Dictionary<int, Dictionary<string, Claim>>();

		public static void Handle(int clientId, CarPartClaimPacket packet, float now)
		{
			var entry = CarPartsStore.Get(packet.CarLoaderID);
			if (entry == null || entry.SpawnSeq != packet.SpawnSeq) return;
			if (!claims.TryGetValue(packet.CarLoaderID, out var held))
			{
				held = new Dictionary<string, Claim>();
				claims[packet.CarLoaderID] = held;
			}

			if (packet.Release)
			{
				Release(packet.CarLoaderID, packet.Keys.Where(k => held.TryGetValue(k, out var c) && c.Owner == clientId).ToList());
				return;
			}

			var taken = packet.Keys.Where(k => held.TryGetValue(k, out var c) && c.Owner != clientId).ToList();
			if (taken.Count > 0)
			{
				Server.SendToClient(new CarPartClaimUpdatePacket
				{
					CarLoaderID = packet.CarLoaderID, SpawnSeq = packet.SpawnSeq, Keys = taken, OwnerPlayerId = held[taken[0]].Owner
				}, clientId);
				Logger.Debug($"[Cars] Claim by client {clientId} on loader {packet.CarLoaderID} denied: {string.Join(", ", taken)} held by {held[taken[0]].Owner}.");
				return;
			}

			foreach (string key in packet.Keys) held[key] = new Claim { Owner = clientId, Since = now };
			Server.SendToClients(new CarPartClaimUpdatePacket
			{
				CarLoaderID = packet.CarLoaderID, SpawnSeq = packet.SpawnSeq, Keys = packet.Keys, OwnerPlayerId = clientId
			});
		}

		public static void ReleaseCommitted(int clientId, int loader, IEnumerable<string> keys)
		{
			if (!claims.TryGetValue(loader, out var held)) return;
			Release(loader, keys.Where(k => held.TryGetValue(k, out var c) && c.Owner == clientId).ToList());
		}

		public static void ReleaseOwner(int clientId)
		{
			foreach (var pair in claims.ToList())
				Release(pair.Key, pair.Value.Where(c => c.Value.Owner == clientId).Select(c => c.Key).ToList());
		}

		public static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			if (from == GameScene.Garage) ReleaseOwner(clientId);
		}

		public static void DropLoader(int loader) => claims.Remove(loader);

		public static void Expire(float now)
		{
			foreach (var pair in claims.ToList())
			{
				var expired = pair.Value.Where(c => now - c.Value.Since > ExpirySeconds).Select(c => c.Key).ToList();
				if (expired.Count > 0)
				{
					Logger.Info($"[Cars] Claims on loader {pair.Key} expired: {string.Join(", ", expired)}");
					Release(pair.Key, expired);
				}
			}
		}

		public static void SendActive(int loader, int clientId)
		{
			var entry = CarPartsStore.Get(loader);
			if (entry == null || !claims.TryGetValue(loader, out var held)) return;
			foreach (var group in held.GroupBy(c => c.Value.Owner))
			{
				Server.SendToClient(new CarPartClaimUpdatePacket
				{
					CarLoaderID = loader, SpawnSeq = entry.SpawnSeq, Keys = group.Select(c => c.Key).ToList(), OwnerPlayerId = group.Key
				}, clientId);
			}
		}

		private static void Release(int loader, List<string> keys)
		{
			if (keys.Count == 0 || !claims.TryGetValue(loader, out var held)) return;
			foreach (string key in keys) held.Remove(key);
			var entry = CarPartsStore.Get(loader);
			Server.SendToClients(new CarPartClaimUpdatePacket
			{
				CarLoaderID = loader, SpawnSeq = entry?.SpawnSeq ?? 0, Keys = keys, OwnerPlayerId = CarPartClaimUpdatePacket.Released
			});
		}
	}
}
