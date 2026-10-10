using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Hook;
using CMS21Together.Network;
using CMS21Together.UI;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Jobs;

// sync-orders-and-jobs D9: the client's mirror of the server's orders and active jobs, applied to the game in one
// place under the apply guard that every job hook checks.
public static class JobsSync
{
	private const float TakeTimeoutSeconds = 45f;
	private const float MissionAnswerSeconds = 5f;

	private static JobsState mirror;
	private static int applyDepth;
	private static float missionAskedAt = float.NegativeInfinity;
	private static int missionTakenElsewhere = -1;

	public static bool IsGenerator { get; private set; }
	public static bool IsApplying => applyDepth > 0;
	public static bool MissionPending => Time.realtimeSinceStartup - missionAskedAt < MissionAnswerSeconds;
	public static int PendingTake { get; private set; } = -1;
	public static bool AcceptBypass { get; private set; }
	public static bool ServingRequest { get; private set; }
	public static int RequestId { get; private set; }
	private static bool madeOrder;

	private static OrderGenerator Generator => Singleton<GameManager>.Instance?.OrderGenerator;

	private static bool CanApply => ClientScene.IsGarageReady && Generator != null;

	public static void Reset()
	{
		mirror = null;
		PendingTake = -1;
		IsGenerator = false;
		missionAskedAt = float.NegativeInfinity;
		missionTakenElsewhere = -1;
	}

	public static void MissionAsked() => missionAskedAt = Time.realtimeSinceStartup;

	public static void OnGeneratorLoaded()
	{
		if (mirror != null && Client.Instance.IsConnectionValid) ApplyFull();
	}

	// Packets

	public static void OnState(JobsStatePacket packet, int snapshotId)
	{
		mirror = packet.State ?? new JobsState();
		IsGenerator = packet.IsGenerator;
		missionAskedAt = float.NegativeInfinity;
		missionTakenElsewhere = -1;
		if (CanApply) ApplyFull();
		SyncTracker.Applied(SyncOrder.JobsKey, snapshotId);
	}

	public static void OnRole(OrderGeneratorRolePacket packet)
	{
		IsGenerator = packet.IsGenerator;
		Log.Info($"[Jobs] Order generator: {(IsGenerator ? "this client" : "another client")}.");
		if (mirror != null && CanApply) OfferMission();
	}

	public static void OnOrderRequest(OrderRequestPacket packet)
	{
		var reason = !IsGenerator || !CanApply || !NotificationCenter.IsGameReady ? OrderRequestReason.NotReady
			: !GameSettings.CanGenerateOrders ? OrderRequestReason.Disabled
			: PendingTake >= 0 || IsApplying ? OrderRequestReason.Busy
			: GenerateOrder(packet.RequestId) ? OrderRequestReason.None
			: OrderRequestReason.NoCar;
		if (reason != OrderRequestReason.None) AnswerRequest(packet.RequestId, reason);
	}

	public static void AnswerRequest(int requestId, OrderRequestReason reason)
	{
		string text = $"[Jobs] Order request {requestId}: no order ({reason}).";
		if (reason == OrderRequestReason.NoCar || reason == OrderRequestReason.Disabled) Log.Info(text);
		else Log.Debug(text);
		Client.Instance.Send(new OrderGeneratedPacket { RequestId = requestId, Reason = reason, MaxOpenOrders = GlobalData.GetMaxOrdersAmount() });
	}

	public static void OrderSent() => madeOrder = true;

	public static bool GenerateOrder(int requestId)
	{
		var generator = Generator;
		madeOrder = false;
		ServingRequest = true;
		RequestId = requestId;
		try { generator.GenerateNewJob(); }
		finally
		{
			ServingRequest = false;
			RequestId = 0;
			GlobalData.Jobs = generator.jobs?.Count ?? 0;
		}
		return madeOrder;
	}

	public static void OnOrderAdded(OrderAddedPacket packet)
	{
		if (mirror == null) return;
		mirror.ActiveJobs.RemoveAll(a => a.Job.id == packet.Job.id);
		mirror.Orders.RemoveAll(o => o.Job.id == packet.Job.id);
		mirror.Orders.Add(new OrderEntry { Job = packet.Job, RemainingSeconds = packet.RemainingSeconds });
		if (packet.Job.IsMission)
		{
			mirror.Missions.CurrentMissionDone = false;
			missionAskedAt = float.NegativeInfinity;
			missionTakenElsewhere = -1;
		}
		if (CanApply) ApplyFull();
	}

