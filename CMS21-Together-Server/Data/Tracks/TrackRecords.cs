using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Tracks
{
	/// <summary>
	/// Best race-track lap and speed-track top speed per player identity (<see cref="PlayerRecord"/>) and for the group
	/// (<see cref="WorldState"/>). The value comes from the driver's client and is trusted within the bounds.
	/// Callers hold <see cref="GameDataManager.StateLock"/>.
	/// </summary>
	public static class TrackRecords
	{
		public const long MinLapMs = 10_000;
		public const long MaxLapMs = 30 * 60_000;
		public const long MinTopSpeedKmh = 1;
		public const long MaxTopSpeedKmh = 700;

		private static WorldState World => GameDataManager.CurrentState.WorldState;

		public static bool IsRecordScene(GameScene scene) => scene == GameScene.RaceTrack || scene == GameScene.SpeedTrack;

		public static bool InBounds(GameScene scene, long value) => scene == GameScene.RaceTrack
			? value >= MinLapMs && value <= MaxLapMs
			: value >= MinTopSpeedKmh && value <= MaxTopSpeedKmh;

		public static bool Beats(GameScene scene, long value, long best) =>
			best <= 0 || (scene == GameScene.RaceTrack ? value < best : value > best);

		public static long PersonalBest(PlayerRecord record, GameScene scene) =>
			record == null ? 0 : scene == GameScene.RaceTrack ? record.BestLapMs : record.TopSpeedKmh;

		public static ModTrackRecord GroupBest(GameScene scene) => scene == GameScene.RaceTrack ? World.GroupBestLap : World.GroupTopSpeed;

		public static string Format(GameScene scene, long value) => scene == GameScene.RaceTrack
			? $"{value / 60000}:{value / 1000 % 60:00}.{value % 1000:000}"
			: $"{value} km/h";

		public static void OnRecord(int clientId, TrackRecordPacket packet)
		{
			if (packet == null) return;
			Server.Clients.TryGetValue(clientId, out var client);
			var record = PlayerRecords.Get(client?.Identity);
			var scene = packet.Scene;
			long value = packet.Value;
			string name = PresenceRegistry.Get(clientId)?.Username ?? record?.Name ?? $"Player {clientId}";

			if (record == null || !IsRecordScene(scene) || !InBounds(scene, value))
			{
				Logger.Info($"[Tracks] {scene} value {value} from client {clientId} ignored ({(record == null ? "no player record" : !IsRecordScene(scene) ? "no records on this scene" : "out of bounds")}).");
				Server.SendToClient(new TrackRecordUpdatePacket { Scene = scene, PlayerId = clientId, PlayerName = name, Value = PersonalBest(record, scene) }, clientId);
				return;
			}

			bool personal = Beats(scene, value, PersonalBest(record, scene));
			if (!personal)
			{
				Logger.Info($"[Tracks] {scene} {Format(scene, value)} by client {clientId} is not a personal best ({Format(scene, PersonalBest(record, scene))}).");
				Server.SendToClient(new TrackRecordUpdatePacket { Scene = scene, PlayerId = clientId, PlayerName = name, Value = PersonalBest(record, scene) }, clientId);
				return;
			}
			if (scene == GameScene.RaceTrack) record.BestLapMs = value;
			else record.TopSpeedKmh = (int)value;

			var group = GroupBest(scene);
			bool groupRecord = Beats(scene, value, group?.Value ?? 0);
			var update = new TrackRecordUpdatePacket { Scene = scene, PlayerId = clientId, PlayerName = name, Value = value, IsGroupRecord = groupRecord, IsPersonalBest = true };
			if (groupRecord)
			{
				var best = new ModTrackRecord { PlayerName = name, Value = value };
				if (scene == GameScene.RaceTrack) World.GroupBestLap = best;
				else World.GroupTopSpeed = best;
				Logger.Info($"[Tracks] New group record on the {TrackScenes.NameOf(scene)}: {name} (client {clientId}), {Format(scene, value)}.");
				Server.SendToClients(update);
				return;
			}
			Logger.Info($"[Tracks] Personal best on the {TrackScenes.NameOf(scene)} for {name} (client {clientId}): {Format(scene, value)}.");
			Server.SendToClient(update, clientId);
		}

		public static IEnumerable<string> Describe()
		{
			foreach (var scene in new[] { GameScene.RaceTrack, GameScene.SpeedTrack })
			{
				var group = GroupBest(scene);
				yield return $"  {TrackScenes.NameOf(scene)} group record: {(group == null ? "none" : $"{group.PlayerName}, {Format(scene, group.Value)}")}";
				foreach (var record in GameDataManager.CurrentState.PlayerRecords.Values.Where(r => PersonalBest(r, scene) > 0).OrderBy(r => r.Name))
					yield return $"    {PlayerRecords.ShortKey(record.Key)} '{record.Name}': {Format(scene, PersonalBest(record, scene))}";
			}
		}
	}
}
