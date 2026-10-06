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

	private static JobsState mirror;
	private static int applyDepth;
	private static bool generatorLoaded;

	public static bool IsGenerator { get; private set; }
	public static bool IsApplying => applyDepth > 0;
	public static int PendingTake { get; private set; } = -1;
	public static bool AcceptBypass { get; private set; }

	private static OrderGenerator Generator => Singleton<GameManager>.Instance?.OrderGenerator;

	private static bool CanApply => ClientScene.IsGarageReady && generatorLoaded && Generator != null;

	public static void Reset()
	{
		mirror = null;
		generatorLoaded = false;
		PendingTake = -1;
	}

	public static void OnGeneratorLoaded()
	{
		generatorLoaded = true;
		if (mirror != null && Client.Instance.IsConnectionValid) ApplyFull();
	}

	// Packets

	public static void OnState(JobsStatePacket packet, int snapshotId)
	{
		mirror = packet.State ?? new JobsState();
		IsGenerator = packet.IsGenerator;
		if (CanApply) ApplyFull();
		SyncTracker.Applied(SyncOrder.JobsKey, snapshotId);
	}

	public static void OnRole(OrderGeneratorRolePacket packet)
	{
		IsGenerator = packet.IsGenerator;
		Log.Info($"[Jobs] Order generator: {(IsGenerator ? "this client" : "another client")}.");
	}

	public static void OnOrderAdded(OrderAddedPacket packet)
	{
		if (mirror == null) return;
		mirror.ActiveJobs.RemoveAll(a => a.Job.id == packet.Job.id);
		mirror.Orders.RemoveAll(o => o.Job.id == packet.Job.id);
		mirror.Orders.Add(new OrderEntry { Job = packet.Job, RemainingSeconds = packet.RemainingSeconds });
		if (CanApply) ApplyFull();
	}

	public static void OnJobStarted(JobStartedPacket packet)
	{
		if (mirror == null) return;
		mirror.Orders.RemoveAll(o => o.Job.id == packet.JobId);
		mirror.ActiveJobs.RemoveAll(a => a.Job.id == packet.JobId);
		mirror.ActiveJobs.Add(new ActiveJobEntry { Job = packet.Job, CarLoaderId = packet.CarLoaderId });
		if (packet.Missions != null) mirror.Missions = packet.Missions;
		if (CanApply) ApplyFull();
	}

	public static void OnJobRemoved(JobRemovedPacket packet)
	{
		if (mirror == null) return;
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
		if (packet.Action != OrderActionType.Accept) return;
		if (!packet.Approved)
		{
			Log.Info($"[Jobs] Accept of order {packet.JobId} refused: {packet.Reason}.");
			UIManager.Get()?.ShowInfoWindow(packet.Reason == "AlreadyTaken" ? "Another player took this order." : "Another order is being taken right now. Try again in a moment.");
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
				job.StartTimer();
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
			int maxId = mirror.Orders.Select(o => o.Job.id).Concat(mirror.ActiveJobs.Select(a => a.Job.id)).DefaultIfEmpty(0).Max();
			if (generator.LastUId < maxId) generator.LastUId = maxId;
			UIManager.Get()?.UpdateJobs(jobs, null);
		}
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
		try { carLoader.DeleteCar(); }
		finally { CarSpawnHooks.Release(loader); }
		CarPartsSync.OnCarDeleted(loader);
	}

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