	public static void OnJobStarted(JobStartedPacket packet)
	{
		if (mirror == null) return;
		if (packet.JobId == missionTakenElsewhere) missionTakenElsewhere = -1;
		mirror.Orders.RemoveAll(o => o.Job.id == packet.JobId);
		mirror.ActiveJobs.RemoveAll(a => a.Job.id == packet.JobId);
		mirror.ActiveJobs.Add(new ActiveJobEntry { Job = packet.Job, CarLoaderId = packet.CarLoaderId });
		if (packet.Missions != null) mirror.Missions = packet.Missions;
		if (CanApply) ApplyFull();
	}

	public static void OnJobRemoved(JobRemovedPacket packet)
	{
		if (mirror == null) return;
		if (packet.Reason == JobRemovedReason.Taken && mirror.Orders.Any(o => o.Job.id == packet.JobId && o.Job.IsMission)) missionTakenElsewhere = packet.JobId;
		else if (packet.JobId == missionTakenElsewhere) missionTakenElsewhere = -1;
		mirror.Orders.RemoveAll(o => o.Job.id == packet.JobId);
		mirror.ActiveJobs.RemoveAll(a => a.Job.id == packet.JobId);
		if (packet.Missions != null) mirror.Missions = packet.Missions;
		if (packet.Reason == JobRemovedReason.TakeAborted && PendingTake == packet.JobId) PendingTake = -1;
		if ((packet.Reason == JobRemovedReason.Ended || packet.Reason == JobRemovedReason.TakeAborted) && packet.CarLoaderId >= 0 && !JobEndContext.IsActive)
			DeleteCar(packet.CarLoaderId);
		if (CanApply) ApplyFull();
	}

	public static void OnActionResult(OrderActionResultPacket packet)
	{
		if (packet.Action == OrderActionType.Decline && !packet.Approved)
		{
			Log.Info($"[Jobs] Decline of order {packet.JobId} refused: {packet.Reason}.");
			if (packet.Reason == "Mission") ModNotify.ShowToast("Story missions cannot be declined.");
			return;
		}
		if (packet.Action != OrderActionType.Accept) return;
		if (!packet.Approved)
		{
			Log.Info($"[Jobs] Accept of order {packet.JobId} refused: {packet.Reason}.");
			if (packet.Reason == "Unknown") ModNotify.ShowToast("This order is no longer available.");
			else UIManager.Get()?.ShowInfoWindow(packet.Reason == "AlreadyTaken" ? "Another player took this order." : "Another order is being taken right now. Try again in a moment.");
			return;
		}
		var job = FindOpen(packet.JobId);
		if (job == null)
		{
			Client.Instance.Send(new OrderActionPacket { JobId = packet.JobId, Action = OrderActionType.AbortTake });
			return;
		}
		PendingTake = packet.JobId;
		var window = UnityEngine.Object.FindObjectOfType<CMS.UI.Windows.OrdersWindow>();
		AcceptBypass = true;
		try
		{
			if (window != null)
			{
				window.currentJob = job;
				window.AcceptOrderAction();
			}
			else if (job.IsMission) Generator.StartCoroutine(Generator.TakeMission(job.id, true));
			else Generator.StartCoroutine(Generator.TakeJob(job.id, true));
		}
		finally
		{
			AcceptBypass = false;
		}
		MelonCoroutines.Start(WatchTake(packet.JobId));
	}

	// The take is finished when the job shows up in selectedJobs with its car loader (PrepareJob for jobs, the
	// TakeMission coroutine for missions); polling covers both without relying on either hook.
	private static IEnumerator WatchTake(int jobId)
	{
		float deadline = Time.realtimeSinceStartup + TakeTimeoutSeconds;
		while (Time.realtimeSinceStartup < deadline && PendingTake == jobId)
		{
			var selected = Generator?.selectedJobs;
			for (int i = 0; selected != null && i < selected.Count; i++)
			{
				var job = selected[i];
				if (job.id != jobId || job.carLoaderID < 0) continue;
				var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(job.carLoaderID);
				if (carLoader == null || !carLoader.IsCarLoaded()) continue;
				PendingTake = -1;
				var missions = job.IsMission ? Missions() : null;
				Log.Info($"[Jobs] Job {jobId} started on loader {job.carLoaderID}.");
				var started = new JobStartedPacket { JobId = jobId, CarLoaderId = job.carLoaderID, Job = ModJobConverter.ToMod(job), Missions = missions };
				OnJobStarted(started);
				Client.Instance.Send(started);
				yield return new WaitForSeconds(0.5f);
				CarPartsSync.UploadBaseline(job.carLoaderID);
				yield break;
			}
			yield return new WaitForSeconds(0.25f);
		}
		if (PendingTake != jobId) yield break;
		PendingTake = -1;
		Log.Warn($"[Jobs] Take of order {jobId} did not finish in {TakeTimeoutSeconds} s; giving it back.");
		Client.Instance.Send(new OrderActionPacket { JobId = jobId, Action = OrderActionType.AbortTake });
	}

	// Apply

	public static IDisposable Guard() => new ApplyScope();

