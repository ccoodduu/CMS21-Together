using System.Collections.Generic;

namespace TogetherTestHarness.Features;

// Batch runs (Run-Session.ps1 -Scenarios) reuse the game between scenarios; this undoes every per-session harness toggle.
public static class HarnessResetCommands
{
    [HarnessCommand("harness-reset")]
    private static object HarnessReset(string args)
    {
        var changed = new List<string>();
        NetHoldCommands.Reset(changed);
        ToolsCommands.Reset(changed);
        GuardCommands.Reset(changed);
        GuardTraceCommands.Reset(changed);
        PlacementTraceCommands.Reset(changed);
        EconomyCommands.Reset(changed);
        JobsCommands.Reset(changed);
        JobsTrace.Reset(changed);
        JobsStatsTrace.Reset(changed);
        JobCarCommands.Reset(changed);
        SeatEngineCommands.Reset(changed);
        TestDriveCommands.Reset(changed);
        CarCommands.Reset(changed);
        CarDetailsCommands.Reset(changed);
        DigestCommands.Reset(changed);
        CompatCommands.Reset(changed);
        JoinCommands.Reset(changed);
        PresenceCommands.Reset(changed);
        SceneCommands.Reset(changed);
        VisualCommands.Reset(changed);
        DriveCommands.Reset(changed);
        LockTraceCommands.Reset(changed);
        LockCommands.Reset(changed);
        LockTryCommands.Reset(changed);
        LockSelect2Commands.Reset(changed);
        return new Dictionary<string, object> { ["reset"] = changed };
    }
}
