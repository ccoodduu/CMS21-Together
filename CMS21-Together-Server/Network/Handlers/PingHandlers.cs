using System;
using System.Collections.Generic;
using CMS21_Together_Core;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data;
using CMS21_Together_Server.Data.Cars;
using CMS21_Together_Server.Data.Outdoor;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class PingHandlers
	{
		public const float MinIntervalSeconds = 0.4f;
		private const float DropLogIntervalSeconds = 5f;
		private const float MaxCoordinate = 100000f;

		private class Budget
		{
			public float LastAccepted = float.MinValue;
			public int Dropped;
			public float LastDropLog = float.MinValue;
		}

		private static readonly Dictionary<int, Budget> budgets = new Dictionary<int, Budget>();

		public static void Initialize()
		{
			PresenceEvents.Left += clientId => budgets.Remove(clientId);
		}

		[PacketHandler(PacketTypes.CoopPing)]
		public static void OnCoopPing(long clientId, CoopPingPacket packet)
		{
			int id = (int)clientId;
			var record = PresenceRegistry.Get(id);
			if (record == null || packet == null) return;
			if (packet.Scene != record.Scene)
			{
				Logger.Info($"[Ping] Ignored a ping of player {id} from {packet.Scene}: the player is in {record.Scene}.");
				return;
			}
			bool shared = OutdoorInstances.IsShared(record.Scene);
			if (!GameSceneInfo.ShowsAvatars(record.Scene, shared) || (shared && record.OutdoorInstanceId == 0))
			{
				Logger.Info($"[Ping] Ignored a ping of player {id} in {record.Scene}: players do not see each other there.");
				return;
			}
			if (!Valid(packet.Position))
			{
				Logger.Info($"[Ping] Ignored a ping of player {id}: no valid position.");
				return;
			}
			if (!WithinBudget(id)) return;

			var relayed = new CoopPingPacket
			{
				PlayerId = id,
				Scene = record.Scene,
				Position = new Vector3Serializable(packet.Position.X, packet.Position.Y, packet.Position.Z),
			};
			if (packet.IsPart && ValidKey(packet.PartKey) && CarPartsStore.Get(packet.CarLoaderID) != null)
			{
				relayed.CarLoaderID = packet.CarLoaderID;
				relayed.PartKey = packet.PartKey;
			}

			int sent = 0;
			foreach (var other in PresenceRegistry.All)
			{
				if (other.PlayerId == id || other.Scene != record.Scene) continue;
				if (shared && other.OutdoorInstanceId != record.OutdoorInstanceId) continue;
				if (!Server.Clients.TryGetValue(other.PlayerId, out var client) || !client.IsConnected || client.SyncState == SyncState.Connected) continue;
				Server.SendToClient(relayed, other.PlayerId);
				sent++;
			}
			Logger.Info($"[Ping] Player {id} '{record.Username}' pinged {relayed}; relayed to {sent} player(s).");
		}

		private static bool WithinBudget(int clientId)
		{
			if (!budgets.TryGetValue(clientId, out var budget))
			{
				budget = new Budget();
				budgets[clientId] = budget;
			}
			float now = ServerTime.Time;
			if (now - budget.LastAccepted >= MinIntervalSeconds)
			{
				budget.LastAccepted = now;
				return true;
			}

			budget.Dropped++;
			if (now - budget.LastDropLog >= DropLogIntervalSeconds)
			{
				budget.LastDropLog = now;
				Logger.Info($"[Ping] Rate limit: dropped a ping of player {clientId} ({budget.Dropped} so far, at most one per {MinIntervalSeconds:0.0} s).");
			}
			return false;
		}

		private static bool Valid(Vector3Serializable position) =>
			position != null && Finite(position.X) && Finite(position.Y) && Finite(position.Z);

		private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < MaxCoordinate;

		private static bool ValidKey(string key) =>
			key.Length <= CoopPingPacket.MaxKeyLength && (key.StartsWith("s:", StringComparison.Ordinal) || key.StartsWith("b:", StringComparison.Ordinal));
	}
}
