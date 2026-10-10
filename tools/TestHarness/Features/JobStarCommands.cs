using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Logic.Car.Parts;

namespace TogetherTestHarness.Features;

// The order tab's star: JobHelper.CheckJobPartAndGetItems builds the tab's rows, and a row's MarkAction is what the
// star button calls; it sets PartScript.markImportantPart, which PartScript.Update turns into the part's highlight.
public static class JobStarCommands
{
    private static OrderGenerator Generator => Singleton<GameManager>.Instance.OrderGenerator;

    [HarnessCommand("job-star")]
    private static object JobStar(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) throw new ArgumentException("usage: job-star <jobId> [off]");
        bool mark = !(parts.Length > 1 && parts[1] == "off");
        var (job, carLoader, loader) = ActiveJob(int.Parse(parts[0]));
        var rows = new List<object>();
        for (int i = 0; job.jobTasks != null && i < job.jobTasks.Length; i++)
        {
            var task = job.jobTasks[i];
            Il2CppSystem.Collections.Generic.List<CMS.UI.Logic.CarInfo.CarInfoPart> items;
            try { items = JobHelper.CheckJobPartAndGetItems(carLoader, ref job, ref task); }
            catch (Exception e)
            {
                rows.Add(new { task = task.type, error = e.GetType().Name });
                continue;
            }
            for (int k = 0; items != null && k < items.Count; k++)
            {
                var item = items[k];
                if (item.MarkAction == null) continue;
                item.MarkAction.Invoke(mark);
                rows.Add(new { task = task.type, item.Name });
            }
        }
        return new { job.id, loader, mark, rows, starred = State(loader, carLoader, job) };
    }

    [HarnessCommand("job-star-state")]
    private static object JobStarState(string args)
    {
        var (job, carLoader, loader) = ActiveJob(int.Parse((args ?? "").Trim()));
        return new { job.id, loader, job.globalCondition, starred = State(loader, carLoader, job) };
    }

    private static List<Dictionary<string, object>> State(int loader, CarLoader carLoader, Job job)
    {
        var registry = PartRegistry.Build(carLoader);
        var result = new List<Dictionary<string, object>>();
        foreach (var key in registry.SubKeys)
        {
            var script = registry.Sub(key);
            if (script == null || !script.markImportantPart) continue;
            var ho = script.ho;
            result.Add(new Dictionary<string, object>
            {
                ["key"] = key,
                ["id"] = script.id,
                ["mark"] = script.markImportantPart,
                ["highlight"] = ho != null && ho.IsEnabled,
                ["updating"] = script.enabled && script.canUpdate,
                ["condition"] = script.Condition,
                ["unmounted"] = script.IsUnmounted,
                ["repaired"] = script.IsRepaired(job.globalCondition),
                ["blocked"] = script.IsBlocked(),
                ["members"] = script.GetUnmountWith().Count,
            });
        }
        return result;
    }

    private static (Job Job, CarLoader Loader, int Index) ActiveJob(int id)
    {
        var places = CarLoaderPlaces.Get();
        for (int i = 0; i < places.GetCarLoadersCount(); i++)
        {
            var job = Generator.GetJobForCarLoader(i);
            if (job != null && job.id == id) return (job, places.GetCarLoaderByIndex(i), i);
        }
        throw new ArgumentException($"no active job {id}");
    }
}
