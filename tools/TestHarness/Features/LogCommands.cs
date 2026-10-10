using System;
using System.Collections.Generic;
using MelonLoader;

namespace TogetherTestHarness.Features;

public static class LogCommands
{
    [HarnessCommand("log-burst")]
    private static object LogBurst(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || (parts[1] != "warn" && parts[1] != "error"))
            throw new ArgumentException("usage: log-burst <count> <warn|error> <tag>");
        int count = int.Parse(parts[0]);
        for (int i = 1; i <= count; i++)
        {
            string line = $"[LogBurst] {parts[2]} {i}/{count}";
            if (parts[1] == "warn") MelonLogger.Warning(line);
            else MelonLogger.Error(line);
        }
        return new Dictionary<string, object> { ["count"] = count, ["kind"] = parts[1], ["tag"] = parts[2] };
    }
}
