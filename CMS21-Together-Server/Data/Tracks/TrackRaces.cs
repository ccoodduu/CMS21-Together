using System;
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
	/// Shared races on the race track: one countdown for every player driving there, lap times from the racers' clients,
	/// finishing order and DNFs decided here, the last results kept in <see cref="WorldState.RaceResults"/>. Running
	/// races are never saved. Callers hold <see cref="GameDataManager.StateLock"/>.
	/// </summary>
	public static class TrackRaces
	{
		public const int MinLaps = 1;
		public const int MaxLaps = 20;
		public const int StartInMs = 10_000;
		public const float TimeoutSeconds = 10 * 60f;
		public const int KeptResults = 10;

		private class Racer
		{
			public int PlayerId;
			public string Name;
			public int Laps;
			public long TotalMs;
			public long BestLapMs;
			public bool Finished;
			public RaceDnfReason Dnf;
			public bool Done => Finished || Dnf != RaceDnfReason.None;
		}

		private class Race
		{
			public int RaceId;
			public GameScene Scene;
			public int Laps;
			public int StarterId;
			public float StartAt;
			public long StartedUtcMs;
			public List<Racer> Racers;
		}

		private static readonly Dictionary<GameScene, Race> running = new Dictionary<GameScene, Race>();
		private static int nextRaceId = 1;

		private static WorldState World => GameDataManager.CurrentState.WorldState;

		public static void Initialize()
		{
			PresenceEvents.SceneChanged += OnSceneChanged;
			PresenceEvents.Left += OnLeft;
		}

		public static void Clear() => running.Clear();

		public static bool IsRaceScene(GameScene scene) => scene == GameScene.RaceTrack;

		public static bool IsRunning(GameScene scene) => running.ContainsKey(scene);

		public static int RunningRaceId(GameScene scene) => running.TryGetValue(scene, out var race) ? race.RaceId : 0;

		private static string NameOf(int playerId) =>
			PresenceRegistry.Get(playerId)?.Username ?? $"Player {playerId}";

		private static bool IsDrivingOn(int playerId, GameScene scene)
		{
			var drive = ActiveDrives.Get(playerId);
			return drive != null && drive.Scene == scene && PresenceRegistry.Get(playerId)?.Scene == scene && !Rides.IsPassenger(playerId);
		}

		public static void OnStart(int clientId, RaceStartRequestPacket packet, float now)
		{
			if (packet == null) return;
			var scene = packet.Scene;
			var refusal = RaceRefusal.None;
			if (!IsRaceScene(scene)) refusal = RaceRefusal.NotRaceTrack;
			else if (running.ContainsKey(scene)) refusal = RaceRefusal.RaceRunning;
			else if (Rides.IsPassenger(clientId)) refusal = RaceRefusal.Passenger;
			else if (!IsDrivingOn(clientId, scene)) refusal = RaceRefusal.NotDriving;
			else if (packet.Laps < MinLaps || packet.Laps > MaxLaps) refusal = RaceRefusal.LapsOutOfRange;
			if (refusal != RaceRefusal.None)
			{
				Logger.Info($"[Race] Start of a {packet.Laps}-lap race on the {TrackScenes.NameOf(scene)} by client {clientId} refused: {refusal}.");
				Server.SendToClient(new RaceRefusedPacket { Scene = scene, Laps = packet.Laps, Reason = refusal, RunningRaceId = RunningRaceId(scene) }, clientId);
				return;
			}

			int stored = World.RaceResults?.Count > 0 ? World.RaceResults.Max(r => r.RaceId) : 0;
			nextRaceId = Math.Max(nextRaceId, stored + 1);
			var race = new Race
			{
				RaceId = nextRaceId++,
				Scene = scene,
				Laps = packet.Laps,
				StarterId = clientId,
				StartAt = now + StartInMs / 1000f,
				StartedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + StartInMs,
				Racers = PresenceRegistry.All.Where(r => IsDrivingOn(r.PlayerId, scene)).OrderBy(r => r.PlayerId)
					.Select(r => new Racer { PlayerId = r.PlayerId, Name = r.Username }).ToList(),
			};
			running[scene] = race;
			var countdown = Countdown(race, StartInMs);
			int sent = 0;
			foreach (int id in PresenceRegistry.InScene(scene).ToList())
			{
				Server.SendToClient(countdown, id);
				sent++;
			}
			Logger.Info($"[Race] Race {race.RaceId} on the {TrackScenes.NameOf(scene)} started by client {clientId}: {race.Laps} laps, racers {string.Join(", ", race.Racers.Select(r => $"{r.Name} (client {r.PlayerId})"))}; countdown {StartInMs} ms sent to {sent}.");
		}

		private static RaceCountdownPacket Countdown(Race race, int startInMs) => new RaceCountdownPacket
		{
			RaceId = race.RaceId, Scene = race.Scene, Laps = race.Laps, StarterId = race.StarterId,
			Participants = race.Racers.Where(r => !r.Done).Select(r => r.PlayerId).ToList(), StartInMs = startInMs,
		};

		private static Racer RacerOf(int clientId, int raceId, out Race race)
		{
			race = running.Values.FirstOrDefault(r => r.RaceId == raceId);
			return race?.Racers.FirstOrDefault(r => r.PlayerId == clientId);
		}

		public static void OnLap(int clientId, RaceLapPacket packet, float now)
		{
			if (packet == null) return;
			var racer = RacerOf(clientId, packet.RaceId, out var race);
			string why = race == null ? "no such race running"
				: racer == null ? "not a racer"
				: racer.Done ? "already done"
				: now < race.StartAt ? "before the start"
				: packet.Lap != racer.Laps + 1 ? $"lap {packet.Lap} out of order (expected {racer.Laps + 1})"
				: !TrackRecords.InBounds(GameScene.RaceTrack, packet.LapMs) ? "out of bounds"
				: null;
			if (why != null)
			{
				Logger.Info($"[Race] Lap {packet.Lap} of {packet.LapMs} ms from client {clientId} for race {packet.RaceId} ignored ({why}).");
				return;
			}

			racer.Laps = packet.Lap;
			racer.TotalMs += packet.LapMs;
			racer.BestLapMs = racer.BestLapMs == 0 ? packet.LapMs : Math.Min(racer.BestLapMs, packet.LapMs);
			if (racer.Laps >= race.Laps) racer.Finished = true;
			Logger.Info($"[Race] Race {race.RaceId}: {racer.Name} (client {clientId}) lap {packet.Lap}/{race.Laps} in {TrackRecords.Format(GameScene.RaceTrack, packet.LapMs)}{(racer.Finished ? $", finished in {TrackRecords.Format(GameScene.RaceTrack, racer.TotalMs)}" : "")}.");
			CompleteIfDone(race);
		}

		public static void OnQuit(int clientId, RaceQuitPacket packet)
		{
			if (packet == null) return;
			var racer = RacerOf(clientId, packet.RaceId, out var race);
			if (racer == null || racer.Done)
			{
				Logger.Info($"[Race] Quit from client {clientId} for race {packet.RaceId} ignored ({(racer == null ? "not a racer of a running race" : "already done")}).");
				return;
			}
			MarkDnf(race, racer, RaceDnfReason.Quit, "restarted from the pause menu");
		}

		private static void OnSceneChanged(int clientId, GameScene from, GameScene to)
		{
			foreach (var race in running.Values.ToList())
			{
				var racer = race.Racers.FirstOrDefault(r => r.PlayerId == clientId);
				if (racer != null && !racer.Done && from == race.Scene && to != race.Scene) MarkDnf(race, racer, RaceDnfReason.LeftTrack, $"went to {to}");
			}

			if (!running.TryGetValue(to, out var watched) || watched.Racers.Any(r => r.PlayerId == clientId && !r.Done)) return;
			int startInMs = (int)Math.Round((watched.StartAt - ServerTime.Time) * 1000f);
			Server.SendToClient(Countdown(watched, startInMs), clientId);
			Logger.Info($"[Race] Client {clientId} arrived on the {TrackScenes.NameOf(to)} during race {watched.RaceId} and watches it ({startInMs} ms to the start).");
		}

		private static void OnLeft(int clientId)
		{
			foreach (var race in running.Values.ToList())
			{
				var racer = race.Racers.FirstOrDefault(r => r.PlayerId == clientId);
				if (racer != null && !racer.Done) MarkDnf(race, racer, RaceDnfReason.Disconnected, "left the game");
			}
		}

		public static void Tick(float now)
		{
			if (running.Count == 0) return;
			foreach (var race in running.Values.Where(r => now - r.StartAt >= TimeoutSeconds).ToList())
			{
				foreach (var racer in race.Racers.Where(r => !r.Done)) racer.Dnf = RaceDnfReason.Timeout;
				Logger.Info($"[Race] Race {race.RaceId} timed out {TimeoutSeconds / 60f:0} minutes after the start.");
				CompleteIfDone(race);
			}
		}

		public static void Stop()
		{
			foreach (var race in running.Values)
				Logger.Info($"[Race] Race {race.RaceId} on the {TrackScenes.NameOf(race.Scene)} ended by the server stop: {string.Join(", ", race.Racers.Where(r => !r.Done).Select(r => r.Name))} did not finish; not stored.");
			running.Clear();
		}

		private static void MarkDnf(Race race, Racer racer, RaceDnfReason reason, string why)
		{
			racer.Dnf = reason;
			Logger.Info($"[Race] Race {race.RaceId}: {racer.Name} (client {racer.PlayerId}) did not finish ({reason}: {why}) after {racer.Laps} of {race.Laps} laps.");
			CompleteIfDone(race);
		}

		private static void CompleteIfDone(Race race)
		{
			if (!race.Racers.All(r => r.Done)) return;
			running.Remove(race.Scene);
			var result = new ModRaceResult
			{
				RaceId = race.RaceId,
				Scene = race.Scene,
				Laps = race.Laps,
				StartedUtcMs = race.StartedUtcMs,
				Order = race.Racers
					.OrderBy(r => r.Finished ? 0 : 1).ThenBy(r => r.Finished ? r.TotalMs : -r.Laps).ThenBy(r => r.PlayerId)
					.Select(r => new ModRaceEntry
					{
						PlayerId = r.PlayerId, PlayerName = r.Name, Laps = r.Laps, TotalMs = r.Finished ? r.TotalMs : 0,
						BestLapMs = r.BestLapMs, Dnf = !r.Finished, DnfReason = r.Finished ? RaceDnfReason.None : r.Dnf,
					}).ToList(),
			};
			World.RaceResults ??= new List<ModRaceResult>();
			World.RaceResults.Add(result);
			while (World.RaceResults.Count > KeptResults) World.RaceResults.RemoveAt(0);
			Logger.Info($"[Race] Race {race.RaceId} result: {Summary(result)}.");
			Server.SendToClients(new RaceResultPacket { Result = result });
		}

		public static string Summary(ModRaceResult result) => string.Join(", ", result.Order.Select((e, i) => e.Dnf
			? $"DNF {e.PlayerName} ({e.DnfReason}, {e.Laps}/{result.Laps} laps)"
			: $"{i + 1}. {e.PlayerName} {TrackRecords.Format(GameScene.RaceTrack, e.TotalMs)} (best lap {TrackRecords.Format(GameScene.RaceTrack, e.BestLapMs)})"));

		public static IEnumerable<string> Describe(float now)
		{
			if (running.Count == 0) yield return "  no race running";
			foreach (var race in running.Values)
			{
				yield return $"  race {race.RaceId} on the {TrackScenes.NameOf(race.Scene)}: {race.Laps} laps, {(now < race.StartAt ? $"starts in {race.StartAt - now:0.0} s" : $"running for {now - race.StartAt:0} s")}";
				foreach (var racer in race.Racers)
					yield return $"    {racer.Name} (client {racer.PlayerId}): {racer.Laps}/{race.Laps} laps, {TrackRecords.Format(GameScene.RaceTrack, racer.TotalMs)}{(racer.Finished ? ", finished" : racer.Dnf != RaceDnfReason.None ? $", DNF {racer.Dnf}" : "")}";
			}
			var stored = World.RaceResults ?? new List<ModRaceResult>();
			yield return $"  stored results: {stored.Count}";
			foreach (var result in stored)
				yield return $"    race {result.RaceId} ({result.Laps} laps): {Summary(result)}";
		}
	}
}
