using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Economy;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Jobs;

// sync-orders-and-jobs D8/D15: the native EndJob runs unchanged; this captures what it pays and sends one
// JobEndRequest at the commit point, the CancelJob(job.id) inside the end coroutine right after the payout.
public static class JobEndContext
{
	private const float TimeoutSeconds = 10f;
	private const float StepGapSeconds = 1f;

	private static int jobId = -1;
	private static int loader = -1;
	private static bool isMission;
	private static int payout;
	private static int xp;
	private static int moneyBefore;
	private static float startedAt;
	private static float lastStepAt = -1f;
	private static readonly EconomyScopeEntry scope = new EconomyScopeEntry
	{
		Name = "JobPayout", Mode = EconomyMode.Covered, Claims = EconomyKind.Money | EconomyKind.Exp,
		CaptureMoney = amount => CaptureMoney(amount), CaptureExp = amount => CaptureExp(amount),
	};

	public static bool IsActive
	{
		get
		{
			if (jobId >= 0 && lastStepAt >= 0f && Time.realtimeSinceStartup - lastStepAt > StepGapSeconds) Drop("its end coroutine stopped before the payout");
			return jobId >= 0 && Time.realtimeSinceStartup - startedAt < TimeoutSeconds;
		}
	}

	public static EconomyScopeEntry Scope => IsActive ? scope : null;

	public static void Begin(Job job, int carLoaderId)
	{
		jobId = job.id;
		loader = carLoaderId;
		isMission = job.IsMission;
		payout = 0;
		xp = 0;
		moneyBefore = GlobalData.PlayerMoney;
		startedAt = Time.realtimeSinceStartup;
		lastStepAt = -1f;
		Log.Info($"[Jobs] Ending job {jobId} on loader {loader}.");
	}

	public static bool CaptureMoney(int amount)
	{
		if (!IsActive) return false;
		payout += amount;
		return true;
	}

	public static bool CaptureExp(int amount)
	{
		if (!IsActive) return false;
		xp += amount;
		return true;
	}

	public static bool IsCommit(int cancelledId) => IsActive && cancelledId == jobId;

	public static bool CoroutineStarted => lastStepAt >= 0f;

	public static void OnCoroutineStep()
	{
		if (jobId >= 0) lastStepAt = Time.realtimeSinceStartup;
	}

	public static void Drop(string why)
	{
		if (jobId < 0) return;
		Log.Info($"[Jobs] Job {jobId} not ended: {why}.");
		if (xp > 0) EconomyRequests.SendWork(xp);
		jobId = -1;
	}

	public static void Commit(Job job)
	{
		int paid = payout != 0 ? payout : GlobalData.PlayerMoney - moneyBefore;
		Log.Info($"[Jobs] Job {jobId} ended: payout {paid}, xp {xp}, completed {job?.IsCompleted}.");
		Client.Instance.Send(new JobEndRequestPacket
		{
			JobId = jobId, CarLoaderId = loader, Payout = paid, Xp = xp, IsCompleted = job?.IsCompleted ?? false,
			IsMission = isMission, Missions = isMission ? JobsSync.Missions() : null,
		});
		jobId = -1;
	}
}
