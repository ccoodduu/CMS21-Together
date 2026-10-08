using System;
using System.Collections.Generic;
using System.Linq;
using CMS.UI;
using CMS.UI.Logic;
using CMS.UI.Windows;
using HarmonyLib;
using UnityEngine;

namespace TogetherTestHarness.Features;

// sync-orders-and-jobs task 1.2: drive the native job flow from the harness (native calls only).
[HarmonyPatch]
public static class JobsCommands
{
    private static bool autogen = true;
    private static float nextTtl = -1f;
    private static float stickyTtl = -1f;
    private static string nextBonus;

    private static OrderGenerator Generator => Singleton<GameManager>.Instance.OrderGenerator;

    [HarmonyPatch(typeof(OrderGenerator), "Update")]
    [HarmonyPrefix]
    private static bool BeforeUpdate() => autogen;

    [HarmonyPatch(typeof(CMS21Together.Logic.Jobs.JobsSync), nameof(CMS21Together.Logic.Jobs.JobsSync.OnOrderRequest))]
    [HarmonyPrefix]
    private static bool BeforeOrderRequest(CMS21_Together_Core.Network.Packets.OrderRequestPacket packet)
    {
        if (autogen) return true;
        CMS21Together.Logic.Jobs.JobsSync.AnswerRequest(packet.RequestId, CMS21_Together_Core.Network.Packets.OrderRequestReason.HarnessOff);
        return false;
    }

    [HarmonyPatch(typeof(OrderGenerator), "GenerateNewJob")]
    [HarmonyPrefix]
    private static void BeforeGenerateNewJob(out int __state) => __state = Generator.Jobs?.Count ?? 0;

    [HarmonyPatch(typeof(OrderGenerator), "GenerateNewJob")]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void AfterGenerateNewJob(int __state)
    {
        float ttl = nextTtl > 0f ? nextTtl : stickyTtl;
        string bonus = nextBonus;
        nextTtl = -1f;
        nextBonus = null;
        var jobs = Generator.Jobs;
        if (jobs == null || jobs.Count <= __state) return;
        var job = jobs[jobs.Count - 1];
        if (ttl > 0f) job.timeToEnd = ttl;
        if (bonus == "money") job.BonusToMoney = true;
        if (bonus == "exp") job.BonusToExp = true;
    }

    [HarnessCommand("orders-ttl")]
    private static object OrdersTtl(string args)
    {
        stickyTtl = float.TryParse((args ?? "").Trim(), out float ttl) ? ttl : -1f;
        return stickyTtl > 0f ? $"every generated order lasts {stickyTtl} s" : "off";
    }

