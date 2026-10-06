using System.Collections.Generic;
using CMS21Together.Persistence;

namespace TogetherTestHarness.Features;

public static class ProfileCommands
{
    [HarnessCommand("profile-pref")]
    private static object ProfilePref(string args)
    {
        var manager = Singleton<GameManager>.Instance;
        return new Dictionary<string, object>
        {
            ["pref"] = manager.RDGPlayerPrefs.GetInt("selectedProfile", -1),
            ["field"] = manager.ProfileManager.selectedProfile,
            ["profileSlots"] = manager.GameDataManager.ProfileData?.Length ?? 0,
            ["guardActive"] = SessionGuard.Active,
        };
    }
}
