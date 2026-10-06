using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Presence
{
	/// <summary>Per-player records keyed by identity. Callers hold <see cref="GameDataManager.StateLock"/>.</summary>
	public static class PlayerRecords
	{
		private const int MaxKeyLength = 64;

		private static Dictionary<string, PlayerRecord> Records => GameDataManager.CurrentState.PlayerRecords;

		public static PlayerRecord Get(string identity) =>
			identity != null && Records.TryGetValue(identity, out var record) ? record : null;

		public static string Resolve(Client client, string playerKey)
		{
			if (client.ConnectionType == NetworkType.Steam)
				return client.SteamID != 0 ? $"steam:{client.SteamID}" : null;

			string key = playerKey?.Trim();
			if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || !key.All(c => char.IsLetterOrDigit(c) || c == '-'))
				return null;
			return $"guid:{key}";
		}

		public static Client ConnectedWith(string identity, int exceptClientId) =>
			Server.Clients.Values.FirstOrDefault(c => c.ID != exceptClientId && c.IsConnected && c.Identity == identity);

		public static PlayerRecord OnJoined(string identity, string name, out bool returning)
		{
			returning = Records.TryGetValue(identity, out var record);
			if (!returning)
			{
				record = new PlayerRecord { Key = identity };
				Records[identity] = record;
			}
			record.Name = name;
			record.LastSeenUtc = DateTime.UtcNow;
			return record;
		}

		public static void OnLeft(Client client)
		{
			var record = Get(client.Identity);
			if (record == null) return;
			CaptureLive(client.ID, record);
			record.LastSeenUtc = DateTime.UtcNow;
			Logger.Info($"[Players] {Describe(record)}");
		}

		public static void CaptureConnected()
		{
			foreach (var client in Server.Clients.Values)
			{
				if (!client.IsConnected) continue;
				var record = Get(client.Identity);
				if (record != null) CaptureLive(client.ID, record);
			}
		}

		private static void CaptureLive(int clientId, PlayerRecord record)
		{
			var presence = PresenceRegistry.Get(clientId);
			if (presence == null || !IsPlace(presence.Scene)) return;

			var movement = presence.LastMovement;
			if (movement == null || movement.Scene != presence.Scene || movement.Position == null) return;

			record.Scene = presence.Scene;
			record.Position = movement.Position;
			record.Rotation = movement.Rotation;
		}

		private static bool IsPlace(GameScene scene) =>
			scene != GameScene.Unknown && scene != GameScene.Loading && scene != GameScene.Menu;

		public static string ShortKey(string identity)
		{
			if (identity == null) return "none";
			if (!identity.StartsWith("guid:")) return identity;
			string key = identity.Substring(5);
			return "guid:" + (key.Length <= 8 ? key : key.Substring(0, 8));
		}

		public static string Describe(PlayerRecord record)
		{
			string place = record.Position == null
				? record.Scene.ToString()
				: $"{record.Scene} at ({record.Position.X:F2}, {record.Position.Y:F2}, {record.Position.Z:F2})";
			return $"{ShortKey(record.Key)} '{record.Name}' last seen {record.LastSeenUtc:u}, {place}";
		}

		public static IEnumerable<string> DescribeAll()
		{
			if (Records.Count == 0)
			{
				yield return "no player records";
				yield break;
			}
			foreach (var record in Records.Values.OrderByDescending(r => r.LastSeenUtc))
			{
				var client = ConnectedWith(record.Key, 0);
				yield return Describe(record) + (client != null ? $", connected as Client[{client.ID}]" : "");
			}
		}
	}
}
