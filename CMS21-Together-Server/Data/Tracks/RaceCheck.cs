using System;
using System.Linq;
using System.Reflection;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Tracks
{
	// --check-races: the start rules, the finishing order from the lap times, each DNF rule (quit, leaving the track,
	// disconnect, timeout), ignored laps, watching a race after arriving late and keeping the last ten results,
	// in-process with unconnected client slots.
	public static class RaceCheck
	{
		private const int A = 1;
		private const int B = 2;
		private const int C = 3;

		private static int failures;
		private static float now;

		public static int Run()
		{
			PacketRouter.Initialize(Assembly.GetExecutingAssembly());
			GameDataManager.UseStateForCheck(new ModGameState());
			foreach (int id in new[] { A, B, C })
			{
				Server.Clients[id] = new Client(id) { ConnectionType = NetworkType.DirectIP };
				PresenceRegistry.Add(id, id == A ? "Ann" : id == B ? "Bob" : "Cid");
			}
			TrackRaces.Initialize();

			Check("a start by a player who is not driving there is refused", !Start(A, 1) && !TrackRaces.IsRunning(GameScene.RaceTrack));
			Drive(A);
			Drive(B);
			Check("a start on the speed track is refused", !Start(A, 1, GameScene.SpeedTrack));
			Check("0 laps are refused", !Start(A, 0));
			Check("21 laps are refused", !Start(A, 21));

			Check("A starts a one-lap race", Start(A, 1));
			int race = TrackRaces.RunningRaceId(GameScene.RaceTrack);
			Check("a second start on the running track is refused", !Start(B, 1) && TrackRaces.RunningRaceId(GameScene.RaceTrack) == race);
			Drive(C);
			Check("a lap from C, who started driving after the start, does not count", Lap(C, race, 1, 80_000) && TrackRaces.IsRunning(GameScene.RaceTrack));
			now = 5f;
			Lap(A, race, 1, 90_000);
			Check("a lap before the start is ignored", TrackRaces.IsRunning(GameScene.RaceTrack) && Results() == 0);
			now = 120f;
			Lap(A, race, 2, 90_000);
			Lap(A, race, 1, 5_000);
			Check("an out-of-order and a 5 s lap are ignored", TrackRaces.IsRunning(GameScene.RaceTrack) && Results() == 0);
			Lap(B, race, 1, 95_000);
			Check("the race waits for A after B finished", TrackRaces.IsRunning(GameScene.RaceTrack));
			Lap(A, race, 1, 90_000);
			var result = Last();
			Check("the race ends when both finished", !TrackRaces.IsRunning(GameScene.RaceTrack) && Results() == 1);
			Check("the faster lap wins: Bob 1:35 after his lap, Ann 1:30", result.Order.Select(e => e.PlayerName).SequenceEqual(new[] { "Ann", "Bob" })
				&& result.Order[0].TotalMs == 90_000 && result.Order[1].TotalMs == 95_000 && result.Order.All(e => !e.Dnf));

			Move(C, GameScene.RaceTrack, GameScene.Garage);
			Start(A, 2);
			race = TrackRaces.RunningRaceId(GameScene.RaceTrack);
			now += 20f;
			Lap(A, race, 1, 60_000);
			Move(B, GameScene.RaceTrack, GameScene.Loading);
			Check("the race runs on after B leaves the track", TrackRaces.IsRunning(GameScene.RaceTrack));
			Lap(A, race, 2, 70_000);
			result = Last();
			Check("B who left the track is DNF, A finished with 2:10", result.RaceId == race && result.Order[0].PlayerName == "Ann" && result.Order[0].TotalMs == 130_000
				&& result.Order[0].BestLapMs == 60_000 && result.Order.Any(e => e.PlayerName == "Bob" && e.Dnf && e.DnfReason == RaceDnfReason.LeftTrack));

			Move(B, GameScene.Loading, GameScene.RaceTrack);
			Drive(B);
			Drive(C);
			Start(A, 1);
			race = TrackRaces.RunningRaceId(GameScene.RaceTrack);
			TrackRaces.OnQuit(B, new RaceQuitPacket { RaceId = race });
			PresenceEvents.RaiseLeft(C);
			Move(C, GameScene.RaceTrack, GameScene.Garage);
			Check("a quit and a disconnect are DNF, the race waits for A", TrackRaces.IsRunning(GameScene.RaceTrack));
			TrackRaces.Tick(now + TrackRaces.StartInMs / 1000f + TrackRaces.TimeoutSeconds - 1f);
			Check("the race still runs a second before the timeout", TrackRaces.IsRunning(GameScene.RaceTrack));
			TrackRaces.Tick(now + TrackRaces.StartInMs / 1000f + TrackRaces.TimeoutSeconds + 1f);
			result = Last();
			Check("the timeout ends the race: A timeout, B quit, Cid disconnected", !TrackRaces.IsRunning(GameScene.RaceTrack) && result.RaceId == race
				&& Reason(result, "Ann") == RaceDnfReason.Timeout && Reason(result, "Bob") == RaceDnfReason.Quit && Reason(result, "Cid") == RaceDnfReason.Disconnected);

			Start(A, 1);
			race = TrackRaces.RunningRaceId(GameScene.RaceTrack);
			Move(C, GameScene.Loading, GameScene.RaceTrack);
			Check("C arriving during the race watches it", TrackRaces.IsRunning(GameScene.RaceTrack));
			TrackRaces.OnQuit(A, new RaceQuitPacket { RaceId = race });
			TrackRaces.OnQuit(B, new RaceQuitPacket { RaceId = race });
			Check("the race ends without waiting for the watcher", !TrackRaces.IsRunning(GameScene.RaceTrack) && Last().Order.Count == 2);

			for (int i = 0; i < 12; i++)
			{
				Start(A, 1);
				TrackRaces.OnQuit(A, new RaceQuitPacket { RaceId = TrackRaces.RunningRaceId(GameScene.RaceTrack) });
				TrackRaces.OnQuit(B, new RaceQuitPacket { RaceId = TrackRaces.RunningRaceId(GameScene.RaceTrack) });
			}
			var stored = GameDataManager.CurrentState.WorldState.RaceResults;
			Check($"only the last {TrackRaces.KeptResults} results are kept, race ids rising", stored.Count == TrackRaces.KeptResults
				&& stored.Select(r => r.RaceId).SequenceEqual(stored.Select(r => r.RaceId).OrderBy(id => id).Distinct()));

			Start(A, 1);
			TrackRaces.Stop();
			Check("a server stop ends the running race without storing it", !TrackRaces.IsRunning(GameScene.RaceTrack) && stored.Count == TrackRaces.KeptResults);

			Drive(C);
			Start(C, 1);
			var grid = TrackRaces.GridOf(GameScene.RaceTrack);
			Check($"the grid puts the starter Cid first, then Ann and Bob in their order of arrival ({string.Join(", ", grid)})", grid.SequenceEqual(new[] { C, A, B }));
			TrackRaces.Stop();
			Move(A, GameScene.RaceTrack, GameScene.Garage);
			Move(A, GameScene.Garage, GameScene.RaceTrack);
			Drive(A);
			Start(B, 1);
			grid = TrackRaces.GridOf(GameScene.RaceTrack);
			Check($"Ann, back on the track last, starts behind Cid when Bob starts ({string.Join(", ", grid)})", grid.SequenceEqual(new[] { B, C, A }));
			TrackRaces.Stop();

			foreach (int id in new[] { A, B, C })
			{
				Server.Clients.Remove(id);
				PresenceRegistry.Remove(id);
			}
			Console.WriteLine($"race check: {(failures == 0 ? "OK" : $"FAILED ({failures})")}");
			return failures == 0 ? 0 : 1;
		}

		private static void Drive(int id)
		{
			var record = PresenceRegistry.Get(id);
			if (record.Scene != GameScene.RaceTrack) PresenceRegistry.SetScene(record, GameScene.RaceTrack);
			ActiveDrives.Begin(new CarDriveStartPacket { PlayerId = id, Scene = GameScene.RaceTrack, DriveId = id });
		}

		private static void Move(int id, GameScene from, GameScene to)
		{
			PresenceRegistry.SetScene(PresenceRegistry.Get(id), to);
			if (to != GameScene.RaceTrack) ActiveDrives.End(id, out _);
		}

		private static bool Start(int id, int laps, GameScene scene = GameScene.RaceTrack)
		{
			int before = TrackRaces.RunningRaceId(scene);
			TrackRaces.OnStart(id, new RaceStartRequestPacket { Scene = scene, Laps = laps }, now);
			int after = TrackRaces.RunningRaceId(scene);
			return after != 0 && after != before;
		}

		private static bool Lap(int id, int race, int lap, long ms)
		{
			TrackRaces.OnLap(id, new RaceLapPacket { RaceId = race, Lap = lap, LapMs = ms }, now);
			return true;
		}

		private static int Results() => GameDataManager.CurrentState.WorldState.RaceResults.Count;

		private static ModRaceResult Last() => GameDataManager.CurrentState.WorldState.RaceResults.LastOrDefault() ?? new ModRaceResult();

		private static RaceDnfReason Reason(ModRaceResult result, string name) =>
			result.Order.FirstOrDefault(e => e.PlayerName == name)?.DnfReason ?? RaceDnfReason.None;

		private static void Check(string what, bool ok)
		{
			Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
			if (!ok) failures++;
		}
	}
}