	public static void ApplyFull()
	{
		var generator = Generator;
		if (mirror == null || generator == null) return;
		using (Guard())
		{
			var jobs = generator.jobs;
			var selected = generator.selectedJobs;
			for (int i = 0; jobs != null && i < jobs.Count; i++) jobs[i].StopTimer();
			jobs?.Clear();
			var keepSelected = new HashSet<int>(mirror.ActiveJobs.Select(a => a.Job.id));
			for (int i = selected.Count - 1; i >= 0; i--)
				if (!keepSelected.Contains(selected[i].id) && selected[i].id != PendingTake) selected.RemoveAt(i);

			foreach (var order in mirror.Orders)
			{
				var job = ModJobConverter.ToGame(order.Job);
				job.timeToEnd = order.RemainingSeconds;
				jobs.Add(job);
				if (!job.IsMission) job.StartTimer();
			}
			foreach (var active in mirror.ActiveJobs)
			{
				bool present = false;
				for (int i = 0; i < selected.Count; i++)
					if (selected[i].id == active.Job.id) present = true;
				if (present) continue;
				var job = ModJobConverter.ToGame(active.Job);
				job.carLoaderID = active.CarLoaderId;
				selected.Add(job);
				MarkCustomerCar(active.CarLoaderId, job.id);
			}
			GlobalData.Jobs = mirror.Orders.Count;
			GlobalData.MissionsFinished = mirror.Missions.MissionsFinished;
			GlobalData.CurrentMissionDone = mirror.Missions.CurrentMissionDone;
			GlobalData.IsStoryMissionInProgress = mirror.Missions.IsStoryMissionInProgress;
			Achievements.SharedAchievements.AfterMissions();
			int maxId = mirror.Orders.Select(o => o.Job.id).Concat(mirror.ActiveJobs.Select(a => a.Job.id)).DefaultIfEmpty(0).Max();
			if (generator.LastUId < maxId) generator.LastUId = maxId;
			UIManager.Get()?.UpdateJobs(jobs, null);
		}
		OfferMission();
	}

	private static void OfferMission()
	{
		var generator = Generator;
		if (!IsGenerator || MissionPending || missionTakenElsewhere >= 0 || mirror == null || generator == null || !CanApply || !Client.Instance.IsConnectionValid) return;
		if (mirror.Orders.Any(o => o.Job.IsMission) || mirror.ActiveJobs.Any(a => a.Job.IsMission)) return;
		int missionId = GlobalData.GetMissionID();
		if (missionId < 0 || GlobalData.CurrentMissionDone) return;
		var jobs = generator.jobs;
		var selected = generator.selectedJobs;
		for (int i = 0; jobs != null && i < jobs.Count; i++)
			if (jobs[i].IsMission) return;
		for (int i = 0; selected != null && i < selected.Count; i++)
			if (selected[i].IsMission) return;
		Log.Info($"[Jobs] No story mission is open; generating mission {missionId}.");
		generator.GenerateMission(missionId, false);
	}

	public static void MarkCustomerCar(int loader, int jobId)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || !carLoader.IsCarLoaded()) return;
		carLoader.customerCar = true;
		carLoader.orderConnection = jobId;
	}

	public static void OnCarLoaded(int loader)
	{
		var active = mirror?.ActiveJobs.FirstOrDefault(a => a.CarLoaderId == loader);
		if (active != null) MarkCustomerCar(loader, active.Job.id);
	}

	private static void DeleteCar(int loader)
	{
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(loader);
		if (carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad)) return;
		CarSpawnHooks.Suppress(loader);
		try { carLoader.DeleteCar(true); }
		finally { CarSpawnHooks.Release(loader); }
		CarPartsSync.OnCarDeleted(loader);
	}

	public static int PrepSeedOf(int jobId) => mirror?.Orders.FirstOrDefault(o => o.Job.id == jobId)?.Job.PrepSeed ?? 0;

	private static Job FindOpen(int id)
	{
		var jobs = Generator?.jobs;
		for (int i = 0; jobs != null && i < jobs.Count; i++)
			if (jobs[i].id == id) return jobs[i];
		return null;
	}

	public static ModMissionState Missions() => new ModMissionState
	{
		MissionsFinished = GlobalData.MissionsFinished, CurrentMissionDone = GlobalData.CurrentMissionDone, IsStoryMissionInProgress = GlobalData.IsStoryMissionInProgress
	};

	public static List<object> Describe() => mirror == null ? null :
		mirror.Orders.Select(o => (object)new { o.Job.id, o.Job.carFile, remaining = Mathf.Round(o.RemainingSeconds), o.Job.IsMission }).ToList();

	private sealed class ApplyScope : IDisposable
	{
		public ApplyScope() => applyDepth++;
		public void Dispose() => applyDepth--;
	}
}
