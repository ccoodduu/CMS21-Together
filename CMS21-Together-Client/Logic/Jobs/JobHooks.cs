using System.Collections.Generic;
using CMS.UI.Windows;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;
using CMS21Together.Logic.Car.Away;

namespace CMS21Together.Logic.Jobs;

[HarmonyPatch]
public static class JobHooks
{
	private static bool Connected => Client.Instance != null && Client.Instance.IsConnectionValid;

	[HarmonyPatch(typeof(OrderGenerator), "Update")]
	[HarmonyPrefix]
	private static bool BeforeUpdate() => !Connected || JobsSync.IsGenerator;

	[HarmonyPatch(typeof(OrderGenerator), "GenerateNewJob")]
	[HarmonyPrefix]
	private static bool BeforeGenerate(OrderGenerator __instance, out int __state)
	{
		__state = __instance.jobs?.Count ?? 0;
		return !Connected || JobsSync.ServingRequest;
	}

	[HarmonyPatch(typeof(OrderGenerator), "GenerateNewJob")]
	[HarmonyPostfix]
	private static void AfterGenerate(OrderGenerator __instance, int __state) => SendNew(__instance, __state);

	[HarmonyPatch(typeof(OrderGenerator), nameof(OrderGenerator.GenerateMission))]
	[HarmonyPrefix]
	private static bool BeforeMission(OrderGenerator __instance, bool forTutorial, out int __state)
	{
		__state = __instance.jobs?.Count ?? 0;
		return !Connected || !forTutorial && JobsSync.IsGenerator && !JobsSync.MissionPending;
	}

	[HarmonyPatch(typeof(OrderGenerator), nameof(OrderGenerator.GenerateMission))]
	[HarmonyPostfix]
	private static void AfterMission(OrderGenerator __instance, int __state, bool __runOriginal)
	{
		if (__runOriginal) SendNew(__instance, __state);
	}

	private static void SendNew(OrderGenerator generator, int countBefore)
	{
		if (!Connected || JobsSync.IsApplying) return;
		var jobs = generator.jobs;
		if (jobs == null || jobs.Count <= countBefore) return;
		var fresh = new List<Job>();
		for (int i = countBefore; i < jobs.Count; i++) fresh.Add(jobs[i]);
		using (JobsSync.Guard())
		{
			foreach (var job in fresh)
			{
				Log.Info($"[Jobs] Generated order {job.carFile}{(job.IsMission ? $" (mission {GlobalData.MissionsFinished})" : "")}.");
				if (job.IsMission) JobsSync.MissionAsked();
				Client.Instance.Send(new OrderGeneratedPacket { Job = ModJobConverter.ToMod(job), MaxOpenOrders = GlobalData.GetMaxOrdersAmount(), RequestId = job.IsMission ? 0 : JobsSync.RequestId });
				if (!job.IsMission) JobsSync.OrderSent();
				job.StopTimer();
				jobs.Remove(job);
			}
			GlobalData.Jobs = jobs.Count;
		}
	}

	[HarmonyPatch(typeof(OrdersWindow), "AcceptOrderAction")]
	[HarmonyPrefix]
	private static bool BeforeAccept(OrdersWindow __instance)
	{
		if (!Connected || JobsSync.AcceptBypass || __instance.currentJob == null) return true;
		Log.Info($"[Jobs] Asking to take order {__instance.currentJob.id}.");
		Client.Instance.Send(new OrderActionPacket { JobId = __instance.currentJob.id, Action = OrderActionType.Accept });
		return false;
	}

	[HarmonyPatch(typeof(OrdersWindow), "DeclineOrderAction")]
	[HarmonyPrefix]
	private static bool BeforeDecline(OrdersWindow __instance)
	{
		if (!Connected || __instance.currentJob == null || __instance.currentJob.IsMission) return true;
		Client.Instance.Send(new OrderActionPacket { JobId = __instance.currentJob.id, Action = OrderActionType.Decline });
		return false;
	}

	[HarmonyPatch(typeof(OrderGenerator), nameof(OrderGenerator.CancelJob))]
	[HarmonyPrefix]
	private static bool BeforeCancel(OrderGenerator __instance, int id)
	{
		if (!Connected || JobsSync.IsApplying || id == JobsSync.PendingTake) return true;
		if (JobEndContext.IsCommit(id))
		{
			Job ended = null;
			var selected = __instance.selectedJobs;
			for (int i = 0; selected != null && i < selected.Count; i++)
				if (selected[i].id == id) ended = selected[i];
			JobEndContext.Commit(ended);
			return true;
		}
		return JobEndContext.IsActive;
	}

	[HarmonyPatch(typeof(OrderGenerator), nameof(OrderGenerator.Load))]
	[HarmonyPostfix]
	private static void AfterLoad() => JobsSync.OnGeneratorLoaded();

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.EndJob))]
	[HarmonyPrefix]
	private static bool BeforeEndJob(Job job, CarLoader carLoader)
	{
		if (!Connected || job == null || carLoader == null) return true;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		if (loader >= 0 && CarAwaySync.BlockIfLocked(loader, "job end")) return false;
		if (loader >= 0 && Car.Locks.LockGate.Active && Car.Locks.CarLockMirror.OtherOwnerOn(loader) is int holder && holder >= 0)
		{
			Car.Locks.LockMessages.Refuse(Car.Locks.LockMessages.Busy(holder));
			return false;
		}
		JobHelper.CheckJob(carLoader, ref job);
		JobEndContext.Begin(job, loader);
		return true;
	}

	[HarmonyPatch(typeof(GameScript), nameof(GameScript.EndJob))]
	[HarmonyPostfix]
	private static void AfterEndJob()
	{
		if (JobEndContext.IsActive && !JobEndContext.CoroutineStarted) JobEndContext.Drop("the game refused it before its end coroutine");
	}

	[HarmonyPatch(typeof(GameScript._EndJobCoroutine_d__139), nameof(GameScript._EndJobCoroutine_d__139.MoveNext))]
	[HarmonyPrefix]
	private static void BeforeEndJobStep() => JobEndContext.OnCoroutineStep();

	[HarmonyPatch(typeof(GameScript._EndJobCoroutine_d__139), nameof(GameScript._EndJobCoroutine_d__139.MoveNext))]
	[HarmonyPostfix]
	private static void AfterEndJobStep(bool __result)
	{
		if (!__result) JobEndContext.Drop("the game's checks refused it");
	}

	[HarmonyPatch(typeof(CMS.MainMenu.Windows.TutorialsWindow), "RunTutorialAction")]
	[HarmonyPrefix]
	private static bool BeforeTutorial()
	{
		if (!Connected) return true;
		UIManager.Get()?.ShowInfoWindow("Tutorials are not available while playing together.");
		return false;
	}
}