    [HarnessCommand("orders-timer")]
    private static object OrdersTimer(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var generator = Generator;
        if (parts.Length == 2)
        {
            generator.orderTimer = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            generator.nextOrderTime = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        return new { generator.orderTimer, generator.nextOrderTime };
    }

    [HarnessCommand("orders-autogen")]
    private static object OrdersAutogen(string args)
    {
        autogen = (args ?? "").Trim() != "off";
        return autogen ? "on" : "off";
    }

    internal static void Reset(List<string> changed)
    {
        if (!autogen) changed.Add("orders-autogen off");
        if (stickyTtl > 0f) changed.Add("orders-ttl");
        nextBonus = null;
        autogen = true;
        nextTtl = -1f;
        stickyTtl = -1f;
    }

    [HarnessCommand("orders-generate")]
    private static object OrdersGenerate(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        nextTtl = parts.Length > 0 && float.TryParse(parts[0], out float ttl) ? ttl : -1f;
        nextBonus = parts.Length > 1 && (parts[1] == "money" || parts[1] == "exp") ? parts[1] : null;
        int before = Generator.Jobs?.Count ?? 0;
        bool connected = CMS21Together.Network.Client.Instance != null && CMS21Together.Network.Client.Instance.IsConnectionValid;
        if (connected) return new { before, sent = CMS21Together.Logic.Jobs.JobsSync.GenerateOrder(0) };
        Generator.GenerateNewJob();
        return new { before, after = Generator.Jobs?.Count ?? 0 };
    }

    [HarnessCommand("orders-mission")]
    private static object OrdersMission(string args)
    {
        Generator.GenerateMission(int.Parse((args ?? "").Trim()), false);
        return OrdersList(null);
    }

    [HarnessCommand("orders-list")]
    private static object OrdersList(string args)
    {
        var result = new List<object>();
        var jobs = Generator.Jobs;
        for (int i = 0; jobs != null && i < jobs.Count; i++)
        {
            var job = jobs[i];
            result.Add(new { job.id, car = job.carFile, job.carLoaderID, job.forXP, job.IsMission, timeToEnd = Mathf.Round(job.timeToEnd), tasks = job.jobTasks?.Length ?? 0, job.BonusToMoney, job.BonusToExp });
        }
        return new Dictionary<string, object> { ["jobs"] = result, ["globalJobs"] = GlobalData.Jobs, ["max"] = GlobalData.GetMaxOrdersAmount() };
    }

    [HarnessCommand("order-slots")]
    private static object OrderSlots(string args)
    {
        var slots = new List<object>();
        for (int i = 0; i < 8; i++)
        {
            bool unlocked = GlobalData.IsOrderSlotUnlocked(i, out OrderSlotLockReason reason, out int levelCap);
            slots.Add(new { index = i, unlocked, reason = reason.ToString(), levelCap });
        }
        return slots;
    }

    [HarnessCommand("orders-accept")]
    private static object OrdersAccept(string args) => OrderAction(args, accept: true);

    [HarnessCommand("orders-decline")]
    private static object OrdersDecline(string args) => OrderAction(args, accept: false);

    private static object OrderAction(string args, bool accept)
    {
        var job = FindJob(int.Parse((args ?? "").Trim()));
        WindowManager.Instance.Show(WindowID.Orders, false);
        var windows = Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<OrdersWindow>());
        var window = windows != null && windows.Length > 0 ? windows[0].Cast<OrdersWindow>() : null;
        if (window == null) throw new InvalidOperationException("the orders window did not open");
        window.currentJob = job;
        if (accept) window.AcceptOrderAction();
        else window.DeclineOrderAction();
        return new { job.id, car = job.carFile, accepted = accept };
    }

    [HarnessCommand("orders-decline-packet")]
    private static object OrdersDeclinePacket(string args)
    {
        int id = int.Parse((args ?? "").Trim());
        CMS21Together.Network.Client.Instance.Send(new CMS21_Together_Core.Network.Packets.OrderActionPacket { JobId = id, Action = CMS21_Together_Core.Network.Packets.OrderActionType.Decline });
        return "sent";
    }

    [HarnessCommand("missions-state")]
    private static object MissionsState(string args) => new
    {
        GlobalData.MissionsFinished, GlobalData.CurrentMissionDone, GlobalData.IsStoryMissionInProgress, missionId = GlobalData.GetMissionID(),
        GlobalData.MissionsAmount, level = GlobalData.PlayerLevel, maxOrders = GlobalData.GetMaxOrdersAmount(),
    };

    [HarnessCommand("orders-reload")]
    private static object OrdersReload(string args)
    {
        Generator.Load();
        return OrdersList(null);
    }

    [HarnessCommand("job-examine")]
    private static object JobExamine(string args)
    {
        var (job, carLoader) = ActiveJob(args);
        int examined = 0;
        foreach (var part in carLoader.GetComponentsInChildren<PartScript>(true))
        {
            if (part.IsExamined) continue;
            part.Examine(true);
            examined++;
        }
        return new { job.id, examined };
    }

