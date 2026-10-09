using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Data.Jobs
{
	// --check-jobs: the job DTOs survive a BinaryFormatter (network) and a Newtonsoft (save) round trip unchanged, and
	// JobContributors (shared-job-achievements D1, D2) on a synthetic state, without clients.
	public static class JobsCheck
	{
		public static int Run()
		{
			var job = new ModJob
			{
				id = 7, carFile = "car_boltatlanta", configVersion = 2, timeToEnd = 123.5f, PaintType = ModPaintType.Metallic,
				carColor = new ModColor { r = 0.1f, g = 0.2f, b = 0.3f, a = 1f }, jobType = new[] { true, false, true },
				IsMission = true, MissionID = 3, TotalPayout = 1500, XP = 90, IconTypeBrakes = true, PrepSeed = -123456789,
			};
			job.jobTasks.Add(new ModJobTask { type = "brakes", subtype = "front", moneySpent = 40, Done = true });
			job.jobTasks[0].Parts.Add(new ModJobPart { ID = "tarcza_1", Name = "Brake disc", Done = true, Found = true });
			var state = new JobsState { NextJobId = 8, Missions = new ModMissionState { MissionsFinished = 2, IsStoryMissionInProgress = true } };
			state.Orders.Add(new OrderEntry { Job = job, RemainingSeconds = 99f });
			state.ActiveJobs.Add(new ActiveJobEntry { Job = job, OrderJob = job, CarLoaderId = 1, OriginalSeconds = 300f, Contributors = { "guid:1a2b3c4d", "steam:76561190000000001" } });

			string original = JsonConvert.SerializeObject(state);
			var formatter = new BinaryFormatter();
			JobsState viaBinary;
			using (var stream = new MemoryStream())
			{
				formatter.Serialize(stream, state);
				stream.Position = 0;
				viaBinary = (JobsState)formatter.Deserialize(stream);
			}
			string binary = JsonConvert.SerializeObject(viaBinary);
			string json = JsonConvert.SerializeObject(JsonConvert.DeserializeObject<JobsState>(original));

			bool ok = binary == original && json == original;
			Console.WriteLine($"jobs check: BinaryFormatter {(binary == original ? "same" : "DIFFERENT")}, Newtonsoft {(json == original ? "same" : "DIFFERENT")} -> {(ok ? "OK" : "FAILED")}");
			int contributorFailures = Contributors();
			return ok && contributorFailures == 0 ? 0 : 1;
		}

		private static int Contributors()
		{
			int failures = 0;
			void Expect(bool condition, string what)
			{
				Console.WriteLine($"{(condition ? "ok" : "FAILED")}: {what}");
				if (!condition) failures++;
			}

			const int startLoader = 0, movedLoader = 2, otherLoader = 1;
			var state = new ModGameState();
			var job = new ModJob { id = 5, BonusToMoney = true };
			state.JobsState.ActiveJobs.Add(new ActiveJobEntry { Job = job, CarLoaderId = startLoader });
			state.CarState.LoadedCars[movedLoader] = new CarLoaderEntry { Spawn = new CarSpawnResponsePacket { IsJob = true, JobID = 5 } };
			state.CarState.LoadedCars[otherLoader] = new CarLoaderEntry { Spawn = new CarSpawnResponsePacket { IsJob = false } };
			GameDataManager.UseStateForCheck(state);
			var active = state.JobsState.ActiveJobs[0];

			Expect(JobContributors.Add(active, "guid:aaaa1111", "took it"), "the taker becomes a contributor");
			Expect(JobContributors.JobOfCar(movedLoader) == active, "the job is found from the car's spawn record on the loader it moved to");
			Expect(JobContributors.JobOfCar(startLoader) == null, "the loader the job started on, now empty, names no job");
			Expect(JobContributors.JobOfCar(otherLoader) == null, "a car that is not a job car names no job");
			Expect(!JobContributors.Add(active, "guid:aaaa1111", "part change"), "a contributor is kept once");
			Expect(JobContributors.Add(JobContributors.JobOfCar(movedLoader), "guid:bbbb2222", "part change") && active.Contributors.SequenceEqual(new[] { "guid:aaaa1111", "guid:bbbb2222" }), "a second player on the moved car is added to that job");
			Expect(JobContributors.StatsOf(active, true).Stats.SequenceEqual(new[] { JobContributors.FinishOrder, JobContributors.BonusMoney }), "a completed job with a money bonus awards the finished order and the money bonus");
			Expect(JobContributors.StatsOf(active, false).Stats.Count == 0 && !JobContributors.StatsOf(active, false).MissionFinished, "an uncompleted job awards nothing");
			state.JobsState.ActiveJobs.Clear();
			Expect(JobContributors.JobOfCar(movedLoader) == null, "a job that is no longer active takes no contributors");

			var oldSave = JsonConvert.DeserializeObject<JobsState>("{\"ActiveJobs\":[{\"Job\":{\"id\":3},\"CarLoaderId\":1}]}");
			Expect(oldSave.ActiveJobs[0].Contributors != null && oldSave.ActiveJobs[0].Contributors.Count == 0, "a jobs section of version 1 loads with no contributors");
			return failures;
		}
	}
}
