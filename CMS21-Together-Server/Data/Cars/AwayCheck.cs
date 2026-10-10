using System;
using System.Reflection;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Data.Tracks;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Cars
{
	// --check-away: the release rules of a track claim for each of the three tracks, the 15-minute watchdog that only
	// garage activities get, and the lap and top speed bounds, in-process with unconnected client slots.
	public static class AwayCheck
	{
		private const int Loader = 0;
		private const int A = 1;
		private const int B = 2;

		private static int failures;
		private static int nextRequest = 1;

		public static int Run()
		{
			PacketRouter.Initialize(Assembly.GetExecutingAssembly());
			var state = new ModGameState();
			state.CarState.LoadedCars[Loader] = new CarLoaderEntry { SpawnSeq = 1, HasBaseline = true };
			GameDataManager.UseStateForCheck(state);
			CarLocks.Reset();
			Server.Clients[A] = new Client(A) { ConnectionType = NetworkType.DirectIP };
			Server.Clients[B] = new Client(B) { ConnectionType = NetworkType.DirectIP };
			CarAwayRegistry.Initialize();

			for (int i = 0; i < TrackScenes.All.Count; i++)
			{
				var track = TrackScenes.All[i];
				string k = track.Kind.ToString();
				Check($"{k}: granted from the garage", Ask(A, track.Kind) && Owner() == A);
				Check($"{k}: B is refused while A holds it", !Ask(B, track.Kind) && Owner() == A);
				Move(A, GameScene.Garage, GameScene.Loading);
				Move(A, GameScene.Loading, track.Scene);
				Check($"{k}: kept while A drives on the {track.Name}", Owner() == A);
				CarAwayRegistry.Tick(ServerTime.Time + 16 * 60f);
				Check($"{k}: the 15-minute garage watchdog does not apply", Owner() == A);
				Move(A, track.Scene, GameScene.Loading);
				Move(A, GameScene.Loading, GameScene.Garage);
				Check($"{k}: kept right after the return to the garage", Owner() == A);
				CarAwayRegistry.Tick(ServerTime.Time + 30f);
				Check($"{k}: kept 30 s after the return", Owner() == A);
				CarAwayRegistry.Tick(ServerTime.Time + 61f);
				Check($"{k}: released 60 s after the return", Owner() == -1);

				Ask(A, track.Kind);
				Move(A, GameScene.Garage, GameScene.Loading);
				Move(A, GameScene.Loading, track.Scene);
				Move(A, track.Scene, GameScene.Loading);
				Move(A, GameScene.Loading, GameScene.Junkyard);
				Check($"{k}: released when A goes elsewhere", Owner() == -1);

				Ask(A, track.Kind);
				Move(A, GameScene.Garage, GameScene.Loading);
				var other = TrackScenes.All[(i + 1) % TrackScenes.All.Count];
				Move(A, GameScene.Loading, other.Scene);
				Check($"{k}: released when A arrives on the {other.Name} instead", Owner() == -1);

				Ask(A, track.Kind);
				Move(A, GameScene.Garage, GameScene.Loading);
				Move(A, GameScene.Loading, track.Scene);
				PresenceEvents.RaiseLeft(B);
				Check($"{k}: kept when another player leaves", Owner() == A);
				PresenceEvents.RaiseLeft(A);
				Check($"{k}: released when A disconnects", Owner() == -1);
				Check($"{k}: a track kind", TrackScenes.IsTrackKind(track.Kind) && TrackScenes.SceneOf(track.Kind) == track.Scene);
			}

			Ask(A, CarAwayKind.Dyno);
			CarAwayRegistry.Tick(ServerTime.Time + 16 * 60f);
			Check("Dyno: the 15-minute watchdog still releases a garage activity", Owner() == -1);
			Check("Dyno and PathTest are not track kinds", !TrackScenes.IsTrackKind(CarAwayKind.Dyno) && !TrackScenes.IsTrackKind(CarAwayKind.PathTest));

			Check("a 95 s lap is in bounds", TrackRecords.InBounds(GameScene.RaceTrack, 95_000));
			Check("a 5 s lap is out of bounds", !TrackRecords.InBounds(GameScene.RaceTrack, 5_000));
			Check("a 31 min lap is out of bounds", !TrackRecords.InBounds(GameScene.RaceTrack, 31 * 60_000));
			Check("a lower lap beats the best, a higher one does not", TrackRecords.Beats(GameScene.RaceTrack, 94_000, 95_000) && !TrackRecords.Beats(GameScene.RaceTrack, 96_000, 95_000));
			Check("an equal lap does not beat the best", !TrackRecords.Beats(GameScene.RaceTrack, 95_000, 95_000));
			Check("any lap beats no best", TrackRecords.Beats(GameScene.RaceTrack, 600_000, 0));
			Check("a higher top speed beats the best", TrackRecords.Beats(GameScene.SpeedTrack, 251, 250) && !TrackRecords.Beats(GameScene.SpeedTrack, 249, 250));
			Check("a 0 km/h top speed is out of bounds", !TrackRecords.InBounds(GameScene.SpeedTrack, 0));
			Check("the test track keeps no records", !TrackRecords.IsRecordScene(GameScene.TestTrack));
			Check("a lap is shown as m:ss.fff", TrackRecords.Format(GameScene.RaceTrack, 83_456) == "1:23.456");

			Server.Clients.Remove(A);
			Server.Clients.Remove(B);
			Console.WriteLine($"away check: {(failures == 0 ? "OK" : $"FAILED ({failures})")}");
			return failures == 0 ? 0 : 1;
		}

		private static bool Ask(int client, CarAwayKind kind)
		{
			CarAwayRegistry.OnRequest(client, new CarAwayRequestPacket { RequestId = nextRequest++, CarLoaderID = Loader, SpawnSeq = 1, Kind = kind }, ServerTime.Time);
			return CarAwayRegistry.OwnerOf(Loader) == client;
		}

		private static int Owner() => CarAwayRegistry.OwnerOf(Loader);

		private static void Move(int client, GameScene from, GameScene to) => PresenceEvents.RaiseSceneChanged(client, from, to);

		private static void Check(string what, bool ok)
		{
			Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
			if (!ok) failures++;
		}
	}
}
