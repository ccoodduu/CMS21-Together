using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using Newtonsoft.Json;

namespace CMS21_Together_Server.Data.Jobs
{
	// --check-jobs: the job DTOs survive a BinaryFormatter (network) and a Newtonsoft (save) round trip unchanged.
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
			state.ActiveJobs.Add(new ActiveJobEntry { Job = job, OrderJob = job, CarLoaderId = 1, OriginalSeconds = 300f });

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
			return ok ? 0 : 1;
		}
	}
}
