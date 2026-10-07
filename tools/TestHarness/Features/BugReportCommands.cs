using System.Collections.Generic;
using System.Linq;
using CMS21Together.Diagnostics;

namespace TogetherTestHarness.Features;

public static class BugReportCommands
{
    [HarnessCommand("bug-report")]
    private static object Report(string args)
    {
        if ((args ?? "").Trim() == "list") return BugReport.Recent.Select(Row).ToList();
        var record = BugReport.Request();
        return record.Refusal ?? (object)Row(record);
    }

    private static Dictionary<string, object> Row(BugReport.Record record) => new Dictionary<string, object>
    {
        ["id"] = record.Id,
        ["path"] = record.Path,
        ["origin"] = record.Origin,
        ["connected"] = record.Connected,
        ["done"] = record.Done,
        ["written"] = record.Written,
        ["error"] = record.Error,
        ["bytes"] = record.Bytes,
        ["captureMs"] = record.CaptureMs,
        ["writeMs"] = record.WriteMs,
        ["longestFrameMs"] = record.LongestFrameMs,
        ["frames"] = record.Frames,
        ["serverFile"] = record.ServerFile,
        ["serverError"] = record.ServerError,
    };
}
