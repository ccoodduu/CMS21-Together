using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Presence;
using CMS21_Together_Server.Log;
using CMS21_Together_Server.Network;

namespace CMS21_Together_Server.Data.Jobs
{
	public enum JobStatsRule
	{
		Contributors,
		Garage,
		Finisher
	}

	// shared-job-achievements D1-D3: who worked on an active job (player short keys, kept with the job), and the Steam
	// stats of a finished job for them. Callers hold StateLock.
	public static class JobContributors
	{
		public const string FinishOrder = "stat_finish_order";
		public const string BonusExp = "stat_bonus_exp";
		public const string BonusMoney = "stat_bonus_money";

		public static JobStatsRule Rule { get; set; } = JobStatsRule.Contributors;

		private static JobsState State => GameDataManager.CurrentState.JobsState;

		public static bool TryParseRule(string value, out JobStatsRule rule) =>
			Enum.TryParse((value ?? "").Trim(), true, out rule) && Enum.IsDefined(typeof(JobStatsRule), rule);

		public static void OnCarChange(int loader, int clientId, string what)
		{
			var active = JobOfCar(loader);
			if (active != null) Add(active, KeyOf(clientId), what);
		}

		public static void OnJobPlayer(ActiveJobEntry active, int clientId, string what) => Add(active, KeyOf(clientId), what);

		public static ActiveJobEntry JobOfCar(int loader)
		{
			if (!GameDataManager.CurrentState.CarState.LoadedCars.TryGetValue(loader, out var car) || car.Spawn?.IsJob != true) return null;
			return State.ActiveJobs.FirstOrDefault(j => j.Job.id == car.Spawn.JobID);
		}

		public static bool Add(ActiveJobEntry active, string key, string what)
		{
			if (active == null || key == null) return false;
			active.Contributors ??= new List<string>();
			if (active.Contributors.Contains(key)) return false;
			active.Contributors.Add(key);
			Logger.Info($"[Jobs] Job {active.Job.id}: {key} works on it ({what}).");
			return true;
		}

		private static string KeyOf(int clientId) =>
			Server.Clients.TryGetValue(clientId, out var client) && client.Identity != null ? PlayerRecords.ShortKey(client.Identity) : null;

		public static JobStatsAwardPacket StatsOf(ActiveJobEntry active, bool completed)
		{
			var award = new JobStatsAwardPacket { JobId = active.Job.id, MissionFinished = active.Job.IsMission };
			if (completed)
			{
				award.Stats.Add(FinishOrder);
				if (active.Job.BonusToExp) award.Stats.Add(BonusExp);
				if (active.Job.BonusToMoney) award.Stats.Add(BonusMoney);
			}
			return award;
		}

		public static List<int> Receivers(ActiveJobEntry active, int finisherId, JobStatsRule rule)
		{
			var receivers = new List<int>();
			if (rule == JobStatsRule.Finisher) return receivers;
			var keys = new HashSet<string>(active.Contributors ?? new List<string>());
			foreach (var client in Server.Clients.Values)
			{
				if (client.ID == finisherId || !client.IsConnected || client.SyncState != SyncState.InSession || client.Identity == null) continue;
				bool receives = rule == JobStatsRule.Garage
					? PresenceRegistry.Get(client.ID)?.Scene == GameScene.Garage
					: keys.Contains(PlayerRecords.ShortKey(client.Identity));
				if (receives) receivers.Add(client.ID);
			}
			return receivers;
		}

		public static void Award(ActiveJobEntry active, int finisherId, bool completed)
		{
			var award = StatsOf(active, completed);
			if (award.Stats.Count == 0 && !award.MissionFinished) return;
			var receivers = Receivers(active, finisherId, Rule);
			Logger.Info($"[Jobs] Job {active.Job.id} stats [{string.Join(", ", award.Stats)}]{(award.MissionFinished ? " + mission" : "")} ({Rule.ToString().ToLowerInvariant()}): " +
			            (receivers.Count == 0 ? "nobody besides the finisher." : $"clients {string.Join(", ", receivers)}."));
			foreach (int clientId in receivers) Server.SendToClient(award, clientId);
		}

		public static IEnumerable<string> Describe(int jobId)
		{
			var active = State.ActiveJobs.FirstOrDefault(j => j.Job.id == jobId);
			if (active == null)
			{
				yield return $"no active job {jobId}";
				yield break;
			}
			yield return $"job {jobId}: {(active.Contributors == null || active.Contributors.Count == 0 ? "no contributors" : string.Join(", ", active.Contributors))}; stats go to {Rule.ToString().ToLowerInvariant()}";
		}
	}
}
