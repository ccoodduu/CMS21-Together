using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BuildInfo = CMS21_Together_Core.BuildInfo;
using CMS21Together;
using MelonLoader;

namespace TogetherTestHarness.Features;

public static class BuildCommands
{
    [HarnessCommand("build-info")]
    private static object BuildInfoCommand(string args)
    {
        var gameDir = Directory.GetCurrentDirectory();
        var facepunch = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Facepunch.Steamworks.Win64");

        return new Dictionary<string, object>
        {
            ["fullVersion"] = BuildInfo.LoadedFullVersion,
            ["melonVersion"] = typeof(MainMod).Assembly.GetCustomAttribute<MelonInfoAttribute>()?.Version,
            ["files"] = new Dictionary<string, object>
            {
                ["Mods/CMS21-Together.dll"] = Describe(typeof(MainMod).Assembly, Path.Combine(gameDir, "Mods", "CMS21-Together.dll")),
                ["UserLibs/CMS21_Together_Core.dll"] = Describe(typeof(BuildInfo).Assembly, Path.Combine(gameDir, "UserLibs", "CMS21_Together_Core.dll")),
                ["UserLibs/Facepunch.Steamworks.Win64.dll"] = Describe(facepunch, Path.Combine(gameDir, "UserLibs", "Facepunch.Steamworks.Win64.dll")),
            },
            ["steamLibPresent"] = File.Exists(Path.Combine(gameDir, "UserLibs", "steam_api64.dll")),
            ["steamAvailable"] = MainMod.IsSteamAvailable,
        };
    }

    private static Dictionary<string, object> Describe(Assembly loaded, string installedPath)
    {
        var location = loaded?.Location;
        var path = string.IsNullOrEmpty(location) ? installedPath : location;
        return new Dictionary<string, object>
        {
            ["loaded"] = loaded != null,
            ["path"] = path,
            ["sha256"] = File.Exists(path) ? Sha256(path) : null,
        };
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