    [HarnessCommand("job-repair")]
    private static object JobRepair(string args)
    {
        var (job, carLoader) = ActiveJob(args);
        var wanted = new HashSet<string>();
        for (int i = 0; job.jobTasks != null && i < job.jobTasks.Length; i++)
            for (int p = 0; job.jobTasks[i].Parts != null && p < job.jobTasks[i].Parts.Count; p++)
                wanted.Add(job.jobTasks[i].Parts[p].ID);
        int repaired = 0;
        foreach (var part in carLoader.GetComponentsInChildren<PartScript>(true))
        {
            if (!wanted.Contains(part.id) && !wanted.Contains(part.tunedID) && !wanted.Contains(part.gameObject.name)) continue;
            part.SetCondition(1f, true);
            repaired++;
        }
        for (int i = 0; carLoader.carParts != null && i < carLoader.carParts.Count; i++)
        {
            var body = carLoader.carParts[i];
            if (!wanted.Contains(body.name)) continue;
            body.Condition = 1f;
            repaired++;
        }
        var sample = new List<string>();
        foreach (var part in carLoader.GetComponentsInChildren<PartScript>(true)) if (sample.Count < 8) sample.Add($"{part.id}/{part.gameObject.name}");
        return new { job.id, wanted = wanted.Count, repaired, wantedIds = wanted.Take(8).ToList(), carParts = sample };
    }

    [HarnessCommand("job-end-direct")]
    private static object JobEndDirect(string args)
    {
        var (job, carLoader) = ActiveJob(args);
        job.IsCompleted = true;
        CMS21Together.Logic.Jobs.JobEndContext.Begin(job, CarLoaderPlaces.Get().GetCarLoaderId(carLoader));
        GlobalData.AddPlayerMoney(1234);
        GlobalData.AddPlayerExp(50, false);
        Generator.CancelJob(job.id);
        return new { job.id, payout = 1234, xp = 50 };
    }

    [HarnessCommand("job-spawn-unclaimed")]
    private static object JobSpawnUnclaimed(string args)
    {
        var request = new CMS21_Together_Core.Network.Packets.CarSpawnRequestPacket
        {
            CarLoaderID = int.Parse((args ?? "").Trim()), CarToLoad = "car_boltatlanta", IsJob = true, JobID = 9999, PlaceNo = -1
        };
        CMS21Together.Network.Client.Instance.Send(request);
        return "sent";
    }

    [HarnessCommand("job-check")]
    private static object JobCheck(string args)
    {
        var (job, carLoader) = ActiveJob(args);
        JobHelper.CheckJob(carLoader, ref job);
        var tasks = new List<object>();
        for (int i = 0; job.jobTasks != null && i < job.jobTasks.Length; i++)
        {
            var task = job.jobTasks[i];
            tasks.Add(new { task.type, task.subtype, task.partsCount, task.moneySpent, task.Done });
        }
        return new { job.id, tasks };
    }

    [HarnessCommand("job-finish")]
    private static object JobFinish(string args)
    {
        var (job, carLoader) = ActiveJob(args);
        GameScript.Get().EndJob(job, carLoader);
        return new { job.id, car = job.carFile };
    }

    [HarnessCommand("tutorial-run")]
    private static object TutorialRun(string args)
    {
        var windows = Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<CMS.MainMenu.Windows.TutorialsWindow>());
        if (windows == null || windows.Length == 0) throw new InvalidOperationException("no TutorialsWindow");
        windows[0].Cast<CMS.MainMenu.Windows.TutorialsWindow>().RunTutorialAction();
        return "started";
    }

    private static Job FindJob(int id)
    {
        var jobs = Generator.Jobs;
        for (int i = 0; jobs != null && i < jobs.Count; i++)
            if (jobs[i].id == id) return jobs[i];
        throw new ArgumentException($"no order {id}");
    }

    private static (Job Job, CarLoader Loader) ActiveJob(string args)
    {
        int id = int.Parse((args ?? "").Trim());
        var places = CarLoaderPlaces.Get();
        for (int i = 0; i < places.GetCarLoadersCount(); i++)
        {
            var job = Generator.GetJobForCarLoader(i);
            if (job != null && job.id == id) return (job, places.GetCarLoaderByIndex(i));
        }
        throw new ArgumentException($"no active job {id}");
    }
}
