using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Cars
{
	// sync-test-drive-and-diagnostics D1/D10: one runtime "away" claim per car (test track, test path, dyno), separate
	// from the part claims. Callers hold StateLock.
	public static class CarAwayRegistry
	{
		private const float ReturnGraceSeconds = 60f;
		private const float GarageActivitySeconds = 15 * 60f;

		private class Away
		{
			public int SpawnSeq;
			public int Owner;
			public CarAwayKind Kind;
			public float Since;
			public float BackInGarageSince = -1f;
		}

		private static readonly Dictionary<int, Away> claims = new Dictionary<int, Away>();

		public static void Initialize()
		{
			CarPartsStore.LoaderCleared += (loader, _, _) => Drop(loader, "car cleared");
			PresenceEvents.Left += clientId => ReleaseOwner(clientId, "owner left");
			PresenceEvents.SceneChanged += OnSceneChanged;
		}

		public static bool IsAwayFrom(int loader, int clientId) => claims.TryGetValue(loader, out var away) && away.Owner != clientId;

		public static int OwnerOf(int loader) => claims.TryGetValue(loader, out var away) ? away.Owner : -1;

		public static bool Blocks(int loader, int clientId, string what)
		{
			if (!claims.TryGetValue(loader, out var away) || away.Owner == clientId) return false;
			Logger.Info($"[Away] {what} on loader {loader} from client {clientId} refused: {away.Kind} by client {away.Owner}.");
			return true;
		}

		public static bool IsOwner(int loader, int clientId, CarAwayKind kind, int spawnSeq) =>
			claims.TryGetValue(loader, out var away) && away.Owner == clientId && away.Kind == kind && away.SpawnSeq == spawnSeq;

		public static void OnRequest(int clientId, CarAwayRequestPacket packet, float now)
		{
			var entry = CarPartsStore.Get(packet.CarLoaderID);
			claims.TryGetValue(packet.CarLoaderID, out var current);
			var refusal = CarAwayRefusal.None;
			if (entry == null || entry.SpawnSeq != packet.SpawnSeq || !entry.HasBaseline) refusal = CarAwayRefusal.NotReady;
			else if (current != null && current.Owner != clientId) refusal = CarAwayRefusal.Busy;
			else if (CarClaims.Held(packet.CarLoaderID).Any(h => h.Owner != clientId)) refusal = CarAwayRefusal.InUse;

			if (refusal != CarAwayRefusal.None)
			{
				Logger.Info($"[Away] {packet.Kind} on loader {packet.CarLoaderID} refused for client {clientId}: {refusal}.");
				Server.SendToClient(new CarAwayUpdatePacket
				{
					CarLoaderID = packet.CarLoaderID, SpawnSeq = packet.SpawnSeq, Kind = current?.Kind ?? packet.Kind,
					OwnerPlayerId = current?.Owner ?? -1, RequestId = packet.RequestId, Refusal = refusal,
				}, clientId);
				return;
			}

			var ownKeys = CarClaims.Held(packet.CarLoaderID).Where(h => h.Owner == clientId).Select(h => h.Key).ToList();
			if (ownKeys.Count > 0) CarClaims.ReleaseCommitted(clientId, packet.CarLoaderID, ownKeys);
			claims[packet.CarLoaderID] = new Away { SpawnSeq = packet.SpawnSeq, Owner = clientId, Kind = packet.Kind, Since = now };
			Logger.Info($"[Away] {packet.Kind} on loader {packet.CarLoaderID} granted to client {clientId}.");
			var update = new CarAwayUpdatePacket { CarLoaderID = packet.CarLoaderID, SpawnSeq = packet.SpawnSeq, Kind = packet.Kind, OwnerPlayerId = clientId };
			Server.SendToClients(update, exceptClient: clientId);
			update.RequestId = packet.RequestId;
			Server.SendToClient(update, clientId);
		}

		public static void OnRelease(int clientId, CarAwayReleasePacket packet)
		{
			if (!claims.TryGetValue(packet.CarLoaderID, out var away) || away.Owner != clientId || away.SpawnSeq != packet.SpawnSeq) return;
			if (packet.SpecialState >= 0)
			{
				var entry = CarPartsStore.Get(packet.CarLoaderID);
				if (entry?.Spawn != null) entry.Spawn.SpecialState = packet.SpecialState;
			}
			Release(packet.CarLoaderID, "released by the owner", packet.SpecialState);
		}

		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			foreach (var pair in claims.Where(c => c.Value.Owner == clientId).ToList())
			{
				var away = pair.Value;
				if (away.Kind == CarAwayKind.TestTrack)
				{
					if (to == GameScene.Garage) away.BackInGarageSince = ServerTime.Time;
					else if (to != GameScene.Loading && to != GameScene.TestTrack) Release(pair.Key, $"owner went to {to}");
				}
				else if (from == GameScene.Garage)
				{
					Release(pair.Key, $"owner left the garage for {to}");
				}
			}
		}

		public static void Tick(float now)
		{
			foreach (var pair in claims.ToList())
			{
				var away = pair.Value;
				if (away.Kind == CarAwayKind.TestTrack && away.BackInGarageSince >= 0f && now - away.BackInGarageSince > ReturnGraceSeconds)
					Release(pair.Key, "watchdog: owner back in the garage for 60 s");
				else if (away.Kind != CarAwayKind.TestTrack && now - away.Since > GarageActivitySeconds)
					Release(pair.Key, "watchdog: older than 15 min");
			}
		}

		public static void SendActive(int loader, int clientId)
		{
			if (!claims.TryGetValue(loader, out var away)) return;
			Server.SendToClient(new CarAwayUpdatePacket { CarLoaderID = loader, SpawnSeq = away.SpawnSeq, Kind = away.Kind, OwnerPlayerId = away.Owner }, clientId);
		}

		public static IEnumerable<string> Describe(float now) =>
			claims.OrderBy(c => c.Key).Select(c => $"  loader {c.Key}: {c.Value.Kind} by client {c.Value.Owner}, {now - c.Value.Since:0} s"
			                                       + (c.Value.BackInGarageSince >= 0f ? $", back for {now - c.Value.BackInGarageSince:0} s" : ""));

		private static void ReleaseOwner(int clientId, string reason)
		{
			foreach (int loader in claims.Where(c => c.Value.Owner == clientId).Select(c => c.Key).ToList()) Release(loader, reason);
		}

		private static void Drop(int loader, string reason)
		{
			if (claims.ContainsKey(loader)) Release(loader, reason);
		}

		private static void Release(int loader, string reason, int specialState = -1)
		{
			if (!claims.TryGetValue(loader, out var away)) return;
			claims.Remove(loader);
			Logger.Info($"[Away] {away.Kind} on loader {loader} by client {away.Owner} released: {reason}.");
			Server.SendToClients(new CarAwayUpdatePacket
			{
				CarLoaderID = loader, SpawnSeq = away.SpawnSeq, Kind = away.Kind, OwnerPlayerId = -1, SpecialState = specialState,
			});
		}
	}
}
