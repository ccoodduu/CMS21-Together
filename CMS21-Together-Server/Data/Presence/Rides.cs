using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Presence
{
	/// <summary>Never saved. Callers hold <see cref="GameDataManager.StateLock"/>.</summary>
	public static class Rides
	{
		public const float ArriveSeconds = 120f;

		private class Ride
		{
			public int DriverId;
			public int PassengerId;
			public int Loader;
			public float Since;
			public bool Arrived;
		}

		private static readonly Dictionary<int, Ride> byPassenger = new Dictionary<int, Ride>();

		public static void Initialize()
		{
			CarAwayRegistry.Granted += OnGranted;
			CarAwayRegistry.Released += OnReleased;
			PresenceEvents.SceneChanged += OnSceneChanged;
			PresenceEvents.Left += OnLeft;
		}

		public static void Clear() => byPassenger.Clear();

		public static IEnumerable<string> Describe(float now) =>
			byPassenger.Values.OrderBy(r => r.PassengerId).Select(r =>
				$"  passenger {r.PassengerId} with driver {r.DriverId} in car {r.Loader}, {now - r.Since:0} s{(r.Arrived ? ", on the track" : ", travelling")}");

		private static void OnGranted(int loader, int driverId, CarAwayKind kind)
		{
			if (kind != CarAwayKind.TestTrack) return;
			foreach (var record in PresenceRegistry.All.ToList())
			{
				if (record.PlayerId == driverId || record.Scene != GameScene.Garage || record.SeatCarLoaderId != loader) continue;
				if (!Server.Clients.TryGetValue(record.PlayerId, out var client) || !client.IsConnected || client.SyncState == SyncState.Connected) continue;
				if (byPassenger.ContainsKey(record.PlayerId)) continue;
				var ride = new Ride { DriverId = driverId, PassengerId = record.PlayerId, Loader = loader, Since = ServerTime.Time };
				byPassenger[record.PlayerId] = ride;
				Logger.Info($"[Ride] Client {record.PlayerId} sits in car {loader} ({(record.SeatLeft ? "left" : "right")}) and rides along with client {driverId}.");
				Broadcast(ride, true, RideEndReason.None);
			}
		}

		private static void OnReleased(int loader, int ownerId, CarAwayKind kind)
		{
			if (kind != CarAwayKind.TestTrack) return;
			bool left = PresenceRegistry.Get(ownerId) == null;
			foreach (var ride in byPassenger.Values.Where(r => r.DriverId == ownerId && r.Loader == loader).ToList())
				End(ride, left ? RideEndReason.DriverLeft : RideEndReason.DriveCancelled, left ? "the driver left the game" : "the test drive claim was released");
		}

		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			foreach (var ride in byPassenger.Values.Where(r => r.DriverId == clientId).ToList())
			{
				if (from == GameScene.TestTrack) End(ride, RideEndReason.DriverReturned, $"the driver went to {to}");
				else if (from == GameScene.Loading && to != GameScene.TestTrack) End(ride, RideEndReason.DriveCancelled, $"the driver arrived in {to}");
			}

			if (byPassenger.TryGetValue(clientId, out var own))
			{
				if (to == GameScene.TestTrack)
				{
					own.Arrived = true;
					Logger.Info($"[Ride] Client {clientId} arrived on the test track to ride with client {own.DriverId}.");
				}
				else if (from == GameScene.TestTrack || from == GameScene.Loading && to != GameScene.TestTrack)
				{
					End(own, RideEndReason.PassengerLeft, $"the passenger went to {to}");
				}
			}

			if (to == GameScene.TestTrack)
				foreach (var ride in byPassenger.Values.Where(r => r.PassengerId != clientId))
					Server.SendToClient(Packet(ride, true, RideEndReason.None), clientId);
		}

		private static void OnLeft(int clientId)
		{
			foreach (var ride in byPassenger.Values.Where(r => r.DriverId == clientId).ToList()) End(ride, RideEndReason.DriverLeft, "the driver left the game");
			if (byPassenger.TryGetValue(clientId, out var own)) End(own, RideEndReason.PassengerLeft, "the passenger left the game");
		}

		public static void Tick(float now)
		{
			if (byPassenger.Count == 0) return;
			foreach (var ride in byPassenger.Values.Where(r => !r.Arrived && now - r.Since > ArriveSeconds).ToList())
				End(ride, RideEndReason.PassengerDidNotArrive, $"the passenger did not reach the test track within {ArriveSeconds:0} s");
		}

		private static void End(Ride ride, RideEndReason reason, string why)
		{
			if (!byPassenger.Remove(ride.PassengerId)) return;
			Logger.Info($"[Ride] Client {ride.PassengerId}'s ride with client {ride.DriverId} in car {ride.Loader} ended: {why}.");
			Broadcast(ride, false, reason);
		}

		private static RideUpdatePacket Packet(Ride ride, bool active, RideEndReason reason) => new RideUpdatePacket
		{
			DriverId = ride.DriverId, PassengerId = ride.PassengerId, CarLoaderID = ride.Loader, Active = active, Reason = reason,
		};

		private static void Broadcast(Ride ride, bool active, RideEndReason reason) => Server.SendToClients(Packet(ride, active, reason));
	}
}
