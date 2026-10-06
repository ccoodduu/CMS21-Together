using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;

namespace CMS21_Together_Server.Data.Presence
{
	/// <summary>Presence records of connected players. Callers hold <see cref="GameDataManager.StateLock"/>.</summary>
	public static class PresenceRegistry
	{
		private const int MaxNameLength = 24;

		private static Dictionary<int, PlayerPresenceRecord> Records => GameDataManager.CurrentState.PlayerState.Records;

		public static PlayerPresenceRecord Get(int playerId) => Records.TryGetValue(playerId, out var record) ? record : null;

		public static IEnumerable<PlayerPresenceRecord> All => Records.Values;

		public static IEnumerable<int> InScene(GameScene scene) => Records.Values.Where(r => r.Scene == scene).Select(r => r.PlayerId);

		public static PlayerPresenceRecord Add(int playerId, string requestedName)
		{
			var record = new PlayerPresenceRecord
			{
				PlayerId = playerId,
				Username = UniqueName(playerId, requestedName),
				Scene = GameScene.Loading
			};
			Records[playerId] = record;
			return record;
		}

		public static bool Remove(int playerId) => Records.Remove(playerId);

		public static void SetScene(PlayerPresenceRecord record, GameScene scene)
		{
			if (record.Scene == scene) return;
			var from = record.Scene;
			record.Scene = scene;
			PresenceEvents.RaiseSceneChanged(record.PlayerId, from, scene);
		}

		private static string UniqueName(int playerId, string requested)
		{
			string name = new string((requested ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
			if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).Trim();
			if (name.Length == 0) name = $"Player{playerId}";

			var taken = new HashSet<string>(Records.Values.Where(r => r.PlayerId != playerId).Select(r => r.Username), StringComparer.OrdinalIgnoreCase);
			if (!taken.Contains(name)) return name;
			for (int i = 2; ; i++)
			{
				string candidate = $"{name} ({i})";
				if (!taken.Contains(candidate)) return candidate;
			}
		}
	}

	public static class PresenceEvents
	{
		public static event Action<int, GameScene, GameScene> SceneChanged;
		public static event Action<int> Left;

		internal static void RaiseSceneChanged(int playerId, GameScene from, GameScene to) => SceneChanged?.Invoke(playerId, from, to);
		internal static void RaiseLeft(int playerId) => Left?.Invoke(playerId);
	}
}
